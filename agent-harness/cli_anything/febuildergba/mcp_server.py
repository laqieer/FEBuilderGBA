"""Dependency-free stdio MCP (Model Context Protocol) adapter for
cli-anything-febuildergba (issue #1942).

This module speaks newline-delimited JSON-RPC 2.0 directly on stdin/stdout —
it does NOT depend on any MCP SDK, and it does NOT shell out to Click. It
reuses the existing ``core`` wrappers (``core.project``, ``core.data``,
``core.text``, ``core.lint``, ``core.export``, ``core.verbs``) and the
``Session`` class directly, exactly like ``febuildergba_cli.py`` does.

Framing
-------
Every JSON-RPC message (a single object, or a JSON array batch) is exactly
one line of flushed, UTF-8 JSON on stdout. All logging/diagnostics go to
stderr. Nothing else is ever written to stdout.

Supported protocol versions: ``2025-03-26`` (latest/default) and
``2024-11-05``. The client's requested ``protocolVersion`` is honored if it
is one of these two; otherwise the server negotiates the latest.

Only 21 tools are exposed (see ``TOOL_DEFS``) — no generic command runner,
and no patch/rebuild/repair/event/music mutation tools. See
``docs/MCP-SERVER.md`` for the full reference.
"""

import json
import os
import sys

from cli_anything.febuildergba import __version__
from cli_anything.febuildergba.core.session import Session


# ── Protocol constants ─────────────────────────────────────────────────

SUPPORTED_PROTOCOL_VERSIONS = ("2025-03-26", "2024-11-05")
LATEST_PROTOCOL_VERSION = "2025-03-26"

SERVER_NAME = "febuildergba-cli"
SERVER_VERSION = __version__

PARSE_ERROR = -32700
INVALID_REQUEST = -32600
METHOD_NOT_FOUND = -32601
INVALID_PARAMS = -32602
INTERNAL_ERROR = -32603
RESOURCE_NOT_FOUND = -32002

MAX_STRING_LEN = 65536
TEXT_SEARCH_DEFAULT_LIMIT = 50
TEXT_SEARCH_MAX_LIMIT = 500
LINT_DEFAULT_LIMIT = 200
LINT_MAX_LIMIT = 1000
HISTORY_MIN = 1
HISTORY_MAX = 100
NAMES_MAX_IDS = 256

# Per-item/recursive string bound applied to individual result items (a
# single text-search match, a single lint line, or any string value inside a
# session tool/resource payload) — deliberately smaller than MAX_STRING_LEN,
# which bounds whole stdout/stderr/raw_output blobs.
MAX_ITEM_STRING_LEN = 4096

# Agent-controlled free-string input bounds (tool schemas). Enum-constrained
# strings (force_version, mode, kind, table's own enum-like values, etc.)
# don't need a maxLength; free-form paths/queries do, so a malicious/buggy
# caller can never grow unbounded session history/resource payloads through
# these inputs.
MAX_PATH_LEN = 4096
MAX_QUERY_LEN = 4096
MAX_TABLE_NAME_LEN = 128
MAX_ADDR_LEN = 64
MAX_REQUEST_LINE_CHARS = 1024 * 1024
MAX_BATCH_ITEMS = 64

# rom_checksum / data_roundtrip / text_roundtrip additionally treat backend
# exit code 2 as a structured, non-error advisory result (see the exit-code
# handling in each of their handlers below, and core/verbs.py, core/data.py,
# core/text.py docstrings).


class _ProtocolError(Exception):
    """A JSON-RPC protocol-level error (never used for tool business errors)."""

    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


# ── Minimal closed JSON-Schema validator ───────────────────────────────
#
# Deliberately small: it only implements the subset of JSON Schema our own
# tool schemas use (object/string/integer/number/boolean/array, required,
# additionalProperties:false, enum, min/maxLength, minimum/maximum,
# min/maxItems, items). No silent coercion — bool is never accepted as int.

def _validate(schema, value, path=""):
    t = schema.get("type")
    label = path or "value"

    if t == "object":
        if not isinstance(value, dict):
            return f"{label} must be an object"
        props = schema.get("properties", {})
        for req in schema.get("required", []):
            if req not in value:
                return f"missing required property '{req}'"
        if schema.get("additionalProperties", True) is False:
            for k in value:
                if k not in props:
                    return f"unexpected property '{k}'"
        for k, v in value.items():
            if k in props:
                err = _validate(props[k], v, f"{path}.{k}" if path else k)
                if err:
                    return err
        return None

    if t == "string":
        if not isinstance(value, str):
            return f"{label} must be a string"
        if "enum" in schema and value not in schema["enum"]:
            return f"{label} must be one of {schema['enum']}"
        if "minLength" in schema and len(value) < schema["minLength"]:
            return f"{label} must have length >= {schema['minLength']}"
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            return f"{label} must have length <= {schema['maxLength']}"
        return None

    if t == "integer":
        # bool is a subclass of int in Python — must not be silently accepted.
        if isinstance(value, bool) or not isinstance(value, int):
            return f"{label} must be an integer"
        if "minimum" in schema and value < schema["minimum"]:
            return f"{label} must be >= {schema['minimum']}"
        if "maximum" in schema and value > schema["maximum"]:
            return f"{label} must be <= {schema['maximum']}"
        return None

    if t == "number":
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            return f"{label} must be a number"
        return None

    if t == "boolean":
        if not isinstance(value, bool):
            return f"{label} must be a boolean"
        return None

    if t == "array":
        if not isinstance(value, list):
            return f"{label} must be an array"
        if "minItems" in schema and len(value) < schema["minItems"]:
            return f"{label} must have at least {schema['minItems']} items"
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            return f"{label} must have at most {schema['maxItems']} items"
        items_schema = schema.get("items")
        if items_schema:
            for idx, item in enumerate(value):
                err = _validate(items_schema, item, f"{label}[{idx}]")
                if err:
                    return err
        return None

    return None


