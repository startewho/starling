using BenchmarkDotNet.Running;

namespace Starling.Bench;

internal static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
