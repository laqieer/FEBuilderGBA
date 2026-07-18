"""Dependency-free contract tests for the real-mGBA CI proof driver."""

import hashlib
import json
from pathlib import Path

import pytest

from febuildergba_playtest.model import load_scenario
import run_real_mgba_proof as proof
import run_real_cli_proof as cli_proof
import bounded_process
from synthetic_gba import DEFAULT_MARKER, build_synthetic_rom


def test_proof_scenarios_are_strict_and_cover_press_then_release():
    rom = build_synthetic_rom()
    held = load_scenario(json.dumps(proof._scenario(rom, released=False)))
    released = load_scenario(json.dumps(proof._scenario(rom, released=True)))

    assert held.runFrames == 2
    assert held.events[0].keys == ("A",)
    assert held.assertions[0].value == DEFAULT_MARKER

    assert released.runFrames == 3
    assert [event.keys for event in released.events] == [("A",), ()]
    assert released.events[1].frame == 2
    assert released.assertions[0].value == 0
    assert released.watchdogs[0].maxStallFrames == 3


def test_parse_single_json_rejects_multiple_documents():
    with pytest.raises(AssertionError, match="2 non-empty stdout lines"):
        proof._parse_single_json(b'{"a":1}\n{"b":2}\n', "test")


def test_verify_run_checks_persisted_json_and_png(tmp_path):
    screenshot = b"\x89PNG\r\n\x1a\nproof"
    screenshot_path = tmp_path / "held.png"
    screenshot_path.write_bytes(screenshot)
    result = {
        "status": "pass",
        "exitCode": 0,
        "mgba": {
            "version": proof.REQUIRED_VERSION,
            "commit": proof.REQUIRED_COMMIT,
        },
        "startupConfig": proof.EXPECTED_STARTUP,
        "romGuard": {
            "romSha256": {"matched": True},
            "gameCode": {"matched": True},
        },
        "assertions": [{"passed": True, "actual": DEFAULT_MARKER}],
        "artifact": {
            "sha256": hashlib.sha256(screenshot).hexdigest(),
            "written": True,
        },
    }
    out_path = tmp_path / "result.json"
    out_path.write_text(json.dumps(result), encoding="utf-8")

    result_sha, screenshot_sha = proof._verify_run(
        result, out_path, screenshot_path, DEFAULT_MARKER
    )
    assert len(result_sha) == 64
    assert screenshot_sha == hashlib.sha256(screenshot).hexdigest()


def test_cli_proof_command_uses_canonical_dotnet_surface():
    command = cli_proof._build_command(
        Path("FEBuilderGBA.CLI"),
        Path("python"),
        ["--check"],
        check=True,
    )
    assert command == [
        "FEBuilderGBA.CLI",
        "--playtest",
        "--check",
        "--python=python",
    ]


def test_cli_proof_preserves_virtualenv_interpreter_path(monkeypatch, tmp_path):
    monkeypatch.chdir(tmp_path)

    def reject_resolve(_self):
        raise AssertionError("virtualenv interpreter paths must not be resolved")

    monkeypatch.setattr(Path, "resolve", reject_resolve)
    expected = tmp_path / "venv" / "bin" / "python"
    actual = cli_proof._absolute_interpreter_path("venv/bin/python")

    assert actual == expected


def test_cli_proof_failure_scenario_expects_machine_failure():
    rom = build_synthetic_rom()
    scenario = cli_proof._failing_scenario(rom)
    parsed = load_scenario(json.dumps(scenario))

    assert parsed.assertions[0].value == 1
    assert parsed.screenshot is None


def test_proof_drivers_convert_bounded_process_failures(monkeypatch):
    def overflow(*args, **kwargs):
        raise bounded_process.ProcessOutputLimitError(
            "process stdout exceeded the capture limit"
        )

    monkeypatch.setattr(proof, "run_bounded", overflow)
    with pytest.raises(AssertionError, match="direct proof.*stdout"):
        proof._run_cli(["--check"], "direct proof")

    monkeypatch.setattr(cli_proof, "run_bounded", overflow)
    with pytest.raises(AssertionError, match="CLI proof.*stdout"):
        cli_proof._run_cli(
            Path("FEBuilderGBA.CLI"),
            Path("python"),
            ["--check"],
            "CLI proof",
            check=True,
        )
