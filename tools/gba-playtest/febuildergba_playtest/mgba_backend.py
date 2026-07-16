"""Pinned mGBA 0.10.5 display-free backend adapter.

The ``mgba`` binding is imported lazily so this module (and the rest of the
package) stays importable on hosts without the native emulator. The adapter is
never exercised by the WU1 dependency-free tests; its exact API surface is
pinned by contract tests that model the 0.10.5 binding without importing it.

Every symbol used here is verified against the official pinned sources at commit
``26b7884bc25a5933960f3cdcd98bac1ae14d42e2``:

* Load ROM: build a *native mapped* ``VFile`` via ``mgba.vfs.VFile.fromEmpty()``
  (backed by ``VFileMemChunk``), ``write`` the image, ``seek(0, os.SEEK_SET)``,
  then ``mgba.core.load_vf(vfile)``. A pure-Python ``vfs.open(BytesIO)`` VFile
  cannot load a ROM: its ``_vfpMap`` callback is unimplemented while
  ``GBALoadROM`` requires ``vf->map``. ``VFile`` has no ``fromFile``. On success
  ``load_vf`` -> ``core.load_rom`` transfers the VFile to the core (which closes
  it on deinit); the pinned ``load_rom`` wrapper does not set ``_claimed``
  (unlike ``load_save``/``load_bios``), so the adapter claims it to prevent a
  double ``close`` (``VFile.__del__`` closes only when not ``_claimed``).
* Config: ``Config(defaults=...)`` is only the input carrier;
  ``core.load_config(config)`` maps it into the native ``core._core.opts``
  (``struct mCoreOptions``) via ``mCoreLoadForeignConfig`` -> ``mCoreConfigMap``.
  Effective values are therefore verified directly from ``core._core.opts``
  (``audioSync``/``videoSync``/``frameskip``/``mute``/``useBios``/``skipBios``),
  failing closed on any mismatch. ``mCoreLoadForeignConfig`` does *not* copy
  these keys back into ``core.config``, so ``core.config[key]`` is not read.
* Crash: append a handler to the core-owned ``core._callbacks.core_crashed``
  list (no ``add_callbacks`` / ``callbacks.crashed``). :meth:`MgbaBackend.close`
  removes that handler before native release to break the reference cycle.
* Keys: ``core.set_keys(raw=mask)`` (positional args are key *indexes*).
* Memory: ``core.memory.<domain>.u8/u16/u32[address]`` views for read/write.
* Screenshot: ``Image.save_png(fileobj)`` returning ``bool`` (no Pillow).
* Version / commit: ``mgba.__version__`` (hardcoded ``0.10.5``) and
  ``mgba.Git.commit`` (``git describe --always --abbrev=40 --dirty`` — a full
  40-hex SHA). Availability requires *both* the exact version and the exact
  pinned commit; a ``-dirty`` stamp or ``(unknown)``/``None`` is rejected.

Startup contract (verified as effective, not merely requested):

* ``audioSync=false``, ``videoSync=false``, ``frameskip=0``, ``mute=true``,
  ``useBios=false`` (built-in HLE BIOS), ``skipBios=false`` (fixed HLE startup).
* No save VFile is loaded: the pinned mGBA core safely uses anonymous
  in-memory savedata when no save is attached. Save / patch / cheat autoload
  is not an ``mCoreOptions`` setting in mGBA 0.10.5; it requires explicit
  frontend calls to ``core.autoload_save()``, ``core.autoload_patch()``, or
  ``core.autoload_cheats()``, none of which this adapter makes. The reported
  ``autoload*`` values describe that adapter-level execution policy. Avoiding
  a temporary-save VFile also removes an unnecessary native handle from the
  deterministic teardown in :meth:`MgbaBackend.close`.
* Built-in HLE BIOS; no external BIOS file.
* No autoload of save / patch / cheats.
* A video buffer is attached only when a screenshot is requested.
* A crash callback flips the core into a faulted state.
* Windows native-library discovery: before importing ``mgba`` the adapter
  registers the DLL directories recorded by the bootstrap manifest
  (``.mgba-build/mgba-dll-dirs.txt``, plus an optional
  ``FEBUILDERGBA_MGBA_DLL_DIRS`` override) via ``os.add_dll_directory`` — see
  :func:`prepare_native_library_search`. This is one deterministic strategy
  shared with the bootstrap and is a no-op on POSIX. Manifest and override
  inputs are bounded (size / line count / path length) and registration is
  idempotent across repeated calls, so malformed input fails closed and handles
  never grow without bound.

Teardown contract (deterministic native release):

* :meth:`MgbaBackend.close` is idempotent. It removes the crash handler from
  ``core._callbacks.core_crashed`` (breaking the cycle) and then, while the ROM
  VFile / config / image are still referenced, imports ``mgba.core.ffi`` and
  calls ``ffi.release(core._core)`` exactly once. Per the official CFFI docs,
  ``ffi.release()`` runs the ``ffi.gc`` destructor immediately and prevents a
  later second call. The claimed ROM VFile is never closed from Python; the
  native core owns and closes it during release.

All third-party diagnostics go to stderr; stdout is reserved for the single
result document emitted by ``__main__``. Every note that could carry a path is
passed through :func:`redact_message` first.
"""

