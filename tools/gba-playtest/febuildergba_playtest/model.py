"""Strict, data-only scenario model for the FEBuilderGBA headless playtest engine.

This module parses and validates a versioned JSON scenario document. It contains
no emulator, filesystem, or network logic: it turns untrusted JSON text into a
frozen, fully-validated :class:`Scenario` object or raises :class:`ScenarioError`.

Security / determinism rules enforced here:

* Only ``schemaVersion == 1`` is accepted.
* Duplicate JSON object keys are rejected.
* Unknown object properties are rejected.
* ``NaN`` / ``Infinity`` and non-integer numeric values are rejected.
* Booleans are never accepted where integers are expected.
* Frame counts, event/write/assertion/watchdog counts, and the raw document
  size have hard upper bounds.
* Writes are restricted to WRAM / IWRAM; reads use an explicit non-ROM domain
  allowlist.
* Addresses are bounded to their memory domain and naturally aligned.
* Duplicate / overlapping input events and duplicate writes are rejected.
* Screenshot basenames must be safe single path components (no separators,
  traversal, absolute paths, or drive letters). Filesystem-level symlink /
  reparse-point checks are the runner's responsibility.

The scenario is *data only*: it contains no command strings, imports,
expressions, hooks, or host paths.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple

SCHEMA_VERSION = 1

# --- Hard bounds -----------------------------------------------------------

MAX_SCENARIO_BYTES = 1 << 20  # 1 MiB of scenario JSON.
MAX_RUN_FRAMES = 216_000      # One hour at 60 fps.
MAX_EVENTS = 4096
MAX_WRITES = 4096
MAX_ASSERTIONS = 1024
MAX_WATCHDOGS = 256
MAX_BASENAME_LENGTH = 128
MAX_UINT32 = 0xFFFFFFFF

# --- GBA input -------------------------------------------------------------

# GBA key bit positions as used by mGBA's ``GBAKey`` enum.
KEY_BITS: Dict[str, int] = {
    "A": 0,
    "B": 1,
    "SELECT": 2,
    "START": 3,
    "RIGHT": 4,
    "LEFT": 5,
    "UP": 6,
    "DOWN": 7,
    "R": 8,
    "L": 9,
}

# Mutually exclusive physical directions; pressing both in one frame is a
# malformed scenario rather than a real controller state.
_OPPOSITE_KEYS: Tuple[Tuple[str, str], ...] = (("LEFT", "RIGHT"), ("UP", "DOWN"))

# --- GBA memory domains ----------------------------------------------------

# Byte size of each addressable non-ROM domain.
DOMAIN_SIZES: Dict[str, int] = {
    "wram": 0x40000,   # EWRAM (0x0200_0000)
    "iwram": 0x8000,   # IWRAM (0x0300_0000)
    "io": 0x400,       # MMIO  (0x0400_0000)
    "palette": 0x400,  # (0x0500_0000)
    "vram": 0x18000,   # (0x0600_0000)
    "oam": 0x400,      # (0x0700_0000)
    "sram": 0x10000,   # (0x0E00_0000)
}

# schema-v1 writes may only target work RAM, never IO/VRAM/save/ROM.
WRITE_DOMAINS = frozenset({"wram", "iwram"})

# Explicit non-ROM read allowlist (ROM / BIOS / cart are intentionally absent).
READ_DOMAINS = frozenset(DOMAIN_SIZES.keys())

VALID_WIDTHS = frozenset({8, 16, 32})

ASSERT_OPS = frozenset({"equals", "notEquals", "changed", "inclusiveRange"})


class ScenarioError(ValueError):
    """Raised when a scenario document is missing, malformed, or unsafe."""


# --- Frozen model ----------------------------------------------------------


@dataclass(frozen=True)
class InputEvent:
    frame: int
    keys: Tuple[str, ...]  # sorted, deduplicated key names for this frame

    def mask(self) -> int:
        value = 0
        for name in self.keys:
            value |= 1 << KEY_BITS[name]
        return value


@dataclass(frozen=True)
class MemoryWrite:
    frame: int
    domain: str
    address: int
    width: int
    value: int


@dataclass(frozen=True)
class Assertion:
    domain: str
    address: int
    width: int
    op: str
    value: Optional[int] = None
    minValue: Optional[int] = None
    maxValue: Optional[int] = None
    label: Optional[str] = None


@dataclass(frozen=True)
class Watchdog:
    domain: str
    address: int
    width: int
    maxStallFrames: int
    label: Optional[str] = None


@dataclass(frozen=True)
class Screenshot:
    basename: str
    expectedSha256: Optional[str] = None


@dataclass(frozen=True)
class Scenario:
    schemaVersion: int
    runFrames: int
    name: Optional[str]
    expectedRomSha256: Optional[str]
    expectedGameCode: Optional[str]
    events: Tuple[InputEvent, ...]
    writes: Tuple[MemoryWrite, ...]
    assertions: Tuple[Assertion, ...]
    watchdogs: Tuple[Watchdog, ...]
    screenshot: Optional[Screenshot]


# --- JSON parsing helpers --------------------------------------------------


def _no_duplicate_keys(pairs: List[Tuple[str, Any]]) -> Dict[str, Any]:
    seen: Dict[str, Any] = {}
    for key, value in pairs:
        if key in seen:
            raise ScenarioError(f"duplicate JSON key: {key!r}")
        seen[key] = value
    return seen


def _reject_constant(token: str) -> float:
    raise ScenarioError(f"non-finite JSON number is not allowed: {token}")


def parse_json(text: str) -> Any:
    if len(text.encode("utf-8")) > MAX_SCENARIO_BYTES:
        raise ScenarioError("scenario document exceeds maximum size")
    try:
        return json.loads(
            text,
            object_pairs_hook=_no_duplicate_keys,
            parse_constant=_reject_constant,
        )
    except ScenarioError:
        raise
    except json.JSONDecodeError as exc:
        raise ScenarioError(f"invalid JSON: {exc}") from exc


def _require_object(value: Any, where: str) -> Dict[str, Any]:
    if not isinstance(value, dict):
        raise ScenarioError(f"{where} must be a JSON object")
    return value


def _reject_unknown(obj: Dict[str, Any], allowed: frozenset, where: str) -> None:
    extra = set(obj.keys()) - allowed
    if extra:
        joined = ", ".join(sorted(extra))
        raise ScenarioError(f"{where} has unknown propert{'y' if len(extra) == 1 else 'ies'}: {joined}")


def _as_int(value: Any, where: str) -> int:
    # Booleans are ``int`` subclasses in Python and must be rejected explicitly.
    if isinstance(value, bool) or not isinstance(value, int):
        raise ScenarioError(f"{where} must be an integer")
    return value


def _coerce_uint(value: Any, where: str) -> int:
    """Accept a non-negative integer or a ``0x``-prefixed hex string."""
    if isinstance(value, bool):
        raise ScenarioError(f"{where} must be an unsigned integer")
    if isinstance(value, int):
        result = value
    elif isinstance(value, str):
        token = value.strip()
        if not token.lower().startswith("0x"):
            raise ScenarioError(f"{where} string must be 0x-prefixed hex")
        try:
            result = int(token, 16)
        except ValueError as exc:
            raise ScenarioError(f"{where} is not valid hex: {value!r}") from exc
    else:
        raise ScenarioError(f"{where} must be an integer or hex string")
    if result < 0:
        raise ScenarioError(f"{where} must be non-negative")
    return result


def _bounded_int(value: Any, where: str, low: int, high: int) -> int:
    result = _as_int(value, where)
    if result < low or result > high:
        raise ScenarioError(f"{where} must be in [{low}, {high}]")
    return result


def _optional_str(obj: Dict[str, Any], key: str, where: str) -> Optional[str]:
    if key not in obj:
        return None
    value = obj[key]
    if not isinstance(value, str):
        raise ScenarioError(f"{where}.{key} must be a string")
    return value


def _value_fits(width: int, value: int, where: str) -> int:
    limit = (1 << width) - 1
    if value < 0 or value > limit:
        raise ScenarioError(f"{where} value {value} does not fit in {width}-bit width")
    return value


def _validate_mem_ref(domain: str, address: int, width: int, domains: frozenset, where: str) -> None:
    if domain not in domains:
        raise ScenarioError(f"{where} uses unsupported domain: {domain!r}")
    if width not in VALID_WIDTHS:
        raise ScenarioError(f"{where} width must be one of 8, 16, 32")
    size = DOMAIN_SIZES[domain]
    byte_width = width // 8
    if address % byte_width != 0:
        raise ScenarioError(f"{where} address 0x{address:X} is not {width}-bit aligned")
    if address < 0 or address + byte_width > size:
        raise ScenarioError(f"{where} address 0x{address:X} is out of range for domain {domain!r}")


def _validate_hex_sha256(value: str, where: str) -> str:
    token = value.strip().lower()
    if len(token) != 64 or any(c not in "0123456789abcdef" for c in token):
        raise ScenarioError(f"{where} must be a 64-character hex SHA-256")
    return token


def _validate_game_code(value: str, where: str) -> str:
    if not (1 <= len(value) <= 4) or any(not (32 <= ord(c) < 127) for c in value):
        raise ScenarioError(f"{where} must be 1-4 printable ASCII characters")
    return value


def _validate_basename(value: str, where: str) -> str:
    if not value or len(value) > MAX_BASENAME_LENGTH:
        raise ScenarioError(f"{where} must be a non-empty basename <= {MAX_BASENAME_LENGTH} chars")
    if value in (".", ".."):
        raise ScenarioError(f"{where} must not be '.' or '..'")
    if "/" in value or "\\" in value or "\x00" in value:
        raise ScenarioError(f"{where} must not contain path separators or NUL")
    if ":" in value:
        raise ScenarioError(f"{where} must not contain a drive or stream separator")
    for ch in value:
        if not (ch.isalnum() or ch in "._-"):
            raise ScenarioError(f"{where} contains an unsupported character: {ch!r}")
    return value


# --- Section parsers -------------------------------------------------------


def _parse_events(raw: Any, run_frames: int) -> Tuple[InputEvent, ...]:
    if not isinstance(raw, list):
        raise ScenarioError("keys must be a JSON array")
    if len(raw) > MAX_EVENTS:
        raise ScenarioError("too many input events")
    events: List[InputEvent] = []
    seen_frames: set = set()
    for index, item in enumerate(raw):
        where = f"keys[{index}]"
        obj = _require_object(item, where)
        _reject_unknown(obj, frozenset({"frame", "keys"}), where)
        if "frame" not in obj:
            raise ScenarioError(f"{where} is missing 'frame'")
        frame = _bounded_int(obj["frame"], f"{where}.frame", 0, run_frames - 1)
        if frame in seen_frames:
            raise ScenarioError(f"{where} duplicates an input frame ({frame})")
        seen_frames.add(frame)
        keys_raw = obj.get("keys", [])
        if not isinstance(keys_raw, list):
            raise ScenarioError(f"{where}.keys must be an array")
        names: List[str] = []
        for key in keys_raw:
            if not isinstance(key, str) or key not in KEY_BITS:
                raise ScenarioError(f"{where}.keys has an unsupported key: {key!r}")
            if key in names:
                raise ScenarioError(f"{where}.keys duplicates key {key!r}")
            names.append(key)
        for a, b in _OPPOSITE_KEYS:
            if a in names and b in names:
                raise ScenarioError(f"{where}.keys presses opposite directions {a}+{b}")
        events.append(InputEvent(frame=frame, keys=tuple(sorted(names))))
    events.sort(key=lambda e: e.frame)
    return tuple(events)


def _parse_writes(raw: Any, run_frames: int) -> Tuple[MemoryWrite, ...]:
    if not isinstance(raw, list):
        raise ScenarioError("writes must be a JSON array")
    if len(raw) > MAX_WRITES:
        raise ScenarioError("too many memory writes")
    writes: List[MemoryWrite] = []
    seen: set = set()
    for index, item in enumerate(raw):
        where = f"writes[{index}]"
        obj = _require_object(item, where)
        _reject_unknown(obj, frozenset({"frame", "domain", "address", "width", "value"}), where)
        for field in ("frame", "domain", "address", "width", "value"):
            if field not in obj:
                raise ScenarioError(f"{where} is missing '{field}'")
        frame = _bounded_int(obj["frame"], f"{where}.frame", 0, run_frames - 1)
        domain = obj["domain"]
        if not isinstance(domain, str):
            raise ScenarioError(f"{where}.domain must be a string")
        width = _as_int(obj["width"], f"{where}.width")
        address = _coerce_uint(obj["address"], f"{where}.address")
        _validate_mem_ref(domain, address, width, WRITE_DOMAINS, where)
        value = _value_fits(width, _coerce_uint(obj["value"], f"{where}.value"), where)
        dedup = (frame, domain, address, width)
        if dedup in seen:
            raise ScenarioError(f"{where} duplicates a write at frame {frame}")
        seen.add(dedup)
        writes.append(MemoryWrite(frame=frame, domain=domain, address=address, width=width, value=value))
    writes.sort(key=lambda w: (w.frame, w.domain, w.address))
    return tuple(writes)


def _parse_assertions(raw: Any) -> Tuple[Assertion, ...]:
    if not isinstance(raw, list):
        raise ScenarioError("assertions must be a JSON array")
    if len(raw) > MAX_ASSERTIONS:
        raise ScenarioError("too many assertions")
    allowed = frozenset({"domain", "address", "width", "op", "value", "min", "max", "label"})
    result: List[Assertion] = []
    for index, item in enumerate(raw):
        where = f"assertions[{index}]"
        obj = _require_object(item, where)
        _reject_unknown(obj, allowed, where)
        for field in ("domain", "address", "width", "op"):
            if field not in obj:
                raise ScenarioError(f"{where} is missing '{field}'")
        domain = obj["domain"]
        if not isinstance(domain, str):
            raise ScenarioError(f"{where}.domain must be a string")
        width = _as_int(obj["width"], f"{where}.width")
        address = _coerce_uint(obj["address"], f"{where}.address")
        _validate_mem_ref(domain, address, width, READ_DOMAINS, where)
        op = obj["op"]
        if op not in ASSERT_OPS:
            raise ScenarioError(f"{where}.op must be one of {sorted(ASSERT_OPS)}")
        value = min_value = max_value = None
        if op in ("equals", "notEquals"):
            if "value" not in obj:
                raise ScenarioError(f"{where} requires 'value' for op {op}")
            if "min" in obj or "max" in obj:
                raise ScenarioError(f"{where} must not set min/max for op {op}")
            value = _value_fits(width, _coerce_uint(obj["value"], f"{where}.value"), where)
        elif op == "changed":
            if "value" in obj or "min" in obj or "max" in obj:
                raise ScenarioError(f"{where} must not set value/min/max for op changed")
        else:  # inclusiveRange
            if "min" not in obj or "max" not in obj:
                raise ScenarioError(f"{where} requires 'min' and 'max' for inclusiveRange")
            if "value" in obj:
                raise ScenarioError(f"{where} must not set 'value' for inclusiveRange")
            min_value = _value_fits(width, _coerce_uint(obj["min"], f"{where}.min"), where)
            max_value = _value_fits(width, _coerce_uint(obj["max"], f"{where}.max"), where)
            if min_value > max_value:
                raise ScenarioError(f"{where} min must be <= max")
        result.append(Assertion(
            domain=domain, address=address, width=width, op=op,
            value=value, minValue=min_value, maxValue=max_value,
            label=_optional_str(obj, "label", where),
        ))
    return tuple(result)


def _parse_watchdogs(raw: Any, run_frames: int) -> Tuple[Watchdog, ...]:
    if not isinstance(raw, list):
        raise ScenarioError("watchdogs must be a JSON array")
    if len(raw) > MAX_WATCHDOGS:
        raise ScenarioError("too many watchdogs")
    allowed = frozenset({"domain", "address", "width", "maxStallFrames", "label"})
    result: List[Watchdog] = []
    for index, item in enumerate(raw):
        where = f"watchdogs[{index}]"
        obj = _require_object(item, where)
        _reject_unknown(obj, allowed, where)
        for field in ("domain", "address", "width", "maxStallFrames"):
            if field not in obj:
                raise ScenarioError(f"{where} is missing '{field}'")
        domain = obj["domain"]
        if not isinstance(domain, str):
            raise ScenarioError(f"{where}.domain must be a string")
        width = _as_int(obj["width"], f"{where}.width")
        address = _coerce_uint(obj["address"], f"{where}.address")
        _validate_mem_ref(domain, address, width, READ_DOMAINS, where)
        max_stall = _bounded_int(obj["maxStallFrames"], f"{where}.maxStallFrames", 1, run_frames)
        result.append(Watchdog(
            domain=domain, address=address, width=width,
            maxStallFrames=max_stall, label=_optional_str(obj, "label", where),
        ))
    return tuple(result)


def _parse_screenshot(raw: Any) -> Optional[Screenshot]:
    obj = _require_object(raw, "screenshot")
    _reject_unknown(obj, frozenset({"basename", "expectedSha256"}), "screenshot")
    if "basename" not in obj:
        raise ScenarioError("screenshot is missing 'basename'")
    if not isinstance(obj["basename"], str):
        raise ScenarioError("screenshot.basename must be a string")
    basename = _validate_basename(obj["basename"], "screenshot.basename")
    expected = None
    if "expectedSha256" in obj:
        if not isinstance(obj["expectedSha256"], str):
            raise ScenarioError("screenshot.expectedSha256 must be a string")
        expected = _validate_hex_sha256(obj["expectedSha256"], "screenshot.expectedSha256")
    return Screenshot(basename=basename, expectedSha256=expected)


# --- Public API ------------------------------------------------------------

_TOP_LEVEL_KEYS = frozenset({
    "schemaVersion", "runFrames", "name", "expectedRomSha256", "expectedGameCode",
    "keys", "writes", "assertions", "watchdogs", "screenshot",
})


def build_scenario(document: Any) -> Scenario:
    obj = _require_object(document, "scenario")
    _reject_unknown(obj, _TOP_LEVEL_KEYS, "scenario")

    if "schemaVersion" not in obj:
        raise ScenarioError("scenario is missing 'schemaVersion'")
    schema_version = _as_int(obj["schemaVersion"], "schemaVersion")
    if schema_version != SCHEMA_VERSION:
        raise ScenarioError(f"unsupported schemaVersion {schema_version}; expected {SCHEMA_VERSION}")

    if "runFrames" not in obj:
        raise ScenarioError("scenario is missing 'runFrames'")
    run_frames = _bounded_int(obj["runFrames"], "runFrames", 1, MAX_RUN_FRAMES)

    name = _optional_str(obj, "name", "scenario")

    expected_rom = None
    if "expectedRomSha256" in obj:
        raw = obj["expectedRomSha256"]
        if not isinstance(raw, str):
            raise ScenarioError("expectedRomSha256 must be a string")
        expected_rom = _validate_hex_sha256(raw, "expectedRomSha256")

    expected_code = None
    if "expectedGameCode" in obj:
        raw = obj["expectedGameCode"]
        if not isinstance(raw, str):
            raise ScenarioError("expectedGameCode must be a string")
        expected_code = _validate_game_code(raw, "expectedGameCode")

    events = _parse_events(obj.get("keys", []), run_frames)
    writes = _parse_writes(obj.get("writes", []), run_frames)
    assertions = _parse_assertions(obj.get("assertions", []))
    watchdogs = _parse_watchdogs(obj.get("watchdogs", []), run_frames)
    screenshot = _parse_screenshot(obj["screenshot"]) if "screenshot" in obj else None

    if not assertions and not watchdogs and screenshot is None:
        raise ScenarioError("scenario must define at least one assertion, watchdog, or screenshot")

    return Scenario(
        schemaVersion=schema_version,
        runFrames=run_frames,
        name=name,
        expectedRomSha256=expected_rom,
        expectedGameCode=expected_code,
        events=events,
        writes=writes,
        assertions=assertions,
        watchdogs=watchdogs,
        screenshot=screenshot,
    )


def load_scenario(text: str) -> Scenario:
    """Parse and fully validate a scenario JSON string."""
    return build_scenario(parse_json(text))
