# Web/DOM/JS API Gap — McMaster.com Bundle Analysis

**WP:** `wp:M4-01-api-gap-mcmaster`
**Status:** `complete`
**Date:** 2026-05-20

Bundles analyzed:
- `mcm_d78496ad10ecd233db191e8eb0dbf30d.js` (137 KB — core-js 3 + webpack runtime)
- `mcm_93043416e382333b8830bc7dd755e73f.js` (295 KB — YUI 2.6 + jQuery + Backbone + Handlebars + routing)

Both fetched with Chrome/120 UA; no 404s.

---

## Methodology

Static scan of both minified bundles for Web platform surface calls. Diff against
the Starling binding surface in `src/Starling.Bindings/`.

---

## API Surface Hit in Bundles

| API | B1 | B2 | Starling | Priority |
|-----|----|----|----------|----------|
| `setTimeout/setInterval/clear*` | 23 | 44 | implemented | — |
| `document.getElementById` | 0 | 13 | implemented | — |
| `document.createElement` | 8 | 20 | implemented | — |
| `document.body/documentElement` | 0 | 14 | implemented | — |
| `querySelector/querySelectorAll` | 0 | 14 | implemented | — |
| `getAttribute/setAttribute` | 0 | 25 | implemented | — |
| `appendChild/removeChild/insertBefore` | 0 | 26 | implemented | — |
| `addEventListener/removeEventListener` | 0 | 10 | implemented | — |
| `element.contains` | 0 | 20 | implemented | — |
| `getBoundingClientRect` | 0 | 6 | implemented (zeros) | — |
| `getClientRects` | 0 | 4 | implemented | — |
| `element.remove()` | 0 | 23 | implemented | — |
| `element.matches` | 0 | 2 | implemented | — |
| `localStorage/sessionStorage` | 0 | 0 | implemented | — |
| `history.pushState/replaceState` | 0 | 1 | implemented | — |
| `performance.now/mark` | 4 | 4 | implemented | — |
| `requestAnimationFrame` | 0 | 3 | implemented (needs loop) | — |
| `encodeURIComponent/decodeURIComponent` | 15 | 9 | JS built-in | — |
| `JSON.parse/stringify` | 5 | 8 | JS built-in | — |
| `Promise/Symbol/WeakMap` | 16+ | 0 | JS built-in | — |
| `navigator.userAgent/appName` | 0 | 8 | implemented | — |
| `location.href/hash/pathname` | 0 | 16 | implemented | — |
| `fetch` | 1 | 0 | implemented | — |
| `XMLHttpRequest` | 0 | 5 | implemented | — |
| `MutationObserver` | 1 | 0 | surface installed | — |
| `document.readyState` | 0 | 2 | always "complete" | — |
| `document.cookie` | 0 | 4 | implemented | — |
| `element.cloneNode` | 0 | 5 | **MISSING** | **P1** |
| `element.before/after/prepend/append` | 0 | 14 | **MISSING** | **P1** |
| `element.replaceWith` | 0 | 1 | **MISSING** | P2 |
| `element.style.X` (inline style setters) | 1 | 26 | **MISSING** | **P1** |
| `element.style.cssText` | 0 | 2 | **MISSING** | P2 |
| `element.innerText` | 0 | 3 | **MISSING** | P2 |
| `element.classList` JS binding | 0 | 0 | C# exists, **not wired to JS** | **P1** |
| `element.dataset` JS binding | 0 | 0 | **MISSING** | P2 |
| `element.scrollTo` | 0 | 1 | **MISSING** | P3 |
| `URL` constructor | 5 | 3 | core-js polyfills; **no native** | P2 |
| `URLSearchParams` constructor | 3 | 0 | core-js polyfills; **no native** | P2 |
| `document.hidden/visibilityState` | 0 | 1 | **MISSING** | P2 |
| `document.compatMode` | 0 | 4 | **MISSING** | **P1 — jQuery boot** |
| `document.documentMode` | 0 | 14 | undefined (correct) | — |
| `document.forms` | 0 | 1 | **MISSING** | P3 |
| `navigator.languages/cookieEnabled` | 0 | 0 | **MISSING** | P3 |
| `window.scrollX/scrollY` | 0 | 0 | **MISSING** | P3 |
| `window.matchMedia` | 0 | 0 | **MISSING** | P3 |
| `CustomEvent` constructor | 0 | 0 | **MISSING** | **P1** |
| `atob/btoa` | 0 | 0 | **MISSING** | P3 |

