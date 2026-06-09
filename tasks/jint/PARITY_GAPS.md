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

## Status (2026-06-08 parity pass)

**Tier 1:** 🟢 complete. **Tier 2:** 🟢 **all 9 families complete** (CSSOM,
traversal, ranges, selection, iframe, FontFace, Web Animations, WebAssembly,
Attr/NamedNodeMap). **Tier 3:** 🟢 **complete** — btoa/atob/structuredClone,
console, `element.style` full coverage, `document.cookie`, MutationObserver (real
firing), `document.createEvent`, **Blob/File**, **FormData completeness**,
**AbortController→fetch**, **IntersectionObserver/ResizeObserver** liveness.
**Tier 4:** 🟢 **complete** — legacy Event surface
(initEvent/initCustomEvent/cancelBubble/returnValue + dispatchEvent
InvalidStateError), `CSS[@@toStringTag]`, storage named access, TextDecoder
`fatal`+labels, `window.onerror`, the Node/Element/Document/CharacterData method
holes (getRootNode, compare/isSame/isEqualNode, lookupNamespace*/baseURI,
hasAttributes, click, insertAdjacent*, prefix, namespaced attr methods,
getElementsByTagNameNS, doctype + DocumentType, getElementsByName,
createAttribute(NS)/CDATASection/PI, adoptNode/importNode, DOMImplementation,
CharacterData mutation + splitText/wholeText), **fetch body coverage**
(Response.blob/formData, getSetCookie, multipart/urlencoded bodies), **URL engine**
(WHATWG `Starling.Url`), **CSS Typed OM style maps**, and **navigator extras**.
Jint binding test suite: **249 passing, 0 failing.** Remaining smaller sub-gaps are
noted inline (e.g. AbortSignal.timeout has no timer; TextDecoder covers four
encodings; IntersectionObserver re-delivery needs layout hooks; no legacy
`window.event`).

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
  - ✅ (resolved 2026-06-08): `element.attributes` is now a real live `NamedNodeMap`
    via the Tier 2 Attr family port — see the Attr / NamedNodeMap item below.

---

## Tier 2 — install the missing families

Each is a reference binding file with no Jint counterpart. None are registered in
`JintBindings.InstallAll`.

