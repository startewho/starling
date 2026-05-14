# native/ — Skia Graphite + Dawn + ANGLE build and C ABI shim

This directory holds everything needed to **reproduce** Tessera's native
graphics layer: the Skia/Dawn build driver and the C ABI shim (`tessera_skia`)
that .NET loads. It contains build scripts and shim source — it does **not**
contain any built binaries, and a Skia checkout is never committed here.

> **Status: built and working on osx-arm64.** `build-skia.sh` + the shim CMake
> build produce a working `libtessera_skia.dylib` (~27 MB) that the engine
> renders through. win-x64 / linux-x64 native builds have **not** been produced
> yet — `build-skia.ps1` and the Linux path of `build-skia.sh` are written but
> unrun. Skia Graphite is the engine's **sole** rasterizer: there is no managed
> fallback, so on any RID without the shim the engine build hard-fails (see
> "Why a fresh checkout fails the build" below).

## The shim is gitignored — you must build it on a fresh checkout

`runtimes/` and `third_party/skia/` are in `.gitignore`. A fresh `git clone`
has **no** `libtessera_skia.dylib`. Because there is no managed fallback,
`dotnet build` will fail fast with an actionable error until you produce it.
The two build steps below are the whole story.

## Fresh-checkout build (osx-arm64)

```bash
# 0. one-time: depot_tools on PATH (provides fetch/gclient; gn+ninja are
#    self-provisioned by Skia, see below).
git clone https://chromium.googlesource.com/chromium/tools/depot_tools.git ~/depot_tools
echo 'export PATH="$HOME/depot_tools:$PATH"' >> ~/.zshrc && source ~/.zshrc

# 1. build Skia + Graphite + Dawn static libs  (~20-40 min, one-time per
#    REVISIONS.md change). Stages libs + headers into runtimes/osx-arm64/native/.
./native/build-skia.sh

# 2. build the shim and static-link it against the Skia/Dawn libs from step 1.
#    Produces runtimes/osx-arm64/native/libtessera_skia.dylib (~seconds-minutes).
cmake -S native/shim -B native/build/osx-arm64 -DCMAKE_BUILD_TYPE=Release
cmake --build native/build/osx-arm64

# 3. (optional) run the native smoke test: context -> surface -> fill_rect ->
#    read_pixels with pixel-color asserts.
cmake --build native/build/osx-arm64 --target run_smoke

# 4. now the .NET build finds the shim and succeeds.
dotnet build
```

Step 1 is the long pole and only needs re-running when `REVISIONS.md` changes.
Step 2 is what you re-run after editing `shim/tessera_skia.cpp`.

### Windows / Linux

`build-skia.ps1` (Windows, from a Developer PowerShell) and the Linux path of
`build-skia.sh` are written but **have not been run** — expect to debug them.
After step 1 succeeds on those platforms, step 2 is the same `cmake` invocation
with the RID-appropriate `-B native/build/<rid>` (CMake auto-detects the RID).

## Why a fresh checkout fails the build (by design)

Skia Graphite is the **sole** rasterizer — there is no managed fallback. Two
guards make a missing shim a loud, early failure instead of a runtime
`DllNotFoundException` deep in a render:

- `src/Tessera.Skia/Tessera.Skia.csproj` has a `BeforeTargets="Build"` target
  that errors out if the shim for the host OS is absent, pointing back here.
- `src/Tessera.Skia/Interop/NativeLoader.cs` throws an actionable
  `DllNotFoundException` (listing every probed path) if the resolver can't find
  the shim at load time.

This is intentional. Do not add a fallback — fix the build instead.

## Layout

```
native/
├── README.md            ← this file
├── build-skia.sh        ← Skia+Dawn build driver (macOS done; Linux unrun)
├── build-skia.ps1       ← Skia+Dawn build driver (Windows; unrun)
├── out/<rid>/           ← GN/Ninja build output + Dawn gen headers  (gitignored)
├── build/<rid>/         ← CMake build dir for the shim  (gitignored)
└── shim/
    ├── tessera_skia.h    ← C ABI surface (~20 `ts_` functions)
    ├── tessera_skia.cpp  ← real impl: Dawn + Skia Graphite behind the C ABI
    ├── smoke_test.c      ← native end-to-end harness
    └── CMakeLists.txt    ← builds libtessera_skia, static-linked to libskia+Dawn

third_party/
├── REVISIONS.md         ← the pinned lockfile (Skia/Dawn/ANGLE SHAs)
└── skia/                ← fetched Skia checkout  (gitignored)

runtimes/
└── <rid>/native/        ← staged Skia/Dawn libs + the final shim  (gitignored)
```

