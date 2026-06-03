# JS engine comparison: Starling vs Jint

Where does the Starling JS engine land against Jint? This runs Jint's own
"EngineComparison" benchmark on our engine. Same 19 scripts, same strict mode,
ranked on one machine so the numbers compare fairly.

The benchmark lives in `bench/Starling.JsEngineBench`. The scripts under
`Scripts/` are copied straight from Jint's suite (see `Scripts/README.md` for the
source commit).

## How to run

```bash
# Full ranked sweep
dotnet run -c Release --project bench/Starling.JsEngineBench -- --filter '*'

# Quick check (one shot per case, no timing)
dotnet run -c Release --project bench/Starling.JsEngineBench -- --job dry --filter '*'
```

Each engine runs two ways, matching Jint's own split:

- **cold** — parse, compile, and run every time. Read `Starling` against `Jint`.
- **prepared** — compile once, then run on a fresh runtime each time. Read
  `Starling_Prepared` against `Jint_ParsedScript`.

## What we found

Starling ran **all 19 scripts with zero failures** — modern syntax, `eval`,
`new Function`, regular expressions, and the 34 KB `linq-js` library. For a young
engine that is the headline. Jint passes everything too, so this is a clean
head-to-head.

On speed, Starling is the slower engine on every script. The gap is small on
some, huge on others:

- **Close (1.2x–2x):** the dromaeo 3d-cube and object-string scripts. Tight
  arithmetic and string building. Starling's bytecode interpreter keeps up well
  here.
- **Middle (3x–7x):** core-eval, base64, stopwatch, linq, plain evaluation. The
  common case.
- **Two blow-ups:** the regular-expression scripts run about **120x slower** —
  almost 13 seconds against Jint's 0.1 second. This is the Starling
  regular-expression engine, not the interpreter. It is the single biggest thing
  to fix.

One bright spot: on `linq-js`, prepared Starling (182 µs) beats **cold** Jint
(1,000 µs). Reuse the compiled script and Starling does fine on real library code.

The bigger weakness is memory. Starling allocates far more than Jint on most
scripts. The range is wide: nearly even on the object-string scripts (1.1x), up
to 346x on the regular-expression scripts. A no-JIT bytecode interpreter does
more boxing and short-lived allocation per operation, and it shows. This is the
other thing to chase, because high allocation drives garbage-collector pauses
that hurt frame time in the browser.

(JIT = just-in-time compiling to native code. Jint stays an interpreter too, so
the gap here is about allocation, not native codegen.)

## Local results (this machine)

Apple M3 Max, macOS 26.3, .NET 10.0.8 Arm64. BenchmarkDotNet 0.14.0, ShortRun (3
launches, 3 warmup, 3 iterations). Times are mean per run.

- **cold x** is `Starling` time divided by `Jint` time.
- **prep x** is `Starling_Prepared` time divided by `Jint_ParsedScript` time.
- **Alloc** is `Starling` bytes divided by `Jint` bytes.

Lower is better for all three. Ranked from smallest cold gap to largest.

| Script | Jint | Starling | cold x | prep x | Alloc |
|---|--:|--:|--:|--:|--:|
| dromaeo-3d-cube.js | 17.3 ms | 20.8 ms | 1.2x | 1.7x | 11x |
| dromaeo-object-string.js | 104 ms | 169 ms | 1.6x | 1.6x | 1.1x |
| dromaeo-object-string-modern.js | 106 ms | 180 ms | 1.7x | 1.7x | 1.2x |
| dromaeo-3d-cube-modern.js | 12.1 ms | 24.2 ms | 2.0x | 1.8x | 11x |
| stopwatch-modern.js | 172 ms | 577 ms | 3.4x | 3.3x | 286x |
| linq-js.js | 1.00 ms | 3.66 ms | 3.7x | 3.3x | 4.0x |
| dromaeo-core-eval-modern.js | 1.93 ms | 7.44 ms | 3.9x | 3.9x | 95x |
| dromaeo-core-eval.js | 1.79 ms | 7.23 ms | 4.0x | 4.1x | 95x |
| dromaeo-string-base64.js | 20.5 ms | 91.0 ms | 4.4x | 4.5x | 335x |
| dromaeo-string-base64-modern.js | 23.4 ms | 102 ms | 4.4x | 4.0x | 338x |
| stopwatch.js | 155 ms | 746 ms | 4.8x | 4.9x | 284x |
| evaluation-modern.js | 17 µs | 111 µs | 6.5x | 16x | 14x |
| evaluation.js | 16 µs | 113 µs | 6.9x | 18x | 14x |
| array-stress.js | 3.08 ms | 22.1 ms | 7.2x | 6.4x | 65x |
| dromaeo-object-array-modern.js | 15.0 ms | 164 ms | 10.9x | 11.0x | 128x |
| dromaeo-object-array.js | 13.8 ms | 162 ms | 11.8x | 12.0x | 126x |
| minimal.js | 7.5 µs | 97 µs | 13.0x | 31x | 18x |
| dromaeo-object-regexp-modern.js | 110 ms | **12.5 s** | **114x** | 130x | 346x |
| dromaeo-object-regexp.js | 104 ms | **12.9 s** | **124x** | 163x | 344x |

