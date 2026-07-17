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
    manifest = _write_manifest(tmp_path, ["C:\\m1", "C:\\shared", "C:\\m2"])
    env = {
        mb._DLL_SEARCH_ENV: ";".join(
            ["C:\\e1", "c:\\SHARED", "C:\\e2"]
        )
    }
    got = mb._collect_dll_dirs(env, manifest)
    # Env override entries come first; duplicates collapse; order preserved.
    assert got == ["C:\\e1", "c:\\SHARED", "C:\\e2", "C:\\m1", "C:\\m2"]


def test_collect_dll_dirs_ignores_empty_env_segments(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    env = {mb._DLL_SEARCH_ENV: ";C:\\only;"}
    assert mb._collect_dll_dirs(env, manifest) == ["C:\\only"]


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
    manifest = _write_manifest(
        tmp_path, ["C:\\exists1", "C:\\missing", "C:\\exists2"]
    )
    existing = {"C:\\exists1", "C:\\exists2"}
    calls = []
    got = mb.prepare_native_library_search(
        register=lambda d: (calls.append(d), object())[1],
        isdir=lambda d: d in existing,
        is_windows=True,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["C:\\exists1", "C:\\exists2"]
    assert calls == ["C:\\exists1", "C:\\exists2"]


def test_prepare_native_library_search_uses_env_override(tmp_path):
    manifest = _write_manifest(tmp_path, ["C:\\from_manifest"])
    calls = []
    got = mb.prepare_native_library_search(
        register=lambda d: calls.append(d),
        isdir=lambda d: True,
        is_windows=True,
        environ={mb._DLL_SEARCH_ENV: "C:\\from_env"},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["C:\\from_env", "C:\\from_manifest"]


def test_prepare_native_library_search_survives_register_oserror(tmp_path):
    manifest = _write_manifest(tmp_path, ["C:\\a", "C:\\b"])

    def register(directory):
        if directory == "C:\\a":
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
    assert got == ["C:\\b"]


def test_prepare_native_library_search_no_register_available(tmp_path):
    manifest = _write_manifest(tmp_path, ["C:\\a"])
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


def test_manifest_path_falls_back_to_bootstrap_venv(tmp_path):
    package_manifest = mb._dll_manifest_path(isfile=lambda _path: False)
    build_root = tmp_path / ".mgba-build"
    interpreter = build_root / "venv" / "Scripts" / "python.exe"
    interpreter.parent.mkdir(parents=True)
    interpreter.write_bytes(b"")
    manifest = build_root / "mgba-dll-dirs.txt"
    manifest.write_text("C:\\runtime\n", encoding="utf-8")

    resolved = mb._dll_manifest_path(
        executable=str(interpreter),
        isfile=os.path.isfile,
    )

    assert resolved == str(manifest)
    assert resolved != package_manifest


def test_package_manifest_precedes_bootstrap_venv(monkeypatch, tmp_path):
    package_dir = tmp_path / "published" / "gba-playtest"
    module_path = package_dir / "febuildergba_playtest" / "mgba_backend.py"
    module_path.parent.mkdir(parents=True)
    package_manifest = package_dir / ".mgba-build" / "mgba-dll-dirs.txt"
    package_manifest.parent.mkdir()
    package_manifest.write_text("C:\\package\n", encoding="utf-8")

    build_root = tmp_path / "source" / ".mgba-build"
    interpreter = build_root / "venv" / "bin" / "python.exe"
    interpreter.parent.mkdir(parents=True)
    interpreter.write_bytes(b"")
    (build_root / "mgba-dll-dirs.txt").write_text(
        "C:\\venv\n",
        encoding="utf-8",
    )
    monkeypatch.setattr(mb, "__file__", str(module_path))

    assert mb._dll_manifest_path(executable=str(interpreter)) == str(
        package_manifest
    )


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


def test_read_manifest_invalid_utf8_is_empty(tmp_path):
    manifest = tmp_path / "mgba-dll-dirs.txt"
    manifest.write_bytes(b"C:\\ok\n\xff\n")
    assert mb._read_manifest_dirs(str(manifest)) == []


def test_collect_dll_dirs_ignores_oversize_env(tmp_path):
    manifest = _write_manifest(tmp_path, ["from_manifest"])
    huge = ";".join(["d"] * (mb._DLL_ENV_MAX_LEN))
    env = {mb._DLL_SEARCH_ENV: huge}
    assert len(env[mb._DLL_SEARCH_ENV]) > mb._DLL_ENV_MAX_LEN
    # The oversized override is dropped; only the manifest remains.
    assert mb._collect_dll_dirs(env, manifest) == ["from_manifest"]


def test_collect_dll_dirs_caps_env_entry_count(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    env = {mb._DLL_SEARCH_ENV: ";".join(f"e{i}" for i in range(mb._DLL_MAX_DIRS + 30))}
    got = mb._collect_dll_dirs(env, manifest)
    assert len(got) == mb._DLL_MAX_DIRS


def test_collect_dll_dirs_skips_overlong_env_entry(tmp_path):
    manifest = _write_manifest(tmp_path, [])
    long_entry = "y" * (mb._DLL_DIR_MAX_LEN + 5)
    env = {
        mb._DLL_SEARCH_ENV: ";".join(
            ["C:\\ok", long_entry, "C:\\ok2"]
        )
    }
    assert mb._collect_dll_dirs(env, manifest) == ["C:\\ok", "C:\\ok2"]


def test_prepare_native_library_search_is_idempotent_across_calls(tmp_path):
    manifest = _write_manifest(tmp_path, ["C:\\exists1", "C:\\exists2"])
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
    assert first == ["C:\\exists1", "C:\\exists2"]
    assert second == []
    assert calls == ["C:\\exists1", "C:\\exists2"]


def test_prepare_native_library_search_rejects_relative_dirs(tmp_path):
    manifest = _write_manifest(tmp_path, ["relative", "C:\\absolute"])
    calls = []
    got = mb.prepare_native_library_search(
        register=lambda directory: calls.append(directory),
        isdir=lambda directory: True,
        is_windows=True,
        environ={},
        manifest_path=manifest,
        seen_dirs=set(),
    )
    assert got == ["C:\\absolute"]
    assert calls == ["C:\\absolute"]


def test_prepare_native_library_search_caps_state_across_changing_calls(tmp_path):
    shared = set()
    calls = []
    for batch in range(3):
        manifest = _write_manifest(
            tmp_path,
            [
                f"C:\\batch{batch}\\d{index}"
                for index in range(mb._DLL_MAX_DIRS)
            ],
        )
        mb.prepare_native_library_search(
            register=lambda directory: (calls.append(directory), object())[1],
            isdir=lambda directory: True,
            is_windows=True,
            environ={},
            manifest_path=manifest,
            seen_dirs=shared,
        )
    assert len(shared) == mb._DLL_MAX_DIRS
    assert len(calls) == mb._DLL_MAX_DIRS
