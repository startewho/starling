# HTML parser comparison: Starling vs AngleSharp

Where does the Starling HTML parser land against a mature, pure-managed
reference parser? This runs Starling.Html and [AngleSharp](https://github.com/AngleSharp/AngleSharp)
on the same pages, on one machine, ranked together so the numbers compare
fairly. It is the HTML-parsing counterpart of `engine-comparison.md`, which does
the same for the Starling JS engine against Jint.

The benchmark lives in `bench/Starling.HtmlParserBench`. AngleSharp is a
dev-only dependency there. No engine project references it, so the managed-first
interop policy is unaffected (AngleSharp ships no native code, MIT-licensed).

## What it measures

Both engines parse the same input to a full DOM, then read `TextContent`. The
text walk forces a complete tree traversal, so neither engine can skip building
the tree. The measured cost is tokenize plus tree construction plus one full
node walk. AngleSharp's parser is reusable, so it is built once and reused,
which is how a long-lived consumer would use it.

`[MemoryDiagnoser]` is on, so each run also reports bytes allocated. For a
parser that matters as much as wall-clock time.

## Fixtures

| Page | Size | Source |
|---|---|---|
| `Tiny` | ~60 B | inline string |
| `NginxOrg` | ~6.4 KB | committed snapshot, `testdata/snapshots/nginx.org/index.html` |
| `GitHub` | ~567 KB | committed snapshot, `testdata/sites/github/index.html` |
| `Synthetic1Mb` | ~1 MB | generated tree of repeated sections |

The 1 MB synthetic page exists because no committed real page reaches the 1 MB
budget target in `browser-plan/04_HTML_PARSING.md`. It is a deep tree with
attributes, links, and inline markup, so the tree builder does real nesting work
rather than copying a flat blob.

## How to run

```bash
# Full ranked sweep across all four pages
dotnet run -c Release --project bench/Starling.HtmlParserBench -- --filter '*'

# One page
dotnet run -c Release --project bench/Starling.HtmlParserBench -- --filter '*GitHub*'

# Quick smoke (one shot per case, no real timing)
dotnet run -c Release --project bench/Starling.HtmlParserBench -- --job dry --filter '*'
```

Results land in `BenchmarkDotNet.Artifacts/results/` next to where you run it.

## Results

Run on an Apple M3 Max, .NET 10.0.8, BenchmarkDotNet v0.14.0 (2026-06-03).
`Mean` is per parse. `Alloc` is managed memory allocated per parse.

| Page | Starling | AngleSharp | Faster | Starling alloc | AngleSharp alloc |
|---|--:|--:|---|--:|--:|
| Tiny (~60 B) | 1.69 µs | 3.49 µs | **Starling, 2.1×** | 6.03 KB | 12.13 KB |
| nginx.org (~6.4 KB) | 121.6 µs | 131.3 µs | Starling, 1.08× | 285.6 KB | 150.2 KB |
| GitHub home (~567 KB) | 14.26 ms | 6.12 ms | **AngleSharp, 2.3×** | 16.87 MB | 5.88 MB |
| Synthetic (~1 MB) | 51.7 ms | 33.0 ms | **AngleSharp, 1.6×** | 58.55 MB | 21.25 MB |

### What this says

The answer to "is ours faster?" is **no, not on real pages.** Starling only
wins on tiny input, where its lower startup cost dominates. As the page grows,
AngleSharp pulls ahead, and on a real 567 KB page it is more than twice as fast.

- **Tiny:** Starling parses a 60-byte page in about half the time.
- **nginx.org:** a near tie on time. Starling is about 8% faster but already
  allocates almost twice as much memory.
- **GitHub and the 1 MB tree:** AngleSharp is clearly ahead. It parses the
  GitHub page in 6.1 ms against Starling's 14.3 ms, and the 1 MB tree in 33 ms
  against 52 ms.

### The lever: allocations

On the large pages Starling allocates about 2.8× more than AngleSharp (16.9 MB
vs 5.9 MB on GitHub, 58.6 MB vs 21.2 MB on the 1 MB tree). That extra garbage
makes the garbage collector work harder. Starling triggers gen-2 collections on
both large pages, the most expensive kind. The allocation gap is the most likely
cause of the time gap, and it points straight at the allocation-conscious goals
in the AGENTS.md performance policy. Cutting per-node and per-token allocations
in the parser is the clearest path to closing it.

One note on the budget: `04_HTML_PARSING.md` targets 1 MB in 50 ms or less.
Starling lands at 51.7 ms on the 1 MB tree, right at the line. It meets its own
budget, but a mature managed parser comes in over 1.5× under it.

