"""Command-line entrypoint for the FEBuilderGBA headless playtest engine.

Emits exactly one JSON result document on stdout; all diagnostics go to stderr.
Exit codes:

* ``0`` - pass / check succeeded
* ``1`` - setup, usage, dependency, or harness failure
* ``2`` - behavioral verification failure (assertion, crash, softlock, ROM guard)

The runtime NEVER downloads or installs anything. When ``mgba`` is missing it
returns a ``dependency_error`` result and exit ``1``; it does not attempt to
install the binding.
"""

from __future__ import annotations

import argparse
import sys
from typing import Any, Dict, List, Optional, Tuple

from .model import ScenarioError, load_scenario
from .runner import RESULT_SCHEMA_VERSION, STATUS_EXIT_CODES, BackendError, Runner, canonical_json


def _emit(result: Dict[str, Any], exit_code: int) -> int:
    sys.stdout.write(canonical_json(result))
    sys.stdout.write("\n")
    sys.stdout.flush()
    return exit_code


def _error_result(status: str, note: str, extra: Optional[Dict[str, Any]] = None) -> Tuple[Dict[str, Any], int]:
    result: Dict[str, Any] = {
        "resultSchemaVersion": RESULT_SCHEMA_VERSION,
        "status": status,
        "exitCode": STATUS_EXIT_CODES[status],
        "note": " ".join(str(note).split())[:200],
    }
    if extra:
        result.update(extra)
    return result, STATUS_EXIT_CODES[status]


def _run_check() -> Tuple[Dict[str, Any], int]:
    try:
        from .mgba_backend import REQUIRED_COMMIT, REQUIRED_VERSION, check_available
    except Exception as exc:  # pragma: no cover - defensive
        return _error_result("dependency_error", f"backend import failed: {exc}")
    info = check_available()
    status = "check_ok" if info.get("available") else "check_failed"
    result: Dict[str, Any] = {
        "resultSchemaVersion": RESULT_SCHEMA_VERSION,
        "status": status,
        "exitCode": STATUS_EXIT_CODES[status],
        "requiredVersion": REQUIRED_VERSION,
        "requiredCommit": REQUIRED_COMMIT,
        "mgba": {"version": info.get("version"), "commit": info.get("commit")},
    }
    if info.get("reason"):
        result["note"] = " ".join(str(info["reason"]).split())[:200]
    return result, STATUS_EXIT_CODES[status]


def _read_bytes(path: str) -> bytes:
    with open(path, "rb") as handle:
        return handle.read()


def _run_scenario(rom_path: str, scenario_path: str, out_path: Optional[str],
                  artifact_dir: Optional[str]) -> Tuple[Dict[str, Any], int]:
    try:
        scenario_text = open(scenario_path, "r", encoding="utf-8").read()
    except OSError as exc:
        return _error_result("harness_error", f"cannot read scenario: {exc.strerror}")
    try:
        scenario = load_scenario(scenario_text)
    except ScenarioError as exc:
        return _error_result("scenario_error", str(exc))

    try:
        rom_bytes = _read_bytes(rom_path)
    except OSError as exc:
        return _error_result("harness_error", f"cannot read ROM: {exc.strerror}")

    try:
        from .mgba_backend import MgbaBackend
    except Exception as exc:
        return _error_result("dependency_error", f"mGBA backend unavailable: {exc}")

    try:
        backend = MgbaBackend(want_screenshot=scenario.screenshot is not None)
    except BackendError as exc:
        return _error_result("dependency_error", str(exc))

    runner = Runner(scenario, backend, artifact_dir=artifact_dir)
    try:
        result, exit_code = runner.run(rom_bytes)
    except BackendError as exc:
        result, exit_code = _error_result("harness_error", str(exc))

    if out_path is not None:
        try:
            with open(out_path, "w", encoding="utf-8") as handle:
                handle.write(canonical_json(result))
                handle.write("\n")
        except OSError as exc:
            return _error_result("harness_error", f"cannot write --out: {exc.strerror}")
    return result, exit_code


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="febuildergba_playtest",
        description="Deterministic headless mGBA playtest runner.",
    )
    parser.add_argument("--check", action="store_true", help="Verify the pinned mGBA binding is available.")
    parser.add_argument("--rom", help="Path to the GBA ROM to load.")
    parser.add_argument("--scenario", help="Path to the JSON scenario document.")
    parser.add_argument("--out", help="Optional path to also write the JSON result.")
    parser.add_argument("--artifact-dir", dest="artifact_dir", help="Directory for optional screenshot output.")
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_parser()
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        result, code = _error_result("harness_error", "invalid command-line arguments")
        return _emit(result, code)

    if args.check:
        if args.rom or args.scenario:
            result, code = _error_result("harness_error", "--check cannot be combined with --rom/--scenario")
            return _emit(result, code)
        result, code = _run_check()
        return _emit(result, code)

    if not args.rom or not args.scenario:
        result, code = _error_result("harness_error", "--rom and --scenario are required")
        return _emit(result, code)

    result, code = _run_scenario(args.rom, args.scenario, args.out, args.artifact_dir)
    return _emit(result, code)


if __name__ == "__main__":
    sys.exit(main())
