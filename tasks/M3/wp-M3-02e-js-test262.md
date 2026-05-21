---
id: "wp:M3-02e-js-test262"
parent: "wp:M3-02-js-parser"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-test262"
claimed_at: "2026-05-21T03:20:00Z"
completed_at: "2026-05-21T03:35:00Z"
branch: "main"
depends_on: []
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-02-js-parser"
---

# wp:M3-02e — JS: Test262 conformance harness + pass-rate gate

## Goal
Stand up a Test262 (ECMAScript conformance suite) runner against the Starling JS
engine, measure a baseline pass rate, and ratchet toward the ≥ 80% target.
"Start" deliverable: the harness + corpus fetch + a measured baseline, with a
floor gate that prevents regression. Reaching 80% is iterative engine work
tracked from the failure report this produces.

## Delivered (this WP — infrastructure + baseline)
- `tools/fetch-test262.sh` — shallow, sparse (`harness/` + `test/`) clone of
  tc39/test262 at a **pinned SHA** into `testdata/test262/` (gitignored;
  ~53.5k test files). Idempotent.
- `tests/Starling.Js.Test262.Tests/` (new MSTest project, in `Starling.slnx`):
  - `Test262Runner.cs` — frontmatter parser (`flags`, `includes`, `negative`,
    `features`) + harness assembly + scenario execution. Handles: non-strict /
    strict / raw modes; `onlyStrict`/`noStrict`; default harness (`assert.js`,
    `sta.js`) + `includes:` + async `doneprintHandle.js`; negative tests
    (`phase: parse` → expect parse error; `phase: runtime/resolution` → expect a
    throw whose error `name` matches `type`); async `$DONE` via captured
    `print` output; a minimal `$262` host object. Module tests are skipped
    (loader + relative-resolution; follow-up). Each scenario runs on a worker
    thread with a timeout. Harness chunks are parsed once and re-run per realm.
  - `Test262Tests.cs` — locates the (gitignored) corpus, runs a configurable
    subset, aggregates by category, writes `testdata/test262/results/summary.txt`,
    and gates on `STARLING_TEST262_FLOOR`. Inconclusive (skips) when the corpus
    is absent, so CI without it stays green. Env knobs: `STARLING_TEST262_DIRS`,
    `_FILTER`, `_MAX`, `_TIMEOUT_MS`, `_FLOOR`.

## Enabling engine fix (shipped here)
- **VM call-depth guard** (`JsVm.MaxCallDepth`): every JS call recurses through
  `JsVm.Run` in C#, so unbounded JS recursion overflowed the native stack and
  crashed the process (uncatchable) — which aborted the corpus run. Added a
  depth cap that throws a catchable `RangeError("Maximum call stack size
  exceeded")` (spec-permitted, implementation-defined limit). Conservative
  (1000) to stay safe on a ~1 MB stack. This is also a real conformance win
  (deep-recursion tests expect `RangeError`).

## Acceptance
- `tools/fetch-test262.sh` fetches the pinned corpus.
- `Conformance_pass_rate` runs the subset and writes a report; baseline pass
  rate recorded here and a floor gate set just below it (ratchet).
- Existing `Starling.Js.Tests` stay green (depth guard adds no regression).
- Baseline number + top failure buckets recorded in the handoff log to seed the
  push to 80%.

## Notes / known limitations (seed the 80% push)
- Module tests skipped (not counted) — needs loader + `_FIXTURE.js`/relative
  resolution.
- `eval` is not a global — eval-dependent tests fail.
- Member/super update forms (`obj.x++`) unsupported (`EmitUpdate` identifier-only)
  — shows up directly in `language/types`.
- DO NOT touch `tasks/` from automated runs; corpus + results are gitignored.

## Baseline (2026-05-21, pinned SHA c42f56d)
Default scope `test/language` (core ECMAScript semantics), 64 MB worker stack,
2–5 s timeout:

**37.77%** — 16,447 / 43,546 scenarios (strict + non-strict), 0 timeouts, 806
skipped (module/io). 23,645 files, ~60 s.

Committed gate: default `STARLING_TEST262_DIRS=language`, floor **37%** (ratchet).
`built-ins` is opt-in (a few huge-array tests hit the timeout). Top buckets to
attack toward 80% (pass/total):
- `language/eval-code` 0.4% (no `eval` global) and `arguments-object` 3.5%
- `language/expressions` 35.0% (21k scenarios — the bulk of the gap)
- `language/global-code` 17.3%, `block-scope` 29.3%, `literals` 39.1%,
  `computed-property-names` 45.8%, `directive-prologue` 37.1%
- `language/asi` 97.1%, `import` 100%, `keywords` 96% already strong.

## Handoff log
- 2026-05-21T03:20Z — created; harness + fetch + depth guard built (agent-claude-cody-test262).
- 2026-05-21T03:34Z — COMPLETE (infra slice). Language baseline 37.77% recorded; floor gate set to 37%. Enabling fix: VM call-depth guard (`RangeError`) — without it deep-recursion/TCO tests crashed the run with a native StackOverflow. Runner uses a 64 MB worker stack so the depth-1000 guard fires with headroom. `built-ins` left opt-in (slow huge-array tests + timeout-thread-leak). Next: drive the language buckets above toward 80% (each is its own engine WP), and harden built-ins execution (bounded array allocation / cancellable timeout) before counting it.
