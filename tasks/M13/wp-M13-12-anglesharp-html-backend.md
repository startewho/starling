---
id: "wp:M13-12-anglesharp-html-backend"
milestone: "M13"
status: "claimed"
claimed_by: "agent-claude-anglesharp"
claimed_at: "2026-06-03T19:03:12Z"
completed_at: ""
branch: "main"
reviewed_commit: ""
depends_on: []
blocks: []
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/anglesharp-backend-plan.md"
---

# wp:M13-12 — AngleSharp as a swappable HTML-parser backend

## Goal

Add AngleSharp as an opt-in, off-by-default HTML parser, picked at runtime,
mirroring how the JS engine switches between Starling and Jint. The Starling
parser stays the default. AngleSharp is a reference backend and correctness
oracle, not a speed upgrade. The whole thing is deletable by removing one
project reference and one selector arm.

## Inputs

- The Starling parser entry points: `HtmlParser.Parse` and
  `HtmlTreeBuilder.ParseFragment` (in `src/Starling.Html`).
- The Starling DOM public construction API (`src/Starling.Dom`).
- AngleSharp pinned at 1.4.0 in `Directory.Packages.props`.
- The JS-engine seam as the pattern to follow.

## Outputs

- **Phase 1 — seam.** `IHtmlParserBackend`, the settable `HtmlParsing.Backend`
  holder, and the default `StarlingHtmlBackend` (all in `src/Starling.Html`).
  `HtmlParser.Parse` routes through the holder, so every full-document call site
  (engine load, progressive paint, iframe load, native shell chrome) goes
  through the backend with no per-site edits. The two fragment-parse helpers in
  `src/Starling.Bindings/NodeBindings.cs` and
  `src/Starling.Bindings.Jint/NodeBindings.cs` route through
  `HtmlParsing.Backend.ParseFragment`.
- **Phase 2 — template content.** `HtmlTemplateElement : Element` with a
  `Content` fragment owned by a separate inert document (`src/Starling.Dom`).
  `Document.CreateElement` / `CreateElementNS` mint it for HTML `<template>`. The
  tree builder keeps `<template>` open and redirects its children into the
  content fragment (`InsertionTarget`, `StartTemplate` / `EndTemplate`).
  `template.content` is wired in both bindings.
- **Phase 3 — adapter.** `src/Starling.Html.AngleSharp` with
  `AngleSharpHtmlBackend : IHtmlParserBackend`. It parses with AngleSharp and
  copies the tree into a Starling `Document` through the public DOM API. Quirks
  mode is not set (locked decision 1). A differential test project
  (`tests/Starling.Html.AngleSharp.Tests`) parses fixtures through both backends
  and asserts the serialized Starling DOM matches in the common subset.
- **Phase 4 — selection + docs.** `HtmlBackendSelector` in `src/Starling.Engine`
  reads `STARLING_HTML_PARSER` (`starling` default, `anglesharp`) and installs
  the backend. `--anglesharp-html` / `--starling-html` flags in `AppHost.cs`, the
  env-var default in `Gui/Program.cs`, an `AngleSharpCopy` column in
  `bench/Starling.HtmlParserBench`, and a note in `AGENTS.md`.

## Acceptance

- Full `dotnet build` and `dotnet test` stay green with the default (Starling).
- `STARLING_HTML_PARSER=anglesharp` swaps the backend with no other change.
- Template tests: `tests/Starling.Html.Tests/TemplateContentTests.cs` and the
  `template.content` test in `tests/Starling.Bindings.Jint.Tests`.
- Differential tests: `tests/Starling.Html.AngleSharp.Tests`.

## Notes

- Implements the standalone plan in `browser-plan/anglesharp-backend-plan.md`.
- Deviation from the plan, same intent: Phase 1 routes the `HtmlParser.Parse`
  facade through the holder instead of editing each `Parse` call site. This is
  strictly fewer edits, preserves the default parameters the native-shell sites
  rely on, and honors locked decision 2 ("route everything through the backend")
  more completely, since nothing can bypass the seam.
- The template content fragment uses a separate inert owner document so its
  scripts never run and it never lays out (matches the spec's "template contents
  owner"). Inertness depends on that owner having no `NodeConnected` hook.
