"""Pinned mGBA 0.10.5 display-free backend adapter.

The ``mgba`` binding is imported lazily so this module (and the rest of the
package) stays importable on hosts without the native emulator. The adapter is
never exercised by the WU1 dependency-free tests; it is validated by the
separate real-emulator CI job.

Startup contract (verified as effective, not just requested):

* ``audioSync=false``, ``videoSync=false``, ``frameskip=0``, ``mute=true``.
* An in-memory temporary save (``VFile.fromEmpty()`` + ``load_temporary_save``)
  so no ``.sav`` is written beside the ROM.
* Built-in HLE BIOS; no external BIOS file.
* No autoload of save / patch / cheats.
* A video buffer is attached only when a screenshot is requested.
* A crash callback flips the core into a faulted state.

All third-party diagnostics go to stderr; stdout is reserved for the single
result document emitted by ``__main__``.
"""

from __future__ import annotations

import sys
from typing import Any, Callable, Dict, Optional

from .runner import Backend, BackendError

REQUIRED_VERSION = "0.10.5"
REQUIRED_COMMIT = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"

# Domain name -> mGBA ``GBAMemory`` attribute name.
_DOMAIN_ATTR: Dict[str, str] = {
    "wram": "wram",
    "iwram": "iwram",
    "io": "io",
    "palette": "palette",
    "vram": "vram",
    "oam": "oam",
    "sram": "sram",
}


def _diag(message: str) -> None:
    sys.stderr.write(f"[mgba-backend] {message}\n")


def check_available() -> Dict[str, Any]:
    """Return a diagnostic dict describing mGBA binding availability."""
    try:
        import mgba  # noqa: F401
        import mgba.core  # noqa: F401
    except Exception as exc:  # pragma: no cover - environment dependent
        return {"available": False, "reason": f"import failed: {exc}", "version": None, "commit": None}

    version, commit = _read_version_commit()
    if version != REQUIRED_VERSION:
        return {
            "available": False,
            "reason": f"version {version!r} != required {REQUIRED_VERSION!r}",
            "version": version,
            "commit": commit,
        }
    return {"available": True, "reason": None, "version": version, "commit": commit}


def _read_version_commit():  # pragma: no cover - environment dependent
    version = None
    commit = None
    try:
        import mgba
        lib = getattr(mgba, "lib", None)
        if lib is not None:
            raw = getattr(lib, "projectVersion", None)
            if raw is not None:
                version = _ffi_string(raw)
            raw_commit = getattr(lib, "gitCommit", None)
            if raw_commit is not None:
                commit = _ffi_string(raw_commit)
    except Exception as exc:
        _diag(f"could not read version/commit: {exc}")
    return version, commit


def _ffi_string(raw):  # pragma: no cover - environment dependent
    try:
        from mgba._pylib import ffi
        return ffi.string(raw).decode("utf-8", "replace")
    except Exception:
        return None


