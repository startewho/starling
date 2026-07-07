using AwesomeAssertions;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

// A Response whose backing bytes are null (e.g. a bodyless HTTP response) must
// read as an empty body rather than throwing a raw .NET ArgumentNullException
// out of text()/arrayBuffer()/json().
[TestClass]
public sealed class FetchNullBodyTests
{
    [TestMethod]
    public async Task Text_on_null_body_resolves_empty_string()
    {
        var env = FetchTests.NewEnv("http://localhost/");
        InstallNullBodyResponse(env.Runtime);

        FetchTests.Eval(env.Runtime, """
            globalThis.result = null;
            resp.text().then(function(t) { globalThis.result = t; });
        """);
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("result").IsString);
        env.Runtime.GetGlobal("result").AsString.Should().Be("");
    }

    [TestMethod]
    public async Task ArrayBuffer_on_null_body_resolves_zero_length()
    {
        var env = FetchTests.NewEnv("http://localhost/");
        InstallNullBodyResponse(env.Runtime);

        FetchTests.Eval(env.Runtime, """
            globalThis.result = null;
            resp.arrayBuffer().then(function(b) { globalThis.result = b.byteLength; });
        """);
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("result").IsNumber);
        env.Runtime.GetGlobal("result").AsNumber.Should().Be(0);
    }

    [TestMethod]
    public async Task Json_on_null_body_rejects_with_syntax_error_not_dotnet_exception()
    {
        var env = FetchTests.NewEnv("http://localhost/");
        InstallNullBodyResponse(env.Runtime);

        // Empty body is not valid JSON, so json() rejects with a JS SyntaxError.
        // The point is that it rejects cleanly rather than crashing with a raw
        // .NET ArgumentNullException from Encoding.UTF8.GetString(null).
        FetchTests.Eval(env.Runtime, """
            globalThis.result = null;
            resp.json().then(
                function() { globalThis.result = 'resolved'; },
                function(e) { globalThis.result = (e && e.constructor && e.constructor.name) || 'other'; });
        """);
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("result").IsString);
        env.Runtime.GetGlobal("result").AsString.Should().Be("SyntaxError");
    }

    private static void InstallNullBodyResponse(JsRuntime runtime)
    {
        var realm = runtime.Realm;
        var headers = new HeadersObject(realm.HeadersPrototype);
        var resp = new ResponseObject(realm.ResponsePrototype, 200, "OK", headers, null!, "", false);
        runtime.SetGlobal("resp", JsValue.Object(resp));
    }
}
