"""Tests for the additional CLI-verb wrappers (issue #1933).

Two layers:
- Unit tests monkeypatch ``core.verbs.run_cli`` to assert each wrapper builds
  the correct backend arg-list and shapes its JSON result — no CLI invoked.
- E2E tests call the wrappers with the real backend + a real ROM, skip-gated on
  backend/ROM availability (roms/FE8U.gba). They never mutate a source ROM
  (repair-header runs on a temp copy).
"""

import os
import shutil
import tempfile
from pathlib import Path
from types import SimpleNamespace

import pytest
from click.testing import CliRunner

from cli_anything.febuildergba import febuildergba_cli
from cli_anything.febuildergba.core import verbs


# ── Unit-test scaffolding: fake run_cli ───────────────────────────────

class _Recorder:
    """Captures the args passed to run_cli and returns a canned result."""

    def __init__(self, returncode=0, stdout="", stderr=""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.args = None

    def __call__(self, args, **kwargs):
        self.args = args
        return SimpleNamespace(
            returncode=self.returncode, stdout=self.stdout, stderr=self.stderr
        )


@pytest.fixture
def rec(monkeypatch):
    r = _Recorder()
    monkeypatch.setattr("cli_anything.febuildergba.core.verbs.run_cli", r)
    return r


# ── Unit tests: one per verb ──────────────────────────────────────────

class TestChecksumUnit:
    def test_valid(self, rec):
        rec.returncode = 0
        rec.stdout = ("ROM: r.gba (16777216 bytes)\nTitle: T\nGame Code: BE8E\n"
                      "Header checksum: 0x91 (expected: 0x91)\nStatus: VALID")
        result = verbs.checksum("r.gba")
        assert rec.args == ["--checksum", "--rom=r.gba"]
        assert result["valid"] is True
        assert result["actual"] == "0x91"
        assert result["expected"] == "0x91"
        assert result["exit_code"] == 0

    def test_invalid_is_not_an_error(self, rec):
        # exit 2 = checked, header INVALID — must be structured, not raised.
        rec.returncode = 2
        rec.stdout = ("Header checksum: 0xAB (expected: 0xCD)\n"
                      "Status: INVALID (use --repair-header to fix)")
        result = verbs.checksum("r.gba")
        assert result["valid"] is False
        assert result["actual"] == "0xAB"
        assert result["expected"] == "0xCD"
        assert result["exit_code"] == 2

    def test_file_error_valid_is_none(self, rec):
        rec.returncode = 1
        rec.stderr = "Error: ROM not found"
        result = verbs.checksum("nope.gba")
        assert result["valid"] is None

    def test_force_version_arg(self, rec):
        verbs.checksum("r.gba", "FE8U")
        assert "--force-version=FE8U" in rec.args


class TestRepairHeaderUnit:
    def test_repaired(self, rec):
        rec.returncode = 0
        rec.stdout = "Repaired header checksum: 0xAB -> 0x9D\nSaved: r.gba"
        result = verbs.repair_header("r.gba")
        assert rec.args == ["--repair-header", "--rom=r.gba"]
        assert result["repaired"] is True
        assert result["already_valid"] is False

    def test_already_valid_is_noop(self, rec):
        rec.returncode = 0
        rec.stdout = "Header checksum already valid (0x9D). No repair needed."
        result = verbs.repair_header("r.gba")
        assert result["already_valid"] is True
        assert result["repaired"] is False


class TestRomDiffUnit:
    def test_identical(self, rec):
        rec.returncode = 0
        rec.stdout = "ROMs are identical."
        result = verbs.rom_diff("a.gba", "b.gba")
        assert rec.args == ["--diff", "--rom=a.gba", "--rom2=b.gba"]
        assert result["identical"] is True
        assert result["bytes_differ"] == 0
        assert result["regions"] == 0

    def test_differ(self, rec):
        rec.returncode = 0
        rec.stdout = ("0x00000100-0x00000200 (256 bytes changed)\n"
                      "Total: 256 bytes differ across 1 region(s)")
        result = verbs.rom_diff("a.gba", "b.gba", "d.tsv")
        assert "--out=d.tsv" in rec.args
        assert result["identical"] is False
        assert result["bytes_differ"] == 256
        assert result["regions"] == 1

    def test_error_identical_is_none(self, rec):
        rec.returncode = 1
        result = verbs.rom_diff("a.gba", "b.gba")
        assert result["identical"] is None


class TestExportMapSettingsRawUnit:
    def test_args(self, rec):
        verbs.export_map_settings_raw("r.gba", "m.tsv")
        assert rec.args == ["--export-map-settings", "--rom=r.gba", "--out=m.tsv"]


class TestImportMidiUnit:
    def test_args(self, rec):
        verbs.import_midi("r.gba", "1A", "song.mid")
        assert rec.args == ["--import-midi", "--rom=r.gba",
                            "--song-id=1A", "--in=song.mid"]


class TestPaletteUnit:
    def test_export_with_colors(self, rec):
        verbs.export_palette("r.gba", "0x5524", "p.pal", colors=16)
        assert rec.args == ["--export-palette", "--rom=r.gba",
                            "--addr=0x5524", "--out=p.pal", "--colors=16"]

    def test_export_without_colors_omits_flag(self, rec):
        verbs.export_palette("r.gba", "0x5524", "p.pal")
        assert "--colors=0" not in rec.args
        assert not any(a.startswith("--colors") for a in rec.args)

    def test_import_args(self, rec):
        verbs.import_palette("r.gba", "0x5524", "p.pal")
        assert rec.args == ["--import-palette", "--rom=r.gba",
                            "--addr=0x5524", "--in=p.pal"]


class TestCompileEventUnit:
    def test_args_with_out(self, rec):
        verbs.compile_event("r.gba", "s.event", "out.gba")
        assert rec.args == ["--compile-event", "--rom=r.gba",
                            "--in=s.event", "--out=out.gba"]

    def test_args_without_out_omits_flag(self, rec):
        verbs.compile_event("r.gba", "s.event")
        assert not any(a.startswith("--out") for a in rec.args)


class TestLZ77Unit:
    """core.verbs.lz77_file (issue #1942) — no ROM/--rom involved."""

    def test_compress_args(self, rec):
        verbs.lz77_file("compress", "in.bin", "out.lz")
        assert rec.args == ["--lz77", "--in=in.bin", "--out=out.lz", "--compress"]

    def test_decompress_args(self, rec):
        verbs.lz77_file("decompress", "in.lz", "out.bin")
        assert rec.args == ["--lz77", "--in=in.lz", "--out=out.bin", "--decompress"]

    def test_invalid_mode_raises_without_calling_backend(self, rec):
        with pytest.raises(ValueError):
            verbs.lz77_file("frobnicate", "in.bin", "out.bin")
        assert rec.args is None  # run_cli must never be invoked

    def test_result_shape(self, rec, tmp_path):
        out = tmp_path / "out.lz"
        out.write_bytes(b"\x10\x03\x00\x00AB")
        rec.returncode = 0
        rec.stdout = "Compressed: out.lz (6 bytes, 200%)"
        result = verbs.lz77_file("compress", "in.bin", str(out))
        assert result["mode"] == "compress"
        assert result["input_path"] == "in.bin"
        assert result["output_path"] == str(out)
        assert result["file_size"] == 6
        assert result["exit_code"] == 0


class TestLZ77ClickLayer:
    def test_requires_compress_or_decompress(self, monkeypatch):
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.lz77_file",
                            lambda *a, **k: pytest.fail("should not be called"))
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["lz77", "-i", "in.bin", "-o", "out.bin"])
        assert res.exit_code != 0

    def test_rejects_both_flags(self, monkeypatch):
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.lz77_file",
                            lambda *a, **k: pytest.fail("should not be called"))
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["lz77", "-i", "in.bin", "-o", "out.bin",
                                  "--compress", "--decompress"])
        assert res.exit_code != 0

    def test_compress_success(self, monkeypatch):
        def fake(mode, in_path, out_path):
            assert mode == "compress"
            return {"mode": mode, "input_path": in_path, "output_path": out_path,
                    "file_size": 4, "exit_code": 0, "stdout": "ok", "stderr": ""}
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.lz77_file", fake)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["--json", "lz77", "-i", "in.bin", "-o", "out.bin", "--compress"])
        assert res.exit_code == 0, res.output
        assert '"mode": "compress"' in res.output

    def test_backend_failure_raises(self, monkeypatch):
        def fake(mode, in_path, out_path):
            return {"mode": mode, "input_path": in_path, "output_path": out_path,
                    "file_size": 0, "exit_code": 1, "stdout": "", "stderr": "bad input"}
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.lz77_file", fake)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["lz77", "-i", "in.bin", "-o", "out.bin", "--decompress"])
        assert res.exit_code != 0


