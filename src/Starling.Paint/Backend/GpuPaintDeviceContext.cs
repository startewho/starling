// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.ExceptionServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace Starling.Paint.Backend;

/// <summary>
/// Reflection bridge to ImageSharp's non-public WebGPU device context. It lets
/// Starling allocate ImageSharp render targets on the compositor device.
/// </summary>
internal sealed class GpuPaintDeviceContext : IDisposable
{
    private static readonly Type ContextType = typeof(WebGPURenderTarget).Assembly.GetType(
        "SixLabors.ImageSharp.Drawing.Processing.Backends.WebGPUDeviceContext",
        throwOnError: true)!;

    private static readonly ConstructorInfo ContextConstructor = ContextType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(nint), typeof(nint)],
        modifiers: null)
        ?? throw new InvalidOperationException("ImageSharp WebGPU device context constructor was not found.");

    private static readonly MethodInfo CreateRenderTargetMethod = ContextType.GetMethod(
        "CreateRenderTarget",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(WebGPUTextureFormat), typeof(int), typeof(int)],
        modifiers: null)
        ?? throw new InvalidOperationException("ImageSharp WebGPU CreateRenderTarget method was not found.");

    private static readonly MethodInfo ThrowIfDisposedMethod = ContextType.GetMethod(
        "ThrowIfDisposed",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: Type.EmptyTypes,
        modifiers: null)
        ?? throw new InvalidOperationException("ImageSharp WebGPU ThrowIfDisposed method was not found.");

    private object? _context;

    public GpuPaintDeviceContext(nint deviceHandle, nint queueHandle)
    {
        _context = ContextConstructor.Invoke([deviceHandle, queueHandle]);
    }

    public void ThrowIfDisposed()
    {
        var context = Context;
        Invoke(() => ThrowIfDisposedMethod.Invoke(context, null));
    }

    public WebGPURenderTarget CreateRenderTarget(WebGPUTextureFormat format, int width, int height)
    {
        var context = Context;
        return Invoke(() =>
            CreateRenderTargetMethod.Invoke(context, [format, width, height]) as WebGPURenderTarget
            ?? throw new InvalidOperationException("ImageSharp WebGPU CreateRenderTarget returned no target."));
    }

    private object Context
        => _context ?? throw new ObjectDisposedException(nameof(GpuPaintDeviceContext));

    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static T Invoke<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    public void Dispose()
    {
        if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _context = null;
    }
}
