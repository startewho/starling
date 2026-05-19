---
id: "wp:M0-01-scaffold"
milestone: "M0"
status: "complete"
claimed_by: ""
claimed_at: ""
branch: ""
completed_at: "2026-05-11T14:46:00Z"
depends_on: []
blocks:
  - "wp:M0-02-common"
  - "wp:M1-01-html-tokenizer"
  - "wp:M1-05-css-tokenizer-parser"
  - "wp:M2-01-url-parser"
  - "wp:M3-01-js-lexer"
subsystem: "build"
plan_refs:
  - "browser-plan/02_PROJECT_SETUP.md"
  - "browser-plan/14_AGENT_TASKS.md#wpm0-01-scaffold"
---

# wp:M0-01 — Solution scaffold

## Goal
Land `Starling.sln`, `Directory.Build.props`, `Directory.Packages.props`,
`.editorconfig`, `.gitignore`, `.github/workflows/ci.yml`, all 13 source
projects + 13 test projects + 1 e2e + 1 bench + 1 headless project, so
subsequent packages have somewhere to write code.

## Inputs
None.

## Outputs
- `Starling.sln`
- `Directory.Build.props` / `Directory.Packages.props`
- `src/Starling.{Common,Url,Net,Html,Dom,Css,Layout,Paint,Js,Bindings,Loop,Engine,Shell,Headless}`
- `tests/Starling.*.Tests` + `tests/Starling.E2E`
- `bench/Starling.Bench`
- `.github/workflows/ci.yml`

## Acceptance
- `dotnet build` exits 0 on the OS matrix.
- `dotnet test` exits 0.
- Rule-0 lint (no `DllImport` / `LibraryImport` in engine modules) passes.

## Notes
- A follow-up session fixed two build-config bugs that surfaced on first run
  with SDK 10.0.203: `GenerateDocumentationFile` had to be enabled for the
  `EnforceCodeStyleInBuild`+`IDE0005=error` combo to compile, and `CS1591` is
  globally suppressed since the project doesn't ship XML docs.

## Handoff log
- 2026-05-11T14:46Z — initial scaffold landed.
- 2026-05-11T15:10Z — fixed Directory.Build.props (`GenerateDocumentationFile`/`CS1591`).
- 2026-05-11T15:14Z — complete; `dotnet build` + `dotnet test` green (34/34).
