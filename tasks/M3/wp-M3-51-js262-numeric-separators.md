---
id: "wp:M3-51-js262-numeric-separators"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "1"
integrated: "main ef9421e (also on branch js262-phase0-2)"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-51 — JS lexer: numeric separators `1_000` (Phase 1)

## Why (evidence)
~110 failures with `parse:lexical error: identifier or digit immediately after
numeric literal`. Sample files are all numeric-separator tests:
`literals/bigint/numeric-separators/*`, and
`cpn-*-computed-property-name-from-integer-separators.js`. The lexer rejects the
`_` separator.

## Scope
- `src/Starling.Js/Lex/JsLexer.cs`: support the single-`_` numeric separator in
  decimal, hex (`0x`), octal (`0o`), binary (`0b`), and BigInt (`…n`) literals.
- Enforce the early errors: no leading/trailing `_`, no doubled `__`, no `_`
  adjacent to the radix prefix, the decimal point, the exponent marker, or the
  BigInt `n` suffix.

## Acceptance
- `STARLING_TEST262_FILTER=numeric-separator` and `integer-separators` subsets
  improve; report before/after.
- Existing `Starling.Js.Tests` stay green; add a focused lexer test.
