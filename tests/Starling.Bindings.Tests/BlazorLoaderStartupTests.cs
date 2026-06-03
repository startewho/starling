// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Loop;

namespace Starling.Bindings.Tests;

[TestClass]
public sealed class BlazorLoaderStartupTests
{
    [TestMethod]
    public void Published_blazor_loader_reaches_boot_resource_callback()
    {
        var runtime = BuildEnv();
        var logs = new List<string>();
        runtime.Realm.ConsoleSink = (level, message) => logs.Add($"{level}: {message}");

        Eval(runtime, File.ReadAllText(Path.Combine(
            LocateRepoRoot(),
            "testdata",
            "sites",
            "blazor-status",
            "_framework",
            "blazor.webassembly.js")));

        Eval(runtime, """
            globalThis.loadBootResourceHit = "";
            globalThis.startError = "";
            Blazor.start({
                loadBootResource: function (type, name, defaultUri) {
                    globalThis.loadBootResourceHit = type + ":" + name;
                    return defaultUri;
                },
                configureRuntime: function () {
                    globalThis.configureRuntimeHit = true;
                }
            }).catch(function (error) {
                globalThis.startError = error && error.message ? error.message : String(error);
            });
            """);
        runtime.DrainMicrotasks();
        runtime.DrainMicrotasks();

        var diagnostics = runtime.GetGlobal("startError").AsString + "\n" + string.Join("\n", logs);
        runtime.GetGlobal("loadBootResourceHit").AsString.Should().Be("dotnetjs:dotnet.js", diagnostics);
    }

    [TestMethod]
    public void Published_blazor_fixture_reaches_mocked_dotnet_module_before_watchdog()
    {
        var (runtime, loop, doc) = BuildEnvWithTimersAndModuleHost();
        var logs = new List<string>();
        runtime.Realm.ConsoleSink = (level, message) => logs.Add($"{level}: {message}");

        foreach (var script in LoadPublishedFixtureScripts())
        {
            Eval(runtime, script.Source, script.Label);
        }

        runtime.DrainMicrotasks();
        for (var i = 0; i < 5 && !runtime.GetGlobal("__dotnetCreateHit").IsBoolean; i++)
        {
            if (loop.PendingTimerCount == 0) break;
            loop.AdvanceBy(50);
        }

        var boot = doc.GetElementById("blazor-boot")?.TextContent ?? "";
        var diagnostics = boot + "\n" + string.Join("\n", logs);
        var createHit = runtime.GetGlobal("__dotnetCreateHit");
        (createHit.IsBoolean && createHit.AsBool).Should().BeTrue(diagnostics);
    }

    [TestMethod]
    public void Published_dotnet_module_dynamic_import_settles()
    {
        var runtime = BuildEnv();
        _ = new ModuleLoader(runtime, new PublishedFixtureModuleHost());

        Eval(runtime, """
            globalThis.dotnetImportStatus = 'pending';
            import('http://localhost:8088/blazor-status/_framework/dotnet.js').then(
                function (ns) { globalThis.dotnetImportStatus = typeof ns.dotnet; },
                function (error) { globalThis.dotnetImportStatus = 'error:' + (error && error.message ? error.message : String(error)); });
            """, "http://localhost:8088/blazor-status/index.html");
        runtime.DrainMicrotasks();
        runtime.DrainMicrotasks();

        runtime.GetGlobal("dotnetImportStatus").AsString.Should().Be("object");
    }

