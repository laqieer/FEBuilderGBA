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

import json
import os
import re
import sys
from typing import Any, Dict, List, Optional, Tuple

from .model import (
    MAX_SCENARIO_BYTES,
    ScenarioError,
    load_scenario,
    parse_json,
)
from .runner import (
    MAX_ROM_BYTES,
    RESULT_SCHEMA_VERSION,
    STATUS_EXIT_CODES,
    BackendError,
    Runner,
    atomic_write_bytes,
    canonical_json,
    redact_message,
    sanitize_meta,
)


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
        "note": redact_message(note),
    }
    if extra:
        result.update(extra)
    return result, STATUS_EXIT_CODES[status]


def _run_check() -> Tuple[Dict[str, Any], int]:
    try:
        from .mgba_backend import REQUIRED_COMMIT, REQUIRED_VERSION, check_available
    except Exception as exc:  # pragma: no cover - defensive
        return _error_result("dependency_error", f"backend import failed: {type(exc).__name__}")
    try:
        info = check_available()
    except Exception as exc:  # noqa: BLE001 - never let the backend crash the CLI
        return _error_result("dependency_error", f"backend check failed: {type(exc).__name__}")
    status = "check_ok" if info.get("available") else "check_failed"
    result: Dict[str, Any] = {
        "resultSchemaVersion": RESULT_SCHEMA_VERSION,
        "status": status,
        "exitCode": STATUS_EXIT_CODES[status],
        "requiredVersion": REQUIRED_VERSION,
        "requiredCommit": REQUIRED_COMMIT,
        # A wrong local module could return unbounded / path-bearing metadata;
        # bound and redact both fields before they enter the stable result.
        "mgba": {
            "version": sanitize_meta(info.get("version")),
            "commit": sanitize_meta(info.get("commit")),
        },
    }
    if info.get("reason"):
        result["note"] = redact_message(info["reason"])
    return result, STATUS_EXIT_CODES[status]


class _TooLarge(Exception):
    """Raised when a bounded read would exceed its cap."""

    def __init__(self, prefix: bytes = b""):
        super().__init__("bounded read exceeded its cap")
        self.prefix = prefix


def _read_capped(path: str, cap: int) -> bytes:
    """Read at most ``cap + 1`` bytes; raise :class:`_TooLarge` if oversize.

    This bounds untrusted file sizes at the I/O boundary so an enormous ROM or
    scenario cannot be slurped whole into memory before the size check.
    """
    with open(path, "rb") as handle:
        data = handle.read(cap + 1)
    if len(data) > cap:
        raise _TooLarge(data[:cap])
    return data


def _paths_collide(a: Optional[str], b: Optional[str]) -> bool:
    """True if ``a`` and ``b`` resolve to the same filesystem target.

    Detects both physical aliases (existing files reached by different paths,
    via :func:`os.path.samefile`) and future targets reached through lexical or
    symlinked-directory aliases.
    """
    if not a or not b:
        return False
    try:
        if os.path.exists(a) and os.path.exists(b) and os.path.samefile(a, b):
            return True
    except OSError:
        pass
    try:
        left = os.path.normcase(os.path.realpath(a))
        right = os.path.normcase(os.path.realpath(b))
    except (OSError, ValueError):
        return False
    if sys.platform == "darwin":
        left = left.casefold()
        right = right.casefold()
    return left == right


def _reject_output_collisions(rom_path: str, scenario_path: str, out_path: Optional[str],
                              screenshot_dest: Optional[str]) -> Optional[Tuple[Dict[str, Any], int]]:
    """Reject any output target that would overwrite an input or another output.

    Neither input (ROM, scenario) may be mutated, so a result-output or a
    screenshot destination that aliases either input — or the two outputs
    aliasing each other — is refused before emulation starts.
    """
    pairs = (
        (out_path, rom_path),
        (out_path, scenario_path),
        (screenshot_dest, rom_path),
        (screenshot_dest, scenario_path),
        (out_path, screenshot_dest),
    )
    for dest, other in pairs:
        if _paths_collide(dest, other):
            return _error_result(
                "harness_error",
                "output path collides with an input file or another output path",
            )
    return None


