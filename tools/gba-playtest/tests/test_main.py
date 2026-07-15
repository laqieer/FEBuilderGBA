"""Dependency-free tests for the CLI I/O boundary (bounded reads, one JSON)."""

import json

import pytest

import febuildergba_playtest.__main__ as cli
from febuildergba_playtest.__main__ import _TooLarge, _read_capped


_VALID_SCENARIO = json.dumps({
    "schemaVersion": 1,
    "runFrames": 2,
    "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
})


def _one_json(capsys):
    out = capsys.readouterr().out
    lines = [line for line in out.splitlines() if line.strip()]
    assert len(lines) == 1  # exactly one JSON object on stdout
    return json.loads(lines[0])


# --- Bounded read helper ---------------------------------------------------


def test_read_capped_returns_small_file(tmp_path):
    path = tmp_path / "small.bin"
    path.write_bytes(b"abcdef")
    assert _read_capped(str(path), 16) == b"abcdef"


def test_read_capped_allows_exact_cap(tmp_path):
    path = tmp_path / "exact.bin"
    path.write_bytes(b"x" * 8)
    assert _read_capped(str(path), 8) == b"x" * 8


def test_read_capped_rejects_oversize_without_loading_all(tmp_path):
    path = tmp_path / "big.bin"
    path.write_bytes(b"y" * 1000)
    with pytest.raises(_TooLarge):
        _read_capped(str(path), 8)


# --- Scenario read bounds --------------------------------------------------


def test_oversize_scenario_rejected(tmp_path, monkeypatch, capsys):
    monkeypatch.setattr(cli, "MAX_SCENARIO_BYTES", 32)
    rom = tmp_path / "rom.gba"
    rom.write_bytes(b"\x00" * 0x200)
    scenario = tmp_path / "s.json"
    scenario.write_text("{" + " " * 64 + "}", encoding="utf-8")
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario)])
    result = _one_json(capsys)
    assert result["status"] == "scenario_error"
    assert "exceeds the maximum size" in result["note"]
    assert code == 1


def test_non_utf8_scenario_rejected(tmp_path, capsys):
    rom = tmp_path / "rom.gba"
    rom.write_bytes(b"\x00" * 0x200)
    scenario = tmp_path / "s.json"
    scenario.write_bytes(b"\xff\xfe\x00bad")
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario)])
    result = _one_json(capsys)
    assert result["status"] == "scenario_error"
    assert "not valid UTF-8" in result["note"]
    assert code == 1


# --- ROM read bounds -------------------------------------------------------


def test_oversize_rom_rejected(tmp_path, monkeypatch, capsys):
    monkeypatch.setattr(cli, "MAX_ROM_BYTES", 64)
    rom = tmp_path / "rom.gba"
    rom.write_bytes(b"\x00" * 256)
    scenario = tmp_path / "s.json"
    scenario.write_text(_VALID_SCENARIO, encoding="utf-8")
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario)])
    result = _one_json(capsys)
    assert result["status"] == "harness_error"
    assert "ROM exceeds the maximum size" in result["note"]
    assert code == 1


def test_empty_rom_rejected(tmp_path, capsys):
    rom = tmp_path / "rom.gba"
    rom.write_bytes(b"")
    scenario = tmp_path / "s.json"
    scenario.write_text(_VALID_SCENARIO, encoding="utf-8")
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario)])
    result = _one_json(capsys)
    assert result["status"] == "harness_error"
    assert "ROM file is empty" in result["note"]
    assert code == 1


# --- No-output argument parser (exactly one JSON, no help/usage leak) -------


@pytest.mark.parametrize("argv", [
    ["-h"],
    ["--help"],
    ["--bogus"],
    ["--rom", "a", "--rom", "b"],          # duplicate option
    ["positional"],
    ["--rom"],                              # missing value
    ["--rom", ""],                          # empty value
    ["--check=1"],                          # flag takes no value
    ["--rom=a", "--scenario"],              # trailing missing value
])
def test_malformed_argv_emits_single_json_exit_1(argv, capsys):
    code = cli.main(argv)
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    # No traceback / program name / path leakage in the note.
    assert "Traceback" not in result["note"]
    assert "\\" not in result["note"]


def test_help_writes_no_usage_text_to_stdout(capsys):
    cli.main(["--help"])
    out = capsys.readouterr().out
    lines = [line for line in out.splitlines() if line.strip()]
    assert len(lines) == 1
    json.loads(lines[0])  # the only stdout line is the JSON result


def test_check_rejects_out(capsys):
    code = cli.main(["--check", "--out", "x.json"])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "--check cannot be combined" in result["note"]


def test_check_rejects_artifact_dir(capsys):
    code = cli.main(["--check", "--artifact-dir", "shots"])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "--check cannot be combined" in result["note"]


def test_missing_required_args_rejected(capsys):
    code = cli.main(["--rom", "only.gba"])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "--rom and --scenario are required" in result["note"]


# --- Destructive path-collision rejection (inputs must never be mutated) ----


def _prepare_valid_inputs(tmp_path):
    rom = tmp_path / "rom.gba"
    rom.write_bytes(b"\x00" * 0x200)
    scenario = tmp_path / "s.json"
    scenario.write_text(_VALID_SCENARIO, encoding="utf-8")
    return rom, scenario


def test_out_colliding_with_rom_rejected(tmp_path, capsys):
    rom, scenario = _prepare_valid_inputs(tmp_path)
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario), "--out", str(rom)])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "collides" in result["note"]
    # The ROM was not overwritten.
    assert rom.read_bytes() == b"\x00" * 0x200


def test_out_colliding_with_scenario_rejected(tmp_path, capsys):
    rom, scenario = _prepare_valid_inputs(tmp_path)
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario), "--out", str(scenario)])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "collides" in result["note"]
    assert scenario.read_text(encoding="utf-8") == _VALID_SCENARIO


def test_out_lexical_alias_of_rom_rejected(tmp_path, capsys):
    rom, scenario = _prepare_valid_inputs(tmp_path)
    # A non-existing lexical alias of the ROM (dir/../rom.gba).
    alias = str(tmp_path / "sub" / ".." / "rom.gba")
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario), "--out", alias])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "collides" in result["note"]
    assert rom.read_bytes() == b"\x00" * 0x200


def test_out_atomic_write_refuses_directory_target(tmp_path, monkeypatch, capsys):
    rom, scenario = _prepare_valid_inputs(tmp_path)
    out_dir = tmp_path / "outdir"
    out_dir.mkdir()

    class _Backend:
        def load_rom(self, rom_bytes):
            pass

    # Make the scenario run reach the --out write by faking a trivially passing
    # backend, then confirm a directory --out target is refused (harness_error).
    def _fake_run_scenario(rom_path, scenario_path, out_path, artifact_dir):
        from febuildergba_playtest.runner import atomic_write_bytes, BackendError
        try:
            atomic_write_bytes(out_path, b"{}")
        except BackendError as exc:
            return cli._error_result("harness_error", str(exc))
        return {"status": "pass"}, 0

    monkeypatch.setattr(cli, "_run_scenario", _fake_run_scenario)
    code = cli.main(["--rom", str(rom), "--scenario", str(scenario), "--out", str(out_dir)])
    result = _one_json(capsys)
    assert code == 1
    assert result["status"] == "harness_error"
    assert "not a regular file" in result["note"]
