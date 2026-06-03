using BenchmarkDotNet.Running;

namespace Starling.JsEngineBench;

internal static class Program
{
    // Run:   dotnet run -c Release --project bench/Starling.JsEngineBench
    // Smoke: dotnet run -c Release --project bench/Starling.JsEngineBench -- --job short
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
