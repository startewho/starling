namespace Tessera.Js.Runtime;

/// <summary>
/// B1b-2c — Generator instance. Returned by invoking a generator function
/// (<c>function* g() { ... }</c>). Implements the iterator protocol:
/// <c>.next(v)</c>, <c>.return(v)</c>, <c>.throw(e)</c>, and
/// <c>[Symbol.iterator]() === this</c>.
/// </summary>
public sealed class JsGenerator : JsObject
{
    /// <summary>Underlying suspended frame — owns the worker thread that
    /// runs the generator body to its next yield/return.</summary>
    public SuspendedFrame Frame { get; }

    /// <summary>True after the generator completes (normally or via
    /// throw / return). Subsequent <c>.next()</c> calls return
    /// <c>{value: undefined, done: true}</c>.</summary>
    public bool Done { get; internal set; }

    /// <summary>True after the worker thread has been kicked off the first
    /// time. Generators don't run their body until the first
    /// <c>.next()</c>.</summary>
    public bool Started { get; internal set; }

    public JsGenerator(JsRealm realm, SuspendedFrame frame)
        : base(realm.GeneratorPrototype)
    {
        Frame = frame ?? throw new System.ArgumentNullException(nameof(frame));
    }
}

/// <summary>
/// B1b-2c — Async function wrapper. Tracks the worker thread executing
/// the body and the outer Promise the caller observes.
/// </summary>
public sealed class JsAsyncFunctionState
{
    public SuspendedFrame Frame { get; }
    public JsPromise OuterPromise { get; }
    public bool Started { get; set; }
    public bool Settled { get; set; }

    public JsAsyncFunctionState(SuspendedFrame frame, JsPromise outerPromise)
    {
        Frame = frame ?? throw new System.ArgumentNullException(nameof(frame));
        OuterPromise = outerPromise ?? throw new System.ArgumentNullException(nameof(outerPromise));
    }
}
