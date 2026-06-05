// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Attributes;

namespace Starling.RegExp.Benchmarks;

/// <summary>
/// Micro-benchmarks for flag parsing (used at RegExp construction time in JS hosts).
/// </summary>
[MemoryDiagnoser]
public class RegexFlagBenchmarks
{
    [Benchmark(Baseline = true)]
    public bool TryParseEmpty()
    {
        RegexFlagParser.TryParse("", out _, out _);
        return true;
    }

    [Benchmark]
    public bool TryParseCommon()
    {
        RegexFlagParser.TryParse("gimsu", out _, out _);
        return true;
    }

    [Benchmark]
    public bool TryParseAll()
    {
        RegexFlagParser.TryParse("dgimsuvy", out _, out _);
        return true;
    }

    [Benchmark]
    public string ToStringCommon()
    {
        RegexFlagParser.TryParse("gi", out var f, out _);
        return RegexFlagParser.ToFlagString(f);
    }
}
