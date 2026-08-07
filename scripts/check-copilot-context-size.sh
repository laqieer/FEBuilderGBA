#!/usr/bin/env bash
# Enforce a byte budget for repository instructions loaded on every Copilot turn.

set -euo pipefail

LIMIT="${COPILOT_CONTEXT_SIZE_LIMIT:-12288}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ROOT="${1:-${COPILOT_CONTEXT_ROOT:-$REPO_ROOT}}"

if [ ! -d "$ROOT" ]; then
  echo "ERROR: repository root not found: $ROOT" >&2
  exit 2
fi

forbidden=(
  "CLAUDE.md"
  "AGENTS.md"
  "GEMINI.md"
  ".claude/CLAUDE.md"
)

for relative in "${forbidden[@]}"; do
  if [ -e "$ROOT/$relative" ]; then
    echo "ERROR: legacy always-loaded instruction file is not budgeted: $relative" >&2
    exit 2
  fi
done

instructions="$ROOT/.github/copilot-instructions.md"
if [ ! -f "$instructions" ]; then
  echo "ERROR: repository instructions not found: $instructions" >&2
  exit 2
fi

size="$(wc -c < "$instructions" | tr -d '[:space:]')"
if [ "$size" -gt "$LIMIT" ]; then
  echo "FAIL: always-loaded repository instructions total $size bytes." >&2
  echo "      The configured limit is $LIMIT bytes; keep detailed guidance in skills or docs." >&2
  exit 1
fi

echo "OK: always-loaded repository instructions total $size bytes (limit $LIMIT)."
exit 0
