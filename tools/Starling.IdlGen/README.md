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

# Report member coverage over the target interfaces, by cause.
dotnet run --project tools/Starling.IdlGen -- coverage
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
     mapping would be wrong. They keep their hand-written bindings.
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

`NodeBindings.Install` calls the generated installers after the hand-written
ones, so the generated members overwrite the mechanical hand-written members.
The binding tests and the Web Platform Tests hold behavioral equivalence.

## Refreshing the IDL

The IDL lives in `testdata/webref/idl/`, a pinned snapshot of `w3c/webref`. See
`testdata/webref/README.md` to refresh it. `dom.idl` carries the core DOM
surface the generator targets.

## Adding an override

When a generated member behaves differently from the hand-written one, a binding
test or a Web Platform Test fails. Two fixes, both in `overrides/overrides.json`:

- Add the member under `skip` with a reason. It keeps its hand-written binding.
- Add it under `override` with a custom `getter` (and optional `setter`). The
  emitter generates the member using that code instead of the mechanical
  mapping. This is how `tagName` and `nodeName` get their HTML uppercasing.

The node factory methods (`createElement` and friends) are on `skip` because
they need custom prototype selection.
