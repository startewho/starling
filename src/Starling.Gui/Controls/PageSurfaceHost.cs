using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Starling.Gui.Controls;

/// <summary>
/// Embeds a GPU swapchain surface in the page region of the Avalonia window so the
/// page can be presented straight to it (zero-copy), while the chrome stays
/// Avalonia. On macOS the host creates a click-through <c>NSView</c> backed by a
/// <c>CAMetalLayer</c>: the layer is the wgpu render target, and the view's
/// <c>hitTest:</c> returns nil so pointer/scroll events fall through to the
/// Avalonia content (the page canvas) beneath it. The raw layer pointer is exposed
/// via <see cref="MetalLayerPtr"/> for the compositor to build its swapchain on.
/// </summary>
/// <remarks>
/// Windows (child HWND) and X11 (child window) are not wired yet, so
/// <see cref="MetalLayerPtr"/> is 0 off macOS and the caller keeps the readback
/// present path.
/// </remarks>
internal sealed class PageSurfaceHost : NativeControlHost
{
    private nint _view;
    private nint _metalLayer;

    /// <summary>The window's device-pixel scale, applied to the metal layer's contentsScale.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Raised after the native surface handle becomes available (control attached).</summary>
    public event Action? SurfaceReady;

    /// <summary>True once a presentable native surface handle exists.</summary>
    public bool HasSurface => _metalLayer != 0;

    /// <summary>
    /// The raw <c>CAMetalLayer*</c> the surface present path binds its wgpu
    /// swapchain to, or 0 before the control is attached / off macOS. The caller
    /// retains no ownership — the host releases it in <see cref="DestroyNativeControlCore"/>.
    /// </summary>
    public nint MetalLayerPtr => _metalLayer;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsMacOS())
        {
            _view = MacMetal.CreatePassthroughMetalView(Scale, out _metalLayer);
            if (_view != 0)
            {
                if (_metalLayer != 0)
                    SurfaceReady?.Invoke();
                return new PlatformHandle(_view, "NSView");
            }
        }

        // Unsupported platform (or view creation failed): hand back a default child so
        // the control still attaches; the caller keeps the readback present path.
        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsMacOS() && _view != 0)
        {
            MacMetal.Release(_metalLayer);
            MacMetal.Release(_view);
            _metalLayer = 0;
            _view = 0;
            return;
        }

        base.DestroyNativeControlCore(control);
    }

    /// <summary>Updates the metal layer's contentsScale (call on DPI change).</summary>
    public void UpdateScale(double scale)
    {
        Scale = scale;
        if (_metalLayer != 0)
            MacMetal.SetContentsScale(_metalLayer, scale);
    }
}

/// <summary>
/// Minimal Objective-C runtime interop for a click-through, metal-layer-backed
/// <c>NSView</c>. Confined to macOS.
/// </summary>
internal static unsafe class MacMetal
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Objc)] private static extern nint objc_getClass(string name);
    [DllImport(Objc)] private static extern nint sel_registerName(string name);
    [DllImport(Objc)] private static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);
    [DllImport(Objc)] private static extern void objc_registerClassPair(nint cls);
    [DllImport(Objc)] private static extern nint objc_lookUpClass(string name);
    [DllImport(Objc)][return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(nint cls, nint sel, nint imp, string types);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern nint MsgSend(nint receiver, nint sel);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void MsgSendVoidPtr(nint receiver, nint sel, nint arg);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void MsgSendVoidBool(nint receiver, nint sel, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void MsgSendVoidDouble(nint receiver, nint sel, double arg);

    private static nint _passthroughClass;

    // Fields are never read in managed code — they exist only to give objc_msgSend
    // the correct by-value calling convention for hitTest:'s NSPoint argument.
#pragma warning disable CS0649
    private struct CGPoint { public double X; public double Y; }
#pragma warning restore CS0649

    // hitTest: implementation that always returns nil, so AppKit continues the hit
    // search to the views behind this overlay (the Avalonia content view), letting
    // pointer/scroll events reach the Avalonia page canvas.
    [UnmanagedCallersOnly]
    private static nint HitTestReturnNil(nint self, nint sel, CGPoint point) => 0;

    private static nint PassthroughViewClass()
    {
        if (_passthroughClass != 0) return _passthroughClass;

        // A second run in the same process (rare) would find the class already
        // registered; reuse it rather than re-allocating (which returns nil).
        var existing = objc_lookUpClass("StarlingPassthroughMetalView");
        if (existing != 0) { _passthroughClass = existing; return _passthroughClass; }

        var cls = objc_allocateClassPair(objc_getClass("NSView"), "StarlingPassthroughMetalView", 0);
        if (cls == 0) return 0;

        delegate* unmanaged<nint, nint, CGPoint, nint> imp = &HitTestReturnNil;
        // "@@:{CGPoint=dd}" — returns id, args: self (id), _cmd (SEL), point (CGPoint of two doubles).
        class_addMethod(cls, sel_registerName("hitTest:"), (nint)imp, "@@:{CGPoint=dd}");
        objc_registerClassPair(cls);
        _passthroughClass = cls;
        return cls;
    }

    /// <summary>
    /// Creates a click-through NSView backed by a fresh CAMetalLayer, returning the
    /// view pointer (NSView*) and, via <paramref name="metalLayer"/>, the layer
    /// pointer (CAMetalLayer*). Returns 0 on failure. wgpu sets the layer's device and
    /// pixel format when it creates the surface; we set contentsScale for crisp Retina.
    /// </summary>
    public static nint CreatePassthroughMetalView(double scale, out nint metalLayer)
    {
        metalLayer = 0;
        try
        {
            var viewClass = PassthroughViewClass();
            if (viewClass == 0) return 0;

            var view = MsgSend(MsgSend(viewClass, sel_registerName("alloc")), sel_registerName("init"));
            if (view == 0) return 0;

            var metalClass = objc_getClass("CAMetalLayer");
            if (metalClass == 0) return view;
            var layer = MsgSend(MsgSend(metalClass, sel_registerName("alloc")), sel_registerName("init"));
            if (layer == 0) return view;

            // The render session configures the swapchain
            // with RenderAttachment usage only, so the default framebufferOnly = YES
            // would actually suffice. We clear it anyway so the drawable also allows
            // copy/sample usages — leaving room for a future surface-readback path
            // without a reconfigure (an unsupported usage makes wgpuSurfaceConfigure
            // abort the process with a non-unwinding panic, so it must be set up front).
            MsgSendVoidBool(layer, sel_registerName("setFramebufferOnly:"), false);

            // Layer-hosting view: assign the custom layer, then set wantsLayer.
            MsgSendVoidPtr(view, sel_registerName("setLayer:"), layer);
            MsgSendVoidBool(view, sel_registerName("setWantsLayer:"), true);
            if (scale > 0)
                MsgSendVoidDouble(layer, sel_registerName("setContentsScale:"), scale);

            metalLayer = layer;
            return view;
        }
        catch
        {
            metalLayer = 0;
            return 0;
        }
    }

    public static void SetContentsScale(nint metalLayer, double scale)
    {
        if (metalLayer == 0 || scale <= 0) return;
        try { MsgSendVoidDouble(metalLayer, sel_registerName("setContentsScale:"), scale); }
        catch { /* best effort */ }
    }

    public static void Release(nint obj)
    {
        if (obj == 0) return;
        try { MsgSend(obj, sel_registerName("release")); }
        catch { /* best effort */ }
    }
}