class TestLZ77RealBackendRoundtrip:
    """Synthetic (no ROM) compress/decompress roundtrip against the real
    backend. Skip-gated ONLY on backend availability — never on a data
    mismatch (a genuine roundtrip failure must fail the test).
    """

    def test_compress_decompress_roundtrip(self, tmp_path):
        from cli_anything.febuildergba.utils.febuildergba_backend import check_backend
        if not check_backend().get("available"):
            pytest.skip("FEBuilderGBA.CLI backend not available (build it first)")

        original = tmp_path / "payload.bin"
        # Synthetic, ROM-free payload with some repetition (compressible).
        payload = (b"Hello FEBuilderGBA LZ77 roundtrip! " * 8) + bytes(range(64))
        original.write_bytes(payload)

        compressed = tmp_path / "payload.lz"
        decompressed = tmp_path / "payload.out"

        comp_result = verbs.lz77_file("compress", str(original), str(compressed))
        assert comp_result["exit_code"] == 0, comp_result["stderr"]
        assert compressed.is_file()

        decomp_result = verbs.lz77_file("decompress", str(compressed), str(decompressed))
        assert decomp_result["exit_code"] == 0, decomp_result["stderr"]
        assert decompressed.read_bytes() == payload


# ── Click-layer regression guard (checksum exit-2 must NOT raise) ─────

