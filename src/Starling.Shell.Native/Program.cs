namespace Starling.Shell.Native;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--spike") >= 0)
            return SpikeProgram.Run();

        // Default: the zero-copy page-present demo. --frames N auto-closes.
        var maxFrames = 0;
        var fi = Array.IndexOf(args, "--frames");
        if (fi >= 0 && fi + 1 < args.Length && int.TryParse(args[fi + 1], out var n))
            maxFrames = n;
        return NativePresentDemo.Run(maxFrames);
    }
}