def validate_schema(schema, value):
    """Validate ``value`` against a closed JSON Schema. Returns an error
    message string, or None if valid."""
    return _validate(schema, value, "")


# ── Tool schema building blocks ────────────────────────────────────────

_ROM_PATH_PROP = {
    "type": "string",
    "maxLength": MAX_PATH_LEN,
    "description": "Path to a .gba ROM file. If omitted, the active session's ROM is used.",
}
_FORCE_VERSION_PROP = {
    "type": "string",
    "enum": ["FE6", "FE7J", "FE7U", "FE8J", "FE8U"],
    "description": (
        "Force a specific ROM version instead of auto-detection. If omitted, "
        "the active session's force_version (if any) is used."
    ),
}


def _path_prop(description):
    return {"type": "string", "maxLength": MAX_PATH_LEN, "description": description}

_ANNOT_RO = {"readOnlyHint": True, "destructiveHint": False, "openWorldHint": False}
_ANNOT_DESTRUCTIVE = {"readOnlyHint": False, "destructiveHint": True, "openWorldHint": False}

_OVERWRITE_WARNING = (
    " WARNING: this tool writes to the filesystem and will overwrite the "
    "declared output path(s) (or prefix-expanded outputs) if they already exist."
)


def _tool(name, description, properties, required, annotations):
    return {
        "name": name,
        "description": description,
        "inputSchema": {
            "type": "object",
            "properties": properties,
            "required": required,
            "additionalProperties": False,
        },
        "annotations": annotations,
    }


