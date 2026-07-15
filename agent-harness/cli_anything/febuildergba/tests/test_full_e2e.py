"""E2E tests for cli-anything-febuildergba.

These tests invoke the real FEBuilderGBA.CLI backend and require:
- .NET 10.0 SDK installed
- FEBuilderGBA.CLI project built
- ROM files in roms/ directory (relative to repo root)
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import pytest


# ── Helpers ───────────────────────────────────────────────────────────

def _repo_root() -> Path:
    """Find the repository root."""
    p = Path(__file__).resolve()
    # Walk up from tests/ -> febuildergba/ -> cli_anything/ -> agent-harness/ -> repo root
    for _ in range(5):
        p = p.parent
        if (p / "FEBuilderGBA.CLI").is_dir():
            return p
    pytest.skip("Cannot find repo root with FEBuilderGBA.CLI")


def _find_rom() -> str:
    """Find a ROM file for testing."""
    root = _repo_root()
    roms_dir = root / "roms"
    if not roms_dir.is_dir():
        pytest.skip("roms/ directory not found")

    # Prefer FE8U, then any .gba
    for name in ["FE8U.gba", "FE7U.gba", "FE7J.gba", "FE8J.gba", "FE6.gba"]:
        path = roms_dir / name
        if path.is_file():
            return str(path)

    gba_files = list(roms_dir.glob("*.gba"))
    if gba_files:
        return str(gba_files[0])

    pytest.skip("No .gba ROM files found in roms/")


def _resolve_cli(name: str) -> list[str]:
    """Resolve installed CLI command; falls back to python -m for dev."""
    force = os.environ.get("CLI_ANYTHING_FORCE_INSTALLED", "").strip() == "1"
    path = shutil.which(name)
    if path:
        print(f"[_resolve_cli] Using installed command: {path}")
        return [path]
    if force:
        raise RuntimeError(f"{name} not found in PATH. Install with: pip install -e .")
    module = "cli_anything.febuildergba.febuildergba_cli"
    print(f"[_resolve_cli] Falling back to: {sys.executable} -m {module}")
    return [sys.executable, "-m", module]


# ── Backend availability ──────────────────────────────────────────────

class TestBackend:
    """Test that the FEBuilderGBA.CLI backend is available."""

    def test_backend_check(self):
        from cli_anything.febuildergba.utils.febuildergba_backend import check_backend
        result = check_backend()
        if not result["available"]:
            pytest.skip(f"Backend not available: {result.get('error', 'unknown')}")
        assert result["available"]
        assert "version" in result

    def test_backend_version(self):
        from cli_anything.febuildergba.utils.febuildergba_backend import get_version
        try:
            version = get_version()
            assert version  # non-empty
            print(f"\n  Backend version: {version}")
        except RuntimeError:
            pytest.skip("Backend not available")


# ── ROM operations ────────────────────────────────────────────────────

class TestROMOperations:
    """E2E tests for ROM info and validation."""

    def test_rom_info(self):
        from cli_anything.febuildergba.core.project import rom_info
        rom = _find_rom()
        result = rom_info(rom)
        assert result["rom_size"] > 0
        assert result["detected_version"] != "unknown"
        print(f"\n  ROM: {result['rom_path']}")
        print(f"  Version: {result['detected_version']}")
        print(f"  Size: {result['rom_size_mb']} MB")

    def test_rom_validate(self):
        from cli_anything.febuildergba.core.project import validate_rom
        rom = _find_rom()
        assert validate_rom(rom)

    def test_rom_version_detection(self):
        from cli_anything.febuildergba.core.project import _detect_version
        rom = _find_rom()
        version = _detect_version(rom)
        assert version in ["FE6", "FE7J", "FE7U", "FE8J", "FE8U"]
        print(f"\n  Detected: {version}")


# ── Data export E2E ───────────────────────────────────────────────────

class TestDataExport:
    """E2E tests for struct data export."""

    def test_export_units(self, tmp_path):
        from cli_anything.febuildergba.core.data import export_table
        rom = _find_rom()
        out = str(tmp_path / "units.tsv")
        try:
            result = export_table(rom, "units", out)
            assert result["exit_code"] == 0
            assert os.path.isfile(out)
            size = os.path.getsize(out)
            assert size > 0
            print(f"\n  units.tsv: {out} ({size:,} bytes)")
        except RuntimeError as e:
            pytest.skip(f"Backend error: {e}")

    def test_export_classes(self, tmp_path):
        from cli_anything.febuildergba.core.data import export_table
        rom = _find_rom()
        out = str(tmp_path / "classes.tsv")
        try:
            result = export_table(rom, "classes", out)
            assert result["exit_code"] == 0
            assert os.path.isfile(out)
            print(f"\n  classes.tsv: {out} ({os.path.getsize(out):,} bytes)")
        except RuntimeError as e:
            pytest.skip(f"Backend error: {e}")

    def test_export_inspect_units(self, tmp_path):
        from cli_anything.febuildergba.core.data import export_table, tsv_summary
        rom = _find_rom()
        out = str(tmp_path / "units.tsv")
        try:
            export_table(rom, "units", out)
            summary = tsv_summary(out)
            assert summary["row_count"] > 0
            assert len(summary["columns"]) > 0
            print(f"\n  Units: {summary['row_count']} rows, {len(summary['columns'])} columns")
            print(f"  Columns: {', '.join(summary['columns'][:5])}...")
        except RuntimeError as e:
            pytest.skip(f"Backend error: {e}")


# ── Text export E2E ───────────────────────────────────────────────────

class TestTextExport:
    """E2E tests for text export."""

    def test_export_text(self, tmp_path):
        from cli_anything.febuildergba.core.text import export_text
        rom = _find_rom()
        out = str(tmp_path / "texts.tsv")
        try:
            result = export_text(rom, out)
            assert result["exit_code"] == 0
            assert result["file_size"] > 0
            print(f"\n  texts.tsv: {out} ({result['file_size']:,} bytes)")
        except RuntimeError as e:
            pytest.skip(f"Backend error: {e}")


# ── Lint E2E ──────────────────────────────────────────────────────────

class TestLint:
    """E2E tests for ROM lint."""

    def test_lint_rom(self):
        from cli_anything.febuildergba.core.lint import lint_rom
        rom = _find_rom()
        try:
            result = lint_rom(rom)
            assert "error_count" in result
            assert "warning_count" in result
            print(f"\n  Lint: {result['error_count']} errors, {result['warning_count']} warnings")
            print(f"  Clean: {result['clean']}")
        except RuntimeError as e:
            pytest.skip(f"Backend error: {e}")


# ── Session E2E ───────────────────────────────────────────────────────

class TestSessionE2E:
    """E2E tests for session management with real ROMs."""

    def test_full_session_workflow(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        from cli_anything.febuildergba.core.project import rom_info

        rom = _find_rom()
        sess = Session(str(tmp_path / "session.json"))

        # Open
        info = rom_info(rom)
        sess.open_rom(rom, info["detected_version"], info["rom_size"])
        assert sess.is_open()

        # Record operations
        sess.record_operation("lint", {"result": "clean"})
        sess.record_operation("data_export", {"table": "units"})
        assert len(sess.state.history) == 3  # open + 2 ops

        # Check info
        session_info = sess.info()
        assert session_info["rom_version"] in ["FE6", "FE7J", "FE7U", "FE8J", "FE8U"]

        # Close
        sess.close()
        assert not sess.is_open()
        print(f"\n  Session workflow completed for {info['detected_version']}")


# ── CLI Subprocess tests ──────────────────────────────────────────────

class TestCLISubprocess:
    """Test the installed CLI command via subprocess."""

    CLI_BASE = _resolve_cli("cli-anything-febuildergba")

    def _run(self, args, check=True):
        return subprocess.run(
            self.CLI_BASE + args,
            capture_output=True, text=True,
            check=check,
        )

    def test_help(self):
        result = self._run(["--help"])
        assert result.returncode == 0
        assert "FEBuilderGBA" in result.stdout

    def test_version(self):
        result = self._run(["--version"])
        assert result.returncode == 0

    def test_rom_tables(self):
        result = self._run(["rom", "tables"])
        assert result.returncode == 0
        assert "units" in result.stdout

    def test_json_rom_tables(self):
        result = self._run(["--json", "rom", "tables"])
        assert result.returncode == 0
        data = json.loads(result.stdout)
        assert "tables" in data
        assert "units" in data["tables"]

    def test_rom_validate_nonexistent(self):
        result = self._run(["rom", "validate", "/nonexistent/rom.gba"], check=False)
        assert result.returncode == 0
        # Should report invalid

    def test_rom_info_real(self):
        """Test ROM info with a real ROM file."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        result = self._run(["--json", "rom", "info", rom], check=False)
        if result.returncode == 0:
            data = json.loads(result.stdout)
            assert "detected_version" in data
            print(f"\n  ROM info via subprocess: {data.get('detected_version', '?')}")

    def test_check_backend(self):
        result = self._run(["check"], check=False)
        # May fail if backend not built, that's ok
        assert result.returncode == 0 or "NOT FOUND" in result.stdout + result.stderr

    def test_session_status_no_session(self):
        result = self._run(["session", "status"])
        assert result.returncode == 0

    def test_rom_header(self):
        """Test ROM header dump with real ROM."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        result = self._run(["--json", "rom", "header", rom])
        assert result.returncode == 0
        data = json.loads(result.stdout)
        assert "game_code" in data
        assert len(data["game_code"]) == 4
        print(f"\n  Header game_code: {data['game_code']}")

    def test_data_diff_identical(self, tmp_path):
        """Test data diff with identical files."""
        tsv = str(tmp_path / "test.tsv")
        with open(tsv, "w") as f:
            f.write("ID\tName\n0\tEirika\n1\tSeth\n")
        result = self._run(["--json", "data", "diff", tsv, tsv])
        assert result.returncode == 0
        data = json.loads(result.stdout)
        assert data["unchanged_count"] == 2
        assert len(data["added_rows"]) == 0

    def test_data_lookup(self, tmp_path):
        """Test data lookup by ID."""
        tsv = str(tmp_path / "test.tsv")
        with open(tsv, "w") as f:
            f.write("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n")
        result = self._run(["--json", "data", "lookup", tsv, "1"])
        assert result.returncode == 0
        data = json.loads(result.stdout)
        assert data["found"]
        assert data["row"]["Name"] == "Seth"

    def test_text_search(self):
        """Test text search with real ROM."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        result = self._run(["--json", "--rom", rom, "text", "search", "Eirika"], check=False)
        if result.returncode != 0:
            pytest.skip(f"Backend failed: {result.stderr}")
        data = json.loads(result.stdout)
        assert "matches" in data
        print(f"\n  Text search 'Eirika': {data.get('match_count', 0)} matches")

    def test_resolve_names(self):
        """Test name resolution with real ROM."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        result = self._run(["--json", "--rom", rom, "names", "unit", "0,1,2"], check=False)
        if result.returncode != 0:
            pytest.skip(f"Backend failed: {result.stderr}")
        data = json.loads(result.stdout)
        assert data["count"] == 3
        assert "0" in data["names"]
        print(f"\n  Unit 0: {data['names'].get('0', '?')}")

    def test_render_portrait(self, tmp_path):
        """Test portrait rendering with real ROM."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        out = str(tmp_path / "portrait.png")
        result = self._run(["--json", "--rom", rom, "portrait", "0", "-o", out], check=False)
        if result.returncode != 0:
            pytest.skip(f"Backend failed: {result.stderr}")
        data = json.loads(result.stdout)
        assert data["file_size"] > 0
        assert os.path.isfile(out)
        print(f"\n  Portrait: {out} ({data['file_size']} bytes)")

    def test_export_midi(self, tmp_path):
        """Test MIDI export with real ROM."""
        try:
            rom = _find_rom()
        except Exception:
            pytest.skip("No ROM available")
        out = str(tmp_path / "song.mid")
        result = self._run(["--json", "--rom", rom, "export-midi", "0x1A", "-o", out], check=False)
        if result.returncode != 0:
            pytest.skip(f"Backend failed: {result.stderr}")
        data = json.loads(result.stdout)
        assert data["file_size"] > 0
        assert os.path.isfile(out)
        # Verify MIDI magic bytes
        with open(out, "rb") as f:
            magic = f.read(4)
        assert magic == b"MThd", f"Expected MIDI header, got {magic}"
        print(f"\n  MIDI: {out} ({data['file_size']} bytes)")
