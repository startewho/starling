# Jint binding parity gaps — tracker

Goal: bring the **Jint** backend (`src/Starling.Bindings.Jint/`) to full Web-API
binding parity with the canonical **Starling.Js** backend (`src/Starling.Bindings/`).
This is the open "full Web-API binding parity" goal from
[`TRACKER.md`](TRACKER.md). The J0–J7 packages in that tracker built the engine
seam, event loop, and module loader. This file tracks the **API surface** that is
still missing or stubbed.

Source: a 5-way symbol-level diff of every binding family (2026-06-08).

| Status legend |
|---|
| ⬜ `todo` · 🟡 `partial` · 🟢 `done` |

"Missing entirely" = no Jint code exists. "Partial" = the symbol is present but a
no-op or thinner than the reference.

---

## Tier 1 — structural fixes (unblock the most surface)

These are wiring-level. Each one fixes a whole family at once.

- [x] 🟢 **Fix prototype routing in `JintDomWrapper.SelectPrototype`.** Done
  (2026-06-08). Added prototype slots + `SelectPrototype` arms for Text, Comment,
  CDATA, ProcessingInstruction, DocumentFragment, DocumentType, and Attr, each
  chained per the DOM hierarchy; `NodeBindings` now builds and registers them and
  wires real `Comment`/`CDATASection`/`ProcessingInstruction`/`DocumentFragment`/
  `DocumentType`/`Attr` globals (removed from the marker loop). `textNode.data`/
  `.length` are now reachable and `instanceof Text/Comment/CharacterData/…` resolve.
  Added a minimal `InstallAttr` (name/value/ownerElement/…) so a wrapped AttrNode
  reads back. Tests: `Tier1ParityTests.TextNode_data_and_length_are_reachable`,
  `Node_subtypes_resolve_instanceof`.
  - Remaining sub-gap (Tier 2): `DocumentFragment.prototype` has no `querySelector`/
    `querySelectorAll` yet; `DocumentType` name/publicId/systemId and `document.doctype`
    are still absent (Tier 4); Attr is routed but `element.attributes` is still a plain
    snapshot array, not a real `NamedNodeMap` (Tier 2).

- [x] 🟢 **Real `DOMException`.** Done (2026-06-08). New
  `src/Starling.Bindings.Jint/DomExceptionBinding.cs` ports the canonical:
  constructible `DOMException(message, name)` with own `name`/`message`, a `code`
  accessor over all 21 names, `toString`, all 25 legacy `*_ERR` constants on ctor +
  prototype, and `Make(ctx, name, msg)` / `Throw(ctx, name, msg)` helpers (stored
  prototype slot on `JintDomWrapper`). Removed the `DOMException` marker from
  `NodeBindings`; installed in `InstallAll`. Tests:
  `DomException_is_constructible_with_name_message_code`,
  `DomException_instanceof_constants_and_toString`.
  - Remaining: existing binding call sites still throw plain `TypeError` in most
    places — migrating them to `DomExceptionBinding.Throw` is follow-up (the helper
    now exists, so it is a mechanical change per call site).

- [x] 🟢 **Event subtype property reflection + subtype prototype on dispatch.** Done
  (2026-06-08). `EventTargetBinding.InstallEventSubtypes` builds real subtype
  prototypes + constructors that build the host subtype (`MouseEvent`/`KeyboardEvent`/
  `FocusEvent`/`UiEvent`) and read the init dictionary into it; the init dict is kept
  on the wrapper (`EventState._initDict`) so members with no host slot read back.
  `InvokeJsListener` now wraps a host-fired event against
  `EventState.GetEventPrototypeForHost(ev)`, so a real `click`/`keydown` listener sees
  `instanceof MouseEvent` and `event.clientX`.
  - [x] `MouseEvent`: `clientX/Y`, `x/y`, `pageX/Y`, `screenX/Y`, `button`, `buttons`, `ctrl/shift/alt/metaKey`, `relatedTarget`
  - [x] `KeyboardEvent`: `key`, `code`, `repeat`, `location`, `isComposing`, `charCode`, `keyCode`, `which`, modifier keys
  - [x] `UIEvent`: `detail`, `view`; `WheelEvent`: `deltaX/Y/Z`, `deltaMode`; `FocusEvent`: `relatedTarget`; `CompositionEvent`: `data`
  - [x] Host subtype prototype selected in `InvokeJsListener` (`GetEventPrototypeForHost`)
  - Tests: `MouseEvent_reflects_init_and_chains_instanceof`, `KeyboardEvent_reflects_key_and_code`, `Dispatched_event_keeps_subtype_prototype_and_properties`.

