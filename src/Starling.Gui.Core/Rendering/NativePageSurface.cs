// SPDX-License-Identifier: Apache-2.0
namespace Starling.Gui.Core.Rendering;

public enum NativePageSurfaceKind
{
    MetalLayer,
    WindowsHwnd,
    XlibWindow,
}

public readonly struct NativePageSurface
{
    private NativePageSurface(NativePageSurfaceKind kind, nint handle, nint auxiliaryHandle, ulong windowId)
    {
        Kind = kind;
        Handle = handle;
        AuxiliaryHandle = auxiliaryHandle;
        WindowId = windowId;
    }

    public NativePageSurfaceKind Kind { get; }

    public nint Handle { get; }

    public nint AuxiliaryHandle { get; }

    public ulong WindowId { get; }

    public static NativePageSurface MetalLayer(nint layer)
        => new(NativePageSurfaceKind.MetalLayer, layer, 0, 0);

    public static NativePageSurface WindowsHwnd(nint hwnd, nint hinstance)
        => new(NativePageSurfaceKind.WindowsHwnd, hwnd, hinstance, 0);

    public static NativePageSurface XlibWindow(nint display, ulong window)
        => new(NativePageSurfaceKind.XlibWindow, 0, display, window);
}
