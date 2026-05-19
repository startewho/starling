# Strictly-Ready Stub Inventory

Snapshot of `tests/Starling.Css.Spec.Tests/` `[PendingFact]` stubs that have
been promoted to `[SpecFact]` with strict typed assertions, plus those that
still cannot be strictly promoted.

Strict bar:

> A stub may be promoted if the legacy `tests/Starling.Css.Tests/` suite
> (tagged `[Spec("<id>", "<url>")]`) contains a test that exercises the
> same property / selector / at-rule **with a concrete value** and asserts
> the resulting typed `CssValue` / `Selector` / `AtRule`. Promotion means
> writing the same typed assertion in the spec-tests stub.
>
> A stub may **not** be promoted on the basis of "the parser accepts it
> without throwing." That proves CSS Syntax 3 tokenization, not the spec
> the stub belongs to.

Last regenerated: 2026-05-19.

## Suite scoreboard

`dotnet test tests/Starling.Css.Spec.Tests` →
**53 passed · 0 failed · 1132 skipped** (1185 total stubs).

## Promoted to `[SpecFact]` — 53

### Properties (30)

| Spec | Stub file | Properties |
|---|---|---|
| css-color-4      | `CssColor/PropertyTests.cs`      | `color`, `opacity` (2) |
| css-fonts-4      | `CssFonts/PropertyTests.cs`      | `font-family`, `font-style`, `font-weight` (3) |
| css-cascade-5    | `CssCascade/PropertyTests.cs`    | `all` (1) |
| css-animations-1 | `CssAnimations/PropertyTests.cs` | `animation`, `animation-duration`, `animation-name`, `animation-timing-function` (4) |
| css-sizing-4     | `CssSizing4/PropertyTests.cs`    | `aspect-ratio` (1) |
| css-flexbox-1    | `CssFlexbox/PropertyTests.cs`    | `flex`, `flex-direction`, `flex-flow` (3) |
| css-grid-2       | `CssGrid/PropertyTests.cs`       | `grid-area`, `grid-auto-flow`, `grid-column`, `grid-row`, `grid-template-columns` (5) |
| css-logical-1    | `CssLogical/PropertyTests.cs`    | `block-size`, `border-inline`, `border-inline-start`, `border-start-start-radius`, `inline-size`, `inset`, `inset-inline`, `margin-block`, `margin-inline`, `margin-inline-start`, `padding-inline` (11) |

### Selectors (16)

| Spec | Stub file | Selectors |
|---|---|---|
| selectors-4 | `Selectors/SelectorTests.cs` | `:any-link`, `:defined`, `:dir`, `:first-child`, `:has`, `:is`, `:lang`, `:link`, `:not`, `:nth-child`, `:only-child`, `:optional`, `:placeholder-shown`, `:required`, `:visited`, `:where` (16) |

### At-rules (7)

| Spec | Stub file | At-rules |
|---|---|---|
| css-animations-1   | `CssAnimations/AtRuleTests.cs`   | `@keyframes` (1) |
| css-fonts-4        | `CssFonts/AtRuleTests.cs`        | `@font-face` (1) |
| css-cascade-5      | `CssCascade/AtRuleTests.cs`      | `@import`, `@layer` (2) |
| mediaqueries-5     | `Mediaqueries/AtRuleTests.cs`    | `@media` (1) |
| css-conditional-3  | `CssConditional/AtRuleTests.cs`  | `@media`, `@supports` (2) |

## Pending — 1132

Everything else. These stubs describe properties / selectors / at-rules whose
*existence* the engine handles (parser tokenizes them) but whose *spec
semantics* aren't yet covered by a legacy test. Without legacy evidence to
mirror, writing a strict assertion would require new engine work — typically:

- A typed value parser path (e.g. for `<track-list>` syntax beyond the
  shorthands already promoted).
- Cascade/style-engine wiring (e.g. logical-property → physical-property
  mapping varying by writing-mode, requiring a fixture element).
- Selector matching against a fixture DOM (for selectors with no legacy
  `SelectorMatcherTests` case).
- For pseudos like `:hover`, `:focus`, `:active`, `:valid`, `:invalid`,
  `:fullscreen`, `:modal`, etc. — the legacy `ModernPseudoClassTests`
  Theory only asserts `parses without throwing`, which by definition is
  **not** strict, so they remain `[PendingFact]`.

These promotions belong in the per-spec implementation work packages
(`wp:spec-<id>`), not in this inventory.

## Promotion recipe

1. Open the legacy test that proves the feature (e.g.
   `tests/Starling.Css.Tests/LogicalPropertyTests.cs` for `margin-inline-start`).
2. Note the typed assertion — usually `decl.Value.Should().Be(new CssLength(...))`
   or `.Should().BeEquivalentTo(new {...})` for records with extra fields
   (e.g. `CssColor` has hidden wide-gamut fields).
3. In the corresponding stub file
   (`tests/Starling.Css.Spec.Tests/<Folder>/PropertyTests.cs`), replace:

   ```csharp
   [PendingFact("property '<name>' not asserted yet", trackingWp: "...")]
   public void Parses_<name>() => throw new NotImplementedException();
   ```

   with the same typed assertion, tagged `[SpecFact]`. Templates:

   - **Property strict** — `tests/Starling.Css.Spec.Tests/CssLogical/PropertyTests.cs`
   - **Selector strict** — `tests/Starling.Css.Spec.Tests/Selectors/SelectorTests.cs`
   - **At-rule strict** — `tests/Starling.Css.Spec.Tests/CssAnimations/AtRuleTests.cs`

4. Run `dotnet test tests/Starling.Css.Spec.Tests --filter "Spec=<spec-id>"`.
5. Re-generation is safe — the stub generator never overwrites existing files.

## Regenerating the strict candidate set

```bash
cd /Users/cody/code/tessera
python3 - <<'PY'
import re, pathlib
LEGACY = pathlib.Path("tests/Starling.Css.Tests")
SPEC = pathlib.Path("tests/Starling.Css.Spec.Tests")
spec_tag_re = re.compile(r'\[Spec\("([^"]+)"')
decl_re = re.compile(r'([a-z][a-z0-9\-]+?)\s*:\s*[^"\';\n}]+')
proved = {}
for f in sorted(LEGACY.glob("*.cs")):
    src = f.read_text()
    m = spec_tag_re.search(src)
    if not m: continue
    bag = set()
    for a,b in re.findall(r'"([^"]*)"|"""(.*?)"""', src, re.DOTALL):
        for s in (a,b):
            bag |= {p for p in decl_re.findall(s) if len(p)>=3}
    proved.setdefault(m.group(1), set()).update(bag)
LEGACY_TO_WEBREF = {
    "css-fonts-4":"CssFonts","css-cascade-5":"CssCascade","css-animations-1":"CssAnimations",
    "css-sizing-4":"CssSizing4","css-flexbox-1":"CssFlexbox","css-grid-2":"CssGrid",
    "css-logical-1":"CssLogical","css-color-4":"CssColor","css-color-5":"CssColor5",
    "css-transforms-2":"CssTransforms2","css-images-4":"CssImages4","css-values-5":"CssValues5",
}
for sid, folder in LEGACY_TO_WEBREF.items():
    fp = SPEC/folder/"PropertyTests.cs"
    if not fp.exists(): continue
    stubs = set(re.findall(r"property '([a-z\-]+)' not asserted yet", fp.read_text()))
    overlap = sorted(stubs & proved.get(sid,set()))
    if overlap:
        print(f"{sid:20} {folder:18} {len(overlap):>3}: {', '.join(overlap)}")
PY
```
