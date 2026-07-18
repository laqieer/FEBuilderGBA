"""Tests for the additional CLI-verb wrappers (issue #1933).

Two layers:
- Unit tests monkeypatch ``core.verbs.run_cli`` to assert each wrapper builds
  the correct backend arg-list and shapes its JSON result — no CLI invoked.
- E2E tests call the wrappers with the real backend + a real ROM, skip-gated on
  backend/ROM availability (roms/FE8U.gba). They never mutate a source ROM
  (repair-header runs on a temp copy).
"""

import os
import json
import shutil
import tempfile
from pathlib import Path
from types import SimpleNamespace

import pytest
from click.testing import CliRunner

from cli_anything.febuildergba import febuildergba_cli
from cli_anything.febuildergba.core import verbs


def _write_valid_test_rom(path, game_code=b"BE8E"):
    """Write a minimal, header-valid 1 MiB synthetic GBA ROM."""
    rom = bytearray(0x100000)
    rom[0xAC:0xB0] = game_code
    rom[0xB2] = 0x96
    rom[0xBD] = (-sum(rom[0xA0:0xBD]) - 0x19) & 0xFF
    Path(path).write_bytes(rom)


# ── Unit-test scaffolding: fake run_cli ───────────────────────────────

class _Recorder:
    """Captures the args passed to run_cli and returns a canned result."""

    def __init__(self, returncode=0, stdout="", stderr=""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.args = None
        self.kwargs = None
        # The value of the first ``--rom=`` argument observed, and whether it
        # pointed at an existing regular file at the instant of the call —
        # used to assert wrappers hand the backend a live, private snapshot
        # rather than the caller's original path (issue #1942 / PR #1971).
        self.rom_arg = None
        self.rom_arg_existed_during_call = None

    def __call__(self, args, **kwargs):
        self.args = args
        self.kwargs = kwargs
        for arg in args:
            if isinstance(arg, str) and arg.startswith("--rom="):
                self.rom_arg = arg[len("--rom="):]
                self.rom_arg_existed_during_call = os.path.isfile(self.rom_arg)
                break
        return SimpleNamespace(
            returncode=self.returncode, stdout=self.stdout, stderr=self.stderr
        )


@pytest.fixture
def rec(monkeypatch):
    r = _Recorder()
    monkeypatch.setattr("cli_anything.febuildergba.core.verbs.run_cli", r)
    return r


@pytest.fixture
def playtest_rec(monkeypatch):
    r = _Recorder()
    monkeypatch.setattr(
        "cli_anything.febuildergba.core.playtest.run_cli",
        r,
    )
    return r


class TestPlaytestUnit:
    @staticmethod
    def _json(status, exit_code, **extra):
        result = {
            "resultSchemaVersion": 1,
            "status": status,
            "exitCode": exit_code,
        }
        result.update(extra)
        return json.dumps(result, sort_keys=True, separators=(",", ":"))

    def test_check_delegates_without_rom(self, playtest_rec):
        from cli_anything.febuildergba.core.playtest import playtest

        playtest_rec.stdout = self._json("check_ok", 0)
        result = playtest(check=True, python_executable="python")

        assert playtest_rec.args == [
            "--playtest",
            "--check",
            "--python=python",
        ]
        assert playtest_rec.kwargs == {"timeout": 630}
        assert result["status"] == "check_ok"

    def test_run_delegates_all_structural_options(self, playtest_rec):
        from cli_anything.febuildergba.core.playtest import playtest

        playtest_rec.stdout = self._json("pass", 0, framesExecuted=12)
        result = playtest(
            rom_path="r.gba",
            scenario_path="s.json",
            out_path="result.json",
            artifact_dir="artifacts",
            python_executable="python3",
            timeout_ms=12_000,
        )

        assert playtest_rec.args == [
            "--playtest",
            "--rom=r.gba",
            "--scenario=s.json",
            "--timeout=12000",
            "--out=result.json",
            "--artifact-dir=artifacts",
            "--python=python3",
        ]
        assert playtest_rec.kwargs == {"timeout": 42}
        assert result["framesExecuted"] == 12

    @pytest.mark.parametrize(
        ("status", "exit_code"),
        [
            ("dependency_error", 1),
            ("harness_error", 1),
            ("assertion_failed", 2),
            ("crash", 2),
            ("softlock", 2),
        ],
    )
    def test_preserves_nonzero_result_distinctions(
        self, playtest_rec, status, exit_code
    ):
        from cli_anything.febuildergba.core.playtest import playtest

        playtest_rec.returncode = exit_code
        playtest_rec.stdout = self._json(status, exit_code)

        result = playtest("r.gba", "s.json")

        assert result["status"] == status
        assert result["exitCode"] == exit_code

    @pytest.mark.parametrize(
        "stdout",
        [
            "",
            "not json",
            "[]",
            '{"resultSchemaVersion":1,"status":"pass"}',
            '{"exitCode":0,"resultSchemaVersion":1,"status":"unknown"}',
            '{"exitCode":0,"exitCode":0,"resultSchemaVersion":1,"status":"pass"}',
            '{"exitCode":0,"resultSchemaVersion":1,"status":"pass"}\n{}',
        ],
    )
    def test_malformed_backend_json_is_an_error(self, playtest_rec, stdout):
        from cli_anything.febuildergba.core.playtest import (
            PlaytestResultError,
            playtest,
        )

        playtest_rec.stdout = stdout
        with pytest.raises(PlaytestResultError):
            playtest("r.gba", "s.json")

    def test_process_exit_must_match_document(self, playtest_rec):
        from cli_anything.febuildergba.core.playtest import (
            PlaytestResultError,
            playtest,
        )

        playtest_rec.returncode = 1
        playtest_rec.stdout = self._json("pass", 0)
        with pytest.raises(PlaytestResultError):
            playtest("r.gba", "s.json")

    def test_check_rejects_run_arguments(self, playtest_rec):
        from cli_anything.febuildergba.core.playtest import playtest

        with pytest.raises(ValueError, match="cannot be combined"):
            playtest(rom_path="r.gba", check=True)
        with pytest.raises(ValueError, match="timeout"):
            playtest(check=True, timeout_ms=1_000)
        assert playtest_rec.args is None


class TestPlaytestClick:
    def test_json_check_output(self, monkeypatch):
        captured = {}

        def fake_playtest(**kwargs):
            captured.update(kwargs)
            return {
                "resultSchemaVersion": 1,
                "status": "check_ok",
                "exitCode": 0,
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.playtest.playtest",
            fake_playtest,
        )
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            ["--json", "playtest", "--check", "--python=python"],
        )

        assert result.exit_code == 0, result.output
        assert json.loads(result.output)["status"] == "check_ok"
        assert captured["check"] is True
        assert captured["rom_path"] == ""

    def test_behavior_failure_preserves_exit_two(self, monkeypatch, tmp_path):
        scenario = tmp_path / "s.json"
        scenario.write_text("{}", encoding="utf-8")
        rom = tmp_path / "r.gba"
        _write_valid_test_rom(rom)

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.playtest.playtest",
            lambda **kwargs: {
                "resultSchemaVersion": 1,
                "status": "assertion_failed",
                "exitCode": 2,
                "note": "assertion mismatch",
            },
        )
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            [
                "--json",
                "playtest",
                "--rom",
                str(rom),
                "--scenario",
                str(scenario),
            ],
        )

        assert result.exit_code == 2
        assert json.loads(result.output)["status"] == "assertion_failed"

    def test_explicit_rom_does_not_inherit_global_rom(
        self, monkeypatch, tmp_path
    ):
        scenario = tmp_path / "s.json"
        scenario.write_text("{}", encoding="utf-8")
        explicit_rom = tmp_path / "explicit.gba"
        _write_valid_test_rom(explicit_rom)
        captured = {}

        def fake_playtest(**kwargs):
            captured.update(kwargs)
            return {
                "resultSchemaVersion": 1,
                "status": "pass",
                "exitCode": 0,
            }

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.playtest.playtest",
            fake_playtest,
        )
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            [
                "--rom",
                "unrelated-session.gba",
                "playtest",
                "--rom",
                str(explicit_rom),
                "--scenario",
                str(scenario),
            ],
        )

        assert result.exit_code == 0, result.output
        assert captured["rom_path"] == str(explicit_rom)

    def test_missing_explicit_rom_uses_session_rom(self, monkeypatch, tmp_path):
        scenario = tmp_path / "s.json"
        scenario.write_text("{}", encoding="utf-8")
        session_rom = tmp_path / "session.gba"
        _write_valid_test_rom(session_rom)
        captured = {}
        fake_session = SimpleNamespace(
            is_open=lambda: True,
            state=SimpleNamespace(rom_path=str(session_rom)),
        )

        monkeypatch.setattr(
            febuildergba_cli,
            "Session",
            lambda _path=None: fake_session,
        )
        monkeypatch.setattr(
            "cli_anything.febuildergba.core.playtest.playtest",
            lambda **kwargs: (
                captured.update(kwargs)
                or {
                    "resultSchemaVersion": 1,
                    "status": "pass",
                    "exitCode": 0,
                }
            ),
        )
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            ["playtest", "--scenario", str(scenario)],
        )

        assert result.exit_code == 0, result.output
        assert captured["rom_path"] == str(session_rom)

    def test_playtest_is_not_exposed_as_mcp_tool(self):
        from cli_anything.febuildergba import mcp_server

        assert "playtest" not in {
            tool["name"] for tool in mcp_server.TOOL_DEFS
        }

    def test_check_rejects_explicit_timeout(self):
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            ["playtest", "--check", "--timeout=1000"],
        )
        assert result.exit_code == 1
        assert "--check cannot be combined" in result.output

    @pytest.mark.parametrize(
        "arguments",
        [
            ["playtest", "--timeout=999"],
            ["playtest", "--timeout=not-a-number"],
            ["playtest", "--timeout=²"],
            ["playtest", "--rom", "missing.gba", "--scenario", "missing.json"],
            ["playtest", "--unknown"],
            ["playtest", "--scenario"],
        ],
    )
    def test_usage_and_parameter_errors_exit_one(self, arguments):
        result = CliRunner().invoke(febuildergba_cli.cli, arguments)
        assert result.exit_code == 1, result.output


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
    def test_export_with_colors(self, rec, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        verbs.export_palette(str(rom_path), "0x5524", "p.pal", colors=16)
        # A bare direct call (like Click) never enters MCP's
        # prebuilt_backend_only() scope, so the backend receives the
        # caller's real path unchanged (issue #1942 / PR #1971 legacy
        # passthrough) — MCP-scoped snapshotting is covered by
        # test_core.py's TestRomSnapshotAcrossAllWrappers.
        assert rec.rom_arg is not None
        assert rec.rom_arg == str(rom_path)
        assert rec.rom_arg_existed_during_call is True
        assert rec.args == ["--export-palette", f"--rom={rec.rom_arg}",
                            "--addr=0x5524", "--out=p.pal", "--colors=16"]
        # No snapshot was created, so there is nothing to remove.
        assert os.path.isfile(rec.rom_arg)

    def test_export_without_colors_omits_flag(self, rec, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        verbs.export_palette(str(rom_path), "0x5524", "p.pal")
        assert "--colors=0" not in rec.args
        assert not any(a.startswith("--colors") for a in rec.args)

    def test_import_args(self, rec, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        verbs.import_palette(str(rom_path), "0x5524", "p.pal")
        # Same legacy-passthrough contract as export, for the mutating
        # wrapper's (no-op outside MCP) commit path.
        assert rec.rom_arg is not None
        assert rec.rom_arg == str(rom_path)
        assert rec.args == ["--import-palette", f"--rom={rec.rom_arg}",
                            "--addr=0x5524", "--in=p.pal"]
        # No snapshot was created, so there is nothing to remove.
        assert os.path.isfile(rec.rom_arg)


class TestPaletteClickLayer:
    """Click's `rom palette export|import` commands keep working end-to-end
    with a real ROM (issue #1942 / PR #1971): outside MCP's
    ``prebuilt_backend_only`` scope these wrappers retain their historical,
    direct-path behavior — no private snapshot, no local validation — so
    the backend receives the caller's real path unchanged, exactly as
    before this fix. MCP-scoped snapshot/commit coverage lives in
    ``test_core.py``'s ``TestRomSnapshotAcrossAllWrappers`` /
    ``TestMutatingWrappersCommitProtocol``."""

    def test_export_success_with_real_rom(self, monkeypatch, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        out = tmp_path / "p.pal"

        def fake_run_cli(args, **kwargs):
            rom_arg = next(a for a in args if a.startswith("--rom="))[len("--rom="):]
            assert rom_arg == str(rom_path), "Click keeps the direct path"
            assert os.path.isfile(rom_arg)
            Path(out).write_bytes(b"\x00" * 32)
            return SimpleNamespace(
                returncode=0, stdout="Exported: p.pal (32 bytes)", stderr="")

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.run_cli", fake_run_cli)
        res = CliRunner().invoke(
            febuildergba_cli.cli,
            ["--rom", str(rom_path), "palette", "export",
             "--addr=0x5524", "-o", str(out)],
        )
        assert res.exit_code == 0, res.output
        assert out.is_file()

    def test_import_success_with_real_rom(self, monkeypatch, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        infile = tmp_path / "p.pal"
        infile.write_bytes(b"\x00" * 32)

        def fake_run_cli(args, **kwargs):
            rom_arg = next(a for a in args if a.startswith("--rom="))[len("--rom="):]
            assert rom_arg == str(rom_path), "Click keeps the direct path"
            return SimpleNamespace(returncode=0, stdout="Imported.", stderr="")

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.run_cli", fake_run_cli)
        res = CliRunner().invoke(
            febuildergba_cli.cli,
            ["--rom", str(rom_path), "palette", "import",
             "--addr=0x5524", "-i", str(infile)],
        )
        assert res.exit_code == 0, res.output
        # The (no-op) commit ran against the real original path directly.
        assert rom_path.is_file()

    def test_import_backend_failure_raises_and_leaves_rom_untouched(
            self, monkeypatch, tmp_path):
        rom_path = tmp_path / "r.gba"
        _write_valid_test_rom(rom_path)
        original_bytes = rom_path.read_bytes()
        infile = tmp_path / "p.pal"
        infile.write_bytes(b"\x00" * 32)

        def fake_run_cli(args, **kwargs):
            return SimpleNamespace(returncode=1, stdout="", stderr="bad palette")

        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.run_cli", fake_run_cli)
        res = CliRunner().invoke(
            febuildergba_cli.cli,
            ["--rom", str(rom_path), "palette", "import",
             "--addr=0x5524", "-i", str(infile)],
        )
        assert res.exit_code != 0
        assert rom_path.read_bytes() == original_bytes


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
