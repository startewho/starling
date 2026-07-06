using Starling.Bindings.Backend;
using Starling.Js.Hosting;

namespace Starling.Engine;

internal enum JsEngineKind
{
    Starling,
}

/// <summary>
/// Reads <c>STARLING_JS_ENGINE</c> once and dispenses the matching
/// <see cref="IScriptEngineFactory"/>. Mirrors
/// <c>Starling.Paint.Backend.PaintBackendSelector</c>: lazy, default
/// <c>"starling"</c>, and a typo is rejected loudly rather than silently falling
/// back, so a bad value in an Aspire manifest or CI matrix surfaces immediately.
/// </summary>
/// <remarks>
/// The selector lives in <c>Starling.Engine</c> (not <c>Starling.Js.Hosting</c>)
/// on purpose: the seam project must not reference the backend, so the concrete
/// factory wiring belongs here, where the backend assembly is referenced.
/// </remarks>
internal static class JsEngineSelector
{
    private const string EnvVar = "STARLING_JS_ENGINE";

    private static readonly Lazy<JsEngineKind> _selected = new(ReadEnv);

    internal static JsEngineKind Selected => _selected.Value;

    private static JsEngineKind ReadEnv() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    internal static JsEngineKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsEngineKind.Starling;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "starling" => JsEngineKind.Starling,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised JS engine. Allowed values: 'starling'."),
        };
    }

    /// <summary>The chosen factory, constructed once. The backend is a
    /// stateless factory; sessions hold the per-page state.</summary>
    internal static IScriptEngineFactory Factory => _factory.Value;

    private static readonly Lazy<IScriptEngineFactory> _factory =
        new(static () => new StarlingScriptEngineFactory());
}
