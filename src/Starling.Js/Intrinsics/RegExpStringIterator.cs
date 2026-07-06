using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §22.2.9 RegExp String Iterator — the iterator returned by
/// <c>String.prototype.matchAll(re)</c> / <c>RegExp.prototype[@@matchAll]</c>.
/// Holds the species-constructed matcher OBJECT and drives §22.2.7.1
/// RegExpExec per <c>.next()</c>, so a subclass or monkey-patched
/// <c>exec</c>/<c>lastIndex</c> is honored. Inherits from %IteratorPrototype%
/// so <c>iter[Symbol.iterator]()</c> returns the same iterator (one-shot).
/// </summary>
internal sealed class JsRegExpStringIterator : JsObject
{
    private readonly JsObject _matcher;
    private readonly string _input;
    private readonly bool _global;
    private readonly bool _unicode;
    private bool _done;

    public JsRegExpStringIterator(JsRealm realm, JsObject matcher, string input, bool global, bool unicode)
        : base(realm.IteratorPrototype)
    {
        _matcher = matcher;
        _input = input;
        _global = global;
        _unicode = unicode;

        // Install .next as an own property so we don't need a dedicated
        // %RegExpStringIteratorPrototype% slot in JsRealm. %IteratorPrototype%
        // already provides [@@iterator] returning this, satisfying the
        // it[Symbol.iterator]() === it invariant.
        var nextFn = new JsNativeFunction(realm, "next", 0,
            (thisV, _) => thisV.IsObject && thisV.AsObject is JsRegExpStringIterator it
                ? it.Next(realm)
                : throw new JsThrow(realm.NewTypeError("RegExp String Iterator.prototype.next called on incompatible receiver")),
            isConstructor: false);
        DefineOwnProperty("next",
            PropertyDescriptor.BuiltinMethod(JsValue.Object(nextFn)));
    }

    public JsValue Next(JsRealm realm)
    {
        if (_done)
        {
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }

        var vm = realm.ActiveVm;
        var result = RegExpCtor.RegExpExec(realm, _matcher, _input);
        if (result.IsNull)
        {
            _done = true;
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }

        if (!_global)
        {
            _done = true;
            return IteratorIntrinsics.MakeResult(realm, result, done: false);
        }

        // §22.2.9.2.1 step ii.4 — an empty total match advances lastIndex so
        // the loop terminates.
        var matchStr = JsValue.ToStringValue(AbstractOperations.Get(vm, result.AsObject, "0"));
        if (matchStr.Length == 0)
        {
            var li = (long)Math.Max(0, JsValue.ToNumber(AbstractOperations.Get(vm, _matcher, "lastIndex")));
            AbstractOperations.Set(vm, _matcher, "lastIndex",
                JsValue.Number(RegExpCtor.AdvanceStringIndex(_input, (int)Math.Min(li, int.MaxValue), _unicode)));
        }

        return IteratorIntrinsics.MakeResult(realm, result, done: false);
    }
}
