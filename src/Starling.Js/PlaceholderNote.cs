namespace Starling.Js;

/// <summary>
/// Compatibility marker kept for older probes that still look for a JS
/// placeholder. The Starling JS engine now lives in this assembly.
/// </summary>
public static class PlaceholderNote
{
    public const string Message = "Starling.Js is implemented. See browser-plan/09_JS_ENGINE.md.";
}
