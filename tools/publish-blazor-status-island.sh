#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project="$repo_root/spikes/Starling.BlazorStatusIsland/Starling.BlazorStatusIsland.csproj"
publish_dir="$repo_root/spikes/Starling.BlazorStatusIsland/bin/Release/net10.0/publish"
site_dir="$repo_root/testdata/sites/blazor-status"

dotnet publish "$project" -c Release -o "$publish_dir"
rm -rf "$site_dir"
mkdir -p "$site_dir"
cp -R "$publish_dir/wwwroot/." "$site_dir/"

echo "Published Blazor WASM island to $site_dir"
