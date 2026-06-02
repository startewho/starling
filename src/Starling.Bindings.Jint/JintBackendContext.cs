using System.Collections.Concurrent;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Loop;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Bindings.Jint;

/// <summary>
/// Per-session state shared by every Jint binding family. One instance is
/// created per page render and threaded through
/// <see cref="JintBindings.InstallAll(JintBackendContext)"/> into each family's
/// <c>Install(JintBackendContext)</c>.
/// </summary>
/// <remarks>
/// This is the stable contract binding families depend on. Do not change the
/// shape of the public surface lightly. Binding families read <see cref="Engine"/>,
/// <see cref="Document"/>, <see cref="BaseUrl"/>, <see cref="Http"/>,
/// <see cref="Diag"/>, <see cref="Loop"/>, and use <see cref="Wrappers"/> +
/// <see cref="JintInterop"/> helpers; they never construct this directly.
/// </remarks>
public sealed class JintBackendContext
{
    /// <summary>The live Jint engine for this session (one realm per page).</summary>
    public global::Jint.Engine Engine { get; }

    /// <summary>The shared real DOM document both engines wrap.</summary>
    public Document Document { get; }

    /// <summary>Document base URL, for resolving relative resource URLs.</summary>
    public StarlingUrl BaseUrl { get; }

    /// <summary>JS-owned HTTP client for fetch / XHR / dynamic scripts.</summary>
    public Starling.Net.StarlingHttpClient Http { get; }

    /// <summary>Session diagnostics sink.</summary>
    public IDiagnostics Diag { get; }

    /// <summary>Per-engine Dom↔JS wrapper identity map + prototype slots.</summary>
    public JintDomWrapper Wrappers { get; }

    /// <summary>Simulated event loop driving timers / requestAnimationFrame / the pump.
    /// The session advances it in PumpOnce.</summary>
    public WebEventLoop Loop { get; }

    /// <summary>Layout-readback host, when the engine supplied one. Strongly
    /// typed as <see cref="ILayoutHost"/> — the shared contract now lives in the
    /// engine-neutral hosting seam, so this backend reaches it without
    /// referencing Starling.Bindings.</summary>
    public ILayoutHost? LayoutHost { get; }

    /// <summary>Layout viewport in CSS px, as supplied by the engine via
    /// <see cref="Starling.Js.Hosting.ScriptSessionOptions.ViewportWidth"/>.
    /// Surfaced to JS through <c>window.innerWidth</c>/<c>innerHeight</c> and
    /// <c>window.screen</c>. 0 means "no viewport hint" (bare unit-test
    /// contexts).</summary>
    public int ViewportWidth { get; init; }

    /// <inheritdoc cref="ViewportWidth"/>
    public int ViewportHeight { get; init; }

    /// <summary>Fetch script/module source through the session's shared fetch
    /// path (file/data/http). Used by the dynamic-script runner and the module
    /// loader.</summary>
    public Func<StarlingUrl, CancellationToken, Task<string?>> Fetch { get; }

    // Default thread-safe post queue, used when no session installs its own Post
    // hook (e.g. a bare context in a unit test). Drained on the JS thread by
    // DrainPosted().
    private readonly ConcurrentQueue<Action> _defaultPostQueue = new();

    /// <summary>
    /// Thread-safe "post to the JS thread" hook. Binding
    /// families that complete async work on a background thread (fetch/XHR HTTP
    /// completions, dynamic-script fetches) call <c>Post(action)</c> to enqueue
    /// the action for execution on the JS thread; the action is then drained and
    /// invoked on the JS thread, keeping the pump reporting "not idle" while
    /// anything is queued. Calls are safe from any thread.
    /// <para><see cref="JintScriptSession"/> installs its own hook that feeds the
    /// session pump (<see cref="JintScriptSession.PumpOnce"/>). The default
    /// enqueues onto an internal thread-safe queue drained by
    /// <see cref="DrainPosted"/> — so a bare context (no session) still defers
    /// completions to a later turn rather than running them inline, matching real
    /// async semantics.</para>
    /// </summary>
    public Action<Action> Post { get; set; }

    /// <summary>
    /// Notification that a not-yet-started <c>&lt;script&gt;</c>
    /// element just had its <c>src</c> assigned from JS, via
    /// <c>setAttribute('src', …)</c> or the <c>.src</c> IDL property. The session
    /// installs this to route into <c>JintDynamicScriptRunner.OnSrcSet</c>, which
    /// runs HTML §4.12.1 "prepare a script" (fetch + execute + fire load/error)
    /// while honouring the per-element "already started" flag. This is the Jint
    /// analogue of the Starling backend's realm-keyed <c>ScriptSrcHook</c>: the
    /// bindings observe the mutation, the session owns the fetch+run pipeline.
    /// <para>Null until a session installs it (a bare context in a unit test):
    /// the mutation then just lands as a plain attribute write.</para>
    /// </summary>
    public Action<Element>? OnScriptSrcSet { get; set; }

    /// <summary>Drain and run every action posted to the default queue, on the
    /// calling (JS) thread. No-op once a session has installed its own
    /// <see cref="Post"/> hook (those actions go to the session pump). Returns
    /// true if any action ran. A drained action may post more work; that lands on
    /// the next drain.</summary>
    public bool DrainPosted()
    {
        var ran = false;
        while (_defaultPostQueue.TryDequeue(out var action)) { ran = true; action(); }
        return ran;
    }

    /// <summary>True while the default post queue has undrained work (a bare
    /// context's "not idle" signal). Always false once a session owns Post.</summary>
    public bool HasPosted => !_defaultPostQueue.IsEmpty;

    public JintBackendContext(
        global::Jint.Engine engine,
        Document document,
        StarlingUrl baseUrl,
        Starling.Net.StarlingHttpClient http,
        IDiagnostics diag,
        WebEventLoop loop,
        ILayoutHost? layoutHost,
        Func<StarlingUrl, CancellationToken, Task<string?>> fetch)
    {
        Engine = engine;
        Document = document;
        BaseUrl = baseUrl;
        Http = http;
        Diag = diag;
        Loop = loop;
        LayoutHost = layoutHost;
        Fetch = fetch;
        Wrappers = new JintDomWrapper(this);
        Post = _defaultPostQueue.Enqueue;
    }
}
