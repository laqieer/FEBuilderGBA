#!/usr/bin/env bash
# check-claudemd-size.sh — fail if CLAUDE.md exceeds the harness truncation limit.
#
# The Claude Code harness truncates the injected project-instructions file
# (CLAUDE.md) at roughly 40,000 characters. Anything past that point is silently
# dropped, so guidance below the cut never reaches the agent. Issue #1645 (a
# regression of #1039) restructured CLAUDE.md back under the limit by relocating
# the verbose per-file Core-seam catalog into docs/CORE-SEAMS.md. This gate keeps
# CLAUDE.md from silently regrowing past the limit again.
#
# Usage:
#   scripts/check-claudemd-size.sh [path-to-file]
#
# The target file defaults to CLAUDE.md (resolved relative to the repo root, i.e.
# the directory above this script). Override with the first argument or the
# CLAUDE_MD_PATH environment variable — this lets the self-test point the gate at
# a temporary oversized fixture without mutating the real CLAUDE.md.
#
# Exit codes: 0 = within limit, 1 = over limit, 2 = file not found / bad usage.

set -euo pipefail

# Maximum allowed size in BYTES. wc -c counts bytes; the harness limit is ~40,000
# characters. We gate on bytes (a conservative proxy: a multi-byte UTF-8 char is
# >=1 byte, so staying under 40,000 bytes guarantees under 40,000 chars).
LIMIT="${CLAUDEMD_SIZE_LIMIT:-40000}"

# Resolve the default target relative to the repo root (parent of scripts/).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

TARGET="${1:-${CLAUDE_MD_PATH:-$REPO_ROOT/CLAUDE.md}}"

if [ ! -f "$TARGET" ]; then
  echo "ERROR: target file not found: $TARGET" >&2
  exit 2
fi

SIZE="$(wc -c < "$TARGET" | tr -d '[:space:]')"

if [ "$SIZE" -ge "$LIMIT" ]; then
  echo "FAIL: $TARGET is $SIZE bytes, at or over the $LIMIT-byte harness limit." >&2
  echo "      Relocate verbose per-file Core-seam notes into docs/CORE-SEAMS.md" >&2
  echo "      (see issue #1645) and keep CLAUDE.md to high-value guidance + a pointer." >&2
  exit 1
fi

echo "OK: $TARGET is $SIZE bytes (limit $LIMIT)."
exit 0
