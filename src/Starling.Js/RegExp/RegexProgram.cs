namespace Starling.Js.RegExp;

/// <summary>Compiled regex bytecode: instruction array + side tables for
/// character classes and lookaround sub-programs.</summary>
public sealed class RegexProgram
{
    public IReadOnlyList<RegexInst> Code { get; }
    public IReadOnlyList<RegexCharClass> Klasses { get; }
    public IReadOnlyList<RegexProgram> Subs { get; }
    public int CaptureCount { get; }
    public IReadOnlyDictionary<string, int> NamedCaptures { get; }

    public RegexProgram(
        IReadOnlyList<RegexInst> code,
        IReadOnlyList<RegexCharClass> klasses,
        IReadOnlyList<RegexProgram> subs,
        int captureCount,
        IReadOnlyDictionary<string, int> namedCaptures)
    {
        Code = code;
        Klasses = klasses;
        Subs = subs;
        CaptureCount = captureCount;
        NamedCaptures = namedCaptures;
    }
}
