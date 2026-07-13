"""Stateful session management for FEBuilderGBA CLI.

Tracks the currently loaded ROM, version, and operation history
across multiple CLI invocations via a JSON session file.
"""

import json
import math
import os
import time
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Optional


MAX_HISTORY_ENTRIES = 100
MAX_SESSION_PATH_LEN = 4096
MAX_SESSION_ROM_SIZE = 0xFFFFFFFF
MAX_SESSION_TIMESTAMP = 10_000_000_000
MAX_SESSION_VERSION_LEN = 64
HISTORY_OP_DATA_EXPORT = "data_export"
HISTORY_OP_DATA_IMPORT = "data_import"
HISTORY_OP_IMPORT_PALETTE = "import_palette"


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


@dataclass
class SessionState:
    """Persistent session state."""
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
        history = [
            entry for entry in history
            if isinstance(entry, dict)
        ][-MAX_HISTORY_ENTRIES:]

        rom_path = data.get("rom_path", "")
        if not _valid_string(rom_path, MAX_SESSION_PATH_LEN):
            rom_path = ""
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


def _locked_save_json(path: str, data: dict) -> None:
    """Write JSON with best-effort file locking."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    try:
        f = open(path, "r+")
    except FileNotFoundError:
        f = open(path, "w")
    with f:
        _locked = False
        try:
            import msvcrt
            msvcrt.locking(f.fileno(), msvcrt.LK_LOCK, 1)
            _locked = True
        except (ImportError, OSError):
            try:
                import fcntl
                fcntl.flock(f.fileno(), fcntl.LOCK_EX)
                _locked = True
            except (ImportError, OSError):
                pass
        try:
            f.seek(0)
            f.truncate()
            json.dump(data, f, indent=2)
            f.flush()
        finally:
            if _locked:
                try:
                    import msvcrt
                    f.seek(0)
                    msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
                except (ImportError, OSError):
                    try:
                        import fcntl
                        fcntl.flock(f.fileno(), fcntl.LOCK_UN)
                    except (ImportError, OSError):
                        pass


class Session:
    """Manages persistent session state."""

    def __init__(self, session_path: Optional[str] = None):
        if session_path:
            self.path = Path(session_path)
        else:
            self.path = _default_session_dir() / "default.json"
        self.state = SessionState()
        if self.path.exists():
            self._load()

    def _load(self):
        try:
            with open(self.path) as f:
                data = json.load(f)
            self.state = SessionState.from_dict(data)
        except (json.JSONDecodeError, KeyError):
            self.state = SessionState()

    def save(self):
        self.state.updated_at = time.time()
        _locked_save_json(str(self.path), self.state.to_dict())

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

        self.state.rom_path = abs_rom_path
        self.state.rom_version = version
        self.state.rom_size = rom_size
        self.state.force_version = force_version
        self.state.created_at = time.time()
        self.state.modified = False
        self.state.history = []
        self._add_history("open", {"rom": rom_path, "version": version})
        self.save()

    def record_operation(self, op: str, details: dict = None):
        self._add_history(op, details or {})
        self.save()

    def mark_modified(self):
        self.state.modified = True
        self.save()

    def close(self):
        self.state = SessionState()
        if self.path.exists():
            self.path.unlink()

    def is_open(self) -> bool:
        return _valid_string(
            self.state.rom_path,
            MAX_SESSION_PATH_LEN,
            allow_empty=False,
        )

    def _add_history(self, op: str, details: dict):
        entry = {
            "op": op,
            "time": time.time(),
            **details,
        }
        self.state.history.append(entry)
        if len(self.state.history) > MAX_HISTORY_ENTRIES:
            self.state.history = self.state.history[-MAX_HISTORY_ENTRIES:]

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
