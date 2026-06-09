using AwesomeAssertions;
using Starling.IdlGen.Mapping;
using Starling.IdlGen.Merging;
using Starling.IdlGen.Model;
using Starling.IdlGen.Parsing;

namespace Starling.IdlGen.Tests;

[TestClass]
public class TypeMapperTests
{
    private static TypeMapper MapperFor(string idl) =>
        new(IdlMerger.Merge([IdlParser.Parse(idl)]));

    private static IdlType ArgType(string idl, string iface = "I")
    {
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        return model.Interfaces[iface].Members.OfType<IdlOperation>().First().Arguments.First().Type;
    }

    [TestMethod]
    [DataRow("boolean", "bool")]
    [DataRow("long", "int")]
    [DataRow("unsigned long", "uint")]
    [DataRow("unsigned long long", "ulong")]
    [DataRow("octet", "byte")]
    [DataRow("double", "double")]
    [DataRow("unrestricted double", "double")]
    [DataRow("DOMString", "string")]
    [DataRow("USVString", "string")]
    public void Primitive_and_string_mappings(string idl, string expected)
    {
        var mapper = MapperFor($"interface I {{ undefined f({idl} x); }};");
        mapper.Map(ArgType($"interface I {{ undefined f({idl} x); }};"), TypePosition.Parameter)
            .CSharp.Should().Be(expected);
    }

    [TestMethod]
    public void Nullable_value_type_gets_question_mark()
    {
        var mapper = MapperFor("interface I { undefined f(long? x); };");
        var m = mapper.Map(ArgType("interface I { undefined f(long? x); };"), TypePosition.Parameter);
        m.CSharp.Should().Be("int?");
        m.Nullable.Should().BeTrue();
    }

    [TestMethod]
    public void Nullable_string_gets_question_mark()
    {
        string idl = "interface I { undefined f(DOMString? x); };";
        var m = MapperFor(idl).Map(ArgType(idl), TypePosition.Parameter);
        m.CSharp.Should().Be("string?");
        m.Kind.Should().Be(TypeKind.String);
        m.Nullable.Should().BeTrue();
    }

    [TestMethod]
    public void Sequence_position_changes_collection_type()
    {
        string idl = "interface I { undefined f(sequence<DOMString> x); };";
        var mapper = MapperFor(idl);
        mapper.Map(ArgType(idl), TypePosition.Parameter).CSharp.Should().Be("IEnumerable<string>");
        mapper.Map(ArgType(idl), TypePosition.Return).CSharp.Should().Be("IReadOnlyList<string>");
    }

    [TestMethod]
    public void Record_maps_to_readonly_dictionary()
    {
        string idl = "interface I { undefined f(record<DOMString, long> x); };";
        MapperFor(idl).Map(ArgType(idl), TypePosition.Parameter).CSharp
            .Should().Be("IReadOnlyDictionary<string, int>");
    }

    [TestMethod]
    public void Promise_maps_to_task()
    {
        string idl1 = "interface I { undefined f(Promise<long> x); };";
        MapperFor(idl1).Map(ArgType(idl1), TypePosition.Parameter).CSharp.Should().Be("Task<int>");

        string idl2 = "interface I { undefined f(Promise<undefined> x); };";
        MapperFor(idl2).Map(ArgType(idl2), TypePosition.Parameter).CSharp.Should().Be("Task");
    }

    [TestMethod]
    public void Union_maps_to_generated_name()
    {
        string idl = "interface I { undefined f((Node or DOMString) x); };";
        var mapper = MapperFor(idl);
        var m = mapper.Map(ArgType(idl), TypePosition.Parameter);
        m.Kind.Should().Be(TypeKind.Union);
        m.CSharp.Should().Be("NodeOrString");
    }

    [TestMethod]
    public void Enum_dictionary_interface_classified()
    {
        string idl = """
            enum Mode { "a" };
            dictionary Opts { boolean x = false; };
            interface Node {};
            interface I {
              undefined a(Mode m);
              undefined b(Opts o);
              undefined c(Node n);
            };
            """;
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        var mapper = new TypeMapper(model);
        var ops = model.Interfaces["I"].Members.OfType<IdlOperation>().ToList();

        mapper.Map(ops[0].Arguments[0].Type, TypePosition.Parameter).Kind.Should().Be(TypeKind.Enum);
        mapper.Map(ops[1].Arguments[0].Type, TypePosition.Parameter).Kind.Should().Be(TypeKind.Dictionary);
        mapper.Map(ops[2].Arguments[0].Type, TypePosition.Parameter).Kind.Should().Be(TypeKind.Interface);
    }

    [TestMethod]
    public void Any_and_object_map_to_js_value()
    {
        string idl = "interface I { undefined f(any x); };";
        MapperFor(idl).Map(ArgType(idl), TypePosition.Parameter).CSharp.Should().Be("JsValue");
    }

    [TestMethod]
    public void Typedef_union_resolves_to_union_name()
    {
        string idl = """
            interface Node {};
            typedef (Node or DOMString) Nodeish;
            interface I { undefined f(Nodeish x); };
            """;
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        var mapper = new TypeMapper(model);
        var arg = model.Interfaces["I"].Members.OfType<IdlOperation>().Single().Arguments.Single().Type;
        mapper.Map(arg, TypePosition.Parameter).CSharp.Should().Be("NodeOrString");
    }
}
