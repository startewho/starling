# Agent instructions

The repository's agent rules of engagement live in [AGENTS.md](AGENTS.md) — read it first.

The **C# performance policy** is the source of truth in AGENTS.md under
"Coding standards — performance policy for C# code". It applies to all code in
this repo. Follow it for every change.

**Testing a demo site:** when asked to test, verify, or check a demo site (the
fixtures in `testdata/sites/...`), always load it through the running Aspire
harness at `http://localhost:8088/<name>/`, never by rendering the files from
disk (`file://`, a loopback, or an in-process stub). Rendering from disk hides
real serving failures — most notably the `sites` YARP container not running when
Docker is down. See AGENTS.md under "Getting traces & telemetry from Aspire" for
the rule and the quick checks when a site will not load.
