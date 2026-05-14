---
id: "wp:M3-06l-ci-policy"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-ci"
claimed_at: "2026-05-14T15:09:29Z"
completed_at: "2026-05-14T18:25:00Z"
branch: "main"
depends_on:
  - "wp:M3-06d-codecs"
  - "wp:M3-06e-sslstream-tls"
  - "wp:M3-06h-skia-interop"
blocks: []
subsystem: "build"
plan_refs:
  - "browser-plan/02_PROJECT_SETUP.md#ci"
  - "browser-plan/12_TESTING.md#interop-seam-policy-test"
  - "browser-plan/03_NETWORKING.md#tls-approach"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06l-ci-policy — repurpose the lint job to the interop seam policy

## Goal

Phase 10 (CI half): flip the CI from a blanket "Rule 0" `DllImport`/`LibraryImport`
ban to a **project allowlist** — the same 12-project grep list, with the two
interop projects (`Tessera.Skia`, `Tessera.Codecs`) simply never added. Add the
native Skia package restore and the Linux codec libs to the `build` job, and
rewrite the matching test-policy assertions. The CI lint job is repurposed, not
deleted.

## Inputs

- `wp:M3-06d-codecs` complete: `Tessera.Codecs` exists and uses `LibraryImport`.
- `wp:M3-06e-sslstream-tls` complete: BouncyCastle gone; `Tessera.Net` uses
  `SslStream` (still P/Invoke-free).
- `wp:M3-06h-skia-interop` complete: `Tessera.Skia` exists and uses
  `LibraryImport`; a native package needs restoring before `dotnet build`.

## Outputs

- `.github/workflows/ci.yml` — `lint` job: blanket `DllImport|LibraryImport` ban
  → project allowlist (the two interop projects omitted); job/step renamed to
  the interop seam policy. `build` job: add Linux `apt-get install libpng16-16
  libjpeg-turbo8 libwebp7`; restore the native Skia package before
  `dotnet build`.
- `browser-plan/12_TESTING.md` — rename "Rule 0 lint test" → "interop seam
  policy test"; `NoPInvoke_InAnyEngineProject` excludes `Tessera.Skia` +
  `Tessera.Codecs`; **delete** `NoSslStream_InNetProject`.
- The test code backing the above assertions (the policy test class) updated to
  match.

## Acceptance

- The `lint` job greps a 12-project allowlist; `LibraryImport` in
  `Tessera.Skia` / `Tessera.Codecs` passes, `LibraryImport` anywhere else fails
  the job.
- The job and step are renamed away from "Rule 0" to the interop seam policy.
- `build` installs `libpng16-16 libjpeg-turbo8 libwebp7` on Linux and restores
  the native Skia package before `dotnet build`.
- `NoPInvoke_InAnyEngineProject` excludes the two interop projects;
  `NoSslStream_InNetProject` is deleted; `12_TESTING.md` prose matches.
- `dotnet build && dotnet test` green on win/mac/linux with the repurposed job.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 10,
  "CI" bullet).
- This is the CI/test counterpart of `06f-docs-policy` (prose docs) — keep the
  policy wording consistent between them.
- `native.yml` itself is owned by `06b-native-build`; this package only adds the
  *restore* of its artifact to `ci.yml`. `.github/workflows/` is shared with
  `06b` — coordinate via handoff log.
- `09_JS_ENGINE.md`'s grep stays valid — no change needed there.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T15:09:29Z — claimed (agent-claude-cody-ci). Doing the non-Skia-dependent
  portion only: `ci.yml` lint job repurposed to the interop seam policy, Linux codec
  libs added to the `build` job, `12_TESTING.md` prose updated. The native-Skia-package
  restore in the `build` job stays a `# TODO(wp:M3-06h)` placeholder until `Tessera.Skia`
  lands. No real policy test class exists in `tests/` (the `RuleZeroTests` in
  `12_TESTING.md` is a doc sketch only) — so there is no test code to rewrite.
- 2026-05-14T15:09:29Z — implemented (agent-claude-cody-ci). DONE:
  - `.github/workflows/ci.yml`: `lint` job renamed `lint (formatting + interop policy)`;
    step renamed `interop seam policy — confine native interop` with rewritten comment
    (allowlist-by-omission, `Tessera.Skia` forward-declared via wp:M3-06h note); the
    12-project grep list kept verbatim. `build` job: added Linux-only
    `apt-get install libpng16-16 libjpeg-turbo8 libwebp7` step before Build; added a
    `# TODO(wp:M3-06h)` placeholder for the native Skia package restore.
  - `browser-plan/12_TESTING.md`: "Interop seam policy test" prose updated to describe
    the project-allowlist grep + the two interop projects; `RuleZeroTests` sketch
    renamed to `InteropSeamPolicyTests`; `NoSslStream_InNetProject` removed.
  - `dotnet build` + `dotnet test` both green (8000+ tests pass); ci.yml YAML validated.
  REMAINING TODO (deferred — needs wp:M3-06h / `Tessera.Skia` to exist):
  - the `# TODO(wp:M3-06h)` native Skia package restore step in the `build` job.
  Status stays `claimed` (WP not fully complete) pending that one step.
- 2026-05-14T18:25:00Z — completed (agent-claude-cody). Resolved the remaining
  `# TODO(wp:M3-06h)` step. First pass added a graceful ImageSharp fallback so a
  no-shim environment stayed green — but the user rejected that ("i don't want
  the fallback at all: make this CORRECT"): Skia Graphite is the engine's *sole*
  rasterizer and a missing shim must be a loud, hard failure, not a silent
  degradation. Final state (committed `wp:M3-06 — remove the ImageSharp
  fallback…`): the ImageSharp fallback is gone — `ImageSharpBackend.cs` /
  `PaintBackend.cs` deleted, `Painter.SelectBackend()` removed, `NativeLoader`
  throws an actionable `DllNotFoundException` when the shim is absent, and
  `Tessera.Skia.csproj` has a `BeforeTargets="Build"` guard that fails the build
  early with a build-it-with-`./native/build-skia.sh` message. The `ci.yml` step
  now restores the shim from the latest successful `native.yml` run via
  `gh run download`. Verified: full suite green WITH the dylib (0 skips); build
  fails with a clear error WITHOUT it. Honest consequence: until native.yml
  builds win-x64/linux-x64, the ubuntu/windows CI legs fail at Build — that red
  is real missing platform support, intentionally not papered over.
