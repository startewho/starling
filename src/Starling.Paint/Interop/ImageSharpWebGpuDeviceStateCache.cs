// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace Starling.Paint.Interop;

internal static class ImageSharpWebGpuDeviceStateCache
{
    private static readonly Lazy<Hooks?> CacheHooks = new(CreateHooks);

    internal static bool Contains(nint deviceHandle)
    {
        if (deviceHandle == 0 || CacheHooks.Value is not { } hooks)
        {
            return false;
        }

        lock (hooks.Sync)
        {
            return (bool)hooks.ContainsKey.Invoke(hooks.Cache, [deviceHandle])!;
        }
    }

    internal static bool TryDispose(nint deviceHandle)
    {
        if (deviceHandle == 0 || CacheHooks.Value is not { } hooks)
        {
            return false;
        }

        object? state;
        lock (hooks.Sync)
        {
            var args = new object?[] { deviceHandle, null };
            if (!(bool)hooks.TryRemove.Invoke(hooks.Cache, args)!)
            {
                return false;
            }

            state = args[1];
            (state as IDisposable)?.Dispose();
        }

        return state is not null;
    }

    private static Hooks? CreateHooks()
    {
        var runtimeType = typeof(WebGPURenderTarget).Assembly.GetType(
            "SixLabors.ImageSharp.Drawing.Processing.Backends.WebGPURuntime",
            throwOnError: false);
        if (runtimeType is null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var sync = runtimeType.GetField("DeviceStateCacheSync", flags)?.GetValue(null);
        var cache = runtimeType.GetField("DeviceStateCache", flags)?.GetValue(null);
        if (sync is null || cache is null)
        {
            return null;
        }

        var cacheType = cache.GetType();
        var args = cacheType.GetGenericArguments();
        if (args.Length != 2)
        {
            return null;
        }

        var containsKey = cacheType.GetMethod(
            "ContainsKey",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(nint)],
            modifiers: null);
        var tryRemove = cacheType.GetMethod(
            "TryRemove",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(nint), args[1].MakeByRefType()],
            modifiers: null);

        return containsKey is null || tryRemove is null
            ? null
            : new Hooks(sync, cache, containsKey, tryRemove);
    }

    private sealed record Hooks(object Sync, object Cache, MethodInfo ContainsKey, MethodInfo TryRemove);
}
