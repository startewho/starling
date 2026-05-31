using System.Text.Json;
using AwesomeAssertions;
using Starling.Gui.Mcp;

namespace Starling.Gui.Tests;

/// <summary>
/// Guards the MCP browser tool catalog: the descriptor JSON must stay valid and
/// the element-targeting tools (highlight / select / focus) must be registered.
/// The MCP server re-parses <c>GetToolDescriptorsJson</c> per request, so a
/// malformed descriptor would break tools/list at runtime.
/// </summary>
[TestClass]
public sealed class BrowserToolsTests
{
    [TestMethod]
    public void Descriptors_parse_and_include_the_element_tools()
    {
        var tools = new BrowserTools(new ThrowingDispatcher());

        using var doc = JsonDocument.Parse(tools.GetToolDescriptorsJson());
        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();

        names.Should().Contain(new[] { "browser_highlight", "browser_select", "browser_focus" });

        // Every declared descriptor must be dispatchable, and vice versa.
        foreach (var name in names)
            tools.HasTool(name!).Should().BeTrue($"'{name}' is declared so it must be dispatchable");

        tools.HasTool("browser_highlight").Should().BeTrue();
        tools.HasTool("browser_select").Should().BeTrue();
        tools.HasTool("browser_focus").Should().BeTrue();
    }

    [TestMethod]
    public void Highlight_descriptor_requires_a_selector()
    {
        var tools = new BrowserTools(new ThrowingDispatcher());
        using var doc = JsonDocument.Parse(tools.GetToolDescriptorsJson());
        var highlight = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "browser_highlight");

        var required = highlight.GetProperty("inputSchema").GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().Contain("selector");
    }

    // The catalog only needs a dispatcher to construct; these tests never invoke it.
    private sealed class ThrowingDispatcher : IBrowserControlDispatcher
    {
        public Task<BrowserControlResult> NavigateAsync(string url, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> BackAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> ForwardAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> ReloadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> ScreenshotAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> InspectAsync(bool includeHtml, string? logPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> ClickAsync(double x, double y, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> MoveMouseAsync(double x, double y, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> TypeTextAsync(string text, bool submit, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> ResizeAsync(double width, double height, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> HighlightAsync(string selector, string? color, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> SelectElementAsync(string selector, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrowserControlResult> FocusElementAsync(string selector, CancellationToken ct) => throw new NotSupportedException();
    }
}
