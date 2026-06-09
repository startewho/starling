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

        // Live DOM collections (NodeList / HTMLCollection / DOMTokenList). Install
        // before NodeBindings so the interface prototypes exist for the wrappers
        // the Node bindings hand back.
        CollectionsBinding.Install(ctx);

        // Node / Element / Document.
        NodeBindings.Install(ctx);

        // DOMException — the error type DOM methods throw. Installed after
        // NodeBindings (which no longer defines a DOMException marker) so the real
        // constructible interface wins.
        DomExceptionBinding.Install(ctx);

        // DOM §6 traversal (NodeFilter / TreeWalker / NodeIterator). Needs the
        // DocumentPrototype slot from NodeBindings for createTreeWalker/createNodeIterator.
        TraversalBinding.Install(ctx);

        // DOM §4.6 Range / StaticRange + document.createRange.
        RangeBinding.Install(ctx);

        // Selection API (window/document.getSelection). After RangeBinding so the
        // Range wrapper exists for getRangeAt/addRange.
        SelectionBinding.Install(ctx);

        // CSSOM (document.styleSheets, element.sheet, CSSStyleSheet/Rule/Declaration).
        CssomBinding.Install(ctx);

        // DOM §4.9 Attr / NamedNodeMap (real element.attributes, Attr-node methods,
        // document.createAttribute). Needs Element/Document prototypes from NodeBindings.
        AttrBinding.Install(ctx);

        // CSS Font Loading (document.fonts + FontFace constructor).
        FontFaceBinding.Install(ctx);

        // Web Animations (element.animate → Animation + KeyframeEffect).
        WebAnimationsBinding.Install(ctx);

        // WebAssembly (Wasmtime-backed Module/Instance/Memory/Table + errors).
        WebAssemblyBinding.Install(ctx);

        // IFrame browsing context (contentDocument/contentWindow + src load).
        // After WindowBinding (defaultView override) and NodeBindings prototypes.
        IFrameBinding.Install(ctx);

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

        // Blob / File / FormData (real classes). Before FetchBinding so its
        // Response.blob() can mint a real Blob; after NodeBindings for the
        // __starlingFormDataEntries hook.
        BlobFileFormDataBinding.Install(ctx);

        // Network.
        FetchBinding.Install(ctx);
        XhrBinding.Install(ctx);

        // Observers, crypto, cookies. Cookies run after NodeBindings.
        MutationObserverBinding.Install(ctx); // real MutationObserver (records + delivery)
        ObserversBinding.Install(ctx);        // Intersection / Resize (surface-level)
        CryptoBinding.Install(ctx);
        CookieBinding.Install(ctx);

        // Core utility globals: btoa / atob / structuredClone.
        CoreWebApiBinding.Install(ctx);

        // Full console (idempotent — the live session installs its own with a real
        // sink before InstallAll; this serves bare contexts / parity tests).
        ConsoleBinding.Install(ctx);

        // ES modules.
        ModuleLoader.Install(ctx);
    }
}
