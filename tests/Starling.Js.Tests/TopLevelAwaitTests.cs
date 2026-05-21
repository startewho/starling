using AwesomeAssertions;
using Starling.Js.Modules;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-03b — top-level await in ES modules. A module that uses (or transitively
/// imports) a top-level <c>await</c> evaluates asynchronously: its body suspends
/// at each <c>await</c> via the existing async-function machinery, the loader
/// drives the microtask queue until evaluation settles, and importers observe the
/// module's bindings only after it finishes. Modules with no top-level await keep
/// the synchronous evaluation contract.
/// </summary>
/// <remarks>
/// Uses the same in-memory <see cref="IModuleHost"/> + <c>report()</c> harness the
/// existing <c>JsModuleTests</c> use. Tests are deterministic: <c>LoadAndEvaluate</c>
/// drives <see cref="MicrotaskQueue.DrainAll"/> internally, so no sleeps are needed.
/// </remarks>
[TestClass]
public class TopLevelAwaitTests
{
    /// <summary>In-memory module host: identity resolution over a flat map of
    /// specifiers to source text (mirrors <c>JsModuleTests.MapHost</c>).</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Evaluate the module graph and return the single value the entry
    /// module passed to <c>report(...)</c>.</summary>
    private static JsValue RunGraph(Dictionary<string, string> modules, string entry)
    {
        JsValue captured = JsValue.Undefined;
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            captured = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        runtime.WithActiveVm(() => loader.LoadAndEvaluate(entry));
        return captured;
    }

    /// <summary>Evaluate the module graph, collecting every <c>report(...)</c> call
    /// in order so a test can assert evaluation ordering across modules.</summary>
    private static List<string> RunGraphLog(Dictionary<string, string> modules, string entry)
    {
        var log = new List<string>();
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            log.Add(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined");
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        runtime.WithActiveVm(() => loader.LoadAndEvaluate(entry));
        return log;
    }

    [TestMethod]
    public void Top_level_await_binds_resolved_value_for_later_statement()
    {
        // The awaited value binds; a later statement in the SAME module sees it.
        var modules = new Dictionary<string, string>
        {
            ["main"] = "const v = await Promise.resolve(42); report(v);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Top_level_await_suspends_then_resumes_in_order()
    {
        // Two sequential top-level awaits accumulate correctly across suspensions.
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                const a = await Promise.resolve(1);
                const b = await Promise.resolve(2);
                report(a + b);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Importer_observes_tla_module_exports_only_after_it_settles()
    {
        // 'dep' uses top-level await before publishing its export. 'main' must
        // observe the post-await export and the ordering log must show dep's body
        // finishing before main's body runs.
        var modules = new Dictionary<string, string>
        {
            ["dep"] = @"
                report('dep:start');
                export const value = await Promise.resolve('ready');
                report('dep:done');",
            ["main"] = @"
                import { value } from 'dep';
                report('main:' + value);",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Equal("dep:start", "dep:done", "main:ready");
    }

    [TestMethod]
    public void Importer_body_waits_for_dependency_top_level_await()
    {
        // 'main' itself has NO top-level await but imports a TLA module. Its body
        // must still wait for the dependency's await to complete before running.
        var modules = new Dictionary<string, string>
        {
            ["dep"] = @"
                report('dep:before-await');
                export const ready = await Promise.resolve(true);
                report('dep:after-await');",
            ["main"] = @"
                import { ready } from 'dep';
                report('main:body ' + ready);",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Equal("dep:before-await", "dep:after-await", "main:body true");
    }

    [TestMethod]
    public void Multiple_async_dependencies_all_settle_before_dependent_body()
    {
        var modules = new Dictionary<string, string>
        {
            ["a"] = "export const a = await Promise.resolve(1); report('a:done');",
            ["b"] = "export const b = await Promise.resolve(2); report('b:done');",
            ["main"] = @"
                import { a } from 'a';
                import { b } from 'b';
                report('main:' + (a + b));",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Contain("a:done");
        log.Should().Contain("b:done");
        log[^1].Should().Be("main:3");
        // The dependent body runs strictly after both deps finish.
        log.IndexOf("a:done").Should().BeLessThan(log.IndexOf("main:3"));
        log.IndexOf("b:done").Should().BeLessThan(log.IndexOf("main:3"));
    }

    [TestMethod]
    public void Rejected_top_level_await_surfaces_as_evaluation_error()
    {
        var modules = new Dictionary<string, string>
        {
            ["main"] = "await Promise.reject('boom'); report('unreached');",
        };
        var act = () => RunGraph(modules, "main");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Be("boom");
    }

    [TestMethod]
    public void Throw_after_top_level_await_surfaces_as_evaluation_error()
    {
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                const x = await Promise.resolve(1);
                throw 'late-' + x;",
        };
        var act = () => RunGraph(modules, "main");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Be("late-1");
    }

    [TestMethod]
    public void Error_in_async_dependency_propagates_to_importer()
    {
        // The dependency rejects in its top-level await; the importer must NOT run
        // its body and the graph evaluation errors.
        var modules = new Dictionary<string, string>
        {
            ["dep"] = "await Promise.reject('dep-fail'); export const x = 1;",
            ["main"] = "import { x } from 'dep'; report('should-not-run ' + x);",
        };
        var act = () => RunGraph(modules, "main");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Be("dep-fail");
    }

    [TestMethod]
    public void For_await_at_top_level_marks_module_async()
    {
        // `for await (… of …)` is a top-level await; the loop must await each
        // async-iterator step and complete before the trailing report runs.
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                let total = 0;
                async function* gen() { yield 1; yield 2; yield 3; }
                for await (const n of gen()) { total = total + n; }
                report(total);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Await_nested_in_non_tla_module_function_stays_synchronous()
    {
        // An `await` inside a nested async function is NOT module top-level: the
        // module is still synchronous and its body completes before any of the
        // async function's reactions. report() inside the async fn fires later.
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                async function f() { return await Promise.resolve('inner'); }
                f().then(function(v){ globalThis.tail = v; });
                report('sync-body');",
        };
        RunGraph(modules, "main").AsString.Should().Be("sync-body");
    }

    [TestMethod]
    public void Non_tla_module_still_evaluates_synchronously()
    {
        // Regression: a plain module (no top-level await anywhere in its graph)
        // evaluates synchronously — report() observes the value with no Promise
        // settling round-trip required.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const answer = 42;",
            ["main"] = "import { answer } from 'lib'; report(answer);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Tla_module_imported_by_non_tla_chain_propagates_async_settling()
    {
        // a (TLA) <- b (no TLA, imports a) <- main (no TLA, imports b). The async
        // settling must thread all the way up: main's body runs only after a's
        // top-level await completes.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                report('a:start');
                export const val = await Promise.resolve('v');
                report('a:done');",
            ["b"] = @"
                import { val } from 'a';
                report('b:' + val);
                export const fromB = val + '!';",
            ["main"] = @"
                import { fromB } from 'b';
                report('main:' + fromB);",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Equal("a:start", "a:done", "b:v", "main:v!");
    }
}
