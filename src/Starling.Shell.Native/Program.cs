namespace Starling.Shell.Native;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--spike") >= 0)
            return SpikeProgram.Run();

        // --frames N: auto-close after N presented frames (smoke-test helper).
        var maxFrames = 0;
        var fi = Array.IndexOf(args, "--frames");
        if (fi >= 0 && fi + 1 < args.Length && int.TryParse(args[fi + 1], out var n))
            maxFrames = n;

        // --browser: run the interactive browser window with real engine navigation.
        if (Array.IndexOf(args, "--browser") >= 0)
        {
            using var browser = new NativeBrowserWindow(maxFrames);
            return browser.Run();
        }

        // Default: the zero-copy page-present demo.
        return NativePresentDemo.Run(maxFrames);
    }
}