    private static JsRuntime BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var body = doc.CreateElement("body");
        var app = doc.CreateElement("div");
        app.SetAttribute("id", "app");
        doc.AppendChild(html);
        html.AppendChild(head);
        html.AppendChild(body);
        body.AppendChild(app);

        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(
            DocumentUrl: "http://localhost:8088/blazor-status/"));
        return runtime;
    }

    private static (JsRuntime Runtime, WebEventLoop Loop, Document Document) BuildEnvWithTimersAndModuleHost()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var body = doc.CreateElement("body");
        var app = doc.CreateElement("div");
        var shell = doc.CreateElement("div");
        var title = doc.CreateElement("span");
        var boot = doc.CreateElement("span");
        var counter = doc.CreateElement("button");

        app.SetAttribute("id", "app");
        boot.SetAttribute("id", "blazor-boot");
        boot.TextContent = "booting Blazor";
        title.TextContent = "Blazor WASM Island";
        counter.TextContent = "0";

        doc.AppendChild(html);
        html.AppendChild(head);
        html.AppendChild(body);
        body.AppendChild(app);
        app.AppendChild(shell);
        shell.AppendChild(title);
        shell.AppendChild(boot);
        shell.AppendChild(counter);

        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(
            DocumentUrl: "http://localhost:8088/blazor-status/"));
        var loop = new WebEventLoop();
        TimersBinding.Install(runtime, loop);
        _ = new ModuleLoader(runtime, new MockDotnetModuleHost());
        return (runtime, loop, doc);
    }

    private static void Eval(JsRuntime runtime, string source, string? label = null)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = label is null ? JsCompiler.Compile(program) : JsCompiler.Compile(program, label);
        new JsVm(runtime).Run(chunk);
    }

    private static List<(string Source, string Label)> LoadPublishedFixtureScripts()
    {
        var root = LocateRepoRoot();
        var fixtureRoot = Path.Combine(root, "testdata", "sites", "blazor-status");
        var html = File.ReadAllText(Path.Combine(fixtureRoot, "index.html"));
        var scripts = new List<(string Source, string Label)>();
        var position = 0;
        while (true)
        {
            var tagStart = html.IndexOf("<script", position, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) return scripts;
            var tagEnd = html.IndexOf('>', tagStart);
            var close = html.IndexOf("</script>", tagEnd, StringComparison.OrdinalIgnoreCase);
            if (tagEnd < 0 || close < 0) throw new InvalidOperationException("Malformed Blazor fixture script tag.");

            var tag = html[tagStart..(tagEnd + 1)];
            if (tag.Contains("src=\"_framework/blazor.webassembly.js\"", StringComparison.Ordinal))
            {
                scripts.Add((File.ReadAllText(Path.Combine(fixtureRoot, "_framework", "blazor.webassembly.js")),
                    "http://localhost:8088/blazor-status/_framework/blazor.webassembly.js"));
            }
            else
            {
                scripts.Add((html[(tagEnd + 1)..close], "<inline>"));
            }
            position = close + "</script>".Length;
        }
    }

    private sealed class MockDotnetModuleHost : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer)
        {
            if (Uri.TryCreate(specifier, UriKind.Absolute, out var absolute))
                return absolute.ToString();
            var baseUri = Uri.TryCreate(referrer, UriKind.Absolute, out var parsed)
                ? parsed
                : new Uri("http://localhost:8088/blazor-status/");
            return Uri.TryCreate(baseUri, specifier, out var resolved) ? resolved.ToString() : null;
        }

        public string? FetchSource(string resolvedUrl)
        {
            if (!resolvedUrl.EndsWith("/_framework/dotnet.js", StringComparison.Ordinal))
                return null;

            return """
                export const dotnet = {
                  withApplicationCulture() { return this; },
                  withApplicationEnvironment() { return this; },
                  withResourceLoader() { return this; },
                  withModuleConfig() { return this; },
                  create() {
                    globalThis.__dotnetCreateHit = true;
                    return Promise.reject(new Error("mock dotnet stop"));
                  }
                };
                """;
        }
    }

    private sealed class PublishedFixtureModuleHost : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer)
        {
            if (Uri.TryCreate(specifier, UriKind.Absolute, out var absolute))
                return absolute.ToString();
            var baseUri = Uri.TryCreate(referrer, UriKind.Absolute, out var parsed)
                ? parsed
                : new Uri("http://localhost:8088/blazor-status/");
            return Uri.TryCreate(baseUri, specifier, out var resolved) ? resolved.ToString() : null;
        }

        public string? FetchSource(string resolvedUrl)
        {
            if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Port != 8088
                || !uri.AbsolutePath.StartsWith("/blazor-status/", StringComparison.Ordinal))
                return null;

            var relative = uri.AbsolutePath["/blazor-status/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(LocateRepoRoot(), "testdata", "sites", "blazor-status", relative);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starling.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Starling.slnx walking up from the test binary.");
        return dir.FullName;
    }
}
