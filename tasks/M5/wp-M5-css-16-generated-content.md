---
id: wp:M5-css-16-generated-content
milestone: M5
status: "claimed"
claimed_by: "agent-claude-cody-content"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: ""
branch: "worktree"
depends_on:
  - wp:M1-08-layout-block-inline
  - wp:M1-07-css-cascade
subsystem: Starling.Layout
plan_refs:
  - browser-plan/07_LAYOUT.md
  - browser-plan/06_CSS.md
---

# wp:M5-css-16 — generated content (`content`, `::before`/`::after`) + list markers

## Goal

Add generated content and list markers — neither exists today. `content`,
`list-style-type`, `list-style-position`, `list-style-image`, `quotes`, and the
counter properties are not in `PropertyId` at all, and `BoxTreeBuilder` never
synthesizes `::before`/`::after` boxes or list-item markers. Real sites lean on
`::before`/`::after` (icons, clearfix, decorative glyphs) and `<ul>/<ol>`
markers, so this is high-coverage.

## Inputs

- `src/Starling.Css/Properties/PropertyId.cs` + `PropertyRegistry.cs` — add
  `Content`, `ListStyleType`, `ListStylePosition`, `ListStyleImage` (and
  `Quotes`); typed parse for `content` (`none`/`normal`/`<string>`/`attr()`,
  with `counter()` optional/deferred) and the `list-style` shorthand.
- `src/Starling.Css/Selectors/SelectorMatcher.cs` — pseudo-element matching for
  `::before`/`::after` (selectors already parse pseudo-elements; confirm the
  cascade can produce a ComputedStyle for them).
- `src/Starling.Layout/Tree/BoxTreeBuilder.cs` — where element boxes are built
  (pre-cascade ~44). Synthesize a `::before` box before children and `::after`
  after, when the pseudo's `content` computes to something renderable; synthesize
  a marker box for `display: list-item` / `<li>`.
- `src/Starling.Css/UserAgent/UaStyleSheet.cs` — UA defaults: `ul/ol/li`
  `display: list-item`, default `list-style-type` (disc / decimal), nested-list
  marker types.

## Outputs

- `content` generated boxes: `::before`/`::after` produce anonymous inline boxes
  carrying the generated text (string + `attr()`; `none`/`normal` suppress).
- List markers: `disc`, `circle`, `square`, `decimal`, `decimal-leading-zero`,
  `lower/upper-alpha`, `lower/upper-roman`, `none`; `list-style-position`
  `outside`/`inside`. Ordinal computed from sibling index among list items.
- Tests: `tests/Starling.Css.Tests/` parse + `tests/Starling.Css.Spec.Tests/`
  (`CssContent3/`, `CssLists3/` — create) `[Spec]` `[SpecFact]`;
  `tests/Starling.Layout.Tests/` — `::before { content:"x" }` yields a leading
  text fragment; an `<ol>` of three `<li>` yields markers `1.`/`2.`/`3.`; `disc`
  on `<ul>` yields a bullet glyph.

## Acceptance

- `dotnet build && dotnet test` green (sandbox: `-p:UseAppHost=false`, `sixlabors.lic`).
- `p::before { content: "» " }` prepends the string as a fragment in `<p>`'s
  layout; `content: none` suppresses it.
- `content: attr(data-x)` reflects the element's attribute value.
- `<ul><li>a</li></ul>` renders a disc marker; `<ol>` renders incrementing
  decimal markers; `list-style-type: none` removes them.

## Notes

- Stays in `Starling.Layout` + `Starling.Css`; disjoint from the three paint
  WPs running concurrently. Marker glyphs paint through the normal text path —
  no new display item required.
- Counters (`counter-reset`/`-increment`/`counter()`/`counters()`) and `quotes`/
  `open-quote` may be deferred to a follow-up; if so, parse-accept them and
  document the gap. Roman/alpha numbering is the tedious part — table-drive it.

## Handoff log

- 2026-05-20 — created + claimed by agent-claude-cody-content (orchestrated batch).
