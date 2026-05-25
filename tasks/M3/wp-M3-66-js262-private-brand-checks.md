---
id: "wp:M3-66-js262-private-brand-checks"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main 3542295 (also js262-phase0-2; pushed origin/js-262)"
finding: "brand check used Has()/FindPrivateDescriptor which WALK the prototype chain — conflated reachability with brand ownership. Added per-object _privateBrands set ([[PrivateElements]]); checks consult it directly. Brands installed in RunFieldInits (after super) + on ctor for statics. 13 tests."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-66 — JS: private-member brand checks + access-before-init (TypeError)

## Why (evidence)
~73 `Expected a TypeError to be thrown but no exception` in class `elements`
are **private**-member cases (the largest class-TypeError sub-cluster); plus a
separate 45 `threw TypeError: Cannot read private member …` where the
error/behavior differs. Representative:
`private-fields-proxy-default-handler-throws.js`,
`prod-private-{getter,setter,method}-before-super-return-in-constructor.js`,
`static-private-method-subclass-receiver.js`.

## Scope
- Enforce §ClassElementName brand checks: reading/writing/calling a private
  field/method/accessor (`obj.#x`, `#x in obj`) on an object that is NOT an
  instance carrying that private brand throws **TypeError**. Static private
  members: only the constructor itself carries the brand (subclass/other
  receivers → TypeError).
- Private access **before initialization** (before `super()` returns in a
  derived constructor, or in a field initializer that runs before the brand is
  installed) throws TypeError.
- Proxy: a private access never traps through a Proxy — it throws TypeError
  (the proxy isn't a brand carrier).
- Read the failing tests under
  `/Users/cody/code/starling/testdata/test262/test/language/.../class/elements/`
  to pin exact semantics; align the existing "Cannot read private member"
  throws to the spec error type so those tests pass too.

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=private` (+ class elements) failures
  drop; report before/after.
- Focused unit tests: brand-check TypeError for wrong receiver (instance +
  static), access-before-super TypeError, proxy private access TypeError; valid
  private access still works.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
