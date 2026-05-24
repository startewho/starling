using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Xunit;
using Starling.Common.Diagnostics;
using Starling.Engine;
using Starling.Gui.Controls;
using Starling.Gui.Theme;
using DomElement = Starling.Dom.Element;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Coverage for the synthetic-input surface the MCP server drives —
/// <see cref="WebviewPanel.ClickAt"/> / <see cref="WebviewPanel.TypeText"/> /
/// <see cref="WebviewPanel.MoveTo"/> — exercised through a real
/// <see cref="WebviewPanel"/> at document-space coordinates (the same space
/// browser_screenshot captures). These are the paths browser_click /
/// browser_type / browser_move bottom out in.
/// </summary>
/// <remarks>
/// Uses self-contained file:// fixtures (never the shared testdata todo app): the
/// JS-backed sites persist to a process-wide localStorage, so two tests touching
/// the same app would contaminate each other's starting state.
/// </remarks>
public class ProgrammaticInputTests
{
    // A form whose submit handler appends the typed value as a list item, with no
    // persistence — so each test starts from a clean DOM.
    private const string FormFixture =
        "<!doctype html><html><body>"
        + "<form id=\"f\"><input id=\"q\" type=\"text\"><button type=\"submit\">go</button></form>"
        + "<ul id=\"out\"></ul>"
        + "<script>document.getElementById('f').addEventListener('submit', function(e){"
        + "  e.preventDefault();"
        + "  var li=document.createElement('li'); li.textContent=document.getElementById('q').value;"
        + "  document.getElementById('out').appendChild(li); });</script>"
        + "</body></html>";

    [AvaloniaFact]
    public async Task ClickAt_focuses_an_input_and_TypeText_sets_its_value()
    {
        // No JS needed: focus + value editing work on any backend.
        var (engine, page, input) = await LoadAsync(
            "<!doctype html><html><body><input type=\"text\" id=\"q\"></body></html>", "q");
        var (window, panel) = ShowPanel(engine, page);
        try
        {
            var focus = Click(panel, CenterOf(CurrentRoot(panel), input)!.Value);
            focus.Ok.Should().BeTrue();
            page.Document.FocusedElement.Should().BeSameAs(input, "the click should focus the field");

            var typed = panel.TypeText("hello");
            typed.Ok.Should().BeTrue();
            input.InputValue.Should().Be("hello");
        }
        finally { Teardown(window, panel); }
    }

