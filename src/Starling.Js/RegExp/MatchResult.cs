namespace Tessera.Js.RegExp;

/// <summary>Result of a successful regex match. Captures are stored as
/// (start, end) pairs in <see cref="Captures"/>; -1 means "not captured on this
/// path". The full match is at index 0.</summary>
public sealed class RegexMatch
{
    public int Start { get; }
    public int End { get; }
    public int[] Captures { get; }
    public string Input { get; }

    public RegexMatch(string input, int start, int end, int[] captures)
    {
        Input = input;
        Start = start;
        End = end;
        Captures = captures;
    }

    /// <summary>Group <paramref name="i"/>'s text (null if not captured).</summary>
    public string? Group(int i)
    {
        if (i < 0 || i * 2 + 1 >= Captures.Length) return null;
        var s = Captures[i * 2];
        var e = Captures[i * 2 + 1];
        if (s < 0 || e < 0) return null;
        return Input.Substring(s, e - s);
    }

    public (int Start, int End)? GroupSpan(int i)
    {
        if (i < 0 || i * 2 + 1 >= Captures.Length) return null;
        var s = Captures[i * 2];
        var e = Captures[i * 2 + 1];
        if (s < 0 || e < 0) return null;
        return (s, e);
    }
}
