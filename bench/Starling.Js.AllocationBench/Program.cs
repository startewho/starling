// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Starling.Js.Lex;
using Starling.Js.Parse;

namespace Starling.Js.AllocationBench;

internal static class Program
{
    private const int DefaultIterations = 2_000;

    private const string ParseHeavySource = """
        var alpha = 1, beta = 2, gamma = 3, delta = 4;
        function combine(first, second, third) {
            return { first, second, third, total: first + second + third };
        }
        for (var index = 0; index < 250; index++) {
            alpha = alpha + index;
            beta = beta ^ index;
            gamma = gamma ?? delta;
            delta = combine(alpha, beta, gamma).total;
        }
        /alpha\/beta/g.test('alpha/beta');
        `alpha ${delta} beta`;
        """;

    public static int Main(string[] args)
    {
        var iterations = args.Length > 0 && int.TryParse(args[0], out var parsed)
            ? parsed
            : DefaultIterations;

        RunScenario("lex-parse-heavy", iterations, static () =>
        {
            var lexer = new JsLexer(ParseHeavySource);
            var count = 0;
            while (lexer.Next().Kind != JsTokenKind.EndOfFile)
                count++;
            return count;
        });

        RunScenario("parse-heavy", iterations, static () =>
            new JsParser(ParseHeavySource).ParseProgram().Body.Count);

        return 0;
    }

    private static void RunScenario(string name, int iterations, Func<int> action)
    {
        for (var i = 0; i < 100; i++)
            action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var beforeGen0 = GC.CollectionCount(0);
        var before = Stopwatch.GetTimestamp();
        var checksum = 0;

        for (var i = 0; i < iterations; i++)
            checksum += action();

        var elapsed = Stopwatch.GetElapsedTime(before);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
        var gen0 = GC.CollectionCount(0) - beforeGen0;

        Console.WriteLine(string.Join('\t',
            name,
            $"iterations={iterations}",
            $"checksum={checksum}",
            $"elapsed_ms={elapsed.TotalMilliseconds:F3}",
            $"allocated_bytes={allocated}",
            $"allocated_per_iter={allocated / iterations}",
            $"gen0={gen0}"));
    }
}