- [x] 🟢 **Live collection objects.** Done (2026-06-08). New
  `src/Starling.Bindings.Jint/CollectionsBinding.cs` adds exotic `JintNodeListObject`
  (indexed + `length` + `item` + `values`/`keys`/`entries`/`forEach`/`@@iterator`),
  `JintHtmlCollectionObject` (also named access by id/name + `namedItem` + `@@iterator`),
  and `JintDomTokenListObject` (indexed + `@@iterator`/iteration), each with a real
  interface prototype + constructor so `instanceof NodeList`/`HTMLCollection`/
  `DOMTokenList` resolve and `@@toStringTag` is set. `NodeBindings` returns them for
  `childNodes` (NodeList, live), `children`/`getElementsByTagName`/`getElementsByClassName`
  (HTMLCollection, live), `querySelectorAll` (static NodeList), and `classList`
  (DOMTokenList, indexable + iterable). Tests:
  `ChildNodes_is_a_NodeList_with_item_and_iteration`, `HtmlCollection_named_access_and_namedItem`,
  `QuerySelectorAll_is_a_static_NodeList`, `ChildNodes_is_live`, `ClassList_is_indexable_and_iterable`.
  - Remaining sub-gap (Tier 2): `NamedNodeMap` for `element.attributes` is NOT done —
    it depends on the real Attr-node family (Tier 2). `element.attributes` still
    returns a plain snapshot array of `{name,value}`.

---

## Tier 2 — install the missing families

Each is a reference binding file with no Jint counterpart. None are registered in
`JintBindings.InstallAll`.

- [ ] ⬜ **CSSOM stylesheets** (port `CssomBinding.cs`): `document.styleSheets`,
  `StyleSheetList`, `*.sheet` on style/link elements, `CSSStyleSheet`, `CSSRuleList`,
  `CSSStyleRule`, and the live CSSOM-backed `CSSStyleDeclaration`.
- [ ] ⬜ **DOM traversal** (port `TraversalBinding.cs`): `NodeFilter` (+ `SHOW_*`
  constants), `TreeWalker`, `NodeIterator`, `document.createTreeWalker` /
  `createNodeIterator`.
- [ ] ⬜ **Ranges** (port `RangeBinding.cs`): `Range` full prototype, `StaticRange`,
  `document.createRange`.
- [ ] ⬜ **Selection** (port `SelectionBinding.cs`): `Selection`,
  `window.getSelection`, `document.getSelection`.
- [ ] ⬜ **IFrame** (port `IFrameBinding.cs`): `contentWindow` / `contentDocument`,
  nested `BrowsingContext`, cross-realm wiring, `load` event, parser-inserted
  subframes. Today `top`/`parent`/`frames` are hard-wired to the window itself.
- [ ] ⬜ **FontFace** (port `FontFaceBinding.cs`): `FontFace` constructor and
  `document.fonts` (FontFaceSet), seeded from `@font-face` rules.
- [ ] ⬜ **Web Animations** (port `WebAnimationsBinding.cs`): `Element.animate`,
  `Animation`, `KeyframeEffect`.
- [ ] ⬜ **WebAssembly** (port `WebAssemblyBinding.cs`): the whole `WebAssembly`
  global. Blocks Blazor WASM and any `typeof WebAssembly` feature-detect.
- [ ] ⬜ **Attr / NamedNodeMap as real interfaces**: `Attr` prototype
  (`name`/`value`/`ownerElement`/...), Attr-node wrapping, `element.attributes`
  returning a NamedNodeMap, and the Element Attr-node methods
  (`getAttributeNode`/`setAttributeNode`/...).

---

## Tier 3 — fill the data and utility holes

- [ ] ⬜ **`btoa` / `atob` / `structuredClone`** (port from `CoreWebApiBinding.cs`).
  All three throw `ReferenceError` today.
- [ ] ⬜ **Blob / File** — real classes (constructor, `size`, `type`, `text`,
  `arrayBuffer`, `slice`; File adds `name`, `lastModified`). Today they are
  throw-on-construct markers, so `new Blob`/`new File` throw and `Response.blob()`
  cannot return a real Blob.
- [ ] ⬜ **`document.cookie`** — non-functional stub (getter returns `""`, setter
  discards). Wire a `CookieJar` into `JintBackendContext` and route through it.
- [ ] ⬜ **FormData completeness** — add `set`, `delete`, `getAll`, `has`, `forEach`,
  `keys`, `values`; support Blob/File values; make it serializable as a fetch body
  (multipart with boundary + content-type).
- [ ] ⬜ **AbortController / AbortSignal → fetch** — `signal` is dropped in
  `ParseRequestInit`/`Fetch`, so there is no cancellation, no `abort` event, and
  `Request.signal` is always null. Add the static `abort`/`timeout`/`any` too.
- [ ] ⬜ **console methods** — add `time`, `timeEnd`, `count`, `countReset`,
  `group`, `groupCollapsed`, `groupEnd`, `assert`, `clear`. These throw
  "not a function" on real sites today.
- [ ] ⬜ **`element.style` full property coverage** — replace the hard-coded 78-entry
  list with `InlineStyleProperties ∪ PropertyRegistry.All` (every registered CSS
  property), so uncommon longhands return `""` instead of `undefined`.
- [ ] 🟡 **MutationObserver real firing** — port `MutationObserverState`, the
  `Document` mutation-hook subscriptions, and `observe()` argument validation so
  records actually fire and bad options throw. Today `observe` is a no-op and
  `takeRecords` always returns `[]`.
