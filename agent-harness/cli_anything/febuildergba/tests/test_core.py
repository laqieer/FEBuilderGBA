"""Unit tests for cli-anything-febuildergba core modules.

All tests use synthetic data — no external dependencies required.
"""

import json
import os
import tempfile
import time

import pytest


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
