"""Pinned mGBA 0.10.5 display-free backend adapter.

The ``mgba`` binding is imported lazily so this module (and the rest of the
package) stays importable on hosts without the native emulator. The adapter is
never exercised by the WU1 dependency-free tests; its exact API surface is
pinned by contract tests that model the 0.10.5 binding without importing it.

Every symbol used here is verified against the official pinned sources at commit
``26b7884bc25a5933960f3cdcd98bac1ae14d42e2``:

* Load ROM: ``mgba.core.load_vf(vfile)`` (module function).
* Config: ``Config(defaults=...)`` is only the input carrier;
  ``core.load_config(config)`` maps it into the native ``core._core.opts``
  (``struct mCoreOptions``) via ``mCoreLoadForeignConfig`` -> ``mCoreConfigMap``.
  Effective values are therefore verified directly from ``core._core.opts``
  (``audioSync``/``videoSync``/``frameskip``/``mute``/``useBios``/``skipBios``),
  failing closed on any mismatch. ``mCoreLoadForeignConfig`` does *not* copy
  these keys back into ``core.config``, so ``core.config[key]`` is not read.
* Crash: append a handler to the core-owned ``core._callbacks.core_crashed``
  list (no ``add_callbacks`` / ``callbacks.crashed``).
* Keys: ``core.set_keys(raw=mask)`` (positional args are key *indexes*).
* Memory: ``core.memory.<domain>.u8/u16/u32[address]`` views for read/write.
* Screenshot: ``Image.save_png(fileobj)`` returning ``bool`` (no Pillow).
* Version / commit: ``mgba.__version__`` and ``mgba.Git.commit``.

Startup contract (verified as effective, not merely requested):

* ``audioSync=false``, ``videoSync=false``, ``frameskip=0``, ``mute=true``,
  ``useBios=false`` (built-in HLE BIOS), ``skipBios=false`` (fixed HLE startup).
* An in-memory temporary save (``VFile.fromEmpty()`` + ``load_temporary_save``)
  so no ``.sav`` is written beside the ROM.
* Built-in HLE BIOS; no external BIOS file.
* No autoload of save / patch / cheats.
* A video buffer is attached only when a screenshot is requested.
* A crash callback flips the core into a faulted state.

All third-party diagnostics go to stderr; stdout is reserved for the single
result document emitted by ``__main__``. Every note that could carry a path is
passed through :func:`redact_message` first.
"""

from __future__ import annotations

import io
import sys
from typing import Any, Callable, Dict, List, Optional

from .runner import Backend, BackendError, redact_message

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

# Effective ``struct mCoreOptions`` fields verified after ``load_config``.
_REQUIRED_OPTS_BOOL = {
    "audioSync": False,
    "videoSync": False,
    "mute": True,
    "useBios": False,
    "skipBios": False,
}
_REQUIRED_OPTS_INT = {"frameskip": 0}


def _diag(message: str) -> None:
    sys.stderr.write(f"[mgba-backend] {redact_message(message)}\n")


class _NonClosingBytesIO(io.BytesIO):
    """BytesIO whose ``close`` is a no-op.

    mGBA's ``png.PNG`` wraps the file object in a ``VFile`` whose ``__del__``
    calls ``fileobj.close()``. A normal ``BytesIO`` would then reject
    ``getvalue()``. Suppressing ``close`` keeps the captured PNG readable.
    """

    def close(self) -> None:  # noqa: D401 - intentional no-op
        pass


def _opts_bool(opts: Any, key: str) -> Optional[bool]:
    """Read a boolean option from the native ``struct mCoreOptions``.

    cffi surfaces a C ``bool`` field as an ``int``/``bool``; coerce defensively.
    """
    try:
        raw = getattr(opts, key)
    except Exception:
        return None
    if raw is None:
        return None
    try:
        return bool(int(raw))
    except (TypeError, ValueError):
        try:
            return bool(raw)
        except Exception:  # pragma: no cover - defensive
            return None


