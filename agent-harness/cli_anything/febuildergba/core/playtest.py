"""Agent-harness wrapper for the canonical .NET ``--playtest`` verb.

This module contains no emulator or ROM logic. It delegates to
``FEBuilderGBA.CLI --playtest``, validates the single JSON result document,
and preserves exit 0/1/2 so callers can distinguish a pass, a harness error,
and a behavioral verification failure.
"""

import json

from cli_anything.febuildergba.core.project import backend_rom_snapshot
from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


PLAYTEST_DEFAULT_TIMEOUT_MS = 600_000
PLAYTEST_MINIMUM_TIMEOUT_MS = 1_000
PLAYTEST_MAXIMUM_TIMEOUT_MS = 3_600_000
PLAYTEST_MAXIMUM_RESULT_CHARS = 1_048_576

_STATUS_EXIT_CODES = {
    "pass": 0,
    "check_ok": 0,
    "scenario_error": 1,
    "dependency_error": 1,
    "harness_error": 1,
    "check_failed": 1,
    "rom_mismatch": 2,
    "assertion_failed": 2,
    "crash": 2,
    "softlock": 2,
}


class PlaytestResultError(RuntimeError):
    """Raised when the .NET backend violates the JSON result contract."""


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise PlaytestResultError("playtest backend returned duplicate JSON keys")
        result[key] = value
    return result


def _reject_json_constant(_value):
    raise PlaytestResultError("playtest backend returned non-standard JSON")


def _parse_result(stdout: str, process_exit_code: int) -> dict:
    if (
        not isinstance(stdout, str)
        or not stdout.strip()
        or len(stdout) > PLAYTEST_MAXIMUM_RESULT_CHARS
    ):
        raise PlaytestResultError(
            "playtest backend returned an empty or oversized result"
        )
    try:
        result = json.loads(
            stdout,
            object_pairs_hook=_unique_object,
            parse_constant=_reject_json_constant,
        )
    except PlaytestResultError:
        raise
    except (TypeError, ValueError, json.JSONDecodeError) as exc:
        raise PlaytestResultError(
            "playtest backend returned malformed JSON"
        ) from exc

    if not isinstance(result, dict):
        raise PlaytestResultError(
            "playtest backend result must be a JSON object"
        )
    schema_version = result.get("resultSchemaVersion")
    status = result.get("status")
    document_exit_code = result.get("exitCode")
    if (
        type(schema_version) is not int
        or schema_version != 1
        or not isinstance(status, str)
        or type(document_exit_code) is not int
    ):
        raise PlaytestResultError(
            "playtest backend result is missing required fields"
        )
    expected_exit_code = _STATUS_EXIT_CODES.get(status)
    if (
        expected_exit_code is None
        or document_exit_code != expected_exit_code
        or process_exit_code != document_exit_code
    ):
        raise PlaytestResultError(
            "playtest backend status and exit code do not match"
        )
    return result


def playtest(
    rom_path: str = "",
    scenario_path: str = "",
    out_path: str = "",
    artifact_dir: str = "",
    python_executable: str = "",
    timeout_ms=None,
    check: bool = False,
) -> dict:
    """Run or dependency-check the canonical .NET playtest command."""
    if check and timeout_ms is not None:
        raise ValueError("check cannot be combined with timeout_ms")
    if timeout_ms is None:
        timeout_ms = PLAYTEST_DEFAULT_TIMEOUT_MS
    if (
        type(timeout_ms) is not int
        or timeout_ms < PLAYTEST_MINIMUM_TIMEOUT_MS
        or timeout_ms > PLAYTEST_MAXIMUM_TIMEOUT_MS
    ):
        raise ValueError(
            "timeout_ms must be an integer from 1000 through 3600000"
        )
    if check:
        if rom_path or scenario_path or out_path or artifact_dir:
            raise ValueError(
                "check cannot be combined with ROM, scenario, output, or artifacts"
            )
        args = ["--playtest", "--check"]
        if python_executable:
            args.append(f"--python={python_executable}")
        result = run_cli(
            args,
            timeout=max(30, timeout_ms // 1000 + 30),
        )
        return _parse_result(result.stdout, result.returncode)

    if not rom_path:
        raise ValueError("rom_path is required")
    if not scenario_path:
        raise ValueError("scenario_path is required")

    with backend_rom_snapshot(rom_path) as backend_rom:
        args = [
            "--playtest",
            f"--rom={backend_rom}",
            f"--scenario={scenario_path}",
            f"--timeout={timeout_ms}",
        ]
        if out_path:
            args.append(f"--out={out_path}")
        if artifact_dir:
            args.append(f"--artifact-dir={artifact_dir}")
        if python_executable:
            args.append(f"--python={python_executable}")
        result = run_cli(
            args,
            timeout=max(30, timeout_ms // 1000 + 30),
        )
    return _parse_result(result.stdout, result.returncode)
