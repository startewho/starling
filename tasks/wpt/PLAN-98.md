# WPT to 98% — parallel execution plan (Round 1 + Round 2)

Goal: drive every measured Web Platform Tests (WPT) area to a 98% per-subtest
pass rate, with the work split so many agents can run at once without stepping
on each other.

This plan was built by fanning out one analysis agent per failing cluster. Each
agent read the real failure messages in `testdata/wpt/results/failures.txt`, the
real WPT test sources, and the Starling source, then reported its root causes,
the exact files it would edit, and a realistic subtest gain. The per-cluster
detail is in the appendix.

## Target engine: Starling

WPT runs on the **Starling JS engine**. `WptRunner.cs:66` builds
`new StarlingEngine(diag)` with no engine override, and `Engine.cs:1224` stands
up "Starling.Js by default". So `failures.txt` and the 81.92% number (run on
2026-05-29) reflect the Starling backend. Every fix below lands in the Starling
stack:

- `src/Starling.Bindings/` (the Starling host objects)
- `src/Starling.Dom`, `src/Starling.Css`, `src/Starling.Js`, `src/Starling.Html`,
  `src/Starling.Layout`, `src/Starling.Codecs`, `src/Starling.Common`

The Starling backend is already fairly complete. So the gaps are subtle — a
wrong DOMException type, a wrong serialization, a missing edge case or accessor —
not whole missing APIs.

## Method

Follow `tasks/wpt/PLAN.md`: measure, rank by subtests unblocked, fix a root
cause, re-measure, attribute the delta. Never edit a test to pass it. Ratchet a
per-area floor after each lane lands.

## Starting point (2026-05-29)

| Area | Now | Subtests to reach 98% |
|---|---|---|
| dom/nodes | 6538/9843 (66.4%) | +3109 |
| css/cssom | 1537/3424 (44.9%) | +1819 |
| css/selectors | 402/1216 (33.1%) | +790 |
| dom/events | 180/493 (36.5%) | +304 |
| css/css-syntax | 298/394 (75.6%) | +89 |
| dom/ranges | 21790/22445 (97.1%) | +207 |
| dom/collections | 12/48 (25.0%) | +36 |
| dom/lists | 145/189 (76.7%) | +41 |
| dom/traversal | 1578/1599 (98.7%) | already there |

---

## The one hard constraint

`src/Starling.Bindings/NodeBindings.cs` is a 3309-line static class that **15 of
the 19 Round 1 clusters edit**. A few other files are shared too:

| Shared file | Clusters that edit it |
|---|---|
| `Bindings/NodeBindings.cs` | 15+ |
| `Bindings/WindowBinding.cs` | 7 |
| `Dom/Document.cs` | 6 |
| `Bindings/CssomBinding.cs` | 6 |
| `Dom/Node.cs` (InsertBefore / RemoveFromParent) | 3 |

You cannot run 19 worktrees in parallel when almost all of them write one file.
The whole plan turns on splitting these files first.

---

## Wave 0 — foundations (one owner, serial, ~1 day)

Everything else depends on this. Do it first, on the main branch.

1. **Split the hotspot files into partial classes.** `NodeBindings.cs` already
   groups its work into `InstallNode / InstallElement / InstallDocument /
   InstallCharacterData / InstallAttr / InstallNamedNodeMap`. Mark the class
   `partial` and move each `Install*` into its own file
   (`NodeBindings.Element.cs`, `NodeBindings.Document.cs`, and so on). Do the
   same for `WindowBinding`, `CssomBinding`, `Document`, `Node`. This is
   mechanical and low-risk, and it turns the 15-way collision into one file per
   subsystem.
2. **Add one mutation-step seam** in `Node.InsertBefore` and
   `RemoveFromParent`. Live Ranges, MutationObserver, and NodeIterators all need
   to be told when nodes move. Today Ranges are not wired at all. This single
   seam unblocks three clusters.
3. **Element foundation.** Make `Element.Namespace` nullable and add the
   `prefix` and `namespaceURI` accessors. Used by element creation,
   querySelector with namespaces, and CSSOM serialization.

---

## Lanes

A lane owns a set of files. After the Wave 0 split, lanes that own different
files run at the same time in their own worktrees. Inside a lane the work is
serial. Clusters from both rounds join the lane that owns their files, so Round
2 work stacks behind the matching Round 1 work in the same lane.

