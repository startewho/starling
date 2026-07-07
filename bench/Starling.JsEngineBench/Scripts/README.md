# Benchmark scripts

These 19 `.js` files are vendored verbatim from the Jint project's benchmark
suite so `ScriptSuiteBench` runs a well-established set of workloads (dromaeo,
base64, linq, regexp, eval).

- Source: https://github.com/sebastienros/jint/tree/main/Jint.Benchmark/Scripts
- Upstream commit: `8e44385124edb663a030eb4074fd38279867e2b8`
- Fetched: 2026-06-02

Do not edit them — re-fetch from upstream to update. Several of the dromaeo and
linq scripts call a small test harness (`startTest`, `test`, `prep`, `endTest`,
`log`, `assert`). `ScriptSuiteBench` injects stub versions of those globals
before each script, the same way the upstream benchmark does.
