# Starling.IdlGen — Web IDL to Starling.Bindings generator

Generates JS binding glue for Starling DOM from Web IDL. It reads the curated
IDL snapshot, builds a model, maps each member to a Starling DOM CLR member, and
writes accessor and method installers into `Starling.Bindings`.

The generated code installs JS prototypes on a realm and marshals values to and
from the C# DOM. So this is closer to the Chromium and Gecko binding generators,
which emit real runtime glue, than to the TypeScript DOM lib generator, which
only emits type declarations.

## Commands

Run from the repo root.

```bash
# Parse every vendored IDL file and print a summary. A parser smoke test.
dotnet run --project tools/Starling.IdlGen -- parse

# Build the merged model and report counts and unresolved cross-spec includes.
dotnet run --project tools/Starling.IdlGen -- model

# Generate the binding glue and the supporting types into
# src/Starling.Bindings/Generated/.
dotnet run --project tools/Starling.IdlGen -- emit

# Generate only the backend-neutral surface manifest.
dotnet run --project tools/Starling.IdlGen -- manifest

# Report member coverage over the target interfaces, by cause.
dotnet run --project tools/Starling.IdlGen -- coverage

# Same report, plus the full list of gap members. Use it to pick what to add next.
dotnet run --project tools/Starling.IdlGen -- coverage --notes
```

## What it generates

Into `src/Starling.Bindings/Generated/`:

| File | Contents |
|---|---|
| `CoreDomBindings.g.cs` | Prototype installers: accessors, setters, methods, constants |
| `Unions.g.cs` | .NET 11 `union` types for IDL union types |
| `Dictionaries.g.cs` | C# classes for IDL dictionaries |
| `Enums.g.cs` | C# enums plus wire-string maps for IDL enums |
| `Callbacks.g.cs` | C# delegates for IDL callback functions |

The backend-neutral surface manifest is written to
`testdata/webref/core-dom-surface.json`. It lists the IDL members, arity,
required argument count, nullable state, and descriptor shape. Rows marked
`required` are checked against both JS engines.

The emitters cross-reference: generating dictionaries unlocks unions over
dictionaries, generating callbacks unlocks unions over callbacks. A type that is
not generated yet falls back to `JsValue` so the output always compiles.

After `emit`, build `Starling.Bindings` and run the tests:

```bash
dotnet build src/Starling.Bindings/Starling.Bindings.csproj
dotnet test tests/Starling.IdlGen.Tests
dotnet test tests/Starling.Bindings.Tests
```

## How it works

1. **Parse** — `Parsing/` turns each `.idl` file into an abstract syntax tree.
2. **Merge** — `Merging/` folds partials together, applies `includes` to copy
   mixin members, and resolves typedefs.
3. **Map** — `Mapping/TypeMapper` maps IDL types to C# types. `Mapping/ClrMap`
   reflects over the real `Starling.Dom` assembly, so the emitter only generates
   a binding when the CLR member actually exists with a mappable type.
4. **Override** — `Overrides/` reads `overrides/overrides.json`. Three layers:
   - `skip` names members the emitter must not generate because the mechanical
     mapping would be wrong. They keep the Starling binding.
   - `override` supplies a custom getter or setter the emitter uses instead of
     the mechanical mapping. For example, `tagName` and `nodeName` get a getter
     that applies HTML uppercasing.
   - `add` injects verbatim binding code for members the IDL does not describe
     (host extras), keyed by interface.

   Each entry says why. (A fourth layer, `patch`, for rewriting parsed members
   before emit, is not implemented — `override` and `add` cover its uses here.)
5. **Emit** — `Emit/BindingsEmitter` writes the installer methods.

## The generated file

`src/Starling.Bindings/Generated/CoreDomBindings.g.cs` is committed. It is the
golden baseline. `tests/Starling.IdlGen.Tests` re-runs the generator and fails
if the output drifts. Regenerate with `emit` and review the diff before you
commit.

