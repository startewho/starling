using Jint.Native;
using Starling.Common.Diagnostics;

namespace Starling.Bindings.Jint;

/// <summary>
/// HTML §6.7.3 <c>document.cookie</c> on the Jint backend.
/// </summary>
/// <remarks>
/// <see cref="Starling.Js.Hosting.ScriptSessionOptions"/> does not yet expose a
/// CookieJar to bindings. Cookies live in <c>StarlingHttpClient</c> for now.
/// This binding therefore installs a graceful no-op accessor: the getter
/// returns <c>""</c>; the setter logs a debug diagnostic and discards the
/// value. When a session-scoped CookieJar lands, this is the only file to
/// teach about it.
/// </remarks>
internal static class CookieBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;
        var documentProto = ctx.Wrappers.DocumentPrototype;
        if (documentProto is null)
        {
            // NodeBindings has not installed a Document prototype slot.
            // Without it we have nowhere idempotent to attach the accessor.
            ctx.Diag?.Log(DiagLevel.Debug, "jint.cookie",
                "DocumentPrototype is null; document.cookie accessor not installed.");
            return;
        }

        JintInterop.DefineAccessor(engine, documentProto, "cookie",
            (_, _) => JintInterop.Str(""),
            (_, args) =>
            {
                var value = args.Length > 0 ? args[0].ToString() : "";
                ctx.Diag?.Log(DiagLevel.Debug, "jint.cookie",
                    $"document.cookie= ignored (no CookieJar wired): {Truncate(value)}");
                return JsValue.Undefined;
            });
    }

    private static string Truncate(string s, int max = 120)
        => s.Length <= max ? s : s[..max] + "…";
}