from __future__ import annotations

import io
import ntpath
import os
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


# --- Windows native-library discovery --------------------------------------
# One deterministic, documented strategy shared with the bootstrap: the build
# script records the DLL search directories (build output dir holding
# ``libmgba``; under MSYS2 also the UCRT64 ``bin`` holding ``libgcc`` /
# ``libwinpthread``) as native paths in ``.mgba-build/mgba-dll-dirs.txt``. Before
# importing ``mgba`` this adapter registers each existing directory with
# ``os.add_dll_directory``. A UCRT64 Python launched from PowerShell/.NET does
# not otherwise resolve the binding's dependent DLLs (``_builder.py``'s
# ``runtime_library_dirs`` does not cover this). No-op on POSIX and when nothing
# is recorded. ``FEBUILDERGBA_MGBA_DLL_DIRS`` is a semicolon-separated Windows
# path list applied ahead of the manifest. Its separator is deliberately fixed
# instead of using the host ``os.pathsep`` so contract tests can model Windows
# paths without splitting drive-letter colons on POSIX hosts.
_DLL_MANIFEST_NAME = "mgba-dll-dirs.txt"
_DLL_SEARCH_ENV = "FEBUILDERGBA_MGBA_DLL_DIRS"
_DLL_ENV_SEPARATOR = ";"

# Hard bounds so a malformed/oversized manifest or environment override fails
# closed (registers nothing) instead of consuming unbounded memory or driving
# unbounded ``os.add_dll_directory`` calls.
_DLL_MANIFEST_MAX_BYTES = 64 * 1024
_DLL_MAX_DIRS = 64
_DLL_DIR_MAX_LEN = 4096
_DLL_ENV_MAX_LEN = 64 * 1024

# Handles from ``os.add_dll_directory`` are kept alive for the process lifetime:
# closing a handle removes the directory from the search path again.
_DLL_HANDLES: List[Any] = []

# Directories already registered, so repeated ``check``/backend construction in
# one process does not grow the handle list without bound (idempotent).
_REGISTERED_DIRS: set = set()


def _dll_manifest_path() -> str:
    tool_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(tool_dir, ".mgba-build", _DLL_MANIFEST_NAME)


def _read_manifest_dirs(manifest_path: str) -> List[str]:
    """Return bounded, sanitized directory entries from the DLL manifest.

    Reads at most :data:`_DLL_MANIFEST_MAX_BYTES` + 1; an oversized manifest is
    ignored entirely. Blank/``#`` lines are skipped, overlong entries dropped,
    and at most :data:`_DLL_MAX_DIRS` entries are returned.
    """
    try:
        with open(manifest_path, "rb") as handle:
            raw = handle.read(_DLL_MANIFEST_MAX_BYTES + 1)
        if len(raw) > _DLL_MANIFEST_MAX_BYTES:
            _diag("DLL manifest exceeds the size bound; ignoring it")
            return []
        blob = raw.decode("utf-8")
    except (OSError, UnicodeError):
        return []
    dirs: List[str] = []
    for line in blob.splitlines():
        entry = line.strip()
        if not entry or entry.startswith("#"):
            continue
        if len(entry) > _DLL_DIR_MAX_LEN:
            _diag("DLL manifest entry exceeds the length bound; skipping it")
            continue
        dirs.append(entry)
        if len(dirs) >= _DLL_MAX_DIRS:
            break
    return dirs


def _dll_dir_key(directory: str) -> str:
    """Return a host-independent Windows key for directory deduplication."""

    return ntpath.normcase(ntpath.abspath(directory))


def _is_absolute_windows_path(directory: str) -> bool:
    drive, tail = ntpath.splitdrive(directory)
    return bool(drive and tail.startswith(("\\", "/")))


