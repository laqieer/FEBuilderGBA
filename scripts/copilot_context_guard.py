#!/usr/bin/env python3
"""Fail-open ``preToolUse`` context-budget guard for GitHub Copilot CLI.

Purpose (issue #1995)
----------------------
Copilot CLI compacts context by *token* utilization, but the CAPI backend
rejects serialized requests above a fixed 5 MB *byte* ceiling. Repeated large
``view`` reads of image files can accumulate bytes far faster than tokens,
crossing that ceiling before compaction ever triggers. This script is invoked
as a repository ``preToolUse`` command hook (see
``.github/hooks/copilot-context-budget.json``) to enforce a conservative,
best-effort cumulative byte budget on *image* files read through the ``view``
tool, denying reads that would push the session over budget.

Contract (see README.md's "Context safety" section and the plan accepted on
issue #1995 for the full rationale):

* Falls through (prints ``{}`` and exits 0) for every tool other than
  ``view``, for missing/non-image paths, and for every uncertain or
  infrastructure failure (malformed stdin, unreadable file, corrupt state,
  lock contention, unexpected exceptions). It NEVER emits a forced
  ``permissionDecision: allow`` -- it only ever abstains (``{}``) or denies.
* Only a *definitive* cumulative-budget overflow denies: it prints a JSON
  object with ``permissionDecision: "deny"`` and a non-empty
  ``permissionDecisionReason``, then exits with status 2.
* Per the official hooks reference, a ``preToolUse`` command hook is
  fail-closed on *any* non-zero, non-timeout exit (including exit 2): the
  tool call is denied even if stdout reports ``allow``. That means every
  code path in this script that is not a definitive deny MUST exit 0 -- an
  uncaught exception here would silently deny unrelated ``view`` calls.
  The ``main()`` wrapper below guarantees that with a catch-all fail-open.
* State is never read from the runtime's ``events.jsonl`` history. It is
  stored outside the Git worktree: preferably beside the resolved Copilot
  session directory (``~/.copilot/session-state/<sessionId>/hook-state/``),
  falling back to the OS temp directory when the session directory cannot be
  resolved. Access is guarded by an atomic directory lock with jittered
  retry and stale-lock recovery; state updates are written atomically.
"""

import json
import os
import random
import shutil
import sys
import tempfile
import time

# --- Tunables -----------------------------------------------------------

DEFAULT_BUDGET_BYTES = 1_250_000
BUDGET_ENV_VAR = "COPILOT_CONTEXT_GUARD_BUDGET_BYTES"

# Test-only override: redirects where guard state is stored so tests never
# touch a real user's ~/.copilot directory. Not part of the user-facing
# contract; unset in normal operation.
STATE_DIR_OVERRIDE_ENV_VAR = "COPILOT_CONTEXT_GUARD_STATE_DIR"

SUPPORTED_IMAGE_EXTENSIONS = {
    ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico",
    ".heic", ".heif",
}

STATE_FILE_NAME = "context-budget.json"
LOCK_DIR_SUFFIX = ".lock"
STALE_LOCK_SECONDS = 15
LOCK_MAX_WAIT_SECONDS = 2.0
LOCK_RETRY_MIN_SECONDS = 0.01
LOCK_RETRY_MAX_SECONDS = 0.05
TEMP_STATE_TTL_SECONDS = 7 * 24 * 60 * 60  # one week
FALLBACK_SUBDIR_NAME = "copilot-context-guard"

FALL_THROUGH = "{}"


def _emit(text):
    sys.stdout.write(text)
    sys.stdout.flush()


def _sanitize_session_id(session_id):
    """Reduce sessionId to a filesystem-safe token; never raises."""
    if not isinstance(session_id, str) or not session_id:
        return "unknown-session"
    safe = "".join(ch if (ch.isalnum() or ch in "-_") else "_" for ch in session_id)
    safe = safe.strip("._") or "unknown-session"
    return safe[:128]


def _copilot_home():
    override = os.environ.get("COPILOT_HOME")
    if override:
        return override
    if sys.platform.startswith("win"):
        base = os.environ.get("USERPROFILE") or os.path.expanduser("~")
    else:
        base = os.path.expanduser("~")
    return os.path.join(base, ".copilot")


def _current_user_token():
    """Best-effort per-user token used to partition the OS-temp fallback dir.

    Prevents unrelated local users sharing a multi-user temp directory
    (e.g. POSIX ``/tmp``) from colliding on -- or reading -- the same
    fallback state path. Never raises; falls back to a fixed token if no
    usable identity is found.
    """
    try:
        if hasattr(os, "getuid"):
            return "uid-{0}".format(os.getuid())
    except Exception:
        pass
    for var in ("USERNAME", "USER", "LOGNAME"):
        val = os.environ.get(var)
        if val:
            return "user-{0}".format(_sanitize_session_id(val))
    return "user-unknown"


