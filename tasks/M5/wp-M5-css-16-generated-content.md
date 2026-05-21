---
id: wp:M5-css-16-generated-content
milestone: M5
status: "complete"
claimed_by: "agent-claude-cody-content"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T23:30:00Z"
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
- 2026-05-20 — completed by agent-claude-cody-content.

  **Implemented**
  - `PropertyId`: added `Content`, `ListStyleType`, `ListStylePosition`,
    `ListStyleImage`, `Quotes`.
  - `PropertyRegistry`: initial values (`content: normal`, `list-style-type:
    disc`, `-position: outside`, `-image: none`, `quotes: auto`); marked
    `list-style-*` + `quotes` inherited (`content` stays non-inherited); added
    the `list-style` shorthand expander (type || position || image, with the
    `none` ambiguity resolved type-first) + its ShorthandLonghands entry for
    var() pending-substitution. `content` parses through the default path:
    `<string>` → `CssString`, `attr()` → `CssAttrReference` (resolved to a
    string at computed time by the existing cascade `ResolveReferences`).
  - `StyleEngine.ComputePseudoElement(element, pseudo, elementStyle)`: new public
    API that cascades a pseudo-element using the originating element's own
    ComputedStyle as the inheritance parent (CSS Pseudo 4 §3.1) and a
    pseudo-filtered SelectorMatchContext so only `::before`/`::after` rules match.
  - `UaStyleSheet`: `ul/menu { list-style-type: disc }`, `ol { list-style-type:
    decimal }` (inherits to `<li>`), nested `ul ul` → circle, `ul ul ul` → square.
    (`li { display: list-item }` already existed.)
  - `BoxTreeBuilder`: synthesizes `::before` (before children) / `::after` (after
    children) inline boxes carrying the generated string when `content` is
    renderable; prepends a marker TextBox for `display: list-item`. Markers paint
    through the normal text path (no new display item).
  - `ListMarker` (new, table-driven): disc/circle/square Unicode bullets; decimal,
    decimal-leading-zero, lower/upper-alpha (bijective base-26), lower/upper-roman
    (subtractive, decimal fallback outside 1..3999), lower-greek; `none` →
    no marker. Ordinal = 1-based index among `<li>` siblings, honoring
    `<ol start>` and `<li value>`.

  **Tests** (all green)
  - `tests/Starling.Css.Tests/GeneratedContentPropertyTests.cs` — 11 parse tests.
  - `tests/Starling.Css.Spec.Tests/CssContent3/` (7) + `CssLists3/` (6) — `[Spec]`
    `[SpecFact]`.
  - `tests/Starling.Layout.Tests/GeneratedContentLayoutTests.cs` (10) +
    `ListMarkerTests.cs` (numbering tables).

  **Deferred (parse-accepted, documented gaps)**
  - Counters: `counter-reset`/`counter-increment`/`counter()`/`counters()` and
    `@counter-style` — `counter()` in `content` parses to a `CssFunctionValue`
    and generates no box (no text).
  - `quotes`/`open-quote`/`close-quote` — `quotes` parses + inherits but
    open/close-quote produce no text.
  - `list-style-image` rendering (parsed/cascaded; not drawn).
  - `list-style-position: inside` vs `outside` is parsed/cascaded but the marker
    is always laid out inline as a leading fragment (no outside offset into the
    padding). A custom `<string>` list-style-type symbol falls back to a disc.
  - Ordinal counting keys on the `li` tag, not arbitrary `display:list-item`
    elements (sufficient for HTML lists; the WP defines ordinal as sibling index
    among list items).

  **Shared-file note:** none. Changes confined to `src/Starling.Css` +
  `src/Starling.Layout` (+ tests). No edits under `src/Starling.Paint`.
  Worktree was rebased from a stale base (9d3ec43) onto current main (40d1f02)
  before work — clean fast-forward.
