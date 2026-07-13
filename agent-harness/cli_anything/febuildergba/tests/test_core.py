"""Unit tests for cli-anything-febuildergba core modules.

All tests use synthetic data — no external dependencies required.
"""

import json
import os
import subprocess
import tempfile
import time

import pytest


# ── Backend availability tests ────────────────────────────────────────

class TestBackend:
    def test_get_version_success(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 0,
                                             stdout="FEBuilderGBA 1.2.3\n", stderr="")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        assert backend.get_version() == "FEBuilderGBA 1.2.3"

    def test_get_version_rejects_failed_probe(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 7,
                                             stdout="", stderr="SDK unavailable")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        with pytest.raises(RuntimeError, match="exit code 7"):
            backend.get_version()

    def test_get_version_rejects_empty_probe(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 0,
                                             stdout=" \n", stderr="")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        with pytest.raises(RuntimeError, match="no version text"):
            backend.get_version()

    def test_check_backend_reports_probe_failure(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])

        def fail_version():
            raise RuntimeError("probe failed")

        monkeypatch.setattr(backend, "get_version", fail_version)
        result = backend.check_backend()
        assert result["available"] is False
        assert result["error"] == "probe failed"


# ── Session tests ─────────────────────────────────────────────────────

class TestSession:
    """Tests for core/session.py."""

    def test_create_default_session(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        assert not sess.is_open()

    def test_open_rom(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", 16777216)
        assert sess.is_open()
        assert sess.state.rom_path.endswith("rom.gba")
        assert sess.state.rom_version == "FE8U"
        assert sess.state.rom_size == 16777216

    def test_close_session(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        sess.close()
        assert not sess.is_open()
        assert not (tmp_path / "test_session.json").exists()

    def test_record_operation(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        sess.record_operation("data_export", {"table": "units"})
        assert len(sess.state.history) == 2  # open + data_export
        assert sess.state.history[-1]["op"] == "data_export"

    def test_mark_modified(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        assert not sess.state.modified
        sess.mark_modified()
        assert sess.state.modified

    def test_session_persistence(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")

        sess1 = Session(path)
        sess1.open_rom("/fake/rom.gba", "FE8U", 16777216)
        sess1.record_operation("lint", {})

        # Reload from disk
        sess2 = Session(path)
        assert sess2.is_open()
        assert sess2.state.rom_version == "FE8U"
        assert sess2.state.rom_size == 16777216
        assert len(sess2.state.history) == 2

    def test_history_limit(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        for i in range(150):
            sess.record_operation(f"op_{i}", {})
        assert len(sess.state.history) <= 100

    def test_info_output(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", 16777216, "FE8U")
        info = sess.info()
        assert "rom_path" in info
        assert "rom_version" in info
        assert info["rom_version"] == "FE8U"
        assert info["rom_size"] == 16777216
        assert "session_file" in info

    def test_empty_session_info(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        info = sess.info()
        assert info["rom_path"] == ""
        assert info["history_count"] == 0

    def test_corrupt_json_recovery(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_text("not valid json {{{")
        sess = Session(str(path))
        assert not sess.is_open()

    def test_force_version_stored(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", force_version="FE8U")
        assert sess.state.force_version == "FE8U"


# ── Project tests ─────────────────────────────────────────────────────

class TestProject:
    """Tests for core/project.py."""

    def test_list_tables(self):
        from cli_anything.febuildergba.core.project import list_tables
        tables = list_tables()
        assert len(tables) == 40
        assert "units" in tables
        assert "classes" in tables
        assert "items" in tables
        assert "portraits" in tables
        assert "map_settings" in tables

    def test_validate_rom_missing(self):
        from cli_anything.febuildergba.core.project import validate_rom
        assert not validate_rom("/nonexistent/rom.gba")

    def test_validate_rom_small_file(self, tmp_path):
        from cli_anything.febuildergba.core.project import validate_rom
        small = tmp_path / "small.gba"
        small.write_bytes(b"\x00" * 100)
        assert not validate_rom(str(small))

    def test_detect_version_fe8u(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        # Create fake ROM with FE8U game code at offset 0xAC
        rom = bytearray(0xB0)
        rom[0xAC:0xB0] = b"BE8E"
        path = tmp_path / "fe8u.gba"
        path.write_bytes(bytes(rom))
        assert _detect_version(str(path)) == "FE8U"

    def test_detect_version_fe7u(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        rom = bytearray(0xB0)
        rom[0xAC:0xB0] = b"AE7E"
        path = tmp_path / "fe7u.gba"
        path.write_bytes(bytes(rom))
        assert _detect_version(str(path)) == "FE7U"

    def test_detect_version_fe6(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        rom = bytearray(0xB0)
        rom[0xAC:0xB0] = b"AFEJ"
        path = tmp_path / "fe6.gba"
        path.write_bytes(bytes(rom))
        assert _detect_version(str(path)) == "FE6"

    def test_detect_version_forced(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        assert _detect_version("any_path", "FE8U") == "FE8U"

    def test_detect_version_unknown(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        rom = bytearray(0xB0)
        rom[0xAC:0xB0] = b"XXXX"
        path = tmp_path / "unknown.gba"
        path.write_bytes(bytes(rom))
        result = _detect_version(str(path))
        assert "unknown" in result


# ── Data tests ────────────────────────────────────────────────────────

class TestData:
    """Tests for core/data.py."""

    def test_read_tsv(self, tmp_path):
        from cli_anything.febuildergba.core.data import read_tsv
        tsv = tmp_path / "test.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n")
        rows = read_tsv(str(tsv))
        assert len(rows) == 2
        assert rows[0]["Name"] == "Eirika"
        assert rows[1]["HP"] == "30"

    def test_tsv_summary(self, tmp_path):
        from cli_anything.febuildergba.core.data import tsv_summary
        tsv = tmp_path / "test.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n2\tFranz\t20\n")
        summary = tsv_summary(str(tsv))
        assert summary["row_count"] == 3
        assert "ID" in summary["columns"]
        assert "Name" in summary["columns"]
        assert len(summary["preview"]) == 3

    def test_unknown_table_raises(self):
        from cli_anything.febuildergba.core.data import export_table
        with pytest.raises(ValueError, match="Unknown table"):
            export_table("fake.gba", "nonexistent_table", "out.tsv")

    def test_empty_tsv(self, tmp_path):
        from cli_anything.febuildergba.core.data import read_tsv
        tsv = tmp_path / "empty.tsv"
        tsv.write_text("ID\tName\n")
        rows = read_tsv(str(tsv))
        assert len(rows) == 0


# ── SessionState serialization tests ──────────────────────────────────

class TestSessionState:
    """Tests for SessionState dataclass."""

    def test_to_dict(self):
        from cli_anything.febuildergba.core.session import SessionState
        state = SessionState(rom_path="/test.gba", rom_version="FE8U")
        d = state.to_dict()
        assert d["rom_path"] == "/test.gba"
        assert d["rom_version"] == "FE8U"

    def test_from_dict(self):
        from cli_anything.febuildergba.core.session import SessionState
        d = {"rom_path": "/test.gba", "rom_version": "FE8U", "rom_size": 1024,
             "force_version": "", "created_at": 0.0, "updated_at": 0.0,
             "history": [], "modified": False}
        state = SessionState.from_dict(d)
        assert state.rom_path == "/test.gba"
        assert state.rom_version == "FE8U"

    def test_from_dict_extra_keys(self):
        from cli_anything.febuildergba.core.session import SessionState
        d = {"rom_path": "/test.gba", "rom_version": "FE8U", "rom_size": 0,
             "force_version": "", "created_at": 0.0, "updated_at": 0.0,
             "history": [], "modified": False, "extra_key": "ignored"}
        state = SessionState.from_dict(d)
        assert state.rom_path == "/test.gba"


# ── ROM header tests ──────────────────────────────────────────────────

class TestRomHeader:
    """Tests for core/project.py rom_header."""

    def test_header_fe8u(self, tmp_path):
        from cli_anything.febuildergba.core.project import rom_header
        rom = bytearray(0x100000)  # 1 MB min
        rom[0xA0:0xAC] = b"FIRE EMBLEM\x00"
        rom[0xAC:0xB0] = b"BE8E"
        rom[0xB0:0xB2] = b"01"
        rom[0xB2] = 0x96
        rom[0xB3] = 0x00
        rom[0xB4] = 0x00
        rom[0xBC] = 0x01
        rom[0xBD] = 0x42
        path = tmp_path / "fe8u.gba"
        path.write_bytes(bytes(rom))
        result = rom_header(str(path))
        assert result["game_code"] == "BE8E"
        assert result["title"].startswith("FIRE EMBLEM")
        assert result["maker_code"] == "01"
        assert result["software_version"] == 1
        assert result["header_checksum"] == 0x42

    def test_header_missing_file(self):
        from cli_anything.febuildergba.core.project import rom_header
        with pytest.raises(FileNotFoundError):
            rom_header("/nonexistent.gba")


# ── ROM save tests ────────────────────────────────────────────────────

class TestRomSave:
    """Tests for core/project.py save_rom."""

    def test_save_rom(self, tmp_path):
        from cli_anything.febuildergba.core.project import save_rom
        src = tmp_path / "input.gba"
        src.write_bytes(b"\x00" * 1024)
        dst = str(tmp_path / "output.gba")
        result = save_rom(str(src), dst)
        assert result["output_path"] == dst
        assert result["file_size"] == 1024
        assert os.path.isfile(dst)

    def test_save_rom_missing(self):
        from cli_anything.febuildergba.core.project import save_rom
        with pytest.raises(FileNotFoundError):
            save_rom("/nonexistent.gba", "/out.gba")


# ── Data diff tests ───────────────────────────────────────────────────

class TestDataDiff:
    """Tests for core/data.py diff_tsv."""

    def test_identical_files(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        tsv = tmp_path / "a.tsv"
        tsv.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        result = diff_tsv(str(tsv), str(tsv))
        assert result["unchanged_count"] == 2
        assert len(result["added_rows"]) == 0
        assert len(result["removed_rows"]) == 0
        assert len(result["changed_rows"]) == 0

    def test_added_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\n0\tEirika\n")
        b.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["added_rows"]) == 1
        assert result["added_rows"][0]["ID"] == "1"

    def test_removed_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        b.write_text("ID\tName\n0\tEirika\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["removed_rows"]) == 1

    def test_changed_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\tHP\n0\tEirika\t16\n")
        b.write_text("ID\tName\tHP\n0\tEirika\t20\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["changed_rows"]) == 1


# ── Data lookup tests ─────────────────────────────────────────────────

class TestDataLookup:
    """Tests for core/data.py lookup_entry."""

    def test_lookup_found(self, tmp_path):
        from cli_anything.febuildergba.core.data import lookup_entry
        tsv = tmp_path / "units.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n")
        result = lookup_entry(str(tsv), "1")
        assert result["found"]
        assert result["row"]["Name"] == "Seth"

    def test_lookup_not_found(self, tmp_path):
        from cli_anything.febuildergba.core.data import lookup_entry
        tsv = tmp_path / "units.tsv"
        tsv.write_text("ID\tName\n0\tEirika\n")
        result = lookup_entry(str(tsv), "99")
        assert not result["found"]

    def test_lookup_missing_file(self):
        from cli_anything.febuildergba.core.data import lookup_entry
        with pytest.raises(FileNotFoundError):
            lookup_entry("/nonexistent.tsv", "0")


# ── Patch list tests ──────────────────────────────────────────────────

class TestPatchList:
    """Tests for core/patches.py list_patches."""

    def test_list_patches(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        ver_dir = tmp_path / "config" / "patch2" / "FE8U"
        ver_dir.mkdir(parents=True)
        (ver_dir / "PATCH_Test.txt").write_text(
            "NAME=Test Patch\nINFO=A test patch\n"
        )
        result = list_patches(str(tmp_path / "config"), "FE8U")
        assert result["count"] >= 1
        assert any(p["name"] == "Test Patch" for p in result["patches"])
        assert any(p["info"] == "A test patch" for p in result["patches"])

    def test_list_patches_empty(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        ver_dir = tmp_path / "config" / "patch2" / "FE8U"
        ver_dir.mkdir(parents=True)
        result = list_patches(str(tmp_path / "config"), "FE8U")
        assert result["count"] == 0

    def test_list_patches_missing_dir(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        result = list_patches(str(tmp_path / "nonexistent"), "FE8U")
        assert result["count"] == 0
