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

    [TestMethod]
    public async Task Compile_decodes_buffer_source_module()
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
            globalThis.answer = null;
            WebAssembly.compile(bytes).then(function (module) {
                const instance = new WebAssembly.Instance(module);
                globalThis.answer = instance.exports.add(15, 27);
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsNumber);

        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public async Task Instantiate_streaming_fetches_and_instantiates_imported_function_module()
    {
        await using var server = await LocalServer.Start(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/wasm";
            var bytes = ImportedFunctionModuleBytes();
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        });
        var env = FetchTests.NewEnv(server.BaseUrl);

        FetchTests.Eval(env.Runtime, $$"""
            globalThis.answer = null;
            WebAssembly.instantiateStreaming(fetch('{{server.BaseUrl}}/imported-function.wasm'), {
                env: {
                    hostAdd: function (a, b) { return a + b + 1; }
                }
            }).then(function (result) {
                globalThis.answer = result.instance.exports.call(10, 20);
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsNumber);

        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(31);
    }

    [TestMethod]
    public async Task Compile_streaming_fetches_module_for_later_instantiation()
    {
        await using var server = await LocalServer.Start(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/wasm";
            var bytes = ImportedFunctionModuleBytes();
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        });
        var env = FetchTests.NewEnv(server.BaseUrl);

        FetchTests.Eval(env.Runtime, $$"""
            globalThis.answer = null;
            WebAssembly.compileStreaming(fetch('{{server.BaseUrl}}/imported-function.wasm'))
                .then(function (module) {
                    const instance = new WebAssembly.Instance(module, {
                        env: {
                            hostAdd: function (a, b) { return a + b + 7; }
                        }
                    });
                    globalThis.answer = instance.exports.call(2, 3);
                });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsNumber);

        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(12);
    }

    [TestMethod]
    public async Task Imported_function_can_be_called_from_wasm()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ImportedFunctionModuleBytes())}}]);
            globalThis.answer = null;
            WebAssembly.instantiate(bytes, {
                env: {
                    hostAdd: function (a, b) { return a + b + 3; }
                }
            }).then(function (result) {
                globalThis.answer = result.instance.exports.call(4, 5);
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsNumber);

        env.Runtime.GetGlobal("answer").AsNumber.Should().Be(12);
    }

    [TestMethod]
    public async Task Imported_zero_arg_module_closure_runs_scheduler_guard()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ImportedZeroArgModuleBytes())}}]);
            let Jo;
            let calls = 0;
            const Ke = {
                safeSetTimeout(callback, delay) {
                    calls++;
                    return 7;
                }
            };
            function Ko() {}
            function schedule() {
                Jo || (Jo = Ke.safeSetTimeout(Ko, 0));
            }
            globalThis.answer = null;
            WebAssembly.instantiate(bytes, { env: { schedule } }).then(function (result) {
                result.instance.exports.call();
                globalThis.answer = calls + ':' + Jo;
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsString);

        env.Runtime.GetGlobal("answer").AsString.Should().Be("1:7");
    }

    [TestMethod]
    public async Task Imported_function_throw_surfaces_to_export_caller()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ImportedZeroArgModuleBytes())}}]);
            globalThis.answer = null;
            WebAssembly.instantiate(bytes, {
                env: {
                    schedule() { throw new Error('boom'); }
                }
            }).then(function (result) {
                try {
                    result.instance.exports.call();
                    globalThis.answer = 'unexpected';
                } catch (error) {
                    globalThis.answer = error && error.message ? error.message : String(error);
                }
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsString);

        env.Runtime.GetGlobal("answer").AsString.Should().Contain("boom");
    }

    [TestMethod]
    public async Task Imported_memory_is_visible_through_live_buffer()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ImportedMemoryModuleBytes())}}]);
            const memory = new WebAssembly.Memory({ initial: 1 });
            const view = new Uint8Array(memory.buffer);
            view[0] = 7;
            globalThis.before = null;
            globalThis.after = null;
            WebAssembly.instantiate(bytes, { env: { memory: memory } }).then(function (result) {
                globalThis.before = result.instance.exports.read0();
                result.instance.exports.write0(9);
                globalThis.after = view[0];
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("after").IsNumber);

        env.Runtime.GetGlobal("before").AsNumber.Should().Be(7);
        env.Runtime.GetGlobal("after").AsNumber.Should().Be(9);
    }

    [TestMethod]
    public async Task Exported_table_exposes_length_get_set_and_grow()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ExportedTableModuleBytes())}}]);
            globalThis.shape = null;
            WebAssembly.instantiate(bytes).then(function (result) {
                const exports = result.instance.exports;
                const table = exports.__indirect_function_table;
                const first = table.get(0);
                const before =
                    table.length + ':' +
                    (table instanceof WebAssembly.Table) + ':' +
                    (typeof first) + ':' +
                    first(41);
                table.set(0, exports.inc);
                const afterSet = table.get(0)(9);
                const oldLength = table.grow(2);
                const afterGrow =
                    table.length + ':' +
                    (table.get(1) === null) + ':' +
                    (table.get(2) === null);
                globalThis.shape = before + ':' + afterSet + ':' + oldLength + ':' + afterGrow;
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("shape").IsString);

        env.Runtime.GetGlobal("shape").AsString.Should().Be("1:true:function:42:10:1:3:true:true");
    }

    [TestMethod]
    public async Task Exported_memory_buffer_stays_live_after_wasm_growth()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ExportedMemoryGrowModuleBytes())}}]);
            globalThis.answer = null;
            WebAssembly.instantiate(bytes).then(function (result) {
                const exports = result.instance.exports;
                const view = new Uint8Array(exports.memory.buffer);
                const oldPages = exports.grow(1);
                view[0] = 77;
                globalThis.answer =
                    oldPages + ':' +
                    exports.memory.buffer.byteLength + ':' +
                    exports.read0();
            });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("answer").IsString);

        env.Runtime.GetGlobal("answer").AsString.Should().Be("1:131072:77");
    }

    [TestMethod]
    public async Task Missing_import_reports_module_and_name()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, $$"""
            const bytes = new Uint8Array([{{ByteList(ImportedFunctionModuleBytes())}}]);
            globalThis.error = null;
            WebAssembly.instantiate(bytes, {}).then(
                function () { globalThis.error = 'unexpected'; },
                function (err) { globalThis.error = err.message; });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("error").IsString);

        env.Runtime.GetGlobal("error").AsString.Should().Contain("env.hostAdd");
    }

    [TestMethod]
    public async Task WebAssembly_error_constructors_match_browser_shape()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, """
            const runtimeError = new WebAssembly.RuntimeError("boom", { cause: 7 });
            globalThis.errorShape =
                (runtimeError instanceof WebAssembly.RuntimeError) + ":" +
                (runtimeError instanceof Error) + ":" +
                runtimeError.name + ":" +
                runtimeError.message + ":" +
                runtimeError.cause + ":" +
                String(runtimeError) + ":" +
                (new WebAssembly.LinkError("link") instanceof WebAssembly.LinkError);

            globalThis.compileErrorShape = null;
            WebAssembly.compile(new Uint8Array([0])).then(
                function () { globalThis.compileErrorShape = "unexpected"; },
                function (err) {
                    globalThis.compileErrorShape =
                        (err instanceof WebAssembly.CompileError) + ":" +
                        (err instanceof Error) + ":" +
                        err.name;
                });
        """);

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("compileErrorShape").IsString);

        env.Runtime.GetGlobal("errorShape").AsString.Should()
            .Be("true:true:RuntimeError:boom:7:RuntimeError: boom:true");
        env.Runtime.GetGlobal("compileErrorShape").AsString.Should().Be("true:true:CompileError");
    }

    private static string ByteList(byte[] bytes) =>
        string.Join(",", bytes.Select(b => "0x" + b.ToString("x2")));

    private static byte[] ImportedFunctionModuleBytes() =>
    [
        0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
        0x01,0x07,0x01,0x60,0x02,0x7f,0x7f,0x01,0x7f,
        0x02,0x0f,0x01,0x03,0x65,0x6e,0x76,0x07,0x68,0x6f,0x73,0x74,0x41,0x64,0x64,0x00,0x00,
        0x03,0x02,0x01,0x00,
        0x07,0x08,0x01,0x04,0x63,0x61,0x6c,0x6c,0x00,0x01,
        0x0a,0x0a,0x01,0x08,0x00,0x20,0x00,0x20,0x01,0x10,0x00,0x0b,
    ];

    private static byte[] ImportedZeroArgModuleBytes() =>
    [
        0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
        0x01,0x04,0x01,0x60,0x00,0x00,
        0x02,0x10,0x01,0x03,0x65,0x6e,0x76,0x08,0x73,0x63,0x68,0x65,0x64,0x75,0x6c,0x65,0x00,0x00,
        0x03,0x02,0x01,0x00,
        0x07,0x08,0x01,0x04,0x63,0x61,0x6c,0x6c,0x00,0x01,
        0x0a,0x06,0x01,0x04,0x00,0x10,0x00,0x0b,
    ];

    private static byte[] ImportedMemoryModuleBytes() =>
    [
        0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
        0x01,0x09,0x02,0x60,0x00,0x01,0x7f,0x60,0x01,0x7f,0x00,
        0x02,0x0f,0x01,0x03,0x65,0x6e,0x76,0x06,0x6d,0x65,0x6d,0x6f,0x72,0x79,0x02,0x00,0x01,
        0x03,0x03,0x02,0x00,0x01,
        0x07,0x12,0x02,0x05,0x72,0x65,0x61,0x64,0x30,0x00,0x00,0x06,0x77,0x72,0x69,0x74,0x65,0x30,0x00,0x01,
        0x0a,0x13,0x02,0x07,0x00,0x41,0x00,0x2d,0x00,0x00,0x0b,0x09,0x00,0x41,0x00,0x20,0x00,0x3a,0x00,0x00,0x0b,
    ];

    private static byte[] ExportedTableModuleBytes() =>
    [
        0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
        0x01,0x06,0x01,0x60,0x01,0x7f,0x01,0x7f,
        0x03,0x02,0x01,0x00,
        0x04,0x04,0x01,0x70,0x00,0x01,
        0x07,0x23,0x02,0x19,0x5f,0x5f,0x69,0x6e,0x64,0x69,0x72,0x65,0x63,0x74,0x5f,0x66,0x75,0x6e,0x63,0x74,0x69,0x6f,0x6e,0x5f,0x74,0x61,0x62,0x6c,0x65,0x01,0x00,0x03,0x69,0x6e,0x63,0x00,0x00,
        0x09,0x07,0x01,0x00,0x41,0x00,0x0b,0x01,0x00,
        0x0a,0x09,0x01,0x07,0x00,0x20,0x00,0x41,0x01,0x6a,0x0b,
        0x00,0x14,0x04,0x6e,0x61,0x6d,0x65,0x01,0x06,0x01,0x00,0x03,0x69,0x6e,0x63,0x04,0x05,0x01,0x00,0x02,0x74,0x30,
    ];

    private static byte[] ExportedMemoryGrowModuleBytes() =>
    [
        0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
        0x01,0x0a,0x02,0x60,0x01,0x7f,0x01,0x7f,0x60,0x00,0x01,0x7f,
        0x03,0x03,0x02,0x00,0x01,
        0x05,0x04,0x01,0x01,0x01,0x02,
        0x07,0x19,0x03,0x06,0x6d,0x65,0x6d,0x6f,0x72,0x79,0x02,0x00,0x04,0x67,0x72,0x6f,0x77,0x00,0x00,0x05,0x72,0x65,0x61,0x64,0x30,0x00,0x01,
        0x0a,0x10,0x02,0x06,0x00,0x20,0x00,0x40,0x00,0x0b,0x07,0x00,0x41,0x00,0x2d,0x00,0x00,0x0b,
    ];
}
