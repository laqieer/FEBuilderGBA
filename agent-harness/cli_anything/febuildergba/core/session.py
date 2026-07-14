"""Stateful session management for FEBuilderGBA CLI.

Tracks the currently loaded ROM, version, and operation history
across multiple CLI invocations via a JSON session file.
"""

import copy
import errno
import json
import math
import os
import tempfile
import time
import uuid
from contextlib import contextmanager
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Optional


MAX_HISTORY_ENTRIES = 100
MAX_HISTORY_COLLECTION_ITEMS = 100
MAX_HISTORY_DETAIL_FIELDS = MAX_HISTORY_COLLECTION_ITEMS - 2
MAX_HISTORY_KEY_LEN = 4096
MAX_HISTORY_OP_LEN = 128
MAX_HISTORY_STRING_LEN = 4096
MAX_HISTORY_VALUE_DEPTH = 16
MAX_SESSION_FILE_BYTES = 8 * 1024 * 1024
MAX_SESSION_INTEGER_DIGITS = 20
MAX_SESSION_ID_LEN = 64
MAX_SESSION_PATH_LEN = 4096
MAX_SESSION_ROM_SIZE = 0xFFFFFFFF
MAX_SESSION_TIMESTAMP = 10_000_000_000
MAX_SESSION_VERSION_LEN = 64
SESSION_LOCK_TIMEOUT_SECONDS = 5.0
SESSION_LOCK_RETRY_SECONDS = 0.05
HISTORY_OP_DATA_EXPORT = "data_export"
HISTORY_OP_DATA_IMPORT = "data_import"
HISTORY_OP_IMPORT_PALETTE = "import_palette"


def _parse_session_int(value: str) -> int:
    signless = value[1:] if value.startswith("-") else value
    if len(signless) > MAX_SESSION_INTEGER_DIGITS:
        raise ValueError("Session integer exceeds digit limit")
    return int(value)


def _parse_session_float(value: str) -> float:
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError("Session float must be finite")
    return parsed


def _reject_session_constant(value: str):
    raise ValueError(f"Non-standard JSON constant: {value}")


def _valid_string(value, max_len: int, allow_empty: bool = True) -> bool:
    return (
        isinstance(value, str)
        and len(value) <= max_len
        and (allow_empty or bool(value))
    )


def _nonnegative_int(value, max_value: int, default: int = 0) -> int:
    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or value < 0
        or value > max_value
    ):
        return default
    return value


def _nonnegative_number(value, max_value: float, default: float = 0.0):
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or value < 0
        or value > max_value
        or (isinstance(value, float) and not math.isfinite(value))
    ):
        return default
    return value


def _normalize_history_value(value, root_item_limit=MAX_HISTORY_COLLECTION_ITEMS):
    """Copy one JSON-compatible value while enforcing session-history bounds."""
    root = [None]
    active_containers = set()
    stack = [("visit", value, root, 0, 0, root_item_limit)]

    while stack:
        action, current, parent, slot, depth, item_limit = stack.pop()
        if action == "leave":
            active_containers.remove(current)
            continue

        if depth > MAX_HISTORY_VALUE_DEPTH:
            raise ValueError("Session history value exceeds nesting limit")

        if current is None or isinstance(current, bool):
            parent[slot] = current
            continue
        if isinstance(current, int):
            if abs(current) >= 10 ** MAX_SESSION_INTEGER_DIGITS:
                raise ValueError("Session history integer exceeds digit limit")
            parent[slot] = current
            continue
        if isinstance(current, float):
            if not math.isfinite(current):
                raise ValueError("Session history float must be finite")
            parent[slot] = current
            continue
        if isinstance(current, str):
            if len(current) > MAX_HISTORY_STRING_LEN:
                raise ValueError("Session history string exceeds length limit")
            parent[slot] = current
            continue

        if isinstance(current, dict):
            if len(current) > item_limit:
                raise ValueError("Session history object exceeds field limit")
            items = list(current.items())
            for key, _ in items:
                if not _valid_string(key, MAX_HISTORY_KEY_LEN):
                    raise ValueError(
                        "Session history object keys must be bounded strings")
            container_id = id(current)
            if container_id in active_containers:
                raise ValueError("Session history value contains a cycle")
            active_containers.add(container_id)
            normalized = {}
            parent[slot] = normalized
            stack.append(("leave", container_id, None, None, 0, 0))
            for key, child in reversed(items):
                stack.append((
                    "visit",
                    child,
                    normalized,
                    key,
                    depth + 1,
                    MAX_HISTORY_COLLECTION_ITEMS,
                ))
            continue

        if isinstance(current, (list, tuple)):
            if len(current) > item_limit:
                raise ValueError("Session history array exceeds item limit")
            container_id = id(current)
            if container_id in active_containers:
                raise ValueError("Session history value contains a cycle")
            active_containers.add(container_id)
            normalized = [None] * len(current)
            parent[slot] = normalized
            stack.append(("leave", container_id, None, None, 0, 0))
            for index in range(len(current) - 1, -1, -1):
                stack.append((
                    "visit",
                    current[index],
                    normalized,
                    index,
                    depth + 1,
                    MAX_HISTORY_COLLECTION_ITEMS,
                ))
            continue

        raise ValueError("Session history values must be JSON-compatible")

    return root[0]


