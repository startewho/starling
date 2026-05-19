---
id: "wp:M3-02a-js-parser-expressions"
parent: "wp:M3-02-js-parser"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T17:45:00Z"
branch: "wp-M3-02a-js-parser-expressions"
completed_at: "2026-05-11T17:55:00Z"
depends_on:
  - "wp:M3-01-js-lexer"
blocks:
  - "wp:M3-02b-js-parser-statements"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#parser"
---

# wp:M3-02a — JS parser: expressions

## Goal
First slice of the JS parser. Lay down the AST type hierarchy under
`src/Starling.Js/Ast/` and a recursive-descent parser that consumes
`JsLexer` output to build expression trees with correct operator
precedence + associativity per ES2024 §13.

## Inputs
- wp:M3-01-js-lexer complete.
- `src/Starling.Js/Lex/JsLexer.cs` producing the token stream.

## Outputs
- `src/Starling.Js/Ast/AstNode.cs` — node hierarchy base.
- `src/Starling.Js/Ast/Expressions.cs` — Literal, Identifier, Binary,
  Unary, Conditional, Assignment, Member, Call, Sequence, This, Null,
  Boolean.
- `src/Starling.Js/Parse/JsParser.cs` — parser scaffold with public
  `ParseExpression(string)` entry plus `ParseProgram` stubbed for
  M3-02b to fill in.

## Acceptance
- 20+ expression test cases passing: arithmetic precedence, parens,
  unary, ternary, assignment chains, member/call combos.
- Expression operator precedence matches ES2024 §13.16 ordering
  exactly (sequence < yield < assign < conditional < null-coalescing
  < logical-or < logical-and < bitwise-or < bitwise-xor < bitwise-and
  < equality < relational < shift < additive < multiplicative <
  exponentiation < unary < update < call/new < primary).

## Out of scope
- Statements / declarations (M3-02b).
- Function expressions / arrow functions (M3-02b or c).
- Class expressions (M3-02c).
- Template literals + tagged templates (lexer doesn't emit them yet).
- RegExp literals (same).
- Destructuring patterns (M3-02d).
- async/await/yield as expression forms (M3-02c).

## Handoff log
- 2026-05-11T17:45Z — created and claimed by agent-claude-cody. Claim
  posted as its own atomic commit before any implementation.
- 2026-05-11T17:55Z — landed Ast/Expressions.cs (21 node types) +
  Parse/JsParser.cs (recursive-descent over ES2024 §13.16 precedence
  ladder). 34 unit tests covering precedence, associativity (especially
  ** right-assoc and assignment right-assoc), unary/update with the
  no-LT rule, optional chaining (?., ?.[, ?.()), new with member chain,
  array elision + spread, object shorthand + computed + reserved-word
  keys, sequence, and basic syntax-error reporting. 245/245 full repo.
  Unblocks wp:M3-02b (statements).
