using BenchmarkDotNet.Attributes;
using AngleSharpParser = AngleSharp.Html.Parser.HtmlParser;
using StarlingParser = Starling.Html.HtmlParser;

namespace Starling.HtmlParserBench;

// Where does Starling.Html land against a mature, pure-managed reference
// parser (AngleSharp)? Same input, same machine, reported side by side so the
// numbers compare fairly — the HTML-parsing analogue of
// bench/engine-comparison.md (Starling vs Jint).
//
// Both methods parse to a full DOM and then read TextContent, which forces a
// complete tree walk so neither engine can skip materializing the tree. That
// keeps the comparison apples-to-apples: tokenize + tree-construct + a full
// node traversal. AngleSharp's HtmlParser is reusable, so it is built once in
// GlobalSetup (matching how a long-lived consumer would use it).
[MemoryDiagnoser]
public class HtmlParserComparisonBench
{
    public enum Page
    {
        Tiny,         // ~60 B
        NginxOrg,     // ~6.4 KB real page
        Synthetic1Mb, // ~1 MB synthetic (the 04_HTML_PARSING.md budget target)
        GitHub,       // ~567 KB real page
    }

    [Params(Page.Tiny, Page.NginxOrg, Page.Synthetic1Mb, Page.GitHub)]
    public Page Fixture { get; set; }

    private string _html = string.Empty;
    private AngleSharpParser _angle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _angle = new AngleSharpParser();
        _html = Fixture switch
        {
            Page.Tiny => Fixtures.Tiny,
            Page.NginxOrg => File.ReadAllText(Fixtures.NginxHtmlPath),
            Page.GitHub => File.ReadAllText(Fixtures.GitHubHtmlPath),
            Page.Synthetic1Mb => Fixtures.SyntheticLarge(1024 * 1024),
            _ => throw new ArgumentOutOfRangeException(nameof(Fixture)),
        };
    }

    [Benchmark(Baseline = true)]
    public int Starling() => StarlingParser.Parse(_html).TextContent.Length;

    [Benchmark]
    public int AngleSharp() => _angle.ParseDocument(_html).DocumentElement.TextContent.Length;
}
