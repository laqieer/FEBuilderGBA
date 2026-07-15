"""Contract tests for the pinned mGBA 0.10.5 backend adapter.

These tests import :class:`MgbaBackend` but never import the real ``mgba``
binding. Instead they inject a fake ``mgba`` module tree whose API shape mirrors
the official pinned sources exactly (``core.load_vf``, ``Config(defaults=...)``
with ``core.load_config`` mapping into the native ``core._core.opts``
(``struct mCoreOptions``), a core-owned ``_callbacks.core_crashed`` list,
``set_keys(raw=...)``, ``memory.<domain>.u8``/``u16``/``u32`` views, and
``Image.save_png(fileobj)`` returning ``bool``).

If the adapter regresses to a name that does not exist in the pinned binding
(``load1``, ``get_bool``, ``add_callbacks``, ``store16``, ``to_pil``, ...), the
fake will not provide it and the corresponding test fails.
"""

import sys
import types
from contextlib import contextmanager

import pytest

from febuildergba_playtest.mgba_backend import MgbaBackend

PINNED_VERSION = "0.10.5"
PINNED_COMMIT = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"


class FakeMemoryView:
    """Models mGBA ``MemoryView`` — indexable get/set only (no store16 etc.)."""

    def __init__(self):
        self._store = {}

    def __getitem__(self, address):
        return self._store.get(address, 0)

    def __setitem__(self, address, value):
        self._store[address] = value


class FakeMemory:
    def __init__(self):
        self.u8 = FakeMemoryView()
        self.u16 = FakeMemoryView()
        self.u32 = FakeMemoryView()


class FakeGBAMemory:
    def __init__(self):
        for name in ("wram", "iwram", "io", "palette", "vram", "oam", "sram"):
            setattr(self, name, FakeMemory())


class FakeCallbacks:
    """Models ``CoreCallbacks``: ``core_crashed`` is a plain list."""

    def __init__(self):
        self.video_frame_started = []
        self.video_frame_ended = []
        self.core_crashed = []
        self.sleep = []
        self.keys_read = []


class FakeConfig:
    """Models ``mgba.core.Config`` — bytes on read, bool->int normalization.

    Note: the real ``mCoreLoadForeignConfig`` maps a foreign ``Config`` into the
    native ``core._core.opts`` and does *not* copy these keys back into
    ``core.config``; the adapter therefore verifies from opts, not from here.
    """

    def __init__(self, native=None, port=None, defaults=None):
        self._values = {}
        for key, value in (defaults or {}).items():
            if isinstance(value, bool):
                value = int(value)
            self._values[key] = str(value)

    def __getitem__(self, key):
        value = self._values.get(key)
        return None if value is None else value.encode("ascii")

    def __setitem__(self, key, value):
        if isinstance(value, bool):
            value = int(value)
        self._values[key] = str(value)


# Fields the fake maps from a foreign Config into ``core._core.opts``, matching
# ``struct mCoreOptions`` in pinned 0.10.5.
_OPTS_BOOL_FIELDS = ("audioSync", "videoSync", "mute", "useBios", "skipBios")
_OPTS_INT_FIELDS = ("frameskip",)


class FakeOpts:
    """Models ``struct mCoreOptions`` (zero-initialized) reachable via cffi."""

    def __init__(self):
        for name in _OPTS_BOOL_FIELDS:
            setattr(self, name, False)
        for name in _OPTS_INT_FIELDS:
            setattr(self, name, 0)


class FakeNativeCore:
    """Models the native ``struct mCore*`` exposed as ``Core._core``."""

    def __init__(self):
        self.opts = FakeOpts()


class FakeCore:
    def __init__(self, apply_config=True):
        self._callbacks = FakeCallbacks()
        self.config = FakeConfig()
        self._core = FakeNativeCore()
        self._apply_config = apply_config
        self.memory = None
        self.key_calls = []
        self.run_frames = 0
        self.video_buffer = None
        self.reset_count = 0
        self.temporary_save = None

    def load_config(self, config):
        # Mirror mCoreLoadForeignConfig -> mCoreConfigMap: values flow into the
        # native opts, not back into core.config. When ``apply_config`` is
        # False the mapping is skipped so opts keep their zero defaults, which
        # lets a test exercise the fail-closed verification path.
        if not self._apply_config:
            return
        for key in _OPTS_BOOL_FIELDS:
            if key in config._values:
                setattr(self._core.opts, key, config._values[key] not in ("0", "false", "False", ""))
        for key in _OPTS_INT_FIELDS:
            if key in config._values:
                setattr(self._core.opts, key, int(config._values[key]))

    def load_temporary_save(self, vfile):
        self.temporary_save = vfile
        return True

    def reset(self):
        self.reset_count += 1
        self.memory = FakeGBAMemory()  # mirrors GBA._load() creating memory

    def set_keys(self, *args, **kwargs):
        self.key_calls.append((args, dict(kwargs)))

    def run_frame(self):
        self.run_frames += 1

    def desired_video_dimensions(self):
        return (240, 160)

    def set_video_buffer(self, image):
        self.video_buffer = image


