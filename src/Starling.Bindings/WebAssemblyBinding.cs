// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotWasm.Encoding;
using DotWasm.Models;
using DotWasm.Runtime;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// Minimal WebAssembly JavaScript API backed by DotWasm. This first slice is
/// enough for self-contained numeric modules and gives Blazor WASM a real
/// <c>WebAssembly</c> object to probe before the next browser-runtime gaps.
/// </summary>
public static class WebAssemblyBinding
{
    private static readonly ConditionalWeakTable<JsObject, WasmFunctionReference> WasmFunctionReferences = new();
    private static readonly ConditionalWeakTable<WasmInstance, WasmInstanceMemoryObjects> InstanceMemoryObjects = new();

    private static readonly FieldInfo MemoryDataField =
        typeof(MemoryInstance).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DotWasm MemoryInstance.data field was not found.");

    public static void Install(JsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var realm = runtime.Realm;
        if (realm.GlobalObject.GetOwnPropertyDescriptor("WebAssembly") is not null) return;

        var moduleProto = new JsObject(realm.ObjectPrototype);
        var instanceProto = new JsObject(realm.ObjectPrototype);
        var memoryProto = new JsObject(realm.ObjectPrototype);
        var tableProto = new JsObject(realm.ObjectPrototype);

        var moduleCtor = new JsNativeFunction(realm, "Module", 1, (_, args) =>
            JsValue.Object(new WasmModuleObject(moduleProto, DecodeModule(realm, Arg(args, 0)))),
            isConstructor: true);
        WireConstructor(moduleCtor, moduleProto, "Module");

        var instanceCtor = new JsNativeFunction(realm, "Instance", 1, (_, args) =>
        {
            var module = RequireModule(realm, Arg(args, 0));
            var importObject = args.Length > 1 ? args[1] : JsValue.Undefined;
            return JsValue.Object(BuildInstanceObject(realm, instanceProto, memoryProto, tableProto, module, importObject));
        }, isConstructor: true);
        WireConstructor(instanceCtor, instanceProto, "Instance");

        var memoryCtor = new JsNativeFunction(realm, "Memory", 1, (_, args) =>
        {
            var descriptor = Arg(args, 0);
            if (!descriptor.IsObject)
                throw new JsThrow(realm.NewTypeError("WebAssembly.Memory requires a descriptor object"));

            var obj = descriptor.AsObject;
            var initial = ToNonNegativeInt(realm, obj.Get("initial"), "initial");
            var maximum = obj.Get("maximum");
            var memory = maximum.IsUndefined
                ? new MemoryInstance(initial)
                : new MemoryInstance(initial)
                {
                    Max = (ulong)ToNonNegativeInt(realm, maximum, "maximum"),
                };
            return JsValue.Object(new WasmMemoryObject(memoryProto, memory));
        }, isConstructor: true);
        WireConstructor(memoryCtor, memoryProto, "Memory");
        EventTargetBinding.DefineAccessor(realm, memoryProto, "buffer", (thisV, _) =>
        {
            var memory = RequireMemory(realm, thisV);
            return JsValue.Object(memory.GetBuffer(realm.ArrayBufferPrototype, MemoryBytes(memory.Memory)));
        });
        EventTargetBinding.DefineMethod(realm, memoryProto, "grow", (thisV, args) =>
        {
            var memoryObject = RequireMemory(realm, thisV);
            var memory = memoryObject.Memory;
            var oldPages = memory.Data.Length / 65536;
            memory.Grow(ToNonNegativeInt(realm, Arg(args, 0), "delta"));
            memoryObject.SyncBuffer(MemoryBytes(memory));
            return JsValue.Number(oldPages);
        }, length: 1);

        var tableCtor = new JsNativeFunction(realm, "Table", 1, (_, args) =>
        {
            var descriptor = Arg(args, 0);
            if (!descriptor.IsObject)
                throw new JsThrow(realm.NewTypeError("WebAssembly.Table requires a descriptor object"));

            var obj = descriptor.AsObject;
            var element = JsValue.ToStringValue(obj.Get("element"));
            if (!StringComparer.Ordinal.Equals(element, "anyfunc") &&
                !StringComparer.Ordinal.Equals(element, "funcref"))
            {
                throw new JsThrow(realm.NewTypeError("WebAssembly.Table descriptor 'element' must be 'funcref'"));
            }

            var maximum = obj.Get("maximum");
            var initial = (ulong)ToNonNegativeInt(
                realm, obj.Get("initial"), "initial", "WebAssembly.Table descriptor");
            var table = maximum.IsUndefined
                ? new TableInstance(initial)
                {
                    ElementType = WasmTypes.FuncRef(true),
                }
                : new TableInstance(initial)
                {
                    ElementType = WasmTypes.FuncRef(true),
                    Max = (ulong)ToNonNegativeInt(
                        realm, maximum, "maximum", "WebAssembly.Table descriptor"),
                };

            return JsValue.Object(new WasmTableObject(tableProto, table));
        }, isConstructor: true);
        WireConstructor(tableCtor, tableProto, "Table");
        EventTargetBinding.DefineAccessor(realm, tableProto, "length", (thisV, _) =>
            JsValue.Number(RequireTable(realm, thisV).Table.References.Length));
        EventTargetBinding.DefineMethod(realm, tableProto, "get", (thisV, args) =>
        {
            var table = RequireTable(realm, thisV);
            var index = ToTableIndex(realm, Arg(args, 0), table.Table.References.Length);
            return FromTableValue(realm, table, table.Table.References[index]);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, tableProto, "set", (thisV, args) =>
        {
            var table = RequireTable(realm, thisV);
            var index = ToTableIndex(realm, Arg(args, 0), table.Table.References.Length);
            table.Table.References[index] = ToTableValue(realm, table, Arg(args, 1), defaultToNull: false);
            return JsValue.Undefined;
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, tableProto, "grow", (thisV, args) =>
        {
            var table = RequireTable(realm, thisV);
            var oldLength = table.Table.References.Length;
            var delta = ToNonNegativeInt(realm, Arg(args, 0), "delta", "WebAssembly.Table grow");
            var initial = ToTableValue(realm, table, Arg(args, 1), defaultToNull: true);
            table.Table.Grow(delta, initial);
            return JsValue.Number(oldLength);
        }, length: 1);

        var wasm = new JsObject(realm.ObjectPrototype);
        wasm.DefineOwnProperty("Module",
            PropertyDescriptor.Data(JsValue.Object(moduleCtor), writable: true, enumerable: false, configurable: true));
        wasm.DefineOwnProperty("Instance",
            PropertyDescriptor.Data(JsValue.Object(instanceCtor), writable: true, enumerable: false, configurable: true));
        wasm.DefineOwnProperty("Memory",
            PropertyDescriptor.Data(JsValue.Object(memoryCtor), writable: true, enumerable: false, configurable: true));
        wasm.DefineOwnProperty("Table",
            PropertyDescriptor.Data(JsValue.Object(tableCtor), writable: true, enumerable: false, configurable: true));

        EventTargetBinding.DefineMethod(realm, wasm, "compile", (_, args) =>
        {
            try
            {
                return FetchBinding.ResolvedPromise(realm,
                    JsValue.Object(new WasmModuleObject(moduleProto, DecodeModule(realm, Arg(args, 0)))));
            }
            catch (JsThrow ex)
            {
                return FetchBinding.RejectedPromise(realm, ex.Value);
            }
            catch (Exception ex)
            {
                return FetchBinding.RejectedPromise(realm, realm.NewTypeError(ex.Message));
            }
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, wasm, "validate", (_, args) =>
        {
            try
            {
                DecodeModule(realm, Arg(args, 0));
                return JsValue.True;
            }
            catch (JsThrow)
            {
                return JsValue.False;
            }
            catch
            {
                return JsValue.False;
            }
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, wasm, "instantiate", (_, args) =>
        {
            try
            {
                var importObject = args.Length > 1 ? args[1] : JsValue.Undefined;
                if (Arg(args, 0).IsObject && Arg(args, 0).AsObject is WasmModuleObject moduleObject)
                {
                    var instance = BuildInstanceObject(
                        realm, instanceProto, memoryProto, tableProto, moduleObject.Module, importObject);
                    return FetchBinding.ResolvedPromise(realm, JsValue.Object(instance));
                }

                var module = DecodeModule(realm, Arg(args, 0));
                var moduleWrapper = new WasmModuleObject(moduleProto, module);
                var instanceWrapper = BuildInstanceObject(realm, instanceProto, memoryProto, tableProto, module, importObject);
                return FetchBinding.ResolvedPromise(realm,
                    JsValue.Object(BuildInstantiateResult(realm, moduleWrapper, instanceWrapper)));
            }
            catch (JsThrow ex)
            {
                return FetchBinding.RejectedPromise(realm, ex.Value);
            }
            catch (Exception ex)
            {
                return FetchBinding.RejectedPromise(realm, realm.NewTypeError(ex.Message));
            }
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, wasm, "compileStreaming", (_, args) =>
            StreamBytes(realm, Arg(args, 0), bytes =>
                JsValue.Object(new WasmModuleObject(moduleProto, DecodeModule(realm, bytes)))), length: 1);

        EventTargetBinding.DefineMethod(realm, wasm, "instantiateStreaming", (_, args) =>
        {
            var importObject = args.Length > 1 ? args[1] : JsValue.Undefined;
            return StreamBytes(realm, Arg(args, 0), bytes =>
            {
                var module = DecodeModule(realm, bytes);
                var moduleWrapper = new WasmModuleObject(moduleProto, module);
                var instanceWrapper = BuildInstanceObject(realm, instanceProto, memoryProto, tableProto, module, importObject);
                return JsValue.Object(BuildInstantiateResult(realm, moduleWrapper, instanceWrapper));
            });
        }, length: 1);

        realm.GlobalObject.DefineOwnProperty("WebAssembly",
            PropertyDescriptor.Data(JsValue.Object(wasm), writable: true, enumerable: false, configurable: true));
    }

    private static JsValue Arg(JsValue[] args, int index)
        => index < args.Length ? args[index] : JsValue.Undefined;

    private static void WireConstructor(JsObject ctor, JsObject proto, string name)
    {
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(name), writable: false, enumerable: false, configurable: true));
    }

    private static WasmModule DecodeModule(JsRealm realm, JsValue value)
    {
        try
        {
            return WasmEncoding.Decode(CoreWebApiBinding.BytesFromBufferSource(realm, value));
        }
        catch (JsThrow)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsThrow(realm.NewTypeError(ex.Message));
        }
    }

    private static WasmModule DecodeModule(JsRealm realm, byte[] bytes)
    {
        try
        {
            return WasmEncoding.Decode(bytes);
        }
        catch (Exception ex)
        {
            throw new JsThrow(realm.NewTypeError(ex.Message));
        }
    }

    private static WasmModule RequireModule(JsRealm realm, JsValue value)
    {
        if (value.IsObject && value.AsObject is WasmModuleObject module)
            return module.Module;
        throw new JsThrow(realm.NewTypeError("Expected WebAssembly.Module"));
    }

    private static WasmMemoryObject RequireMemory(JsRealm realm, JsValue value)
    {
        if (value.IsObject && value.AsObject is WasmMemoryObject memory)
            return memory;
        throw new JsThrow(realm.NewTypeError("Expected WebAssembly.Memory"));
    }

    private static WasmTableObject RequireTable(JsRealm realm, JsValue value)
    {
        if (value.IsObject && value.AsObject is WasmTableObject table)
            return table;
        throw new JsThrow(realm.NewTypeError("Expected WebAssembly.Table"));
    }

    private static WasmInstanceObject BuildInstanceObject(
        JsRealm realm,
        JsObject instanceProto,
        JsObject memoryProto,
        JsObject tableProto,
        WasmModule module,
        JsValue importObject)
    {
        var store = new WasmStore();
        var linker = new WasmLinker(store);
        RegisterImports(realm, linker, module, importObject);
        var instance = linker.Instantiate(module);
        var exports = BuildExports(realm, memoryProto, tableProto, instance);
        return new WasmInstanceObject(instanceProto, instance, exports);
    }

    private static void RegisterImports(
        JsRealm realm,
        WasmLinker linker,
        WasmModule module,
        JsValue importObject)
    {
        foreach (var import in module.Imports)
        {
            var value = ResolveImport(realm, importObject, import);
            switch (import.Kind)
            {
                case ImportExportKind.Function:
                    linker.RegisterFunction(import.Module, import.Name,
                        BuildHostFunction(realm, import, value));
                    break;
                case ImportExportKind.Memory:
                    var memory = RequireImportedMemory(realm, import, value);
                    linker.RegisterMemory(import.Module, import.Name, memory);
                    break;
                default:
                    throw new JsThrow(realm.NewTypeError(
                        $"Unsupported WebAssembly import {import.Module}.{import.Name} ({import.Kind})"));
            }
        }
    }

    private static JsValue ResolveImport(JsRealm realm, JsValue importObject, Import import)
    {
        if (!importObject.IsObject)
            throw MissingImport(realm, import);
        var moduleValue = importObject.AsObject.Get(import.Module);
        if (!moduleValue.IsObject)
            throw MissingImport(realm, import);
        var value = moduleValue.AsObject.Get(import.Name);
        if (value.IsUndefined)
            throw MissingImport(realm, import);
        return value;
    }

    private static JsThrow MissingImport(JsRealm realm, Import import) =>
        new(realm.NewTypeError($"Missing WebAssembly import {import.Module}.{import.Name}"));

    private static HostFunction BuildHostFunction(JsRealm realm, Import import, JsValue value)
    {
        if (!AbstractOperations.IsCallable(value))
            throw new JsThrow(realm.NewTypeError(
                $"WebAssembly import {import.Module}.{import.Name} must be a function"));
        if (import.Type.Value is not FuncType type)
            throw new JsThrow(realm.NewTypeError(
                $"WebAssembly import {import.Module}.{import.Name} has no function type"));

        return new HostFunction
        {
            Type = type,
            Delegate = (args, results) =>
            {
                var jsArgs = new JsValue[args.Length];
                for (var i = 0; i < jsArgs.Length; i++)
                    jsArgs[i] = FromWasmValue(realm, args[i], type.Parameters[i]);

                var result = AbstractOperations.Call(realm.ActiveVm, value, JsValue.Undefined, jsArgs);
                if (results.Length == 1)
                    results[0] = ToWasmValue(realm, result, type.Results[0]);
                else if (results.Length > 1)
                {
                    if (!result.IsObject)
                        throw new JsThrow(realm.NewTypeError(
                            $"WebAssembly import {import.Module}.{import.Name} must return an array"));
                    var obj = result.AsObject;
                    for (var i = 0; i < results.Length; i++)
                        results[i] = ToWasmValue(realm, obj.Get(i.ToString(CultureInfo.InvariantCulture)), type.Results[i]);
                }
            },
        };
    }

    private static MemoryInstance RequireImportedMemory(JsRealm realm, Import import, JsValue value)
    {
        var memory = RequireMemory(realm, value).Memory;
        if (import.Type.Value is not MemoryType type)
            throw new JsThrow(realm.NewTypeError(
                $"WebAssembly import {import.Module}.{import.Name} has no memory type"));

        var pages = (ulong)(memory.Data.Length / 65536);
        if (pages < type.Minimum)
            throw new JsThrow(realm.NewTypeError(
                $"WebAssembly import {import.Module}.{import.Name} memory is smaller than required"));
        return memory;
    }

    private static JsObject BuildExports(
        JsRealm realm,
        JsObject memoryProto,
        JsObject tableProto,
        WasmInstance instance)
    {
        var exports = new JsObject(realm.ObjectPrototype);
        foreach (var export in instance.Module.Exports)
        {
            switch (export.Kind)
            {
                case ImportExportKind.Function:
                    exports.DefineOwnProperty(export.Name,
                        PropertyDescriptor.Data(
                            BuildExportedFunction(
                                realm,
                                instance,
                                export.Name,
                                instance.GetFunctionAddress((int)export.Index)),
                            writable: true, enumerable: true, configurable: true));
                    break;
                case ImportExportKind.Memory:
                    if (instance.TryGetExportedMemory(export.Name, out var memory))
                    {
                        var memoryObject = new WasmMemoryObject(memoryProto, memory);
                        RegisterMemoryObject(instance, memoryObject);
                        exports.DefineOwnProperty(export.Name,
                            PropertyDescriptor.Data(JsValue.Object(memoryObject),
                                writable: true, enumerable: true, configurable: true));
                    }
                    break;
                case ImportExportKind.Table:
                    if (instance.TryGetExportedTable(export.Name, out var table))
                    {
                        exports.DefineOwnProperty(export.Name,
                            PropertyDescriptor.Data(
                                JsValue.Object(new WasmTableObject(tableProto, table, instance)),
                                writable: true,
                                enumerable: true,
                                configurable: true));
                    }
                    break;
            }
        }

        return exports;
    }

    private static JsObject BuildInstantiateResult(
        JsRealm realm,
        WasmModuleObject moduleWrapper,
        WasmInstanceObject instanceWrapper)
    {
        var result = new JsObject(realm.ObjectPrototype);
        result.DefineOwnProperty("module",
            PropertyDescriptor.Data(JsValue.Object(moduleWrapper), writable: true, enumerable: true, configurable: true));
        result.DefineOwnProperty("instance",
            PropertyDescriptor.Data(JsValue.Object(instanceWrapper), writable: true, enumerable: true, configurable: true));
        return result;
    }

    private static JsValue BuildExportedFunction(
        JsRealm realm,
        WasmInstance instance,
        string exportName,
        FunctionAddress address)
    {
        if (!instance.TryGetExportedFunction(exportName, out var function))
            throw new JsThrow(realm.NewTypeError($"Missing WebAssembly function export '{exportName}'"));

        var type = FunctionType(instance, function);
        var fn = new JsNativeFunction(realm, exportName, type.Parameters.Length, (_, args) =>
            InvokeExportedFunction(realm, instance, exportName, type, args), isConstructor: false);
        WasmFunctionReferences.Add(fn, new WasmFunctionReference(instance, address));
        return JsValue.Object(fn);
    }

    private static JsValue InvokeExportedFunction(
        JsRealm realm,
        WasmInstance instance,
        string exportName,
        FuncType type,
        JsValue[] args)
    {
        var parameters = new WasmValue[type.Parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            parameters[i] = ToWasmValue(realm, Arg(args, i), type.Parameters[i]);

        var results = new WasmValue[type.Results.Length];
        try
        {
            instance.Invoke(exportName, parameters, results);
        }
        catch (WasmTrapException ex)
        {
            throw new JsThrow(realm.NewTypeError(
                $"WebAssembly function {exportName} trapped: {ex.Message}"));
        }
        finally
        {
            SyncMemoryObjects(instance);
        }

        return results.Length switch
        {
            0 => JsValue.Undefined,
            1 => FromWasmValue(realm, results[0], type.Results[0]),
            _ => MultiValueResult(realm, results, type.Results),
        };
    }

    private static FuncType FunctionType(WasmInstance instance, FunctionInstance function)
        => function.Value switch
        {
            RuntimeFunction runtime => instance.Module.Types[(int)runtime.Definition.TypeIndex].AsFunctionType(),
            HostFunction host => host.Type,
            _ => throw new InvalidOperationException("Unknown WebAssembly function kind."),
        };

    private static WasmValue ToWasmValue(JsRealm realm, JsValue value, WasmValueType type)
    {
        if (type == WasmTypes.I32) return WasmValue.FromI32((int)JsValue.ToNumber(value));
        if (type == WasmTypes.I64)
        {
            if (value.Kind == JsValueKind.BigInt) return WasmValue.FromI64((long)value.AsBigInt);
            return WasmValue.FromI64((long)JsValue.ToNumber(value));
        }
        if (type == WasmTypes.F32) return WasmValue.FromF32((float)JsValue.ToNumber(value));
        if (type == WasmTypes.F64) return WasmValue.FromF64(JsValue.ToNumber(value));

        throw new JsThrow(realm.NewTypeError($"Unsupported WebAssembly parameter type '{type}'"));
    }

    private static JsValue FromWasmValue(JsRealm realm, WasmValue value, WasmValueType type)
    {
        if (type == WasmTypes.I32) return JsValue.Number(value.I32);
        if (type == WasmTypes.I64) return JsValue.BigInt(new BigInteger(value.I64));
        if (type == WasmTypes.F32) return JsValue.Number(value.F32);
        if (type == WasmTypes.F64) return JsValue.Number(value.F64);

        throw new JsThrow(realm.NewTypeError($"Unsupported WebAssembly result type '{type}'"));
    }

    private static JsValue FromTableValue(JsRealm realm, WasmTableObject table, WasmValue value)
    {
        if (value.IsNullReference)
            return JsValue.Null;
        if (table.Owner is null)
            throw new JsThrow(realm.NewTypeError("WebAssembly.Table function references are not bound to an instance"));

        var address = new FunctionAddress((long)value.Bits);
        if (table.FunctionCache.TryGetValue(address.Value, out var cached))
            return cached;

        var function = table.Owner.Store.GetFunctionInstance(address);
        var type = FunctionType(table.Owner, function);
        var exportName = FindFunctionExportName(table.Owner, address);
        var fn = new JsNativeFunction(realm, exportName ?? $"wasm table function {address.Value}",
            type.Parameters.Length, (_, args) =>
            {
                if (exportName is not null)
                    return InvokeExportedFunction(realm, table.Owner, exportName, type, args);
                throw new JsThrow(realm.NewTypeError(
                    $"WebAssembly table function {address.Value} is not callable from Starling yet"));
            }, isConstructor: false);
        WasmFunctionReferences.Add(fn, new WasmFunctionReference(table.Owner, address));

        var result = JsValue.Object(fn);
        table.FunctionCache[address.Value] = result;
        return result;
    }

    private static WasmValue ToTableValue(
        JsRealm realm,
        WasmTableObject table,
        JsValue value,
        bool defaultToNull)
    {
        if (value.IsNull || (defaultToNull && value.IsUndefined))
            return WasmValue.NullReference;
        if (!value.IsObject ||
            table.Owner is null ||
            !WasmFunctionReferences.TryGetValue(value.AsObject, out var reference) ||
            !ReferenceEquals(reference.Owner, table.Owner))
        {
            throw new JsThrow(realm.NewTypeError("WebAssembly.Table value must be a wasm function or null"));
        }

        return WasmValue.FromRaw((ulong)reference.Address.Value);
    }

    private static string? FindFunctionExportName(WasmInstance instance, FunctionAddress address)
    {
        foreach (var export in instance.Module.Exports)
        {
            if (export.Kind == ImportExportKind.Function &&
                instance.GetFunctionAddress((int)export.Index) == address)
            {
                return export.Name;
            }
        }

        return null;
    }

    private static JsValue MultiValueResult(JsRealm realm, WasmValue[] values, IReadOnlyList<WasmValueType> types)
    {
        var array = new JsArray(realm);
        for (var i = 0; i < values.Length; i++)
            array.Push(FromWasmValue(realm, values[i], types[i]));
        return JsValue.Object(array);
    }

    private static int ToNonNegativeInt(
        JsRealm realm,
        JsValue value,
        string name,
        string owner = "WebAssembly.Memory descriptor")
    {
        var number = JsValue.ToNumber(value);
        if (double.IsNaN(number) || number < 0 || number > int.MaxValue)
            throw new JsThrow(realm.NewTypeError($"{owner} '{name}' must be a non-negative integer"));
        return (int)number;
    }

    private static int ToTableIndex(JsRealm realm, JsValue value, int length)
    {
        var number = JsValue.ToNumber(value);
        if (double.IsNaN(number) ||
            number < 0 ||
            number >= length ||
            number > int.MaxValue ||
            Math.Truncate(number) != number)
        {
            throw new JsThrow(realm.NewRangeError("WebAssembly.Table index out of bounds"));
        }

        return (int)number;
    }

    private static JsValue StreamBytes(JsRealm realm, JsValue source, Func<byte[], JsValue> onBytes)
    {
        var sourcePromise = PromiseResolve(realm, source);
        var bytesPromise = PromiseThen(realm, sourcePromise, new JsNativeFunction(realm, "", 1, (_, args) =>
        {
            var response = Arg(args, 0);
            if (!response.IsObject)
                throw new JsThrow(realm.NewTypeError("WebAssembly streaming source must resolve to a Response"));
            var arrayBuffer = response.AsObject.Get("arrayBuffer");
            if (!AbstractOperations.IsCallable(arrayBuffer))
                throw new JsThrow(realm.NewTypeError("WebAssembly streaming source has no arrayBuffer method"));
            return AbstractOperations.Call(realm.ActiveVm, arrayBuffer, response, Array.Empty<JsValue>());
        }, isConstructor: false));

        return PromiseThen(realm, bytesPromise, new JsNativeFunction(realm, "", 1, (_, args) =>
        {
            var bytes = CoreWebApiBinding.BytesFromBufferSource(realm, Arg(args, 0));
            return onBytes(bytes);
        }, isConstructor: false));
    }

    private static JsValue PromiseResolve(JsRealm realm, JsValue value)
    {
        if (realm.PromiseConstructor is null)
            throw new InvalidOperationException("Promise not installed");
        var resolve = realm.PromiseConstructor.Get("resolve");
        return AbstractOperations.Call(realm.ActiveVm, resolve,
            JsValue.Object(realm.PromiseConstructor), new[] { value });
    }

    private static JsValue PromiseThen(JsRealm realm, JsValue promise, JsObject onFulfilled)
    {
        if (!promise.IsObject)
            throw new JsThrow(realm.NewTypeError("Expected Promise"));
        var then = promise.AsObject.Get("then");
        if (!AbstractOperations.IsCallable(then))
            throw new JsThrow(realm.NewTypeError("Expected thenable Promise"));
        return AbstractOperations.Call(realm.ActiveVm, then, promise,
            new[] { JsValue.Object(onFulfilled) });
    }

    private static byte[] MemoryBytes(MemoryInstance memory) =>
        (byte[])MemoryDataField.GetValue(memory)!;

    private static void RegisterMemoryObject(WasmInstance instance, WasmMemoryObject memory)
        => InstanceMemoryObjects.GetOrCreateValue(instance).Items.Add(memory);

    private static void SyncMemoryObjects(WasmInstance instance)
    {
        if (!InstanceMemoryObjects.TryGetValue(instance, out var memories))
            return;
        foreach (var memory in memories.Items)
            memory.SyncBuffer(MemoryBytes(memory.Memory));
    }
}

internal sealed class WasmModuleObject : JsObject
{
    public WasmModuleObject(JsObject? prototype, WasmModule module) : base(prototype)
        => Module = module;

    public WasmModule Module { get; }
}

internal sealed class WasmInstanceObject : JsObject
{
    public WasmInstanceObject(JsObject? prototype, WasmInstance instance, JsObject exports) : base(prototype)
    {
        Instance = instance;
        DefineOwnProperty("exports",
            PropertyDescriptor.Data(JsValue.Object(exports), writable: false, enumerable: true, configurable: true));
    }

    public WasmInstance Instance { get; }
}

internal sealed class WasmMemoryObject : JsObject
{
    public WasmMemoryObject(JsObject? prototype, MemoryInstance memory) : base(prototype)
        => Memory = memory;

    public MemoryInstance Memory { get; }

    private JsArrayBuffer? Buffer { get; set; }

    private byte[]? BufferBytes { get; set; }

    public JsArrayBuffer GetBuffer(JsObject? prototype, byte[] bytes)
    {
        if (Buffer is null)
        {
            Buffer = JsArrayBuffer.Wrap(prototype, bytes);
            BufferBytes = bytes;
        }
        else if (!ReferenceEquals(BufferBytes, bytes))
        {
            Buffer.ReplaceBytes(bytes);
            BufferBytes = bytes;
        }

        return Buffer;
    }

    public void SyncBuffer(byte[] bytes)
    {
        if (Buffer is null || ReferenceEquals(BufferBytes, bytes))
            return;
        Buffer.ReplaceBytes(bytes);
        BufferBytes = bytes;
    }
}

internal sealed class WasmTableObject : JsObject
{
    public WasmTableObject(JsObject? prototype, TableInstance table, WasmInstance? owner = null) : base(prototype)
    {
        Table = table;
        Owner = owner;
    }

    public TableInstance Table { get; }

    public WasmInstance? Owner { get; }

    public Dictionary<long, JsValue> FunctionCache { get; } = new();
}

internal sealed class WasmFunctionReference
{
    public WasmFunctionReference(WasmInstance owner, FunctionAddress address)
    {
        Owner = owner;
        Address = address;
    }

    public WasmInstance Owner { get; }

    public FunctionAddress Address { get; }
}

internal sealed class WasmInstanceMemoryObjects
{
    public List<WasmMemoryObject> Items { get; } = new();
}
