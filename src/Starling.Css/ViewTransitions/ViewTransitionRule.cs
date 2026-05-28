namespace Starling.Css.ViewTransitions;

/// <summary>
/// A parsed <c>@view-transition</c> rule per
/// <see href="https://www.w3.org/TR/css-view-transitions-1/#view-transition-rule">CSS View Transitions Level 1 §2.1</see>.
/// </summary>
/// <param name="Navigation">The <c>navigation</c> descriptor: <c>auto</c> or <c>none</c>
/// (initial <c>none</c>) — whether a same-origin navigation triggers a transition.</param>
/// <param name="Types">The <c>types</c> descriptor: the active view-transition type
/// names, or empty for <c>none</c>.</param>
public sealed record ViewTransitionRule(
    string Navigation,
    IReadOnlyList<string> Types);
