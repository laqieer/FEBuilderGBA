#!/usr/bin/env bash
# Self-tests for check-copilot-context-size.sh.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GATE="$SCRIPT_DIR/check-copilot-context-size.sh"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

fail=0
pass() { echo "PASS: $1"; }
bad()  { echo "FAIL: $1"; fail=1; }

if bash "$GATE" "$REPO_ROOT" >/dev/null 2>&1; then
  pass "repository context is within budget"
else
  bad "repository context violates the budget"
fi

make_fixture() {
  local root
  root="$(mktemp -d)"
  mkdir -p "$root/.github"
  printf 'small instructions\n' > "$root/.github/copilot-instructions.md"
  echo "$root"
}

fixture="$(make_fixture)"
if bash "$GATE" "$fixture" >/dev/null 2>&1; then
  pass "small instruction set is accepted"
else
  bad "small instruction set was rejected"
fi
rm -rf "$fixture"

fixture="$(make_fixture)"
head -c 12288 /dev/zero | tr '\0' 'x' > "$fixture/.github/copilot-instructions.md"
if bash "$GATE" "$fixture" >/dev/null 2>&1; then
  pass "instruction set at the byte limit is accepted"
else
  bad "instruction set at the byte limit was rejected"
fi
rm -rf "$fixture"

fixture="$(make_fixture)"
head -c 12289 /dev/zero | tr '\0' 'x' > "$fixture/.github/copilot-instructions.md"
bash "$GATE" "$fixture" >/dev/null 2>&1
rc=$?
if [ "$rc" -eq 1 ]; then
  pass "instruction set above the byte limit is rejected"
else
  bad "over-limit fixture expected exit 1, got $rc"
fi
rm -rf "$fixture"

fixture="$(make_fixture)"
printf 'legacy instructions\n' > "$fixture/CLAUDE.md"
bash "$GATE" "$fixture" >/dev/null 2>&1
rc=$?
if [ "$rc" -eq 2 ]; then
  pass "legacy always-loaded instruction file is rejected"
else
  bad "legacy instruction fixture expected exit 2, got $rc"
fi
rm -rf "$fixture"

fixture="$(make_fixture)"
if COPILOT_CONTEXT_ROOT="$fixture" bash "$GATE" >/dev/null 2>&1; then
  pass "COPILOT_CONTEXT_ROOT override is honoured"
else
  bad "COPILOT_CONTEXT_ROOT override failed"
fi
rm -rf "$fixture"

if [ "$fail" -ne 0 ]; then
  echo "check-copilot-context-size self-tests FAILED"
  exit 1
fi

echo "All check-copilot-context-size self-tests passed."
exit 0