`NodeBindings.Install` calls the generated installers after the Starling
bindings, so the generated members overwrite the mechanical Starling members.
The binding tests and the Web Platform Tests hold behavioral equivalence.

## Coverage gates

`coverage-gates.json` sets a minimum member-coverage percent for each target
interface in `BindingsEmitter.CoreDomInterfaces`. The gates ratchet coverage: if
an interface drops below its floor, or a new target has no gate, both the
`coverage` command (non-zero exit) and `CoverageGateTests` fail. After a coverage
gain, raise the floor to lock it in. Read the current numbers with:

```bash
dotnet run --project tools/Starling.IdlGen -- coverage
```

An interface whose members are all inherited (for example `Comment`) reads as
100% — it has nothing of its own to bind.

## Refreshing the IDL

The IDL lives in `testdata/webref/idl/`, a pinned snapshot of `w3c/webref`. See
`testdata/webref/README.md` to refresh it. `dom.idl` carries the core DOM
surface the generator targets.

## Adding an override

When a generated member behaves differently from the Starling binding, a binding
test or a Web Platform Test fails. Two fixes, both in `overrides/overrides.json`:

- Add the member under `skip` with a reason. It keeps the Starling binding.
- Add it under `override` with a custom `getter` (and optional `setter`). The
  emitter generates the member using that code instead of the mechanical
  mapping. This is how `tagName` and `nodeName` get their HTML uppercasing.

The node factory methods (`createElement` and friends) are on `skip` because
they need custom prototype selection.

## Todo

Parallel markers:

- `parallel-root` means the root item can run beside other root items once its
  normal code dependencies are met.
- `parallel-subtask` means the item can be split from sibling tasks under the
  same parent. On a parent row, it means at least some child rows can split.

- [x] Generate a backend-neutral IDL surface manifest and use it to check the
  Starling JS engine binding surface.
