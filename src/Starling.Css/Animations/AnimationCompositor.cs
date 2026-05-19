using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;

namespace Tessera.Css.Animations;

/// <summary>
/// Static helpers that turn cascaded Animation* longhand values into a flat
/// list of <see cref="AnimationDeclaration"/> objects suitable for feeding to
/// <see cref="AnimationEngine.OnAnimationsCascaded"/>.
/// </summary>
/// <remarks>
/// Per CSS Animations 1 §4.1, the parallel longhand lists are zipped to the
/// length of <c>animation-name</c>. Shorter parallel lists cycle from the
/// start so e.g. <c>animation-name: a, b, c; animation-duration: 1s, 2s</c>
/// gives durations <c>1s, 2s, 1s</c>.
/// </remarks>
public static class AnimationCompositor
{
    public static IReadOnlyList<AnimationDeclaration> BuildDeclarations(ComputedStyle style)
    {
        var names = AsList(style.Get(PropertyId.AnimationName));
        if (names.Count == 0)
            return Array.Empty<AnimationDeclaration>();

        var durations = AsList(style.Get(PropertyId.AnimationDuration));
        var delays = AsList(style.Get(PropertyId.AnimationDelay));
        var timings = AsList(style.Get(PropertyId.AnimationTimingFunction));
        var iterations = AsList(style.Get(PropertyId.AnimationIterationCount));
        var directions = AsList(style.Get(PropertyId.AnimationDirection));
        var fills = AsList(style.Get(PropertyId.AnimationFillMode));
        var playStates = AsList(style.Get(PropertyId.AnimationPlayState));

        var result = new List<AnimationDeclaration>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var name = NameOf(names[i]);
            if (name is null or "none")
                continue;
            result.Add(new AnimationDeclaration(
                name,
                TimeMs(Pick(durations, i)),
                TimeMs(Pick(delays, i)),
                ParseTimingFunction(Pick(timings, i)),
                IterationCount(Pick(iterations, i)),
                Direction(Pick(directions, i)),
                FillMode(Pick(fills, i)),
                PlayState(Pick(playStates, i))));
        }
        return result;
    }

    private static IReadOnlyList<CssValue> AsList(CssValue value)
        => value is CssValueList list
            ? list.Values.Where(v => v is not CssKeyword { Name: "" }).ToList()
            : new[] { value };

    private static CssValue Pick(IReadOnlyList<CssValue> list, int i)
        => list.Count == 0 ? new CssKeyword("initial") : list[i % list.Count];

    private static string? NameOf(CssValue v) => v switch
    {
        CssKeyword k => k.Name,
        CssString s => s.Value,
        _ => null,
    };

    private static double TimeMs(CssValue v) => v switch
    {
        CssTime t => t.InSeconds * 1000d,
        CssDimension d when d.Unit == "s" => d.Value * 1000d,
        CssDimension d when d.Unit == "ms" => d.Value,
        CssNumber n => n.Value, // tolerate unitless 0
        _ => 0d,
    };

    private static double IterationCount(CssValue v) => v switch
    {
        CssNumber n => n.Value,
        CssKeyword { Name: "infinite" } => double.PositiveInfinity,
        _ => 1d,
    };

    private static AnimationDirection Direction(CssValue v) => v switch
    {
        CssKeyword { Name: "reverse" } => AnimationDirection.Reverse,
        CssKeyword { Name: "alternate" } => AnimationDirection.Alternate,
        CssKeyword { Name: "alternate-reverse" } => AnimationDirection.AlternateReverse,
        _ => AnimationDirection.Normal,
    };

    private static AnimationFillMode FillMode(CssValue v) => v switch
    {
        CssKeyword { Name: "forwards" } => AnimationFillMode.Forwards,
        CssKeyword { Name: "backwards" } => AnimationFillMode.Backwards,
        CssKeyword { Name: "both" } => AnimationFillMode.Both,
        _ => AnimationFillMode.None,
    };

    private static AnimationPlayState PlayState(CssValue v) => v switch
    {
        CssKeyword { Name: "paused" } => AnimationPlayState.Paused,
        _ => AnimationPlayState.Running,
    };

    private static TimingFunction ParseTimingFunction(CssValue v) => TimingFunction.FromCss(v);

    private static double AsDouble(CssValue v) => v switch
    {
        CssNumber n => n.Value,
        CssPercentage p => p.Value / 100d,
        _ => 0d,
    };
}
