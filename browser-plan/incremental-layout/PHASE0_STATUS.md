# Incremental layout — Phase 0 status

This tracks the "measurement and safety net" phase of the incremental-layout
plan. Phase 0 is the prerequisite that blocks the rest of the work.

## What landed

### 0b — fixed the false `RenderFrame` doc comment

`Engine.RenderFrame` claimed its cascade cache "short-circuits the static
side". That was not true. The box tree is rebuilt from scratch every call,
with a new per-build cascade cache. The comment now says so, and points at
incremental fragment reuse as the planned replacement.

### 0c — the live loop relayouts on the narrow signal

The live loop used to relayout whenever the DOM changed at all
(`Document.MutationVersion`). It now relayouts only when a change a built-in
style or layout pass cares about lands (`Document.LayoutInvalidationVersion`).

A `requestAnimationFrame` callback that writes only `data-*`, `aria-*`, or
`js*` framework attributes no longer forces a full reflow every frame. Text
writes and layout-relevant attribute writes still relayout, so the Animations
demo's per-frame status text keeps updating.

The change lives in `WebviewPanel`. It tracks the document's
`LayoutInvalidationVersion` from the last layout and compares it after each
pump. `ShowPage` re-baselines the version on every page swap.

**Known accepted gap.** Author CSS that uses an attribute selector (such as
`[role="button"] { ... }`) plus a script that writes that attribute will miss
a recompute until the next layout-relevant change. The spec-correct fix is
selector-aware invalidation, which is a tracked follow-up. The gap is
documented in code at both the live loop and `Document.IsLayoutRelevantAttribute`.

### 0d — the verification harness shell

`LayoutVerifier` (in `Starling.Layout`) walks two layout outputs in lockstep
and reports the first place their geometry diverges. It returns the box path,
the DOM element, the field that differs (such as `Frame.X` or `ChildCount`),
and the expected and actual values. It stops at the first difference so the
report points at the root cause, not every box knocked out of place by it.

`LayoutEngine` runs it when `VerifyLayout` is set (default: the
`STARLING_LAYOUT_VERIFY=1` env switch). Today both sides are a full rebuild,
so this is an identity check that proves the harness itself works. When the
incremental path lands, the produced output becomes the incremental result and
the harness keeps checking it against a full rebuild.

Tests in `Starling.Layout.Tests/Verification/`:

- two full rebuilds of the same document are identical (the identity check),
- an injected fault names the right element and field (a 5px shove, a dropped
  child box, differing text, a different viewport width),
- the engine's dual run records the `layout.verify.ok` counter and logs no
  error on a deterministic page,
- every site under `testdata/sites/` lays out deterministically.

## What is blocked in this environment

### 0a — the steady-state trace

Phase 0a wants a captured trace from the running app with the Aspire dashboard,
so the three forking numbers can be read and Phase 4 gated yes or no:

- `paint.style_cascade` vs `paint.layout`,
- the shaper sub-span as a fraction of `paint.layout`,
- `show_page.hit_index` as a fraction of the frame,
- plus garbage-collection events against frame time.

This needs a built and running GUI. The GUI cannot be built here. The paint
backend depends on the Six Labors drawing package, which fails its license
check without a `sixlabors.lic` file. That file is git-ignored and absent in
this environment, and the download hosts for the .NET SDK and the license are
not on the network allow-list. So `Starling.Paint`, `Starling.Engine`, and
`Starling.Gui` cannot compile here, and the app cannot run.

The lower stack — `Starling.Dom`, `Starling.Css`, `Starling.Layout` — builds
and tests cleanly, which is why 0d landed with full test coverage.

**0a is deferred to a licensed build environment.** Phase 4 stays gated until
0a produces the hit-index number.

## What this means for the code that could not be compiled here

The 0b and 0c changes live in `Starling.Engine` and `Starling.Gui`, which do
not build in this environment for the license reason above. They were written
against the existing public surface and reviewed by hand:

- 0b is a doc comment only. It uses no cross-assembly `cref` to an internal
  type, so it cannot break the build.
- 0c uses only members that already exist: `LaidOutPage.Document`,
  `Document.LayoutInvalidationVersion`, `RefreshLiveLayout`, and `CaretLog`.

A build in a licensed environment should confirm both. The Phase 0 work that
could be verified here — the layout substrate and the 0d harness — is green.
