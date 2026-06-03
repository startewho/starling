using Starling.RegExp;

namespace Starling.Js.Runtime.Regex;

/// <summary>
/// A compiled regex behind a backend-agnostic seam. The runtime holds one of
/// these on every <see cref="JsRegExp"/>; the concrete instance is chosen by
/// <see cref="RegexBackendSelector"/> (Pike VM or .NET delegation). The shape
/// mirrors the Pike VM's <c>CompiledRegex</c> so the intrinsics (exec/test,
/// Symbol.* protocols, the match-array builder) read it the same way regardless
/// of backend.
/// </summary>
public interface IRegexMatcher
{
    /// <summary>The original JS pattern source (no slashes).</summary>
    string Source { get; }

    /// <summary>The parsed flag bits.</summary>
    RegexFlags Flags { get; }

    /// <summary>Number of capturing groups (excludes group 0, the whole match).
    /// This is the Pike VM's count — the source of truth for both backends.</summary>
    int CaptureCount { get; }

    /// <summary>Named-capture lookup: group name → JS group index (1-based,
    /// left-to-right). The Pike VM's view, used identically by both backends.</summary>
    IReadOnlyDictionary<string, int> NamedCaptures { get; }

    /// <summary>Run the regex against <paramref name="input"/> starting the scan
    /// at <paramref name="start"/>. Returns <c>null</c> on no match. Matches the
    /// Pike VM contract: a non-sticky run scans forward from <paramref name="start"/>.</summary>
    IRegexMatch? Exec(string input, int start);
}

/// <summary>A successful match from an <see cref="IRegexMatcher"/>.</summary>
public interface IRegexMatch
{
    /// <summary>Index of the first matched character.</summary>
    int Start { get; }

    /// <summary>Index one past the last matched character.</summary>
    int End { get; }

    /// <summary>The input string the match ran against.</summary>
    string Input { get; }

    /// <summary>Group <paramref name="i"/>'s text. Index 0 is the whole match.
    /// Returns <c>null</c> for a non-participating group.</summary>
    string? Group(int i);

    /// <summary>Group <paramref name="i"/>'s (start, end) span, or <c>null</c>
    /// for a non-participating group. Index 0 is the whole match.</summary>
    (int Start, int End)? GroupSpan(int i);
}
