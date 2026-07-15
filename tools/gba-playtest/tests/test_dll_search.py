"""Tests for the Windows native-library (DLL) discovery strategy.

The strategy is deterministic and shared with the bootstrap: the build script
records DLL directories in ``.mgba-build/mgba-dll-dirs.txt`` (plus an optional
``FEBUILDERGBA_MGBA_DLL_DIRS`` override), and the adapter registers each
existing directory with ``os.add_dll_directory`` before importing ``mgba``.

Every argument of :func:`prepare_native_library_search` is injectable so the
behaviour is exercised without a real binding, without Windows, and without
mutating the interpreter's real DLL search path.
"""

import os

from febuildergba_playtest import mgba_backend as mb


def _write_manifest(tmp_path, lines):
    manifest = tmp_path / "mgba-dll-dirs.txt"
    manifest.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return str(manifest)


def test_read_manifest_skips_blanks_and_comments(tmp_path):
    manifest = _write_manifest(tmp_path, ["C:\\a", "", "# comment", "  ", "C:\\b"])
    assert mb._read_manifest_dirs(manifest) == ["C:\\a", "C:\\b"]


def test_read_manifest_missing_file_is_empty(tmp_path):
    missing = str(tmp_path / "nope.txt")
    assert mb._read_manifest_dirs(missing) == []


def test_collect_dll_dirs_env_first_then_manifest_ordered_dedup(tmp_path):
    manifest = _write_manifest(tmp_path, ["m1", "shared", "m2"])
    env = {mb._DLL_SEARCH_ENV: os.pathsep.join(["e1", "shared", "e2"])}
    got = mb._collect_dll_dirs(env, manifest)
    # Env override entries come first; duplicates collapse; order preserved.
    assert got == ["e1", "shared", "e2", "m1", "m2"]


def test_collect_dll_dirs_ignores_empty_env_segments(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    env = {mb._DLL_SEARCH_ENV: os.pathsep + "only" + os.pathsep}
    assert mb._collect_dll_dirs(env, manifest) == ["only"]


def test_prepare_native_library_search_noop_on_posix(tmp_path):
    calls = []
    manifest = _write_manifest(tmp_path, ["anything"])
    got = mb.prepare_native_library_search(
        register=lambda d: calls.append(d),
        isdir=lambda d: True,
        is_windows=False,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == []
    assert calls == []


def test_prepare_native_library_search_registers_existing_dirs_only(tmp_path):
    manifest = _write_manifest(tmp_path, ["exists1", "missing", "exists2"])
    existing = {"exists1", "exists2"}
    calls = []
    got = mb.prepare_native_library_search(
        register=lambda d: (calls.append(d), object())[1],
        isdir=lambda d: d in existing,
        is_windows=True,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["exists1", "exists2"]
    assert calls == ["exists1", "exists2"]


def test_prepare_native_library_search_uses_env_override(tmp_path):
    manifest = _write_manifest(tmp_path, ["from_manifest"])
    calls = []
    got = mb.prepare_native_library_search(
        register=lambda d: calls.append(d),
        isdir=lambda d: True,
        is_windows=True,
        environ={mb._DLL_SEARCH_ENV: "from_env"},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["from_env", "from_manifest"]


def test_prepare_native_library_search_survives_register_oserror(tmp_path):
    manifest = _write_manifest(tmp_path, ["a", "b"])

    def register(directory):
        if directory == "a":
            raise OSError("cannot add")
        return object()

    got = mb.prepare_native_library_search(
        register=register,
        isdir=lambda d: True,
        is_windows=True,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["b"]


def test_prepare_native_library_search_no_register_available(tmp_path):
    manifest = _write_manifest(tmp_path, ["a"])
    got = mb.prepare_native_library_search(
        register=None,
        isdir=lambda d: False,  # nothing is registerable regardless of API
        is_windows=True,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == []


def test_manifest_path_points_under_mgba_build():
    path = mb._dll_manifest_path()
    assert path.endswith(os.path.join(".mgba-build", "mgba-dll-dirs.txt"))
    # Anchored at the tool dir (parent of the package dir), not the package dir.
    pkg_dir = os.path.dirname(os.path.abspath(mb.__file__))
    tool_dir = os.path.dirname(pkg_dir)
    assert path == os.path.join(tool_dir, ".mgba-build", "mgba-dll-dirs.txt")


# --- Bounds and idempotency (defence against unbounded/hostile input) -------


def test_read_manifest_ignores_oversize_file(tmp_path):
    manifest = tmp_path / "mgba-dll-dirs.txt"
    manifest.write_text("C:\\a\n" * (mb._DLL_MANIFEST_MAX_BYTES // 5 + 100), encoding="utf-8")
    assert manifest.stat().st_size > mb._DLL_MANIFEST_MAX_BYTES
    assert mb._read_manifest_dirs(str(manifest)) == []


def test_read_manifest_caps_entry_count(tmp_path):
    manifest = _write_manifest(tmp_path, [f"C:\\d{i}" for i in range(mb._DLL_MAX_DIRS + 20)])
    got = mb._read_manifest_dirs(str(manifest))
    assert len(got) == mb._DLL_MAX_DIRS


def test_read_manifest_skips_overlong_entry(tmp_path):
    long_entry = "C:\\" + ("x" * (mb._DLL_DIR_MAX_LEN + 10))
    manifest = _write_manifest(tmp_path, ["C:\\ok", long_entry, "C:\\ok2"])
    assert mb._read_manifest_dirs(str(manifest)) == ["C:\\ok", "C:\\ok2"]


def test_collect_dll_dirs_ignores_oversize_env(tmp_path):
    manifest = _write_manifest(tmp_path, ["from_manifest"])
    huge = os.pathsep.join(["d"] * (mb._DLL_ENV_MAX_LEN))
    env = {mb._DLL_SEARCH_ENV: huge}
    assert len(env[mb._DLL_SEARCH_ENV]) > mb._DLL_ENV_MAX_LEN
    # The oversized override is dropped; only the manifest remains.
    assert mb._collect_dll_dirs(env, manifest) == ["from_manifest"]


def test_collect_dll_dirs_caps_env_entry_count(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    env = {mb._DLL_SEARCH_ENV: os.pathsep.join(f"e{i}" for i in range(mb._DLL_MAX_DIRS + 30))}
    got = mb._collect_dll_dirs(env, manifest)
    assert len(got) == mb._DLL_MAX_DIRS


def test_collect_dll_dirs_skips_overlong_env_entry(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    long_entry = "y" * (mb._DLL_DIR_MAX_LEN + 5)
    env = {mb._DLL_SEARCH_ENV: os.pathsep.join(["ok", long_entry, "ok2"])}
    assert mb._collect_dll_dirs(env, manifest) == ["ok", "ok2"]


def test_prepare_native_library_search_is_idempotent_across_calls(tmp_path):
    manifest = _write_manifest(tmp_path, ["exists1", "exists2"])
    shared = set()
    calls = []

    def register(directory):
        calls.append(directory)
        return object()

    first = mb.prepare_native_library_search(
        register=register, isdir=lambda d: True, is_windows=True,
        environ={}, manifest_path=manifest, seen_dirs=shared,
    )
    second = mb.prepare_native_library_search(
        register=register, isdir=lambda d: True, is_windows=True,
        environ={}, manifest_path=manifest, seen_dirs=shared,
    )
    # First call registers both; the second registers nothing (dedup) so the
    # underlying handle set cannot grow without bound across repeated calls.
    assert first == ["exists1", "exists2"]
    assert second == []
    assert calls == ["exists1", "exists2"]
