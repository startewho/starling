using Xunit;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Opt-in gate for tests that need a real GPU. Two paths in this suite depend on
/// one: the <see cref="Starling.Gui.Controls.WebviewPanel"/> zero-copy surface
/// present (a <c>NativeControlHost</c>-backed CAMetalLayer) and the WebGPU tile
/// cache. Neither works in a headless no-GPU sandbox — the surface throws
/// "Surface is not active or not ready", and the tile cache records no hits — so
/// those tests fail for reasons unrelated to the code under test.
///
/// Set <c>STARLING_GPU_TESTS=1</c> on a machine with a display (or a GPU CI arm)
/// to run them.
/// </summary>
internal static class GpuTests
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("STARLING_GPU_TESTS") == "1";

    public const string Reason =
        "Needs a real GPU: the WebviewPanel surface present and the WebGPU tile cache "
        + "don't work in a headless no-GPU sandbox. Set STARLING_GPU_TESTS=1 to run "
        + "(a machine with a display or a GPU CI arm).";

    /// <summary>Skips the calling test unless the GPU arm is enabled. Safe to call
    /// from a shared setup helper — the skip propagates to the test regardless of
    /// stack depth.</summary>
    public static void SkipUnlessAvailable() => Assert.SkipUnless(Enabled, Reason);
}
