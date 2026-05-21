---
id: css-content-3
url: https://www.w3.org/TR/css-content-3/
title: CSS Generated Content Module Level 3
status: WD
generated: false
---

# CSS Generated Content Module Level 3

Spec: <https://www.w3.org/TR/css-content-3/>

Conformance tests in this folder are hand-written and tagged with `[Spec(...)]`
plus either `[SpecFact]` (passes today) or `[PendingFact]` (a known gap).

Implemented (wp:M5-css-16): `content` parsing (`none`/`normal`/`<string>`/
`attr()`) and `::before`/`::after` box synthesis.

Deferred: `counter()`/`counters()`, `open-quote`/`close-quote` + `quotes`,
image (`url()`) content. These parse-accept but generate no box.
