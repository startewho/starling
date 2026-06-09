// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-84 Stage A — guards the native-frame size of the VM dispatch loop.
/// Before the CallFrame extraction each JS call burned ~40 KB of native stack
/// and pure JS recursion died at ~24 frames. If RunInner's frame regresses
/// (e.g. a new closure-capturing local function, or a fat opcode arm moved
/// back inline), this depth collapses and the assertions below catch it.
/// </summary>
[TestClass]
public class JsRecursionDepthTests
{
    [TestMethod]
    public void Recursion_reaches_at_least_50_frames_on_the_default_test_thread()
    {
        // Debug builds measured 72 here at the time of writing (Release: 232).
        MaxDepth(64).Should().BeGreaterThanOrEqualTo(50);
    }

    [TestMethod]
    public void Recursion_reaches_at_least_200_frames_on_an_8mb_thread()
    {
        // Debug builds measured 422 here at the time of writing (Release hits
        // the logical MaxCallDepth cap, ~998).
        var depth = 0;
        var t = new Thread(() => depth = MaxDepth(2048), 8 * 1024 * 1024);
        t.Start();
        t.Join();
        depth.Should().BeGreaterThanOrEqualTo(200);
    }

    /// <summary>Binary-search the largest n for which f(n) completes without
    /// the recursion RangeError.</summary>
    private static int MaxDepth(int hi)
    {
        var lo = 1;
        if (!Works(lo)) return 0;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (Works(mid)) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private static bool Works(int n)
    {
        try
        {
            var program = new JsParser(
                $"function f(n){{ return n<=0 ? 0 : 1+f(n-1); }} f({n});").ParseProgram();
            var chunk = Starling.Js.Bytecode.JsCompiler.CompileForEval(program);
            var v = new JsVm(new JsRuntime()).Run(chunk);
            return v.IsNumber && v.AsNumber == n;
        }
        catch (JsThrow)
        {
            return false;
        }
    }
}