class TestChecksumClickLayer:
    """The `rom checksum` Click command must treat exit 2 (invalid header) as a
    structured, non-fatal result — never a raised ClickException. This guards
    against a future "simplification" reintroducing the dead-code bug that the
    pre-existing data/text roundtrip commands have.
    """

    def _patch(self, monkeypatch, returncode, valid, actual="0xAB", expected="0xCD"):
        def fake(rom_path, force_version=""):
            return {
                "exit_code": returncode, "stdout": "", "stderr": "",
                "rom_path": rom_path, "valid": valid,
                "actual": actual, "expected": expected,
            }
        monkeypatch.setattr("cli_anything.febuildergba.core.verbs.checksum", fake)

    def test_invalid_header_exit2_json_does_not_raise(self, monkeypatch):
        self._patch(monkeypatch, 2, False)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["--json", "rom", "checksum", "fake.gba"])
        assert res.exit_code == 0, res.output  # exit 2 must NOT raise
        assert '"valid": false' in res.output.lower()

    def test_invalid_header_human_does_not_raise(self, monkeypatch):
        self._patch(monkeypatch, 2, False)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["rom", "checksum", "fake.gba"])
        assert res.exit_code == 0, res.output
        assert "INVALID" in res.output

    def test_valid_header(self, monkeypatch):
        self._patch(monkeypatch, 0, True, actual="0x91", expected="0x91")
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["rom", "checksum", "fake.gba"])
        assert res.exit_code == 0
        assert "VALID" in res.output and "INVALID" not in res.output

    def test_file_error_exit1_raises(self, monkeypatch):
        # exit 1 = real file/usage error → must surface as a non-zero exit.
        self._patch(monkeypatch, 1, None)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["rom", "checksum", "fake.gba"])
        assert res.exit_code != 0

    def test_unexpected_nonzero_exit_raises(self, monkeypatch):
        # Any non-zero code other than the advisory 2 (e.g. a tool crash, 3)
        # must fail fast, not silently print "INVALID (actual None)".
        self._patch(monkeypatch, 3, None, actual=None, expected=None)
        res = CliRunner().invoke(febuildergba_cli.cli,
                                 ["rom", "checksum", "fake.gba"])
        assert res.exit_code != 0


# ── E2E scaffolding (real backend + real ROM, skip-gated) ─────────────

def _repo_root() -> Path:
    p = Path(__file__).resolve()
    for _ in range(6):
        p = p.parent
        if (p / "FEBuilderGBA.CLI").is_dir():
            return p
    pytest.skip("Cannot find repo root with FEBuilderGBA.CLI")


