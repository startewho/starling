# Site styling plan — GitHub and x.com

Goal: github.com and x.com load mostly normally, styling-wise. This plan
ranks engine work by visible impact on those two sites. It comes from a
2026-06-09 audit of the layout, paint, and animation code plus a feature
count of what each site's CSS actually uses.

> ## Status — all five tiers implemented (2026-06-10)
>
> Every numbered item below landed on `feat/js-stack-trampoline`, each
> through an adversarially reviewed agent slice with review findings
> fixed before merge. `tasks/SPEC_COVERAGE.md` rows are synced.
>
> Exit checks: the x.com fixture renders with primary column 600px at
> x=224 (Chromium 217/600), sidebar 290px at x=854 (Chromium 837/290),
> UserName y=291 (Chromium 293) — within the ~25px target everywhere
> except the tabs row (y=393 vs 436, a text-metrics gap outside this
> plan). The GitHub fixture through the Aspire harness now paints the
> correct dark canvas, logo, and header chrome.
>
> Known follow-ups, in rough priority order:
> 1. GitHub homepage body content is still mostly absent in a static
>    render — needs its own root-cause pass (JS hydration, web-font and
>    image fetch in headless, possibly residual layout). Not a CSS-tier
>    item.
> 2. x.com tabs-row offset: text metrics / line-height vs Chromium.
> 3. Scroll WP6 (promoted scroll layers — tile reuse on scroll) from
>    browser-plan/scroll-model.md.
> 4. backdrop-filter on the GPU canvas / tile paths and cross-layer
>    backdrops (CPU flat path is correct).
> 5. Borders on absolutely positioned boxes never paint (PositionLayout
>    zeroes Border edges — PendingFact in `InlineThemePaintTests`).
> 6. Client rects keep the document-space convention (no ancestor scroll
>    subtraction); stuck-sticky rects follow it.

Fixtures for checking progress:

- `testdata/sites/github/` — logged-out GitHub homepage snapshot. Serve it
  through the Aspire harness at `http://localhost:8088/github/`.
- `testdata/sites/xcom-nasa/index.html` — x.com/NASA profile DOM snapshot
  (post-React, dark mode, no images). Renders headless in about 15 seconds.
  Chromium ground truth at 1280×900: header rail width 217, primary column
  x=217 width 600, sidebar x=837 width 290, user name y=293, tabs y=436.

## What the audit found

Parsing and the cascade are in good shape. Custom properties with `var()`
fallbacks, CSS nesting, Selectors Level 4 (`:is`, `:where`, `:not`,
`:has`), media queries, and the full color suite all work. That covers
GitHub's single biggest dependency: 17,252 `var()` references drive every
color and even the page-shell grid templates.

The gaps are in behavior — layout math and paint output:

- **Flexbox** is real but has three root-caused bugs (listed in Tier 1).
- **Grid** (`src/Starling.Layout/Grid/GridLayout.cs`, 408 lines) does
  explicit columns, `fr`, `repeat()`, gap, and alignment. It has no
  `grid-template-rows`, no explicit placement or spans, no
  `grid-template-areas`, no `minmax()`, no `auto-fill`/`auto-fit`.
  Note: the grid row in `tasks/SPEC_COVERAGE.md` still says "parse-only"
  — stale, update it.
- **No scroll model.** `overflow: auto/scroll` does not make a scroll
  container. `position: sticky` is a clamped-relative fallback.
- **Paint** covers backgrounds, gradients, border-radius, outer shadows,
  z-index order, rounded clips, masks, and text decoration. Missing:
  outline, inset shadows, dashed/dotted borders, filters and
  backdrop-filter, `text-overflow: ellipsis`, `object-fit`, form-control
  widgets. `transform-origin` is hardcoded to center.
- **Animations** are strong. Keyframes, transitions, and easing run live,
  and transform/opacity animations reuse compositor layers. Missing:
  animation and transition DOM events, `playbackRate`, a real `finished`
  promise.

## What each site needs

The two sites stress almost disjoint CSS surfaces.

**x.com** is React Native Web. Every container div is
`display:flex; flex-direction:column; flex-shrink:0; min-width:0`. All
dark-mode color comes from inline styles. It uses zero `var()`, zero grid,
zero media queries, and almost no compound selectors. What it leans on:
flexbox correctness, percent resolution rules, `calc()` lengths,
`@font-face` (TwitterChirp), one spinner keyframe, `backdrop-filter` on
the header, and `clip-path: url(#svg)` avatars.

**GitHub** is Primer. It leans on custom properties (about 6,500
definitions per theme), 1,012 media queries, 3,069 attribute selectors,
heavy `:where`/`:is`/`:not`, and a grid page shell. `.Layout` and
`.PageLayout-columns` use `grid-template-columns` and
`grid-template-areas` whose values arrive through `var()`. The header is
flex plus `gap`. Dropdowns are `<details>`/`<summary>`. Sticky headers
and sidebars use `position: sticky`.

