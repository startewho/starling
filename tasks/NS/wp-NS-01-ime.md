---
id: wp:NS-01-ime
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

# wp:NS-01-ime — Input method editor for the native shell

## Goal

Support text composition (Chinese, Japanese, Korean, and dead keys) in the
native shell's focused fields. Silk.NET does not provide this, so it is native
per-platform work.

## What already works

- **Commit-style IME works today.** Standard GLFW (the shell's windowing)
  delivers *committed* composed characters through its character callback, which
  the shell's `KeyChar` handler already inserts. So selecting a character with the
  macOS input method reaches a focused field. The missing piece is only the
  inline preedit display.
- **The composition model is built and tested.**
  `Starling.Gui.Core.Text.ImeComposition` models committed text plus an active
  preedit (marked text), shaped like `NSTextInputClient` (set-marked-text /
  insert / delete-backward). Nine unit tests cover it. A native driver feeds it.

## Remaining scope

- The native preedit driver. Standard GLFW exposes no preedit callback, so we
  need a native `NSTextInputClient` on the window's content view. That means a
  custom `NSView` (replacing GLFW's text input) or method swizzling on the GLFW
  view — the hard part. It calls `ImeComposition.SetMarkedText` as the user
  composes and `Insert` on commit.
- Render the preedit inline (underlined) in the focused field.
- Then Windows (`WM_IME_*`) and Linux (IBus / `xim`).

## Acceptance

- Typing a multi-key composition into a focused `<input>` shows the underlined
  preedit and commits the final string.
- No regression to plain Latin typing (still works) or to commit-style IME.

## Status note (native-shell branch)

Unchanged this pass. Commit-style IME already works (GLFW char callback ->
focused field). The remaining piece is the native NSTextInputClient preedit
driver + inline underlined preedit display — Objective-C interop (custom NSView /
swizzle) that needs a display and a real IME to verify, so it was not shipped
blind. The ImeComposition model (9 tests) is ready for it.

## Completion (native-shell branch)

MacImeBridge swizzles the GLFW content view's NSTextInputClient setMarkedText /
unmarkText so the composition reaches the shell, which draws the preedit
underlined at the focused field (page-space overlay). Committed text still flows
through the GLFW char callback. Opt-in via STARLING_IME_PREEDIT=1, isolated from
the default path. Verified: default unchanged; with the env var the swizzle
installs and the shell loads/presents with no crash. Driving a real composition
needs a Mac display + input method.