def _find_rom() -> str:
    root = _repo_root()
    roms_dir = root / "roms"
    if not roms_dir.is_dir():
        pytest.skip("roms/ directory not found")
    for name in ["FE8U.gba", "FE7U.gba", "FE7J.gba", "FE8J.gba", "FE6.gba"]:
        path = roms_dir / name
        if path.is_file():
            return str(path)
    gba = list(roms_dir.glob("*.gba"))
    if gba:
        return str(gba[0])
    pytest.skip("No .gba ROM files found in roms/")


def _require_backend():
    from cli_anything.febuildergba.utils.febuildergba_backend import check_backend
    if not check_backend().get("available"):
        pytest.skip("FEBuilderGBA.CLI backend not available (build it first)")


class TestVerbsE2E:
    def test_checksum_real_rom(self):
        _require_backend()
        rom = _find_rom()
        result = verbs.checksum(rom)
        # A real retail ROM has a valid header (exit 0); either way never errors.
        assert result["exit_code"] in (0, 2)
        assert result["valid"] is not None
        assert result["actual"] is not None

    def test_diff_identical_real_rom(self):
        _require_backend()
        rom = _find_rom()
        result = verbs.rom_diff(rom, rom)
        assert result["exit_code"] == 0
        assert result["identical"] is True
        assert result["bytes_differ"] == 0

    def test_repair_header_on_temp_copy(self):
        _require_backend()
        rom = _find_rom()
        with tempfile.TemporaryDirectory() as td:
            copy = os.path.join(td, "rom.gba")
            shutil.copyfile(rom, copy)
            # Corrupt the header checksum byte (0xBD) so repair actually writes.
            with open(copy, "r+b") as fh:
                fh.seek(0xBD)
                orig = fh.read(1)
                fh.seek(0xBD)
                fh.write(bytes([(orig[0] ^ 0xFF) & 0xFF]))
            result = verbs.repair_header(copy)
            assert result["exit_code"] == 0
            assert result["repaired"] is True
            assert result["already_valid"] is False
            # A second run is now a no-op (header already valid).
            again = verbs.repair_header(copy)
            assert again["already_valid"] is True
            assert again["repaired"] is False

    def test_export_map_settings_raw_real_rom(self):
        _require_backend()
        rom = _find_rom()
        with tempfile.TemporaryDirectory() as td:
            out = os.path.join(td, "map.tsv")
            result = verbs.export_map_settings_raw(rom, out)
            assert result["exit_code"] == 0
            assert result["file_size"] > 0
            assert os.path.isfile(out)

    def test_export_palette_real_rom(self):
        _require_backend()
        rom = _find_rom()
        with tempfile.TemporaryDirectory() as td:
            out = os.path.join(td, "pal.pal")
            result = verbs.export_palette(rom, "0x5524", out, colors=16)
            assert result["exit_code"] == 0
            assert os.path.isfile(out)

    def test_compile_event_gated(self):
        """Real compile-event, gated on EA/ColorzCore availability.

        Skips when the tool is unresolvable (exit 1), which is the common dev
        case (the EA/ColorzCore submodules are often uninitialized); runs the
        real compile when the tool is present (CI initializes it).
        """
        _require_backend()
        rom = _find_rom()
        with tempfile.TemporaryDirectory() as td:
            romcopy = os.path.join(td, "rom.gba")
            shutil.copyfile(rom, romcopy)
            script = os.path.join(td, "s.event")
            with open(script, "w", encoding="utf-8") as f:
                f.write("// minimal no-op EA script\n")
            result = verbs.compile_event(romcopy, script)
            msg = (result["stderr"] + " " + result["stdout"]).lower()
            tool_missing = ("event assembler" in msg or "colorzcore" in msg
                            or "not found" in msg)
            if result["exit_code"] != 0 and tool_missing:
                pytest.skip("EA/ColorzCore not available: "
                            + (result["stderr"] or result["stdout"])[:200])
            # Tool IS present → a genuine compile failure must fail the test,
            # not silently skip (so CI catches real regressions).
            assert result["exit_code"] == 0, (result["stderr"] or result["stdout"])[:400]
            assert result["output_path"] == romcopy  # --out omitted → overwrites input
