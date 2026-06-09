namespace Starling.Js.Runtime;

/// <summary>
/// B1b-2c — Generator instance. Returned by invoking a generator function
/// (<c>function* g() { ... }</c>). Implements the iterator protocol:
/// <c>.next(v)</c>, <c>.return(v)</c>, <c>.throw(e)</c>, and
/// <c>[Symbol.iterator]() === this</c>.
/// </summary>
public sealed class JsGenerator : JsObject
{
    /// <summary>Underlying heap-backed frame that runs the generator body to
    /// its next yield or return.</summary>
    public SuspendedFrame Frame { get; }

    /// <summary>True after the generator completes (normally or via
    /// throw / return). Subsequent <c>.next()</c> calls return
    /// <c>{value: undefined, done: true}</c>.</summary>
    public bool Done { get; internal set; }

    /// <summary>True after the body has been resumed the first time.
    /// Generators don't run their body until the first <c>.next()</c>.</summary>
    public bool Started { get; internal set; }

    private int _running;

    /// <summary>Enter a resume operation. Reentrant resume must throw instead
    /// of deadlocking or corrupting the parked frame.</summary>
    internal bool TryEnterResume()
        => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    internal void ExitResume()
        => Volatile.Write(ref _running, 0);

    public JsGenerator(JsRealm realm, SuspendedFrame frame)
        : base(realm.GeneratorPrototype)
    {
        Frame = frame ?? throw new System.ArgumentNullException(nameof(frame));
    }
}

/// <summary>
/// B1b-2c — Async function wrapper. Tracks the continuation frame and the
/// outer Promise the caller observes.
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

/// <summary>
/// wp:M3-04g — kind of a queued async-generator request, mirroring the
/// <c>completion</c> a resume injects into the suspended body (§27.6.3.2–.4).
/// </summary>
public enum AsyncGeneratorRequestKind
{
    /// <summary><c>.next(v)</c> — resume with a normal completion.</summary>
    Next,
    /// <summary><c>.return(v)</c> — resume with a Return completion so the
    /// body walks any enclosing <c>finally</c> blocks before completing.</summary>
    Return,
    /// <summary><c>.throw(e)</c> — resume by injecting a throw at the
    /// suspension point.</summary>
    Throw,
}

/// <summary>
/// wp:M3-04g — one queued AsyncGeneratorRequest (§27.6.3.1). Each call to
/// <c>next</c>/<c>return</c>/<c>throw</c> on an async generator allocates one
/// of these, hands the caller its <see cref="Capability"/> promise, and
/// enqueues it. The driver processes the queue strictly in FIFO order so the
/// body never runs two requests concurrently.
/// </summary>
public sealed class AsyncGeneratorRequest
{
    public AsyncGeneratorRequestKind Kind { get; }
    public JsValue Value { get; }
    public JsPromise Capability { get; }

    public AsyncGeneratorRequest(AsyncGeneratorRequestKind kind, JsValue value, JsPromise capability)
    {
        Kind = kind;
        Value = value;
        Capability = capability ?? throw new System.ArgumentNullException(nameof(capability));
    }
}

/// <summary>
/// wp:M3-04g — Async-generator instance returned by invoking an
/// <c>async function*</c>. Owns the suspended body frame plus a serialized
/// queue of pending <c>next</c>/<c>return</c>/<c>throw</c> requests
/// (AsyncGeneratorEnqueue / DrainQueue, §27.6.3). Within the body, <c>yield</c>
/// produces a result for the head request and <c>await</c> suspends the body
/// until the awaited promise settles — both ride the same
/// <see cref="SuspendedFrame"/>.
/// </summary>
public sealed class JsAsyncGenerator : JsObject
{
    /// <summary>Underlying heap-backed frame that runs the async-generator body
    /// across yield and await suspensions.</summary>
    public SuspendedFrame Frame { get; }

    /// <summary>True after the body has completed (return / throw / fall-off).
    /// Once done, queued requests resolve to <c>{value:undefined, done:true}</c>
    /// (or, for <c>throw</c> on a not-yet-started generator, reject).</summary>
    public bool Done { get; set; }

    /// <summary>True once the body has been resumed the first time. The body
    /// does not run until the first request is processed.</summary>
    public bool Started { get; set; }

    /// <summary>True while a request is being driven to its next yield/await
    /// suspension or completion. Guards against re-entrant draining.</summary>
    public bool Draining { get; set; }

    /// <summary>FIFO queue of pending requests (§27.6.3.1 AsyncGeneratorEnqueue).</summary>
    public Queue<AsyncGeneratorRequest> Queue { get; } = new();

    public JsAsyncGenerator(JsRealm realm, SuspendedFrame frame)
        : base(realm.AsyncGeneratorPrototype)
    {
        Frame = frame ?? throw new System.ArgumentNullException(nameof(frame));
    }
}