def _normalize_persisted_history_entry(entry):
    if not isinstance(entry, dict):
        return None
    try:
        normalized = _normalize_history_value(entry)
    except ValueError:
        return None
    if not _valid_string(
            normalized.get("op"), MAX_HISTORY_OP_LEN, allow_empty=False):
        return None
    return normalized


@dataclass
class SessionState:
    """Persistent session state."""
    session_id: str = ""
    rom_path: str = ""
    rom_version: str = ""
    rom_size: int = 0
    force_version: str = ""
    created_at: float = 0.0
    updated_at: float = 0.0
    history: list[dict] = field(default_factory=list)
    modified: bool = False

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "SessionState":
        if not isinstance(data, dict):
            return cls()
        history = data.get("history", [])
        if not isinstance(history, list):
            history = []
        normalized_history = []
        for entry in reversed(history):
            normalized = _normalize_persisted_history_entry(entry)
            if normalized is not None:
                normalized_history.append(normalized)
                if len(normalized_history) == MAX_HISTORY_ENTRIES:
                    break
        history = list(reversed(normalized_history))

        rom_path = data.get("rom_path", "")
        if not _valid_string(rom_path, MAX_SESSION_PATH_LEN):
            rom_path = ""
        session_id = data.get("session_id", "")
        if not _valid_string(session_id, MAX_SESSION_ID_LEN):
            session_id = ""
        rom_version = data.get("rom_version", "")
        if not _valid_string(rom_version, MAX_SESSION_VERSION_LEN):
            rom_version = ""
        force_version = data.get("force_version", "")
        if not _valid_string(force_version, MAX_SESSION_VERSION_LEN):
            force_version = ""
        modified = data.get("modified", False)
        if not isinstance(modified, bool):
            modified = False

        return cls(
            session_id=session_id,
            rom_path=rom_path,
            rom_version=rom_version,
            rom_size=_nonnegative_int(
                data.get("rom_size", 0),
                MAX_SESSION_ROM_SIZE,
            ),
            force_version=force_version,
            created_at=_nonnegative_number(
                data.get("created_at", 0.0),
                MAX_SESSION_TIMESTAMP,
            ),
            updated_at=_nonnegative_number(
                data.get("updated_at", 0.0),
                MAX_SESSION_TIMESTAMP,
            ),
            history=history,
            modified=modified,
        )


def _default_session_dir() -> Path:
    """Default session directory."""
    return Path.home() / ".cli-anything-febuildergba" / "sessions"


