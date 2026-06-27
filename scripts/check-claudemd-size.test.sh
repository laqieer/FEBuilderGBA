#!/usr/bin/env bash
# Self-tests for check-claudemd-size.sh — runs in CI before the real gate.
# No external tooling dependencies (no PyYAML, no .NET); just Bash + coreutils.
# Bash (not /bin/sh): uses BASH_SOURCE and `set -o pipefail`.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GATE="$SCRIPT_DIR/check-claudemd-size.sh"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

fail=0
pass() { echo "PASS: $1"; }
bad()  { echo "FAIL: $1"; fail=1; }

# 1. The real CLAUDE.md must pass the gate (it was trimmed under 40k in #1645).
if bash "$GATE" "$REPO_ROOT/CLAUDE.md" >/dev/null 2>&1; then
  pass "real CLAUDE.md is within the limit"
else
  bad "real CLAUDE.md exceeds the limit (gate exited non-zero)"
fi

# 2. An oversized fixture must FAIL (exit 1) — proves the gate actually trips.
tmp_over="$(mktemp)"
# 40,001 bytes of 'x' (one over the default 40000 limit).
yes x | head -c 40001 > "$tmp_over" 2>/dev/null || head -c 40001 /dev/zero | tr '\0' 'x' > "$tmp_over"
bash "$GATE" "$tmp_over" >/dev/null 2>&1
rc=$?
if [ "$rc" -eq 1 ]; then
  pass "oversized fixture is rejected (exit 1)"
else
  bad "oversized fixture not rejected (expected exit 1, got $rc)"
fi
rm -f "$tmp_over"

# 3. A small fixture must PASS (exit 0).
tmp_ok="$(mktemp)"
printf 'small file\n' > "$tmp_ok"
if bash "$GATE" "$tmp_ok" >/dev/null 2>&1; then
  pass "small fixture is accepted (exit 0)"
else
  bad "small fixture wrongly rejected"
fi
rm -f "$tmp_ok"

# 4. A missing file must error with exit 2.
bash "$GATE" "/no/such/file/claude.md" >/dev/null 2>&1
rc=$?
if [ "$rc" -eq 2 ]; then
  pass "missing file returns usage error (exit 2)"
else
  bad "missing file expected exit 2, got $rc"
fi

# 5. CLAUDE_MD_PATH env override is honoured.
tmp_env="$(mktemp)"
printf 'env override\n' > "$tmp_env"
if CLAUDE_MD_PATH="$tmp_env" bash "$GATE" >/dev/null 2>&1; then
  pass "CLAUDE_MD_PATH env override is honoured"
else
  bad "CLAUDE_MD_PATH env override failed"
fi
rm -f "$tmp_env"

if [ "$fail" -ne 0 ]; then
  echo "check-claudemd-size self-tests FAILED"
  exit 1
fi
echo "All check-claudemd-size self-tests passed."
exit 0
