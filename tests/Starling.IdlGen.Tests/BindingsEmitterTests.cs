using AwesomeAssertions;
using Starling.IdlGen.Emit;
using Starling.IdlGen.Mapping;
using Starling.IdlGen.Merging;
using Starling.IdlGen.Overrides;
using Starling.IdlGen.Parsing;

namespace Starling.IdlGen.Tests;

[TestClass]
public class BindingsEmitterTests
{
    private static string EmitCoreDom()
    {
        string root = FindRepoRoot();
        string idlDir = Path.Combine(root, "testdata", "webref", "idl");
        var model = IdlMerger.Merge(Directory.EnumerateFiles(idlDir, "*.idl")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => IdlParser.Parse(File.ReadAllText(p))));
        var overrides = OverrideSet.Load(Path.Combine(root, "tools", "Starling.IdlGen", "overrides", "overrides.json"));
        return new BindingsEmitter(model, new ClrMap(), overrides).Emit(BindingsEmitter.CoreDomInterfaces, out _);
    }

    [TestMethod]
    public void Generated_file_matches_committed_baseline()
    {
        // The committed generated file is the golden baseline. Re-running the
        // emitter must reproduce it exactly. If this fails, run
        //   dotnet run --project tools/Starling.IdlGen -- emit
        // and review the diff before committing.
        string baselinePath = Path.Combine(FindRepoRoot(),
            "src", "Starling.Bindings", "Generated", "CoreDomBindings.g.cs");
        File.Exists(baselinePath).Should().BeTrue("the generated baseline must be committed");

        string expected = File.ReadAllText(baselinePath).ReplaceLineEndings("\n");
        string actual = EmitCoreDom().ReplaceLineEndings("\n");
        actual.Should().Be(expected);
    }

    [TestMethod]
    public void Emit_is_deterministic()
    {
        EmitCoreDom().Should().Be(EmitCoreDom());
    }

    [TestMethod]
    public void Generated_accessors_use_the_real_engine_api()
    {
        string code = EmitCoreDom();
        code.Should().Contain("EventTargetBinding.DefineAccessor(realm, proto, \"nodeValue\"");
        code.Should().Contain("DomWrappers.UnwrapAs<Element>(thisV)");
        code.Should().Contain("JsValue.String(v)");
        // A writable attribute emits a setter.
        code.Should().Contain("h.Id = JsValue.ToStringValue");
        // Override layer: tagName and nodeName use custom getter code, not the
        // mechanical mapping.
        code.Should().Contain("NodeBindings.NormalizeNodeName");
        code.Should().Contain("ToUpperInvariant");
    }

    [TestMethod]
    public void Add_layer_injects_verbatim_binding_code()
    {
        string idlDir = Path.Combine(FindRepoRoot(), "testdata", "webref", "idl");
        var model = IdlMerger.Merge(Directory.EnumerateFiles(idlDir, "*.idl")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => IdlParser.Parse(File.ReadAllText(p))));
        var overrides = OverrideSet.Parse(
            """{ "add": { "Node": ["EventTargetBinding.DefineMethod(realm, proto, \"__hostExtra\", (thisV, _) => JsValue.Null, length: 0);"] } }""");

        string code = new BindingsEmitter(model, new ClrMap(), overrides).Emit(BindingsEmitter.CoreDomInterfaces, out _);

        code.Should().Contain("DefineMethod(realm, proto, \"__hostExtra\"");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starling.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