def _persist_result(
    result: Dict[str, Any],
    exit_code: int,
    out_path: Optional[str],
) -> Tuple[Dict[str, Any], int]:
    if out_path is None:
        return result, exit_code
    try:
        atomic_write_bytes(
            out_path,
            (canonical_json(result) + "\n").encode("utf-8"),
        )
    except BackendError as exc:
        return _error_result("harness_error", str(exc))
    return result, exit_code


def _run_scenario(rom_path: str, scenario_path: str, out_path: Optional[str],
                  artifact_dir: Optional[str]) -> Tuple[Dict[str, Any], int]:
    collision = _reject_output_collisions(
        rom_path,
        scenario_path,
        out_path,
        None,
    )
    if collision is not None:
        return collision

    def finish(outcome: Tuple[Dict[str, Any], int]):
        result, exit_code = outcome
        return _persist_result(result, exit_code, out_path)

    def reject_screenshot_collision(
        basename: Optional[str],
    ) -> Optional[Tuple[Dict[str, Any], int]]:
        if basename is None or artifact_dir is None:
            return None
        return _reject_output_collisions(
            rom_path,
            scenario_path,
            out_path,
            os.path.join(artifact_dir, basename),
        )

    def reject_recovered_screenshot_collisions(
        text: str,
    ) -> Optional[Tuple[Dict[str, Any], int]]:
        try:
            recovered_basenames = _recover_screenshot_basenames(text)
        except _ScreenshotRecoveryLimit:
            return _error_result(
                "harness_error",
                "scenario screenshot recovery exceeds the work limit",
            )
        for recovered_basename in recovered_basenames:
            recovered_collision = reject_screenshot_collision(
                recovered_basename
            )
            if recovered_collision is not None:
                return recovered_collision
        return None

    try:
        scenario_bytes = _read_capped(scenario_path, MAX_SCENARIO_BYTES)
    except _TooLarge as exc:
        collision = reject_recovered_screenshot_collisions(
            exc.prefix.decode("utf-8", errors="replace")
        )
        if collision is not None:
            return collision
        return finish(_error_result(
            "scenario_error", f"scenario exceeds the maximum size of {MAX_SCENARIO_BYTES} bytes"
        ))
    except OSError as exc:
        return finish(_error_result(
            "harness_error",
            f"cannot read scenario: {exc.strerror}",
        ))
    try:
        scenario_text = scenario_bytes.decode("utf-8")
    except UnicodeDecodeError:
        collision = reject_recovered_screenshot_collisions(
            scenario_bytes.decode("utf-8", errors="replace")
        )
        if collision is not None:
            return collision
        return finish(_error_result(
            "scenario_error",
            "scenario is not valid UTF-8 text",
        ))

    try:
        raw_scenario = parse_json(scenario_text)
    except ScenarioError as exc:
        collision = reject_recovered_screenshot_collisions(scenario_text)
        if collision is not None:
            return collision
        return finish(_error_result("scenario_error", str(exc)))

    raw_basename = None
    if isinstance(raw_scenario, dict) and artifact_dir is not None:
        raw_screenshot = raw_scenario.get("screenshot")
        if isinstance(raw_screenshot, dict):
            candidate = raw_screenshot.get("basename")
            if isinstance(candidate, str):
                raw_basename = candidate
    collision = reject_screenshot_collision(raw_basename)
    if collision is not None:
        return collision

    try:
        scenario = load_scenario(scenario_text)
    except ScenarioError as exc:
        return finish(_error_result("scenario_error", str(exc)))

    try:
        rom_bytes = _read_capped(rom_path, MAX_ROM_BYTES)
    except _TooLarge:
        return finish(_error_result(
            "harness_error", f"ROM exceeds the maximum size of {MAX_ROM_BYTES} bytes"
        ))
    except OSError as exc:
        return finish(_error_result(
            "harness_error",
            f"cannot read ROM: {exc.strerror}",
        ))
    if not rom_bytes:
        return finish(_error_result("harness_error", "ROM file is empty"))

    # Refuse destructive path collisions before touching the emulator so neither
    # input can be overwritten by the result output or a screenshot.
    screenshot_dest = None
    if scenario.screenshot is not None and artifact_dir is not None:
        screenshot_dest = os.path.join(artifact_dir, scenario.screenshot.basename)
    collision = _reject_output_collisions(rom_path, scenario_path, out_path, screenshot_dest)
    if collision is not None:
        return collision

    try:
        from .mgba_backend import MgbaBackend
    except Exception as exc:
        return finish(_error_result(
            "dependency_error",
            f"mGBA backend unavailable: {type(exc).__name__}",
        ))

    try:
        backend = MgbaBackend(want_screenshot=scenario.screenshot is not None)
    except BackendError as exc:
        return finish(_error_result("dependency_error", str(exc)))

    runner = Runner(scenario, backend, artifact_dir=artifact_dir)
    try:
        result, exit_code = runner.run(rom_bytes)
    except BackendError as exc:
        result, exit_code = _error_result("harness_error", str(exc))
    finally:
        # Deterministic native teardown ALWAYS runs after backend construction
        # and before any result is persisted or returned.
        cleanup = _close_backend(backend)

    # A cleanup failure overrides any earlier result: a pass is never persisted
    # or reported after teardown failed.
    if cleanup is not None:
        result, exit_code = cleanup

    return finish((result, exit_code))


