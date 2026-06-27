#!/bin/sh
# generate-changelog.sh — conventional-commit release-notes generator (#1632).
#
# Reads `git log FROM..TO` subjects and groups them by their Conventional
# Commits type (feat/fix/docs/... — enforced repo-wide by
# .github/workflows/pr-title-lint.yml, #1647), printing grouped Markdown release
# notes to stdout. The leading type is used only for ROUTING; each subject line
# is printed VERBATIM (prefix kept) so the bullet is self-describing. This is the
# title-derived grouping mechanism the release flow uses: GitHub's native
# `.github/release.yml` categories are label-based and cannot read the
# conventional prefix from a commit/PR title, so we derive the grouping here.
#
# USAGE
#   scripts/generate-changelog.sh [FROM] [TO]
#     FROM  previous ref (default: the previous `ver_*` tag reachable from TO,
#           or the repo root if there is none).
#     TO    new ref (default: HEAD).
#
#   # Notes between the last release tag and HEAD:
#   scripts/generate-changelog.sh
#   # Notes for a specific tag range:
#   scripts/generate-changelog.sh ver_20260204.22 ver_20260601.00
#
# OUTPUT
#   Markdown with one "## <section>" per non-empty conventional type, each item
#   `- <subject>`. Merge commits are EXCLUDED (`git log --no-merges`); any
#   non-merge subject that does not match a single conventional type is routed
#   to "Other Changes" so the notes are never lossy. Exit status is 0 on success
#   (an empty range prints a "no changes" line); a real git failure (e.g. a bad
#   range or unfetched tags) is NOT swallowed and fails the script.
#
# DEPENDENCIES: POSIX sh + git only (no Node/jq/python) — runs as-is on the
# ubuntu release runner and on any contributor machine (GNU or BSD/macOS).

set -eu

TO="${2:-HEAD}"

# Resolve FROM: explicit arg, else the most recent ver_* tag before TO, else
# the repo root (so the very first release still produces a full log).
if [ "${1:-}" != "" ]; then
  FROM="$1"
else
  FROM="$(git describe --tags --abbrev=0 --match 'ver_*' "${TO}^" 2>/dev/null || true)"
fi

if [ -n "${FROM}" ]; then
  RANGE="${FROM}..${TO}"
  HEADER_RANGE="${FROM}...${TO}"
else
  RANGE="${TO}"
  HEADER_RANGE="(initial)...${TO}"
fi

# Pull subjects (one per line). %s is the subject only — body is ignored so a
# multi-paragraph commit contributes a single entry. A real git failure (bad
# range, unfetched tags) is NOT swallowed: `set -e` lets it fail the script so
# the release step never publishes an empty body from a broken range. An empty
# but valid range simply yields no output (handled as "no changes" below).
SUBJECTS="$(git log --no-merges --pretty=format:'%s' "${RANGE}")"

# Temp files per bucket (portable; avoids bash arrays). `mktemp -d` with no
# template is GNU-only; fall back to an explicit template for BSD/macOS mktemp.
TMPDIR_CL="$(mktemp -d 2>/dev/null || mktemp -d -t changelog.XXXXXX)"
trap 'rm -rf "${TMPDIR_CL}"' EXIT INT TERM
: > "${TMPDIR_CL}/feat"
: > "${TMPDIR_CL}/fix"
: > "${TMPDIR_CL}/docs"
: > "${TMPDIR_CL}/ci"
: > "${TMPDIR_CL}/maint"
: > "${TMPDIR_CL}/other"

# Classify every subject by its leading conventional type (with optional scope
# and the breaking-change '!'), e.g. "feat(avalonia): ..." or "fix!: ...", in a
# SINGLE awk pass (per-line shell+sed is far too slow over a multi-thousand
# commit backlog). The subject is written verbatim (prefix kept) so the entry
# is self-describing; routing is by the matched type only.
printf '%s\n' "${SUBJECTS}" | awk -v d="${TMPDIR_CL}" '
  {
    if ($0 == "") next
    type = ""
    # ^<letters>(<optional (scope)>)(<optional !>):
    if (match($0, /^[A-Za-z]+(\([^)]*\))?!?:/)) {
      pre = substr($0, 1, RLENGTH)
      sub(/[(!:].*$/, "", pre)   # keep just the leading type word
      type = tolower(pre)
    }
    if      (type == "feat")                       bucket = "feat"
    else if (type == "fix")                        bucket = "fix"
    else if (type == "docs")                       bucket = "docs"
    else if (type == "ci"   || type == "build")    bucket = "ci"
    else if (type == "chore"    || type == "refactor" || type == "test" \
          || type == "perf"     || type == "style"    || type == "revert") bucket = "maint"
    else                                           bucket = "other"
    print "- " $0 >> (d "/" bucket)
  }
'

emit_section() {
  # $1 = bucket file, $2 = section title
  if [ -s "${TMPDIR_CL}/$1" ]; then
    printf '## %s\n\n' "$2"
    cat "${TMPDIR_CL}/$1"
    printf '\n'
  fi
}

# Total commit count for the summary line.
TOTAL="$(printf '%s\n' "${SUBJECTS}" | grep -c . || true)"
if [ -z "${TOTAL}" ] || [ "${SUBJECTS}" = "" ]; then
  TOTAL=0
fi

printf 'Changes in `%s` — %s commit(s).\n\n' "${HEADER_RANGE}" "${TOTAL}"

if [ "${TOTAL}" -eq 0 ]; then
  printf '_No changes in this range._\n'
  exit 0
fi

emit_section feat  "🚀 Features"
emit_section fix   "🐛 Bug Fixes"
emit_section docs  "📖 Documentation"
emit_section ci    "🤖 CI / Build / Packaging"
emit_section maint "🧰 Maintenance & Refactoring"
emit_section other "🔧 Other Changes"
