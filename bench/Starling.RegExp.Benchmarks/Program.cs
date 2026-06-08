// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Running;

namespace Starling.RegExp.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