def _close_backend(backend: Any) -> Optional[Tuple[Dict[str, Any], int]]:
    """Close the backend, converting any teardown failure into a harness_error.

    Returns ``None`` on a clean close, otherwise a sanitized ``harness_error``
    result tuple that must override any earlier result so a ``pass`` is never
    persisted or reported after cleanup failed. Only :class:`BackendError` and a
    bounded ordinary :class:`Exception` (surfaced as a static, type-only note)
    are handled here; ``BaseException``/signals are never caught.
    """
    try:
        backend.close()
    except BackendError as exc:
        return _error_result("harness_error", str(exc))
    except Exception as exc:  # noqa: BLE001 - bounded, type-only sanitized note
        return _error_result("harness_error", f"backend cleanup failed: {type(exc).__name__}")
    return None


# --- Deterministic, no-output argument parsing -----------------------------
# argparse writes usage/help to stdout/stderr and exits, which would violate the
# "exactly one JSON document on stdout" contract and could leak the program name
# or argument text. This hand parser never writes anything: every malformed,
# unknown, duplicate, or help-shaped argv becomes a single sanitized JSON error.

_FLAG_OPTS = frozenset({"--check"})
_VALUE_OPTS = frozenset({"--rom", "--scenario", "--out", "--artifact-dir"})
_ALL_OPTS = _FLAG_OPTS | _VALUE_OPTS
_RAW_JSON_KEY = re.compile(
    r'"((?:\\.|[^"\\]){1,255})"\s*:',
)
_RAW_JSON_STRING_VALUE = re.compile(
    r'\s*"((?:\\.|[^"\\]){1,255})"',
)
_RAW_SCREENSHOT_OBJECT_CHARS = 4096
_MAX_RAW_JSON_KEY_CANDIDATES = 4096
_MAX_RAW_SCREENSHOT_OBJECTS = 64


class _UsageError(Exception):
    """Raised for any malformed command line. Messages are static (no paths)."""


class _ScreenshotRecoveryLimit(Exception):
    """Raised when malformed screenshot recovery exceeds its work budget."""


def _decode_json_string_fragment(fragment: str) -> Optional[str]:
    try:
        value = json.loads('"' + fragment + '"')
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None
    return value if isinstance(value, str) else None


def _bounded_object_body(text: str, start: int) -> Optional[str]:
    index = start
    while index < len(text) and text[index].isspace():
        index += 1
    if index >= len(text) or text[index] != "{":
        return None

    body_start = index + 1
    limit = min(
        len(text),
        body_start + _RAW_SCREENSHOT_OBJECT_CHARS,
    )
    depth = 1
    in_string = False
    escaped = False
    for index in range(body_start, limit):
        char = text[index]
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[body_start:index]
    if limit < len(text):
        raise _ScreenshotRecoveryLimit()
    return text[body_start:limit]


