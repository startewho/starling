using Starling.Bindings;
using Starling.Css.Animations;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Engine;

/// <summary>
/// Engine implementation of the Web Animations API host seam
/// (<see cref="IAnimationHost"/>). Translates the binding's neutral keyframe /
/// timing payloads into the CSS animation model and records them in a stable,
/// per-document <see cref="ScriptAnimationStore"/>; the store is re-imported
/// into whichever <see cref="AnimationEngine"/> is current (the engine is
/// rebuilt per layout pass) so script animations render through the same
/// compositor as declarative <c>@keyframes</c>.
/// </summary>
internal sealed class EngineAnimationHost : IAnimationHost
{
    private readonly ScriptAnimationStore _store;
    private readonly Func<double> _clock;
    private int _counter;

    public EngineAnimationHost(ScriptAnimationStore store, Func<double>? clock = null)
    {
        _store = store;
        _clock = clock ?? (static () => 0);
    }

    public double TimelineNow => _clock();

    public int Animate(Element element, IReadOnlyList<AnimationKeyframeSpec> keyframes, AnimationEffectTimingSpec timing)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(keyframes);
        ArgumentNullException.ThrowIfNull(timing);

        var rule = BuildKeyframesRule(keyframes);
        var decl = BuildDeclaration(rule.Name, timing);
        return _store.Add(element, rule, decl, TimelineNow);
    }

    public void Play(int id) => _store.Get(id)?.Play(TimelineNow);
    public void Pause(int id) => _store.Get(id)?.Pause(TimelineNow);
    public void Cancel(int id) => _store.Get(id)?.Cancel();
    public void Finish(int id)
    {
        if (_store.Get(id) is { } d) d.Finish(ActiveDurationMs(d.Decl));
    }

    public double CurrentTime(int id) => _store.Get(id)?.CurrentTime(TimelineNow) ?? 0;
    public void SetCurrentTime(int id, double ms) => _store.Get(id)?.SetCurrentTime(ms, TimelineNow);

    public string PlayState(int id)
    {
        if (_store.Get(id) is not { } d) return "idle";
        if (d.Canceled) return "idle";
        if (d.Paused) return "paused";
        var active = ActiveDurationMs(d.Decl);
        if (!double.IsInfinity(d.Decl.IterationCount) && CurrentTime(id) >= d.Decl.DelayMs + active)
            return "finished";
        return "running";
    }

    // ----- translation -------------------------------------------------------

    private KeyframesRule BuildKeyframesRule(IReadOnlyList<AnimationKeyframeSpec> keyframes)
    {
        var name = $"__waapi_{++_counter}";
        var frames = new List<Keyframe>(keyframes.Count);
        foreach (var kf in keyframes)
        {
            var decls = new List<KeyframeDeclaration>(kf.Declarations.Count);
            foreach (var (prop, text) in kf.Declarations)
            {
                if (ParseValue($"{prop}: {text}") is { } parsed)
                    decls.Add(new KeyframeDeclaration(prop.ToLowerInvariant(), parsed));
            }
            frames.Add(new Keyframe(kf.Offset, decls));
        }
        // Keyframes must be offset-sorted for the sampler's bracketing.
        frames.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
        return new KeyframesRule(name, frames);
    }

    /// <summary>Parse a single "<c>property: value</c>" declaration string into
    /// its <see cref="CssValue"/>, or null if it doesn't parse.</summary>
    private static CssValue? ParseValue(string declarationText)
    {
        try
        {
            var decls = new CssParser(declarationText).ParseDeclarationList();
            return decls.Count > 0 ? CssValueParser.Parse(decls[0].Value) : null;
        }
        catch { return null; }
    }

    private static AnimationDeclaration BuildDeclaration(string name, AnimationEffectTimingSpec t)
    {
        var easing = ParseValue($"easing: {t.Easing}") is { } e
            ? TimingFunction.FromCss(e)
            : TimingFunction.Linear;

        return new AnimationDeclaration(
            name,
            t.DurationMs,
            t.DelayMs,
            easing,
            double.IsNaN(t.Iterations) ? 1 : t.Iterations,
            MapDirection(t.Direction),
            MapFill(t.Fill),
            AnimationPlayState.Running);
    }

    private static double ActiveDurationMs(AnimationDeclaration d)
        => d.DurationMs * (double.IsInfinity(d.IterationCount) ? 1 : Math.Max(0, d.IterationCount));

    private static AnimationDirection MapDirection(string s) => s switch
    {
        "reverse" => AnimationDirection.Reverse,
        "alternate" => AnimationDirection.Alternate,
        "alternate-reverse" => AnimationDirection.AlternateReverse,
        _ => AnimationDirection.Normal,
    };

    private static AnimationFillMode MapFill(string s) => s switch
    {
        "forwards" => AnimationFillMode.Forwards,
        "backwards" => AnimationFillMode.Backwards,
        "both" => AnimationFillMode.Both,
        _ => AnimationFillMode.None,
    };
}
