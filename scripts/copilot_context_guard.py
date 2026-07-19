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
  falling back to a private per-session directory under this user's own
  Copilot home (``$COPILOT_HOME/context-guard/<sanitized-sessionId>``,
  e.g. ``~/.copilot/context-guard/...``) when the session directory cannot
  be resolved. The fallback is never placed under a shared OS temp
  directory: a predictable path there is symlink/ownership-attackable by
  any other local user. Before creating or reusing the fallback root or
  its per-session subdirectory, each is checked to reject a pre-existing
  symlink or non-directory outright, and (POSIX, best-effort) to require
  ownership by the current effective user; any uncertainty fails open
  without touching state. Only the ``context-guard`` root and its session
  subdirectories are ever created/chmod'ed 0700 -- ``$COPILOT_HOME``
  itself is never chmod'ed. Access is guarded by an OS-kernel advisory
  file lock (POSIX ``fcntl.flock``, Windows ``msvcrt.locking``) with
  jittered retry up to a bounded timeout, then fail-open on contention;
  state updates are written atomically. Kernel locks are automatically
  released by the OS when the holding process exits or crashes, so there
  is no stale-lock window and no stale-lock deletion race to reason
  about. A missing, empty, or non-string ``sessionId`` is treated as
  uncertain input and falls through *before* any state is resolved or
  persisted -- it is never mapped to a shared, permanent fallback bucket.
