namespace Starling.Bindings;

/// <summary>
/// Compatibility marker kept for older probes that still look for a bindings
/// placeholder. The Web API bindings now live in this assembly.
/// </summary>
public static class PlaceholderNote
{
    public const string Message = "Starling.Bindings is implemented. See browser-plan/10_WEB_APIS.md.";
}