Two notes on prepared mode:

- On the tiny scripts (`minimal`, `evaluation`) prepared mode looks *worse*, not
  better. Jint's parsed-script path drops to a few microseconds, while Starling
  still pays a fixed per-run setup cost. The ratio grows because Jint's
  denominator shrank, not because Starling slowed down.
- `linq-js` is the win. Prepared Starling (182 µs) beats cold Jint (1,000 µs).
  Against Jint's own prepared path (55 µs) it is still 3.3x behind, but the
  compiled-artifact reuse clearly pays off on real library code.

The full table with error bars and per-generation garbage-collector counts is
written to `BenchmarkDotNet.Artifacts/results/` under whatever folder you run the
command from. That folder is gitignored.

## Published reference numbers (the four-engine field)

We only run Starling and Jint locally, so those two columns above are the
trustworthy same-machine pair. The other three engines in Jint's suite —
NiL.JS, Jurassic, and YantraJS — are not run here. The numbers below are Jint's
own published table, copied verbatim for reference.

**Different hardware. Treat as a rough guide, not a same-machine ranking.**
Jint's board ran on an AMD Ryzen 9 5950X under Windows 11 with .NET 10.0.7
(BenchmarkDotNet 0.15.8, last updated 2026-05-10). Our local board is an Apple
M3 Max under macOS. Even the Jint column differs from ours for that reason — for
example regexp reads 135 ms there against 104 ms here.

Cold `Jint` mean per run, base (non-`modern`) scripts:

| Script | Jint | NiL.JS | Jurassic | YantraJS |
|---|--:|--:|--:|--:|
| minimal | 2.7 µs | 2.8 µs | 2,305 µs | 153 µs |
| evaluation | 15 µs | 26 µs | 2,110 µs | 156 µs |
| linq-js | 1.20 ms | 3.97 ms | 36.2 ms | 0.34 ms |
| dromaeo-core-eval | 2.46 ms | 1.23 ms | 17.1 ms | 4.54 ms |
| array-stress | 3.60 ms | 4.85 ms | 9.15 ms | 15.3 ms |
| dromaeo-3d-cube | 12.4 ms | 6.19 ms | 55.1 ms | 3.00 ms |
| dromaeo-object-array | 18.5 ms | 52.2 ms | 35.4 ms | 65.7 ms |
| dromaeo-string-base64 | 26.9 ms | 25.9 ms | 47.1 ms | 43.0 ms |
| dromaeo-object-string | 155 ms | 128 ms | 205 ms | 173 ms |
| stopwatch | 195 ms | 132 ms | 142 ms | 63.4 ms |
| dromaeo-object-regexp | 135 ms | 528 ms | 678 ms | 1,060 ms |

The pattern from that board: Jint and NiL.JS trade the top spot on most scripts.
YantraJS wins the graphics-heavy 3d-cube and the stopwatch loops, but allocates
enormous amounts of memory (over 1 GB on object-array, against Jint's 10 MB).
Jurassic is slow to start and weak on `eval`-style scripts.

One thing stands out for our roadmap: even the *slowest* published engine on
regexp, YantraJS at about 1.06 seconds, is still roughly 12x faster than
Starling's 12.9 seconds. The regular-expression gap is not a Starling-vs-Jint
problem. It is last place against the whole field.

## Where this puts Starling

Slotting Starling in by its ratio to Jint: on the close and middle scripts (1.2x
to 7x), Starling lands near Jurassic's tier — behind Jint and NiL.JS. On the
regular-expression scripts it would sit dead last by a wide margin until the
Starling regular-expression engine is fixed. On `linq-js` with a prepared
script, Starling is genuinely competitive.

So: a solid mid-pack interpreter that already runs everything, with two clear
work items — the regular-expression engine, and allocation per operation.