Shared dependencies, and so the highest-leverage work overall: flexbox
correctness and `calc()`.

## Tier 1 — x.com layout bugs (small, already root-caused)

Each is a focused fix with a known location. Do these first and in the
main tree — they touch overlapping files, so don't fan them out.

1. **Flex items ignore `max-width`/`max-height` during grow.**
   `FlexLayout.cs` never reads `PropertyId.MaxWidth`. Clamp each item's
   main size to its max during free-space distribution (Flexbox §9.7).
   Fixes the primary column growing to 920 instead of 600, which also
   misplaces the banner, Follow button, and sidebar.
2. **Vertical percent padding and margin resolve against viewport
   height.** `BlockLayout.cs` `ResolveBoxModel` (~line 547). All four
   sides must resolve against the containing block's width. Fixes the
   `padding-bottom:100%` avatar trick becoming a 900px spacer that pushes
   the whole profile below the fold.
3. **Fixed or absolute box with both insets auto snaps to the containing
   block origin.** `PositionLayout.cs` `ResolveAxis` (~line 323). Record
   each box's in-flow position during the block pass and use it as the
   static-position fallback. Fixes the "New to X?" card painting at (0,0).

Exit check: render the xcom-nasa fixture and compare geometry to the
Chromium ground truth above.

## Tier 2 — x.com styling fidelity (small to medium)

4. **`@font-face` name-key matching.** Known bug (first seen on
   angular.dev): a family-name mismatch between the `@font-face` rule and
   the `font-family` declaration drops the web font. Fix the registry
   keying (case and quote normalization). TwitterChirp depends on it, and
   wrong fonts shift every text measurement.
5. **`text-overflow: ellipsis`** plus the `-webkit-box` /
   `-webkit-line-clamp` pattern x.com uses for multi-line clamping.
   Parsed today, never rendered.
6. **Confirm inline-style theming end to end.** Colors, per-side border
   colors, and per-corner radius longhands already work per the audit.
   Add a fixture assertion so it stays true.

## Tier 3 — GitHub shell (the big lift)

7. **Grid completeness** in `GridLayout.cs`: `grid-template-rows`,
   explicit placement (`grid-row`/`grid-column` with spans),
   `grid-template-areas`, `minmax()`, `auto-fill`/`auto-fit`. The shell
   templates arrive through `var()`, so the track-list parser must accept
   cascade-substituted values. Estimated 300–600 lines on the existing
   algorithm. Good candidate for one focused worktree.
8. **Scroll model + real `position: sticky`.** The deepest single gap.
   Scroll containers need geometry (`overflow: auto/scroll`), scroll
   offsets need to reach layout, and sticky needs to pin against them.
   Touches layout, paint, and GUI input, so write a short design doc in
   `browser-plan/` before coding. Both sites' sticky headers depend on it.
9. **`aspect-ratio` and intrinsic sizing keywords.** GitHub uses
   `aspect-ratio` (34 places) and Primer uses `min-content`/
   `max-content`/`fit-content`. Both are parsed but ignored in layout.
10. **Flex polish:** `align-content` for wrapped lines, `align-self`,
    column wrap, baseline alignment. Medium. Mostly affects GitHub
    component layouts.

## Tier 4 — paint polish (independent, fans out cleanly)

Each item is one display-list primitive plus builder emission plus
backend rasterization. Safe to run as parallel worktree tasks.

11. Outline and `outline-offset` (678 uses on GitHub — focus rings).
12. Dashed, dotted, and double border styles.
13. Inset `box-shadow`.
14. `object-fit` / `object-position` (avatars, media).
15. `transform-origin` from the style instead of hardcoded center.
16. `background-origin` / `background-clip` box keywords.
17. Form-control rendering: checkbox and radio glyphs, select arrow,
    placeholder text. GitHub forms render as bare boxes today.
18. Filters and `backdrop-filter` (blur and drop-shadow first). The
    biggest paint item — x.com's header blur and 21 GitHub uses. Fine to
    do last. The fallback (no filter) stays readable.

## Tier 5 — animation events and API gaps (small)

19. **Fire animation and transition events into the DOM**:
    `animationstart`/`end`/`iteration`, `transitionrun`/`start`/`end`/
    `cancel`. Sites gate menu close and cleanup on `transitionend`, so a
    missing event can leave UI stuck. The engines already detect
    completion — the dispatch is what's missing.
20. WAAPI gaps: real `finished` promise, `playbackRate`,
    `getAnimations()`.
21. Interpolate `box-shadow` and gradients instead of snapping at 50%.
    Low priority.

## Sequencing

Tier 1 → Tier 2 makes x.com look right. Tier 3 item 7 plus Tier 4 items
11 and 17 make GitHub's shell and chrome look right. Item 8 (scroll
model) is the long pole — start its design doc early and land it in
parallel with Tier 4. After each tier, re-render both fixtures and
compare against Chromium. Promote `[PendingFact]` tests as behaviors
land, and keep `tasks/SPEC_COVERAGE.md` in sync.
