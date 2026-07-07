// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Starling.Bindings;
using Starling.Html;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.BindingSurface.Tests;

[TestClass]
public sealed class IdlSurfaceManifestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void Backend_matches_required_idl_surface_manifest()
    {
        var runtime = NewStarlingRuntime();
        foreach (var member in RequiredMembers())
        {
            string expected = ExpectedSurface(member);
            string starling = ReadStarlingSurface(runtime, member);

            starling.Should().Be(expected, $"Starling JS {member.Interface}.{member.Name} must match the IDL surface manifest");
        }
    }

    private static JsRuntime NewStarlingRuntime()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><head><title>Hi</title></head><body><div id='main'></div></body></html>");
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return runtime;
    }

    private static string ReadStarlingSurface(JsRuntime runtime, SurfaceMember member)
    {
        string source = "result = " + SurfaceCheckScript(member);
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return JsValue.ToStringValue(runtime.GetGlobal("result"));
    }

    private static string SurfaceCheckScript(SurfaceMember member)
    {
        string iface = JsonSerializer.Serialize(member.Interface);
        string name = JsonSerializer.Serialize(member.Name);
        string isStatic = member.Static ? "true" : "false";
        return $$"""
            (function(){
              var ifaceName = {{iface}};
              var memberName = {{name}};
              var ctor = globalThis[ifaceName];
              if (typeof ctor !== 'function') return ifaceName + ': missing interface';
              var target = {{isStatic}} ? ctor : ctor.prototype;
              if (!target) return ifaceName + '.' + memberName + ': missing target';
              var desc = Object.getOwnPropertyDescriptor(target, memberName);
              if (!desc) return ifaceName + '.' + memberName + ': missing descriptor';
              var shape = typeof desc.value === 'function'
                ? 'method'
                : ((typeof desc.get === 'function' || typeof desc.set === 'function') ? 'accessor' : 'data');
              return [
                shape,
                desc.writable === true ? 'w' : 'r',
                desc.enumerable === true ? 'e' : 'ne',
                desc.configurable === true ? 'c' : 'nc',
                typeof desc.get === 'function' ? 'get' : 'noget',
                typeof desc.set === 'function' ? 'set' : 'noset',
                typeof desc.value === 'function' ? String(desc.value.length) : '-'
              ].join('|');
            })()
            """;
    }

    private static string ExpectedSurface(SurfaceMember member)
    {
        var descriptor = member.Descriptor;
        string writable = descriptor.Writable is true ? "w" : "r";
        string enumerable = descriptor.Enumerable ? "e" : "ne";
        string configurable = descriptor.Configurable ? "c" : "nc";
        string hasGetter = descriptor.Kind == "accessor" ? "get" : "noget";
        string hasSetter = descriptor.Kind == "accessor" && member.Readonly is false ? "set" : "noset";
        string length = descriptor.Kind == "method"
            ? (member.RequiredArguments ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "-";

        return string.Join('|',
            descriptor.Kind,
            writable,
            enumerable,
            configurable,
            hasGetter,
            hasSetter,
            length);
    }

    private static IReadOnlyList<SurfaceMember> RequiredMembers()
    {
        string manifestPath = Path.Combine(FindRepoRoot(), "testdata", "webref", "core-dom-surface.json");
        var manifest = JsonSerializer.Deserialize<SurfaceManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions)
            ?? throw new InvalidOperationException("Surface manifest could not be read.");
        return [.. manifest.Members.Where(m => m.Required)];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starling.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

public sealed record SurfaceManifest(
    [property: JsonPropertyName("members")] IReadOnlyList<SurfaceMember> Members);

public sealed record SurfaceMember(
    [property: JsonPropertyName("interface")] string Interface,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("static")] bool Static,
    [property: JsonPropertyName("readonly")] bool? Readonly,
    [property: JsonPropertyName("requiredArguments")] int? RequiredArguments,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("descriptor")] SurfaceDescriptor Descriptor);

public sealed record SurfaceDescriptor(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("writable")] bool? Writable,
    [property: JsonPropertyName("enumerable")] bool Enumerable,
    [property: JsonPropertyName("configurable")] bool Configurable);
