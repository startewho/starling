using Starling.RegExp;

namespace Starling.Js.Runtime.Regex;

/// <summary>
/// The default backend: a thin 1:1 wrapper over the Pike VM's
/// <see cref="CompiledRegex"/>. Forwarding only — zero behavior change. This is
/// also the per-pattern fallback target for the .NET backend.
/// </summary>
public sealed class StarlingRegexMatcher : IRegexMatcher
{
    private readonly CompiledRegex _compiled;

    public StarlingRegexMatcher(CompiledRegex compiled)
    {
        _compiled = compiled;
    }

    /// <summary>The wrapped Pike VM form (the source of truth for capture
    /// metadata and JS early errors).</summary>
    internal CompiledRegex Compiled => _compiled;

    public string Source => _compiled.Source;
    public RegexFlags Flags => _compiled.Flags;
    public int CaptureCount => _compiled.CaptureCount;
    public IReadOnlyDictionary<string, int> NamedCaptures => _compiled.NamedCaptures;

    public IRegexMatch? Exec(string input, int start)
    {
        var m = _compiled.Exec(input, start);
        return m is null ? null : new StarlingRegexMatch(m);
    }
}

/// <summary>1:1 wrapper over a Pike VM <see cref="RegexMatch"/>.</summary>
public sealed class StarlingRegexMatch : IRegexMatch
{
    private readonly RegexMatch _match;

    public StarlingRegexMatch(RegexMatch match)
    {
        _match = match;
    }

    public int Start => _match.Start;
    public int End => _match.End;
    public string Input => _match.Input;
    public string? Group(int i) => _match.Group(i);
    public (int Start, int End)? GroupSpan(int i) => _match.GroupSpan(i);
}