def _opts_int(opts: Any, key: str) -> Optional[int]:
    try:
        raw = getattr(opts, key)
    except Exception:
        return None
    if raw is None:
        return None
    try:
        return int(raw)
    except (TypeError, ValueError):
        return None


def _read_version_commit():
    """Return ``(version, commit)`` from the pinned binding's public API."""
    version: Optional[str] = None
    commit: Optional[str] = None
    try:
        import mgba
        version = getattr(mgba, "__version__", None)
        git = getattr(mgba, "Git", None)
        if git is not None:
            commit = getattr(git, "commit", None)
    except Exception as exc:  # pragma: no cover - environment dependent
        _diag(f"could not read version/commit: {type(exc).__name__}")
    return version, commit


def check_available() -> Dict[str, Any]:
    """Return a diagnostic dict describing mGBA binding availability.

    Every reason is sanitized so no interpreter/library path can leak.
    """
    try:
        import mgba  # noqa: F401
        import mgba.core  # noqa: F401
    except Exception as exc:  # pragma: no cover - environment dependent
        return {
            "available": False,
            "reason": redact_message(f"import failed: {type(exc).__name__}"),
            "version": None,
            "commit": None,
        }

    version, commit = _read_version_commit()
    if version != REQUIRED_VERSION:
        return {
            "available": False,
            "reason": redact_message(f"version {version!r} != required {REQUIRED_VERSION!r}"),
            "version": version,
            "commit": commit,
        }
    return {"available": True, "reason": None, "version": version, "commit": commit}


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
        self._config = None
        self._crash_handler: Optional[Callable[[], None]] = None
        self._image = None
        self._import_and_prepare()

    def _import_and_prepare(self) -> None:
        try:
            import mgba.core  # noqa: F401
            import mgba.log
        except Exception as exc:
            raise BackendError(
                redact_message(f"mGBA Python binding is not available: {type(exc).__name__}")
            ) from None

        # Route library logging to stderr, never stdout.
        try:
            mgba.log.silence()
        except Exception as exc:
            _diag(f"could not silence mGBA log: {type(exc).__name__}")

        self._version, self._commit = _read_version_commit()
        if self._version != REQUIRED_VERSION:
            raise BackendError(
                redact_message(f"mGBA version {self._version!r} is not the required {REQUIRED_VERSION!r}")
            )

    def load_rom(self, rom_bytes: bytes) -> None:
        import mgba.core
        import mgba.vfs as vfs

        self._rom_vfile = vfs.VFile.fromEmpty()
        self._rom_vfile.write(rom_bytes, len(rom_bytes))
        self._rom_vfile.seek(0, 0)

        core = mgba.core.load_vf(self._rom_vfile)
        if core is None:
            raise BackendError("mGBA could not load the ROM image")
        self._core = core

        self._apply_config(core)

        # In-memory temporary save so nothing is written beside the ROM.
        self._save_vfile = vfs.VFile.fromEmpty()
        if not core.load_temporary_save(self._save_vfile):
            raise BackendError("mGBA could not attach an in-memory temporary save")

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
            # Fixed built-in HLE startup: no external BIOS, no BIOS skip.
            "useBios": False,
            "skipBios": False,
        }
        # ``Config`` is only the input carrier. ``load_config`` maps it into the
        # native ``core._core.opts`` (mCoreLoadForeignConfig -> mCoreConfigMap);
        # it does not copy these keys back into ``core.config``. Retain the
        # config so its native storage outlives this call.
        self._config = Config(defaults=requested)
        core.load_config(self._config)

        native = getattr(core, "_core", None)
        opts = getattr(native, "opts", None)
        if opts is None:
            raise BackendError("could not access effective core options")

        # Verify each required value became effective, reading from core opts.
        effective: Dict[str, Any] = {}
        for key, want in _REQUIRED_OPTS_BOOL.items():
            value = _opts_bool(opts, key)
            if value is None or value != want:
                raise BackendError(f"could not verify effective config: {key}")
            effective[key] = value
        for key, want in _REQUIRED_OPTS_INT.items():
            value = _opts_int(opts, key)
            if value is None or value != want:
                raise BackendError(f"could not verify effective config: {key}")
            effective[key] = value

        # HLE BIOS is in effect exactly when the external BIOS is disabled.
        effective["biosHle"] = not effective["useBios"]
        effective.update(
            {
                "autoloadSave": False,
                "autoloadPatch": False,
                "autoloadCheats": False,
            }
        )
        self._effective = effective

    def _register_crash_callback(self, core) -> None:
        callbacks = getattr(core, "_callbacks", None)
        crashed = getattr(callbacks, "core_crashed", None)
        if crashed is None or not isinstance(crashed, list):
            raise BackendError("mGBA core does not expose a crash callback list")

        def _on_crash() -> None:
            self._crash = "core signalled crash"

        crashed.append(_on_crash)
        self._crash_handler = _on_crash

    def _attach_video_buffer(self, core) -> None:
        try:
            from mgba.image import Image

            width, height = core.desired_video_dimensions()
            image = Image(width, height)
            core.set_video_buffer(image)
            self._image = image
        except Exception as exc:
            raise BackendError(
                redact_message(f"could not attach video buffer: {type(exc).__name__}")
            ) from None

    def reset(self) -> None:
        if self._core is None:
            raise BackendError("core not loaded")
        self._core.reset()

    def set_keys(self, mask: int) -> None:
        # ``raw`` sets the literal key mask; positional values are key indexes.
        self._core.set_keys(raw=mask)

    def run_frame(self) -> None:
        try:
            self._core.run_frame()
        except Exception as exc:
            self._crash = redact_message(f"native exception: {type(exc).__name__}")

    def read(self, domain: str, address: int, width: int) -> int:
        view = self._view(domain, width)
        if width == 8:
            return int(view[address]) & 0xFF
        if width == 16:
            return int(view[address]) & 0xFFFF
        return int(view[address]) & 0xFFFFFFFF

    def write(self, domain: str, address: int, width: int, value: int) -> None:
        view = self._view(domain, width)
        if width == 8:
            view[address] = value & 0xFF
        elif width == 16:
            view[address] = value & 0xFFFF
        else:
            view[address] = value & 0xFFFFFFFF

    def _view(self, domain: str, width: int):
        attr = _DOMAIN_ATTR.get(domain)
        if attr is None:
            raise BackendError(f"unsupported memory domain: {domain}")
        memory = getattr(self._core, "memory", None)
        if memory is None:
            raise BackendError("core memory is unavailable (core not reset?)")
        try:
            region = getattr(memory, attr)
        except Exception as exc:
            raise BackendError(
                redact_message(f"could not access domain {domain}: {type(exc).__name__}")
            ) from None
        if width == 8:
            return region.u8
        if width == 16:
            return region.u16
        return region.u32

    def crash_message(self) -> Optional[str]:
        return self._crash

    def screenshot_png(self) -> bytes:
        if self._image is None:
            raise BackendError("no video buffer attached for screenshot")
        buffer = _NonClosingBytesIO()
        try:
            ok = self._image.save_png(buffer)
        except Exception as exc:
            raise BackendError(
                redact_message(f"screenshot encoding failed: {type(exc).__name__}")
            ) from None
        if not ok:
            raise BackendError("mGBA reported PNG encoding failure")
        return buffer.getvalue()

    def version(self) -> Optional[str]:
        return self._version

    def commit(self) -> Optional[str]:
        return self._commit

    def effective_config(self) -> Dict[str, Any]:
        return dict(self._effective)


__all__ = [
    "MgbaBackend",
    "check_available",
    "REQUIRED_VERSION",
    "REQUIRED_COMMIT",
]