| Lane | Clusters (R1 + R2) | Owns (after split) | Net win | Notes |
|---|---|---|---|---|
| **A · Document factory + XML object model** | nodes-create-element, nodes-domimplementation, r2-xml-domparser, r2-iframe-content-docs | `NodeBindings.{Element,Document}`, `Dom/Document.cs`, `Dom/Element.cs`, `Dom/XmlDocument.cs`(new), `DomParserBinding.cs`(new), `DomWrappers.cs`, `IFrameBinding.cs` | ~643 | createDocument work is counted once across the two rounds |
| **B · CharacterData** | nodes-characterdata-ctors, r2-characterdata-methods | `NodeBindings.CharacterData`, `Dom/Document.cs`(factories) | ~62 | shares `Document.cs` with Lane A — runs after it |
| **C · Mutation, Ranges, Observer, moveBefore, misc** | nodes-mutation-algos, nodes-mutationobserver, ranges, r2-movebefore, r2-node-misc | `Dom/Node.cs`(via Wave 0 seam), `Dom/DomRange.cs`, `RangeBinding.cs`, `Observers/MutationObserverBinding.cs`, `NodeBindings.{Mutation,Range}` | ~477 | owns the `Node.cs` mutation primitives outright |
| **D · Charset / encoding** | nodes-charset | `Common/Encoding/WhatwgEncodingLabels.cs`, `Document.Charset`, `IFrameBinding.cs`, `NodeBindings.Charset` | ~654 | shares `Document.cs`/`IFrameBinding` with A — runs after it |
| **E · Events** | events, nodes-createevent, r2-events-2 | `Dom/Events/*`, `EventTargetBinding.cs`, `NodeBindings.Events`, `PerformanceBinding.cs`, `Common/HighResClock.cs`(new) | ~345 | self-contained in `Dom/Events` |
| **F · Collections / DOMTokenList** | collections-namedprops | `DomWrappers.cs`, `Dom/DomTokenList.cs`, `Js/Runtime/JsRealm.cs`, `NodeBindings.Collections` | ~140 | covers dom/collections, dom/lists, classList |
| **G · Selectors** | queryselector, selectors-has, selectors-logical-anb, selectors-uistate-attr, r2-selector-invalidation, r2-new-pseudos | `Css/Selectors/*`, `QuerySelectorEngine.cs`, `Css/Cascade` restyle, `Layout` style recompute, `NodeBindings.QuerySelector` | ~833 | matching, querySelector, invalidation, new pseudo-classes |
| **H · CSSOM** | cssom-serialize-values, cssom-rule-model, cssom-getcomputedstyle-resolved, css-syntax, r2-cssom-2 | `Css/Cssom/*`, `CssomBinding.cs`, `Css/Tokenizer+Parser`, `Css/Cascade`+`Css/Values`+`Layout/Box` (insets), `CssBinding.cs`, `Css/Properties/PropertyRegistry.cs`, `NodeBindings.Cssom` | ~1615 | the biggest lane — the insets work is XL |
| **I · Abort** | zero-pct-subsystems | `FetchBinding.cs` | 2 | fully isolated |
| **Z · Scope ledger** | r2-scope-defer | `tasks/SPEC_COVERAGE.md`, `CssBinding.cs` (`CSS.supports`) | 0 | records the deferral list — see below |

Two selector clusters (`selectors-has`, `selectors-logical-anb`) reported
JS-engine files like `JsRealm.cs` in their edit list. That is wrong. The `:has`,
`:is`, `:where`, and An+B work lives in `src/Starling.Css/Selectors`. Lane G
owns those files. The stray entries are ignored.

### Wave schedule

- **Wave 0:** foundations (above).
- **Wave 1 (parallel):** A, C, E, F, G, H, I, Z. After the split these own
  different files. G and H both touch `CssomBinding.cs` — give Lane G sole
  ownership of the selector-serializer parts so they stay disjoint, or run H one
  step behind G.
- **Wave 2 (parallel):** B and D. Both wait on Lane A because they share
  `Document.cs`. They own different files from each other, so they run together.

The big lanes (G, H, A) carry the most work and set the critical path. Start
them first inside their wave.

---

## The deferral ledger (Lane Z)

