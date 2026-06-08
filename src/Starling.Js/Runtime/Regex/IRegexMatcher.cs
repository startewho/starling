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

    /// <summary>Non-allocating exec: run the regex against <paramref name="input"/>
    /// from <paramref name="start"/> and, on a match, fill
    /// <paramref name="spanBuffer"/> with (start, end) int pairs for groups
    /// 0..<see cref="CaptureCount"/> (group 0 is the whole match). A
    /// non-participating group writes (-1, -1). The buffer length must be at
    /// least 2*(<see cref="CaptureCount"/>+1); the caller owns it and may pool
    /// it across iterations. Returns whether a match was found. Allocates no
    /// substrings — group text is produced by the caller from the spans.
    /// Honors the same sticky and start &gt; input.Length guards as
    /// <see cref="Exec(string,int)"/>, and routes group indices through the same
    /// JS↔.NET resolution so numbering stays JS-correct.</summary>
    bool ExecSpans(string input, int start, int[] spanBuffer, out int matchStart, out int matchEnd);

    /// <summary>Fast whole-string replace of a LITERAL replacement — the caller
    /// guarantees <paramref name="literalReplacement"/> contains no <c>$</c>
    /// substitution tokens — for a backend that can do it in a single optimized
    /// pass (the same path Jint takes). Returns <c>null</c> when the backend has
    /// no fast path, so the caller falls back to a per-match scan.
    /// <paramref name="global"/> false replaces only the first match.</summary>
    string? TryReplaceLiteral(string input, string literalReplacement, bool global);
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