- [x] Make the manifest the source of truth for backend parity.
- [x] Route every generated operation through `IdlMarshal`.
- [x] Fail when a generated installer is not wired into the runtime.
- [x] Add negative Web IDL conversion tests for generated members.
- [ ] Teach the type mapper nullable strings, sequences, records, callbacks,
  dictionaries, unions, overloads, variadics, enums, and `[SameObject]`.
  (`parallel-root`, `parallel-subtask`)
  - [x] Parse and map nullable strings. (`parallel-subtask`) The type mapper maps
    `DOMString?` to `string?`. `ClrMap.FindScalarMethod` now reads per-argument
    IDL nullability so a nullable string argument backed by a CLR `string`
    parameter marshals through `IdlMarshal.RequireNullableString` (a JS null
    becomes a C# null, the argument stays required) instead of `RequireString`.
    This fixes the generated `Node.lookupPrefix`, `lookupNamespaceURI`, and
    `isDefaultNamespace`. Nullable string returns already work through
    `WrapString`.
  - [x] Parse and map `sequence<T>` and `record<K, V>`. (`parallel-subtask`) The
    type mapper maps these (see `TypeMapperTests`). No target interface has a CLR
    member of these shapes that the mechanical path can bind yet, so this is
    mapping-ready with no current binding yield. The variadic `(Node or
    DOMString)...` sequence is handled by the dispatch layer.
  - [ ] Generate dictionary and callback arguments. (`parallel-subtask`)
    Mapping-ready (the type mapper classifies them and the dictionary/callback
    emitters emit the C# types), but no target interface exposes a CLR member that
    takes a dictionary or callback yet, so there is no binding yield.
  - [ ] Generate union inputs and returns. (`parallel-subtask`) Mapping-ready. The
    type mapper produces the generated union name. The only union argument a
    target interface needs today is the `(Node or DOMString)...` variadic, which
    the dispatch layer already converts.
  - [ ] Support enum conversion and validation. (`parallel-subtask`)
    Mapping-ready (the type mapper classifies IDL enums and the enum emitter emits
    the wire-string maps), but no target interface has an enum-typed CLR member
    backing an IDL enum attribute, so there is no binding yield.
  - [ ] Support overloads and variadic arguments. (`parallel-subtask`) The
    required-argument count and JS `.length` for mechanical methods now exclude
    trailing optional and variadic arguments (threaded from the IDL operation),
    matching the dispatch layer. Variadic arguments stay deferred to the dispatch
    layer. Overload resolution is still open.
  - [ ] Honor `[SameObject]` for cached wrapper identity. (`parallel-subtask`)
    Untouched. `[SameObject]` getters such as `Element.attributes` and
    `Element.classList` already cache their wrapper through the override layer.
- [x] Move more DOM algorithms into `Starling.Dom`, then emit thin dispatch
  bindings for them. (`parallel-subtask`)
  - [x] Move CharacterData mutators: `length`, `substringData`, `appendData`,
    `insertData`, `deleteData`, and `replaceData`.
  - [x] Move ParentNode and ChildNode traversal: `firstElementChild`,
    `lastElementChild`, `childElementCount`, `nextElementSibling`, and
    `previousElementSibling`.
  - [x] Move `parentElement`, `isConnected`, `hasChildNodes`, `nodeType`,
    `Text.wholeText`, `Text.splitText`, `Element.className`,
    `Element.namespaceURI`, `Document.doctype`, and
    `DocumentFragment.getElementById`.
  - [ ] Move layout and CSSOM View members such as `clientHeight`, `scrollTop`,
    and `getBoundingClientRect`. (`parallel-subtask`)
  - [ ] Move stylesheet, animation, view-transition, shadow DOM, XPath, Range,
    and tree-walker factory members. (`parallel-subtask`)
- [x] Expand the target interface set with per-interface coverage gates. Added
  `Attr` to the target set (the `AttrNode` .NET type, via an IDL-to-.NET name
  map). `coverage-gates.json` holds a minimum coverage percent per target
  interface. The `coverage` command and `CoverageGateTests` fail when an
  interface drops below its floor or a target has no gate. (`Event` and
  `CustomEvent` were tried but reverted: the event object uses a bespoke wrapper
  the generic marshalling cannot unwrap.)
  - [x] Add `Attr`.
  - [x] Add `coverage-gates.json`.
  - [x] Fail when a target interface has no gate.
  - [x] Fail when an interface drops below its floor.
  - [ ] Revisit `Event` and `CustomEvent` after the event wrapper can be
    unwrapped by generated bindings. (`parallel-subtask`)
- [x] Add runtime IDL harness-style tests over the manifest. (`parallel-root`,
  `parallel-subtask`) Done in
  `tests/Starling.BindingSurface.Tests/IdlRuntimeHarnessTests.cs`. Drives the
  Starling JS engine with the Starling DOM bindings.
  - [x] Load the surface manifest as the test source. Reads
    `testdata/webref/core-dom-surface.json`.
  - [x] Create fixture objects for each interface. (`parallel-subtask`) A JS
    fixture expression per interface, for example `document.createElement('div')`
    for Element and `document.createTextNode('x')` for Text.
  - [x] Check descriptors, constants, methods, and attributes.
    (`parallel-subtask`) Accessor versus method versus data shape, method
    `.length`, and Node constants on both the constructor and the prototype.
  - [x] Check inheritance and prototype chains. (`parallel-subtask`) Walks each
    instance prototype chain and matches it to the IDL inheritance, ending at
    Object.
  - [x] Track expected failures in the manifest or a sidecar file.
    (`parallel-subtask`) Sidecar `testdata/webref/surface-expected-failures.json`.
    The list is empty today because the runtime matches the manifest, and a new
    gap or a stale entry fails the test.
- [ ] Add baseline drift tests for every generated file. (`parallel-root`,
  `parallel-subtask`)
  - [ ] `CoreDomBindings.g.cs`. (`parallel-subtask`)
  - [ ] `Unions.g.cs`. (`parallel-subtask`)
  - [ ] `Dictionaries.g.cs`. (`parallel-subtask`)
  - [ ] `Enums.g.cs`. (`parallel-subtask`)
  - [ ] `Callbacks.g.cs`. (`parallel-subtask`)
  - [ ] `core-dom-surface.json`. (`parallel-subtask`)
- [ ] Add Starling JS engine prototype conformance tests against the manifest.
  (`parallel-root`, `parallel-subtask`)
  - [ ] Check constructors and prototype objects. (`parallel-subtask`)
  - [ ] Check `constructor` links. (`parallel-subtask`)
  - [ ] Check method and attribute descriptors. (`parallel-subtask`)
  - [ ] Compare constants on constructors and prototypes. (`parallel-subtask`)
  - [ ] Compare `instanceof` results. (`parallel-subtask`)
- [ ] Require each `skip` entry to have a reason and a test for the matching
  Starling binding. (`parallel-root`)
  - [ ] Require a reason.
  - [ ] Require a category.
  - [ ] Require a matching Starling binding test.
  - [ ] Require a work item or removal condition.

### Full coverage gaps

- [ ] Define an extended-attributes policy. Handle or reject `[Exposed]`,
  `[LegacyUnforgeable]`, `[NewObject]`, `[PutForwards]`, `[Reflect]`,
  `[CEReactions]`, `[HTMLConstructor]`, `[SecureContext]`, and other attributes
  used by target interfaces. (`parallel-root`, `parallel-subtask`)
  - [ ] Exposure rules: `[Exposed]` and `[SecureContext]`. (`parallel-subtask`)
  - [ ] Constructor rules: `[HTMLConstructor]`. (`parallel-subtask`)
  - [ ] Reflection rules: `[Reflect]` and `[PutForwards]`. (`parallel-subtask`)
  - [ ] Object identity rules: `[SameObject]` and `[NewObject]`.
    (`parallel-subtask`)
  - [ ] Custom element reactions: `[CEReactions]`. (`parallel-subtask`)
  - [ ] Property placement rules: `[LegacyUnforgeable]`. (`parallel-subtask`)
- [ ] Generate and validate interface objects and constructors: globals,
  `prototype`, `constructor`, constants, illegal constructors, and global
  exposure rules. (`parallel-root`, `parallel-subtask`)
  - [ ] Generate globals. (`parallel-subtask`)
  - [ ] Generate `prototype` and `constructor` links. (`parallel-subtask`)
  - [ ] Put constants on constructors and prototypes. (`parallel-subtask`)
  - [ ] Generate illegal constructor behavior. (`parallel-subtask`)
  - [ ] Check `instanceof`. (`parallel-subtask`)
  - [ ] Apply global exposure rules. (`parallel-subtask`)
- [ ] Validate IDL inheritance and prototype chains for every target interface.
  (`parallel-root`)
- [ ] Implement Web IDL overload resolution, including nullable values,
  dictionaries, callbacks, primitives, and platform objects. (`parallel-root`,
  `parallel-subtask`)
  - [ ] Dispatch by argument count. (`parallel-subtask`)
  - [ ] Dispatch by nullable values. (`parallel-subtask`)
  - [ ] Dispatch by dictionary and callback inputs. (`parallel-subtask`)
  - [ ] Dispatch by primitive and platform-object inputs. (`parallel-subtask`)
  - [ ] Add tie-break tests. (`parallel-subtask`)
- [ ] Implement optional arguments and default values. (`parallel-root`,
  `parallel-subtask`)
  - [ ] Optional primitive args. (`parallel-subtask`)
  - [ ] Optional dictionaries. (`parallel-subtask`)
  - [ ] Default values. (`parallel-subtask`)
  - [ ] Method `length` after optional args. (`parallel-subtask`)
- [ ] Expand negative conversion tests into a table-driven matrix for missing
  args, `undefined`, `null`, symbols, BigInts, objects, arrays, functions, wrong
  receivers, and cross-realm wrappers. (`parallel-root`, `parallel-subtask`)
  - [ ] Missing args, `undefined`, and `null`. (`parallel-subtask`)
  - [ ] Symbols and BigInts. (`parallel-subtask`)
  - [ ] Objects, arrays, and functions. (`parallel-subtask`)
  - [ ] Wrong receivers. (`parallel-subtask`)
  - [ ] Cross-realm wrappers. (`parallel-subtask`)
- [ ] Make unsupported IDL features fail for target interfaces unless a skip
  entry explains the gap. (`parallel-root`)
- [ ] Add a skip category for each skipped member, such as custom prototype,
  missing DOM algorithm, missing type mapper support, or backend-specific.
  (`parallel-root`)

### Mission-critical gates

- [ ] Run or mirror WPT `idlharness.js` checks for generated interfaces.
  (`parallel-root`, `parallel-subtask`)
  - [ ] Load the same IDL snapshot.
  - [ ] Build JS fixtures for each interface. (`parallel-subtask`)
  - [ ] Track expected failures. (`parallel-subtask`)
  - [ ] Run checks for Starling JS. (`parallel-subtask`)
- [ ] Require every target IDL member to be classified as generated, covered by a
  Starling binding, blocked by type support, blocked by a missing DOM algorithm,
  or out of scope. (`parallel-root`, `parallel-subtask`)
  - [ ] `generated`.
  - [ ] `starling-binding`.
  - [ ] `blocked-type-support`.
  - [ ] `blocked-dom-algorithm`.
  - [ ] `out-of-scope`.
- [ ] Add hard per-interface coverage gates and ratchet them upward.
  (`parallel-root`)
- [ ] Add differential oracle tests that run selected snippets in Starling JS
  and a real browser. (`parallel-root`, `parallel-subtask`)
  - [ ] Choose representative snippets per interface. (`parallel-subtask`)
  - [ ] Run Starling JS. (`parallel-subtask`)
  - [ ] Run a browser oracle. (`parallel-subtask`)
  - [ ] Store expected differences. (`parallel-subtask`)
- [ ] Add a generated install registry and compare it to the manifest at runtime
  or test time. (`parallel-root`, `parallel-subtask`)
  - [ ] Record each generated installer call. (`parallel-subtask`)
  - [ ] Record each installed member. (`parallel-subtask`)
  - [ ] Compare installed members to required manifest rows. (`parallel-subtask`)
- [ ] Add cross-realm tests for wrappers, constructors, prototypes, and object
  identity. (`parallel-root`, `parallel-subtask`)
  - [ ] Cross-realm wrappers. (`parallel-subtask`)
  - [ ] Cross-realm constructors and prototypes. (`parallel-subtask`)
  - [ ] Cross-realm `instanceof`. (`parallel-subtask`)
  - [ ] Cross-realm object identity. (`parallel-subtask`)
- [ ] Add performance and allocation checks for hot generated bindings such as
  `getAttribute`, `setAttribute`, `querySelector`, `appendChild`, and property
  reads. (`parallel-root`, `parallel-subtask`)
  - [ ] `getAttribute` and `setAttribute`. (`parallel-subtask`)
  - [ ] `querySelector` and `querySelectorAll`. (`parallel-subtask`)
  - [ ] `appendChild`, `insertBefore`, and `removeChild`. (`parallel-subtask`)
  - [ ] Common attribute reads. (`parallel-subtask`)
- [ ] Add a CI regeneration gate that runs `dotnet run --project
  tools/Starling.IdlGen -- emit` and fails if generated files or manifests drift.
  (`parallel-root`)
