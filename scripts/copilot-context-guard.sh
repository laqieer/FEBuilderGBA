#!/usr/bin/env bash
# Fail-open Bash wrapper for scripts/copilot_context_guard.py (issue #1995).
#
# Per the official GitHub Copilot CLI hooks reference, a `preToolUse` command
# hook is fail-CLOSED on any non-zero, non-timeout exit: exit 2 denies (with
# stdout JSON merged in), and any *other* non-zero exit also denies the tool
# call outright ("Denied by preToolUse hook (hook errored)") even if stdout
# reports allow. This wrapper exists purely to make sure that infrastructure
# failures (missing/broken Python, spawn failure, an uncaught guard crash)
# can never accidentally deny a `view` call: only a real exit-2 decision from
# the guard is allowed to propagate as a deny. Everything else becomes a
# fail-open "{}" with wrapper exit 0.
set -u

hook_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
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
  if [ -z "$output" ]; then
    printf '{}'
  else
    printf '%s' "$output"
  fi
  exit 2
fi

# Any other non-zero exit (missing interpreter path resolved but broken,
# script crash, unexpected error) must NOT propagate as a wrapper failure,
# or the fail-closed preToolUse contract would deny unrelated view() calls.
if [ "$status" -ne 0 ]; then
  printf '{}'
  exit 0
fi

if [ -z "$output" ]; then
  printf '{}'
else
  printf '%s' "$output"
fi
exit 0
