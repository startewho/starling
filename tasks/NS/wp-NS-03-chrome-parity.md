---
id: wp:NS-03-chrome-parity
milestone: NS
status: "completed"
claimed_by: ""
claimed_at: ""
completed_at: "2026-05-30"
branch: "native-shell"
depends_on: []
blocks: []
subsystem: Starling.Shell.Native
plan_refs:
  - docs/native-shell.md
---

# wp:NS-03-chrome-parity — Engine-rendered chrome to Avalonia parity

## Goal

Bring the native shell's chrome up to what the Avalonia shell offers, rendered as
engine HTML documents composited above and around the page. The composite path
already exists (`NativeViewportRenderer.PresentComposited` +
`Compositor.AppendSurfaceOps`); this fills in the actual chrome.

## Scope

- A real, interactive URL bar (type and go, not just a label).
- Tabs, with the tab strip as engine-rendered chrome and per-tab page state.
- History navigation in the UI (back/forward), wired to
  `BrowserSession.BackInteractiveAsync` / `ForwardInteractiveAsync`.
- The find bar, devtools panels, and context menus as engine documents.
- Multi-window.

## Acceptance

- A user can type a URL, open and switch tabs, and go back and forward, all
  through engine-rendered chrome in the native shell.

## Status note (native-shell branch)

Done: editable URL bar + --url launch (UrlBarInputNormalizer), back/forward/
reload (Cmd+[ /] /R, Alt+arrows), tabs (per-tab BrowserSession, engine-rendered
tab strip, Cmd+T/W, Cmd+1-9, Ctrl+Tab), find-in-page (Cmd+F, CollectFragments +
substring, highlight via a new optional overlayRoot layer on PresentComposited).
Now also: context menus, a devtools DOM-inspector panel (F12), and
multi-window (Cmd+N, process-per-window). All verified headlessly (--frames);
interactive paths need a display. Richer devtools (inspect-on-hover, styles,
console) is future work.
