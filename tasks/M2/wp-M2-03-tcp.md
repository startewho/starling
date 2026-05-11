---
id: "wp:M2-03-tcp"
milestone: "M2"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M2-01-url-parser"
blocks:
  - "wp:M2-04-tls"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#tcp"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-03-tcp"
---

# wp:M2-03 — TCP transport

## Goal
Async TCP client around `System.Net.Sockets`. Connection pool seam.

## Acceptance
Opens TCP to `example.com:80`, GET / over plaintext returns 200.

## Handoff log
- 2026-05-11T15:20Z — created.
