# Agents — rules of engagement

This file is the **first thing an implementation agent should read** when
opening this repo. It explains how multiple agents share work without
stepping on each other, and how a single agent can stop mid-task and have a
later session resume cleanly.

## TL;DR

```bash
# 1. Orient
cat AGENTS.md                            # this file
less browser-plan/13_MILESTONES.md       # where we are
less tasks/INDEX.md                      # what's available

# 2. Claim something unblocked
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-<your-handle>"

# 3. Switch to the per-package branch
git switch -c wp-M1-03-dom-core

# 4. Read the package file and start
less tasks/M1/wp-M1-03-dom-core.md

# 5. Commit often, with the wp id in the subject:
#    "wp:M1-03 — Node hierarchy + tests"

# 6. Build + test before pushing
dotnet build && dotnet test

# 7. Open PR, then flip to in_review:
./tasks/lib/claim.sh review wp:M1-03-dom-core "<pr-url>"

# 8. After merge:
./tasks/lib/claim.sh complete wp:M1-03-dom-core
```

If you have to stop early, **leave a handoff log entry** in the task file and
either keep the claim (you'll resume) or release it
(`./tasks/lib/claim.sh release wp:…`). Either way: commit and push so the
next agent sees the state.

## The contract

| You can rely on | You must do |
|---|---|
| One file per work package under `tasks/M*/wp-*.md` | Touch only your claimed package's file (plus your code changes) |
| `tasks/INDEX.md` reflects the current status | Update INDEX.md when you change a status |
| Each package has a dedicated git branch | Use the branch name from the task file's `branch:` field |
| Dependencies are explicit in `depends_on` | Don't start a package whose deps aren't `complete` |
| Stale claims age out at 72 h | Add a handoff log entry every session, even if you're "still going" |

## Repo map

```
tessera/
├── AGENTS.md                  ← you are here
├── README.md                  ← human-facing intro
├── browser-plan/              ← the design (immutable except by deliberate edit)
│   ├── 00_INDEX.md            ← start here for design context
│   ├── 13_MILESTONES.md       ← what milestone we're in
│   └── 14_AGENT_TASKS.md      ← authoritative package catalog
├── tasks/                     ← work coordination (this repo's queue)
│   ├── README.md              ← detailed workflow
│   ├── SCHEMA.md              ← frontmatter contract
│   ├── INDEX.md               ← current status of all packages
│   ├── lib/claim.sh           ← atomic claim/release helper
│   └── M<n>/wp-*.md           ← one file per work package
├── src/                       ← engine + headless + shell
├── tests/                     ← one xUnit project per src/ module + E2E
├── bench/Tessera.Bench/       ← BenchmarkDotNet
└── testdata/                  ← fixtures + golden PNGs + WPT subsets
```

## Build + test (must be green before merge)

```bash
dotnet --version            # expect 10.0.x
dotnet restore
dotnet build -c Debug
dotnet test  -c Debug
```

If `dotnet build` errors with permission-denied apphost deletions in a
sandbox or container, pass `-p:UseAppHost=false`. This is a sandbox quirk
only — CI runs without the flag.

## Rule 0

Engine modules under `src/Tessera.{Common,Url,Net,Html,Dom,Css,Layout,Paint,Js,Bindings,Loop,Engine}/`
are **pure managed**. No `[DllImport]`, no `[LibraryImport]`, no native
dependencies beyond what the .NET BCL ships. CI greps for this; lint job
fails if you regress it. The Shell (Avalonia) is exempt.

## Decision hierarchy when something's ambiguous

1. **The plan** (`browser-plan/`) — if a doc gives an answer, follow it.
2. **The work-package file** — it overrides general advice for the scope.
3. **The spec** the plan cites (WHATWG, ECMA, RFC) — follow the spec literally,
   citing section numbers in code comments.
4. **Ask** — open an `<!-- OPEN QUESTION -->` block in the relevant
   `browser-plan/` doc and proceed with your best guess. Do not block on
   absent humans.

## Concurrency model

- **Two agents on different packages:** independent. No coordination
  required.
- **Two agents on the same package:** the second one to commit a `claim`
  edit loses the race (git non-fast-forward on push). They pull, see the
  new state, and pick a different package.
- **Agent dies mid-task:** the `claimed_at` timestamp shows when. After
  72 h with no commits on the branch, any agent may release the claim and
  start over (or pick up using the handoff log).
- **Cross-package edits:** if your work changes a shared file
  (`Directory.Build.props`, `Directory.Packages.props`, `Tessera.sln`),
  call it out in the handoff log and rebase / coordinate. These files are
  the merge-conflict hotspots.

## A worked example

Day 1 — agent-alex picks up `wp:M1-03-dom-core`:

```bash
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-alex"
# tasks/M1/wp-M1-03-dom-core.md now has status: claimed, claimed_by: agent-claude-alex
git switch -c wp-M1-03-dom-core
# ...writes Node.cs, NodeKind.cs, mutations...
git commit -am "wp:M1-03 — Node + NodeKind + first 30 mutation tests"
git push -u origin wp-M1-03-dom-core
```

Day 2 — alex has to stop, riley picks it up:

```bash
# alex, end of session:
$EDITOR tasks/M1/wp-M1-03-dom-core.md   # adds handoff log entry
./tasks/lib/claim.sh release wp:M1-03-dom-core
git push

# riley starts:
git pull
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-riley"
git switch wp-M1-03-dom-core            # same branch, continuing
cat tasks/M1/wp-M1-03-dom-core.md       # reads the handoff log
# ...continues from "remaining: Element attribute mutations"...
```

Day 3 — riley finishes, opens PR:

```bash
./tasks/lib/claim.sh review wp:M1-03-dom-core "https://github.com/.../pull/42"
# After merge:
./tasks/lib/claim.sh complete wp:M1-03-dom-core
# Update tasks/INDEX.md: M1-03 → 🟢 complete; M1-04, M1-06 unblock.
```

## Questions

If the design plan says X and reality says Y, edit the plan to match Y in
the same PR, and explain in the PR body. The plan is a living document —
it tracks the implementation, not the other way around.
