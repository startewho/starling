using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>
/// B5-4 — MutationObserver tests. Covers the JS surface (constructable /
/// observable / disconnectable) and end-to-end record delivery now that the
/// DOM mutation hooks are wired: attribute/childList/characterData records are
/// queued from real DOM mutations and delivered on a drained microtask.
/// </summary>
[TestClass]
public sealed class MutationObserverTests
{
    [TestMethod]
    public void Constructor_is_installed_globally_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof MutationObserver;")
            .AsString.Should().Be("function");
        Eval(runtime, "result = MutationObserver.name;")
            .AsString.Should().Be("MutationObserver");
    }

    [TestMethod]
    public void Constructor_requires_callable_argument()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new MutationObserver(); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");

        Eval(runtime, """
            try { new MutationObserver(123); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Constructor_accepts_callback_and_instance_has_correct_constructor_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            result = o.constructor.name;
        """).AsString.Should().Be("MutationObserver");
    }

    [TestMethod]
    public void Observe_with_empty_options_throws_type_error()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            try { o.observe(document.body, {}); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Observe_with_child_list_true_succeeds()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { childList: true });
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Observe_with_attribute_filter_validates_array()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { attributes: true, attributeFilter: ['class', 'id'] });
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Observe_requires_node_target()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            try { o.observe({}, { childList: true }); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Disconnect_is_callable_and_idempotent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { childList: true });
            o.disconnect();
            o.disconnect();
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Take_records_returns_array_and_is_empty_on_fresh_observer()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            var r = o.takeRecords();
            result = Array.isArray(r) && r.length === 0 ? 'ok' : 'fail';
        """).AsString.Should().Be("ok");
    }

    // -------------------- end-to-end record delivery -----------------------

    [TestMethod]
    public void Attribute_records_carry_old_value_on_set_replace_and_remove()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            globalThis.recs = [];
            var o = new MutationObserver(function (records) {
                for (var i = 0; i < records.length; i++) {
                    var r = records[i];
                    recs.push(r.type + ':' + r.attributeName + ':' + r.oldValue);
                }
            });
            o.observe(document.body, { attributes: true, attributeOldValue: true });
            document.body.setAttribute('data-x', 'one');  // add    -> oldValue null
            document.body.setAttribute('data-x', 'two');  // replace-> oldValue "one"
            document.body.removeAttribute('data-x');       // remove -> oldValue "two"
        """);
        runtime.DrainMicrotasks();
        // The removal record (last) must carry "two" — the bug under review left
        // oldValue null on the remove path.
        Eval(runtime, "result = recs.join('|');").AsString
            .Should().Be("attributes:data-x:null|attributes:data-x:one|attributes:data-x:two");
    }

    [TestMethod]
    public void ChildList_records_fire_for_append_and_remove()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            globalThis.recs = [];
            var o = new MutationObserver(function (records) {
                for (var i = 0; i < records.length; i++) {
                    var r = records[i];
                    recs.push(r.type + ':+' + r.addedNodes.length + ':-' + r.removedNodes.length);
                }
            });
            o.observe(document.body, { childList: true });
            var d = document.createElement('div');
            document.body.appendChild(d);
            document.body.removeChild(d);
        """);
        runtime.DrainMicrotasks();
        Eval(runtime, "result = recs.join('|');").AsString
            .Should().Be("childList:+1:-0|childList:+0:-1");
    }

    [TestMethod]
    public void ChildList_fragment_insert_produces_one_record_with_all_added_nodes()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            globalThis.recs = [];
            var o = new MutationObserver(function (records) {
                for (var i = 0; i < records.length; i++)
                    recs.push(records[i].type + ':+' + records[i].addedNodes.length);
            });
            o.observe(document.body, { childList: true });
            var frag = document.createDocumentFragment();
            frag.appendChild(document.createElement('a'));
            frag.appendChild(document.createElement('b'));
            document.body.appendChild(frag);
        """);
        runtime.DrainMicrotasks();
        // One record covering both moved nodes — not two single-node records.
        Eval(runtime, "result = recs.join('|');").AsString.Should().Be("childList:+2");
    }

    [TestMethod]
    public void CharacterData_record_carries_old_value()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            globalThis.recs = [];
            var t = document.createTextNode('abc');
            document.body.appendChild(t);
            var o = new MutationObserver(function (records) {
                for (var i = 0; i < records.length; i++)
                    recs.push(records[i].type + ':' + records[i].oldValue);
            });
            o.observe(t, { characterData: true, characterDataOldValue: true });
            t.data = 'xyz';
        """);
        runtime.DrainMicrotasks();
        Eval(runtime, "result = recs.join('|');").AsString
            .Should().Be("characterData:abc");
    }

    [TestMethod]
    public void Observing_same_target_twice_does_not_duplicate_records()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            globalThis.count = 0;
            var o = new MutationObserver(function (records) { count += records.length; });
            o.observe(document.body, { attributes: true });
            o.observe(document.body, { attributes: true }); // re-observe same target
            document.body.setAttribute('data-y', '1');
        """);
        runtime.DrainMicrotasks();
        Eval(runtime, "result = count;").AsNumber.Should().Be(1);
    }

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc);
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