"""

import json
import os
import random
import stat
import sys
import tempfile
import time

if sys.platform.startswith("win"):
    import msvcrt
    fcntl = None
else:
    import fcntl
    msvcrt = None

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
LOCK_FILE_SUFFIX = ".lock"
LOCK_MAX_WAIT_SECONDS = 2.0
LOCK_RETRY_MIN_SECONDS = 0.01
LOCK_RETRY_MAX_SECONDS = 0.05

# Private, user-owned fallback cache root, nested directly under this
# user's own Copilot home (e.g. ``~/.copilot/context-guard``) -- deliberately
# NOT under a shared OS temp directory (see _resolve_state_dir/_is_safe_
# private_dir_target docstrings for the symlink/ownership rationale).
CONTEXT_GUARD_CACHE_SUBDIR = "context-guard"

FALL_THROUGH = "{}"


def _emit(text):
    sys.stdout.write(text)
    sys.stdout.flush()


def _sanitize_session_id(session_id):
    """Reduce sessionId to a filesystem-safe token; never raises.

    The ``"unknown-session"`` fallback below is a defensive default for
    direct/programmatic callers only. ``run()`` itself always rejects a
    missing, empty, or non-string ``sessionId`` (falling through before
    ever resolving or persisting state) *before* calling this function, so
    a real invocation never reaches -- and never charges bytes against --
    this shared bucket name.
    """
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


def _is_owned_real_dir(st):
    """True if stat result ``st`` describes a real, non-symlink directory
    that this process's current effective user (POSIX only, best-effort)
    owns. Shared by both the pre-creation safety check and the
    post-creation TOCTOU re-verification below.
    """
    if stat.S_ISLNK(st.st_mode):
        return False
    if not stat.S_ISDIR(st.st_mode):
        return False
    if not sys.platform.startswith("win") and hasattr(os, "geteuid"):
        try:
            if st.st_uid != os.geteuid():
                return False
        except OSError:
            return False
    return True


def _is_safe_private_dir_target(path):
    """True if ``path`` is safe to create or reuse as a private state dir.

    Safe means either: ``path`` does not exist yet (nothing to reject, and
    it can be created fresh), or it already exists as a real directory --
    never a symlink -- that is, on POSIX where ownership can be checked,
    owned by this process's current effective user.

    Rejects (returns False) a pre-existing symlink or non-directory at
    ``path`` outright: silently following or reusing either could hand
    session-budget state to an attacker-controlled location, e.g. a
    symlink planted by another local user at a predictable path. Only a
    ``FileNotFoundError`` from ``os.lstat`` means "does not exist yet" and
    is treated as safe-to-create; every *other* ``OSError`` (permission
    denied, I/O error, etc.) means the path's true nature could not be
    verified and must never be conflated with "safe" -- this function
    fails closed (returns False, "not safe") on any such uncertainty, so
    the caller aborts instead of silently creating or reusing an
    unverifiable path.
    """
    try:
        st = os.lstat(path)
    except FileNotFoundError:
        return True  # confirmed absent: safe to create fresh
    except OSError:
        return False  # unknown/unverifiable state: never assume safe
    return _is_owned_real_dir(st)


def _prepare_private_fallback_dir(state_dir):
    """Verify then create the private per-session fallback state directory.

    ``state_dir`` must be ``<context-guard cache root>/<sanitized-sessionId>``
    (see ``_resolve_state_dir``). Both the cache root and the per-session
    subdirectory are independently checked with
    ``_is_safe_private_dir_target`` -- and, once verified, chmod'ed
    ``0700`` (POSIX-only; a no-op on Windows) -- before either is created
    or reused, since either level could be a location an attacker planted
    ahead of time at this predictable path. A ``chmod`` failure on POSIX
    is treated as fatal (returns False) rather than silently ignored: the
    private-permission guarantee cannot be honored if the mode could not
    be set, so the caller must abstain instead of using or persisting
    state in a directory whose permissions are unverified. This never
    walks or touches anything above the cache root: ``$COPILOT_HOME``/
    ``.copilot`` itself is never created here (beyond whatever
    ``os.makedirs`` needs to reach the cache root) nor ever chmod'ed.
    Returns True on success, False on any uncertainty -- the caller must
    fail open without persisting anything.

    A precheck-then-create window exists between the
    ``_is_safe_private_dir_target`` call and ``os.makedirs``:
    ``os.makedirs(..., exist_ok=True)`` treats an existing path as
    acceptable whenever ``os.path.isdir()`` (which *follows* symlinks) is
    True, so an attacker who plants a symlink-to-an-existing-directory in
    that window would make ``os.makedirs`` succeed silently without ever
    creating anything real. To close that TOCTOU gap, the path is
    re-verified with a fresh (non-symlink-following) ``os.lstat`` *after*
    ``os.makedirs`` returns and *before* any ``chmod`` or use -- a mismatch
    here aborts immediately, never chmod'ing or trusting the replaced path.
    """
    root = os.path.dirname(state_dir)
    for directory in (root, state_dir):
        if not _is_safe_private_dir_target(directory):
            return False
        try:
            os.makedirs(directory, exist_ok=True)
        except OSError:
            return False
        try:
            post_st = os.lstat(directory)
        except OSError:
            return False
        if not _is_owned_real_dir(post_st):
            return False
        if not sys.platform.startswith("win"):
            try:
                os.chmod(directory, 0o700)
            except OSError:
                # A chmod failure here means the private-permission
                # guarantee cannot be honored -- silently proceeding
                # would leave a fallback state directory whose mode is
                # unknown/unverified. Fail closed: the caller must
                # abstain rather than use or persist state in a
                # directory that isn't provably private.
                return False
    return True


def _resolve_state_dir(session_id):
    """Resolve the directory that will hold this session's guard state.

    Prefers the real Copilot session-state directory (sibling to the
    session's git worktree ``files/`` dir, so state never lands inside the
    tracked Git tree). Falls back to a private per-session directory
    nested under this user's own Copilot home
    (``$COPILOT_HOME/context-guard/<sanitized-sessionId>``), scoped per
    sanitized sessionId, when the session directory cannot be resolved.
    Deliberately never a shared OS temp directory: a predictable path
    there would be symlink/ownership-attackable by any other local user.
    ``session_id`` must already have been validated as a non-empty string
    by the caller (see ``run()``) -- this function does not itself decide
    whether an invalid sessionId should fail through. Returns
    ``(state_dir, is_fallback)``; the fallback path is not verified for
    safety here (see ``_prepare_private_fallback_dir``, which the caller
    must invoke before creating or trusting it).
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

    fallback_root = os.path.join(home, CONTEXT_GUARD_CACHE_SUBDIR)
    return os.path.join(fallback_root, safe_id), True


class _LockNotAcquired(Exception):
    pass


