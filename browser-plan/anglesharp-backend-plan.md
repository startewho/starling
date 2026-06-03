# Plan: AngleSharp as a swappable HTML-parser backend

This is the agreed plan for adding AngleSharp as an opt-in, off-by-default HTML
parser, picked at runtime. It mirrors how the JS engine already switches between
the Starling engine and Jint. The Starling parser stays the default.

Read this top to bottom. It is self-contained, so a fresh session can pick it up
and build it without the chat history.

## Why, in one line

AngleSharp is a mature, pure-managed, MIT-licensed HTML/DOM/CSS parser. We add it
behind a seam as a reference backend and correctness oracle, not as a speed
upgrade (see the performance note at the end). No engine project depends on it
except the new backend project plus the selector wiring. It is deletable.

## Locked decisions

1. **Quirks mode: ignore it.** The adapter does not set `Document.Mode`. The
   copied document stays at the default `NoQuirks`. In this engine quirks mode
   only feeds the JS `document.compatMode` string. Nothing in CSS or layout
   reads it, so this is invisible to rendering. We take whatever falls out and
   move on. This also means the adapter needs no internal DOM access for quirks.
2. **Route everything through the backend**, including `innerHTML` / `outerHTML`
   / `insertAdjacentHTML` fragment parsing, not just full-document load.
3. **Build real `<template>` content fragments** in the Starling DOM, because
   Starling's DOM has no template content fragment today and both backends must
   agree on `<template>`.

## What the investigation found (the facts the plan rests on)

- The main document-load path is **one-shot**: `HtmlParser.Parse(string) ->
  Document`. No streaming, no script-blocking pause and resume. Scripts run
  *after* the tree is built. This is why a backend swap is tractable.
- Starling's DOM **cannot be substituted**. Layout, CSS, and the bindings are
  all written against the concrete `Starling.Dom` types. So the AngleSharp
  backend must parse to AngleSharp's own DOM and then **copy that tree into a
  Starling `Document`**. The copy step is the core of the work.
- There is no parser abstraction today. `HtmlParser.Parse` and
  `HtmlTreeBuilder.ParseFragment` are called concretely at every site.

### Parser entry points (in `src/Starling.Html`)

- `HtmlParser.Parse(string html, IDiagnostics? diagnostics = null, bool
  scriptingEnabled = false) -> Document` (facade in `HtmlParser.cs`, delegates to
  `HtmlTreeBuilder.Parse`).
- `HtmlTreeBuilder.ParseFragment(string markup, Element contextElement, Document
  ownerDocument, IDiagnostics? diagnostics = null) -> DocumentFragment`
  (`TreeBuilder/HtmlTreeBuilder.cs`).

### Call sites to reroute

- `src/Starling.Engine/Engine.cs:165` — main page load.
- `src/Starling.Engine/Engine.cs:547` — progressive first-paint path.
- `src/Starling.Bindings/IFrameBinding.cs:221` — iframe load.
- `src/Starling.Bindings/IFrameBinding.cs:381` — XML/XHTML iframe fallback.
- `src/Starling.Bindings/NodeBindings.cs` — `innerHTML` / `outerHTML` /
  `insertAdjacentHTML` (helper around line 2325-2328, callers near 581, 601,
  1115+).
- `src/Starling.Bindings.Jint/NodeBindings.cs` — the Jint mirror (around 1209).
- `src/Starling.Shell.Native/*` — demo and window render call sites
  (`NativePresentDemo.cs:63`, `NativeBrowserWindow.cs:186` and neighbors).

### JS-backend seam to mirror (the precedent)

- Seam interface lives in `src/Starling.Js.Hosting` (`IScriptEngineFactory`,
  `IScriptSession`). It depends only on `Starling.Dom` and `Starling.Common`.
- Backends: `src/Starling.Bindings` (Starling) and
  `src/Starling.Bindings.Jint` (Jint, references only the seam plus the Jint
  package). Each provides a factory.
- Selector: `src/Starling.Engine/JsEngineSelector.cs` reads `STARLING_JS_ENGINE`
  once, caches the choice, and builds the factory. It lives in `Starling.Engine`
  because that is the only project that references both backends.
- Flags: `src/Starling.AppHost/AppHost.cs` maps `--jint` / `--starling` with a
  reusable `SelectFlag` helper, strips them before Aspire, and forwards the
  choice as an environment variable. `src/Starling.Gui/Program.cs` defaults the
  env var when it is unset. Flag beats env var beats default.
- `Starling.Dom` already grants `InternalsVisibleTo` to `Starling.Html` and
  `Starling.Engine`.

## The seam design (HTML)

A small difference from the JS seam: fragment parsing (`innerHTML`) is called
from `Starling.Bindings`, which references `Starling.Html` but not
`Starling.Engine`. A lazy selector in `Starling.Engine` could not reach those
call sites. So we use a **settable holder** that every call site can read,
assigned once at startup by the engine.

Interface, in `src/Starling.Html`:

