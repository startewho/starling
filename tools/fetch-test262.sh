#!/usr/bin/env bash
# tools/fetch-test262.sh — fetch the tc39/test262 conformance suite at a pinned
# commit into testdata/test262/ (gitignored; ~50k files). Sparse: harness/ + test/.
#
# Usage: tools/fetch-test262.sh
# Idempotent: re-runs check out the pinned SHA without a full re-clone.
set -euo pipefail

# Pinned for reproducible pass-rate measurement. Bump deliberately.
TEST262_SHA="c42f56da60d2df4c6e3dd74848b2b38a6c313a50"
REPO="https://github.com/tc39/test262.git"

root="$(git rev-parse --show-toplevel)"
dst="$root/testdata/test262"

if [[ -f "$dst/.git/HEAD" ]]; then
    echo "test262 checkout exists at $dst — fetching pinned SHA $TEST262_SHA"
    git -C "$dst" fetch --depth 1 origin "$TEST262_SHA"
    git -C "$dst" checkout -q FETCH_HEAD
else
    echo "cloning test262 (sparse: harness + test) @ $TEST262_SHA → $dst"
    mkdir -p "$dst"
    git -C "$dst" init -q
    git -C "$dst" remote add origin "$REPO" 2>/dev/null || true
    git -C "$dst" config core.sparseCheckout true
    git -C "$dst" sparse-checkout init --cone
    git -C "$dst" sparse-checkout set harness test
    git -C "$dst" fetch -q --depth 1 origin "$TEST262_SHA"
    git -C "$dst" checkout -q FETCH_HEAD
fi

echo "test262 ready: $(find "$dst/test" -name '*.js' | wc -l | tr -d ' ') test files; harness:"
ls "$dst/harness" | head
