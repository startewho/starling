---
id: wp:NS-01-ime
milestone: NS
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
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

## Scope

- A composition state on the focused field: a preedit string with an underline,
  then commit on accept.
- macOS first: `NSTextInputClient` on the content view, or GLFW preedit hooks if
  they suffice. Then Windows (`WM_IME_*`) and Linux (IBus/`xim`).
- Feed committed text through the same path the `KeyChar` handler uses today
  (`HtmlFormControls.SetValue` + an `input` event + relayout).

## Acceptance

- Typing a multi-key composition into a focused `<input>` shows the preedit and
  commits the final string.
- No regression to plain Latin typing.