def _makedirs_private(path, stop_at):
    """Create ``path`` (and parents) then best-effort chmod ``0700`` every
    directory component from ``path`` up to and including ``stop_at``.

    POSIX-only tightening (a no-op on Windows, where the same owner-only
    semantics don't apply); bounded to ``stop_at`` so we never touch
    directories we don't own the creation of (e.g. the OS temp root
    itself). Returns True if ``path`` exists (created or already present)
    after this call, False if directory creation failed outright.
    """
    try:
        os.makedirs(path, exist_ok=True)
    except OSError:
        return False
    if not sys.platform.startswith("win"):
        stop_norm = os.path.normpath(stop_at)
        current = os.path.normpath(path)
        while True:
            try:
                os.chmod(current, 0o700)
            except OSError:
                pass
            if current == stop_norm:
                break
            parent = os.path.dirname(current)
            if not parent or parent == current:
                break
            current = parent
    return True


def _resolve_state_dir(session_id):
    """Resolve the directory that will hold this session's guard state.

    Prefers the real Copilot session-state directory (sibling to the
    session's git worktree ``files/`` dir, so state never lands inside the
    tracked Git tree). Falls back to an OS temp/cache directory, partitioned
    per sanitized sessionId **and** per current user/UID (so unrelated local
    users sharing a multi-user temp directory never collide on the same
    path), when the session directory cannot be resolved. Returns
    (state_dir, is_fallback).
    """
    override = os.environ.get(STATE_DIR_OVERRIDE_ENV_VAR)
    if override:
        return override, False

    safe_id = _sanitize_session_id(session_id)
    home = _copilot_home()
    session_dir = os.path.join(home, "session-state", safe_id)
    try:
        if os.path.isdir(session_dir):
            return os.path.join(session_dir, "hook-state"), False
    except OSError:
        pass

    fallback_root = os.path.join(
        tempfile.gettempdir(), FALLBACK_SUBDIR_NAME, _current_user_token()
    )
    return os.path.join(fallback_root, safe_id), True


def _cleanup_stale_temp_state(fallback_root):
    """Best-effort TTL cleanup of the OS temp/cache fallback directory.

    Never raises; any failure is silently ignored since this is purely
    housekeeping and must not affect the current decision.
    """
    try:
        if not os.path.isdir(fallback_root):
            return
        now = time.time()
        for entry in os.listdir(fallback_root):
            entry_path = os.path.join(fallback_root, entry)
            try:
                mtime = os.path.getmtime(entry_path)
                if (now - mtime) > TEMP_STATE_TTL_SECONDS:
                    if os.path.isdir(entry_path):
                        shutil.rmtree(entry_path, ignore_errors=True)
                    else:
                        os.remove(entry_path)
            except OSError:
                continue
    except Exception:
        pass


class _LockNotAcquired(Exception):
    pass


class _DirectoryLock(object):
    """Atomic directory-based lock with jittered retry and stale recovery.

    Directory creation (``os.mkdir``) is atomic on every platform this
    script targets (POSIX and Windows), which is why it is used as the lock
    primitive instead of a lock file.
    """

    def __init__(self, target_path):
        self._lock_dir = target_path + LOCK_DIR_SUFFIX
        self._acquired = False

    def __enter__(self):
        deadline = time.monotonic() + LOCK_MAX_WAIT_SECONDS
        while True:
            try:
                os.mkdir(self._lock_dir)
                self._acquired = True
                return self
            except FileExistsError:
                self._maybe_break_stale_lock()
            except OSError:
                # Cannot create the lock (e.g. parent dir missing/denied).
                raise _LockNotAcquired()
            if time.monotonic() >= deadline:
                raise _LockNotAcquired()
            time.sleep(random.uniform(LOCK_RETRY_MIN_SECONDS, LOCK_RETRY_MAX_SECONDS))

    def _maybe_break_stale_lock(self):
        try:
            age = time.time() - os.path.getmtime(self._lock_dir)
            if age > STALE_LOCK_SECONDS:
                shutil.rmtree(self._lock_dir, ignore_errors=True)
        except OSError:
            pass

    def __exit__(self, exc_type, exc_val, exc_tb):
        if self._acquired:
            shutil.rmtree(self._lock_dir, ignore_errors=True)
        return False