class FakeImage:
    def __init__(self, width, height, stride=0, alpha=False):
        self.width = width
        self.height = height

    def save_png(self, fileobj):
        fileobj.write(b"\x89PNG\r\n\x1a\nFAKEPNGDATA")
        return True


class FakeVFile:
    """Models the pinned ``mgba.vfs.VFile`` ownership contract.

    ``fromEmpty`` is the only constructor (the pinned binding has no
    ``fromFile``). ``close`` flips ``_claimed`` and is idempotent; ``__del__``
    closes only when the handle has not been claimed by the core. Instances
    record their ``write``/``seek`` calls so a test can assert the exact
    fromEmpty -> write -> seek(0, SEEK_SET) -> load_vf sequence.
    """

    def __init__(self):
        self.data = bytearray()
        self._claimed = False
        self.closed = 0
        self.writes = []
        self.seeks = []

    @staticmethod
    def fromEmpty():
        return FakeVFile()

    def write(self, buffer, size):
        self.data += bytes(buffer[:size])
        self.writes.append(size)
        return size

    def seek(self, offset, whence):
        self.seeks.append((offset, whence))
        return offset

    def close(self):
        if self._claimed:
            return False
        self._claimed = True
        self.closed += 1
        return True

    def __del__(self):
        try:
            if not self._claimed:
                self.close()
        except Exception:
            pass


class _FakeState:
    """Captures the last core created so a test can inspect it."""

    def __init__(self):
        self.last_core = None
        self.load_vf_calls = 0


@contextmanager
def fake_mgba(state, save_png_ok=True, crash=False, break_config=False):
    mgba = types.ModuleType("mgba")
    mgba.__version__ = PINNED_VERSION

    class Git:
        commit = PINNED_COMMIT

    mgba.Git = Git

    core_mod = types.ModuleType("mgba.core")

    def load_vf(vfile):
        state.load_vf_calls += 1
        core = FakeCore(apply_config=not break_config)
        state.last_core = core
        return core

    core_mod.load_vf = load_vf
    core_mod.Config = FakeConfig

    log_mod = types.ModuleType("mgba.log")
    log_mod.silence = lambda: None

    vfs_mod = types.ModuleType("mgba.vfs")
    vfs_mod.VFile = FakeVFile

    image_mod = types.ModuleType("mgba.image")

    class _Image(FakeImage):
        def save_png(self, fileobj):
            written = super().save_png(fileobj)
            return written and save_png_ok

    image_mod.Image = _Image

    mgba.core = core_mod
    mgba.log = log_mod
    mgba.vfs = vfs_mod
    mgba.image = image_mod

    injected = {
        "mgba": mgba,
        "mgba.core": core_mod,
        "mgba.log": log_mod,
        "mgba.vfs": vfs_mod,
        "mgba.image": image_mod,
    }
    saved = {name: sys.modules.get(name) for name in injected}
    sys.modules.update(injected)
    try:
        yield state
    finally:
        for name, module in saved.items():
            if module is None:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = module


ROM = bytes(0x100)


def test_load_rom_uses_load_vf_not_load1():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        assert state.load_vf_calls == 1
        assert state.last_core is not None


def test_rom_vfile_built_via_fromempty_write_seek_then_load_vf():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        rom_vf = backend._rom_vfile
        # Native mapped VFile path: write the whole image, then rewind with
        # os.SEEK_SET (== 0) before handing it to load_vf.
        assert rom_vf.writes == [len(ROM)]
        assert rom_vf.seeks == [(0, 0)]
        assert bytes(rom_vf.data) == ROM
        assert state.load_vf_calls == 1


def test_rom_vfile_is_claimed_after_ownership_transfer():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        rom_vf = backend._rom_vfile
        # load_vf -> core.load_rom transferred the handle to the core; the
        # wrapper must be claimed and must never have closed it itself.
        assert rom_vf._claimed is True
        assert rom_vf.closed == 0


def test_temporary_save_vfile_retained_not_claimed():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        save_vf = backend._save_vfile
        # GBASavedataMask does not close the masked VFile, so the wrapper keeps
        # ownership: retained on the backend and left unclaimed.
        assert save_vf is not None
        assert backend._save_vfile is save_vf
        assert save_vf._claimed is False
        assert save_vf.closed == 0


def test_vfile_has_no_fromfile_constructor():
    import ast
    import inspect

    import febuildergba_playtest.mgba_backend as mod

    # The pinned VFile exposes only fromEmpty; a regression to a nonexistent
    # fromFile (or the unusable pure-Python vfs.open(BytesIO) path) must be
    # impossible. Structurally the fake lacks fromFile, and an AST scan of the
    # real code (docstrings/comments excluded) confirms the adapter never
    # references fromFile or calls vfs.open.
    assert hasattr(FakeVFile, "fromEmpty")
    assert not hasattr(FakeVFile, "fromFile")

    tree = ast.parse(inspect.getsource(mod))
    attrs = {node.attr for node in ast.walk(tree) if isinstance(node, ast.Attribute)}
    assert "fromEmpty" in attrs
    assert "load_vf" in attrs
    assert "fromFile" not in attrs
    vfs_open_calls = [
        node
        for node in ast.walk(tree)
        if isinstance(node, ast.Call)
        and isinstance(node.func, ast.Attribute)
        and node.func.attr == "open"
        and isinstance(node.func.value, ast.Name)
        and node.func.value.id == "vfs"
    ]
    assert vfs_open_calls == []


