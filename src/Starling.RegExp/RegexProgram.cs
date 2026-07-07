// SPDX-License-Identifier: Apache-2.0

namespace Starling.RegExp;

/// <summary>Compiled regex bytecode: instruction array + side tables for
/// character classes and lookaround sub-programs.</summary>
public sealed class RegexProgram
{
    public IReadOnlyList<RegexInst> Code { get; }
    public IReadOnlyList<RegexCharClass> Klasses { get; }
    public IReadOnlyList<RegexProgram> Subs { get; }
    public int CaptureCount { get; }
    public IReadOnlyDictionary<string, int> NamedCaptures { get; }

    /// <summary>True for a lookbehind sub-program: instructions consume the
    /// input right-to-left starting at the assertion position.</summary>
    public bool Reversed { get; }

    /// <summary>Number of guarded (possibly-empty-body) loops; each owns one
    /// extra slot after the capture slots for its per-iteration position mark.</summary>
    public int LoopCount { get; }

    public RegexProgram(
        IReadOnlyList<RegexInst> code,
        IReadOnlyList<RegexCharClass> klasses,
        IReadOnlyList<RegexProgram> subs,
        int captureCount,
        IReadOnlyDictionary<string, int> namedCaptures,
        bool reversed = false,
        int loopCount = 0)
    {
        Code = code;
        Klasses = klasses;
        Subs = subs;
        CaptureCount = captureCount;
        NamedCaptures = namedCaptures;
        Reversed = reversed;
        LoopCount = loopCount;
    }
}