TOOL_DEFS = [
    _tool(
        "backend_check",
        "Check whether the FEBuilderGBA.CLI backend executable is available.",
        {}, [], _ANNOT_RO,
    ),
    _tool(
        "session_open",
        "Open a ROM and start a persistent session (uses rom_info + Session.open_rom).",
        {
            "rom_path": _path_prop("Path to the .gba ROM file to open."),
            "force_version": _FORCE_VERSION_PROP,
        },
        ["rom_path"],
        _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "session_close",
        "Close the current session, if any (never errors when no session is open).",
        {}, [], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "session_status",
        "Show current session status (open ROM, version, modified flag, history count).",
        {}, [], _ANNOT_RO,
    ),
    _tool(
        "session_history",
        "Show bounded session operation history (most recent entries first-to-last).",
        {
            "count": {
                "type": "integer", "minimum": HISTORY_MIN, "maximum": HISTORY_MAX,
                "description": f"Number of most-recent entries to return ({HISTORY_MIN}..{HISTORY_MAX}, default 10).",
            },
        },
        [], _ANNOT_RO,
    ),
    _tool(
        "rom_info",
        "Show ROM information and metadata (size, detected version, lint summary).",
        {"rom_path": _ROM_PATH_PROP, "force_version": _FORCE_VERSION_PROP},
        [], _ANNOT_RO,
    ),
    _tool(
        "rom_validate",
        "Check whether a file looks like a valid GBA ROM (header heuristic, no backend call).",
        {"rom_path": _ROM_PATH_PROP},
        [], _ANNOT_RO,
    ),
    _tool(
        "rom_list_tables",
        "List all supported struct data table names for data_export/data_import.",
        {}, [], _ANNOT_RO,
    ),
    _tool(
        "rom_checksum",
        "Validate the GBA header checksum. An INVALID header is reported structurally, not an error.",
        {"rom_path": _ROM_PATH_PROP, "force_version": _FORCE_VERSION_PROP},
        [], _ANNOT_RO,
    ),
    _tool(
        "data_export",
        "Export a struct data table to TSV." + _OVERWRITE_WARNING,
        {
            "table": {"type": "string", "maxLength": MAX_TABLE_NAME_LEN,
                      "description": "Table name (see rom_list_tables), or 'all'."},
            "out_path": _path_prop("Output TSV file path (or prefix for 'all')."),
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["table", "out_path"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "data_import",
        "Import struct data from a TSV file into the ROM, in place." + _OVERWRITE_WARNING,
        {
            "table": {"type": "string", "maxLength": MAX_TABLE_NAME_LEN,
                      "description": "Table name (see rom_list_tables)."},
            "in_path": _path_prop("Input TSV file path."),
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["table", "in_path"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "data_roundtrip",
        "Validate struct data round-trip (export/import/compare). Exit 2 is a structured, "
        "non-error 'mismatches found' result, not a tool error.",
        {
            "table": {"type": "string", "maxLength": MAX_TABLE_NAME_LEN,
                      "description": "Table name, or 'all' (default)."},
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        [], _ANNOT_RO,
    ),
    _tool(
        "names_resolve",
        "Resolve entity IDs (unit/class/item/song) to human-readable names.",
        {
            "kind": {"type": "string", "enum": ["unit", "class", "item", "song"],
                     "description": "Entity type."},
            "ids": {
                "type": "array", "items": {"type": "integer"},
                "minItems": 1, "maxItems": NAMES_MAX_IDS,
                "description": f"Entity IDs to resolve (1..{NAMES_MAX_IDS}).",
            },
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["kind", "ids"], _ANNOT_RO,
    ),
    _tool(
        "text_search",
        "Search ROM text by substring (case-insensitive). Bounded/paginated result.",
        {
            "query": {"type": "string", "minLength": 1, "maxLength": MAX_QUERY_LEN,
                      "description": "Substring to search for."},
            "limit": {
                "type": "integer", "minimum": 1, "maximum": TEXT_SEARCH_MAX_LIMIT,
                "description": f"Max matches to return (default {TEXT_SEARCH_DEFAULT_LIMIT}, max {TEXT_SEARCH_MAX_LIMIT}).",
            },
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["query"], _ANNOT_RO,
    ),
    _tool(
        "text_roundtrip",
        "Validate text export/import round-trip (lossless check). Exit 2 is a structured, "
        "non-error 'mismatches found' result. No diagnostic files are written by this tool.",
        {"rom_path": _ROM_PATH_PROP, "force_version": _FORCE_VERSION_PROP},
        [], _ANNOT_RO,
    ),
    _tool(
        "rom_lint",
        "Run integrity checks on the ROM. Bounded errors/warnings/info arrays.",
        {
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
            "limit": {
                "type": "integer", "minimum": 1, "maximum": LINT_MAX_LIMIT,
                "description": f"Max entries per array (default {LINT_DEFAULT_LIMIT}, max {LINT_MAX_LIMIT}).",
            },
        },
        [], _ANNOT_RO,
    ),
    _tool(
        "image_quantize",
        "Quantize an image's palette to 16 colors for GBA (no ROM required)." + _OVERWRITE_WARNING,
        {
            "in_path": _path_prop("Input image file."),
            "out_path": _path_prop("Output image file."),
            "palette_no": {"type": "integer", "minimum": 0, "maximum": 15,
                           "description": "Palette number (default 0)."},
            "no_scale": {"type": "boolean", "description": "Don't scale to GBA 5-bit color."},
            "no_reserve_1st": {"type": "boolean", "description": "Don't reserve palette slot 0."},
            "ignore_tsa": {"type": "boolean", "description": "Ignore TSA 8x8 tile constraints."},
        },
        ["in_path", "out_path"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "image_convert_map",
        "Convert an image to GBA map tiles + TSA data (no ROM required)." + _OVERWRITE_WARNING,
        {
            "in_path": _path_prop("Input image file."),
            "out_img": _path_prop("Output tile image file."),
            "out_tsa": _path_prop("Output TSA data file."),
        },
        ["in_path", "out_img", "out_tsa"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "palette_export",
        "Export a GBA palette to a file (.pal/.act/.gpl/.txt/.gbapal)." + _OVERWRITE_WARNING,
        {
            "addr": {"type": "string", "maxLength": MAX_ADDR_LEN,
                     "description": "Palette address in hex (e.g. 0x5524)."},
            "out_path": _path_prop("Output file path."),
            "colors": {"type": "integer", "minimum": 1, "maximum": 256,
                       "description": "Color count (backend default 16 when omitted)."},
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["addr", "out_path"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "palette_import",
        "Import a palette file into the ROM in place (format auto-detected)." + _OVERWRITE_WARNING,
        {
            "addr": {"type": "string", "maxLength": MAX_ADDR_LEN,
                     "description": "Palette address in hex (e.g. 0x5524)."},
            "in_path": _path_prop("Input palette file."),
            "rom_path": _ROM_PATH_PROP,
            "force_version": _FORCE_VERSION_PROP,
        },
        ["addr", "in_path"], _ANNOT_DESTRUCTIVE,
    ),
    _tool(
        "lz77",
        "LZ77 compress or decompress an arbitrary file (no ROM required)." + _OVERWRITE_WARNING,
        {
            "mode": {"type": "string", "enum": ["compress", "decompress"],
                     "description": "Operation to perform."},
            "in_path": _path_prop("Input file."),
            "out_path": _path_prop("Output file."),
        },
        ["mode", "in_path", "out_path"], _ANNOT_DESTRUCTIVE,
    ),
]

TOOL_SCHEMAS = {t["name"]: t["inputSchema"] for t in TOOL_DEFS}


# ── Common ROM/force-version resolution + helpers ──────────────────────

class _ToolInputError(Exception):
    """Raised by handlers for business-level (non-protocol) tool failures."""


def _resolve_rom(session, args, need_force=True):
    rom_path = args.get("rom_path") or ""
    if not rom_path and session.is_open():
        rom_path = session.state.rom_path
    if not rom_path:
        raise _ToolInputError(
            "No ROM specified: provide 'rom_path' or open a session first (session_open)."
        )
    force_version = ""
    if need_force:
        force_version = args.get("force_version") or ""
        if not force_version and session.is_open():
            force_version = session.state.force_version
    return rom_path, force_version


def _same_as_session_rom(session, rom_path):
    if not session.is_open():
        return False
    try:
        return (os.path.normcase(os.path.abspath(rom_path))
                == os.path.normcase(os.path.abspath(session.state.rom_path)))
    except OSError:
        return False


def _bound_string_fields(d):
    """Bound stdout/stderr/raw_output to MAX_STRING_LEN with truncation metadata."""
    if not isinstance(d, dict):
        return d
    for key in ("stdout", "stderr", "raw_output"):
        if key in d and isinstance(d[key], str):
            s = d[key]
            if len(s) > MAX_STRING_LEN:
                d[key] = s[:MAX_STRING_LEN]
                d[f"{key}_truncated"] = True
                d[f"{key}_original_length"] = len(s)
            else:
                d[f"{key}_truncated"] = False
    return d


def _bound_strings_recursive(value, max_len=MAX_ITEM_STRING_LEN):
    """Recursively truncate every string value (inside dicts/lists) to
    ``max_len`` characters. Used to bound session tool/resource payloads
    (session/history/rom_header) so a pathological persisted entry or header
    field can never balloon a response — and, unlike raw byte data, every
    value here is already plain JSON-safe text (never raw ROM bytes).

    Returns ``(bounded_value, truncated)`` where ``truncated`` is True if
    anything anywhere in ``value`` was shortened.
    """
    if isinstance(value, str):
        if len(value) > max_len:
            return value[:max_len], True
        return value, False
    if isinstance(value, dict):
        out = {}
        truncated = False
        for k, v in value.items():
            bv, t = _bound_strings_recursive(v, max_len)
            out[k] = bv
            truncated = truncated or t
        return out, truncated
    if isinstance(value, list):
        out = []
        truncated = False
        for item in value:
            bv, t = _bound_strings_recursive(item, max_len)
            out.append(bv)
            truncated = truncated or t
        return out, truncated
    return value, False


def _bounded_payload(payload):
    """Bound every string in a JSON object and expose aggregate metadata."""
    bounded, truncated = _bound_strings_recursive(payload)
    bounded["truncated"] = truncated
    return bounded


# ── Tool handlers: (session, args) -> (payload_dict, is_error) ─────────

def _h_backend_check(session, args):
    from cli_anything.febuildergba.utils.febuildergba_backend import check_backend
    return check_backend(), False


def _h_session_open(session, args):
    from cli_anything.febuildergba.core.project import rom_info
    rom_path = args["rom_path"]
    force_version = args.get("force_version") or ""
    info = rom_info(rom_path, force_version)
    session.open_rom(rom_path, info["detected_version"], info["rom_size"], force_version)
    return {"status": "opened", **info}, False


def _h_session_close(session, args):
    if not session.is_open():
        return {"status": "no_session"}, False
    session.close()
    return {"status": "closed"}, False


def _h_session_status(session, args):
    return _bounded_payload({"open": session.is_open(), **session.info()}), False


def _h_session_history(session, args):
    count = args.get("count", 10)
    if not session.is_open():
        payload = {"open": False, "history": [], "count": 0}
        return _bounded_payload(payload), False
    entries = session.state.history[-count:]
    payload = {"open": True, "history": entries, "count": len(entries)}
    return _bounded_payload(payload), False


def _h_rom_info(session, args):
    from cli_anything.febuildergba.core.project import rom_info
    rom_path, force_version = _resolve_rom(session, args)
    return rom_info(rom_path, force_version), False


def _h_rom_validate(session, args):
    from cli_anything.febuildergba.core.project import validate_rom
    rom_path, _fv = _resolve_rom(session, args, need_force=False)
    return {"rom_path": rom_path, "valid": validate_rom(rom_path)}, False


def _h_rom_list_tables(session, args):
    from cli_anything.febuildergba.core.project import list_tables
    tables = list_tables()
    return {"tables": tables, "count": len(tables)}, False


def _h_rom_checksum(session, args):
    from cli_anything.febuildergba.core.verbs import checksum
    rom_path, force_version = _resolve_rom(session, args)
    result = checksum(rom_path, force_version)
    return result, result["exit_code"] not in (0, 2)


def _h_data_export(session, args):
    from cli_anything.febuildergba.core.data import export_table
    rom_path, force_version = _resolve_rom(session, args)
    table = args["table"]
    out_path = args["out_path"]
    result = export_table(rom_path, table, out_path, force_version)
    is_error = result["exit_code"] != 0
    if not is_error and _same_as_session_rom(session, rom_path):
        session.record_operation("data_export", {"table": table, "out": out_path})
    return result, is_error


def _h_data_import(session, args):
    from cli_anything.febuildergba.core.data import import_table
    rom_path, force_version = _resolve_rom(session, args)
    table = args["table"]
    in_path = args["in_path"]
    result = import_table(rom_path, table, in_path, force_version)
    is_error = result["exit_code"] != 0
    if not is_error and _same_as_session_rom(session, rom_path):
        session.record_operation("data_import", {"table": table, "in": in_path})
        session.mark_modified()
    return result, is_error


def _h_data_roundtrip(session, args):
    from cli_anything.febuildergba.core.data import roundtrip_table
    rom_path, force_version = _resolve_rom(session, args)
    table = args.get("table", "all")
    result = roundtrip_table(rom_path, table, force_version)
    return result, result["exit_code"] not in (0, 2)


def _h_names_resolve(session, args):
    from cli_anything.febuildergba.core.export import resolve_names
    rom_path, force_version = _resolve_rom(session, args)
    result = resolve_names(rom_path, args["kind"], args["ids"], force_version)
    return result, result["exit_code"] != 0


def _h_text_search(session, args):
    from cli_anything.febuildergba.core.text import search_text
    rom_path, force_version = _resolve_rom(session, args)
    limit = args.get("limit", TEXT_SEARCH_DEFAULT_LIMIT)
    result = search_text(rom_path, args["query"], force_version)
    is_error = result.get("exit_code", 0) != 0
    matches = result.get("matches") or []
    total = len(matches)
    bounded = matches[:limit]
    # Bound each match's own text to MAX_ITEM_STRING_LEN, in addition to the
    # count-based pagination above; track both per-item and aggregate
    # truncation metadata.
    items_truncated = 0
    for m in bounded:
        if isinstance(m, dict) and isinstance(m.get("text"), str):
            if len(m["text"]) > MAX_ITEM_STRING_LEN:
                m["text"] = m["text"][:MAX_ITEM_STRING_LEN]
                m["text_truncated"] = True
                items_truncated += 1
            else:
                m["text_truncated"] = False
    result["matches"] = bounded
    result["total"] = total
    result["returned"] = len(bounded)
    result["truncated"] = total > limit
    result["matches_text_truncated_count"] = items_truncated
    return result, is_error


def _h_text_roundtrip(session, args):
    from cli_anything.febuildergba.core.text import roundtrip_text
    rom_path, force_version = _resolve_rom(session, args)
    # No out_prefix param exposed: this tool must stay read-only (no diagnostic
    # files written to disk).
    result = roundtrip_text(rom_path, "", force_version)
    # Exit 2 = advisory "mismatches found", not a tool error (parallel to
    # rom_checksum / data_roundtrip).
    return result, result["exit_code"] not in (0, 2)


def _h_rom_lint(session, args):
    from cli_anything.febuildergba.core.lint import lint_rom
    rom_path, force_version = _resolve_rom(session, args)
    limit = args.get("limit", LINT_DEFAULT_LIMIT)
    result = lint_rom(rom_path, force_version)
    is_error = result["exit_code"] != 0
    for key in ("errors", "warnings", "info"):
        arr = result.get(key) or []
        total = len(arr)
        bounded = arr[:limit]
        items_truncated = 0
        new_arr = []
        for item in bounded:
            if isinstance(item, str) and len(item) > MAX_ITEM_STRING_LEN:
                new_arr.append(item[:MAX_ITEM_STRING_LEN])
                items_truncated += 1
            else:
                new_arr.append(item)
        result[key] = new_arr
        result[f"{key}_total"] = total
        result[f"{key}_truncated"] = total > limit
        result[f"{key}_items_truncated_count"] = items_truncated
    return result, is_error


def _h_image_quantize(session, args):
    from cli_anything.febuildergba.core.export import decrease_color
    result = decrease_color(
        args["in_path"], args["out_path"],
        args.get("palette_no", 0), args.get("no_scale", False),
        args.get("no_reserve_1st", False), args.get("ignore_tsa", False),
    )
    return result, result["exit_code"] != 0


def _h_image_convert_map(session, args):
    from cli_anything.febuildergba.core.export import convert_map_image
    result = convert_map_image(args["in_path"], args["out_img"], args["out_tsa"])
    return result, result["exit_code"] != 0


def _h_palette_export(session, args):
    from cli_anything.febuildergba.core.verbs import export_palette
    rom_path, force_version = _resolve_rom(session, args)
    result = export_palette(rom_path, args["addr"], args["out_path"],
                             args.get("colors"), force_version)
    return result, result["exit_code"] != 0


def _h_palette_import(session, args):
    from cli_anything.febuildergba.core.verbs import import_palette
    rom_path, force_version = _resolve_rom(session, args)
    result = import_palette(rom_path, args["addr"], args["in_path"], force_version)
    is_error = result["exit_code"] != 0
    if not is_error and _same_as_session_rom(session, rom_path):
        session.record_operation("palette_import", {"addr": args["addr"]})
        session.mark_modified()
    return result, is_error


def _h_lz77(session, args):
    from cli_anything.febuildergba.core.verbs import lz77_file
    result = lz77_file(args["mode"], args["in_path"], args["out_path"])
    return result, result["exit_code"] != 0


TOOL_HANDLERS = {
    "backend_check": _h_backend_check,
    "session_open": _h_session_open,
    "session_close": _h_session_close,
    "session_status": _h_session_status,
    "session_history": _h_session_history,
    "rom_info": _h_rom_info,
    "rom_validate": _h_rom_validate,
    "rom_list_tables": _h_rom_list_tables,
    "rom_checksum": _h_rom_checksum,
    "data_export": _h_data_export,
    "data_import": _h_data_import,
    "data_roundtrip": _h_data_roundtrip,
    "names_resolve": _h_names_resolve,
    "text_search": _h_text_search,
    "text_roundtrip": _h_text_roundtrip,
    "rom_lint": _h_rom_lint,
    "image_quantize": _h_image_quantize,
    "image_convert_map": _h_image_convert_map,
    "palette_export": _h_palette_export,
    "palette_import": _h_palette_import,
    "lz77": _h_lz77,
}

assert set(TOOL_HANDLERS) == set(TOOL_SCHEMAS)


# ── Resources ───────────────────────────────────────────────────────────

def _read_session_resource(session):
    if not session.is_open():
        payload = {"open": False}
    else:
        payload = {"open": True, **session.info()}
    return _bounded_payload(payload)


def _read_session_history_resource(session):
    if not session.is_open():
        payload = {"open": False, "history": []}
    else:
        # Session already caps history at 100 entries on write (see core/session.py).
        payload = {"open": True, "history": session.state.history}
    return _bounded_payload(payload)


def _read_rom_metadata_resource(session):
    if not session.is_open():
        return _bounded_payload({"open": False})
    from cli_anything.febuildergba.core.project import rom_header
    payload = {"open": True, **session.info()}
    try:
        # rom_header returns only parsed metadata fields (title, game_code,
        # etc.) — never raw ROM bytes or arbitrary file contents.
        payload["rom_header"] = rom_header(session.state.rom_path)
    except (FileNotFoundError, ValueError, OSError) as e:
        payload["rom_header"] = None
        payload["rom_header_error"] = str(e)
    return _bounded_payload(payload)


RESOURCE_DEFS = [
    {
        "uri": "febuildergba://session",
        "name": "session",
        "description": "Current session state (open ROM, version, modified flag, history count).",
        "mimeType": "application/json",
    },
    {
        "uri": "febuildergba://session/history",
        "name": "session-history",
        "description": "Bounded session operation history (up to 100 most recent entries).",
        "mimeType": "application/json",
    },
    {
        "uri": "febuildergba://rom/metadata",
        "name": "rom-metadata",
        "description": "Session state plus parsed GBA ROM header metadata. Never raw ROM bytes.",
        "mimeType": "application/json",
    },
]

RESOURCE_READERS = {
    "febuildergba://session": _read_session_resource,
    "febuildergba://session/history": _read_session_history_resource,
    "febuildergba://rom/metadata": _read_rom_metadata_resource,
}


# ── Server state + JSON-RPC method handlers ─────────────────────────────

class _ServerState:
    def __init__(self, session):
        self.session = session
        self.initialized = False
        self.protocol_version = None


def _check_allowed_params(params, allowed):
    """Reject any params field not in ``allowed`` (forward-compatible ``_meta``
    should always be included in the caller's allowed set). Raises
    ``_ProtocolError(INVALID_PARAMS, ...)`` on violation."""
    if not isinstance(params, dict):
        raise _ProtocolError(INVALID_PARAMS, "params must be an object")
    unknown = sorted(k for k in params if k not in allowed)
    if unknown:
        raise _ProtocolError(INVALID_PARAMS,
                              f"Unexpected params field(s): {', '.join(unknown)}")


def _h_initialize(state, params):
    if state.initialized:
        raise _ProtocolError(INVALID_REQUEST,
                              "Server already initialized; duplicate 'initialize' is not allowed")
    if not isinstance(params, dict):
        raise _ProtocolError(INVALID_PARAMS, "initialize requires an object 'params'")

    requested = params.get("protocolVersion")
    if not isinstance(requested, str) or not requested:
        raise _ProtocolError(INVALID_PARAMS,
                              "initialize requires a non-empty string 'protocolVersion'")

    capabilities = params.get("capabilities")
    if not isinstance(capabilities, dict):
        raise _ProtocolError(INVALID_PARAMS, "initialize requires an object 'capabilities'")

    client_info = params.get("clientInfo")
    if not isinstance(client_info, dict):
        raise _ProtocolError(INVALID_PARAMS, "initialize requires an object 'clientInfo'")
    client_name = client_info.get("name")
    client_version = client_info.get("version")
    if not isinstance(client_name, str) or not client_name:
        raise _ProtocolError(INVALID_PARAMS,
                              "initialize requires a non-empty string 'clientInfo.name'")
    if not isinstance(client_version, str) or not client_version:
        raise _ProtocolError(INVALID_PARAMS,
                              "initialize requires a non-empty string 'clientInfo.version'")

    # A well-formed but unrecognized protocolVersion still negotiates the
    # latest supported version rather than failing.
    negotiated = requested if requested in SUPPORTED_PROTOCOL_VERSIONS else LATEST_PROTOCOL_VERSION
    state.protocol_version = negotiated
    state.initialized = True
    return {
        "protocolVersion": negotiated,
        "capabilities": {
            "tools": {"listChanged": False},
            "resources": {"subscribe": False, "listChanged": False},
        },
        "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
    }


def _h_ping(state, params):
    _check_allowed_params(params, {"_meta"})
    return {}


def _h_tools_list(state, params):
    _check_allowed_params(params, {"cursor", "_meta"})
    if "cursor" in params and not isinstance(params["cursor"], str):
        raise _ProtocolError(INVALID_PARAMS, "'cursor' must be a string")
    return {"tools": TOOL_DEFS}


def _h_tools_call(state, params):
    _check_allowed_params(params, {"name", "arguments", "_meta"})
    name = params.get("name")
    if not isinstance(name, str) or not name:
        raise _ProtocolError(INVALID_PARAMS, "tools/call requires a string 'name'")
    arguments = params.get("arguments", {})
    if arguments is None:
        arguments = {}
    if not isinstance(arguments, dict):
        raise _ProtocolError(INVALID_PARAMS, "'arguments' must be an object")
    if name not in TOOL_SCHEMAS:
        raise _ProtocolError(INVALID_PARAMS, f"Unknown tool: {name}")

    err = validate_schema(TOOL_SCHEMAS[name], arguments)
    if err:
        raise _ProtocolError(INVALID_PARAMS, f"Invalid arguments for '{name}': {err}")

    handler = TOOL_HANDLERS[name]
    try:
        payload, is_error = handler(state.session, arguments)
    except Exception as e:  # tool business/backend failure -> isError result, never a protocol error
        payload, is_error = {"error": str(e)}, True

    if isinstance(payload, dict):
        _bound_string_fields(payload)
    text = json.dumps(payload, indent=2, default=str)
    return {"content": [{"type": "text", "text": text}], "isError": is_error}


def _h_resources_list(state, params):
    _check_allowed_params(params, {"cursor", "_meta"})
    if "cursor" in params and not isinstance(params["cursor"], str):
        raise _ProtocolError(INVALID_PARAMS, "'cursor' must be a string")
    return {"resources": RESOURCE_DEFS}


def _h_resources_read(state, params):
    _check_allowed_params(params, {"uri", "_meta"})
    uri = params.get("uri")
    if not isinstance(uri, str) or not uri:
        raise _ProtocolError(INVALID_PARAMS, "resources/read requires a string 'uri'")
    if uri not in RESOURCE_READERS:
        raise _ProtocolError(RESOURCE_NOT_FOUND, f"Unknown resource: {uri}")
    payload = RESOURCE_READERS[uri](state.session)
    text = json.dumps(payload, indent=2, default=str)
    return {"contents": [{"uri": uri, "mimeType": "application/json", "text": text}]}


def _h_notifications_initialized(state, params):
    return None


def _h_notifications_cancelled(state, params):
    return None


_DISPATCH = {
    "initialize": _h_initialize,
    "ping": _h_ping,
    "tools/list": _h_tools_list,
    "tools/call": _h_tools_call,
    "resources/list": _h_resources_list,
    "resources/read": _h_resources_read,
    "notifications/initialized": _h_notifications_initialized,
    "notifications/cancelled": _h_notifications_cancelled,
}

# Methods allowed before initialize (ping is explicitly excepted from the
# "initialize first" lifecycle rule).
_PRE_INIT_METHODS = ("initialize", "ping")


def _err(id_value, code, message):
    return {"jsonrpc": "2.0", "id": id_value, "error": {"code": code, "message": message}}


def _ok(id_value, result):
    return {"jsonrpc": "2.0", "id": id_value, "result": result}


def _process_message(state, msg):
    """Process one already shape-validated JSON-RPC message. Returns a
    response dict, or None if the message is a notification."""
    is_notification = "id" not in msg
    msg_id = msg.get("id")
    method = msg.get("method")

    if not isinstance(method, str) or method == "":
        # Missing/empty/non-string 'method' is a malformed Request, not an
        # unrecognized-but-well-formed method name. Without a valid method,
        # the object is not a valid Notification and still requires an
        # Invalid Request response with id null.
        return _err(msg_id, INVALID_REQUEST,
                    "Invalid Request: 'method' must be a non-empty string")

    if method not in _PRE_INIT_METHODS and not state.initialized:
        if is_notification:
            return None
        return _err(msg_id, INVALID_REQUEST,
                    "Server not initialized; call 'initialize' first")

    handler = _DISPATCH.get(method)
    if handler is None:
        return None if is_notification else _err(msg_id, METHOD_NOT_FOUND,
                                                   f"Method not found: {method}")

    params = msg.get("params", {})
    if params is None:
        params = {}

    try:
        result = handler(state, params)
    except _ProtocolError as e:
        return None if is_notification else _err(msg_id, e.code, e.message)
    except Exception as e:  # unexpected bug in our own dispatch code
        # Log the real exception for operators, but never leak internals to
        # the client — the protocol error message is always generic.
        print(f"[febuildergba-mcp] internal error handling {method}: {e}",
              file=sys.stderr, flush=True)
        return None if is_notification else _err(msg_id, INTERNAL_ERROR, "Internal error")

    if is_notification:
        return None
    return _ok(msg_id, result)


def _validate_shape(item):
    """Structural JSON-RPC validation shared by requests and notifications.
    Returns an error-response dict, or None if the shape is fine."""
    if not isinstance(item, dict):
        return _err(None, INVALID_REQUEST, "Invalid Request: message must be an object")
    id_value = item.get("id")
    id_valid = ("id" not in item
                or (not isinstance(id_value, bool)
                    and isinstance(id_value, (str, int))))
    response_id = id_value if "id" in item and id_valid else None
    if item.get("jsonrpc") != "2.0":
        return _err(response_id, INVALID_REQUEST,
                    "Invalid Request: jsonrpc must be '2.0'")
    if not id_valid:
        return _err(None, INVALID_REQUEST,
                    "Invalid Request: id must be a non-null string or integer")
    if "params" in item and item["params"] is not None and not isinstance(item["params"], dict):
        return _err(item.get("id"), INVALID_REQUEST, "Invalid Request: params must be an object")
    return None


def _handle_single(state, item, in_batch):
    shape_err = _validate_shape(item)
    if shape_err:
        return shape_err

    method = item.get("method")
    if in_batch and method == "initialize":
        # "initialize cannot be batched" — reject only this entry.
        if "id" in item:
            return _err(item["id"], INVALID_REQUEST,
                        "'initialize' must not be sent as part of a batch")
        return None

    return _process_message(state, item)


def handle_line(state, line):
    """Parse and process one line of input. Returns a JSON-serializable
    response (dict or list), or None if nothing should be written."""
    if len(line) > MAX_REQUEST_LINE_CHARS:
        return _err(None, INVALID_REQUEST,
                    f"Invalid Request: input exceeds {MAX_REQUEST_LINE_CHARS} characters")
    try:
        payload = json.loads(line)
    except (json.JSONDecodeError, ValueError):
        return _err(None, PARSE_ERROR, "Parse error: invalid JSON")

    if isinstance(payload, list):
        if len(payload) == 0:
            return _err(None, INVALID_REQUEST, "Invalid Request: empty batch")
        if len(payload) > MAX_BATCH_ITEMS:
            return _err(None, INVALID_REQUEST,
                        f"Invalid Request: batch exceeds {MAX_BATCH_ITEMS} entries")
        responses = []
        for item in payload:
            resp = _handle_single(state, item, in_batch=True)
            if resp is not None:
                responses.append(resp)
        return responses if responses else None  # never emit an empty array

    return _handle_single(state, payload, in_batch=False)


# ── Entry point ──────────────────────────────────────────────────────────

def serve(session_file=None, in_stream=None, out_stream=None):
    """Run the stdio JSON-RPC loop until stdin is closed."""
    in_stream = in_stream or sys.stdin
    out_stream = out_stream or sys.stdout

    session = Session(session_file) if session_file else Session()
    state = _ServerState(session)

    while True:
        raw_line = in_stream.readline(MAX_REQUEST_LINE_CHARS + 2)
        if raw_line == "":
            break

        content_length = len(raw_line.rstrip("\r\n"))
        if content_length > MAX_REQUEST_LINE_CHARS:
            while raw_line and not raw_line.endswith("\n"):
                raw_line = in_stream.readline(MAX_REQUEST_LINE_CHARS + 2)
            response = _err(
                None, INVALID_REQUEST,
                f"Invalid Request: input exceeds {MAX_REQUEST_LINE_CHARS} characters",
            )
            out_stream.write(json.dumps(response) + "\n")
            out_stream.flush()
            continue

        line = raw_line.strip()
        if not line:
            continue
        try:
            response = handle_line(state, line)
        except Exception as e:  # never let an unexpected bug kill the loop
            # Log the real exception for operators, but the client only ever
            # sees a generic Internal error response (id null, since we may
            # not even have parsed far enough to know a request id) — and,
            # critically, we must still emit *something* rather than
            # silently dropping the line so the client isn't left hanging.
            print(f"[febuildergba-mcp] unhandled error: {e}", file=sys.stderr, flush=True)
            out_stream.write(json.dumps(_err(None, INTERNAL_ERROR, "Internal error")) + "\n")
            out_stream.flush()
            continue
        if response is not None:
            out_stream.write(json.dumps(response) + "\n")
            out_stream.flush()


def _parse_argv(argv):
    session_file = None
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg == "--session-file" and i + 1 < len(argv):
            session_file = argv[i + 1]
            i += 2
        elif arg.startswith("--session-file="):
            session_file = arg.split("=", 1)[1]
            i += 1
        else:
            i += 1
    return session_file


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    session_file = _parse_argv(argv)
    serve(session_file)


if __name__ == "__main__":
    main()
