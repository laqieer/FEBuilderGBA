"""Stateful session management for FEBuilderGBA CLI.

Tracks the currently loaded ROM, version, and operation history
across multiple CLI invocations via a JSON session file.
"""

import json
import os
import time
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Optional


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
        return cls(**{k: v for k, v in data.items() if k in cls.__dataclass_fields__})


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
        self.state.rom_path = os.path.abspath(rom_path)
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
        return bool(self.state.rom_path)

    def _add_history(self, op: str, details: dict):
        entry = {
            "op": op,
            "time": time.time(),
            **details,
        }
        self.state.history.append(entry)
        # Keep last 100 entries
        if len(self.state.history) > 100:
            self.state.history = self.state.history[-100:]

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
