#!/usr/bin/env bash
# Validates that PR description does not contain blob/{feature-branch}/ image URLs
# that will break after the branch is deleted post-merge.
#
# Usage: scripts/validate-pr-screenshots.sh <pr-number> [pr-body-file]
# Exit 0 = pass, Exit 1 = blocking violations found
#
# Runs in CI (check.yml) on every pull_request event.
# Pass the PR body via file to avoid network dependency (fail-closed).

set -euo pipefail

PR_NUMBER="${1:-${GITHUB_PR_NUMBER:-}}"
BODY_FILE="${2:-}"
REPO="${GITHUB_REPOSITORY:-laqieer/FEBuilderGBA}"

if [ -z "$PR_NUMBER" ]; then
  echo "Usage: $0 <pr-number> [pr-body-file]"
  exit 1
fi

echo "=== Validating PR #${PR_NUMBER} screenshot URLs ==="

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

# Consolidated check: find ANY blob/{non-master}/ image URL in any directory
# Matches: blob/<branch>/<any-path>.(png|jpg|jpeg|gif) with optional query string
VIOLATIONS=$(echo "$BODY" | grep -oE 'blob/[a-zA-Z0-9_./-]+\.(png|jpg|jpeg|gif)(\?[a-zA-Z0-9_=&]*)?' | grep -v 'blob/master/' || true)

if [ -n "$VIOLATIONS" ]; then
  echo ""
  echo "ERROR: PR description contains feature-branch blob URLs that will break after merge:"
  echo ""
  echo "$VIOLATIONS" | while read -r url; do
    echo "  BLOCKED: $url"
  done
  echo ""
  echo "These URLs use blob/{feature-branch}/ which becomes 404 after branch deletion."
  echo "Fix: commit screenshots to pr-screenshots/ on master (via a docs PR) FIRST,"
  echo "then reference them as blob/master/pr-screenshots/..."
  echo ""
  echo "VALIDATION FAILED"
  exit 1
fi

echo "VALIDATION PASSED: No feature-branch blob URLs found in PR description"
exit 0
