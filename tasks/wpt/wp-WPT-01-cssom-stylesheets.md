---
id: WPT-01
title: CSSOM stylesheet subsystem — document.styleSheets / CSSStyleSheet / CSSStyleRule
status: complete
area: wpt / cssom
branch: wpt-migration
baseline: 25.50% (1339/5251, dom,css,url, sha-pinned, 2026-05-26)
result: 27.80% (1459/5249), css/css-syntax 3.8%→34.3% (+120 subtests, 2026-05-26)
---

## Goal

Build the read/mutate chain `document.styleSheets[i].cssRules[j]` → `CSSStyleRule`
(`.style`, `.selectorText`, `.cssText`) as JS-visible host objects on the
**Starling.Bindings** (non-Jint) backend — the backend the WPT runner exercises
(`StarlingEngine` → Starling.Js default). This is the biggest weak WPT area:
`css/css-syntax` is **15/394 (3.8%)**, gated entirely on the absence of the CSSOM
stylesheet object model.

## Why these tests fail today (measured, not guessed)

Both top css-syntax causes are the *same* absent chain:

- `missing-method:setProperty` (101) — e.g. `css-syntax/decimal-points-in-numbers.html`:
  `document.styleSheets[0].cssRules[0].style.setProperty("line-height","1.0")` then
  `getPropertyValue` must return `"1"`. `element.style.setProperty` already exists
  in shape, but nothing produces the *rule* to call it on.
- `missing-method:slice` (87) — e.g. `css-syntax/anb-parsing.html`:
  `cssRules[0].selectorText = ":nth-child(odd)"` then read back must serialize to
  `2n+1`. `.slice` is a red herring — `selectorText` returns `undefined`, so
  `undefined.slice` throws.

## Scope (in)

1. **`document.styleSheets`** → `StyleSheetList` (array-like: `.length`, indexed
   access, `.item(i)`), reflecting `<style>`/`<link rel=stylesheet>` in document order.
2. **`CSSStyleSheet`**: `.cssRules` (`CSSRuleList`), `.type`, `.ownerNode`, `.href`;
   `.insertRule(text, index)` / `.deleteRule(index)` **only if** causes.txt shows
   tests need them (check before building).
3. **`CSSRuleList`**: array-like (`.length`, indexed, `.item(i)`).
4. **`CSSStyleRule`**: `.selectorText` (get **and** set, round-trip via serialization),
   `.style` (a `CSSStyleDeclaration` backed by the rule's declaration block),
   `.cssText`, `.type`.
5. **`CSSStyleDeclaration` over a rule**: `setProperty`/`getPropertyValue`/
   `removeProperty`/`cssText`/`.length`/`.item(i)`, backed by the rule's live
   `CssDeclaration` list. Reuse the existing inline-style decl logic where possible.
6. **CSS value serialization** (number/dimension canonicalization) so
   `getPropertyValue` round-trips per spec: `"1.0"`→`"1"`, `".1"`→`"0.1"`,
   `"1.0px"`→`"1px"`, and `"1."` / `"1.px"` are **rejected** (invalid → property unset).
7. **Selector serialization** (`SelectorAst` → string), incl. **An+B** serialization
   for `:nth-child()` (`odd`→`2n+1`, `even`→`2n`, `+1`→`1`, `5N`→`5n`, etc.) so
   `anb-parsing.html` and `anb-serialization.html` pass.

## Scope (out)

- Jint backend mirror (parity only; not needed for the WPT delta — do it only if cheap).
- `@media`/`@import`/`@keyframes` rule objects (`CSSMediaRule` etc.) unless a measured
  cause demands it.
- Computed-style / cascade changes — CSSOM edits need not re-trigger layout for these tests.

## Acceptance

- `css-syntax/decimal-points-in-numbers.html`, `anb-parsing.html`,
  `anb-serialization.html` pass.
- **Measured WPT delta** (predict from `causes.txt` → re-measure → attribute), reported
  as "pass X→Y, css-syntax A%→B%". No regression in `dom`/`url` areas.
- Regression unit tests (MSTest + AwesomeAssertions, `[Spec]`/`[SpecFact]` where a
  spec section applies) covering: styleSheets/cssRules wiring, selectorText round-trip,
  An+B serialization, value canonicalization.
- Full existing suite green (Css, Js, Bindings tests) — this touches shared CSS
  parse/serialize code.
- `tasks/wpt/PLAN.md` status log updated with the measured delta.

## Notes (map from recon)

- CSS parser/AST: `src/Starling.Css/Parser/{CssParser,CssAst}.cs`
  (`StyleSheet`/`StyleRule`/`AtRule`/`CssDeclaration`; prelude is token-based).
- Selectors: `src/Starling.Css/Selectors/{SelectorParser,SelectorAst}.cs` — **no
  serialization exists**; An+B parse lives (or must live) here.
- Inline style decl (reuse): `src/Starling.Bindings/NodeBindings.cs`
  (`BuildInlineStyleDecl`, ~1500-1563; `setProperty`/`getPropertyValue`/`removeProperty`).
- DOM→JS binding helpers: `EventTargetBinding.DefineAccessor/DefineMethod`,
  identity map `DomWrappers`.
- Stylesheets are parsed in `src/Starling.Paint/Painter.cs` (`AddAuthorStylesheets`,
  ~325-357) for layout — **not** stored JS-visibly on Document. Bridge needed:
  parse `<style>` text into a `StyleSheet` and back the CSSOM by a *live, mutable*
  structure so `setProperty`/`selectorText` edits round-trip (CssAst records may need
  a mutable decl/prelude layer).
