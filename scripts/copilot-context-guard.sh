#!/usr/bin/env bash
# Fail-open Bash wrapper for scripts/copilot_context_guard.py (issue #1995).
#
# Per the official GitHub Copilot CLI hooks reference, a `preToolUse` command
# hook is fail-CLOSED on any non-zero, non-timeout exit: exit 2 denies (with
# stdout JSON merged in), and any *other* non-zero exit also denies the tool
# call outright ("Denied by preToolUse hook (hook errored)") even if stdout
# reports allow. This wrapper exists purely to make sure that infrastructure
# failures (missing/broken Python, spawn failure, an uncaught guard crash --
# INCLUDING CPython's own exit code 2 for a missing/unreadable script file)
# can never accidentally deny a `view` call: only a *validated* exit-2
# decision from the guard -- stdout is a JSON object with
# permissionDecision == "deny" and a non-empty permissionDecisionReason -- is
# allowed to propagate as a deny. Every other outcome (any non-zero exit, or
# a normal exit 0) becomes the fixed fail-open "{}" literal with wrapper
# exit 0 -- child stdout on a normal exit 0 is never forwarded verbatim,
# since the guard's only legitimate exit-0 decision is abstention.
set -u

script_path="${BASH_SOURCE[0]}"
case "$script_path" in
  */*) script_dir="${script_path%/*}" ;;
  *) script_dir="." ;;
esac
# Resolve to an absolute path using only the `cd`/`pwd` shell builtins --
# deliberately never the external `dirname` binary, so this wrapper keeps
# working (and, just as importantly, keeps failing open deterministically
# in tests) even when PATH is minimal/empty and no external coreutils are
# resolvable.
hook_dir="$(cd "$script_dir" >/dev/null 2>&1 && pwd)"
guard="$hook_dir/copilot_context_guard.py"

python_bin=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1; then
    python_bin="$candidate"
    break
  fi
done

if [ -z "$python_bin" ]; then
  printf '{}'
  exit 0
fi

output="$("$python_bin" "$guard" 2>/dev/null)"
status=$?

if [ "$status" -eq 2 ]; then
  # CPython itself exits 2 for infrastructure failures unrelated to a real
  # deny decision (e.g. "python3: can't open file '<guard>': [Errno 2] No
  # such file or directory" for a missing/unreadable guard script). Only
  # propagate exit 2 when stdout is genuinely a JSON object with
  # permissionDecision == "deny" and a non-empty permissionDecisionReason;
  # otherwise this is an infrastructure failure, not a deny, and must fail
  # open like every other non-exit-2 failure mode.
  if printf '%s' "$output" | "$python_bin" -c '
import json, sys

try:
    decision = json.loads(sys.stdin.read())
except Exception:
    sys.exit(1)

if not isinstance(decision, dict):
    sys.exit(1)
if decision.get("permissionDecision") != "deny":
    sys.exit(1)
reason = decision.get("permissionDecisionReason")
if not isinstance(reason, str) or not reason:
    sys.exit(1)
sys.exit(0)
' 2>/dev/null; then
    printf '%s' "$output"
    exit 2
  fi
  printf '{}'
  exit 0
fi

# Any other non-zero exit (missing interpreter path resolved but broken,
# script crash, unexpected error) must NOT propagate as a wrapper failure,
# or the fail-closed preToolUse contract would deny unrelated view() calls.
if [ "$status" -ne 0 ]; then
  printf '{}'
  exit 0
fi

# The guard's only legitimate exit-0 decision is abstention: every code
# path in copilot_context_guard.py that is not a definitive, validated
# exit-2 deny prints exactly "{}" and exits 0. Never forward the child's
# raw stdout here -- doing so would let a corrupted/partial/arbitrary
# stdout payload on a normal exit (e.g. truncated output, a stray print,
# a future regression) masquerade as a decision object. Always emit the
# fixed abstention literal instead of trusting/echoing child stdout.
printf '{}'
exit 0
