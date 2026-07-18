"""Fail-hard real-mGBA proof used by the dedicated CI workflow.

The proof runs the public playtest CLI in fresh subprocesses against the
repository-authored, logo-free synthetic cartridge ROM. It verifies the exact
pinned binding, repeated deterministic replay, A-button press and release
transitions, stable screenshots, and absence of save-file side effects.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile

from bounded_process import BoundedProcessError, run_bounded
from synthetic_gba import (
    DEFAULT_MARKER,
    build_synthetic_rom,
    header_game_code,
    sha256_hex,
)


TOOL_DIR = Path(__file__).resolve().parents[1]
REQUIRED_VERSION = "0.10.5"
REQUIRED_COMMIT = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"
MAX_PROCESS_OUTPUT = 1 << 20
REPLAY_COUNT = 3
SAVE_SUFFIXES = frozenset(
    {".sav", ".sa1", ".eep", ".fla", ".rtc", ".ss0", ".ss1", ".ss2"}
)

EXPECTED_STARTUP = {
    "audioSync": False,
    "autoloadCheats": False,
    "autoloadPatch": False,
    "autoloadSave": False,
    "biosHle": True,
    "frameskip": 0,
    "mute": True,
    "skipBios": False,
    "useBios": False,
    "videoSync": False,
}


def _canonical_json(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _parse_single_json(blob: bytes, label: str):
    if len(blob) > MAX_PROCESS_OUTPUT:
        raise AssertionError(f"{label} stdout exceeded {MAX_PROCESS_OUTPUT} bytes")
    text = blob.decode("utf-8")
    lines = [line for line in text.splitlines() if line]
    if len(lines) != 1:
        raise AssertionError(f"{label} emitted {len(lines)} non-empty stdout lines")
    return json.loads(lines[0])


def _run_cli(arguments, label: str):
    env = os.environ.copy()
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["PYTHONNOUSERSITE"] = "1"
    try:
        process = run_bounded(
            [sys.executable, "-m", "febuildergba_playtest", *arguments],
            cwd=TOOL_DIR,
            env=env,
            timeout_seconds=180,
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


def _scenario(rom: bytes, released: bool):
    keys = [{"frame": 0, "keys": ["A"]}]
    watchdogs = []
    run_frames = 2
    expected = DEFAULT_MARKER
    name = "synthetic-a-held"
    screenshot = "held.png"
    if released:
        keys.append({"frame": 2, "keys": []})
        watchdogs.append(
            {
                "domain": "wram",
                "address": 0,
                "width": 8,
                "maxStallFrames": 3,
                "label": "press-release-marker",
            }
        )
        run_frames = 3
        expected = 0
        name = "synthetic-a-press-release"
        screenshot = "released.png"
    return {
        "schemaVersion": 1,
        "runFrames": run_frames,
        "name": name,
        "expectedRomSha256": sha256_hex(rom),
        "expectedGameCode": header_game_code(rom),
        "keys": keys,
        "assertions": [
            {
                "domain": "wram",
                "address": 0,
                "width": 8,
                "op": "equals",
                "value": expected,
                "label": "held-a-marker" if not released else "released-a-marker",
            }
        ],
        "watchdogs": watchdogs,
        "screenshot": {"basename": screenshot},
    }


def _verify_run(result, out_path: Path, screenshot_path: Path, expected_value: int):
    if result.get("status") != "pass" or result.get("exitCode") != 0:
        raise AssertionError(f"playtest did not pass: {result!r}")
    if result.get("mgba") != {
        "version": REQUIRED_VERSION,
        "commit": REQUIRED_COMMIT,
    }:
        raise AssertionError(f"unexpected mGBA provenance: {result.get('mgba')!r}")
    if result.get("startupConfig") != EXPECTED_STARTUP:
        raise AssertionError(f"unexpected startup config: {result.get('startupConfig')!r}")
    guards = result.get("romGuard", {})
    if not guards.get("romSha256", {}).get("matched"):
        raise AssertionError("ROM SHA-256 guard did not match")
    if not guards.get("gameCode", {}).get("matched"):
        raise AssertionError("game-code guard did not match")
    assertions = result.get("assertions", [])
    if len(assertions) != 1 or not assertions[0].get("passed"):
        raise AssertionError(f"marker assertion did not pass: {assertions!r}")
    if assertions[0].get("actual") != expected_value:
        raise AssertionError(f"unexpected marker value: {assertions[0]!r}")

    persisted = json.loads(out_path.read_text(encoding="utf-8"))
    if persisted != result:
        raise AssertionError("persisted result does not match stdout result")
    if not screenshot_path.is_file():
        raise AssertionError("screenshot was not published")
    screenshot = screenshot_path.read_bytes()
    if not screenshot.startswith(b"\x89PNG\r\n\x1a\n"):
        raise AssertionError("screenshot does not have a PNG signature")
    screenshot_sha = hashlib.sha256(screenshot).hexdigest()
    artifact = result.get("artifact", {})
    if artifact.get("sha256") != screenshot_sha or artifact.get("written") is not True:
        raise AssertionError(f"screenshot evidence mismatch: {artifact!r}")
    return hashlib.sha256(_canonical_json(result).encode("ascii")).hexdigest(), screenshot_sha


def _run_scenario(output_dir: Path, work_dir: Path, rom_path: Path, rom: bytes, released: bool):
    scenario_name = "released" if released else "held"
    scenario_path = work_dir / f"{scenario_name}.json"
    scenario_path.write_text(
        _canonical_json(_scenario(rom, released)) + "\n",
        encoding="utf-8",
    )

    result_hashes = []
    screenshot_hashes = []
    for replay in range(1, REPLAY_COUNT + 1):
        replay_dir = output_dir / scenario_name / f"run-{replay}"
        replay_dir.mkdir(parents=True)
        out_path = replay_dir / "result.json"
        screenshot_name = "released.png" if released else "held.png"
        process, result = _run_cli(
            [
                f"--rom={rom_path}",
                f"--scenario={scenario_path}",
                f"--out={out_path}",
                f"--artifact-dir={replay_dir}",
            ],
            f"{scenario_name} replay {replay}",
        )
        if process.returncode != 0:
            raise AssertionError(
                f"{scenario_name} replay {replay} failed: "
                f"{process.stderr.decode('utf-8', errors='replace')[:500]}"
            )
        result_sha, screenshot_sha = _verify_run(
            result,
            out_path,
            replay_dir / screenshot_name,
            0 if released else DEFAULT_MARKER,
        )
        result_hashes.append(result_sha)
        screenshot_hashes.append(screenshot_sha)

    if len(set(result_hashes)) != 1:
        raise AssertionError(f"{scenario_name} result JSON is not deterministic")
    if len(set(screenshot_hashes)) != 1:
        raise AssertionError(f"{scenario_name} screenshot is not deterministic")
    return {
        "resultSha256": result_hashes[0],
        "screenshotSha256": screenshot_hashes[0],
        "replays": REPLAY_COUNT,
    }


def main(argv=None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if len(args) != 2 or args[0] != "--output-dir":
        raise SystemExit("usage: run_real_mgba_proof.py --output-dir PATH")

    output_dir = Path(args[1]).resolve()
    output_dir.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix="febuildergba-playtest-proof-") as temp:
        work_dir = Path(temp)
        rom = build_synthetic_rom()
        rom_path = work_dir / "synthetic-playtest.gba"
        rom_path.write_bytes(rom)

        check_process, check = _run_cli(["--check"], "binding check")
        if check_process.returncode != 0 or check.get("status") != "check_ok":
            raise AssertionError(f"binding check failed: {check!r}")
        if check.get("mgba") != {
            "version": REQUIRED_VERSION,
            "commit": REQUIRED_COMMIT,
        }:
            raise AssertionError(f"binding check provenance mismatch: {check!r}")

        held = _run_scenario(output_dir, work_dir, rom_path, rom, released=False)
        released = _run_scenario(output_dir, work_dir, rom_path, rom, released=True)

        side_effects = sorted(
            str(path)
            for root in (work_dir, output_dir)
            for path in root.rglob("*")
            if path.is_file() and path.suffix.lower() in SAVE_SUFFIXES
        )
        if side_effects:
            raise AssertionError(f"unexpected save-file side effects: {side_effects!r}")

    summary = {
        "proofSchemaVersion": 1,
        "mgba": check["mgba"],
        "romSha256": sha256_hex(rom),
        "replayCount": REPLAY_COUNT * 2,
        "pressObserved": True,
        "releaseObserved": True,
        "saveSideEffects": [],
        "scenarios": {"held": held, "released": released},
    }
    (output_dir / "proof-summary.json").write_text(
        _canonical_json(summary) + "\n",
        encoding="utf-8",
    )
    sys.stdout.write(_canonical_json(summary) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
