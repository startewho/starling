namespace Starling.Css;

/// <summary>
/// Module status note. CSS tokenizer/parser, selectors, cascade, animations,
/// transitions, and many property/value parsers are active. Some specs still
/// parse only or degrade until layout/paint support lands.
/// </summary>
public static class PlaceholderNote
{
    public const string Message =
        "Starling.Css: tokenizer, parser, selectors, cascade, animations, and transitions ready. " +
        "Some specs still parse only. See browser-plan/06_CSS.md.";
}
