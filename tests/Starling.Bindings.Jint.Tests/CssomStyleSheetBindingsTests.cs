using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: CSSOM stylesheets — document.styleSheets, StyleSheetList,
/// CSSStyleSheet, cssRules, CSSStyleRule, live CSSStyleDeclaration. Mirrors the
/// canonical backend against Jint.
/// </summary>
[TestClass]
public sealed class CssomStyleSheetBindingsTests
{
    private const string Html =
        "<!doctype html><html><head><style>.a { color: red; font-size: 12px; } #b { display: none; }</style></head>" +
        "<body><p>x</p></body></html>";

    [TestMethod]
    public void styleSheets_list_and_rules()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.styleSheets.length").AsNumber().Should().Be(1);
        e.Evaluate("document.styleSheets[0].cssRules.length").AsNumber().Should().Be(2);
        e.Evaluate("document.styleSheets.item(0).type").AsString().Should().Be("text/css");
        e.Evaluate("document.styleSheets[0].cssRules[0].selectorText").AsString().Should().Be(".a");
        e.Evaluate("document.styleSheets[0].cssRules[1].selectorText").AsString().Should().Be("#b");
        e.Evaluate("document.styleSheets[0].cssRules[0].type").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void rule_style_declaration_read()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.styleSheets[0].cssRules[0].style.getPropertyValue('color')").AsString().Should().Be("red");
        e.Evaluate("document.styleSheets[0].cssRules[0].style.color").AsString().Should().Be("red");
        e.Evaluate("document.styleSheets[0].cssRules[0].style.fontSize").AsString().Should().Be("12px");
        e.Evaluate("document.styleSheets[0].cssRules[0].style.length").AsNumber().Should().BeGreaterThanOrEqualTo(2);
    }

    [TestMethod]
    public void rule_style_setProperty_roundtrips()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var s = document.styleSheets[0].cssRules[0].style;
              s.setProperty('color', 'blue');
              s.fontWeight = 'bold';
              var again = document.styleSheets[0].cssRules[0].style;
              return again.getPropertyValue('color') + '|' + again.fontWeight;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("blue|bold");
    }

    [TestMethod]
    public void selectorText_is_settable()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var r = document.styleSheets[0].cssRules[0];
              r.selectorText = '.changed';
              return document.styleSheets[0].cssRules[0].selectorText;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be(".changed");
    }

    [TestMethod]
    public void element_sheet_on_style()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.querySelector('style').sheet.cssRules.length").AsNumber().Should().Be(2);
        e.Evaluate("document.querySelector('p').sheet").IsNull().Should().BeTrue();
    }

    [TestMethod]
    public void cssText_serializes_rule()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.styleSheets[0].cssRules[0].cssText").AsString().Should().Contain(".a {").And.Contain("color: red");
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine, doc, baseUrl, http, NullLoggerFactory.Instance,
            new WebEventLoop(), null,
            (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return (engine, doc);
    }
}
