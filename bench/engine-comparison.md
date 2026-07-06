# JS engine script-suite results

How fast is the Starling JS engine on real script workloads? This runs the 19
vendored scripts under `bench/Starling.JsEngineBench/Scripts/` (dromaeo loops,
base64, a 34 KB linq library, regular expressions, `eval`-heavy code) in strict
mode, ranked on one machine.

The benchmark lives in `bench/Starling.JsEngineBench` (`ScriptSuiteBench`).

## How to run

```bash
# Full ranked sweep
dotnet run -c Release --project bench/Starling.JsEngineBench -- --filter '*'

# Quick check (one shot per case, no timing)
dotnet run -c Release --project bench/Starling.JsEngineBench -- --job dry --filter '*'
```

Each script runs two ways:

- **cold** (`Starling`) — parse, compile, and run every time.
- **prepared** (`Starling_Prepared`) — compile once, then run on a fresh
  runtime each time.

The full table with error bars and per-generation garbage-collector counts is
written to `BenchmarkDotNet.Artifacts/results/` under whatever folder you run
the command from. That folder is gitignored.

## What we found

Starling ran **all 19 scripts with zero failures** — modern syntax, `eval`,
`new Function`, regular expressions, and the 34 KB `linq-js` library. For a
young engine that is the headline.

Timings on Apple M3 Max, macOS 26.3, .NET 10.0.8 Arm64 (BenchmarkDotNet,
ShortRun; mean per cold run):

| Script | Starling |
|---|--:|
| minimal.js | 97 µs |
| evaluation.js | 113 µs |
| evaluation-modern.js | 111 µs |
| linq-js.js | 3.66 ms |
| dromaeo-core-eval.js | 7.23 ms |
| dromaeo-core-eval-modern.js | 7.44 ms |
| dromaeo-3d-cube.js | 20.8 ms |
| array-stress.js | 22.1 ms |
| dromaeo-3d-cube-modern.js | 24.2 ms |
| dromaeo-string-base64.js | 91.0 ms |
| dromaeo-string-base64-modern.js | 102 ms |
| dromaeo-object-array.js | 162 ms |
| dromaeo-object-array-modern.js | 164 ms |
| dromaeo-object-string.js | 169 ms |
| dromaeo-object-string-modern.js | 180 ms |
| stopwatch-modern.js | 577 ms |
| stopwatch.js | 746 ms |
| dromaeo-object-regexp.js | **12.9 s** |
| dromaeo-object-regexp-modern.js | **12.5 s** |

Two clear work items fall out of the ranking:

1. **The regular-expression engine.** The two regexp scripts take about 13
   seconds while everything else stays under a second. This is the
   regular-expression engine, not the interpreter, and it is the single biggest
   thing to fix.
2. **Allocation per operation.** A no-JIT bytecode interpreter does more boxing
   and short-lived allocation per operation, and the memory columns show it —
   worst on the regexp and stopwatch scripts. High allocation drives
   garbage-collector pauses that hurt frame time in the browser.

One bright spot: on `linq-js`, the prepared path (182 µs) shows the
compiled-artifact reuse paying off on real library code — a ~20x drop from the
cold run.

A note on prepared mode: on the tiny scripts (`minimal`, `evaluation`) prepared
mode saves little, because a fixed per-run setup cost (fresh runtime + realm
bootstrap) dominates. The `bootstrap` case in `StarlingFeatureBench` tracks that
cost in isolation.