def _recover_screenshot_basenames(text: str) -> Tuple[str, ...]:
    recovered = []
    key_candidates = 0
    screenshot_objects = 0
    for match in _RAW_JSON_KEY.finditer(text):
        key_candidates += 1
        if key_candidates > _MAX_RAW_JSON_KEY_CANDIDATES:
            raise _ScreenshotRecoveryLimit()
        key = _decode_json_string_fragment(match.group(1))
        if key != "screenshot":
            continue
        screenshot_objects += 1
        if screenshot_objects > _MAX_RAW_SCREENSHOT_OBJECTS:
            raise _ScreenshotRecoveryLimit()
        body = _bounded_object_body(text, match.end())
        if body is None:
            continue
        for inner_match in _RAW_JSON_KEY.finditer(body):
            inner_key = _decode_json_string_fragment(
                inner_match.group(1)
            )
            if inner_key != "basename":
                continue
            value_match = _RAW_JSON_STRING_VALUE.match(
                body,
                inner_match.end(),
            )
            if value_match is None:
                continue
            value = _decode_json_string_fragment(value_match.group(1))
            if value:
                recovered.append(value)
    return tuple(recovered)


def _parse_args(argv: List[str]) -> Tuple[Dict[str, bool], Dict[str, str]]:
    flags: Dict[str, bool] = {}
    values: Dict[str, str] = {}
    seen: set = set()
    index = 0
    count = len(argv)
    while index < count:
        token = argv[index]
        if token in ("-h", "--help"):
            raise _UsageError("help output is unavailable; this tool emits a single JSON result")
        if token.startswith("--") and "=" in token:
            name, _, inline = token.partition("=")
        elif token.startswith("-") and token != "-":
            name, inline = token, None
        else:
            raise _UsageError("unexpected positional argument")
        if name not in _ALL_OPTS:
            raise _UsageError("unknown option")
        if name in seen:
            raise _UsageError("duplicate option")
        seen.add(name)
        if name in _FLAG_OPTS:
            if inline is not None:
                raise _UsageError("option takes no value")
            flags[name] = True
            index += 1
            continue
        if inline is None:
            if index + 1 >= count:
                raise _UsageError("missing value for option")
            inline = argv[index + 1]
            if inline.startswith("-"):
                raise _UsageError("missing value for option")
            index += 2
        else:
            index += 1
        if inline == "":
            raise _UsageError("empty value for option")
        values[name] = inline
    return flags, values


def _dispatch(argv: Optional[List[str]]) -> Tuple[Dict[str, Any], int]:
    args = list(sys.argv[1:] if argv is None else argv)
    try:
        flags, values = _parse_args(args)
    except _UsageError as exc:
        return _error_result("harness_error", f"invalid command-line arguments: {exc}")

    check = flags.get("--check", False)
    rom = values.get("--rom")
    scenario = values.get("--scenario")
    out = values.get("--out")
    artifact_dir = values.get("--artifact-dir")

    if check:
        if rom or scenario or out or artifact_dir:
            return _error_result(
                "harness_error",
                "--check cannot be combined with --rom/--scenario/--out/--artifact-dir",
            )
        return _run_check()

    if not rom or not scenario:
        return _error_result("harness_error", "--rom and --scenario are required")

    return _run_scenario(rom, scenario, out, artifact_dir)


def main(argv: Optional[List[str]] = None) -> int:
    try:
        result, code = _dispatch(argv)
    except BaseException as exc:  # noqa: BLE001 - guarantee exactly one JSON object
        # Never leak a traceback or path to stdout; emit a single sanitized doc.
        result, code = _error_result(
            "harness_error", f"unexpected failure: {type(exc).__name__}"
        )
    return _emit(result, code)


if __name__ == "__main__":
    sys.exit(main())
