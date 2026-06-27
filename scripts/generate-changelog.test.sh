#!/usr/bin/env bash
# Self-tests for scripts/generate-changelog.sh (#1632).
#
# Builds a throwaway git repo with a known set of conventional-commit subjects
# and asserts the generator routes each subject to the correct section, handles
# the empty range, excludes merges, and respects an explicit FROM..TO range.
#
# Exit 0 = all cases passed, Exit 1 = at least one case failed.
# Wired into .github/workflows/check.yml so every push/PR runs these checks.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GEN="${SCRIPT_DIR}/generate-changelog.sh"

if [ ! -f "$GEN" ]; then
  echo "ERROR: generator not found at $GEN"
  exit 1
fi

PASSED=0
FAILED=0

pass() { echo "PASS: $1"; PASSED=$((PASSED + 1)); }
fail() { echo "FAIL: $1"; FAILED=$((FAILED + 1)); }

# assert_contains <output> <needle> <case-name>
assert_contains() {
  case "$1" in
    *"$2"*) pass "$3" ;;
    *)      fail "$3 (expected to find: $2)" ;;
  esac
}

# assert_not_contains <output> <needle> <case-name>
assert_not_contains() {
  case "$1" in
    *"$2"*) fail "$3 (did NOT expect: $2)" ;;
    *)      pass "$3" ;;
  esac
}

# Build a disposable repo.
WORK="$(mktemp -d 2>/dev/null || mktemp -d -t genclog.XXXXXX)"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT INT TERM

git -C "$WORK" init -q
git -C "$WORK" config user.email test@example.com
git -C "$WORK" config user.name test
git -C "$WORK" config commit.gpgsign false

commit() {
  # commit <subject>  — empty commit so we only exercise subject classification.
  git -C "$WORK" commit -q --allow-empty -m "$1"
}

commit "chore: seed repo"
git -C "$WORK" tag ver_20000101.00          # baseline "previous tag"

commit "feat(avalonia): add a thing"
commit "fix: correct a bug"
commit "fix!: a breaking fix"               # breaking-change marker
commit "docs: write docs"
commit "ci(release): tweak workflow"
commit "build: bump deps"
commit "refactor(core): tidy"
commit "test: add coverage"
commit "i18n: add translations"             # non-conforming -> Other
commit "Random freeform subject"            # non-conforming -> Other

# ---------------------------------------------------------------------------
# Case 1: full range (explicit FROM..TO) classifies every type.
# ---------------------------------------------------------------------------
OUT="$(cd "$WORK" && sh "$GEN" ver_20000101.00 HEAD)"

assert_contains "$OUT" "## 🚀 Features"                  "section: Features present"
assert_contains "$OUT" "- feat(avalonia): add a thing"   "route: feat -> Features"
assert_contains "$OUT" "## 🐛 Bug Fixes"                 "section: Bug Fixes present"
assert_contains "$OUT" "- fix: correct a bug"            "route: fix -> Bug Fixes"
assert_contains "$OUT" "- fix!: a breaking fix"          "route: fix! (breaking) -> Bug Fixes"
assert_contains "$OUT" "## 📖 Documentation"             "section: Documentation present"
assert_contains "$OUT" "- docs: write docs"             "route: docs -> Documentation"
assert_contains "$OUT" "## 🤖 CI / Build / Packaging"    "section: CI present"
assert_contains "$OUT" "- ci(release): tweak workflow"   "route: ci -> CI"
assert_contains "$OUT" "- build: bump deps"             "route: build -> CI"
assert_contains "$OUT" "## 🧰 Maintenance & Refactoring" "section: Maintenance present"
assert_contains "$OUT" "- refactor(core): tidy"          "route: refactor -> Maintenance"
assert_contains "$OUT" "- test: add coverage"           "route: test -> Maintenance"
assert_contains "$OUT" "## 🔧 Other Changes"             "section: Other present"
assert_contains "$OUT" "- i18n: add translations"        "route: i18n (non-type) -> Other"
assert_contains "$OUT" "- Random freeform subject"       "route: freeform -> Other"

# A feat subject must NOT leak into Other.
OTHER_BLOCK="$(printf '%s\n' "$OUT" | awk '/^## 🔧 Other Changes/{p=1;next} /^## /{p=0} p')"
assert_not_contains "$OTHER_BLOCK" "feat(avalonia)" "no feat leak into Other"
assert_not_contains "$OTHER_BLOCK" "fix: correct"   "no fix leak into Other"

# ---------------------------------------------------------------------------
# Case 2: auto-detect previous ver_* tag when FROM is empty.
# ---------------------------------------------------------------------------
OUT2="$(cd "$WORK" && sh "$GEN" "" HEAD)"
assert_contains "$OUT2" "ver_20000101.00...HEAD" "auto-detect: previous ver_* tag in header"
assert_contains "$OUT2" "- feat(avalonia): add a thing" "auto-detect: feat still routed"

# ---------------------------------------------------------------------------
# Case 3: empty range prints the no-changes line and exits 0.
# ---------------------------------------------------------------------------
EMPTY_EXIT=0
OUT3="$(cd "$WORK" && sh "$GEN" HEAD HEAD)" || EMPTY_EXIT=$?
[ "$EMPTY_EXIT" -eq 0 ] && pass "empty range: exit 0" || fail "empty range: exit $EMPTY_EXIT"
assert_contains "$OUT3" "_No changes in this range._" "empty range: no-changes line"

# ---------------------------------------------------------------------------
# Case 4: merge commits are excluded (--no-merges).
# ---------------------------------------------------------------------------
git -C "$WORK" checkout -q -b sidebranch ver_20000101.00
commit "feat: side feature"
git -C "$WORK" checkout -q -
# Create a real merge commit with a conventional-looking subject. This merge is
# required for the case; let a real failure surface instead of masking it.
git -C "$WORK" merge -q --no-ff -m "fix: this is actually a merge commit" sidebranch
OUT4="$(cd "$WORK" && sh "$GEN" ver_20000101.00 HEAD)"
assert_not_contains "$OUT4" "this is actually a merge commit" "merges excluded"
assert_contains "$OUT4" "- feat: side feature" "merged-in non-merge commit still listed"

echo "----------------------------------------"
echo "Self-test summary: ${PASSED} passed, ${FAILED} failed"
[ "$FAILED" -eq 0 ]