def _load_state(state_path):
    """Load persisted state; returns a fresh state dict on any problem."""
    fresh = {"entries": {}, "total_bytes": 0}
    try:
        with open(state_path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
        if not isinstance(data, dict):
            return fresh, True
        entries = data.get("entries")
        total_bytes = data.get("total_bytes")
        if not isinstance(entries, dict) or not isinstance(total_bytes, int):
            return fresh, True
        return {"entries": entries, "total_bytes": total_bytes}, False
    except FileNotFoundError:
        return fresh, False
    except (OSError, ValueError):
        # Corrupt/unreadable state: fail open, treat as "uncertain", and
        # never overwrite the existing (corrupt) file from this path.
        return fresh, True


def _save_state_atomically(state_dir, state_path, state):
    os.makedirs(state_dir, exist_ok=True)
    fd, tmp_path = tempfile.mkstemp(prefix=".tmp-", dir=state_dir)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(state, fh)
        os.replace(tmp_path, state_path)
    finally:
        if os.path.exists(tmp_path):
            try:
                os.remove(tmp_path)
            except OSError:
                pass


def _normalized_fingerprint(abs_path, size, mtime_ns):
    """Build a dedupe fingerprint, case-folding only where safely proven.

    Windows filesystems (NTFS/ReFS in their default configuration) are
    case-insensitive, so folding case there prevents undercounting the same
    file read via two different-case paths. macOS (APFS/HFS+) volumes are
    *usually* case-insensitive by default but can be formatted
    case-sensitive, and POSIX/Linux filesystems are case-sensitive by
    default -- folding case there risks treating two genuinely distinct
    files as the same one and undercounting real bytes read. Conservative
    double-counting (treating same-content-different-case paths as
    distinct on non-Windows) is preferable to that undercounting, so case
    is folded on Windows only.
    """
    normalized = os.path.normpath(abs_path)
    if sys.platform.startswith("win"):
        normalized = normalized.lower()
    return "{0}|{1}|{2}".format(normalized, size, mtime_ns)


def _parse_tool_args(tool_args):
    """Accept toolArgs as either a real object or a JSON-encoded string."""
    if isinstance(tool_args, dict):
        return tool_args
    if isinstance(tool_args, str):
        try:
            parsed = json.loads(tool_args)
        except ValueError:
            return None
        return parsed if isinstance(parsed, dict) else None
    return None


def _budget_bytes():
    raw = os.environ.get(BUDGET_ENV_VAR)
    if raw:
        try:
            value = int(raw)
            if value > 0:
                return value
        except ValueError:
            pass
    return DEFAULT_BUDGET_BYTES


def run(stdin_text):
    """Core decision logic. Returns (stdout_text, exit_code)."""
    try:
        payload = json.loads(stdin_text) if stdin_text else {}
    except ValueError:
        return FALL_THROUGH, 0
    if not isinstance(payload, dict):
        return FALL_THROUGH, 0

    if payload.get("toolName") != "view":
        return FALL_THROUGH, 0

    tool_args = _parse_tool_args(payload.get("toolArgs"))
    if tool_args is None:
        return FALL_THROUGH, 0

    path = tool_args.get("path")
    if not isinstance(path, str) or not path:
        return FALL_THROUGH, 0

    ext = os.path.splitext(path)[1].lower()
    if ext not in SUPPORTED_IMAGE_EXTENSIONS:
        return FALL_THROUGH, 0

    cwd = payload.get("cwd")
    abs_path = path if os.path.isabs(path) else os.path.join(cwd or "", path)
    abs_path = os.path.abspath(abs_path)

    try:
        st = os.stat(abs_path)
    except OSError:
        return FALL_THROUGH, 0

    size = st.st_size
    mtime_ns = getattr(st, "st_mtime_ns", int(st.st_mtime * 1e9))
    fingerprint = _normalized_fingerprint(abs_path, size, mtime_ns)

    session_id = payload.get("sessionId")
    state_dir, is_fallback = _resolve_state_dir(session_id)
    state_path = os.path.join(state_dir, STATE_FILE_NAME)

    if is_fallback:
        _cleanup_stale_temp_state(os.path.dirname(state_dir))
        fallback_anchor = os.path.join(tempfile.gettempdir(), FALLBACK_SUBDIR_NAME)
        if not _makedirs_private(state_dir, fallback_anchor):
            return FALL_THROUGH, 0
    else:
        try:
            os.makedirs(state_dir, exist_ok=True)
        except OSError:
            return FALL_THROUGH, 0

    try:
        with _DirectoryLock(state_path):
            state, was_corrupt = _load_state(state_path)
            if was_corrupt:
                # Uncertain state: fail open without persisting anything.
                return FALL_THROUGH, 0

            entries = state["entries"]
            if fingerprint in entries:
                entries[fingerprint]["last_seen"] = time.time()
                _save_state_atomically(state_dir, state_path, state)
                return FALL_THROUGH, 0

            budget = _budget_bytes()
            new_total = state["total_bytes"] + size
            if new_total > budget:
                reason = (
                    "Cumulative tool-authorized image bytes for this session "
                    "would reach {0}, exceeding the {1}-byte context budget "
                    "(issue #1995). Denying this view() read to avoid a CAPI "
                    "5 MB request overflow.".format(new_total, budget)
                )
                return json.dumps({
                    "permissionDecision": "deny",
                    "permissionDecisionReason": reason,
                }), 2

            entries[fingerprint] = {"size": size, "last_seen": time.time()}
            state["total_bytes"] = new_total
            _save_state_atomically(state_dir, state_path, state)
            return FALL_THROUGH, 0
    except _LockNotAcquired:
        return FALL_THROUGH, 0


def main():
    try:
        stdin_text = sys.stdin.read()
    except Exception:
        _emit(FALL_THROUGH)
        return 0

    try:
        output, code = run(stdin_text)
    except Exception:
        # Catch-all: this script must never crash. An uncaught exception
        # here would be interpreted by the runtime as a fail-closed deny
        # of the tool call (see module docstring).
        _emit(FALL_THROUGH)
        return 0

    _emit(output if output else FALL_THROUGH)
    return code


if __name__ == "__main__":
    sys.exit(main())
