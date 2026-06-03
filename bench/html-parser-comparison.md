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

Not filled in yet. The container that wrote this benchmark had no .NET SDK and
no egress to install one, so the numbers need a run on a machine with the SDK.
Paste the BenchmarkDotNet summary table here once you have it, the same way
`engine-comparison.md` records its run.

Read `Starling` against `AngleSharp` per page, on both time and allocation. The
honest framing until then: we expect the span-based Starling tokenizer to do
well, but we have no measured proof yet, and the one external comparison we do
have (Starling JS engine vs Jint) currently favors the other engine on speed. So
treat any "ours is faster" claim as unproven until this table is real.
