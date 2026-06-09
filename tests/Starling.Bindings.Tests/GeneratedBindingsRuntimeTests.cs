using AwesomeAssertions;
using Starling.Bindings.Generated;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

// Proves the generated binding glue actually works, not just that it compiles.
// The real bindings are installed first, then the generated installers overwrite
// the members they cover, so driving them from real JS exercises the generated
// accessors and methods against a real DOM.
[TestClass]
public sealed class GeneratedBindingsRuntimeTests
{
    [TestMethod]
    public void Generated_accessors_and_methods_read_real_dom()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // id (generated getter), setAttribute / hasAttribute (generated methods),
        // and tagName via the generated OVERRIDE binding (HTML uppercasing).
        var result = Eval(runtime, """
            var e = document.createElement('div');
            e.setAttribute('id', 'foo');
            result = e.id + '/' + e.tagName + '/' + e.hasAttribute('id') + '/' + e.hasAttribute('missing');
        """);

        result.AsString.Should().Be("foo/DIV/true/false");
    }

    [TestMethod]
    public void Generated_node_wrapping_accessor_preserves_identity()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // firstChild is a generated node-wrapping accessor; it must return the
        // same JS wrapper as the child looked up directly.
        Eval(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.appendChild(child);
            result = parent.firstChild === child;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Generated_string_setter_writes_through_to_dom()
    {
        var (runtime, doc) = BuildEnvWithGenerated();

        Eval(runtime, """
            var e = document.createElement('div');
            e.id = 'written';
            result = e.id;
        """).AsString.Should().Be("written");
    }

    [TestMethod]
    public void Generated_character_data_methods_take_unsigned_long_args()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // length (generated numeric getter) and the substringData / appendData /
        // insertData / deleteData / replaceData methods (generated, with
        // unsigned long args marshalled through IdlMarshal.RequireUnsignedLong).
        Eval(runtime, """
            var t = document.createTextNode('hello world');
            var len = t.length;
            var sub = t.substringData(6, 5);
            t.appendData('!');
            t.insertData(0, '>> ');
            t.deleteData(0, 3);
            t.replaceData(0, 5, 'goodbye');
            result = len + '/' + sub + '/' + t.data;
        """).AsString.Should().Be("11/world/goodbye world!");
    }

    [TestMethod]
    public void Generated_substring_data_clamps_count_to_the_end()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // count past the end clamps; this also proves a large JS number survives
        // the Web IDL unsigned long (ToUint32) conversion intact.
        Eval(runtime, """
            var t = document.createTextNode('hello');
            result = t.substringData(2, 1000);
        """).AsString.Should().Be("llo");
    }

    [TestMethod]
    public void Generated_element_traversal_accessors_skip_non_elements()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // firstElementChild / lastElementChild / childElementCount (generated
        // node + numeric getters) and next/previousElementSibling skip the text
        // and comment nodes between the two elements.
        Eval(runtime, """
            var root = document.createElement('root');
            root.appendChild(document.createTextNode('x'));
            var a = document.createElement('a');
            var b = document.createElement('b');
            root.appendChild(a);
            root.appendChild(document.createComment('c'));
            root.appendChild(b);
            result = [
                root.firstElementChild === a,
                root.lastElementChild === b,
                root.childElementCount,
                a.nextElementSibling === b,
                b.previousElementSibling === a
            ].join('/');
        """).AsString.Should().Be("true/true/2/true/true");
    }

    [TestMethod]
    public void Generated_node_type_override_returns_the_spec_code()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // nodeType comes from the override getter mapping the NodeKind enum to its
        // integer DOM code: Element = 1, Text = 3, Comment = 8.
        Eval(runtime, """
            var e = document.createElement('div');
            var t = document.createTextNode('x');
            var c = document.createComment('c');
            result = e.nodeType + '/' + t.nodeType + '/' + c.nodeType;
        """).AsString.Should().Be("1/3/8");
    }

    [TestMethod]
    public void Generated_is_connected_and_parent_element_track_the_tree()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        Eval(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.appendChild(child);
            var detached = document.createElement('p');
            result = [
                child.parentElement === parent,
                detached.parentElement === null,
                parent.hasChildNodes(),
                detached.hasChildNodes(),
                document.documentElement.isConnected
            ].join('/');
        """).AsString.Should().Be("true/true/true/false/true");
    }

    [TestMethod]
    public void Generated_split_text_returns_new_sibling_and_whole_text_rejoins()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // splitText is a generated method with an unsigned long arg returning a
        // wrapped Text node; wholeText is a generated string getter.
        Eval(runtime, """
            var root = document.createElement('root');
            var t = document.createTextNode('hello world');
            root.appendChild(t);
            var tail = t.splitText(5);
            result = t.data + '|' + tail.data + '|' + (t.nextSibling === tail) + '|' + t.wholeText;
        """).AsString.Should().Be("hello| world|true|hello world");
    }

    [TestMethod]
    public void Generated_class_name_reflects_the_class_attribute()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // className is a generated string accessor reflecting the class attribute,
        // both directions.
        Eval(runtime, """
            var e = document.createElement('div');
            e.className = 'a b';
            var viaAttr = e.getAttribute('class');
            e.setAttribute('class', 'c');
            result = e.className + '/' + viaAttr;
        """).AsString.Should().Be("c/a b");
    }

    [TestMethod]
    public void Generated_namespace_uri_returns_html_namespace_or_null()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // namespaceURI is an override getter: the HTML namespace for createElement,
        // and the value is a real string (not null) for HTML elements.
        Eval(runtime, """
            var e = document.createElement('div');
            result = e.namespaceURI;
        """).AsString.Should().Be("http://www.w3.org/1999/xhtml");
    }

    [TestMethod]
    public void Generated_document_fragment_children_is_an_html_collection_of_elements()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // children is a generated override getter returning a live HTMLCollection
        // of element children, skipping the text node.
        Eval(runtime, """
            var f = document.createDocumentFragment();
            f.appendChild(document.createElement('a'));
            f.appendChild(document.createTextNode('x'));
            f.appendChild(document.createElement('b'));
            result = f.children.length + '/' + f.children[0].tagName + '/' + f.children[1].tagName;
        """).AsString.Should().Be("2/A/B");
    }

    [TestMethod]
    public void Generated_attr_accessors_read_and_write_through_the_attr_node()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // Attr is a newly targeted interface: name/value/ownerElement are
        // generated accessors over the AttrNode host, and value writes through.
        Eval(runtime, """
            var e = document.createElement('div');
            e.setAttribute('data-x', 'one');
            var a = e.getAttributeNode('data-x');
            var before = a.name + '=' + a.value + '/' + (a.ownerElement === e);
            a.value = 'two';
            result = before + '/' + e.getAttribute('data-x');
        """).AsString.Should().Be("data-x=one/true/two");
    }

    [TestMethod]
    public void Generated_dotnet11_union_type_works()
    {
        // NodeOrString is a generated .NET 11 union type from IDL (Node or DOMString).
        // Implicit conversion in, then an exhaustive switch over the case types.
        Starling.Bindings.Generated.NodeOrString fromString = "hello";
        string s = fromString switch
        {
            Starling.Dom.Node => "node",
            string str => str,
        };
        s.Should().Be("hello");

        Starling.Bindings.Generated.NodeOrString fromNode = new Document();
        string n = fromNode switch
        {
            Starling.Dom.Node => "node",
            string str => str,
        };
        n.Should().Be("node");
    }

    private static (JsRuntime, Document) BuildEnvWithGenerated()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);

        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());

        // Overwrite the covered prototype members with the generated versions.
        CoreDomBindingsGenerated.InstallAll(runtime.Realm);

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
