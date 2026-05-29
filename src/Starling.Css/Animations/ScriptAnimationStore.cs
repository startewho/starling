using System.Runtime.CompilerServices;
using Starling.Dom;

namespace Starling.Css.Animations;

/// <summary>
/// Stable, per-document home for script-created (Web Animations API
/// <c>element.animate</c>) animations. The <see cref="AnimationEngine"/> is
/// rebuilt on every layout pass, and page scripts run before the final
/// <c>page.Style</c> exists, so a programmatic animation cannot simply live in
/// one engine instance. This store keeps the definitions + playback state and
/// re-imports them into whichever <see cref="AnimationEngine"/> is current
/// (see <see cref="ImportInto"/>), de-duplicated per engine instance.
/// </summary>
public sealed class ScriptAnimationStore
{
    private readonly List<ScriptAnimationDef> _defs = new();
    private readonly ConditionalWeakTable<AnimationEngine, object> _importedInto = new();
    private int _nextId = 1;

    /// <summary>Record a new script animation; returns its handle id.</summary>
    public int Add(Element element, KeyframesRule rule, AnimationDeclaration decl, double startTimeMs)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(rule);
        var def = new ScriptAnimationDef(_nextId++, element, rule, decl) { StartTimeMs = startTimeMs };
        _defs.Add(def);
        return def.Id;
    }

    /// <summary>Look up a definition by handle id, or null if unknown.</summary>
    public ScriptAnimationDef? Get(int id)
    {
        foreach (var d in _defs)
            if (d.Id == id) return d;
        return null;
    }

    /// <summary>
    /// Re-apply every live (non-cancelled) definition into <paramref name="engine"/>.
    /// Idempotent per engine instance: a given <see cref="AnimationEngine"/> is
    /// imported into at most once, so calling this each frame is cheap.
    /// </summary>
    public void ImportInto(AnimationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_importedInto.TryGetValue(engine, out _)) return;
        _importedInto.Add(engine, this);
        foreach (var d in _defs)
        {
            if (d.Canceled) continue;
            var inst = engine.AddScriptAnimation(d.Element, d.Decl, d.Rule, d.StartTimeMs);
            d.Bind(inst);
            if (d.Paused)
            {
                inst.ScriptSetCurrentTime(d.HoldTimeMs);
                inst.ScriptPause();
            }
        }
    }
}

/// <summary>One script animation's definition + mutable playback state. Times
/// are on the document timeline (shared with the <see cref="AnimationEngine"/>
/// clock / <c>performance.now()</c>).</summary>
public sealed class ScriptAnimationDef(int id, Element element, KeyframesRule rule, AnimationDeclaration decl)
{
    public int Id { get; } = id;
    public Element Element { get; } = element;
    public KeyframesRule Rule { get; } = rule;
    public AnimationDeclaration Decl { get; } = decl;

    /// <summary>Timeline ms at which the playback head sits at currentTime 0.</summary>
    public double StartTimeMs { get; set; }
    /// <summary>Frozen currentTime while paused.</summary>
    public double HoldTimeMs { get; set; }
    public bool Paused { get; set; }
    public bool Canceled { get; set; }

    // The live instance in the current engine, if imported. Control operations
    // mutate both this def (so re-imports inherit state) and the live instance.
    private AnimationInstance? _live;
    public void Bind(AnimationInstance inst) => _live = inst;

    public double DelayMs => Decl.DelayMs;

    /// <summary>currentTime on the timeline at clock <paramref name="nowMs"/>.</summary>
    public double CurrentTime(double nowMs) => Paused ? HoldTimeMs : Math.Max(0, nowMs - StartTimeMs);

    public void Pause(double nowMs)
    {
        if (Paused) return;
        HoldTimeMs = Math.Max(0, nowMs - StartTimeMs);
        Paused = true;
        _live?.ScriptPause();
    }

    public void Play(double nowMs)
    {
        if (Canceled) { Canceled = false; StartTimeMs = nowMs; HoldTimeMs = 0; }
        if (Paused) { StartTimeMs = nowMs - HoldTimeMs; Paused = false; }
        _live?.ScriptPlay();
    }

    public void Cancel()
    {
        Canceled = true;
        _live?.ScriptCancel();
    }

    public void Finish(double activeDurationMs)
    {
        // Land (and freeze) the head at the end of the active duration.
        HoldTimeMs = activeDurationMs;
        Paused = true;
        _live?.ScriptFinish();
    }

    public void SetCurrentTime(double ms, double nowMs)
    {
        if (Paused) HoldTimeMs = ms;
        else StartTimeMs = nowMs - ms;
        _live?.ScriptSetCurrentTime(ms);
    }
}
