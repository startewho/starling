// SPDX-License-Identifier: Apache-2.0

using System.Numerics;
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
    public static void Install(JsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var realm = runtime.Realm;
        if (realm.GlobalObject.GetOwnPropertyDescriptor("WebAssembly") is not null) return;

        var moduleProto = new JsObject(realm.ObjectPrototype);
        var instanceProto = new JsObject(realm.ObjectPrototype);
        var memoryProto = new JsObject(realm.ObjectPrototype);

        var moduleCtor = new JsNativeFunction(realm, "Module", 1, (_, args) =>
            JsValue.Object(new WasmModuleObject(moduleProto, DecodeModule(realm, Arg(args, 0)))),
            isConstructor: true);
        WireConstructor(moduleCtor, moduleProto, "Module");

        var instanceCtor = new JsNativeFunction(realm, "Instance", 1, (_, args) =>
        {
            var module = RequireModule(realm, Arg(args, 0));
            var importObject = args.Length > 1 ? args[1] : JsValue.Undefined;
            return JsValue.Object(BuildInstanceObject(realm, instanceProto, memoryProto, module, importObject));
        }, isConstructor: true);
        WireConstructor(instanceCtor, instanceProto, "Instance");

        var memoryCtor = new JsNativeFunction(realm, "Memory", 1, (_, args) =>
        {
            var descriptor = Arg(args, 0);
            if (!descriptor.IsObject)
                throw new JsThrow(realm.NewTypeError("WebAssembly.Memory requires a descriptor object"));

            var obj = descriptor.AsObject;
            var initial = ToNonNegativeInt(realm, obj.Get("initial"), "initial");
            return JsValue.Object(new WasmMemoryObject(memoryProto, new MemoryInstance(initial)));
        }, isConstructor: true);
        WireConstructor(memoryCtor, memoryProto, "Memory");
        EventTargetBinding.DefineAccessor(realm, memoryProto, "buffer", (thisV, _) =>
        {
            var memory = RequireMemory(realm, thisV);
            return JsValue.Object(CoreWebApiBinding.NewArrayBuffer(realm, memory.Memory.Data.ToArray()));
        });
        EventTargetBinding.DefineMethod(realm, memoryProto, "grow", (thisV, args) =>
        {
            var memory = RequireMemory(realm, thisV).Memory;
            var oldPages = memory.Data.Length / 65536;
            memory.Grow(ToNonNegativeInt(realm, Arg(args, 0), "delta"));
            return JsValue.Number(oldPages);
        }, length: 1);

        var wasm = new JsObject(realm.ObjectPrototype);
        wasm.DefineOwnProperty("Module",
            PropertyDescriptor.Data(JsValue.Object(moduleCtor), writable: true, enumerable: false, configurable: true));
        wasm.DefineOwnProperty("Instance",
            PropertyDescriptor.Data(JsValue.Object(instanceCtor), writable: true, enumerable: false, configurable: true));
        wasm.DefineOwnProperty("Memory",
            PropertyDescriptor.Data(JsValue.Object(memoryCtor), writable: true, enumerable: false, configurable: true));

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
                    var instance = BuildInstanceObject(realm, instanceProto, memoryProto, moduleObject.Module, importObject);
                    return FetchBinding.ResolvedPromise(realm, JsValue.Object(instance));
                }

                var module = DecodeModule(realm, Arg(args, 0));
                var moduleWrapper = new WasmModuleObject(moduleProto, module);
                var instanceWrapper = BuildInstanceObject(realm, instanceProto, memoryProto, module, importObject);
                var result = new JsObject(realm.ObjectPrototype);
                result.DefineOwnProperty("module",
                    PropertyDescriptor.Data(JsValue.Object(moduleWrapper), writable: true, enumerable: true, configurable: true));
                result.DefineOwnProperty("instance",
                    PropertyDescriptor.Data(JsValue.Object(instanceWrapper), writable: true, enumerable: true, configurable: true));
                return FetchBinding.ResolvedPromise(realm, JsValue.Object(result));
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

        EventTargetBinding.DefineMethod(realm, wasm, "instantiateStreaming", (_, _) =>
            FetchBinding.RejectedPromise(realm,
                realm.NewTypeError("WebAssembly.instantiateStreaming is not implemented yet")), length: 1);

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

    private static WasmInstanceObject BuildInstanceObject(
        JsRealm realm,
        JsObject instanceProto,
        JsObject memoryProto,
        WasmModule module,
        JsValue importObject)
    {
        if (module.Imports.Length > 0)
            throw new JsThrow(realm.NewTypeError("Imported WebAssembly items are not implemented yet"));

        _ = importObject;
        var store = new WasmStore();
        var linker = new WasmLinker(store);
        var instance = linker.Instantiate(module);
        var exports = BuildExports(realm, memoryProto, instance);
        return new WasmInstanceObject(instanceProto, instance, exports);
    }

    private static JsObject BuildExports(JsRealm realm, JsObject memoryProto, WasmInstance instance)
    {
        var exports = new JsObject(realm.ObjectPrototype);
        foreach (var export in instance.Module.Exports)
        {
            switch (export.Kind)
            {
                case ImportExportKind.Function:
                    exports.DefineOwnProperty(export.Name,
                        PropertyDescriptor.Data(BuildExportedFunction(realm, instance, export.Name),
                            writable: true, enumerable: true, configurable: true));
                    break;
                case ImportExportKind.Memory:
                    if (instance.TryGetExportedMemory(export.Name, out var memory))
                    {
                        exports.DefineOwnProperty(export.Name,
                            PropertyDescriptor.Data(JsValue.Object(new WasmMemoryObject(memoryProto, memory)),
                                writable: true, enumerable: true, configurable: true));
                    }
                    break;
            }
        }

        return exports;
    }

    private static JsValue BuildExportedFunction(JsRealm realm, WasmInstance instance, string exportName)
    {
        if (!instance.TryGetExportedFunction(exportName, out var function))
            throw new JsThrow(realm.NewTypeError($"Missing WebAssembly function export '{exportName}'"));

        var type = FunctionType(instance, function);
        var fn = new JsNativeFunction(realm, exportName, type.Parameters.Length, (_, args) =>
        {
            var parameters = new WasmValue[type.Parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                parameters[i] = ToWasmValue(realm, Arg(args, i), type.Parameters[i]);

            var results = new WasmValue[type.Results.Length];
            instance.Invoke(exportName, parameters, results);

            return results.Length switch
            {
                0 => JsValue.Undefined,
                1 => FromWasmValue(realm, results[0], type.Results[0]),
                _ => MultiValueResult(realm, results, type.Results),
            };
        }, isConstructor: false);
        return JsValue.Object(fn);
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

    private static JsValue MultiValueResult(JsRealm realm, WasmValue[] values, IReadOnlyList<WasmValueType> types)
    {
        var array = new JsArray(realm);
        for (var i = 0; i < values.Length; i++)
            array.Push(FromWasmValue(realm, values[i], types[i]));
        return JsValue.Object(array);
    }

    private static int ToNonNegativeInt(JsRealm realm, JsValue value, string name)
    {
        var number = JsValue.ToNumber(value);
        if (double.IsNaN(number) || number < 0 || number > int.MaxValue)
            throw new JsThrow(realm.NewTypeError($"WebAssembly.Memory descriptor '{name}' must be a non-negative integer"));
        return (int)number;
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
}
