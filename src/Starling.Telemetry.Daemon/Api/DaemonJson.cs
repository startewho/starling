using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starling.Telemetry.Daemon.Api;

/// <summary>
/// Shared JSON options for the daemon's REST + MCP payloads. The key setting is
/// <see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/>: correlation
/// results legitimately carry NaN (a span window with no overlapping CPU sample),
/// and the default serializer throws on NaN. Named literals emit it as "NaN" so
/// a query never fails just because a correlation was unavailable.
/// </summary>
internal static class DaemonJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };
}
