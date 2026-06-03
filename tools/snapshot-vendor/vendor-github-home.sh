#!/usr/bin/env bash
#
# Fetch the github.com home page plus GitHub asset CSS and JS into an ignored
# local snapshot. Use this for CSS and layout perf work that needs the real
# selector mix without committing a large third-party bundle.
#
# Usage:
#   tools/snapshot-vendor/vendor-github-home.sh

set -euo pipefail

ROOT_URL="${1:-https://github.com/}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEST="${2:-$REPO_ROOT/testdata/local-snapshots/github.com}"
ASSETS_DIR="$DEST/assets"

mkdir -p "$ASSETS_DIR"

echo "Fetching $ROOT_URL -> $DEST"

declare -a ASSET_URLS=()
declare -A SEEN_URLS=()
declare -A DOWNLOADED_URLS=()

normalize_url() {
  local url="$1"
  url="${url#http://}"
  url="https://${url#https://}"
  url="${url%%\#*}"
  url="${url%%\?*}"
  printf '%s' "$url"
}

local_name_of() {
  local url
  url="$(normalize_url "$1")"
  basename "$url"
}

is_asset_kind() {
  case "$1" in
    *.css|*.js|*.mjs|*.woff|*.woff2|*.ttf|*.otf|*.png|*.svg|*.jpg|*.jpeg|*.webp|*.gif|*.ico)
      return 0 ;;
    *)
      return 1 ;;
  esac
}

add_asset_url() {
  local url
  url="$(normalize_url "$1")"
  [[ "$url" == https://github.githubassets.com/assets/* ]] || return 0
  is_asset_kind "$url" || return 0
  if [[ -z "${SEEN_URLS[$url]+x}" ]]; then
    SEEN_URLS["$url"]=1
    ASSET_URLS+=("$url")
  fi
}

extract_asset_urls() {
  local file
  for file in "$@"; do
    [[ -f "$file" ]] || continue
    grep -Eoh 'https://github\.githubassets\.com/assets/[^"'\''<>()[:space:]]+' "$file" || true
    grep -Eoh '//github\.githubassets\.com/assets/[^"'\''<>()[:space:]]+' "$file" | sed 's#^//#https://#' || true
  done
}

collect_assets_from_files() {
  local file url
  for file in "$@"; do
    while IFS= read -r url; do
      [[ -n "$url" ]] && add_asset_url "$url"
    done < <(extract_asset_urls "$file")
  done
}

fetch_file() {
  local url="$1"
  local output="$2"
  local headers="$3"

  curl --fail --silent --show-error --location --compressed \
       -A "starling-github-perf-snapshot/1.0" \
       -D "$headers" \
       -o "$output" \
       "$url"
}

download_pending_assets() {
  local url name path headers
  for url in "${ASSET_URLS[@]}"; do
    [[ -z "${DOWNLOADED_URLS[$url]+x}" ]] || continue
    name="$(local_name_of "$url")"
    path="$ASSETS_DIR/$name"
    headers="$(mktemp)"
    echo "  asset $name"
    fetch_file "$url" "$path" "$headers"
    rm -f "$headers"
    DOWNLOADED_URLS["$url"]=1
  done
}

rewrite_file_links() {
  local file="$1"
  local prefix="$2"
  local url name schemeless

  [[ -f "$file" ]] || return 0
  for url in "${ASSET_URLS[@]}"; do
    name="$(local_name_of "$url")"
    schemeless="${url#https:}"
    perl -0pi -e 's@\Q'"$url"'\E@'"$prefix$name"'@g; s@\Q'"$schemeless"'\E@'"$prefix$name"'@g' "$file"
  done
}

write_manifest() {
  local manifest="$DEST/manifest.json"
  local path sha bytes first url name

  {
    printf '{\n'
    printf '  "root_url": "%s",\n' "$ROOT_URL"
    printf '  "host": "github.com",\n'
    printf '  "captured_at": "%s",\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf '  "vendor_tool": "tools/snapshot-vendor/vendor-github-home.sh",\n'
    printf '  "local_only": true,\n'
    printf '  "resources": [\n'

    first=1
    for path in "$DEST/index.original.html" "$DEST/index.html"; do
      [[ -f "$path" ]] || continue
      sha="$(shasum -a 256 "$path" | awk '{print $1}')"
      bytes="$(wc -c < "$path" | tr -d ' ')"
      [[ $first -eq 0 ]] && printf ',\n'
      printf '    {\n      "url": "%s",\n      "path": "/%s",\n      "sha256": "%s",\n      "bytes": %s\n    }' \
        "$ROOT_URL" "$(basename "$path")" "$sha" "$bytes"
      first=0
    done

    for url in "${ASSET_URLS[@]}"; do
      name="$(local_name_of "$url")"
      path="$ASSETS_DIR/$name"
      [[ -f "$path" ]] || continue
      sha="$(shasum -a 256 "$path" | awk '{print $1}')"
      bytes="$(wc -c < "$path" | tr -d ' ')"
      [[ $first -eq 0 ]] && printf ',\n'
      printf '    {\n      "url": "%s",\n      "path": "/assets/%s",\n      "sha256": "%s",\n      "bytes": %s\n    }' \
        "$url" "$name" "$sha" "$bytes"
      first=0
    done

    printf '\n  ]\n}\n'
  } > "$manifest"
}

ROOT_HEADERS="$(mktemp)"
fetch_file "$ROOT_URL" "$DEST/index.original.html" "$ROOT_HEADERS"
rm -f "$ROOT_HEADERS"

cp "$DEST/index.original.html" "$DEST/index.html"
collect_assets_from_files "$DEST/index.original.html"
download_pending_assets

for _ in 1 2; do
  mapfile -d '' asset_files < <(find "$ASSETS_DIR" -type f \( -name '*.css' -o -name '*.js' -o -name '*.mjs' \) -print0)
  before_count="${#ASSET_URLS[@]}"
  collect_assets_from_files "${asset_files[@]}"
  if [[ "${#ASSET_URLS[@]}" -eq "$before_count" ]]; then
    break
  fi
  download_pending_assets
done

rewrite_file_links "$DEST/index.html" "assets/"
for path in "$ASSETS_DIR"/*; do
  [[ -f "$path" ]] && rewrite_file_links "$path" ""
done

write_manifest

css_count="$(find "$ASSETS_DIR" -type f -name '*.css' | wc -l | tr -d ' ')"
js_count="$(find "$ASSETS_DIR" -type f \( -name '*.js' -o -name '*.mjs' \) | wc -l | tr -d ' ')"
echo "Fetched ${#ASSET_URLS[@]} assets ($css_count CSS, $js_count JS)."
echo "Snapshot is ignored by git: $DEST"