class Session:
    """Manages persistent session state."""

    def __init__(self, session_path: Optional[str] = None):
        if session_path:
            self.path = Path(session_path)
        else:
            self.path = _default_session_dir() / "default.json"
        self.lock_path = Path(f"{self.path}.lock")
        self._transaction_active = False
        self.state = SessionState()
        if self.path.exists():
            self.refresh()

    @staticmethod
    def _lock_is_contended(exc: OSError) -> bool:
        return (
            exc.errno in (errno.EACCES, errno.EAGAIN)
            or getattr(exc, "winerror", None) in (33, 36)
        )

    def _try_acquire_lock(self, lock_file) -> bool:
        lock_file.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(lock_file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(
                    lock_file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB,
                )
        except OSError as exc:
            if self._lock_is_contended(exc):
                return False
            raise
        return True

    def _acquire_lock(self):
        os.makedirs(self.lock_path.parent, exist_ok=True)
        lock_file = open(self.lock_path, "a+b")
        acquired = False
        try:
            lock_file.seek(0, os.SEEK_END)
            if lock_file.tell() == 0:
                lock_file.write(b"\0")
                lock_file.flush()
            deadline = time.monotonic() + SESSION_LOCK_TIMEOUT_SECONDS
            while not self._try_acquire_lock(lock_file):
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("Timed out waiting for session lock")
                time.sleep(min(SESSION_LOCK_RETRY_SECONDS, remaining))
            acquired = True
            return lock_file
        finally:
            if not acquired:
                lock_file.close()

    @staticmethod
    def _unlock(lock_file) -> None:
        lock_file.seek(0)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(lock_file.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)

    @contextmanager
    def _transaction(self, reload_state: bool = True):
        if self._transaction_active:
            raise RuntimeError("Nested session transaction")
        self._transaction_active = True
        lock_file = None
        try:
            lock_file = self._acquire_lock()
            if reload_state:
                self._load_unlocked()
            snapshot = copy.deepcopy(self.state)
            try:
                yield
            except BaseException:
                self.state = snapshot
                raise
        finally:
            try:
                if lock_file is not None:
                    self._unlock(lock_file)
            finally:
                if lock_file is not None:
                    lock_file.close()
                self._transaction_active = False

    def _load_unlocked(self):
        try:
            with open(self.path, "rb") as f:
                content = f.read(MAX_SESSION_FILE_BYTES + 1)
        except FileNotFoundError:
            self.state = SessionState()
            return
        try:
            if len(content) > MAX_SESSION_FILE_BYTES:
                self.state = SessionState()
                return
            data = json.loads(
                content,
                parse_int=_parse_session_int,
                parse_float=_parse_session_float,
                parse_constant=_reject_session_constant,
            )
        except (ValueError, RecursionError):
            self.state = SessionState()
            return
        self.state = SessionState.from_dict(data)

    def _write_unlocked(self):
        self.state.updated_at = time.time()
        os.makedirs(self.path.parent, exist_ok=True)
        descriptor, temp_path = tempfile.mkstemp(
            prefix=f".{self.path.name}.",
            suffix=".tmp",
            dir=self.path.parent,
        )
        stream = None
        try:
            stream = os.fdopen(descriptor, "w", encoding="utf-8")
            descriptor = None
            json.dump(self.state.to_dict(), stream, indent=2, allow_nan=False)
            stream.flush()
            os.fsync(stream.fileno())
            stream.close()
            stream = None
            os.replace(temp_path, self.path)
            temp_path = None
        finally:
            if stream is not None:
                stream.close()
            elif descriptor is not None:
                os.close(descriptor)
            if temp_path is not None:
                try:
                    os.unlink(temp_path)
                except FileNotFoundError:
                    pass

    def refresh(self):
        with self._transaction(reload_state=False):
            self._load_unlocked()

    def open_rom(self, rom_path: str, version: str = "", rom_size: int = 0,
                 force_version: str = ""):
        if not _valid_string(rom_path, MAX_SESSION_PATH_LEN, allow_empty=False):
            raise ValueError("ROM path must be a non-empty bounded string")
        if not _valid_string(version, MAX_SESSION_VERSION_LEN):
            raise ValueError("ROM version must be a bounded string")
        if not _valid_string(force_version, MAX_SESSION_VERSION_LEN):
            raise ValueError("Force version must be a bounded string")
        if (
            isinstance(rom_size, bool)
            or not isinstance(rom_size, int)
            or rom_size < 0
            or rom_size > MAX_SESSION_ROM_SIZE
        ):
            raise ValueError("ROM size must be a bounded non-negative integer")

        abs_rom_path = os.path.abspath(rom_path)
        if len(abs_rom_path) > MAX_SESSION_PATH_LEN:
            raise ValueError("Absolute ROM path exceeds the session path limit")

        with self._transaction():
            self.state = SessionState(
                session_id=uuid.uuid4().hex,
                rom_path=abs_rom_path,
                rom_version=version,
                rom_size=rom_size,
                force_version=force_version,
                created_at=time.time(),
            )
            self._add_history("open", {"rom": rom_path, "version": version})
            self._write_unlocked()

    def _generation_token(self):
        if not self.is_open():
            return None
        normalized_path = os.path.normcase(os.path.abspath(self.state.rom_path))
        if _valid_string(
                self.state.session_id, MAX_SESSION_ID_LEN, allow_empty=False):
            return ("session_id", self.state.session_id, normalized_path)
        return ("legacy", self.state.created_at, normalized_path)

    def record_operation(self, op: str, details: dict = None, *, modified: bool = False):
        token = self._generation_token()
        with self._transaction():
            if token is None or token != self._generation_token():
                return False
            entry = self._prepare_history_entry(op, details)
            self._append_history(entry)
            if modified:
                self.state.modified = True
            self._write_unlocked()
            return True

    def record_operation_with_effect(
            self, op: str, details: dict, apply_effect, rollback_effect,
            *, modified: bool = False):
        """Apply one external effect and persist its matching session entry.

        The session lock is acquired before ``apply_effect``. If session
        persistence then fails, ``rollback_effect`` runs before the lock is
        released and the in-memory session snapshot is restored.
        """
        token = self._generation_token()
        with self._transaction():
            if token is None or token != self._generation_token():
                return False

            entry = self._prepare_history_entry(op, details)
            applied = False
            try:
                apply_effect()
                applied = True
                self._append_history(entry)
                if modified:
                    self.state.modified = True
                self._write_unlocked()
            except BaseException:
                if applied:
                    try:
                        rollback_effect()
                    except BaseException as rollback_error:
                        raise RuntimeError(
                            "Session update and external-effect rollback both "
                            "failed; external state may remain changed and "
                            "requires recovery before retry"
                        ) from rollback_error
                raise
            return True

    def mark_modified(self):
        token = self._generation_token()
        with self._transaction():
            if token is None or token != self._generation_token():
                return False
            self.state.modified = True
            self._write_unlocked()
            return True

    def close(self):
        token = self._generation_token()
        with self._transaction():
            if token is None or token != self._generation_token():
                return False
            self.state = SessionState()
            try:
                self.path.unlink()
            except FileNotFoundError:
                pass
            return True

    def is_open(self) -> bool:
        return _valid_string(
            self.state.rom_path,
            MAX_SESSION_PATH_LEN,
            allow_empty=False,
        )

    def owns_rom(self, rom_path: str) -> bool:
        """Return whether *rom_path* identifies this session's ROM."""
        if not self.is_open() or not rom_path:
            return False
        try:
            return os.path.samefile(self.state.rom_path, rom_path)
        except (OSError, TypeError, ValueError):
            try:
                return (
                    os.path.normcase(os.path.abspath(self.state.rom_path))
                    == os.path.normcase(os.path.abspath(rom_path))
                )
            except (OSError, TypeError, ValueError):
                return False

    @staticmethod
    def _prepare_history_entry(op: str, details: Optional[dict]):
        if not _valid_string(op, MAX_HISTORY_OP_LEN, allow_empty=False):
            raise ValueError("Session history operation must be a bounded string")
        if details is None:
            details = {}
        if not isinstance(details, dict):
            raise ValueError("Session history details must be an object")
        if "op" in details or "time" in details:
            raise ValueError("Session history details contain a reserved field")
        normalized = _normalize_history_value(
            details,
            root_item_limit=MAX_HISTORY_DETAIL_FIELDS,
        )
        return {"op": op, "time": time.time(), **normalized}

    def _append_history(self, entry: dict):
        self.state.history.append(entry)
        if len(self.state.history) > MAX_HISTORY_ENTRIES:
            self.state.history = self.state.history[-MAX_HISTORY_ENTRIES:]

    def _add_history(self, op: str, details: dict):
        self._append_history(self._prepare_history_entry(op, details))

    def info(self) -> dict:
        return {
            "rom_path": self.state.rom_path,
            "rom_version": self.state.rom_version,
            "rom_size": self.state.rom_size,
            "force_version": self.state.force_version,
            "modified": self.state.modified,
            "history_count": len(self.state.history),
            "session_file": str(self.path),
        }
