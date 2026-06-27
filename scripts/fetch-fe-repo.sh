#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
#
# fetch-fe-repo.sh — populate the FE-Repo resource folders for the in-app
# Resource Browser (#1644).
#
# The FE-Repo (graphics) and FE-Repo-Music-No-Preview (music) repositories are
# wired into FEBuilderGBA as git submodules but are intentionally NOT bundled
# into released artifacts (their payload is too large to attach to every
# release). This helper fetches them on demand into the `resources/` folder the
# Resource Browser searches, so the browser is no longer empty.
#
# It works in two situations:
#   * Inside a source clone — initializes the submodules in place.
#   * Next to an extracted release .zip (no git repo / no submodule) — shallow
#     `git clone`s the public repos into `resources/` next to the executable.
#
# Idempotent: a folder that is already populated is left untouched.
#
# Usage:
#   scripts/fetch-fe-repo.sh [--graphics-only | --music-only] [--dest DIR]
#
#   --graphics-only   Fetch only FE-Repo (graphics).
#   --music-only      Fetch only FE-Repo-Music-No-Preview (music).
#   --dest DIR        Root that should contain `resources/` (default: the repo
#                     root when run from a clone, else the current directory).
#   -h, --help        Show this help.
#
# Requires: git.
set -euo pipefail

GRAPHICS_URL="https://github.com/Klokinator/FE-Repo"
MUSIC_URL="https://github.com/laqieer/FE-Repo-Music-No-Preview"
GRAPHICS_PATH="resources/FE-Repo"
MUSIC_PATH="resources/FE-Repo-Music-No-Preview"

graphics_only=0
music_only=0
dest=""

# Print the leading comment block (the header docs) as help text. Skip the
# shebang + SPDX lines, then emit every `#`-prefixed line and STOP at the first
# line that is not a comment (so script code/constants never leak into --help).
usage() {
  awk '
    NR <= 2 { next }                 # shebang + SPDX
    /^#/    { sub(/^# ?/, ""); print; next }
    { exit }                         # first non-comment line ends the header
  ' "$0"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --graphics-only) graphics_only=1 ;;
    --music-only)    music_only=1 ;;
    --dest)          shift; dest="${1:-}" ;;
    -h|--help)       usage; exit 0 ;;
    *) echo "fetch-fe-repo: unknown argument: $1" >&2; usage; exit 2 ;;
  esac
  shift
done

# --graphics-only and --music-only are mutually exclusive: selecting both would
# fetch nothing and exit successfully, which is a silent no-op (#1669 review).
if [ "$graphics_only" -eq 1 ] && [ "$music_only" -eq 1 ]; then
  echo "fetch-fe-repo: --graphics-only and --music-only are mutually exclusive." >&2
  usage
  exit 2
fi

do_graphics=1
do_music=1
[ "$music_only" -eq 1 ] && do_graphics=0
[ "$graphics_only" -eq 1 ] && do_music=0

if ! command -v git >/dev/null 2>&1; then
  echo "fetch-fe-repo: git is required but was not found on PATH." >&2
  exit 1
fi

# Resolve the destination root. Prefer the explicit --dest, then the enclosing
# git working tree (a source clone), then the current directory (release zip).
if [ -z "$dest" ]; then
  if dest="$(git rev-parse --show-toplevel 2>/dev/null)"; then
    :
  else
    dest="$(pwd)"
  fi
fi

is_populated() {
  # Populated == directory exists and has at least one child entry
  # (matches FERepoResourceBrowser's empty-placeholder-as-not-found rule).
  [ -d "$1" ] && [ -n "$(ls -A "$1" 2>/dev/null)" ]
}

fetch_one() {
  local name="$1" url="$2" rel="$3"
  local target="$dest/$rel"
  if is_populated "$target"; then
    echo "fetch-fe-repo: $name already populated at $target — skipping."
    return 0
  fi
  echo "fetch-fe-repo: fetching $name into $target ..."
  if git -C "$dest" rev-parse --git-dir >/dev/null 2>&1 \
     && git -C "$dest" config --file .gitmodules --get "submodule.$rel.url" >/dev/null 2>&1; then
    # Source clone: init the registered submodule in place.
    git -C "$dest" submodule update --init --depth 1 "$rel"
  else
    # Released zip / non-git tree: shallow clone the public repo directly.
    mkdir -p "$(dirname "$target")"
    git clone --depth 1 "$url" "$target"
  fi
  echo "fetch-fe-repo: $name ready."
}

[ "$do_graphics" -eq 1 ] && fetch_one "FE-Repo (graphics)" "$GRAPHICS_URL" "$GRAPHICS_PATH"
[ "$do_music" -eq 1 ] && fetch_one "FE-Repo-Music (music)" "$MUSIC_URL" "$MUSIC_PATH"

echo "fetch-fe-repo: done. Open the FE-Repo Resource Browser to browse the assets."
