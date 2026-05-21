# wp:M3-27 — rest parameters in object-literal method shorthands

| Field     | Value |
|-----------|-------|
| ID        | M3-27 |
| Status    | complete |
| Owner     | agent-claude-sonnet-4-6 |
| Subsystem | Starling.Js |
| Spec ref  | ECMA-262 §15.4 Method Definitions |

## Problem

Object-literal method shorthands — `{ m(...n){} }` and computed-key methods
`{ [k](...n){} }` — threw a parse error:

```
expected binding name or pattern (got Ellipsis '...')
```

The mcmaster.com app bundle uses `{[t.name](...n){return t(...n)}}` in a
class-body initializer and failed at `(...n)`, blocking all script execution.

## Root cause

`ParseMethodTail()` in `JsParser.cs` contained an inline parameter loop that
called only `ParseParameter()` in a `while` loop; it did not check for a
leading `Ellipsis` (`...`) token.  The rest-aware `ParseParameterList()`
(added in wp:M3-25 for arrow functions and present in function declarations)
was not called from `ParseMethodTail`.

Class methods were **not** affected — they already used `ParseParameterList()`
via a separate call in `ParseClassMember` (`JsParser.Classes.cs:201`).

## Fix

`ParseMethodTail()` now delegates its parameter-list parsing to
`ParseParameterList()` (same method used by function declarations and class
methods). The `ParseParameterList()` handles:

- plain params: `m(a, b)`
- rest-only: `m(...n)`
- fixed + rest: `m(a, ...b)`
- early break after rest (rest must be last)

Getter/setter well-formedness checks (`getter: 0 params`, `setter: 1 param`)
remain in place after the call and continue to work correctly, including
rejection of rest in a getter.

## Files changed

| File | Change |
|------|--------|
| `src/Starling.Js/Parse/JsParser.cs` | `ParseMethodTail`: replace inline loop with `ParseParameterList()` call |
| `tests/Starling.Js.Tests/Runtime/JsMethodRestParamTests.cs` | 14 new tests |

## Tests

New: `JsMethodRestParamTests` (14 tests):

- `Named_method_sole_rest_collects_all_args` — `{m(...n){return n.length}}.m(1,2,3)` → 3
- `Named_method_sole_rest_empty_call` — `{m(...n){return n.length}}.m()` → 0
- `Named_method_fixed_plus_rest` — `{f(a,...b){return b.length}}.f(1,2,3,4)` → 3
- `Named_method_fixed_plus_rest_values` — correct a and b bindings
- `Computed_key_method_sole_rest` — `{[k](...n){return n[0]}}.m(42)` → 42
- `Computed_key_method_rest_collects_length` — computed-key + rest length
- `Mcmaster_bundle_shape_computed_key_rest_forward` — bundle pattern using spread forward
- `Plain_method_no_params_unaffected` — regression guard
- `Plain_method_two_params_unaffected` — regression guard
- `Getter_no_params_still_works` — regression guard
- `Setter_one_param_still_works` — regression guard
- `Getter_with_rest_param_throws_parse_error` — §15.4.1 getter rule still enforced
- `Setter_with_rest_param_throws_parse_error` — setter normal path still valid
- `Class_method_rest_param_still_works` — class methods unaffected by change

## Render progress

After this fix the mcmaster app bundle advances past the `Ellipsis` error.
The next positioned error in `engine.js` is:

```
expected class member name, got Star (at 162:7595)
```

Source at that position (mcmaster bundle `mcm_cc73c91b…`):

```
...a}*[Symbol.iterator](){for(const[t,n]of this.containers.entries())for(const a of n)yield t<<16|a}_orContainers...
```

This is a computed generator method (`*[Symbol.iterator](){}`) in a class
body. The `ParseClassMember` function doesn't handle a `*` prefix for
generator methods — next WP.