- [ ] 🟡 **IntersectionObserver liveness** — make `unobserve`/`disconnect`/
  `takeRecords` real, parse `root`/`rootMargin`/`threshold`, add the matching
  accessors, and re-deliver on scroll/relayout. Today it fires one fixed
  `ratio:1` record per `observe()` and never again.
- [ ] 🟡 **ResizeObserver** — `box` option validation and target bookkeeping
  (port `ResizeObserverState`).
- [ ] 🟡 **`document.createEvent`** — returns a fake non-dispatchable plain object.
  Port `CreateLegacyEvent`: a real uninitialized host event, `initEvent`/
  `initCustomEvent`, `NotSupportedError` for unknown interfaces.

---

## Tier 4 — smaller correctness gaps

- [ ] 🟡 **Node / Element / Document method holes:**
  - [ ] Node: `getRootNode`, `compareDocumentPosition` (+ constants), `isSameNode`, `isEqualNode`, `lookupNamespaceURI`/`lookupPrefix`/`isDefaultNamespace`, `baseURI`; move ChildNode/ParentNode mixins onto `Node.prototype`
  - [ ] Element: `prefix`, `click`, `hasAttributes`, `insertAdjacentElement`/`insertAdjacentText`, namespaced attr methods (`getAttributeNS`/`setAttributeNS`/`hasAttributeNS`/`removeAttributeNS`/`getElementsByTagNameNS`), namespace-correct `tagName` casing (do not upper-case SVG/MathML)
  - [ ] Document: `doctype`, `getElementsByName`, `getElementsByTagNameNS`, `createAttribute(NS)`, `createCDATASection`, `createProcessingInstruction`, `adoptNode`, `importNode`; `DOMImplementation.createDocument`/`createDocumentType`
  - [ ] CharacterData: `substringData`/`appendData`/`insertData`/`deleteData`/`replaceData`, `Text.splitText`, `Text.wholeText` (depends on the Tier 1 prototype-routing fix)
- [ ] ⬜ **`window.onerror`** — never invoked on uncaught listener errors (Jint only
  logs). Also no legacy `window.event`.
- [ ] ⬜ **`Event` legacy surface** — `initEvent`, `cancelBubble`, `returnValue`;
  `CustomEvent.initCustomEvent`. Add the `dispatchEvent` `InvalidStateError` checks
  (already-dispatching, uninitialized) and passive default-value computation
  (`MarkWindowTarget`).
- [ ] ⬜ **localStorage / sessionStorage named access** — `storage.foo = x` does not
  persist. Make `JsStorage` a named-property exotic.
- [ ] 🟡 **TextDecoder** — enforce `fatal` (throw on malformed UTF-8) and resolve
  non-UTF-8 labels to a real `System.Text.Encoding`.
- [ ] 🟡 **fetch body coverage** — `BodyToBytes` string-coerces FormData /
  URLSearchParams / Blob to junk with no content-type. Add `Response.formData()`,
  real `Response.blob()`, and `getSetCookie()` on Headers.
- [ ] 🟡 **URL engine** — Jint parses with `System.Uri`/`UriBuilder`; the reference
  uses the WHATWG `Starling.Url.UrlParser`. Same symbols, different results on edge
  cases (default-port stripping, empty-path normalization, non-special schemes,
  internationalized domain names). Switch to `Starling.Url`.
- [ ] ⬜ **`CSS[Symbol.toStringTag] = "CSS"`** — one-line add in `CssBinding`.
- [ ] ⬜ **CSS Typed OM style maps** — `element.attributeStyleMap` and
  `element.computedStyleMap()`.
- [ ] 🟡 **navigator extras** — `hardwareConcurrency`, `maxTouchPoints`, `webdriver`,
  and the sub-APIs `clipboard`, `geolocation`, `serviceWorker`. (Missing in both
  backends, so lower priority for strict parity.)

---

## Already at parity — no action

Timers (Jint is a superset: adds `setImmediate`, `queueMicrotask`,
`requestIdleCallback`), requestAnimationFrame/cancelAnimationFrame, Performance
(stub-for-stub), History, Location (Jint ahead), matchMedia / MediaQueryList, the
`window.CSS` namespace (minus the `@@toStringTag` line above), getComputedStyle's
~48-property shape, navigator core fields, XMLHttpRequest (Jint ahead — adds
`timeout`, `withCredentials`, `upload`), crypto `getRandomValues` / `randomUUID`,
EventTarget core + base Event properties + dispatch-phase semantics, and ES modules
/ dynamic import / `import.meta.url`.

## Out of scope — missing in both backends

Not parity gaps: `PerformanceObserver`, `WebSocket`, `EventSource`, `crypto.subtle`,
`FileReader`/`FileList`, `responseType='blob'/'document'` and `responseXML` on XHR,
and import maps (`<script type="importmap">` — flagged in `TRACKER.md` J4 as the open
module gap, but also absent from the reference).
