"""Deterministic scenario runner and backend protocol.

The runner drives an abstract :class:`Backend` (satisfied by the pinned mGBA
adapter in production and by a pure-Python fake in tests) through a scenario and
returns a stable, sanitized result dictionary plus a process exit code.

Result documents intentionally exclude timestamps, wall-clock durations,
interpreter/temporary/absolute paths, stack traces, and raw ROM/RAM bytes so
that two runs of the same pinned build produce byte-identical normalized JSON.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import stat
import tempfile
from typing import Any, Dict, List, Optional, Protocol, Tuple

from .model import DOMAIN_SIZES, READ_DOMAINS, Scenario

RESULT_SCHEMA_VERSION = 1

# Repository GBA product limit (32 MiB) enforced at the Python I/O boundary.
MAX_ROM_BYTES = 32 * 1024 * 1024

# Deterministic test seam: invoked with the final artifact path immediately
# before the atomic ``os.replace``. Production leaves this ``None``; tests use
# it to simulate a late symlink/reparse substitution at the destination.
_PRE_REPLACE_HOOK = None

# --- Note redaction --------------------------------------------------------

# A Windows path "tail": zero or more separator-terminated segments (each may
# contain interior spaces) followed by a final space-free component. This lets
# the redactor consume ``A B\file`` (a directory with a space) while stopping
# before trailing prose (``...\rom.gba while loading``): interior spaces are
# only absorbed inside a segment that still ends in a path separator, and the
# final basename never absorbs a space. ``:`` is excluded from the tail because
# a Windows path carries a colon only in its drive prefix, matched separately.
_WIN_TAIL = r'(?:[^\s\\/<>:"|?*][^\\/<>:"|?*]*[\\/])*[^\s\\/<>:"|?*]*'

# Python's filesystem exceptions quote pathnames. Redact those first so a final
# filename component containing spaces cannot leak after the narrower unquoted
# patterns stop at their first whitespace.
_QUOTED_PATH_PATTERNS = (
    re.compile(r'"(?:\\\\\?\\[A-Za-z]:\\|[A-Za-z]:\\|\\\\|/)[^"\r\n]*"'),
    re.compile(r"'(?:\\\\\?\\[A-Za-z]:\\|[A-Za-z]:\\|\\\\|/)[^'\r\n]*'"),
)

# Environment-dependent path shapes that must never leak into a result note.
# Applied in order; each match is replaced before the next pattern runs.
_REDACT_PATTERNS = (
    re.compile(r'\\\\\?\\(?:[A-Za-z]:[\\/])?' + _WIN_TAIL),                 # \\?\ extended-length
    re.compile(r'\\\\[^\s\\/<>:"|?*][^\\/<>:"|?*]*[\\/]' + _WIN_TAIL),      # \\host\share UNC
    re.compile(r'[A-Za-z]:[\\/]' + _WIN_TAIL),                             # C:\... or C:/... drive
    re.compile(r'(?<!\w)/(?:[^\s/][^/]*/)*[^\s/]*'),                       # /abs/unix/paths (not "and/or")
)

_PLACEHOLDER = "<path>"

# Bound the input scanned by the redactor so a hostile/unbounded value (e.g. a
# version string from a wrong local module) cannot drive pathological regex work
# or unbounded memory. The output is capped separately below.
_REDACT_INPUT_CAP = 4096


def redact_message(text: Any) -> str:
    """Collapse whitespace and strip absolute Windows/Unix paths from a note.

    This is the single shared sanitizer used for every result note so that no
    interpreter, temporary, or absolute path — nor any traceback text — can leak
    into the stable JSON output. Windows drive / UNC / extended-length paths and
    POSIX absolute paths are redacted even when they contain spaces, while
    ordinary text with slashes ("read/write", "and/or") is preserved.
    """
    try:
        cleaned = str(text)
    except BaseException as exc:
        cleaned = f"{type(exc).__name__}: <unprintable>"
    if len(cleaned) > _REDACT_INPUT_CAP:
        cleaned = cleaned[:_REDACT_INPUT_CAP]
    for pattern in _QUOTED_PATH_PATTERNS:
        cleaned = pattern.sub(_PLACEHOLDER, cleaned)
    for pattern in _REDACT_PATTERNS:
        cleaned = pattern.sub(_PLACEHOLDER, cleaned)
    cleaned = " ".join(cleaned.split())
    if len(cleaned) > 200:
        cleaned = cleaned[:200]
    return cleaned


def is_reparse_point(path: str) -> bool:
    """True if ``path`` is a symlink, Windows junction, or any reparse point.

    Used to refuse writing a screenshot through a link that could escape the
    trusted, already-canonicalized artifact directory.
    """
    if os.path.islink(path):
        return True
    isjunction = getattr(os.path, "isjunction", None)
    if isjunction is not None:
        try:
            if isjunction(path):
                return True
        except OSError:
            return True
    try:
        attributes = os.lstat(path).st_file_attributes  # Windows only
    except (AttributeError, OSError):
        return False
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return bool(attributes & reparse_flag)

# Result status -> process exit code.
STATUS_EXIT_CODES: Dict[str, int] = {
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


class BackendError(RuntimeError):
    """Raised by a backend when the emulator cannot start or run."""


class Backend(Protocol):
    """Minimal display-free emulator contract used by the runner.

    Implementations must be deterministic for a fixed build/config/host and must
    never persist state beside the ROM.
    """

    def load_rom(self, rom_bytes: bytes) -> None: ...

    def reset(self) -> None: ...

    def set_keys(self, mask: int) -> None: ...

    def run_frame(self) -> None: ...

    def read(self, domain: str, address: int, width: int) -> int: ...

    def write(self, domain: str, address: int, width: int, value: int) -> None: ...

    def crash_message(self) -> Optional[str]:
        """Return a crash description if the core has faulted, else ``None``."""

    def screenshot_png(self) -> bytes:
        """Return PNG bytes of the current frame (only called when requested)."""

    def version(self) -> Optional[str]: ...

    def commit(self) -> Optional[str]: ...

    def effective_config(self) -> Dict[str, Any]:
        """Return the verified effective startup configuration."""


def canonical_json(result: Dict[str, Any]) -> str:
    """Serialize a result to stable, sorted, compact JSON."""
    return json.dumps(result, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _game_code_from_rom(rom_bytes: bytes) -> Optional[str]:
    # GBA header game code occupies bytes 0xAC..0xAF.
    if len(rom_bytes) < 0xB0:
        return None
    raw = rom_bytes[0xAC:0xB0]
    try:
        text = raw.decode("ascii")
    except UnicodeDecodeError:
        return None
    if any(not (32 <= ord(c) < 127) for c in text):
        return None
    return text


def _safe_artifact_path(artifact_dir: str, basename: str) -> str:
    """Resolve ``basename`` strictly inside ``artifact_dir``.

    Rejects traversal, separators, and symlink / reparse-point escapes, and
    refuses to overwrite an existing non-regular target.
    """
    if os.path.sep in basename or (os.path.altsep and os.path.altsep in basename):
        raise BackendError("artifact basename must not contain path separators")
    if basename in (".", ".."):
        raise BackendError("artifact basename must not be '.' or '..'")

    real_dir = os.path.realpath(artifact_dir)
    if not os.path.isdir(real_dir):
        raise BackendError("artifact directory does not exist")

    candidate = os.path.join(real_dir, basename)
    real_candidate = os.path.realpath(candidate)

    parent = os.path.dirname(real_candidate)
    if os.path.normcase(parent) != os.path.normcase(real_dir):
        raise BackendError("artifact path escapes the artifact directory")

    if os.path.lexists(candidate):
        if is_reparse_point(candidate) or not os.path.isfile(candidate):
            raise BackendError("artifact target exists and is not a regular file")
    return candidate


def _quiet_remove(path: str) -> None:
    try:
        os.remove(path)
    except OSError:
        pass


def _reject_nonregular_target(path: str) -> None:
    """Refuse an existing destination that is a directory or reparse target.

    A regular file may be atomically replaced; a directory, symlink, junction,
    or other reparse point must never be written through or clobbered.
    """
    if os.path.lexists(path):
        if is_reparse_point(path) or os.path.isdir(path) or not os.path.isfile(path):
            raise BackendError("output target exists and is not a regular file")


def atomic_write_bytes(path: str, data: bytes) -> None:
    """Publish ``data`` to ``path`` via a same-directory temp file + rename.

    The destination is validated first (rejecting directories and reparse
    targets), then the bytes are written to a fresh temporary regular file in the
    same directory, flushed and ``fsync``-ed, and moved into place with an atomic
    :func:`os.replace`. A late symlink/junction created at the destination after
    the check cannot be followed: ``os.replace`` swaps the destination *name*
    rather than writing through a link. Every create/write/replace failure is
    normalized to a sanitized :class:`BackendError`, and the temporary file is
    removed on every failure. The temporary name is never exposed.
    """
    _reject_nonregular_target(path)
    directory = os.path.dirname(path) or "."
    try:
        fd, tmp = tempfile.mkstemp(prefix=".gba-playtest-", suffix=".tmp", dir=directory)
    except OSError as exc:
        raise BackendError(
            "failed to create temporary output: " + redact_message(exc.strerror or type(exc).__name__)
        ) from None
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        hook = _PRE_REPLACE_HOOK
        if hook is not None:
            hook(path)
        os.replace(tmp, path)
    except OSError as exc:
        _quiet_remove(tmp)
        raise BackendError(
            "failed to write output: " + redact_message(exc.strerror or type(exc).__name__)
        ) from None
    except BaseException:
        _quiet_remove(tmp)
        raise


def _publish_artifact(artifact_dir: str, basename: str, data: bytes) -> None:
    """Publish ``data`` to ``basename`` strictly inside ``artifact_dir``.

    The basename is resolved and validated by :func:`_safe_artifact_path`
    (rejecting traversal, separators, and reparse targets); the bytes are then
    written atomically via :func:`atomic_write_bytes`.
    """
    path = _safe_artifact_path(artifact_dir, basename)
    atomic_write_bytes(path, data)


class _Assertion:
    __slots__ = ("spec", "initial")

    def __init__(self, spec, initial):
        self.spec = spec
        self.initial = initial


def _assertion_result(spec, initial: int, final: int) -> Tuple[bool, Dict[str, Any]]:
    op = spec.op
    if op == "equals":
        passed = final == spec.value
        expected: Any = spec.value
    elif op == "notEquals":
        passed = final != spec.value
        expected = {"notEquals": spec.value}
    elif op == "changed":
        passed = final != initial
        expected = {"changed": True}
    else:  # inclusiveRange
        passed = spec.minValue <= final <= spec.maxValue
        expected = {"min": spec.minValue, "max": spec.maxValue}
    evidence: Dict[str, Any] = {
        "domain": spec.domain,
        "address": spec.address,
        "width": spec.width,
        "op": op,
        "expected": expected,
        "initial": initial,
        "actual": final,
        "passed": passed,
    }
    if spec.label is not None:
        evidence["label"] = spec.label
    return passed, evidence


class Runner:
    """Executes a validated scenario against a backend."""

    def __init__(self, scenario: Scenario, backend: Backend, artifact_dir: Optional[str] = None):
        self.scenario = scenario
        self.backend = backend
        self.artifact_dir = artifact_dir

    def _seam(self, label: str, fn, *args, **kwargs):
        """Call a backend seam, converting any unexpected error into a
        :class:`BackendError` with a sanitized note so it can never escape as a
        traceback or produce no JSON."""
        try:
            return fn(*args, **kwargs)
        except BackendError:
            raise
        except Exception as exc:  # noqa: BLE001 - deliberately broad seam guard
            raise BackendError(redact_message(f"{label} failed: {type(exc).__name__}")) from None

    def run(self, rom_bytes: bytes) -> Tuple[Dict[str, Any], int]:
        scenario = self.scenario
        rom_sha = _sha256_hex(rom_bytes)

        guard: Dict[str, Any] = {}
        if scenario.expectedRomSha256 is not None:
            match = scenario.expectedRomSha256 == rom_sha
            guard["romSha256"] = {"expected": scenario.expectedRomSha256, "matched": match}
            if not match:
                return self._finalize("rom_mismatch", rom_sha, 0, [], [], None, guard,
                                      note="ROM SHA-256 does not match expectedRomSha256")
        if scenario.expectedGameCode is not None:
            actual_code = _game_code_from_rom(rom_bytes)
            match = actual_code == scenario.expectedGameCode
            guard["gameCode"] = {"expected": scenario.expectedGameCode, "actual": actual_code, "matched": match}
            if not match:
                return self._finalize("rom_mismatch", rom_sha, 0, [], [], None, guard,
                                      note="game code does not match expectedGameCode")

        frames_executed = 0
        assertion_reports: List[Dict[str, Any]] = []
        watch_state: List[Dict[str, Any]] = []
        try:
            self._seam("load", self.backend.load_rom, rom_bytes)
            self._seam("reset", self.backend.reset)

            # Frame-indexed schedules.
            key_by_frame = {event.frame: event.mask() for event in scenario.events}
            writes_by_frame: Dict[int, List] = {}
            for w in scenario.writes:
                writes_by_frame.setdefault(w.frame, []).append(w)

            # Capture initial values for 'changed' assertions and watchdogs after reset.
            assertions = [
                _Assertion(spec, self._seam("read", self.backend.read, spec.domain, spec.address, spec.width))
                for spec in scenario.assertions
            ]
            for wd in scenario.watchdogs:
                value = self._seam("read", self.backend.read, wd.domain, wd.address, wd.width)
                watch_state.append({"spec": wd, "last_value": value, "stall": 0})

            for frame in range(scenario.runFrames):
                if frame in key_by_frame:
                    self._seam("keys", self.backend.set_keys, key_by_frame[frame])
                for w in writes_by_frame.get(frame, ()):
                    self._seam("write", self.backend.write, w.domain, w.address, w.width, w.value)

                self._seam("run_frame", self.backend.run_frame)
                frames_executed = frame + 1

                crash = self._seam("crash_query", self.backend.crash_message)
                if crash is not None:
                    return self._finalize("crash", rom_sha, frames_executed, [], _watch_report(watch_state),
                                          None, guard, note="core crash: " + _sanitize_note(crash))

                softlock = self._check_watchdogs(watch_state)
                if softlock is not None:
                    return self._finalize("softlock", rom_sha, frames_executed, [], _watch_report(watch_state),
                                          None, guard, note=softlock)

            # Evaluate final assertions.
            all_passed = True
            for entry in assertions:
                final = self._seam("read", self.backend.read, entry.spec.domain, entry.spec.address, entry.spec.width)
                passed, evidence = _assertion_result(entry.spec, entry.initial, final)
                all_passed = all_passed and passed
                assertion_reports.append(evidence)

            artifact_report = None
            if scenario.screenshot is not None:
                artifact_report = self._capture_screenshot()
                if artifact_report.get("shaMatched") is False:
                    all_passed = False

            status = "pass" if all_passed else "assertion_failed"
            return self._finalize(status, rom_sha, frames_executed, assertion_reports,
                                  _watch_report(watch_state), artifact_report, guard)
        except BackendError as exc:
            return self._finalize("harness_error", rom_sha, frames_executed, assertion_reports,
                                  _watch_report(watch_state), None, guard, note=str(exc))

    def _check_watchdogs(self, watch_state) -> Optional[str]:
        for state in watch_state:
            spec = state["spec"]
            current = self._seam("read", self.backend.read, spec.domain, spec.address, spec.width)
            if current != state["last_value"]:
                state["last_value"] = current
                state["stall"] = 0
            else:
                state["stall"] += 1
                if state["stall"] >= spec.maxStallFrames:
                    label = spec.label or f"{spec.domain}:0x{spec.address:X}"
                    return f"watchdog '{label}' stalled for {state['stall']} frames"
        return None

    def _capture_screenshot(self) -> Dict[str, Any]:
        shot = self.scenario.screenshot
        png = self._seam("screenshot", self.backend.screenshot_png)
        if not isinstance(png, (bytes, bytearray)) or not png:
            raise BackendError("screenshot capture produced no data")
        png = bytes(png)
        sha = _sha256_hex(png)
        report: Dict[str, Any] = {"basename": shot.basename, "sha256": sha}
        if self.artifact_dir is not None:
            _publish_artifact(self.artifact_dir, shot.basename, png)
            report["written"] = True
        else:
            report["written"] = False
        if shot.expectedSha256 is not None:
            report["expectedSha256"] = shot.expectedSha256
            report["shaMatched"] = shot.expectedSha256 == sha
        return report

    def _safe_meta(self, fn, default):
        try:
            return fn()
        except Exception:  # noqa: BLE001 - metadata must never break result emission
            return default

    def _finalize(self, status: str, rom_sha: str, frames_executed: int,
                  assertion_reports: List[Dict[str, Any]], watch_reports: List[Dict[str, Any]],
                  artifact_report: Optional[Dict[str, Any]], guard: Dict[str, Any],
                  note: Optional[str] = None) -> Tuple[Dict[str, Any], int]:
        result: Dict[str, Any] = {
            "resultSchemaVersion": RESULT_SCHEMA_VERSION,
            "scenarioSchemaVersion": self.scenario.schemaVersion,
            "status": status,
            "exitCode": STATUS_EXIT_CODES[status],
            "romSha256": rom_sha,
            "requestedRunFrames": self.scenario.runFrames,
            "framesExecuted": frames_executed,
            "assertions": assertion_reports,
            "watchdogs": watch_reports,
            "mgba": {
                "version": sanitize_meta(self._safe_meta(self.backend.version, None)),
                "commit": sanitize_meta(self._safe_meta(self.backend.commit, None)),
            },
            "startupConfig": self._safe_meta(self.backend.effective_config, {}),
        }
        if self.scenario.name is not None:
            result["scenarioName"] = self.scenario.name
        if guard:
            result["romGuard"] = guard
        if artifact_report is not None:
            result["artifact"] = artifact_report
        if note is not None:
            result["note"] = _sanitize_note(note)
        return result, STATUS_EXIT_CODES[status]


def _watch_report(watch_state) -> List[Dict[str, Any]]:
    reports: List[Dict[str, Any]] = []
    for state in watch_state:
        spec = state["spec"]
        entry: Dict[str, Any] = {
            "domain": spec.domain,
            "address": spec.address,
            "width": spec.width,
            "maxStallFrames": spec.maxStallFrames,
            "stalledFrames": state["stall"],
        }
        if spec.label is not None:
            entry["label"] = spec.label
        reports.append(entry)
    return reports


def _sanitize_note(text: str) -> str:
    """Strip anything path-like or environment-dependent from a note."""
    return redact_message(text)


def sanitize_meta(value: Any) -> Optional[str]:
    """Bound and sanitize a version/commit string for result JSON.

    A wrong or hostile local module could return an unbounded or path-bearing
    value from ``version()``/``commit()`` (or ``mgba.__version__``). Preserve
    ``None`` but otherwise redact any embedded path and cap the length so the
    stable result can never carry unbounded or environment-dependent metadata.
    """
    if value is None:
        return None
    return redact_message(value)[:100]


__all__ = [
    "Backend",
    "BackendError",
    "Runner",
    "RESULT_SCHEMA_VERSION",
    "STATUS_EXIT_CODES",
    "MAX_ROM_BYTES",
    "canonical_json",
    "redact_message",
    "sanitize_meta",
    "atomic_write_bytes",
    "is_reparse_point",
]
