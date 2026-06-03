// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;

namespace Starling.Bindings.Tests;

[TestClass]
public sealed class WebAssemblyBindingTests
{
    [TestMethod]
    public async Task Instantiate_self_contained_numeric_module()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, """
            const bytes = new Uint8Array([
                0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
                0x01,0x07,0x01,0x60,0x02,0x7f,0x7f,0x01,0x7f,
                0x03,0x02,0x01,0x00,
                0x07,0x07,0x01,0x03,0x61,0x64,0x64,0x00,0x00,
                0x0a,0x09,0x01,0x07,0x00,0x20,0x00,0x20,0x01,0x6a,0x0b
            ]);
            globalThis.valid = WebAssembly.validate(bytes);
            globalThis.answer = null;
            globalThis.shapes = null;
            WebAssembly.instantiate(bytes).then(function (result) {
                globalThis.answer = result.instance.exports.add(40, 2);
                globalThis.shapes =
                    (result.module instanceof WebAssembly.Module) + ':' +
                    (result.instance instanceof WebAssembly.Instance);
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsNumber);

        env.Runtime.GetGlobal("valid").AsBool.Should().BeTrue();
        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(42);
        env.Runtime.GetGlobal("shapes").AsString.Should().Be("true:true");
    }

    [TestMethod]
    public void Module_and_instance_constructors_work()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, """
            const bytes = new Uint8Array([
                0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
                0x01,0x07,0x01,0x60,0x02,0x7f,0x7f,0x01,0x7f,
                0x03,0x02,0x01,0x00,
                0x07,0x07,0x01,0x03,0x61,0x64,0x64,0x00,0x00,
                0x0a,0x09,0x01,0x07,0x00,0x20,0x00,0x20,0x01,0x6a,0x0b
            ]);
            const module = new WebAssembly.Module(bytes);
            const instance = new WebAssembly.Instance(module);
            globalThis.answer = instance.exports.add(7, 5);
        """);

        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(12);
    }
}
