---
id: "wp:M3-62a-js262-yield-identifier-parse"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "2"
integrated: "main 0b06127 (also js262-phase0-2)"
finding: "only the function-expression-name path rejected `yield` (lexer emits Yield token, not Identifier); for-await destructuring cases already worked. +14 tests."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-62a — JS parser: `yield` as a binding identifier in non-generator contexts

## Why (evidence)
~12 failures `parse:expected ';' after statement (got Yield 'yield')`. Failing
tests show `yield` must be a valid IDENTIFIER outside generators (sloppy mode):
`expressions/generators/yield-as-function-expression-binding-identifier.js`,
`statements/for-await-of/async-func-decl-dstr-array-elem-init-yield-ident-valid.js`
(and many `for-await-of/*-yield-ident-valid.js`). The parser over-restricts
`yield`, treating it as a keyword where the grammar allows it as an identifier.

## Scope
- In `src/Starling.Js/Parse/`, fix `yield` handling so it is a valid
  BindingIdentifier / IdentifierReference when NOT inside a generator and NOT in
  strict mode — including inside an async function's `for await` destructuring
  patterns and a non-generator function expression nested in a generator.
- Keep `yield` a keyword inside generator bodies and in strict mode (those
  early-errors must still fire).
