# Tessera GUI

A small .NET MAUI desktop demo that wraps the Tessera engine in an address-bar
+ viewport shell. Mac Catalyst only at v1; Windows / Android / iOS are
deferred until the engine itself matures further.

The GUI is intentionally **kept out of `Tessera.sln`** — `dotnet build` and
`dotnet test` against the solution work without the MAUI workload installed.
Build the GUI explicitly:

```bash
dotnet build src/Tessera.Gui/Tessera.Gui.csproj -f net10.0-maccatalyst
```

If multiple Xcode versions are installed, pin `DEVELOPER_DIR` to the one
that matches the MAUI workload:

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  dotnet run --project src/Tessera.Gui/Tessera.Gui.csproj --framework net10.0-maccatalyst
```

For a compile-only smoke test without packaging:

```bash
dotnet build src/Tessera.Gui/Tessera.Gui.csproj -f net10.0-maccatalyst -t:CoreCompile
```

## What's in the window

- **Address bar** — accepts `https://`, `http://`, and `file://` URLs. Enter
  submits.
- **Back / Forward / Reload** — driven by `BrowserSession.NavigationHistory`,
  so cookies and history persist for the lifetime of the app.
- **Viewport** — an `Image` control bound to the engine's PNG output, with
  `Aspect.AspectFit` scaling inside a scrollable border.
- **Status bar** — render duration, output dimensions, and the resolved URL.

## What's not in here yet

- No click-to-interact (the engine doesn't dispatch DOM events from screen
  coordinates yet — M4 work).
- No tabs (M5).
- No DevTools, scroll-within-page, selection, or zoom.
- The render is a **PNG snapshot** of a single static layout pass; JS is
  parsed and DOM-ready but not executed during a render (M4 will wire that).

This is the M2-era GUI: it lets a human eyeball what the engine produces on
real URLs, with browser-shaped chrome around it. The interactive surface
lands as JS bindings come online.