`<rid>` is a .NET Runtime Identifier: `osx-arm64`, `win-x64`, `linux-x64`.

## What `build-skia.sh` does

1. Reads the pinned revisions from `third_party/REVISIONS.md`.
2. Clones/updates `third_party/skia/` to exactly `SKIA_COMMIT` and **aborts
   loudly** if the checked-out `HEAD` drifts from the pin.
3. `tools/git-sync-deps` (with `GIT_SYNC_DEPS_SKIP_EMSDK=1` — emsdk is WASM-only
   and its activate step fails outside the Skia checkout) pulls Dawn + ANGLE +
   harfbuzz + icu, then verifies the Dawn/ANGLE SHAs against `REVISIONS.md`.
4. Self-provisions Skia's pinned `gn` and `ninja` (`bin/fetch-ninja` /
   `bin/fetch-gn` — git-sync-deps fetches `gn` but **not** `ninja`); depot_tools
   is only needed for `fetch`/`gclient`, not the build proper.
5. `gn gen` with the Graphite GN args, then `ninja … skia`.
6. Stages the Skia + Dawn static libs, headers, and license files into
   `runtimes/<rid>/native/`.

### GN args (and the non-obvious ones)

```
skia_enable_graphite=true
skia_use_dawn=true
skia_use_cpp20=true          # REQUIRED with skia_use_dawn: Dawn's Tint is C++20;
                             # Skia otherwise defaults to C++17 and fails to build Dawn
skia_use_gl=true             # ANGLE fallback
skia_use_harfbuzz=true
skia_use_icu=true
skia_use_system_*=false      # build Skia's *bundled* externals, not host dev libs —
                             # the system libs default to true and break on macOS
is_official_build=true
target_cpu="arm64"|"x64"     # per RID
target_os="mac"|"win"|"linux"
```

## What the shim CMake build does

`shim/CMakeLists.txt` compiles `tessera_skia.cpp` and static-links it against
the Skia/Dawn archives staged in step 1 into one `libtessera_skia.{dylib,dll,so}`
— the single native file .NET loads. Notes that bit experience into the build:

- **Headers** come from the Skia *source checkout* (`third_party/skia/`), not
  the staged flattened `include/` tree: Skia's public headers self-reference as
  `#include "include/core/..."`, and Dawn's *generated* headers
  (`dawn/webgpu_cpp.h`) only exist under `native/out/<rid>/gen/`.
- **Linking:** `is_official_build=true` already folds all of Dawn + SPIRV-Tools
  + several codecs *into* `libskia.a`. Linking the separate `libdawn_*.a` etc.
  produces thousands of duplicate symbols — so the link line is `libskia.a`
  (`-force_load` / `--whole-archive` to keep the registered Graphite backend
  factories) plus *only* the third-party C archives `libskia.a` leaves
  undefined (harfbuzz, png, webp, zlib, skcms, wuffs, dng_sdk, piex).
- On Apple the TU compiles as Objective-C++ (`-x objective-c++ -fobjc-arc`) for
  the CoreText `SkFontMgr` path.

## Prerequisites per platform

All platforms need **`depot_tools`** on `PATH`, **Python 3.9+**, **Git 2.30+**,
**CMake 3.20+**, and ~40 GB free disk + a fast network for the first checkout.

- **macOS (`osx-arm64`)** — macOS 14+ Apple Silicon; full **Xcode** + CLT
  (`xcode-select --install`) for the Metal toolchain. Targets Metal via Dawn.
- **Windows (`win-x64`)** — Windows 11 / Server 2025; **Visual Studio 2022**
  "Desktop development with C++" + Windows SDK; `pwsh`. Targets D3D12 via Dawn.
- **Linux (`linux-x64`)** — Ubuntu 24.04; `build-essential clang
  libgl1-mesa-dev libx11-dev libxcomposite-dev libxcursor-dev libxi-dev
  libxrandr-dev libvulkan-dev mesa-vulkan-drivers ninja-build`. Targets Vulkan
  via Dawn.

## Artifact strategy

Native binaries are built **out-of-band** by `.github/workflows/native.yml`
(manual / `REVISIONS.md`-triggered), uploaded as run artifacts, and consumed by
PR `ci.yml` via a `gh run download` restore step. They are **not** built in PR
`ci.yml` and **not** committed to git. See `third_party/REVISIONS.md` for the
full pinning rationale.
