// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Attributes;
using System.Text.RegularExpressions;

namespace Starling.RegExp.Benchmarks;

// NOTE: Split into separate *benchmark classes* (instead of one class with many methods)
// so that each logical comparison (BCL baseline vs. Starling for a given scenario) can
// independently declare [Benchmark(Baseline = true)]. BenchmarkDotNet only permits a
// single Baseline per (benchmark class + parameter combination).

/// <summary>
/// Literal search (linear Pike VM path).
/// </summary>
[MemoryDiagnoser]
public class RegexMatchLiteralBenchmarks
{
    [Params(100, 10_000)]
    public int InputLength { get; set; }

    private string _input = "";
    private System.Text.RegularExpressions.Regex? _bcl;
    private CompiledRegex? _starling;

    [GlobalSetup]
    public void Setup()
    {
        // Match near the beginning so the unanchored search succeeds on the first attempt.
        _input = "abc" + new string('x', InputLength - 3);
        _bcl = new System.Text.RegularExpressions.Regex("abc", RegexOptions.None);
        _starling = CompiledRegex.Compile("abc", RegexFlags.None);
    }

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Match? Bcl() =>
        _bcl!.Match(_input);

    [Benchmark]
    public RegexMatch? Starling() =>
        _starling!.Exec(_input, 0);
}

/// <summary>
/// More complex pattern with quantifiers and captures (linear Pike VM path).
/// </summary>
[MemoryDiagnoser]
public class RegexMatchComplexBenchmarks
{
    [Params(100, 10_000)]
    public int InputLength { get; set; }

    private string _input = "";
    private System.Text.RegularExpressions.Regex? _bcl;
    private CompiledRegex? _starling;

    [GlobalSetup]
    public void Setup()
    {
        _input = "2026-05-26T12:34:56" + new string('x', System.Math.Max(0, InputLength - 19));
        const string pattern = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}";
        _bcl = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.None);
        _starling = CompiledRegex.Compile(pattern, RegexFlags.None);
    }

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Match? Bcl() =>
        _bcl!.Match(_input);

    [Benchmark]
    public RegexMatch? Starling() =>
        _starling!.Exec(_input, 0);
}

/// <summary>
/// Lookahead (forces Starling's recursive backtracking fallback).
/// Input is fixed/short; no scaling param needed.
/// </summary>
[MemoryDiagnoser]
public class RegexMatchLookaroundBenchmarks
{
    private System.Text.RegularExpressions.Regex? _bcl;
    private CompiledRegex? _starling;

    [GlobalSetup]
    public void Setup()
    {
        const string pattern = @"foo(?=bar)";
        _bcl = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.None);
        _starling = CompiledRegex.Compile(pattern, RegexFlags.None);
    }

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Match? Bcl() =>
        _bcl!.Match("xxfoobarxx");

    [Benchmark]
    public RegexMatch? Starling() =>
        _starling!.Exec("xxfoobarxx", 0);
}

/// <summary>
/// Backreference (forces Starling's recursive backtracking fallback).
/// Input is fixed/short; no scaling param needed.
/// </summary>
[MemoryDiagnoser]
public class RegexMatchBackrefBenchmarks
{
    private System.Text.RegularExpressions.Regex? _bcl;
    private CompiledRegex? _starling;

    [GlobalSetup]
    public void Setup()
    {
        const string pattern = @"(.)(\1)";
        _bcl = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.None);
        _starling = CompiledRegex.Compile(pattern, RegexFlags.None);
    }

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Match? Bcl() =>
        _bcl!.Match("aabbcc");

    [Benchmark]
    public RegexMatch? Starling() =>
        _starling!.Exec("aabbcc", 0);
}

