namespace Starling.Js.RegExp;

/// <summary>The in-memory regex object the runtime holds — the result of
/// parsing+compiling a pattern + flags.</summary>
public sealed class CompiledRegex
{
    public string Source { get; }
    public RegexFlags Flags { get; }
    public RegexProgram Program { get; }
    public int CaptureCount => Program.CaptureCount;
    public System.Collections.Generic.IReadOnlyDictionary<string, int> NamedCaptures => Program.NamedCaptures;

    private readonly RegexPikeVm _vm;

    public CompiledRegex(string source, RegexFlags flags, RegexProgram program)
    {
        Source = source;
        Flags = flags;
        Program = program;
        _vm = new RegexPikeVm(program, flags);
    }

    public RegexMatch? Exec(string input, int start) => _vm.Exec(input, start);

    /// <summary>Parse + compile + wrap. Throws <see cref="RegexSyntaxException"/>
    /// on invalid pattern.</summary>
    public static CompiledRegex Compile(string pattern, RegexFlags flags)
    {
        var parser = new RegexParser(pattern, flags);
        var ast = parser.Parse();
        var compiler = new RegexCompiler(flags, parser.CaptureCount, parser.NamedCaptures);
        var program = compiler.Compile(ast);
        return new CompiledRegex(pattern, flags, program);
    }
}
