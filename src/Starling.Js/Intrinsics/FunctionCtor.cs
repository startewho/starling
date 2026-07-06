using System.Text;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// Spec section 20.2 — the Function intrinsic. Populates
/// <see cref="JsRealm.FunctionConstructor"/> and <see cref="JsRealm.FunctionPrototype"/>,
/// then registers the constructor on the realm's global object.
/// </summary>
/// <remarks>
/// The constructor itself, called as <c>Function(src)</c> or
/// <c>new Function(src)</c>, assembles the parameter list and body into a
/// global-scope function expression, then compiles it through the shared eval
/// path.
/// </remarks>
public static class FunctionCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var funcProto = realm.FunctionPrototype;

        // ----------------------------------------------------------- Constructor
        // Spec 20.2.1 — `Function(p1, …, pN, body)` builds a function from source:
        // the last argument is the body, the rest are comma-joined into the
        // parameter list. We synthesize `(function anonymous(<params>){<body>})`,
        // compile it in global (eval) scope, and return the resulting function.
        var ctor = new JsNativeFunction("Function", (newTarget, args) =>
        {
            var fn = BuildDynamicFunction(realm, args);
            // §20.2.1.1 CreateDynamicFunction uses OrdinaryCreateFromConstructor
            // for the new function's [[Prototype]]. When subclassed
            // (`class S extends Function {}`), super()'s new.target is S, so the
            // produced function is re-parented to S.prototype for instanceof.
            if (fn.IsObject)
            {
                var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, funcProto,
                    static r => r.FunctionPrototype);
                if (!ReferenceEquals(instProto, funcProto))
                {
                    fn.AsObject.SetPrototypeOf(instProto);
                }
            }
            return fn;
        }, isConstructor: true);
        // The constructor inherits from its own prototype object.
        ctor.SetPrototypeOf(funcProto);
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Function"), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(funcProto), writable: false, enumerable: false, configurable: false));

        // Function.prototype.[[Prototype]] is already wired in JsRealm's
        // bootstrap (chain to Object.prototype). Stamp `constructor`, a length of
        // 0, a name of "" (spec 20.2.3), and the prototype methods.
        IntrinsicHelpers.BulkInstallBuiltins(realm, funcProto, new[]
        {
            new IntrinsicHelpers.BulkMember("constructor", 0, null, JsValue.Object(ctor)),
            new IntrinsicHelpers.BulkMember("length", 0, null, JsValue.Number(0), Shape.Configurable),
            new IntrinsicHelpers.BulkMember("name", 0, null, JsValue.String(""), Shape.Configurable),
            new IntrinsicHelpers.BulkMember("call", 1, (thisV, args) => ProtoCall(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("apply", 2, (thisV, args) => ProtoApply(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("bind", 1, (thisV, args) => ProtoBind(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toString", 0, (thisV, args) => ProtoToString(realm, thisV)),
        });

        // §20.2.3 — restricted properties. %Function.prototype% has `caller`
        // and `arguments` accessors whose get AND set are %ThrowTypeError%
        // (the SAME intrinsic as a strict arguments object's `callee` poison,
        // observably — §10.2.4.1). Installed AFTER the bulk shape adopt —
        // accessors can never enter a shape.
        var throwTypeError = realm.ThrowTypeErrorIntrinsic;
        var restricted = PropertyDescriptor.Accessor(throwTypeError, throwTypeError);
        funcProto.DefineOwnProperty("caller", restricted);
        funcProto.DefineOwnProperty("arguments", restricted);

        // §20.2.3.6 Function.prototype[@@hasInstance] — non-writable,
        // non-enumerable, non-configurable; runs OrdinaryHasInstance.
        var hasInstance = new JsNativeFunction(realm, "[Symbol.hasInstance]", 1,
            (thisV, args) => JsValue.Boolean(
                OrdinaryHasInstance(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined)),
            isConstructor: false);
        funcProto.DefineOwnProperty(SymbolCtor.HasInstance,
            PropertyDescriptor.Data(JsValue.Object(hasInstance), writable: false, enumerable: false, configurable: false));

        realm.FunctionConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Function",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Spec 20.2.1.1 CreateDynamicFunction — assemble source from the
    /// argument list (leading args = parameters, last arg = body, each pushed
    /// through the OBSERVABLE ToString in argument order) and compile it as a
    /// function expression in global scope. A malformed parameter list or body
    /// surfaces as a SyntaxError via the shared eval path.</summary>
    private static JsValue BuildDynamicFunction(JsRealm realm, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        var paramList = "";
        var body = "";
        if (args.Length == 1)
        {
            body = AbstractOperations.ToStringJs(vm, args[0]);
        }
        else if (args.Length > 1)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(AbstractOperations.ToStringJs(vm, args[i]));
            }
            paramList = sb.ToString();
            body = AbstractOperations.ToStringJs(vm, args[^1]);
        }
        // The newline before ')' guards against a `//`-comment in the last
        // parameter chunk; the newline before '}' likewise guards the body.
        var source = "(function anonymous(" + paramList + "\n) {\n" + body + "\n})";
        return Globals.RunGlobalSource(realm, source, "<anonymous>");
    }

    // ====================================================================
    //                          Prototype implementations
    // ====================================================================

    /// <summary>Spec 20.2.3.3 — Function.prototype.call(thisArg, ...args).</summary>
    private static JsValue ProtoCall(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsCallable(thisV))
        {
            throw new JsThrow(realm.NewTypeError("Function.prototype.call called on non-callable"));
        }

        var thisArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var rest = args.Length > 1 ? args[1..] : Array.Empty<JsValue>();
        return AbstractOperations.Call(realm.ActiveVm, thisV, thisArg, rest);
    }

    /// <summary>Spec 20.2.3.1 — Function.prototype.apply(thisArg, argsArray).</summary>
    private static JsValue ProtoApply(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsCallable(thisV))
        {
            throw new JsThrow(realm.NewTypeError("Function.prototype.apply called on non-callable"));
        }

        var thisArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var argsArrayV = args.Length > 1 ? args[1] : JsValue.Undefined;
        var callArgs = argsArrayV.IsNullish
            ? Array.Empty<JsValue>()
            : CreateListFromArrayLike(realm, argsArrayV);
        return AbstractOperations.Call(realm.ActiveVm, thisV, thisArg, callArgs);
    }

    /// <summary>§7.3.20 CreateListFromArrayLike — the OBSERVABLE element walk
    /// (accessor `length`/index reads run and their throws propagate).</summary>
    internal static JsValue[] CreateListFromArrayLike(JsRealm realm, JsValue argsArrayV)
    {
        if (!argsArrayV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("CreateListFromArrayLike called on non-object"));
        }

        var vm = realm.ActiveVm;
        var obj = argsArrayV.AsObject;
        var lenV = AbstractOperations.Get(vm, obj, "length");
        var n = lenV.IsNumber ? lenV.AsNumber : NumberCtor.ToNumber(lenV);
        if (double.IsNaN(n) || n <= 0)
        {
            return Array.Empty<JsValue>();
        }

        var len = (int)Math.Min(Math.Truncate(n), int.MaxValue);
        var callArgs = new JsValue[len];
        for (var i = 0; i < len; i++)
        {
            callArgs[i] = AbstractOperations.Get(vm, obj,
                i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return callArgs;
    }

    /// <summary>Spec 20.2.3.2 — Function.prototype.bind(thisArg, ...boundArgs).
    /// Returns a fresh bound function whose <c>[[Prototype]]</c> chains to
    /// <c>Function.prototype</c> so subsequent <c>.bind()</c> calls work.</summary>
    private static JsValue ProtoBind(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject || !AbstractOperations.IsCallable(thisV))
        {
            throw new JsThrow(realm.NewTypeError("Function.prototype.bind called on non-callable"));
        }

        var vm = realm.ActiveVm;
        var target = thisV.AsObject;
        var boundThis = args.Length > 0 ? args[0] : JsValue.Undefined;
        var boundArgs = args.Length > 1 ? args[1..] : Array.Empty<JsValue>();
        var bound = new JsBoundFunction(realm, target, boundThis, boundArgs);
        // §10.4.1.3 BoundFunctionCreate + §20.2.3.2 steps 4-10: `length`
        // derives from the target's own `length` only when it is Number-typed
        // (ToIntegerOrInfinity, +∞ preserved), minus the bound-argument count.
        double lengthNum = 0;
        if (target.GetOwnPropertyDescriptor("length") is not null)
        {
            var targetLenV = AbstractOperations.Get(vm, target, "length");
            if (targetLenV.IsNumber)
            {
                var t = targetLenV.AsNumber;
                if (double.IsPositiveInfinity(t))
                {
                    lengthNum = double.PositiveInfinity;
                }
                else if (!double.IsNaN(t) && !double.IsNegativeInfinity(t))
                {
                    lengthNum = Math.Max(0, Math.Truncate(t) - boundArgs.Length);
                }
            }
        }
        bound.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(lengthNum), writable: false, enumerable: false, configurable: true));
        var targetName = AbstractOperations.Get(vm, target, "name");
        var nameStr = targetName.Kind == JsValueKind.String ? targetName.AsString : "";
        bound.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("bound " + nameStr), writable: false, enumerable: false, configurable: true));
        return JsValue.Object(bound);
    }

    /// <summary>Spec 20.2.3.5 — Function.prototype.toString. Parsed functions
    /// return their exact source slice; built-ins/bound functions return a
    /// NativeFunction-grammar string; non-callables are a TypeError.</summary>
    private static JsValue ProtoToString(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Function.prototype.toString called on non-callable"));
        }

        switch (thisV.AsObject)
        {
            case JsFunction fn:
                return JsValue.String(fn.SourceText ?? NativeFunctionText(fn.Name));
            case JsNativeFunction nat:
                return JsValue.String(NativeFunctionText(nat.Name));
            case JsBoundFunction:
                return JsValue.String("function () { [native code] }");
            case JsProxy when AbstractOperations.IsCallable(thisV):
                return JsValue.String("function () { [native code] }");
            default:
                throw new JsThrow(realm.NewTypeError("Function.prototype.toString called on non-callable"));
        }
    }

    /// <summary>Render a built-in's name into the NativeFunction grammar:
    /// `function` [get|set] IdentifierName? `( ) { [ native code ] }`. Names
    /// that are not IdentifierNames (well-known-symbol methods, spaced
    /// accessor names) drop the invalid part.</summary>
    private static string NativeFunctionText(string name)
    {
        var accessor = "";
        if (name.StartsWith("get ", StringComparison.Ordinal))
        {
            accessor = "get ";
            name = name[4..];
        }
        else if (name.StartsWith("set ", StringComparison.Ordinal))
        {
            accessor = "set ";
            name = name[4..];
        }
        if (!IsIdentifierName(name))
        {
            name = "";
        }

        return $"function {accessor}{name}() {{ [native code] }}";
    }

    private static bool IsIdentifierName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var ok = c == '$' || c == '_' || char.IsLetter(c) || (i > 0 && char.IsDigit(c));
            if (!ok)
            {
                return false;
            }
        }
        return true;
    }

    // ====================================================================
    //                    instanceof machinery (§10.4.6.4)
    // ====================================================================

    /// <summary>§10.4.6.4 OrdinaryHasInstance — proxy-aware prototype walk;
    /// bound functions re-dispatch instanceof against their target.</summary>
    internal static bool OrdinaryHasInstance(JsRealm realm, JsValue c, JsValue o)
    {
        if (!AbstractOperations.IsCallable(c))
        {
            return false;
        }

        if (c.AsObject is JsBoundFunction bf)
        {
            return InstanceofOperator(realm, o, JsValue.Object(bf.Target));
        }

        if (!o.IsObject)
        {
            return false;
        }

        var proto = AbstractOperations.Get(realm.ActiveVm, c.AsObject, "prototype");
        if (!proto.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Function has non-object prototype in instanceof check"));
        }

        var protoObj = proto.AsObject;
        var p = o.AsObject.GetPrototypeOf();
        while (p is not null)
        {
            if (ReferenceEquals(p, protoObj))
            {
                return true;
            }

            p = p.GetPrototypeOf();
        }
        return false;
    }

    /// <summary>§13.10.2 InstanceofOperator — @@hasInstance dispatch used by
    /// the bound-function arm of OrdinaryHasInstance.</summary>
    private static bool InstanceofOperator(JsRealm realm, JsValue o, JsValue target)
    {
        if (!target.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Right-hand side of 'instanceof' is not an object"));
        }

        var vm = realm.ActiveVm;
        var method = AbstractOperations.GetMethod(vm, target, SymbolCtor.HasInstance);
        if (!method.IsUndefined && !method.IsNull)
        {
            return JsValue.ToBoolean(AbstractOperations.Call(vm, method, target, new[] { o }));
        }

        if (!AbstractOperations.IsCallable(target))
        {
            throw new JsThrow(realm.NewTypeError("Right-hand side of 'instanceof' is not callable"));
        }

        return OrdinaryHasInstance(realm, target, o);
    }
}