def _collect_dll_dirs(environ: Any, manifest_path: str) -> List[str]:
    """Ordered, de-duplicated DLL search dirs: env override first, then manifest.

    The environment override is bounded in total length, per-entry length, and
    entry count; an oversized override is ignored so it cannot exhaust memory.
    """
    candidates: List[str] = []
    raw = ""
    try:
        raw = environ.get(_DLL_SEARCH_ENV, "") or ""
    except AttributeError:  # pragma: no cover - defensive
        raw = ""
    if not isinstance(raw, str) or len(raw) > _DLL_ENV_MAX_LEN:
        _diag("DLL search override exceeds the length bound; ignoring it")
        raw = ""
    if raw:
        for part in raw.split(_DLL_ENV_SEPARATOR):
            if part and len(part) <= _DLL_DIR_MAX_LEN:
                candidates.append(part)
            if len(candidates) >= _DLL_MAX_DIRS:
                break
    candidates.extend(_read_manifest_dirs(manifest_path))
    ordered: List[str] = []
    seen = set()
    for entry in candidates:
        key = _dll_dir_key(entry)
        if key not in seen:
            seen.add(key)
            ordered.append(entry)
        if len(ordered) >= _DLL_MAX_DIRS:
            break
    return ordered


def prepare_native_library_search(
    *,
    register: Optional[Callable[[str], Any]] = None,
    isdir: Optional[Callable[[str], bool]] = None,
    is_windows: Optional[bool] = None,
    environ: Optional[Any] = None,
    manifest_path: Optional[str] = None,
    seen_dirs: Optional[set] = None,
) -> List[str]:
    """Register recorded DLL directories before importing ``mgba`` on Windows.

    Deterministic no-op on POSIX and when nothing is recorded. Every argument is
    injectable so the behaviour is testable without a real binding or Windows.
    Registration is idempotent across repeated calls (tracked in ``seen_dirs``,
    defaulting to the process-wide set) so handles never grow without bound.
    Returns the list of directories actually registered by this call.
    """
    if is_windows is None:
        is_windows = os.name == "nt"
    if not is_windows:
        return []
    if register is None:
        register = getattr(os, "add_dll_directory", None)
    if register is None:
        return []
    if isdir is None:
        isdir = os.path.isdir
    env = environ if environ is not None else os.environ
    manifest = manifest_path or _dll_manifest_path()
    use_global_state = seen_dirs is None
    state = _REGISTERED_DIRS if use_global_state else seen_dirs
    registered: List[str] = []
    for directory in _collect_dll_dirs(env, manifest):
        key = _dll_dir_key(directory)
        if key in state:
            continue
        if len(state) >= _DLL_MAX_DIRS:
            break
        if not _is_absolute_windows_path(directory) or not isdir(directory):
            continue
        try:
            handle = register(directory)
        except (OSError, TypeError, ValueError) as exc:
            _diag(f"could not add DLL directory: {type(exc).__name__}")
            continue
        if use_global_state and handle is not None:
            _DLL_HANDLES.append(handle)
        state.add(key)
        registered.append(directory)
    return registered


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


def _commit_ok(commit: Optional[str]) -> bool:
    """True only for the exact pinned commit with a clean (non-dirty) stamp.

    ``mgba.Git.commit`` is ``git describe --always --abbrev=40 --dirty`` (a full
    40-hex SHA, suffixed ``-dirty`` when the build tree was modified, or
    ``None``/``(unknown)`` when Git provenance was unavailable). A ``-dirty``
    stamp is *normalized only by rejecting it* — never by stripping the suffix
    to force a match — and ``(unknown)``/``None`` or any other commit is
    likewise rejected.
    """
    if not isinstance(commit, str) or not commit:
        return False
    stamp = commit.strip()
    if not stamp or stamp.lower() in ("(unknown)", "unknown"):
        return False
    if "dirty" in stamp.lower():
        return False
    return stamp == REQUIRED_COMMIT