```csharp
public interface IHtmlParserBackend
{
    string Name { get; }
    Document Parse(string html, IDiagnostics? diagnostics, bool scriptingEnabled);
    DocumentFragment ParseFragment(string markup, Element context,
        Document ownerDocument, IDiagnostics? diagnostics);
}
```

Holder, in `src/Starling.Html`, default is the Starling backend:

```csharp
public static class HtmlParsing
{
    public static IHtmlParserBackend Backend { get; set; } = new StarlingHtmlBackend();
}
```

`StarlingHtmlBackend` (also in `Starling.Html`) just wraps
`HtmlTreeBuilder.Parse` and `HtmlTreeBuilder.ParseFragment`.

AngleSharp never enters `Starling.Html`. It lives only in the new backend
project, which the engine references and uses to set `HtmlParsing.Backend` at
startup.

## Phases

### Phase 1 — Seam, zero behavior change

- Add `IHtmlParserBackend`, `HtmlParsing`, and `StarlingHtmlBackend` to
  `Starling.Html`.
- Reroute every call site above from the concrete static call to
  `HtmlParsing.Backend.Parse(...)` / `.ParseFragment(...)`.
- Default stays Starling. The full test suite must stay green. This is a pure
  refactor with no behavior change.

### Phase 2 — `<template>` content in the Starling DOM

- Add an `HtmlTemplateElement` (or equivalent) whose `Content` is a
  `DocumentFragment`, following the DOM standard for `template.content`.
- Wire `template.content` in both `src/Starling.Bindings/NodeBindings.cs` and
  `src/Starling.Bindings.Jint/NodeBindings.cs`.
- Update the Starling parser's `<template>` handling to place template children
  into the content fragment.
- Tests for the content-fragment semantics. This is core-DOM work that both
  backends need, so it comes before the adapter.

### Phase 3 — The AngleSharp backend and adapter

- New project `src/Starling.Html.AngleSharp/`. References `Starling.Html`,
  `Starling.Dom`, and AngleSharp (already pinned at 1.4.0 in
  `Directory.Packages.props`).
- `AngleSharpHtmlBackend : IHtmlParserBackend`.
- The tree-copy adapter: parse with AngleSharp, walk its `IDocument`, and rebuild
  a Starling `Document` through Starling's public DOM construction API
  (`CreateElement` / `CreateElementNS`, `CreateText` / `CreateComment` /
  `CreateCDataSection`, `CreateDocumentType`, `AppendChild`, `SetAttribute` /
  `SetAttributeNS`).
- Cover: SVG and MathML namespaces and namespaced attributes, doctype, attribute
  case, text, comments, processing instructions, and the new template content
  fragment.
- Fragment-context adapter for `innerHTML`: map the Starling context element into
  an AngleSharp parse context, parse the fragment, and copy the children back.
- Do **not** set quirks mode (locked decision 1).
- Verify the rebuilt tree fires the engine's `NodeConnected` hook the same way
  the Starling parser's output does, so script discovery and run ordering match.
  This is the subtlest correctness risk. Only add `InternalsVisibleTo` if this or
  template content actually forces it, and call it out if so.

### Phase 4 — Selection, validation, docs

- `HtmlBackendSelector` in `src/Starling.Engine`, reading `STARLING_HTML_PARSER`
  (`starling` default, `anglesharp`). It sets `HtmlParsing.Backend` at startup.
  This is the only project that references the AngleSharp backend.
- Flags `--anglesharp-html` / `--starling-html` in `AppHost.cs` and the env-var
  default in `Gui/Program.cs`, matching the Jint pattern.
- Differential test project `tests/Starling.Html.AngleSharp.Tests`: parse the
  html5lib fixtures and our snapshot pages through both backends and assert the
  serialized Starling DOM matches. Any diff is either an adapter bug or a real
  Starling-parser spec gap. Both are worth finding.
- Add an `AngleSharp+copy` column to the existing `HtmlParserBench` so we measure
  the real in-engine cost, not the raw AngleSharp number.
- Short `STARLING_HTML_PARSER` note in `AGENTS.md`, matching the Jint write-up,
  and a `tasks/` work package per the repo workflow.

## Performance note (important, do not skip)

The earlier benchmark measured AngleSharp parsing to **its own** DOM, where it
beat Starling on large pages. The in-engine backend does AngleSharp-parse **plus
a full tree copy** into the Starling DOM. That is strictly more work and more
memory than the raw number. So this backend is unlikely to be faster in the
engine, and may be slower. Its value is as a correctness reference and oracle.
The `AngleSharp+copy` benchmark column in Phase 4 exists to keep this honest.

## How this honors the locked "own engine" decision

`browser-plan/00_INDEX.md` locks "own engine, no Chromium/Gecko/WebKit reuse."
This stays true the same way Jint does: AngleSharp is opt-in, off by default, and
deletable by removing one project and one selector arm. The Starling parser
remains the default and the thing we keep building.
