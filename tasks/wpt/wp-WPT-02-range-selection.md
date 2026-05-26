# WP-WPT-02: Standard DOM Range + Selection (DOM §4.6, §5)

## Status: complete

## Goal
Implement the `Range` (DOM §4.6) and `Selection` (DOM §5) APIs so
WPT `dom/ranges/` tests start passing. Baseline was 1/224 dom/ranges
subtests (common.js setup was crashing before any subtests ran).

## Scope

**In scope:**
- `DomRange` host object implementing DOM §4.6 boundary-point Range model
- `document.createRange()` and `new Range()` constructor
- All standard Range prototype methods: `setStart`, `setEnd`, `setStartBefore`,
  `setStartAfter`, `setEndBefore`, `setEndAfter`, `collapse`,
  `selectNode`, `selectNodeContents`, `compareBoundaryPoints`,
  `comparePoint`, `isPointInRange`, `intersectsNode`, `cloneRange`,
  `detach`, `deleteContents`, `toString`, plus read-only attributes
  `startContainer`, `startOffset`, `endContainer`, `endOffset`,
  `collapsed`, `commonAncestorContainer`
- `StaticRange` stub (constructor only, no mutation tracking)
- `Selection` global stub (`window.getSelection()` → `null`,
  `document.getSelection()` → `null`)
- `document.doctype` accessor (exposes DocumentType child node)
- `document.createCDATASection()` (fixes harness setup in common.js)
- `new Document()` constructor (fixes 28 harness errors)
- CharacterData mixin methods: `data` (r/w), `substringData`, `appendData`,
  `insertData`, `deleteData`, `replaceData`, `splitText`, `wholeText`
- Node type constants (ELEMENT_NODE=1 … NOTATION_NODE=12) on both Node
  constructor and Node.prototype
- `DOCUMENT_POSITION_*` constants (1,2,4,8,16,32) on Node constructor and
  Node.prototype
- `compareDocumentPosition` (full DOM §4.4.5 algorithm including
  DISCONNECTED + PRECEDING/FOLLOWING consistency requirement)
- `isSameNode`, `isEqualNode`
- `String.prototype.substr` (Annex B §B.2.2.1, used by Range-mutations.js)

**Explicitly out of scope (correctly remain failing):**
- `dom/ranges/tentative/OpaqueRange-*` — CSS Anchor Positioning API, not
  standard Range; ~109 subtests remain failing as expected
- Live range mutation tracking (Range boundary auto-update on DOM mutation,
  dom/ranges/Range-adopt-test.html) — requires mutation-observer integration
- `Selection` implementation beyond stub (`removeAllRanges`, `addRange` etc.)
- `cloneContents`, `extractContents`, `surroundContents` — depend on iframes
  and generate NOTRUN subtests (4176 NOTRUN remain)

## Predicted delta (from causes.txt, pre-implementation)
- `createRange` cluster: ~54 direct + downstream from common.js setup
  completing → hundreds of dom/ranges subtests previously blocked
- `compareDocumentPosition`: 1242 assert_in_array subtests in
  Node-compareDocumentPosition.html
- `document.doctype` + `substr`: ~600+ (doctype→280+252+124 arg-type
  failures; substr→2642 Range-mutations subtests)

## Observed delta
- dom/ranges (focused): 1/224 subtests → 35876/44491 (80.64%)
- dom/nodes: 3477/7356 (47.3%) → 4719/7356 (64.2%)
  (+1242 passes from compareDocumentPosition fix)
- Full suite (dom,css,url): 1459/5250 (27.80%) → 6528/16843 (38.76%)
  (+5069 passes; denominator expanded because Range tests now produce results)
- compareDocumentPosition: 0% → 100% (1444/1444)

## Files changed
- `src/Starling.Dom/DomRange.cs` — new (full DOM §4.6 host implementation)
- `src/Starling.Bindings/RangeBinding.cs` — new (JS bindings layer)
- `src/Starling.Bindings/NodeBindings.cs` — modified (CharacterData methods,
  Node type + DOCUMENT_POSITION constants, compareDocumentPosition,
  isSameNode, isEqualNode, document.doctype, document.createCDATASection,
  new Document() constructor, compareDocumentPosition helper methods)
- `src/Starling.Bindings/WindowBinding.cs` — modified (Install() step 10:
  RangeBinding.Install(realm))
- `src/Starling.Js/Intrinsics/StringCtor.cs` — modified (String.prototype.substr)

## Deferred items
- Live range mutation tracking (Range auto-collapse on node removal)
- Selection API beyond stub
- `cloneContents`, `extractContents`, `surroundContents` full implementation
  (blocked by missing iframe support)
- `DOMParser` constructor (1 failure in StaticRange-constructor.html)
