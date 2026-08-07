#!/usr/bin/env bash
# Self-tests for scripts/validate-pr-screenshots.sh
#
# Exercises the validator against a fixed set of fixtures and reports PASS/FAIL.
# Exit 0 = all cases passed, Exit 1 = at least one case failed.
#
# Wired into .github/workflows/check.yml so every push/PR runs these checks.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VALIDATOR="${SCRIPT_DIR}/validate-pr-screenshots.sh"

if [ ! -f "$VALIDATOR" ]; then
  echo "ERROR: validator not found at $VALIDATOR"
  exit 1
fi

PASSED=0
FAILED=0
TOTAL=0

# run_case <name> <expected_exit> <body-content-or-MISSING> <title> <changed-files>
# If body-content is the literal string "MISSING", the body file is NOT created
# and a bogus path is passed (covers the fail-closed missing-body-file case).
run_case() {
  local name="$1"
  local expected_exit="$2"
  local body_content="$3"
  local title="${4:-docs: test}"
  local changed_files="${5-docs/test.md}"

  TOTAL=$((TOTAL + 1))

  local body_file
  local pass_path
  if [ "$body_content" = "MISSING" ]; then
    # Use a path that intentionally does not exist
    pass_path="/tmp/validate-pr-screenshots.test.does-not-exist.$$"
    # Make sure it really doesn't exist
    rm -f "$pass_path" 2>/dev/null || true
  else
    body_file=$(mktemp)
    printf '%s' "$body_content" > "$body_file"
    pass_path="$body_file"
  fi

  local actual_exit=0
  local output
  local changed_file
  changed_file=$(mktemp)
  printf '%s\n' "$changed_files" > "$changed_file"
  # Capture both stdout and stderr so we can show it on failure.
  output=$(PR_TITLE_OVERRIDE="$title" bash "$VALIDATOR" 999 "$pass_path" master "$changed_file" 2>&1) || actual_exit=$?

  if [ "$actual_exit" = "$expected_exit" ]; then
    echo "  PASS: $name (exit $actual_exit)"
    PASSED=$((PASSED + 1))
  else
    echo "  FAIL: $name (expected exit $expected_exit, got $actual_exit)"
    echo "    --- validator output ---"
    echo "$output" | sed 's/^/    /'
    echo "    --- end output ---"
    FAILED=$((FAILED + 1))
  fi

  if [ "$body_content" != "MISSING" ] && [ -n "${body_file:-}" ]; then
    rm -f "$body_file"
  fi
  rm -f "$changed_file"
}

echo "=== scripts/validate-pr-screenshots.test.sh ==="

# 1. Default-branch blob URL — must pass.
run_case "good_blob_master" 0 \
  '![ok](https://github.com/laqieer/FEBuilderGBA/blob/master/pr-screenshots/foo.png)'

# 2. Feature-branch blob URL — must fail.
run_case "bad_blob_feature" 1 \
  '![bad](https://github.com/laqieer/FEBuilderGBA/blob/fix/some-branch/pr-screenshots/foo.png)'

# 3. Default-branch raw URL — must pass.
run_case "good_raw_master" 0 \
  '![ok](https://raw.githubusercontent.com/laqieer/FEBuilderGBA/master/pr-screenshots/foo.png)'

# 4. Feature-branch raw URL (mirrors PR #338's broken screenshot) — must fail.
run_case "bad_raw_feature" 1 \
  '![bad](https://raw.githubusercontent.com/laqieer/FEBuilderGBA/fix/mcp-reserved-name-337/pr-screenshots/foo.png)'

# 5. Feature-branch raw URL with slashed branch name — must fail.
run_case "bad_raw_slashed_branch" 1 \
  '![bad](https://raw.githubusercontent.com/laqieer/FEBuilderGBA/feat/abc-123/pr-screenshots/foo.png)'

# 6. Empty body — must pass (no URLs to check).
run_case "empty_body" 0 ''

# 7. Missing body file — must fail closed.
run_case "missing_body_file" 1 'MISSING'

# 8. Nested 'master' in path — must STILL fail closed (regression guard against
#    the over-exclusion concern raised on PR #341: a URL where the branch is
#    'feature' and 'master' appears as the 4th path segment must still be
#    flagged as a violation).
run_case "bad_raw_nested_master_in_path" 1 \
  '![bad](https://raw.githubusercontent.com/owner/repo/feature/master/pr-screenshots/foo.png)'

# 9. Non-GUI feat/fix PRs rely on tests and CI, not screenshots.
run_case "non_gui_feat_without_image" 0 \
  'No screenshot needed.' \
  'feat(core): add parser' \
  'FEBuilderGBA.Core/TextEscape.cs'

# 10. GUI feat/fix PRs still require real rendered evidence.
run_case "gui_feat_without_image" 1 \
  'No screenshot yet.' \
  'feat(avalonia): add editor control' \
  'FEBuilderGBA.Avalonia/Views/UnitEditorView.axaml'

# 11. A GitHub attachment satisfies the GUI screenshot requirement.
run_case "gui_fix_with_attachment" 0 \
  '![proof](https://github.com/user-attachments/assets/12345678-1234-1234-1234-123456789abc)' \
  'fix(avalonia): repair editor layout' \
  'FEBuilderGBA.Avalonia/Views/UnitEditorView.axaml'

# 12. A feat/fix PR with no usable changed-file list fails closed.
run_case "feat_with_empty_changed_files" 1 \
  'No screenshot.' \
  'fix(core): repair parser' \
  ''

# 13. An empty GUI feat/fix body still reaches screenshot enforcement.
run_case "gui_feat_with_empty_body" 1 \
  '' \
  'feat(avalonia): add editor control' \
  'FEBuilderGBA.Avalonia/Views/UnitEditorView.axaml'

# 14. Windows/MSYS paths are normalized for both body and changed-file inputs.
if command -v cygpath >/dev/null 2>&1; then
  TOTAL=$((TOTAL + 1))
  body_file=$(mktemp)
  changed_file=$(mktemp)
  printf 'No screenshot needed.' > "$body_file"
  printf 'FEBuilderGBA.Core/TextEscape.cs\n' > "$changed_file"
  windows_body=$(cygpath -w "$body_file")
  windows_changed=$(cygpath -w "$changed_file")
  actual_exit=0
  output=$(PR_TITLE_OVERRIDE='feat(core): add parser' bash "$VALIDATOR" 999 "$windows_body" master "$windows_changed" 2>&1) || actual_exit=$?
  if [ "$actual_exit" -eq 0 ]; then
    echo "  PASS: windows_changed_file_path (exit 0)"
    PASSED=$((PASSED + 1))
  else
    echo "  FAIL: windows_changed_file_path (expected exit 0, got $actual_exit)"
    echo "$output" | sed 's/^/    /'
    FAILED=$((FAILED + 1))
  fi
  rm -f "$body_file" "$changed_file"
fi

echo ""
echo "PASSED: ${PASSED}/${TOTAL}  FAILED: ${FAILED}/${TOTAL}"

if [ "$FAILED" -gt 0 ]; then
  exit 1
fi

exit 0
