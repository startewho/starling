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

    [TestMethod]
    public void HtmlCollection_named_property_does_not_shadow_prototype_builtin()
    {
        var (runtime, _) = BuildEnv();
        // An element with id="item" must NOT shadow the built-in item() method:
        // HTMLCollection has no [LegacyOverrideBuiltins], so a supported name that
        // collides with a prototype property is suppressed.
        Eval(runtime, """
            var s = document.createElement('span');
            s.id = 'item';
            document.body.appendChild(s);
            var coll = document.getElementsByTagName('span');
            result = (typeof coll.item) + ',' + coll.hasOwnProperty('item')
                + ',' + (coll.item(0) === s)
                + ',' + (Object.getOwnPropertyNames(coll).indexOf('item') === -1);
        """).AsString.Should().Be("function,false,true,true");
    }

    [TestMethod]
    public void HtmlCollection_length_is_a_prototype_accessor_not_own()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createElement('span');
            var b = document.createElement('span');
            document.body.appendChild(a);
            document.body.appendChild(b);
            var coll = document.getElementsByTagName('span');
            result = coll.length + ',' + coll.hasOwnProperty('length')
                + ',' + (Object.getOwnPropertyNames(coll).indexOf('length') === -1);
        """).AsString.Should().Be("2,false,true");
    }

    [TestMethod]
    public void HtmlCollection_keys_dedupe_index_collision_with_id()
    {
        var (runtime, _) = BuildEnv();
        // An element with id="0" collides with the array index "0"; the own-key
        // list must contain "0" exactly once (the index), not also as a name.
        Eval(runtime, """
            var s = document.createElement('span');
            s.id = '0';
            document.body.appendChild(s);
            var coll = document.getElementsByTagName('span');
            var names = Object.getOwnPropertyNames(coll);
            result = names.filter(function (n) { return n === '0'; }).length;
        """).AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void HtmlCollection_prototype_shadowed_name_behaves_as_expando()
    {
        var (runtime, _) = BuildEnv();
        // "item" is shadowed by the prototype, so it is NOT a visible named
        // property — assigning to it creates an ordinary own expando.
        Eval(runtime, """
            var s = document.createElement('span');
            s.id = 'item';
            document.body.appendChild(s);
            var coll = document.getElementsByTagName('span');
            coll.item = 5;
            result = coll.hasOwnProperty('item') + ',' + coll.item;
        """).AsString.Should().Be("true,5");
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

    [TestMethod]
    public void Document_own_expando_set_to_undefined_wins_over_named_element()
    {
        var (runtime, _) = BuildEnv();
        // An own expando whose value is undefined is still an own property and
        // must win over a later same-named element — ownership is decided by
        // HasOwn, not by whether the returned value happens to be undefined.
        Eval(runtime, """
            document.foo = undefined;
            var img = document.createElement('img');
            img.setAttribute('name', 'foo');
            document.body.appendChild(img);
            result = (document.foo === undefined) + ',' + document.hasOwnProperty('foo');
        """).AsString.Should().Be("true,true");
    }

    [TestMethod]
    public void Document_named_collection_uses_the_lookup_key_not_first_match_name()
    {
        var (runtime, _) = BuildEnv();
        // Two imgs share id="bar" but have different name attributes. document['bar']
        // must return the live collection keyed by "bar" (both elements), not one
        // rebuilt from the first match's name attribute.
        Eval(runtime, """
            var a = document.createElement('img');
            a.setAttribute('id', 'bar'); a.setAttribute('name', 'zzz');
            var b = document.createElement('img');
            b.setAttribute('id', 'bar'); b.setAttribute('name', 'yyy');
            document.body.appendChild(a);
            document.body.appendChild(b);
            var coll = document['bar'];
            result = Object.prototype.toString.call(coll) + ',' + coll.length;
        """).AsString.Should().Be("[object HTMLCollection],2");
    }

    [TestMethod]
    public void Document_keys_list_supported_names_before_expandos()
    {
        var (runtime, _) = BuildEnv();
        // Legacy platform object ordering: supported property names come before
        // ordinary own (expando) keys — and Keys must agree with OwnPropertyKeys.
        Eval(runtime, """
            document.zzexpando = 1;
            var img = document.createElement('img');
            img.setAttribute('name', 'navnamed');
            document.body.appendChild(img);
            var names = Object.getOwnPropertyNames(document);
            var iName = names.indexOf('navnamed');
            var iExp = names.indexOf('zzexpando');
            result = (iName !== -1) + ',' + (iExp !== -1) + ',' + (iName < iExp);
        """).AsString.Should().Be("true,true,true");
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