    [AvaloniaFact]
    public void TypeText_without_a_focused_field_reports_a_failure()
    {
        var engine = new StarlingEngine();
        using var panel = new WebviewPanel(
            new ThemeManager(), NoopDiagnostics.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)));

        var r = panel.TypeText("hello");
        r.Ok.Should().BeFalse();
        r.Detail.Should().Contain("no page");
    }

    [AvaloniaFact]
    public async Task ClickAt_and_TypeText_submit_a_form_via_the_Add_button()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("STARLING_JS_ENGINE") == "jint",
            "Pins the Jint backend; run with STARLING_JS_ENGINE=jint (e.g. the Jint CI arm).");

        var (engine, page) = await LoadInteractiveAsync(FormFixture);
        var doc = page.Document;
        var input = doc.GetElementById("q")!;
        var output = doc.GetElementById("out")!;
        var button = First(doc, "button")!;
        var (window, panel) = ShowPanel(engine, page);
        try
        {
            panel.BindLiveScripting();
            page.Scripting.Should().NotBeNull("the interactively-loaded form stays live");

            // Locate elements on the live root each time — typing/submitting relays
            // out and swaps the current LaidOutPage.
            Click(panel, CenterOf(CurrentRoot(panel), input)!.Value);
            panel.TypeText("Buy milk").Detail.Should().Contain("Buy milk");
            input.InputValue.Should().Be("Buy milk", "typing writes through to the field's live value");

            Click(panel, CenterOf(CurrentRoot(panel), button)!.Value);

            CountChildren(output, "li").Should().Be(1, "clicking the submit button runs the form handler");
            output.FirstChild!.TextContent.Should().Be("Buy milk");
        }
        finally { Teardown(window, panel); }
    }

    [AvaloniaFact]
    public async Task TypeText_with_submit_presses_Enter_to_submit_the_form()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("STARLING_JS_ENGINE") == "jint",
            "Pins the Jint backend; run with STARLING_JS_ENGINE=jint (e.g. the Jint CI arm).");

        var (engine, page) = await LoadInteractiveAsync(FormFixture);
        var doc = page.Document;
        var input = doc.GetElementById("q")!;
        var output = doc.GetElementById("out")!;
        var (window, panel) = ShowPanel(engine, page);
        try
        {
            panel.BindLiveScripting();

            Click(panel, CenterOf(CurrentRoot(panel), input)!.Value);
            var r = panel.TypeText("Walk dog", submit: true);

            r.Ok.Should().BeTrue();
            CountChildren(output, "li").Should().Be(1, "submit=true presses Enter, submitting the form");
            output.FirstChild!.TextContent.Should().Be("Walk dog");
        }
        finally { Teardown(window, panel); }
    }

    [AvaloniaFact]
    public async Task MoveTo_dispatches_DOM_mouseover_to_the_hovered_element()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("STARLING_JS_ENGINE") == "jint",
            "Pins the Jint backend; run with STARLING_JS_ENGINE=jint (e.g. the Jint CI arm).");

        var (engine, page) = await LoadInteractiveAsync(
            "<!doctype html><html><body>"
            + "<div id=\"target\" style=\"width:200px;height:80px\">hover me</div>"
            + "<script>document.getElementById('target')"
            + ".addEventListener('mouseover', function(){ this.setAttribute('data-hovered','yes'); });</script>"
            + "</body></html>");
        var target = page.Document.GetElementById("target")!;
        var (window, panel) = ShowPanel(engine, page);
        try
        {
            panel.BindLiveScripting();
            target.GetAttribute("data-hovered").Should().BeNull("the handler hasn't fired yet");

            var r = Move(panel, CenterOf(CurrentRoot(panel), target)!.Value);
            r.Ok.Should().BeTrue();
            r.Detail.Should().Contain("moved over");
            target.GetAttribute("data-hovered").Should().Be("yes", "moving over the element fires mouseover");
        }
        finally { Teardown(window, panel); }
    }

    // ---------------------------------------------------------------- helpers

    private static InputResult Click(WebviewPanel panel, (double X, double Y) p) => panel.ClickAt(p.X, p.Y);
    private static InputResult Move(WebviewPanel panel, (double X, double Y) p) => panel.MoveTo(p.X, p.Y);

    private static (Window Window, WebviewPanel Panel) ShowPanel(StarlingEngine engine, LaidOutPage page)
    {
        var panel = new WebviewPanel(
            new ThemeManager(), NoopDiagnostics.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)));
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame(); // force a measure/arrange so the canvas has bounds
        return (window, panel);
    }

    // Close the window before disposing the panel: the panel frees its page bitmap
    // on Dispose, so a window left shown would touch a disposed bitmap in the
    // session's teardown layout pass.
    private static void Teardown(Window window, WebviewPanel panel)
    {
        window.Close();
        panel.Dispose();
    }

    private static async Task<(StarlingEngine Engine, LaidOutPage Page, DomElement Element)> LoadAsync(
        string html, string id)
    {
        var (engine, page) = await LoadStaticAsync(html);
        return (engine, page, page.Document.GetElementById(id)!);
    }

    private static async Task<(StarlingEngine Engine, LaidOutPage Page)> LoadStaticAsync(string html)
    {
        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            WriteFixture(html), new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
            CancellationToken.None);
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return (engine, result.Value);
    }

    private static async Task<(StarlingEngine Engine, LaidOutPage Page)> LoadInteractiveAsync(string html)
    {
        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            WriteFixture(html), new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
            CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return (engine, result.Value);
    }

    private static string WriteFixture(string html)
    {
        var path = Path.Combine(Path.GetTempPath(), $"starling-input-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html);
        return "file://" + path.Replace('\\', '/');
    }

    private static Starling.Layout.Box.BlockBox CurrentRoot(WebviewPanel panel)
    {
        var page = (LaidOutPage)typeof(WebviewPanel)
            .GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
        return page.Root;
    }

    private static int CountChildren(DomElement parent, string localName)
    {
        var n = 0;
        for (var c = parent.FirstChild; c is not null; c = c.NextSibling)
            if (c is DomElement e && e.LocalName == localName) n++;
        return n;
    }

    private static DomElement? First(Starling.Dom.Node root, string localName)
    {
        foreach (var e in root.DescendantElements())
            if (e.LocalName == localName) return e;
        return null;
    }

    private static (double X, double Y)? CenterOf(
        Starling.Layout.Box.Box box, DomElement target, double originX = 0, double originY = 0)
    {
        var fx = originX + box.Frame.X;
        var fy = originY + box.Frame.Y;
        if (ReferenceEquals(box.Element, target))
            return (fx + box.Frame.Width / 2, fy + box.Frame.Height / 2);
        if (box is Starling.Layout.Box.TextBox) return null;

        var cx = fx + box.Border.Left + box.Padding.Left;
        var cy = fy + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            if (CenterOf(child, target, cx, cy) is { } r) return r;
        return null;
    }
}