/// <summary>
/// Pathological quantifier pattern ("a*a*a*a*a*b").
/// Classic backtracking engines are exponential on long uniform input;
/// Starling's Pike VM is linear. BCL side is always run on a short input
/// (to avoid hangs/timeouts); Starling side exercises the long input.
///
/// We also provide BclLongInput60sTimeout (and a dedicated catastrophic case below)
/// which attempts the long input under a hard 60s timeout guard.
/// This is used to prove that the linear Pike VM is necessary — BCL times out
/// while Starling handles it.
/// </summary>
[MemoryDiagnoser]
public class RegexMatchPathologicalBenchmarks
{
    [Params(100, 10_000)]
    public int InputLength { get; set; }

    private string _longInput = "";
    private const string ShortInput = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaab"; // 30 'a' + 'b'

    private System.Text.RegularExpressions.Regex? _bcl;
    private CompiledRegex? _starling;

    // For explicit catastrophic backtracking demonstration (separate from the a* memory demo)
    private string _catastrophicInput = "";
    private System.Text.RegularExpressions.Regex? _bclCatastrophic;
    private CompiledRegex? _starlingCatastrophic;

    [GlobalSetup]
    public void Setup()
    {
        _longInput = new string('a', InputLength - 3) + "b";
        const string pattern = "a*a*a*a*a*b";
        _bcl = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.None);
        _starling = CompiledRegex.Compile(pattern, RegexFlags.None);

        // Classic catastrophic backtracking pattern: (a+)+b on long 'a' string (no b)
        // This is known to cause exponential time in backtracking engines.
        _catastrophicInput = new string('a', 30);
        _bclCatastrophic = new System.Text.RegularExpressions.Regex("(a+)+b", RegexOptions.None);
        _starlingCatastrophic = CompiledRegex.Compile("(a+)+b", RegexFlags.None);
    }

    [Benchmark(Baseline = true)]
    public System.Text.RegularExpressions.Match? Bcl() =>
        _bcl!.Match(ShortInput);

    [Benchmark]
    public RegexMatch? Starling() =>
        _starling!.Exec(_longInput, 0);

    /// <summary>
    /// Attempts the long input on BCL (backtracking engine) but guards with a 60s timeout.
    /// If it times out, this demonstrates the risk of exponential behavior on highly
    /// nondeterministic patterns. Used for the benchmark comparison / README note.
    /// </summary>
    [Benchmark(Description = "BCL long input (60s timeout guard)")]
    public System.Text.RegularExpressions.Match? BclLongInput60sTimeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        System.Text.RegularExpressions.Match? result = null;
        try
        {
            var task = Task.Run(() =>
            {
                result = _bcl!.Match(_longInput);
            }, cts.Token);

            task.Wait(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out - record for reporting
            Console.WriteLine($"[BclLongInput60sTimeout] TIMED OUT after 60s on InputLength={InputLength}");
            result = null;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            Console.WriteLine($"[BclLongInput60sTimeout] TIMED OUT after 60s on InputLength={InputLength}");
            result = null;
        }
        return result;
    }

    /// <summary>
    /// Starling (Pike VM) on the classic catastrophic backtracking input.
    /// Should complete quickly (linear time) even though the pattern is highly nondeterministic.
    /// </summary>
    [Benchmark]
    public RegexMatch? StarlingCatastrophic() =>
        _starlingCatastrophic!.Exec(_catastrophicInput, 0);

    /// <summary>
    /// BCL on the classic catastrophic input with 60s timeout guard.
    /// This is expected to TIMEOUT, proving why a linear-time engine (Pike VM) is necessary.
    /// </summary>
    [Benchmark(Description = "BCL catastrophic (a+)+b on 30a's with 60s timeout - EXPECT TIMEOUT")]
    public System.Text.RegularExpressions.Match? BclCatastrophic60sTimeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        System.Text.RegularExpressions.Match? result = null;
        try
        {
            var task = Task.Run(() =>
            {
                result = _bclCatastrophic!.Match(_catastrophicInput);
            }, cts.Token);

            task.Wait(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[BclCatastrophic60sTimeout] *** TIMED OUT after 60s as expected for backtracking engine ***");
            result = null;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            Console.WriteLine("[BclCatastrophic60sTimeout] *** TIMED OUT after 60s as expected for backtracking engine ***");
            result = null;
        }
        return result;
    }
}
