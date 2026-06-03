using Starling.RegExp;

namespace Starling.Js.Runtime.Regex;

/// <summary>The regex backends a build can select between.</summary>
internal enum RegexBackendKind
{
    /// <summary>The Pike VM (<c>Starling.RegExp.CompiledRegex</c>). The default
    /// and the source of truth for JS semantics.</summary>
    Starling,

    /// <summary><c>System.Text.RegularExpressions</c> for the translatable
    /// subset, falling back to the Pike VM per pattern.</summary>
    DotNet,
}

/// <summary>
/// Reads <c>STARLING_REGEX_ENGINE</c> once and dispenses the matching regex
/// backend. Mirrors <c>Starling.Engine.JsEngineSelector</c> /
/// <c>PaintBackendSelector</c>: lazy, default <c>"starling"</c>, and a typo is
/// rejected loudly rather than silently falling back, so a bad value in an
/// Aspire manifest or CI matrix surfaces immediately.
/// </summary>
/// <remarks>
/// Unlike the JS-engine seam, this selector lives in <c>Starling.Js</c> next to
/// the compile sites because the .NET arm needs nothing beyond
/// <c>System.Text.RegularExpressions</c> and the Pike VM (already referenced).
/// </remarks>
public static class RegexBackendSelector
{
    private const string EnvVar = "STARLING_REGEX_ENGINE";

    private static readonly Lazy<RegexBackendKind> _selected = new(ReadEnv);

    internal static RegexBackendKind Selected => _selected.Value;

    private static RegexBackendKind ReadEnv() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    internal static RegexBackendKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return RegexBackendKind.Starling;

        return raw.Trim().ToLowerInvariant() switch
        {
            "starling" => RegexBackendKind.Starling,
            "dotnet" => RegexBackendKind.DotNet,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised regex engine. Allowed values: 'starling', 'dotnet'."),
        };
    }

    /// <summary>Compile a JS pattern with the process-selected backend. Throws
    /// the same <see cref="RegexSyntaxException"/> the Pike VM would on an
    /// invalid pattern, regardless of backend.</summary>
    public static IRegexMatcher Compile(string pattern, RegexFlags flags)
        => Compile(pattern, flags, Selected);

    /// <summary>Compile with an explicit backend. Exposed so parity tests can
    /// drive both backends in one process without touching the env var (which
    /// the selector reads only once via <see cref="Lazy{T}"/>).</summary>
    internal static IRegexMatcher Compile(string pattern, RegexFlags flags, RegexBackendKind kind)
    {
        // The Pike VM form is always compiled first: it is the source of truth
        // for CaptureCount, NamedCaptures, AND the JS SyntaxError early errors.
        // A throw here must surface identically for every backend.
        var pikeForm = CompiledRegex.Compile(pattern, flags);

        return kind switch
        {
            RegexBackendKind.Starling => new StarlingRegexMatcher(pikeForm),
            RegexBackendKind.DotNet => DotNetRegexMatcher.TryCreate(pikeForm)
                ?? (IRegexMatcher)new StarlingRegexMatcher(pikeForm),
            _ => throw new InvalidOperationException($"Unhandled regex engine: {kind}."),
        };
    }
}
