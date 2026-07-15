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
