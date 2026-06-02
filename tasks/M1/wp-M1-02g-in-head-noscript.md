---
id: "wp:M1-02g-in-head-noscript"
parent: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on:
  - "wp:M1-02b-tree-construction-html5lib"
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
---

# wp:M1-02g — InHeadNoscript insertion mode (§13.2.6.4.5)

## Goal

Model the "in head noscript" insertion mode for the scripting-disabled path
(the html5lib conformance default). `wp:M1-noscript-scripting-flag` shipped
the scripting-enabled side (RAWTEXT inert text); the scripting-disabled side
still falls through `HandleInHead`'s "anything else" path. `wp:M1-02b` shows
the cost — `noscript01.dat` is 0/18.

## Inputs

- §13.2.6.4.5 (spec).
- `HandleInHead` in `HtmlTreeBuilder.cs` (note the `noscript`/`_scriptingEnabled`
  case at line ~471 — the disabled branch currently isn't wired).
- `wp:M1-02b` runner.

## Outputs

- `InsertionMode.InHeadNoscript` (new enum value).
- `HandleInHeadNoscript` per the spec's case list (HTML start tags allowed:
  `basefont`, `bgsound`, `link`, `meta`, `noframes`, `style`; comments
  permitted; HTML, `noscript` start tag and `head`/everything-else is a parse
  error, pop back to InHead).
- `HandleInHead` updated to switch into the new mode for `<noscript>` when
  scripting is disabled (today it falls through to the end of the switch).

## Acceptance

- `noscript01.dat` ≥ 95% in `wp:M1-02b`.
- The existing
  `Noscript_in_head_with_scripting_disabled_parses_contents_as_elements` test
  in `TreeBuilderTests` still passes.
- Overall floor ratcheted.

## Notes

- The scripting-enabled path is unchanged — it stays on RAWTEXT
  (`wp:M1-noscript-scripting-flag`). This WP only adds the disabled side.
- Don't forget: `Document.InsertionMode = InHead` when the mode pops back
  after `</noscript>`.
