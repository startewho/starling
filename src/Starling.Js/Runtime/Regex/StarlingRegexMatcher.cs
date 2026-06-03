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

    public bool ExecSpans(string input, int start, int[] spanBuffer, out int matchStart, out int matchEnd)
    {
        var m = _compiled.Exec(input, start);
        if (m is null)
        {
            matchStart = -1;
            matchEnd = -1;
            return false;
        }
        // The Pike VM already stores groups as (start, end) int pairs at
        // Captures[i*2], Captures[i*2+1] with -1 for a non-participating slot —
        // exactly our wire format. Copy each group 0..CaptureCount; write
        // (-1,-1) for any slot the VM left short.
        var caps = m.Captures;
        int count = _compiled.CaptureCount;
        for (int i = 0; i <= count; i++)
        {
            int si = i * 2;
            if (si + 1 < caps.Length)
            {
                int cs = caps[si];
                int ce = caps[si + 1];
                if (cs < 0 || ce < 0) { cs = -1; ce = -1; }
                spanBuffer[si] = cs;
                spanBuffer[si + 1] = ce;
            }
            else
            {
                spanBuffer[si] = -1;
                spanBuffer[si + 1] = -1;
            }
        }
        matchStart = m.Start;
        matchEnd = m.End;
        return true;
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