Some failing subtests need a subsystem Starling does not have. They cannot reach
98% by fixing a binding. Like Test262 skips out-of-scope files, these come out
of the 98% denominator. Record them in `tasks/SPEC_COVERAGE.md` so the target is
honest. `tasks/SPEC_COVERAGE.md` confirms none of these were ever v1 scope.

| Deferred group | Subtests | Why |
|---|---|---|
| Media-state selectors (`:playing`, `:paused`, `:muted`, …) | 20 | no `HTMLMediaElement` / `<video>` / `<audio>` pipeline |
| Scrolling events | 39 | no real scroll-offset pipeline, needs WebDriver input injection |
| Wheel-event transactions | 7 | needs WebDriver wheel action chains |
| webkit transition / animation events | 4 | no compositor-fired CSS animation or transition events |
| Shadow DOM (`attachShadow`, `::slotted`, shadow relatedTarget) | 3+ | no Shadow DOM anywhere in Starling |
| Caret hit-testing (`caretRangeFromPoint`, `caretPositionFromPoint`) | 24 | no hit-testing, all marked tentative |
| XPath (`document.evaluate`) | 1 | no XPath engine — a full engine for one subtest is not worth it |
| Observable | 1 | Stage-3 proposal, also blocked on network |
| All `*.tentative.html` | 223 | unstable specs |

Total deferred: about 321 subtests, plus about 27 whole files that already
report no result.

One real gap surfaced here: `CSS.supports()` is missing from `CssBinding.cs`.
Add it (reuse the existing `@supports` evaluator — do not write a second one).
It does not win the media tests, but it unblocks about 5 other files. Those wins
are credited to Lane H.

---

## Coverage roadmap

Net pass rate means after the deferred subtests come out of the denominator.

| Area | Now | After Round 1 + Round 2 (net) | Reaches 98%? |
|---|---|---|---|
| dom/ranges | 97.1% | **98%** | yes |
| dom/collections | 25.0% | **98%+** | yes |
| dom/lists | 76.7% | **98%+** | yes |
| dom/abort | 0% | **100%** | yes |
| css/css-syntax | 75.6% | ~94% | no — short ~18 |
| css/cssom | 44.9% | ~91% | no — short ~234 |
| dom/nodes | 66.4% | ~92% | no — short ~534 |
| css/selectors | 33.1% | ~84% | no — short ~163 |
| dom/events | 36.5% | ~74% | no — short ~107 |

Rounds 1 and 2 win about 4,816 subtests. They take four areas to 98% and the
rest to the low-to-mid 90s and 80s. They do **not** finish the job alone. Five
areas need a Round 3.

---

## Round 3 — the closeout to 98%

Each item is the named remaining surface for one area. These are larger or
need a new subsystem, so they come after Rounds 1 and 2 land and the suite is
re-measured.

- **dom/nodes (+~534): build a real XML / XHTML tree parser.** Today
  `IFrameBinding.cs:379` `ParseXmlIntoDocument` just re-runs the HTML parser and
  flips a flag, so an XML document has no real tree. About 424 `createElementNS`
  rows depend on a true XML parser. Then sweep the long tail of small dom/nodes
  files (importNode, adoptNode, normalize, isEqualNode, contains, attributes).
- **css/cssom (+~234): finish resolved-value and serialization tails.** The
  `getComputedStyle` insets work (Lane H) is XL and may not fully clear on the
  first pass. Close the remaining used-value and declaration-serialization
  cases.
- **css/selectors (+~163): finish style invalidation.** Wire invalidation for
  the form-state, link, target, and custom-state pseudo-classes. Defer the
  media-state, top-layer, and Shadow DOM selectors.
- **dom/events (+~107): activation and focus.** Add activation behavior
  (checkbox and radio click), focus and blur and focusin and focusout, and form
  events. Defer scroll, wheel, animation, and Shadow DOM events.
- **css/css-syntax (+~18): a few tokenizer edges.** Custom-property text
  round-trips and the last escape and whitespace cases.

After Round 3 plus the deferral ledger, every measured area reaches 98%.

---

## Ratchet

`STARLING_WPT_FLOOR` is global only today (`WptTests.cs:41`). To lock each
lane's gain, run a per-area check in CI, for example
`STARLING_WPT_DIRS=dom/ranges STARLING_WPT_FLOOR=98`. Or add a small
`STARLING_WPT_FLOOR_<AREA>` map to the runner. That changes gating, not scoring,
so it stays inside the method. Raise a floor only when a real gain lands. Never
lower one to pass.

