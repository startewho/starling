using AwesomeAssertions;
using Starling.Bindings.Observers;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>
/// B5-4 — IntersectionObserver JS surface tests. Layout integration is not
/// wired (see IntersectionObserverBinding's file-level TODO); these tests
/// assert only the constructable / observable / disconnectable shape.
/// </summary>
[TestClass]
public sealed class IntersectionObserverTests
{
    [TestMethod]
    public void Constructor_is_installed_globally_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof IntersectionObserver;")
            .AsString.Should().Be("function");
        Eval(runtime, "result = IntersectionObserver.name;")
            .AsString.Should().Be("IntersectionObserver");
    }

    [TestMethod]
    public void Constructor_requires_callable_argument()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new IntersectionObserver(); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");

        Eval(runtime, """
            try { new IntersectionObserver(123); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Constructor_accepts_callback_and_default_options()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            result = io.constructor.name + '/' + io.rootMargin + '/' + io.thresholds.length;
        """).AsString.Should().Be("IntersectionObserver/0px 0px 0px 0px/1");
    }

    [TestMethod]
    public void Threshold_out_of_range_throws_range_error()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new IntersectionObserver(function () {}, { threshold: 1.5 }); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("RangeError");
    }

    [TestMethod]
    public void Observe_requires_element_target()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            try { io.observe({}); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Observe_and_unobserve_succeed()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            var el = document.createElement('div');
            document.body.appendChild(el);
            io.observe(el);
            io.unobserve(el);
            io.disconnect();
            io.disconnect();
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Take_records_returns_empty_array()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            var r = io.takeRecords();
            result = Array.isArray(r) && r.length === 0 ? 'ok' : 'fail';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Threshold_array_is_parsed_and_exposed()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {}, { threshold: [0, 0.5, 1] });
            result = io.thresholds.length + ':' + io.thresholds[1];
        """).AsString.Should().Be("3:0.5");
    }

    [TestMethod]
    public void Observe_delivers_intersecting_for_in_view_and_not_for_below_fold()
    {
        var (runtime, doc, a, b) = BuildLaidOutEnv();

        Eval(runtime, """
            globalThis.calls = [];
            var a = document.getElementById('a');
            var b = document.getElementById('b');
            var io = new IntersectionObserver(function (entries) {
                for (const e of entries)
                    calls.push((e.target === a ? 'a' : 'b') + ':' + e.isIntersecting);
            }, { threshold: 0 });
            io.observe(a);
            io.observe(b);
        """);
        Drain(runtime);

        // a (y 100) is inside the 800-tall viewport; b (y 2000) is below it.
        Eval(runtime, "result = calls.slice().sort().join(',');")
            .AsString.Should().Be("a:true,b:false");
    }

    [TestMethod]
    public void Scrolling_the_viewport_reveals_a_below_fold_target()
    {
        var (runtime, doc, a, b) = BuildLaidOutEnv();

        Eval(runtime, """
            globalThis.calls = [];
            var a = document.getElementById('a');
            var b = document.getElementById('b');
            var io = new IntersectionObserver(function (entries) {
                for (const e of entries)
                    calls.push((e.target === a ? 'a' : 'b') + ':' + e.isIntersecting);
            }, { threshold: 0 });
            io.observe(a);
            io.observe(b);
        """);
        Drain(runtime);
        Eval(runtime, "calls.length = 0;");

        // Scroll so the viewport (0,1800,1000,800) covers b (y 2000) but not a (y 100).
        IntersectionObserverBinding.UpdateForDocument(doc, new LayoutRect(0, 1800, 1000, 800));
        Drain(runtime);

        Eval(runtime, "result = calls.slice().sort().join(',');")
            .AsString.Should().Be("a:false,b:true");
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

    // A document with two positioned targets and a fake layout host: 'a' sits in
    // the initial 1000x800 viewport, 'b' is 2000px down (below the fold).
    private static (JsRuntime, Document, Element a, Element b) BuildLaidOutEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var a = doc.CreateElement("div");
        a.SetAttribute("id", "a");
        var b = doc.CreateElement("div");
        b.SetAttribute("id", "b");
        body.AppendChild(a);
        body.AppendChild(b);

        var host = new FakeLayoutHost(new Dictionary<Element, LayoutRect>
        {
            [a] = new LayoutRect(0, 100, 200, 50),
            [b] = new LayoutRect(0, 2000, 200, 50),
        });
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc,
            new WindowInstallOptions(InnerWidth: 1000, InnerHeight: 800, LayoutHost: host));
        return (runtime, doc, a, b);
    }

    // Drains queued microtasks (observer delivery rides the realm microtask queue,
    // which WithActiveVm flushes on exit).
    private static void Drain(JsRuntime runtime) => runtime.WithActiveVm(() => { });

    private sealed class FakeLayoutHost(Dictionary<Element, LayoutRect> rects) : ILayoutHost
    {
        public bool TryGetBoundingClientRect(Element element, out LayoutRect rect)
            => rects.TryGetValue(element, out rect);

        public bool TryGetOffsetMetrics(Element element, out OffsetMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public string GetComputedProperty(Element element, string propertyName) => string.Empty;

        public bool MatchMedia(string query) => false;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
