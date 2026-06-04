using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// WebIDL legacy-platform-object semantics for the exotic DOM wrappers:
/// <list type="bullet">
/// <item>DOMTokenList recognizes the full array-index range (0..2^32-2), not
/// just values that fit in a signed int.</item>
/// <item>HTMLCollection and the Document named-property wrapper produce a
/// duplicate-free own-key list — a supported name that collides with an own
/// (expando) property is suppressed, since the own property wins.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class LegacyPlatformObjectTests
{
    // ---- DOMTokenList array-index parsing (WebIDL 0..2^32-2) -----------------

    [TestMethod]
    public void DomTokenList_treats_large_array_index_as_index_not_expando()
    {
        var (runtime, _) = BuildEnv();
        // "4294967294" (2^32-2) is a valid WebIDL array index. It must route
        // through the indexed getter (out of range -> undefined), NOT be read
        // back as an ordinary expando property. With signed-int parsing this
        // string overflowed and was mis-handled as a plain property.
        Eval(runtime, """
            var e = document.createElement('div');
            e.className = 'a b';
            e.classList['4294967294'] = 'expando';
            result = e.classList['4294967294'];
        """).IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void DomTokenList_integer_index_access_still_works()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.className = 'a b';
            result = e.classList[0] + ',' + e.classList[1] + ',' + (e.classList[2] === undefined);
        """).AsString.Should().Be("a,b,true");
    }

    // ---- HTMLCollection own-key dedup ----------------------------------------

    [TestMethod]
    public void HtmlCollection_suppresses_supported_name_colliding_with_expando()
    {
        var (runtime, doc) = BuildEnv();
        // Set the expando BEFORE any element supports the name "foo", so the
        // legacy [[Set]] stores it as an ordinary own property. Then add an
        // element whose id makes "foo" a supported name too.
        Eval(runtime, """
            var coll = document.getElementsByTagName('span');
            coll.foo = 1;                       // own expando (no supported "foo" yet)
            var s = document.createElement('span');
            s.id = 'foo';
            document.body.appendChild(s);       // now "foo" is also a supported name
            var names = Object.getOwnPropertyNames(coll);
            result = names.filter(function (n) { return n === 'foo'; }).length;
        """).AsNumber.Should().Be(1);
    }

    // ---- Document named-property wrapper own-key dedup -----------------------

    [TestMethod]
    public void Document_suppresses_supported_name_colliding_with_expando()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            document.foo = 1;                   // own expando (no named element yet)
            var img = document.createElement('img');
            img.setAttribute('name', 'foo');
            document.body.appendChild(img);     // now "foo" is also a supported name
            var names = Object.getOwnPropertyNames(document);
            result = names.filter(function (n) { return n === 'foo'; }).length;
        """).AsNumber.Should().Be(1);
    }

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