class _FileLock(object):
    """OS-kernel advisory file lock: POSIX ``fcntl.flock``, Windows
    ``msvcrt.locking``.

    Deliberately *not* a directory-existence lock with a stale-lock TTL: a
    getmtime-then-unconditional-rmtree stale-lock recovery scheme has an
    inherent race -- a lock holder can recreate/refresh its lock between
    another process's staleness check and its ``rmtree`` call, causing the
    "stale" cleanup to delete a lock another process is actively (and
    legitimately) holding. Kernel file locks avoid this class of bug
    entirely: the OS releases them automatically the moment the holding
    process's file descriptor is closed, whether that happens via normal
    ``__exit__``, an uncaught exception, or the process crashing/being
    killed outright -- so there is no "is this lock stale?" question to
    answer, and therefore nothing to race. The lock *file* itself is
    created once (if absent) and then left in place; only the transient
    kernel-level lock on it (not the file's existence) provides mutual
    exclusion, so leaving it behind is harmless.
    """

    def __init__(self, target_path):
        self._lock_path = target_path + LOCK_FILE_SUFFIX
        self._fh = None

    def __enter__(self):
        deadline = time.monotonic() + LOCK_MAX_WAIT_SECONDS
        while True:
            try:
                self._fh = open(self._lock_path, "a+b")
            except OSError:
                raise _LockNotAcquired()
            try:
                self._acquire_kernel_lock()
                return self
            except OSError:
                self._fh.close()
                self._fh = None
                if time.monotonic() >= deadline:
                    raise _LockNotAcquired()
                time.sleep(random.uniform(LOCK_RETRY_MIN_SECONDS, LOCK_RETRY_MAX_SECONDS))

    def _acquire_kernel_lock(self):
        fd = self._fh.fileno()
        if sys.platform.startswith("win"):
            if msvcrt is None:
                raise OSError("msvcrt unavailable")
            # msvcrt.locking locks byte ranges starting at the current file
            # position; the file needs at least one byte to lock byte 0.
            if os.fstat(fd).st_size == 0:
                try:
                    self._fh.write(b"\0")
                    self._fh.flush()
                except OSError:
                    pass
            self._fh.seek(0)
            msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
        else:
            if fcntl is None:
                raise OSError("fcntl unavailable")
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)

    def _release_kernel_lock(self):
        fd = self._fh.fileno()
        if sys.platform.startswith("win"):
            if msvcrt is not None:
                self._fh.seek(0)
                msvcrt.locking(fd, msvcrt.LK_UNLCK, 1)
        elif fcntl is not None:
            fcntl.flock(fd, fcntl.LOCK_UN)

    def __exit__(self, exc_type, exc_val, exc_tb):
        if self._fh is not None:
            try:
                self._release_kernel_lock()
            except OSError:
                pass
            finally:
                self._fh.close()
                self._fh = None
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
    """Build a per-file identity fingerprint, case-folding only where safely
    proven.

    This fingerprint identifies *which* file/version was read for metadata
    purposes only (``read_count``, ``last_seen``, latest observed size). It
    is NOT a charging dedupe key: every authorized read -- including a
    repeat read of a file with an unchanged fingerprint -- adds its ``size``
    to the session's cumulative ``total_bytes``, because a repeated ``view``
    of the same image re-adds its bytes to the conversation history again.

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
    if not isinstance(session_id, str) or not session_id:
        # Missing/empty/non-string sessionId is uncertain input: fail open
        # before ever resolving or persisting state, so a real run never
        # falls back to a shared, permanent "unknown-session" bucket.
        return FALL_THROUGH, 0

    state_dir, is_fallback = _resolve_state_dir(session_id)
    state_path = os.path.join(state_dir, STATE_FILE_NAME)

    if is_fallback:
        if not _prepare_private_fallback_dir(state_dir):
            return FALL_THROUGH, 0
    else:
        try:
            os.makedirs(state_dir, exist_ok=True)
        except OSError:
            return FALL_THROUGH, 0

    try:
        with _FileLock(state_path):
            state, was_corrupt = _load_state(state_path)
            if was_corrupt:
                # Uncertain state: fail open without persisting anything.
                return FALL_THROUGH, 0

            entries = state["entries"]

            # Every authorized read charges its bytes against the
            # cumulative budget, including a repeat read of a file whose
            # fingerprint (path+size+mtime) is unchanged: a repeated
            # view() re-adds the same image content/reference to the
            # conversation history again, so deduping the *charge* here
            # would let unlimited repeated reads bypass the budget
            # entirely. The fingerprint is kept only for metadata
            # (read_count / last_seen / latest observed size), never to
            # skip charging.
            budget = _budget_bytes()
            new_total = state["total_bytes"] + size
            if new_total > budget:
                reason = (
                    "Cumulative tool-authorized image bytes for this session "
                    "would reach {0}, exceeding the {1}-byte context budget "
                    "(issue #1995). Denying this view() read to avoid a CAPI "
                    "5 MB request overflow.".format(new_total, budget)
                )
                # A denied read must not mutate state: neither total_bytes
                # nor the fingerprint's metadata is touched.
                return json.dumps({
                    "permissionDecision": "deny",
                    "permissionDecisionReason": reason,
                }), 2

            existing = entries.get(fingerprint)
            if existing is not None:
                existing["size"] = size
                existing["last_seen"] = time.time()
                existing["read_count"] = existing.get("read_count", 1) + 1
            else:
                entries[fingerprint] = {
                    "size": size,
                    "last_seen": time.time(),
                    "read_count": 1,
                }
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
