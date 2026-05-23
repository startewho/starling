using System.Runtime.CompilerServices;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Hosting;
using StarlingUrl = global::Starling.Url.Url;
using StarlingUrlParser = global::Starling.Url.UrlParser;

namespace Starling.Bindings.Jint;

/// <summary>
/// Jint-backend half of HTML §4.12.1 "prepare a script" for the dynamic
/// (<c>src</c>-set-from-JS / runtime-injected external) path. Mirrors
/// <c>Starling.Bindings/Backend/StarlingDynamicScriptRunner.cs</c>: when a
/// not-yet-started <c>&lt;script&gt;</c> gets a <c>src</c> (or a script-inserted
/// external script is connected at runtime), fetch the URL via
/// <see cref="JintBackendContext.Fetch"/> on a background task, then run it on
/// the JS thread (through <see cref="JintBackendContext.Post"/>, drained by
/// <see cref="JintScriptSession.PumpOnce"/>) via the session's
/// <c>RunClassicScript</c>, and fire <c>load</c>/<c>error</c>.
/// </summary>
/// <remarks>
/// The "already started" flag (HTML §4.12.1) is tracked per element so the
/// parser-batch and any dynamically-run script never run twice
/// (<see cref="MarkStarted"/> is wired to <c>IScriptSession.MarkScriptStarted</c>).
/// In-flight fetches are counted so <see cref="HasPending"/> keeps
/// <c>PumpOnce</c> reporting "not idle" until every dynamic script settles.
/// </remarks>
internal sealed class JintDynamicScriptRunner
{
    private readonly IDiagnostics _diag;
    private readonly StarlingUrl _baseUrl;
    private readonly Func<StarlingUrl, CancellationToken, Task<string?>> _fetch;
    private readonly Action<string, string> _runClassic;   // (source, label) on the JS thread
    private readonly Action<Action> _post;                   // marshal back to the JS thread

    private readonly ConditionalWeakTable<Element, object> _started = new();
    private static readonly object Marker = new();

    // Number of dynamic scripts whose fetch+run hasn't completed. Read by the
    // pump (via HasPending) so it stays non-idle; mutated only on the JS thread
    // (OnSrcSet runs synchronously from a JS attribute write; the decrement runs
    // inside the posted completion, which the pump invokes on the JS thread).
    private int _inFlight;

    public JintDynamicScriptRunner(
        IDiagnostics diag, StarlingUrl baseUrl,
        Func<StarlingUrl, CancellationToken, Task<string?>> fetch,
        Action<string, string> runClassic,
        Action<Action> post)
    {
        _diag = diag;
        _baseUrl = baseUrl;
        _fetch = fetch;
        _runClassic = runClassic;
        _post = post;
    }

    /// <summary>Mark a script element "already started" so a later <c>src</c>
    /// write does not re-run it. Called for every parser-batch script.</summary>
    public void MarkStarted(Element script) => _started.AddOrUpdate(script, Marker);

    /// <summary>True while at least one dynamic script is still fetching/running,
    /// so <see cref="JintScriptSession.PumpOnce"/> stays non-idle.</summary>
    public bool HasPending => Volatile.Read(ref _inFlight) > 0;

    /// <summary>HTML §4.12.1 entry for a script whose <c>src</c> was just set
    /// from JS (or a script-inserted external script connected at runtime).
    /// Idempotent: a script already started is ignored. Runs synchronously on the
    /// JS thread, so it only kicks off the background fetch.</summary>
    public void OnSrcSet(Element script)
    {
        if (_started.TryGetValue(script, out _)) return;
        _started.AddOrUpdate(script, Marker);
        Begin(script);
    }

    /// <summary>Queue a script-inserted external <c>&lt;script&gt;</c>.
    /// Idempotent (same start-flag bookkeeping as <see cref="OnSrcSet"/>).</summary>
    public void EnqueueInjectedExternal(Element script) => OnSrcSet(script);

    private void Begin(Element script)
    {
        var src = script.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            FireEvent(script, "error");
            return;
        }

        var absolute = ResolveAbsolute(src, _baseUrl);
        if (absolute is null)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve dynamic <script src='{src}'>");
            FireEvent(script, "error");
            return;
        }

        Interlocked.Increment(ref _inFlight);
        _ = Task.Run(async () =>
        {
            string? source;
            try
            {
                source = await _fetch(absolute, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Dynamic script fetch failed {absolute}: {ex.Message}");
                source = null;
            }

            // Marshal the run + load/error firing back onto the JS thread; the
            // pump invokes this and decrements the in-flight count exactly once.
            _post(() =>
            {
                try { RunOne(script, absolute, source); }
                finally { Interlocked.Decrement(ref _inFlight); }
            });
        });
    }

    private void RunOne(Element script, StarlingUrl absolute, string? source)
    {
        if (source is null)
        {
            FireEvent(script, "error");
            return;
        }

        var label = absolute.ToString();
        var ranOk = false;
        try
        {
            _runClassic(source, label);
            _diag.Counter("engine.script.dynamic.ok", 1);
            ranOk = true;
        }
        catch (ScriptThrow ex)
        {
            _diag.Counter("engine.script.dynamic.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js",
                $"Uncaught dynamic script error ({label}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _diag.Counter("engine.script.dynamic.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js",
                $"Dynamic script compile/run failure ({label}): {ex.Message}");
        }

        FireEvent(script, ranOk ? "load" : "error");
    }

    private void FireEvent(Element script, string type)
    {
        try
        {
            script.DispatchEvent(new Event(type));
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "engine.js",
                $"Dynamic script '{type}' handler threw: {ex.Message}");
        }
    }

    private static StarlingUrl? ResolveAbsolute(string href, StarlingUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? StarlingUrlParser.Parse(href)
            : StarlingUrlParser.Parse(href, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }
}
