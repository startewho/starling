using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// B1b-2c — install <c>next</c>/<c>return</c>/<c>throw</c> on
/// %GeneratorPrototype% and %AsyncGeneratorPrototype%. The actual frame
/// driving lives on <see cref="JsGenerator"/> + <see cref="SuspendedFrame"/>.
/// </summary>
public static class GeneratorIntrinsics
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.GeneratorPrototype;

        var next = new JsNativeFunction(realm, "next", 1,
            (thisV, args) => GeneratorNext(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined),
            isConstructor: false);
        proto.DefineOwnProperty("next", PropertyDescriptor.BuiltinMethod(JsValue.Object(next)));

        var ret = new JsNativeFunction(realm, "return", 1,
            (thisV, args) => GeneratorReturn(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined),
            isConstructor: false);
        proto.DefineOwnProperty("return", PropertyDescriptor.BuiltinMethod(JsValue.Object(ret)));

        var thr = new JsNativeFunction(realm, "throw", 1,
            (thisV, args) => GeneratorThrow(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined),
            isConstructor: false);
        proto.DefineOwnProperty("throw", PropertyDescriptor.BuiltinMethod(JsValue.Object(thr)));

        // [Symbol.iterator]() { return this }
        var iter = new JsNativeFunction(realm, "[Symbol.iterator]", 0,
            (thisV, _) => thisV, isConstructor: false);
        proto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(iter)));

        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Generator"), writable: false, enumerable: false, configurable: true));

        // ----- AsyncGeneratorPrototype (minimal): next returns Promise of result.
        var asyncProto = realm.AsyncGeneratorPrototype;
        var asyncNext = new JsNativeFunction(realm, "next", 1,
            (thisV, args) => AsyncGeneratorNext(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined),
            isConstructor: false);
        asyncProto.DefineOwnProperty("next", PropertyDescriptor.BuiltinMethod(JsValue.Object(asyncNext)));
        var asyncIter = new JsNativeFunction(realm, "[Symbol.asyncIterator]", 0,
            (thisV, _) => thisV, isConstructor: false);
        asyncProto.DefineOwnProperty(SymbolCtor.AsyncIterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(asyncIter)));
        asyncProto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("AsyncGenerator"), writable: false, enumerable: false, configurable: true));
    }

    private static JsValue GeneratorNext(JsRealm realm, JsValue thisV, JsValue sent)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsGenerator gen)
            throw new JsThrow(realm.NewTypeError("Generator.prototype.next called on non-Generator"));
        if (gen.Done) return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        if (!gen.Started)
        {
            gen.Started = true;
            gen.Frame.Resume(JsValue.Undefined); // sent value on first call is ignored
        }
        else
        {
            gen.Frame.Resume(sent);
        }
        if (gen.Frame.Completed)
        {
            gen.Done = true;
            if (gen.Frame.ThrewUncaught) throw new JsThrow(gen.Frame.ReturnValue);
            return IteratorIntrinsics.MakeResult(realm, gen.Frame.ReturnValue, done: true);
        }
        return IteratorIntrinsics.MakeResult(realm, gen.Frame.YieldedValue, done: false);
    }

    private static JsValue GeneratorReturn(JsRealm realm, JsValue thisV, JsValue sent)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsGenerator gen)
            throw new JsThrow(realm.NewTypeError("Generator.prototype.return called on non-Generator"));
        if (gen.Done || !gen.Started)
        {
            gen.Done = true;
            return IteratorIntrinsics.MakeResult(realm, sent, done: true);
        }
        // Tell the worker to perform an early return: we model this as
        // injecting a throw of a sentinel, then catching the sentinel in
        // the caller. Simpler approach: signal via a flag on the frame.
        // For minimal support, treat .return like a final completion —
        // forcefully mark done. The worker thread will eventually GC.
        gen.Done = true;
        // Best-effort: trigger the worker to advance once so any finally
        // blocks run. We inject a throw of a unique sentinel that we
        // catch here. Skip for simplicity — finally semantics on .return
        // are documented as a known gap.
        return IteratorIntrinsics.MakeResult(realm, sent, done: true);
    }

    private static JsValue GeneratorThrow(JsRealm realm, JsValue thisV, JsValue err)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsGenerator gen)
            throw new JsThrow(realm.NewTypeError("Generator.prototype.throw called on non-Generator"));
        if (gen.Done || !gen.Started)
        {
            gen.Done = true;
            throw new JsThrow(err);
        }
        gen.Frame.Resume(err, withThrow: true);
        if (gen.Frame.Completed)
        {
            gen.Done = true;
            if (gen.Frame.ThrewUncaught) throw new JsThrow(gen.Frame.ReturnValue);
            return IteratorIntrinsics.MakeResult(realm, gen.Frame.ReturnValue, done: true);
        }
        return IteratorIntrinsics.MakeResult(realm, gen.Frame.YieldedValue, done: false);
    }

    private static JsValue AsyncGeneratorNext(JsRealm realm, JsValue thisV, JsValue sent)
    {
        // Minimal: wrap the sync next() result in Promise.resolve and put
        // {value, done} into the promise. Doesn't support await inside the
        // body — those would throw at the Suspend opcode because no awaiter
        // wiring exists. Documented gap.
        var promise = new JsPromise(realm.PromisePrototype);
        try
        {
            var result = GeneratorNext(realm, thisV, sent);
            PromiseCtor.Resolve(realm, promise, result);
        }
        catch (JsThrow ex)
        {
            PromiseCtor.Reject(realm, promise, ex.Value);
        }
        return JsValue.Object(promise);
    }
}