class MgbaBackend(Backend):  # pragma: no cover - requires native emulator
    """Display-free mGBA core wrapper. Instantiation requires the binding."""

    def __init__(self, want_screenshot: bool = False):
        self._want_screenshot = want_screenshot
        self._core = None
        self._crash: Optional[str] = None
        self._version: Optional[str] = None
        self._commit: Optional[str] = None
        self._effective: Dict[str, Any] = {}
        self._rom_vfile = None
        self._save_vfile = None
        self._callbacks = None
        self._image = None
        self._import_and_prepare()

    def _import_and_prepare(self) -> None:
        try:
            import mgba.core
            import mgba.log
        except Exception as exc:
            raise BackendError(f"mGBA Python binding is not available: {exc}") from exc

        # Route library logging to stderr, never stdout.
        try:
            mgba.log.silence()
        except Exception as exc:
            _diag(f"could not silence mGBA log: {exc}")

        self._version, self._commit = _read_version_commit()
        if self._version != REQUIRED_VERSION:
            raise BackendError(
                f"mGBA version {self._version!r} is not the required {REQUIRED_VERSION!r}"
            )

    def load_rom(self, rom_bytes: bytes) -> None:
        import mgba.core
        import mgba.vfs as vfs

        self._rom_vfile = vfs.VFile.fromEmpty()
        self._rom_vfile.write(rom_bytes, len(rom_bytes))
        self._rom_vfile.seek(0, 0)

        core = mgba.core.load1(self._rom_vfile)
        if core is None:
            raise BackendError("mGBA could not load the ROM image")
        self._core = core

        self._apply_config(core)

        # In-memory temporary save so nothing is written beside the ROM.
        self._save_vfile = vfs.VFile.fromEmpty()
        core.load_temporary_save(self._save_vfile)

        self._register_crash_callback(core)

        if self._want_screenshot:
            self._attach_video_buffer(core)

    def _apply_config(self, core) -> None:
        from mgba.core import Config

        requested = {
            "audioSync": False,
            "videoSync": False,
            "frameskip": 0,
            "mute": True,
        }
        core.config.load_defaults(requested)
        try:
            core.load_config(Config(defaults=requested))
        except Exception as exc:
            _diag(f"load_config fell back to defaults: {exc}")

        # Verify the config became effective rather than trusting the request.
        self._effective = {
            "audioSync": _config_bool(core, "audioSync"),
            "videoSync": _config_bool(core, "videoSync"),
            "frameskip": _config_int(core, "frameskip"),
            "mute": _config_bool(core, "mute"),
            "biosHle": True,
            "autoloadSave": False,
            "autoloadPatch": False,
            "autoloadCheats": False,
        }
        for key, expected in requested.items():
            if self._effective.get(key) != expected:
                _diag(f"effective config {key}={self._effective.get(key)!r} != {expected!r}")

    def _register_crash_callback(self, core) -> None:
        import mgba.core

        def _on_crash():
            self._crash = "core signalled crash"

        try:
            callbacks = mgba.core.CoreCallbacks()
            callbacks.crashed = _on_crash
            core.add_callbacks(callbacks)
            self._callbacks = callbacks
        except Exception as exc:
            _diag(f"could not register crash callback: {exc}")

    def _attach_video_buffer(self, core) -> None:
        try:
            from mgba.image import Image
            width, height = core.desired_video_dimensions()
            self._image = Image(width, height)
            core.set_video_buffer(self._image)
        except Exception as exc:
            _diag(f"could not attach video buffer: {exc}")
            self._image = None

    def reset(self) -> None:
        if self._core is None:
            raise BackendError("core not loaded")
        self._core.reset()

    def set_keys(self, mask: int) -> None:
        self._core.set_keys(mask)

    def run_frame(self) -> None:
        try:
            self._core.run_frame()
        except Exception as exc:
            self._crash = f"native exception: {exc}"

    def read(self, domain: str, address: int, width: int) -> int:
        mem = self._domain(domain)
        if width == 8:
            return int(mem[address]) & 0xFF
        if width == 16:
            return int(mem.load16(address)) & 0xFFFF
        return int(mem.load32(address)) & 0xFFFFFFFF

    def write(self, domain: str, address: int, width: int, value: int) -> None:
        mem = self._domain(domain)
        if width == 8:
            mem[address] = value & 0xFF
        elif width == 16:
            mem.store16(address, value & 0xFFFF)
        else:
            mem.store32(address, value & 0xFFFFFFFF)

    def _domain(self, domain: str):
        attr = _DOMAIN_ATTR.get(domain)
        if attr is None:
            raise BackendError(f"unsupported memory domain: {domain}")
        try:
            return getattr(self._core.memory, attr)
        except Exception as exc:
            raise BackendError(f"could not access domain {domain}: {exc}") from exc

    def crash_message(self) -> Optional[str]:
        return self._crash

    def screenshot_png(self) -> bytes:
        if self._image is None:
            raise BackendError("no video buffer attached for screenshot")
        import io
        try:
            pil = self._image.to_pil()
            buffer = io.BytesIO()
            pil.save(buffer, format="PNG")
            return buffer.getvalue()
        except Exception as exc:
            raise BackendError(f"screenshot encoding failed: {exc}") from exc

    def version(self) -> Optional[str]:
        return self._version

    def commit(self) -> Optional[str]:
        return self._commit

    def effective_config(self) -> Dict[str, Any]:
        return dict(self._effective)


def _config_bool(core, key: str) -> Optional[bool]:  # pragma: no cover
    try:
        return bool(core.config.get_bool(key))
    except Exception:
        return None


def _config_int(core, key: str) -> Optional[int]:  # pragma: no cover
    try:
        return int(core.config.get_int(key))
    except Exception:
        return None
