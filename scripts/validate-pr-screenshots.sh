#!/usr/bin/env bash
# Validates that PR description does not contain blob/{feature-branch}/ image URLs
# that will break after the branch is deleted post-merge.
#
# Usage: scripts/validate-pr-screenshots.sh <pr-number>
# Exit 0 = pass, Exit 1 = blocking violations found
#
# Runs in CI (check.yml) on every pull_request event.

set -euo pipefail

PR_NUMBER="${1:-${GITHUB_PR_NUMBER:-}}"
REPO="${GITHUB_REPOSITORY:-laqieer/FEBuilderGBA}"

if [ -z "$PR_NUMBER" ]; then
  echo "Usage: $0 <pr-number>"
  exit 1
fi

echo "=== Validating PR #${PR_NUMBER} screenshot URLs ==="

# Get PR body
BODY=$(gh pr view "$PR_NUMBER" -R "$REPO" --json body --jq .body 2>/dev/null || echo "")

if [ -z "$BODY" ]; then
  echo "SKIP: Could not fetch PR body"
  exit 0
fi

# Check for blob/{non-master-branch}/ URLs in image tags
# Pattern: blob/<anything-except-master>/<path>
# Matches both ![...](blob/...) and raw URLs
VIOLATIONS=$(echo "$BODY" | grep -oE 'blob/[a-zA-Z0-9_./-]+/pr-screenshots/[a-zA-Z0-9_.-]+\.(png|jpg|jpeg|gif)' | grep -v 'blob/master/' || true)

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

# Also check for any image URLs pointing to non-master branches
BRANCH_URLS=$(echo "$BODY" | grep -oE 'github\.com/laqieer/FEBuilderGBA/blob/[a-zA-Z0-9_./-]+\.(png|jpg|jpeg|gif)\?raw=1' | grep -v 'blob/master/' || true)

if [ -n "$BRANCH_URLS" ]; then
  echo ""
  echo "ERROR: PR description contains branch-specific image URLs:"
  echo ""
  echo "$BRANCH_URLS" | while read -r url; do
    echo "  BLOCKED: $url"
  done
  echo ""
  echo "VALIDATION FAILED"
  exit 1
fi

echo "VALIDATION PASSED: No feature-branch blob URLs found in PR description"
exit 0
