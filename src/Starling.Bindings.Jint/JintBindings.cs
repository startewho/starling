namespace Starling.Bindings.Jint;

/// <summary>
/// Single registration point that installs every Web-API binding family onto a
/// <see cref="JintBackendContext"/>. Each family lives in its own file with a
/// focused <c>Install(JintBackendContext)</c> method.
/// </summary>
/// <remarks>
/// Order matters where one family's prototype inherits another's:
/// EventTarget, Node/Element/Document, Window + Storage/History/Performance,
/// timers/requestAnimationFrame, fetch/XMLHttpRequest, Observers/crypto/cookies,
/// then the module loader. Cookies run after NodeBindings so the
/// DocumentPrototype slot exists.
/// </remarks>
public static class JintBindings
{
    public static void InstallAll(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // EventTarget + Event. Node.prototype inherits this.
        EventTargetBinding.Install(ctx);

        // Node / Element / Document.
        NodeBindings.Install(ctx);

        // Window/global + companions.
        WindowBinding.Install(ctx);
        CssBinding.Install(ctx);
        StorageBinding.Install(ctx);
        HistoryBinding.Install(ctx);
        PerformanceBinding.Install(ctx);

        // Timers + requestAnimationFrame on the simulated loop.
        TimersBinding.Install(ctx);
        AnimationFrameBinding.Install(ctx);

        // Core URL + Encoding APIs. Install before FetchBinding: the fetch
        // bootstrap's Response.blob().text() uses `new TextDecoder()`, and page
        // code commonly resolves request URLs with `new URL(...)`.
        UrlBinding.Install(ctx);
        EncodingBinding.Install(ctx);

        // Network.
        FetchBinding.Install(ctx);
        XhrBinding.Install(ctx);

        // Observers, crypto, cookies. Cookies run after NodeBindings.
        ObserversBinding.Install(ctx);
        CryptoBinding.Install(ctx);
        CookieBinding.Install(ctx);

        // ES modules.
        ModuleLoader.Install(ctx);
    }
}