def check_available() -> Dict[str, Any]:
    """Return a diagnostic dict describing mGBA binding availability.

    Every reason is sanitized so no interpreter/library path can leak.
    """
    prepare_native_library_search()
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
    if not _commit_ok(commit):
        return {
            "available": False,
            "reason": redact_message(f"commit {commit!r} != required {REQUIRED_COMMIT!r}"),
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
        self._config = None
        self._crash_handler: Optional[Callable[[], None]] = None
        self._image = None
        self._closed = False
        self._import_and_prepare()

    def _import_and_prepare(self) -> None:
        prepare_native_library_search()
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
        if not _commit_ok(self._commit):
            raise BackendError(
                redact_message(f"mGBA commit {self._commit!r} is not the required {REQUIRED_COMMIT!r}")
            )

    def load_rom(self, rom_bytes: bytes) -> None:
        import mgba.core
        import mgba.vfs as vfs

        # A native mapped VFile (``VFileMemChunk``) is mandatory: the pure-Python
        # ``vfs.open(BytesIO)`` VFile leaves ``_vfpMap`` unimplemented while
        # ``GBALoadROM`` demands ``vf->map``. ``VFile`` has no ``fromFile``.
        self._rom_vfile = vfs.VFile.fromEmpty()
        written = self._rom_vfile.write(rom_bytes, len(rom_bytes))
        # ``VFile.write`` returns the number of bytes written; a short write must
        # fail closed rather than hand a truncated image to the core.
        if written != len(rom_bytes):
            raise BackendError("short write while staging the ROM image into a native VFile")
        # ``seek`` returns the resulting absolute position; rewinding to the
        # start must land at offset 0 before ownership transfer.
        position = self._rom_vfile.seek(0, os.SEEK_SET)
        if position != 0:
            raise BackendError("could not rewind the ROM image before load")

        core = mgba.core.load_vf(self._rom_vfile)
        if core is None:
            # Ownership never transferred; the wrapper still owns its handle.
            raise BackendError("mGBA could not load the ROM image")
        # ``load_vf`` -> ``core.load_rom`` handed the VFile to the core, which
        # closes it on deinit. The pinned ``load_rom`` wrapper does not set
        # ``_claimed`` (only ``load_save``/``load_bios`` do), so claim it here so
        # ``VFile.__del__`` cannot close the now core-owned handle a second time.
        self._rom_vfile._claimed = True
        self._core = core

        self._apply_config(core)

        # No save VFile is attached: the pinned mGBA core safely uses anonymous
        # in-memory savedata. Autoload is an explicit frontend action, not an
        # mCoreOptions field, and this adapter invokes none of those actions.
        # Attaching a temporary-save VFile here would only add a native handle
        # to tear down with no behavioral benefit.

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
        # These report adapter-level actions, not native mCoreOptions. In the
        # pinned binding autoload can happen only through explicit frontend
        # methods, which this adapter never invokes; contract tests enforce
        # both the exact API shape and the absence of those calls.
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

    def close(self) -> None:
        """Release the native core exactly once (idempotent, deterministic).

        Teardown contract for the pinned 0.10.5 binding:

        * Return immediately if already closed or if no core was constructed.
        * Remove this backend's crash handler from the core-owned
          ``core._callbacks.core_crashed`` list *before* release, breaking the
          core -> callback -> backend reference cycle.
        * Import the pinned ``mgba.core.ffi`` and call ``ffi.release(core._core)``
          exactly once while the ROM VFile / config / image references are still
          alive (the native core owns and closes the ROM VFile during release).
          Per the official CFFI docs, ``ffi.release()`` invokes the ``ffi.gc``
          destructor immediately and prevents a later second call.
        * Only after a successful release are the Python references cleared. The
          ROM VFile stays ``_claimed=True`` and is never closed from Python:
          the native core owns and closes it.

        Any structural release/callback failure is surfaced as a precise
        :class:`BackendError`; there is no broad silent catch and no success
        fallback.
        """
        if self._closed or self._core is None:
            self._closed = True
            return

        core = self._core

        # Break the cycle: drop our crash handler from the core-owned list
        # before releasing the native core.
        handler = self._crash_handler
        if handler is not None:
            callbacks = getattr(core, "_callbacks", None)
            crashed = getattr(callbacks, "core_crashed", None)
            if crashed is None or not isinstance(crashed, list):
                raise BackendError("mGBA core does not expose a crash callback list at close")
            try:
                crashed.remove(handler)
            except ValueError as exc:
                raise BackendError(
                    redact_message(f"crash callback missing at close: {type(exc).__name__}")
                ) from None

        native = getattr(core, "_core", None)
        if native is None:
            raise BackendError("mGBA core does not expose a native handle at close")

        try:
            from mgba.core import ffi
        except Exception as exc:
            raise BackendError(
                redact_message(f"could not import mgba.core.ffi at close: {type(exc).__name__}")
            ) from None

        release = getattr(ffi, "release", None)
        if release is None:
            raise BackendError("mGBA cffi does not expose ffi.release")

        # The ROM VFile / config / image are intentionally still referenced so
        # the native release sees the live memory it owns; they are cleared only
        # after a successful, single release.
        try:
            release(native)
        except Exception as exc:
            raise BackendError(
                redact_message(f"native core release failed: {type(exc).__name__}")
            ) from None

        # Successful release: clear references. The ROM VFile stays claimed and
        # is never Python-closed (the native core owns/closes it).
        self._closed = True
        self._core = None
        self._crash_handler = None
        self._rom_vfile = None
        self._config = None
        self._image = None

    def effective_config(self) -> Dict[str, Any]:
        return dict(self._effective)


__all__ = [
    "MgbaBackend",
    "check_available",
    "REQUIRED_VERSION",
    "REQUIRED_COMMIT",
]
