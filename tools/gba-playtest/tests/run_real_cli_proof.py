"""Fail-hard proof that drives mGBA through ``FEBuilderGBA.CLI --playtest``.

The protected direct-engine oracle remains separate. This driver proves the
published .NET CLI copies and launches the repository-owned runner, preserves
exit 0/1/2, produces deterministic JSON/PNG evidence, and never creates save
side effects beside the synthetic ROM.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile

from bounded_process import BoundedProcessError, run_bounded
import run_real_mgba_proof as direct_proof
from synthetic_gba import build_synthetic_rom, header_game_code, sha256_hex


MAX_PROCESS_OUTPUT = 1 << 20
CLI_TIMEOUT_MS = 120_000
PROCESS_TIMEOUT_SECONDS = 180
SAVE_SUFFIXES = direct_proof.SAVE_SUFFIXES


def _canonical_json(value) -> str:
    return json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    )


def _absolute_interpreter_path(value: str) -> Path:
    # POSIX virtualenv interpreters are commonly symlinks. Resolving one would
    # launch the base interpreter and lose the virtualenv's installed binding.
    return Path(os.path.abspath(value))


def _build_command(
    cli_path: Path,
    python_path: Path,
    arguments,
    *,
    check: bool = False,
):
    command = [
        str(cli_path),
        "--playtest",
        *arguments,
        f"--python={python_path}",
    ]
    if not check:
        command.append(f"--timeout={CLI_TIMEOUT_MS}")
    return command


def _parse_single_json(blob: bytes, label: str):
    if len(blob) > MAX_PROCESS_OUTPUT:
        raise AssertionError(f"{label} stdout exceeded {MAX_PROCESS_OUTPUT} bytes")
    text = blob.decode("utf-8")
    lines = [line for line in text.splitlines() if line]
    if len(lines) != 1:
        raise AssertionError(f"{label} emitted {len(lines)} non-empty stdout lines")
    return json.loads(lines[0])


def _run_cli(
    cli_path: Path,
    python_path: Path,
    arguments,
    label: str,
    *,
    check: bool = False,
):
    env = os.environ.copy()
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["PYTHONNOUSERSITE"] = "1"
    try:
        process = run_bounded(
            _build_command(
                cli_path,
                python_path,
                arguments,
                check=check,
            ),
            cwd=cli_path.parent,
            env=env,
            timeout_seconds=PROCESS_TIMEOUT_SECONDS,
            stdout_limit=MAX_PROCESS_OUTPUT,
            stderr_limit=MAX_PROCESS_OUTPUT,
        )
    except BoundedProcessError as exc:
        raise AssertionError(f"{label} {exc}") from None
    result = _parse_single_json(process.stdout, label)
    if process.returncode != result.get("exitCode"):
        raise AssertionError(
            f"{label} process/result exit mismatch: "
            f"{process.returncode} != {result.get('exitCode')!r}"
        )
    return process, result


def _passing_scenario(rom: bytes):
    return direct_proof._scenario(rom, released=True)


def _failing_scenario(rom: bytes):
    scenario = direct_proof._scenario(rom, released=True)
    scenario["name"] = "synthetic-expected-assertion-failure"
    scenario["assertions"][0]["value"] = 1
    scenario.pop("screenshot", None)
    return scenario


def _write_scenario(path: Path, scenario) -> None:
    path.write_text(
        _canonical_json(scenario) + "\n",
        encoding="utf-8",
    )


def _verify_replays(
    cli_path: Path,
    python_path: Path,
    output_dir: Path,
    work_dir: Path,
    rom_path: Path,
    rom: bytes,
):
    scenario_path = work_dir / "released.json"
    _write_scenario(scenario_path, _passing_scenario(rom))
    result_hashes = []
    screenshot_hashes = []

    for replay in range(1, 3):
        replay_dir = output_dir / "pass" / f"run-{replay}"
        replay_dir.mkdir(parents=True)
        out_path = replay_dir / "result.json"
        process, result = _run_cli(
            cli_path,
            python_path,
            [
                f"--rom={rom_path}",
                f"--scenario={scenario_path}",
                f"--out={out_path}",
                f"--artifact-dir={replay_dir}",
            ],
            f".NET CLI replay {replay}",
        )
        if process.returncode != 0:
            detail = process.stderr.decode("utf-8", errors="replace")[:500]
            raise AssertionError(f".NET CLI replay {replay} failed: {detail}")
        result_sha, screenshot_sha = direct_proof._verify_run(
            result,
            out_path,
            replay_dir / "released.png",
            0,
        )
        if out_path.read_bytes() != process.stdout:
            raise AssertionError(
                "published CLI stdout and persisted result bytes differ"
            )
        result_hashes.append(result_sha)
        screenshot_hashes.append(screenshot_sha)

    if len(set(result_hashes)) != 1:
        raise AssertionError("published CLI result JSON is not deterministic")
    if len(set(screenshot_hashes)) != 1:
        raise AssertionError("published CLI screenshot is not deterministic")
    return {
        "replays": 2,
        "resultSha256": result_hashes[0],
        "screenshotSha256": screenshot_hashes[0],
    }


def _verify_expected_failure(
    cli_path: Path,
    python_path: Path,
    output_dir: Path,
    work_dir: Path,
    rom_path: Path,
    rom: bytes,
):
    scenario_path = work_dir / "expected-failure.json"
    _write_scenario(scenario_path, _failing_scenario(rom))
    failure_dir = output_dir / "expected-failure"
    failure_dir.mkdir()
    out_path = failure_dir / "result.json"
    process, result = _run_cli(
        cli_path,
        python_path,
        [
            f"--rom={rom_path}",
            f"--scenario={scenario_path}",
            f"--out={out_path}",
        ],
        ".NET CLI expected assertion failure",
    )
    if process.returncode != 2 or result.get("status") != "assertion_failed":
        raise AssertionError(f"expected exit 2 assertion failure: {result!r}")
    assertions = result.get("assertions", [])
    if (
        len(assertions) != 1
        or assertions[0].get("passed") is not False
        or assertions[0].get("actual") != 0
    ):
        raise AssertionError(
            f"expected machine-readable assertion evidence: {assertions!r}"
        )
    if out_path.read_bytes() != process.stdout:
        raise AssertionError(
            "expected-failure stdout and persisted result bytes differ"
        )
    return {
        "exitCode": 2,
        "status": "assertion_failed",
        "resultSha256": hashlib.sha256(process.stdout).hexdigest(),
    }


def main(argv=None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if (
        len(args) != 6
        or args[0] != "--cli"
        or args[2] != "--python"
        or args[4] != "--output-dir"
    ):
        raise SystemExit(
            "usage: run_real_cli_proof.py "
            "--cli PATH --python PATH --output-dir PATH"
        )

    cli_path = Path(args[1]).resolve()
    python_path = _absolute_interpreter_path(args[3])
    output_dir = Path(args[5]).resolve()
    if not cli_path.is_file():
        raise AssertionError("published CLI executable was not found")
    if not python_path.is_file():
        raise AssertionError("pinned mGBA Python executable was not found")
    output_dir.mkdir(parents=True, exist_ok=False)

    with tempfile.TemporaryDirectory(
        prefix="febuildergba-cli-playtest-proof-"
    ) as temp:
        work_dir = Path(temp)
        rom = build_synthetic_rom()
        rom_path = work_dir / "synthetic-playtest.gba"
        rom_path.write_bytes(rom)

        check_process, check = _run_cli(
            cli_path,
            python_path,
            ["--check"],
            ".NET CLI binding check",
            check=True,
        )
        if check_process.returncode != 0 or check.get("status") != "check_ok":
            raise AssertionError(f".NET CLI binding check failed: {check!r}")
        if check.get("mgba") != {
            "version": direct_proof.REQUIRED_VERSION,
            "commit": direct_proof.REQUIRED_COMMIT,
        }:
            raise AssertionError(
                f".NET CLI binding provenance mismatch: {check!r}"
            )

        passing = _verify_replays(
            cli_path,
            python_path,
            output_dir,
            work_dir,
            rom_path,
            rom,
        )
        expected_failure = _verify_expected_failure(
            cli_path,
            python_path,
            output_dir,
            work_dir,
            rom_path,
            rom,
        )

        side_effects = sorted(
            str(path)
            for root in (work_dir, output_dir)
            for path in root.rglob("*")
            if path.is_file() and path.suffix.lower() in SAVE_SUFFIXES
        )
        if side_effects:
            raise AssertionError(
                f"unexpected save-file side effects: {side_effects!r}"
            )

    summary = {
        "proofSchemaVersion": 1,
        "surface": "FEBuilderGBA.CLI --playtest",
        "mgba": check["mgba"],
        "romSha256": sha256_hex(rom),
        "gameCode": header_game_code(rom),
        "passing": passing,
        "expectedFailure": expected_failure,
        "saveSideEffects": [],
    }
    (output_dir / "proof-summary.json").write_text(
        _canonical_json(summary) + "\n",
        encoding="utf-8",
    )
    sys.stdout.write(_canonical_json(summary) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