def test_config_defaults_applied_and_verified_from_opts():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        effective = backend.effective_config()
        assert effective["audioSync"] is False
        assert effective["videoSync"] is False
        assert effective["frameskip"] == 0
        assert effective["mute"] is True
        assert effective["useBios"] is False
        assert effective["skipBios"] is False
        assert effective["biosHle"] is True  # not useBios
        assert effective["autoloadSave"] is False
        # Verification must read the native opts, not core.config.
        opts = state.last_core._core.opts
        assert opts.mute is True
        assert opts.frameskip == 0
        assert opts.useBios is False
        assert opts.skipBios is False


def test_config_failure_is_fail_closed():
    state = _FakeState()
    # With the foreign-config mapping suppressed, mute stays at its zero default
    # (False) while the adapter requires True: verification must fail hard.
    with fake_mgba(state, break_config=True):
        backend = MgbaBackend()
        with pytest.raises(Exception):
            backend.load_rom(ROM)


def test_crash_callback_registered_on_core_callbacks_list():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        core = state.last_core
        assert len(core._callbacks.core_crashed) == 1
        assert backend.crash_message() is None
        # Firing the registered handler flips the backend into a crash state.
        core._callbacks.core_crashed[0]()
        assert backend.crash_message() == "core signalled crash"


def test_set_keys_uses_raw_keyword():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        backend.set_keys(0x1A)
        core = state.last_core
        assert core.key_calls == [((), {"raw": 0x1A})]


def test_memory_read_write_uses_width_views():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        backend.load_rom(ROM)
        backend.reset()
        backend.write("wram", 0x10, 8, 0xAB)
        backend.write("wram", 0x20, 16, 0x1234)
        backend.write("iwram", 0x04, 32, 0xDEADBEEF)
        assert backend.read("wram", 0x10, 8) == 0xAB
        assert backend.read("wram", 0x20, 16) == 0x1234
        assert backend.read("iwram", 0x04, 32) == 0xDEADBEEF
        core = state.last_core
        # Values must land in the width-specific view, not a store16 helper.
        assert core.memory.wram.u8._store[0x10] == 0xAB
        assert core.memory.wram.u16._store[0x20] == 0x1234
        assert core.memory.iwram.u32._store[0x04] == 0xDEADBEEF


def test_screenshot_uses_save_png_without_pillow():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend(want_screenshot=True)
        backend.load_rom(ROM)
        png = backend.screenshot_png()
        assert isinstance(png, bytes)
        assert png.startswith(b"\x89PNG")
        assert b"FAKEPNGDATA" in png


def test_screenshot_failure_raises_on_false_return():
    state = _FakeState()
    with fake_mgba(state, save_png_ok=False):
        backend = MgbaBackend(want_screenshot=True)
        backend.load_rom(ROM)
        with pytest.raises(Exception):
            backend.screenshot_png()


def test_version_and_commit_from_public_api():
    state = _FakeState()
    with fake_mgba(state):
        backend = MgbaBackend()
        assert backend.version() == PINNED_VERSION
        assert backend.commit() == PINNED_COMMIT


def test_check_available_reports_pinned_version():
    from febuildergba_playtest.mgba_backend import check_available

    state = _FakeState()
    with fake_mgba(state):
        info = check_available()
        assert info["available"] is True
        assert info["version"] == PINNED_VERSION
        assert info["commit"] == PINNED_COMMIT
        assert info["reason"] is None


def test_fake_api_shape_matches_pinned_names():
    """Guard: the modeled API must expose only the pinned names."""
    core = FakeCore()
    assert isinstance(core._callbacks.core_crashed, list)
    assert not hasattr(core, "add_callbacks")
    assert not hasattr(core, "load1")
    assert not hasattr(core.config, "get_bool")
    assert not hasattr(core.config, "load_defaults")
    # Effective options are verified from the native opts struct.
    opts = core._core.opts
    for name in ("audioSync", "videoSync", "frameskip", "mute", "useBios", "skipBios"):
        assert hasattr(opts, name)
    mem = FakeMemory()
    assert hasattr(mem, "u8") and hasattr(mem, "u16") and hasattr(mem, "u32")
    assert not hasattr(mem, "store16") and not hasattr(mem, "load32")
    assert hasattr(FakeImage(1, 1), "save_png")
    assert not hasattr(FakeImage(1, 1), "to_pil")
    # VFile exposes only the pinned fromEmpty constructor and the _claimed
    # ownership flag; there is no fromFile.
    vf = FakeVFile.fromEmpty()
    assert hasattr(vf, "_claimed") and vf._claimed is False
    assert not hasattr(FakeVFile, "fromFile")
