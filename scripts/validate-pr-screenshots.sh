#!/usr/bin/env bash
# Validates that PR description does not contain feature-branch image URLs
# (blob/{ref}/ or raw.githubusercontent.com/{owner}/{repo}/{ref}/)
# that will break after the branch is deleted post-merge.
#
# Usage: scripts/validate-pr-screenshots.sh <pr-number> [pr-body-file] [default-branch]
# Exit 0 = pass, Exit 1 = blocking violations found
#
# Runs in CI (check.yml) on every pull_request event.
# Pass the PR body via file to avoid network dependency (fail-closed).

set -euo pipefail

PR_NUMBER="${1:-${GITHUB_PR_NUMBER:-}}"
BODY_FILE="${2:-}"
REPO="${GITHUB_REPOSITORY:-laqieer/FEBuilderGBA}"
DEFAULT_BRANCH="${3:-${DEFAULT_BRANCH:-master}}"

if [ -z "$PR_NUMBER" ]; then
  echo "Usage: $0 <pr-number> [pr-body-file]"
  exit 1
fi

echo "=== Validating PR #${PR_NUMBER} screenshot URLs ==="

# Normalize Windows paths to POSIX (Git-Bash/MSYS won't resolve D:\... paths)
if [ -n "$BODY_FILE" ] && command -v cygpath &>/dev/null; then
  BODY_FILE=$(cygpath -u "$BODY_FILE" 2>/dev/null || echo "$BODY_FILE")
fi

# Get PR body — prefer file input (no network dependency), fallback to gh API
BODY_SOURCE="unknown"
if [ -n "$BODY_FILE" ] && [ -f "$BODY_FILE" ]; then
  BODY=$(cat "$BODY_FILE")
  BODY_SOURCE="file"
elif [ -n "$BODY_FILE" ]; then
  echo "ERROR: Body file '$BODY_FILE' not found — failing closed"
  exit 1
else
  BODY=$(gh pr view "$PR_NUMBER" -R "$REPO" --json body --jq .body 2>/dev/null || echo "")
  BODY_SOURCE="api"
  if [ -z "$BODY" ]; then
    echo "ERROR: Could not fetch PR body via API — failing closed"
    exit 1
  fi
fi

# Empty body is valid (no URLs to check) — screenshots are optional for docs/chore PRs
if [ -z "$BODY" ]; then
  echo "VALIDATION PASSED: PR body is empty (no URLs to check)"
  exit 0
fi

# CHECK 1: feature-branch URLs (blob/ or raw.githubusercontent.com/)
# These 404 after the branch is deleted post-merge.
BLOB_VIOLATIONS=$(echo "$BODY" | grep -oE 'blob/[a-zA-Z0-9_./-]+\.(png|jpg|jpeg|gif)(\?[a-zA-Z0-9_=&]*)?' | grep -v "blob/${DEFAULT_BRANCH}/" || true)
RAW_VIOLATIONS=$(echo "$BODY" | grep -oE 'raw\.githubusercontent\.com/[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+/[a-zA-Z0-9_./-]+\.(png|jpg|jpeg|gif)(\?[a-zA-Z0-9_=&]*)?' | grep -vE "raw\.githubusercontent\.com/[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+/${DEFAULT_BRANCH}/" || true)
VIOLATIONS=$(printf '%s\n%s\n' "$BLOB_VIOLATIONS" "$RAW_VIOLATIONS" | grep -v '^$' || true)

if [ -n "$VIOLATIONS" ]; then
  echo ""
  echo "ERROR: PR description contains feature-branch URLs that will break after merge:"
  echo ""
  echo "$VIOLATIONS" | while read -r url; do
    echo "  BLOCKED: $url"
  done
  echo ""
  echo "These URLs reference a feature branch (blob/{ref}/... or raw.githubusercontent.com/{owner}/{repo}/{ref}/...) and 404 after the branch is deleted."
  echo "Fix: commit screenshots to pr-screenshots/ on master (via a docs PR) FIRST, then reference them as blob/${DEFAULT_BRANCH}/pr-screenshots/... or raw.githubusercontent.com/{owner}/{repo}/${DEFAULT_BRANCH}/pr-screenshots/..."
  echo ""
  echo "VALIDATION FAILED"
  exit 1
fi

echo "CHECK 1 PASSED: No feature-branch URLs found"

# --- CHECK 2: feat/fix PRs must have at least one rendered image ---
# Get PR title
PR_TITLE=""
if command -v gh &>/dev/null; then
  PR_TITLE=$(gh pr view "$PR_NUMBER" -R "$REPO" --json title --jq .title 2>/dev/null || echo "")
fi

if echo "$PR_TITLE" | grep -qiE "^(feat|fix):"; then
  IMAGE_COUNT=$(echo "$BODY" | grep -cE '!\[.*\]\(https://raw\.githubusercontent\.com/|!\[.*\]\(https://github\.com/.*assets/|<img.*src=' || true)

  if [ "$IMAGE_COUNT" -eq 0 ]; then
    echo ""
    echo "ERROR: feat/fix PR #${PR_NUMBER} has NO rendered screenshots!"
    echo ""
    echo "Every feat/fix PR that modifies FEBuilderGBA.Avalonia/ MUST include"
    echo "at least one screenshot of the SPECIFIC affected editor with data."
    echo ""
    echo "Steps:"
    echo "  1. Launch app with ROM"
    echo "  2. Use UIAutomation to navigate to the affected editor"
    echo "  3. Capture via: dotnet run --project tools/WinCapture -c Release -- \"Title\" file.png"
    echo "  4. Commit to pr-screenshots/ and reference via raw.githubusercontent.com"
    echo ""
    echo "VALIDATION FAILED: Missing screenshots"
    exit 1
  fi

  # Warn about generic main window screenshots
  if echo "$BODY" | grep -qiE 'main.window|FEBuilderGBA.*main|proof\.png'; then
    echo "WARNING: PR may contain a generic main window screenshot."
    echo "Ensure screenshots show the SPECIFIC affected editor, not just the main window."
  fi

  echo "CHECK 2 PASSED: Found $IMAGE_COUNT screenshot(s) in feat/fix PR"
else
  echo "CHECK 2 SKIPPED: Not a feat/fix PR (title: $PR_TITLE)"
fi

echo "VALIDATION PASSED"
exit 0