---

## Top Blockers — Critical Boot Path

### P1 — Must fix for jQuery/Backbone/YUI to boot

1. **`element.style` object** — inline style getter/setter. jQuery `.show()`/`.hide()` and
   every DOM-manipulation library that reads or writes visibility depend on this. Currently
   a complete miss — no `style` property on Element.

2. **`element.classList` JS binding** — `ClassList` exists in C# (`DomTokenList`) but is NOT
   wired into JS. Wire with `add/remove/toggle/contains/replace/item/value`.

3. **`element.cloneNode(deep?)` JS binding** — jQuery's internal DOM-building clones elements
   constantly. `wrapAll`, `append`, template expansion all use it.

4. **`element.before` / `after` / `prepend` / `append`** — DOM Living Standard methods, 14
   call-sites in bundle2. `append` takes multiple args / strings (unlike `appendChild`).

5. **`document.compatMode`** — jQuery checks `"CSS1Compat" !== document.compatMode` on boot
   to select the scroll-size calculation path. Missing returns `undefined`, which is falsy
   and triggers the quirks branch.

6. **`CustomEvent` constructor** — SPA frameworks dispatch `CustomEvent` for cross-component
   communication. Zero cost to add given the existing `Event` constructor infrastructure.

### P2 — Needed for full interactive page

7. **`element.innerText`** — Used for visible-text extraction in 3 places.
8. **`element.dataset`** — Data attribute JS access.
9. **`element.replaceWith(...nodes)`** — DOM Living Standard. 1 call-site.
10. **`document.hidden`** — Visibility API. 1 call-site.
11. **`URLSearchParams` / `URL`** — core-js polyfills both; native needed for `instanceof`.
12. **`element.style.cssText`** — Setting all inline styles at once.

### P3 — Nice to have / edge paths

`document.forms`, `window.scrollX/Y`, `window.matchMedia`, `element.insertAdjacentHTML`,
`element.scrollTo`, `element.scrollIntoView`, `atob/btoa`, `navigator.languages/cookieEnabled`.

---

## Large Deferred Gaps (large subsystems)

| Gap | Why deferred | Effort |
|-----|--------------|--------|
| **Full CSSOM `element.style`** | ~200 camelCase properties + CSS parsing/serialization. Needs own WP. | 3-5 d |
| **Shadow DOM / `customElements`** | Not in these bundles. Needs host-model redesign. | 5+ d |
| **`DOMParser`** | Needs HTML parser bridged from `Starling.Html` (separate project). | 1-2 d |
| **`document.write/open`** | Streaming HTML parser mid-page. 2 call-sites (IE iframe shim). | 2 d |
| **`Range` / `Selection`** | Zero call-sites in bundles. Complex spec. | 3+ d |
| **`Worker` / `postMessage`** | 3 postMessage call-sites; full Worker complex. | 2+ d |
| **Native `URL` / `URLSearchParams`** | core-js polyfills; native needed for instanceof. | 1 d |
| **`window.matchMedia`** | Zero bundle call-sites; critical for responsive. | 1 d |

---

## Implemented by this WP

- `element.classList` — JS binding for `DomTokenList`
- `element.cloneNode(deep?)`
- `element.before` / `after` / `prepend` / `append`
- `element.replaceWith(...nodes)`
- `element.innerText` getter/setter
- `element.style` stub (CSSStyleDeclaration-shaped exotic object)
- `document.compatMode`
- `document.hidden` / `document.visibilityState`
- `CustomEvent` constructor (with `detail`)
- `navigator.languages` / `navigator.cookieEnabled`
- `window.scrollX` / `scrollY` / `pageXOffset` / `pageYOffset`

Tests: `tests/Starling.Bindings.Tests/DomApiGapTests.cs`
