# Starling.Js.Test262.Tests

Runs the official JavaScript spec tests ([Test262](https://github.com/tc39/test262))
against the Starling JS engine.

## What it covers

The whole corpus, one test per top-level bucket:

- `Conformance_language` — parsing, scoping, classes, async and await,
  generators, completion values.
- `Conformance_built_ins` — the standard library (Object, Array, String,
  RegExp, Promise, TypedArrays, and so on).
- `Conformance_intl402` — ECMA-402 (Intl).
- `Conformance_annexB` — legacy web-compat semantics.
- `Conformance_staging` — staged proposal tests.

Each bucket has its own ratchet floor (a constant in `Test262Tests.cs`) and
writes its own report to `testdata/test262/results/summary-<bucket>.txt` plus a
full failure dump in `failures-<bucket>.txt`. A floor of 0 means report-only —
set it to the measured baseline once the bucket has one, then ratchet upward.
Never lower a floor.

Tests tagged with features beyond the targeted spec level (ES2024) are skipped,
not failed — see `OutOfScopeFeatures` in `Test262Runner.cs`. Skips are reported
separately and are not in the denominator.

## How to run

```bash
tools/fetch-test262.sh                       # downloads the test corpus
dotnet test tests/Starling.Js.Test262.Tests
```

If the corpus is missing the tests skip, so the build stays green.

Run one slice ad hoc with the custom-scope test:

```bash
STARLING_TEST262_DIRS=language/statements/for-of \
  dotnet test tests/Starling.Js.Test262.Tests --filter Conformance_custom_scope
```

`STARLING_TEST262_FILTER`, `STARLING_TEST262_MAX`, `STARLING_TEST262_TIMEOUT_MS`,
and `STARLING_TEST262_FLOOR` work on every bucket.

## What the badge means

About 95% of `language/` passes today. Targets are 80% at milestone 3, 95%
at milestone 7, and 98% at milestone 11 — see
[`12_TESTING.md`](../../browser-plan/12_TESTING.md). Open work is in
[`tasks/INDEX.md`](../../tasks/INDEX.md).
