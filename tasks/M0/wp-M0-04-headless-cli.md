---
id: "wp:M0-04-headless-cli"
milestone: "M0"
status: "complete"
claimed_by: ""
claimed_at: ""
branch: ""
completed_at: "2026-05-11T14:58:00Z"
depends_on:
  - "wp:M0-03-paint-stub"
blocks:
  - "wp:M2-07-network-end-to-end"
subsystem: "Tessera.Headless"
plan_refs:
  - "browser-plan/02_PROJECT_SETUP.md#headless-cli-shape"
  - "browser-plan/14_AGENT_TASKS.md#wpm0-04-headless-cli"
---

# wp:M0-04 — Headless CLI

## Goal
`tessera render <url> -o out.png` writes a PNG. Bare filesystem paths are
auto-normalized to `file://`. Subcommands beyond `render` are stubbed with
a "not yet implemented" message.

## Inputs
- wp:M0-03-paint-stub complete.

## Outputs
- `src/Tessera.Headless/Program.cs`

## Acceptance
- `dotnet run --project src/Tessera.Headless -- render testdata/hello.html -o out.png` succeeds.
- E2E test `RenderE2ETests.Render_hello_html_fixture` passes.

## Handoff log
- 2026-05-11T14:58Z — complete; CLI shape matches plan §02.
