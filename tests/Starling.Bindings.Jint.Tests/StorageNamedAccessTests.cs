using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: localStorage/sessionStorage named-property access
/// (HTML §12.3.2 — storage.foo = x persists and reads back).
/// </summary>
[TestClass]
public sealed class StorageNamedAccessTests
{
    [TestMethod]
    public void named_set_and_get()
    {
        StorageBinding.ResetForTests();
        var e = NewSession();
        e.Evaluate("sessionStorage.foo = 'bar'");
        e.Evaluate("sessionStorage.foo").AsString().Should().Be("bar");
        e.Evaluate("sessionStorage.getItem('foo')").AsString().Should().Be("bar");
        e.Evaluate("sessionStorage.length").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void setItem_then_named_read()
    {
        StorageBinding.ResetForTests();
        var e = NewSession();
        e.Evaluate("sessionStorage.setItem('k', '7')");
        e.Evaluate("sessionStorage.k").AsString().Should().Be("7");
    }

    [TestMethod]
    public void delete_and_in_operator()
    {
        StorageBinding.ResetForTests();
        var e = NewSession();
        e.Evaluate("sessionStorage.a = '1'");
        e.Evaluate("'a' in sessionStorage").AsBoolean().Should().BeTrue();
        e.Evaluate("delete sessionStorage.a");
        e.Evaluate("'a' in sessionStorage").AsBoolean().Should().BeFalse();
        e.Evaluate("sessionStorage.a === undefined").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void interface_members_are_not_shadowed()
    {
        StorageBinding.ResetForTests();
        var e = NewSession();
        e.Evaluate("typeof sessionStorage.getItem").AsString().Should().Be("function");
        e.Evaluate("sessionStorage.x = 'v'");
        e.Evaluate("typeof sessionStorage.setItem").AsString().Should().Be("function");
    }

    private static global::Jint.Engine NewSession()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var baseUrl = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine, doc, baseUrl, http, NullLoggerFactory.Instance,
            new WebEventLoop(), null,
            (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return engine;
    }
}
