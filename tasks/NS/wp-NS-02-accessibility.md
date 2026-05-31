---
id: wp:NS-02-accessibility
milestone: NS
status: "in_progress"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "native-shell"
depends_on: []
blocks: []
subsystem: Starling.Shell.Native
plan_refs:
  - docs/native-shell.md
---

# wp:NS-02-accessibility — Accessibility tree for the native shell

## Goal

Expose the page to the platform accessibility layer so screen readers and other
assistive tools can use the native shell. The Avalonia shell gets this for free.
This is the largest and hardest native-services item — decide early whether it is
required for the native shell to replace Avalonia.

## Scope

- Map the Starling DOM / box tree to a platform accessibility tree: roles, names,
  values, focus, and bounds.
- macOS `NSAccessibility` first, then Windows UI Automation, then Linux AT-SPI.
- Keep the tree in sync as the DOM mutates and as focus moves.

## Acceptance

- VoiceOver (macOS) reads headings, links, and form fields on a loaded page and
  follows focus.

## Notes

Big, per-platform, and easy to underestimate. May gate the decision to retire
the Avalonia shell. Consider scoping a minimal read-only tree first.

## Status note (native-shell branch)

Tree builder expanded + unit-tested (20 green): Article/Complementary/Region/
Form/Search/ComboBox roles (section/form named-landmark rule), aria-labelledby
(joined, precedence over aria-label), title fallbacks, <select> value. The
native shell already pushes the tree each frame (PushA11y). Remaining: VoiceOver
tuning of the macOS bridge frame conversion (needs a display + VoiceOver).
