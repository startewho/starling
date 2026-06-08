// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Attributes;
using System.Text.RegularExpressions;

namespace Starling.RegExp.Benchmarks;

/// <summary>
/// Benchmarks for regex compilation (parse + compile to VM program).
/// Compares Starling.RegExp to System.Text.RegularExpressions where patterns are compatible.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
public class RegexCompileBenchmarks
{
    // Patterns chosen to exercise different parts of the parser/compiler.
    // All are valid in both engines (JS semantics subset that BCL accepts the same).
    [ParamsSource(nameof(Patterns))]
    public string Pattern { get; set; } = "";

    public static IEnumerable<string> Patterns => new[]
    {
        "foo",
        "a+b*",
        @"\d{4}-\d{2}-\d{2}",
        @"(?<year>\d{4})-(?<month>\d{2})",
        @"^https?://[^\s/$.?#].[^\s]*$",
        "a*a*a*a*a*b", // catastrophic backtracking candidate (linear in Starling)
        @"\p{Letter}+",
        "(?:non)?capturing",
    };

    private static readonly RegexOptions BclOptions = RegexOptions.None;

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Regex Bcl() =>
        new System.Text.RegularExpressions.Regex(Pattern, BclOptions);

    [Benchmark]
    public CompiledRegex Starling() =>
        CompiledRegex.Compile(Pattern, RegexFlags.None);
}