---

## Worktree mechanics

Run each parallel lane in its own git worktree. In `.claude` worktrees, the
Edit and Write tools write to the main repo by mistake, so worktree agents must
author files through Bash. Read-only analysis needs no worktree.

---

## Appendix — all 29 clusters

Net win = realistic subtests won. Defer = subtests that need a deferred
subsystem. Files are the conflict footprint.

### Round 1 (19 clusters, the failing files already producing results)

| Cluster | Area | Win | Effort | Files (Starling) |
|---|---|---|---|---|
| nodes-create-element | dom/nodes | 205 | M | NodeBindings, Dom/Element, Dom/Document |
| nodes-domimplementation | dom/nodes | 420 | M | NodeBindings, DomWrappers, WindowBinding, Dom/Document |
| nodes-charset | dom/nodes | 654 | M | Dom/Document, IFrameBinding, NodeBindings, Common/Encoding/WhatwgEncodingLabels |
| nodes-createevent | dom/nodes | 205 | M | EventTargetBinding, NodeBindings |
| nodes-mutation-algos | dom/nodes | 115 | L | NodeBindings, Dom/Node |
| nodes-mutationobserver | dom/nodes | 95 | L | Dom/{Document,Node,Element,Text,NamedNodeMap,AttrNode}, Observers/MutationObserverBinding, WindowBinding |
| nodes-characterdata-ctors | dom/nodes | 48 | M | NodeBindings, Dom/Document |
| collections-namedprops | dom/collections+lists | 140 | L | DomWrappers, NodeBindings, Dom/DomTokenList, Js/Runtime/JsRealm |
| queryselector | dom/nodes | 300 | L | QuerySelectorEngine, NodeBindings, Dom/DomTokenList, Dom/Document, Css/Selectors/{SelectorParser,SelectorMatcher} |
| cssom-serialize-values | css/cssom | 525 | L | NodeBindings, Css/Cssom/{CssValueSerializer,CssomModel}, CssomBinding, Css/Selectors/SelectorSerializer |
| cssom-getcomputedstyle-resolved | css/cssom | 835 | XL | Layout/Box/{Box,UsedInsets}, Css/Cascade/{ComputedStyle,StyleEngine}, Css/Values/{ColorParser,NamedColors}, WindowBinding, CssomBinding |
| cssom-rule-model | css/cssom | 135 | XL | CssomBinding, NodeBindings, WindowBinding, Css/Cssom/{CssomModel,CssPageRule} |
| selectors-has | css/selectors | 168 | M | Css/Selectors/* |
| selectors-logical-anb | css/selectors | 113 | L | Css/Selectors/* |
| selectors-uistate-attr | css/selectors | 116 | L | Css/Selectors/{SelectorAst,SelectorParser,SelectorSerializer,SelectorMatcher}, Css/Cssom/CssomModel, CssomBinding, NodeBindings, QuerySelectorEngine |
| events | dom/events | 118 | L | Dom/Events/{EventDispatcher,Event,EventTarget,EventSubtypes}, EventTargetBinding, NodeBindings |
| ranges | dom/ranges | 206 | L | Dom/Node, Dom/DomRange, NodeBindings, RangeBinding |
| css-syntax | css/css-syntax | 62 | L | Css/Tokenizer/CssTokenizer, Css/Cssom/{CssomModel,CssValueSerializer}, Css/Selectors/SelectorParser, Css/Parser/CssParser, CssomBinding, NodeBindings |
| zero-pct-subsystems | dom/abort | 2 | S | FetchBinding |

### Round 2 (10 clusters, the surface Round 1 did not cover)

| Cluster | Area | Win | Defer | Effort | Files (Starling) |
|---|---|---|---|---|---|
| r2-xml-domparser | dom/nodes | 500 | 424 | L | NodeBindings, DomWrappers, DomParserBinding(new), Dom/Document, Dom/XmlDocument(new) |
| r2-iframe-content-docs | dom/nodes+events | 18 | 38 | L | NodeBindings, WindowBinding, IFrameBinding, DomParserBinding(new), Dom/Document, Dom/Element |
| r2-characterdata-methods | dom/nodes | 14 | 0 | S | NodeBindings |
| r2-node-misc | dom/nodes | 47 | 6 | L | NodeBindings, EventTargetBinding, Dom/Node, Dom/Document, Css/Tokenizer/CssTokenizer |
| r2-movebefore | dom/nodes | 14 | 22 | M | QuerySelectorEngine, NodeBindings, WindowBinding, Dom/Node |
| r2-selector-invalidation | css/selectors | 110 | ~70 | M | Css/Selectors/*, Css/Cascade restyle, Layout (the agent's JS-file list is corrected here) |
| r2-new-pseudos | css/selectors | 26 | 17 | L | Css/Selectors/{SelectorParser,SelectorMatcher,SelectorAst,HeadingArgument(new)}, Dom/Element, Dom/HtmlFormControls, ElementBindings, NodeBindings, WindowBinding |
| r2-cssom-2 | css/cssom | 58 | 9 | XL | CssomBinding, NodeBindings, WindowBinding, CssBinding, Css/Cssom/{CssomModel,CssValueSerializer,ShorthandSerializer(new),MediaList(new)}, Css/Properties/PropertyRegistry |
| r2-events-2 | dom/events | 22 | 9 | L | Dom/Events/{Event,EventDispatcher,EventTarget,EventSubtypes}, EventTargetBinding, NodeBindings, PerformanceBinding, WindowBinding, Common/HighResClock(new) |
| r2-scope-defer | all | 0 | ~321 | S | tasks/SPEC_COVERAGE.md, CssBinding (CSS.supports) |

Note on double counting: `r2-xml-domparser` and `nodes-domimplementation` both
cover `createDocument`. It is one body of work. The coverage roadmap counts it
once.

---

## Session findings (2026-06-04) — what the remaining clusters are actually gated on

A focused implementation pass landed 14 verified commits (≈+1,560 subtests,
overall WPT 88.31% on dom,css,url, zero regressions). Every fix that was a
wrong error type, a missing accessor, a validation gap, a missing interface,
or a WebIDL coercion is done. Verified this session: `AbortSignal` statics,
`Element.prefix`/`namespaceURI`, `createEvent` error type + legacy interfaces +
uninitialized-dispatch, all CSS properties on `element.style`, `querySelector`
`SyntaxError` DOMException, `CharacterData` `ToUint32`, DOMTokenList token
validation + toStringTag (unhung a timing-out test, +976), the HTML element
interface objects, `document` encoding/contentType/textContent, `CSS.supports`,
`getElementById('')`, namespace-aware `tagName`, iframe `defaultView`.

The remaining areas do **not** yield to small fixes. Each was probed and found
gated on a real subsystem:

| Cluster | Hard blocker (verified) |
|---|---|
| dom/collections, named-property access | A live HTMLCollection must be a JS exotic object. The VM reads properties via `AbstractOperations.Get` walking `GetOwnPropertyDescriptor`, so a `JsObject` subclass needs that path debugged (a first attempt resolved indices/length/iteration but not named access / `item` / `getOwnPropertyNames` — reverted). |
| querySelector-All, charset (+654), createElementNS XML (+424), createEvent cross-global, abort | iframe content elements wrap in the **parent** realm, so their exceptions use the wrong realm's DOMException, and `windowFor(iframe.contentDocument)` identity mismatches. Needs per-realm wrapper management for iframe content. |
| prepend/append-on-Document, charset, Node-properties (foreign/xml docs) | `document.implementation.createDocument` does not produce a recognized `Document`; many tests build their fixture through it. |
| createElementNS XML rows, Node-cloneNode XML | No XML/XHTML tree parser (`ParseXmlIntoDocument` is a stub HTML reparse). |
| css/selectors (33%) | Most failures are `getComputedStyle(null)` because `querySelector(':has(...)')` returns null. Needs the `:has`/invalidation engine. |
| dom/events (38%) | Activation behavior, focus/blur/focusin/focusout, full event-subtype init dicts, event-loop `click()` completion. |
| relList/sizes/htmlFor reflected token lists | Must be typed per HTML interface (`img.sizes` is a string, `link.sizes` a token list); a blanket addition regressed −52 and was reverted. |
| css/cssom remaining | Constructable `CSSStyleSheet`, `insertRule`/`deleteRule`, layout-dependent `getComputedStyle` inset resolution. |

**Highest-leverage next unlock:** the HTMLCollection exotic-object ↔
`AbstractOperations.Get` integration, then the iframe cross-realm wrapper. Each
is a dedicated build with interactive debugging, not a per-cluster commit.