- [x] 🟢 **CSSOM stylesheets** (port `CssomBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/CssomBinding.cs` backed by engine-neutral
  `Starling.Css.Cssom` (CssomStyleSheet/Rule/DeclarationBlock + `CssParser`):
  `document.styleSheets` → StyleSheetList (indexed + length + item) → CSSStyleSheet
  (cssRules/rules/type/href/title/disabled) → CSSRuleList → CSSStyleRule
  (selectorText get/set, style, cssText, type) + at-rule placeholders, and a live
  CSSStyleDeclaration (cssText/length/getPropertyValue/Priority/setProperty/
  removeProperty/item + camel+kebab accessors). `element.sheet` on &lt;style&gt;
  (null otherwise), cached + re-parsed on text change. Reuses
  `NodeBindings.StylePropertyNames`/`KebabToCamelPublic`. Registered after
  SelectionBinding. Tests: `CssomStyleSheetBindingsTests` (6).
- [x] 🟢 **DOM traversal** (port `TraversalBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/TraversalBinding.cs` installs `NodeFilter` (no-op
  function carrying all `SHOW_*`/`FILTER_*` constants), `TreeWalker`/`NodeIterator`
  interface prototypes + illegal-constructor globals, and wires
  `document.createTreeWalker`/`createNodeIterator` onto `DocumentPrototype`. Host
  walking algorithms (`HostTreeWalker`/`HostNodeIterator`) ported literally from
  WHATWG §6.2.2/§6.3.2; the §6.1 filter (callable or `acceptNode` object, fresh
  every step, active-flag → `InvalidStateError`) runs through Jint's `JsValue.Call`.
  Registered in `JintBindings.InstallAll` after NodeBindings. Tests:
  `TraversalBindingsTests` (8) — constants, root/currentNode/instanceof, preorder
  nextNode, whatToShow, function + acceptNode-object filters, NodeIterator fwd/back,
  firstChild/parentNode.
- [x] 🟢 **Ranges** (port `RangeBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/RangeBinding.cs` wraps the engine-neutral
  `Starling.Dom.DomRange`: full `Range` prototype (start/end container+offset,
  collapsed, commonAncestorContainer; setStart/End[Before/After], collapse,
  selectNode[Contents], compareBoundaryPoints/comparePoint/isPointInRange/
  intersectsNode, cloneRange/detach, toString, deleteContents/cloneContents/
  extractContents/insertNode/surroundContents), START_TO_*/END_TO_* constants on
  ctor+prototype, the `Range` constructor, `document.createRange()`, and a
  `StaticRange` constructor. Per-engine wrapper identity cache; `DomRangeException`
  → real `DOMException`. Registered after TraversalBinding. Tests:
  `RangeBindingsTests` (7).
- [x] 🟢 **Selection** (port `SelectionBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/SelectionBinding.cs` wraps the engine-neutral
  `Starling.Dom.DomSelection`: full Selection prototype (anchor/focus node+offset,
  isCollapsed, rangeCount, type, direction; getRangeAt/addRange/removeRange/
  removeAllRanges/empty, collapse/setPosition/collapseToStart/End, extend,
  setBaseAndExtent, selectAllChildren, containsNode, deleteFromDocument, toString,
  modify/getComposedRanges stubs), a non-constructible `Selection` global, and
  `window.getSelection()`/`document.getSelection()` returning the one per-document
  Selection (stable identity). Registered after RangeBinding. Tests:
  `SelectionBindingsTests` (6).
- [x] 🟢 **IFrame** (port `IFrameBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/IFrameBinding.cs`: each &lt;iframe&gt; gets a nested
  about:blank `Document`, `iframe.contentDocument` (wrapped in the parent engine),
  a `contentWindow` proxy (document/self/window/parent/top/frameElement/length),
  `document.defaultView` resolving a nested doc → its contentWindow, and lazy
  `iframe.src` load (fetch via `ctx.Fetch`, HtmlParser, nested classic inline
  scripts run in a child engine, then a `load` event + `onload` handler).
  Registered after WebAssemblyBinding. Tests: `IFrameBindingsTests` (5) — blank
  contentDocument, contentWindow wiring/identity, defaultView, src load + nested
  script DOM mutation + load event, onload handler.
  **Intentional divergence:** Jint has one realm per engine, so there is no
  cross-realm window with its own globals — `contentWindow` is a parent-engine
  proxy and nested scripts run in a separate child engine (their globals are not
  shared with the parent). No postMessage, no frame history; `src` is fetched
  synchronously on first contentDocument/contentWindow access rather than off-thread.
- [x] 🟢 **FontFace** (port `FontFaceBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/FontFaceBinding.cs` over engine-neutral
  `Starling.Css.FontLoading.{FontFace,FontFaceSet}` + `FontFaceParser`: the global
  `FontFace` constructor (family/source + style/weight/stretch/unicodeRange
  descriptors, status, load(), loaded) and `document.fonts` (FontFaceSet:
  size/status/ready, add/delete/has/clear/check/load/forEach, inert
  addEventListener), seeded per document from `@font-face` rules in &lt;style&gt;
  sheets (each marked loaded). Promises via `engine.Advanced.RegisterPromise()`.
  Registered after AttrBinding. Tests: `FontFaceBindingsTests` (6).
- [x] 🟢 **Web Animations** (port `WebAnimationsBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/WebAnimationsBinding.cs` adds `element.animate(keyframes,
  options)` returning an `Animation` (play/pause/cancel/finish/reverse, currentTime/
  startTime/playState/playbackRate/id, finished/ready promises, onfinish/oncancel,
  inert addEventListener) with an associated `KeyframeEffect`
  (getKeyframes/getTiming/getComputedTiming). Parses both array and property-indexed
  keyframe forms + numeric/object timing into the neutral
  `Starling.Js.Hosting.{AnimationKeyframeSpec,AnimationEffectTimingSpec}`, registering
  with an `IAnimationHost` when the layout host implements one (inert otherwise,
  matching the canonical no-host path). Registered after FontFaceBinding. Tests:
  `WebAnimationsBindingsTests` (6).
- [x] 🟢 **WebAssembly** (port `WebAssemblyBinding.cs`). Done (2026-06-08). New
  `src/Starling.Bindings.Jint/WebAssemblyBinding.cs` adds a real Wasmtime-backed
  `WebAssembly` global (added `Wasmtime` package ref to the Jint csproj): Module,
  Instance (numeric exports run, host imports, memory/table exports), Memory
  (buffer/grow), Table (length/get/set/grow), CompileError/LinkError/RuntimeError,
  and compile/validate/instantiate + compileStreaming/instantiateStreaming. Value
  marshaling i32/i64(BigInt)/f32/f64, host-import callbacks, a dedicated big-stack
  execution thread (Blazor recursion), and promise helpers via
  `engine.Advanced.RegisterPromise()`. Registered after WebAnimationsBinding. Tests:
  `WebAssemblyBindingsTests` (7) — real add-module execution, validate, memory,
  errors. **Intentional divergence:** `Memory.buffer` returns a fresh snapshot
  ArrayBuffer each access (Jint has no public zero-copy storage hook equivalent to
  the canonical engine's `JsArrayBuffer.Wrap(IJsArrayBufferStorage)`); live mutation
  of wasm memory through a long-held JS `buffer` view is the one remaining sub-gap.
- [x] 🟢 **Attr / NamedNodeMap as real interfaces**. Done (2026-06-08). New
  `src/Starling.Bindings.Jint/AttrBinding.cs` installs a real `NamedNodeMap`
  interface (item/getNamedItem[NS]/setNamedItem[NS]/removeNamedItem[NS]/length +
  `@@toStringTag`) and a live `JintNamedNodeMapObject` exotic backing
  `element.attributes` (indexed + named access to wrapped `AttrNode`s, prototype
  methods never shadowed). Element gains `getAttributeNode[NS]`/`setAttributeNode[NS]`/
  `removeAttributeNode` (NotFoundError on a foreign node) and `document.createAttribute`.
  `NamedNodeMap` removed from the NodeBindings marker loop; `element.attributes` no
  longer a snapshot array. The `Attr` prototype/global already existed (Tier 1).
  Registered after CssomBinding. Tests: `AttrBindingsTests` (6).
  (`document.createAttributeNS` was later added in the Tier-4 pass via the public
  `Document.CreateAttributeNS`.)

---

## Tier 3 — fill the data and utility holes

- [x] 🟢 **`btoa` / `atob` / `structuredClone`** (port from `CoreWebApiBinding.cs`).
  Done (2026-06-08). New `src/Starling.Bindings.Jint/CoreWebApiBinding.cs`:
  forgiving-base64 `btoa`/`atob` (InvalidCharacterError on bad input) and
  `structuredClone` over the common value graph (primitives, arrays, plain objects
  with cycle handling, ArrayBuffer, typed arrays rebuilt to the same kind;
  Symbol/Function → DataCloneError). Registered in InstallAll. Tests:
  `CoreWebApiBindingsTests`.
- [x] 🟢 **Blob / File** — Done (2026-06-08). New
  `src/Starling.Bindings.Jint/BlobFileFormDataBinding.cs` installs real `Blob`
  (constructor over string/ArrayBuffer/typed-array/Blob parts; `size`/`type`/`text`/
  `arrayBuffer`/`slice` + `@@toStringTag`) and `File extends Blob` (`name`/
  `lastModified`). Removed the throw-on-construct `Blob`/`File` markers from
  NodeBindings; `Response.blob()` now mints a real Blob. Tests:
  `BlobFileFormDataAbortTests`.
- [x] 🟢 **`document.cookie`** — Done (2026-06-08). `JintBackendContext` now owns a
  `CookieJar` (`Cookies`), and `CookieBinding` routes the getter through
  `BuildCookieHeader(BaseUrl)` and the setter through `StoreFromHeaders(BaseUrl, …)`
  — so script cookie get/set round-trips (name overwrite, multiple cookies). Tests:
  `CookieBindingsTests` (3).
  - Remaining sub-gap: the jar is per-context/in-memory; the hosting seam
    (`ScriptSessionOptions`) does not yet hand the Jint backend the HTTP client's
    jar, so script-set cookies are not shared with fetch/XHR `Cookie`/`Set-Cookie`
    headers. A seam change (add `CookieJar` to `ScriptSessionOptions`) would unify them.
- [x] 🟢 **FormData completeness** — Done (2026-06-08). The full `FormData` is now
  in `BlobFileFormDataBinding` (append/set/delete/get/getAll/has/forEach/keys/values/
  `@@iterator`), accepts Blob/File values (non-File Blob wrapped in a `File`), and is
  serialized as a fetch body — `BodyToBytes` builds `multipart/form-data` with a
  boundary and auto-sets the Content-Type. `new FormData(formElement)` still reads
  form fields via the `__starlingFormDataEntries` hook. Tests:
  `BlobFileFormDataAbortTests`.
- [x] 🟢 **AbortController / AbortSignal → fetch** — Done (2026-06-08). `AbortSignal`
  now has a real listener registry (`addEventListener('abort')` + `onabort`) and the
  static `abort`/`timeout`/`any`; `AbortController.abort()` fires the `abort` event.
  `fetch()` reads the signal from init/Request: an already-aborted signal rejects with
  an `AbortError` DOMException, and an in-flight request is raced against an abort.
  `Request.signal` returns the passed signal. Tests: `BlobFileFormDataAbortTests`.
  - Remaining sub-gap: `AbortSignal.timeout(ms)` returns a signal but has no real
    timer wiring (does not auto-abort), and the abort is observed at the JS layer
    rather than cancelling the underlying `StarlingHttpClient` send.
- [x] 🟢 **console methods** — Done (2026-06-08). New
  `src/Starling.Bindings.Jint/ConsoleBinding.cs` is now the single console
  implementation: log/info/warn/error/debug/trace/dir/table **plus** `time`/
  `timeEnd`/`timeLog`, `count`/`countReset`, `group`/`groupCollapsed`/`groupEnd`,
  `assert`, `clear`. Output routes through a sink, so the live `JintScriptSession`
  (its `InstallConsole` now calls `ConsoleBinding.Install` with the real
  ConsoleSink) and the bare unit-test context share one implementation. InstallAll
  installs a logger-backed console for bare contexts (idempotent — skips when the
  session already installed one). Tests: `CoreWebApiBindingsTests.console_has_full_method_set`.
- [x] 🟢 **`element.style` full property coverage** — Done (2026-06-08).
  `NodeBindings` now builds inline-style accessors from `AllStyleProperties` =
  the curated common list ∪ `PropertyRegistry.All` (every registered CSS property),
  so uncommon longhands (e.g. `borderTopLeftRadius`) read `""` and round-trip
  instead of `undefined`. (CSSOM declaration blocks keep the curated `StylePropertyNames`.)
  Test: `LegacyEventAndStyleTests.element_style_exposes_uncommon_longhands`.
- [x] 🟢 **MutationObserver real firing** — Done (2026-06-08). New
  `src/Starling.Bindings.Jint/MutationObserverBinding.cs` is a real implementation:
  subscribes to the document's internal mutation hooks (added
  `Starling.Bindings.Jint` to `Starling.Dom`'s `InternalsVisibleTo`), validates
  `observe()` options (childList/attributes/characterData required → TypeError,
  attributeFilter must be a string sequence, attributeOldValue/attributeFilter
  default attributes on), queues real MutationRecords (type/target/addedNodes/
  removedNodes/previous+nextSibling/attributeName/oldValue) honoring subtree +
  attributeFilter, batches per observer, delivers on a microtask (`ctx.Post`) and
  drains synchronously on `takeRecords()`; `disconnect()` clears state. Adds a
  `MutationRecord` global (illegal ctor) for `instanceof`. Removed the surface-only
  MutationObserver from `ObserversBinding`. Tests: `MutationObserverBindingsTests`
  (6) — childList, attribute+oldValue+filter, subtree gating, option validation,
  disconnect.
- [x] 🟢 **IntersectionObserver liveness** — Done (2026-06-08, the bookkeeping +
  options). `observe`/`unobserve`/`disconnect` now track observed targets (dedup on
  observe, real removal/clear), and the constructor parses `root`/`rootMargin`/
  `threshold` into the matching `root`/`rootMargin`/`thresholds` accessors. Tests:
  `NavigatorAndObserversTests`.
  - Remaining sub-gap: still fires one fixed `ratio:1` record per `observe()` (the
    documented headless one-shot model); re-delivery on scroll/relayout needs layout
    hooks that the bare/headless context does not have.
- [x] 🟢 **ResizeObserver** — Done (2026-06-08). `observe()` validates the `box`
  option (`content-box`/`border-box`/`device-pixel-content-box` → TypeError otherwise)
  and tracks targets with real `unobserve`/`disconnect`. Tests:
  `NavigatorAndObserversTests`.
- [x] 🟢 **`document.createEvent`** — Done (2026-06-08). Replaced the fake plain
  object with `EventTargetBinding.CreateLegacyEvent`: a real uninitialized host
  event of the requested legacy interface (Event/Events/HTMLEvents, CustomEvent,
  UIEvent, MouseEvent, KeyboardEvent), wrapped against its subtype prototype so it
  is dispatchable and `instanceof` resolves; `NotSupportedError` for unknown
  interfaces. Pairs with the new legacy Event surface below
  (initEvent/initCustomEvent). Tests: `LegacyEventAndStyleTests`.

---

## Tier 4 — smaller correctness gaps

- [ ] 🟡 **Node / Element / Document method holes:** (partial — 2026-06-08)
  - [x] Node: `getRootNode`, `compareDocumentPosition` (+ the six `DOCUMENT_POSITION_*`
    constants), `isSameNode`, `isEqualNode` (structural), `lookupNamespaceURI`/
    `lookupPrefix`/`isDefaultNamespace`, `baseURI` — done. Tests: `NodeMethodHolesTests`,
    `NamespacedAndDocumentTests`.
  - [x] Element: `click`, `hasAttributes`, `prefix`, `getAttributeNode`/`setAttributeNode`/
    `removeAttributeNode` (Tier 2 Attr family), `insertAdjacentElement`/`insertAdjacentText`,
    and the namespaced attr methods (`getAttributeNS`/`setAttributeNS`/`hasAttributeNS`/
    `removeAttributeNS`/`getElementsByTagNameNS`) — done. Tests: `NodeMethodHolesTests`,
    `AttrBindingsTests`, `CharacterDataAndAdjacentTests`, `NamespacedAndDocumentTests`.
    `tagName` no longer force-upper-cases non-HTML elements (HTML upper-cases;
    SVG/MathML preserve the DOM-stored case). Tests: `LegacyEventAndStyleTests`.
    - [ ] Remaining: full SVG/MathML case *preservation* at element creation is a
      DOM-layer concern (`Document.CreateElementNS` normalizes case) and is shared
      with the canonical backend.
  - [x] Document: `doctype` + DocumentType `name`/`publicId`/`systemId`,
    `getElementsByName`, `getElementsByTagNameNS`, `createAttribute`/`createAttributeNS`,
    `createCDATASection`, `createProcessingInstruction`, `adoptNode`, `importNode`, and
    `DOMImplementation` (createHTMLDocument/createDocumentType/createDocument) — done.
    Tests: `NodeMethodHolesTests`, `NamespacedAndDocumentTests`.
  - [x] CharacterData: `substringData`/`appendData`/`insertData`/`deleteData`/`replaceData`
    (IndexSizeError on out-of-bounds offset), `Text.splitText`, `Text.wholeText` — done.
    Tests: `CharacterDataAndAdjacentTests`.
- [x] 🟢 **`window.onerror`** — Done (2026-06-08, the handler). New
  `EventTargetBinding.ReportException` runs HTML's "report the exception":
  `window.onerror(message, source, lineno, colno, error)` is invoked on an uncaught
  event-listener error and on uncaught script/pump errors (session `ReportUncaught`
  routes through it); a truthy return cancels the default console/log report. Tests:
  `WindowOnErrorTests` (3).
  - Remaining sub-gap: `onerror` always reports line/column 0 (the engine does not
    surface the throw site here). (Legacy `window.event` is now implemented — see the
    Event legacy surface item.)
- [x] 🟢 **`Event` legacy surface** — Done (2026-06-08, the methods). Added to the
  Jint Event prototype: `initEvent` (type mandatory → TypeError; backed by
  `DomEvent.InitEvent`), `cancelBubble` (get = propagation-stopped; set truthy ⇒
  stopPropagation) and `returnValue` (get = !defaultPrevented; set false ⇒
  preventDefault); and `CustomEvent.initCustomEvent` (sets type/bubbles/cancelable +
  detail). Tests: `LegacyEventAndStyleTests`.
  - `dispatchEvent` `InvalidStateError` checks (already-dispatching / uninitialized)
    are now done (createEvent events start uninitialized; re-entrant dispatch throws),
    and legacy `window.event` (the event currently being dispatched) is exposed via an
    `EventState.CurrentEvent` slot set around each listener call. Tests:
    `LegacyEventAndStyleTests`.
    - Remaining sub-gap: passive default-value computation (`MarkWindowTarget`) not ported.
- [x] 🟢 **localStorage / sessionStorage named access** — Done (2026-06-08).
  `StorageBinding` now builds a `JintStorageObject` named-property exotic (HTML
  §12.3.2): arbitrary string keys read/write storage items (`storage.foo = x`
  persists, `delete storage.foo`, `'foo' in storage`, key enumeration), while
  interface members (length/getItem/…) on the prototype are never shadowed. Tests:
  `StorageNamedAccessTests` (4).
- [x] 🟢 **TextDecoder** — Done (2026-06-08). `EncodingBinding` now enforces
  `fatal` (a strict decoder throws `TypeError` on malformed input) and resolves
  labels to real encodings: utf-8 / utf-16le / utf-16be / windows-1252 (latin1/
  ascii aliases) via a WHATWG label→canonical-name map; an unknown label throws
  `RangeError` from the `TextDecoder` constructor, and `decoder.encoding` reports
  the canonical name. Tests: `TextDecoderFatalTests` (5).
  - Remaining sub-gap: only utf-8/utf-16le/utf-16be/windows-1252 are recognized;
    other WHATWG legacy encodings (Shift_JIS, GBK, EUC-*, …) currently throw
    `RangeError` rather than decoding.
- [x] 🟢 **fetch body coverage** — Done (2026-06-08). `BodyToBytes` now serializes
  Blob/File (bytes + `type`), FormData (multipart + boundary), and URLSearchParams
  (urlencoded), auto-setting the Content-Type on requests/responses. Added
  `Response.formData()` (urlencoded fully + a basic multipart parser), real
  `Response.blob()`, and `Headers.getSetCookie()` (uncombined Set-Cookie list via a
  new native bridge). Tests: `BlobFileFormDataAbortTests`.
- [x] 🟢 **URL engine** — Done (2026-06-08). `UrlBinding`'s C# bridge now parses with
  the WHATWG `Starling.Url.UrlParser` (and resolves relative URLs against a parsed
  base) instead of `System.Uri`/`UriBuilder`. Components, origin (tuple for
  http(s)/ws(s)/ftp, `"null"` otherwise), default-port stripping, and the component
  setters (`with`-record rebuild: protocol/host/hostname/port/pathname/search/hash)
  all follow the WHATWG model, matching the reference backend on the edge cases.
  Tests: `UrlWhatwgTests` (7).
- [x] 🟢 **`CSS[Symbol.toStringTag] = "CSS"`** — Done (2026-06-08). Added in
  `CssBinding`.
- [x] 🟢 **CSS Typed OM style maps** — Done (2026-06-08). `element.attributeStyleMap`
  is a mutable StylePropertyMap over the inline `style` attribute
  (get/getAll/has/set/append/delete/clear/size/forEach; values are CSSStyleValue via
  the global `CSSStyleValue.parse`, set accepts CSSStyleValue or string), and
  `element.computedStyleMap()` is a read-only StylePropertyMapReadOnly over the
  layout host's computed style (get/getAll/has/size/forEach). Tests:
  `TypedOmStyleMapTests` (4).
- [x] 🟢 **navigator extras** — Done (2026-06-08). `BuildNavigator` adds
  `hardwareConcurrency` (= `Environment.ProcessorCount`), `maxTouchPoints` (0),
  `webdriver` (false), `pdfViewerEnabled` (false), and stub sub-APIs `clipboard`
  (writeText/readText → resolved promises), `geolocation`
  (getCurrentPosition fires the error callback with PERMISSION_DENIED; watch/clear),
  and `serviceWorker` (register rejects; getRegistration(s) resolve;
  controller=null). Tests: `NavigatorAndObserversTests`. Note: this makes Jint a
  *superset* of the canonical backend here, which lacks these.

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
