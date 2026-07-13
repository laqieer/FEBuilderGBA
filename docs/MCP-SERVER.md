# FEBuilderGBA MCP stdio server

Tracks issue [#1942](https://github.com/laqieer/FEBuilderGBA/issues/1942).

A **dependency-free** Model Context Protocol (MCP) stdio adapter for the CLI harness
(`cli-anything-febuildergba`, see [`agent-harness/FEBUILDER.md`](../agent-harness/FEBUILDER.md) /
[`agent-harness/cli_anything/febuildergba/README.md`](../agent-harness/cli_anything/febuildergba/README.md)),
implemented in one stdlib-only module:
[`agent-harness/cli_anything/febuildergba/mcp_server.py`](../agent-harness/cli_anything/febuildergba/mcp_server.py).

It reuses the **same** `Session`, backend resolver, and `core/*` wrappers as the Click CLI directly
— it never shells out to Click, and it never imports an MCP SDK. It speaks newline-delimited
JSON-RPC 2.0 on stdin/stdout only.

## Setup

No extra runtime dependencies are required beyond the CLI harness itself (Python 3.10+; the
`FEBuilderGBA.CLI` .NET backend is only needed when a tool that actually touches a ROM/file is
called — protocol-only calls like `initialize`/`ping`/`tools/list` need nothing but the stdlib).

```bash
# Optional: install the harness so the console script is on PATH
cd agent-harness
pip install -e .

# Run directly (installed console script)
cli-anything-febuildergba-mcp [--session-file PATH]

# Or run without installing anything (bootstraps sys.path itself)
python agent-harness/febuildergba_mcp.py [--session-file PATH]
```

`--session-file PATH` pins the server to a specific session JSON file (same format/location
conventions as the Click CLI's `--session-file`); if omitted, the default
`~/.cli-anything-febuildergba/sessions/default.json` is used.

## `.mcp.json` registration

The repo's [`.mcp.json`](../.mcp.json) registers this server as `febuildergba-cli`, alongside the
pre-existing Windows-only `febuildergba-computer-use` entry (both are preserved side by side):

```json
{
  "mcpServers": {
    "febuildergba-computer-use": {
      "type": "stdio",
      "command": "./tools/mcp-computer-use/.venv/Scripts/python.exe",
      "args": ["./tools/mcp-computer-use/server.py"]
    },
    "febuildergba-cli": {
      "type": "stdio",
      "command": "python",
      "args": ["./agent-harness/febuildergba_mcp.py"]
    }
  }
}
```

`command: "python"` (rather than a Windows-only `.exe` path) keeps this entry cross-platform;
the launcher itself bootstraps `sys.path` so `cli_anything.febuildergba` resolves whether or not
the package has been `pip install`-ed.

## Protocol versions, framing, errors, batching

- **Protocol versions:** `2025-03-26` (the newer/default revision implemented by this server, used
  when the client's requested version is a well-formed but unrecognized string) and `2024-11-05`
  are both honored verbatim when requested in `initialize`. Newer MCP revisions are intentionally
  not advertised until their contracts are implemented and tested here.
- **Framing:** every JSON-RPC message — a single object, or a JSON array batch — is exactly one
  flushed, UTF-8 line on stdout. The real stdin/stdout/stderr text streams are explicitly
  reconfigured to UTF-8 before the loop, overriding locale-dependent legacy pipe encodings. All
  logs/diagnostics go to stderr; nothing else is ever written to stdout. Input lines are capped at
  1,048,576 characters and batches at 64 entries; an oversized line is drained before the next
  message is processed. Non-standard `NaN`/`Infinity` JSON tokens are rejected as parse errors.
  Even an unexpected failure escaping the server's dispatch loop emits a single flushed, generic
  `-32603` response (`id: null`) rather than silently dying or dropping the line — the loop always
  keeps processing subsequent lines.
- **Lifecycle:** `initialize` must be called before any other operation, **except `ping`**, which
  is always allowed. Calling anything else first gets `-32600 Invalid Request`. `initialize` itself
  must **not** be sent as part of a JSON-RPC batch (batching it returns `-32600` for that entry),
  and calling `initialize` a second time after a successful one is also `-32600` (duplicate
  initialization).
- **`initialize` conformance:** `params` must be an object containing a non-empty string
  `protocolVersion`, an object `capabilities`, and an object `clientInfo` with non-empty string
  `name` and `version` fields. Missing/malformed fields are `-32602 Invalid params`. A well-formed
  but *unrecognized* `protocolVersion` still negotiates the latest supported version rather than
  failing — only a missing/non-string/empty `protocolVersion` is rejected.
- **Method-specific params:** the operational request methods below validate their own `params`
  shape and reject unexpected fields as `-32602` (forward-compatible `_meta` is accepted):
  - `ping`: no fields besides optional `_meta`.
  - `tools/list` / `resources/list`: only an optional string `cursor` plus optional `_meta`.
  - `tools/call`: only `name` (required string), optional `arguments` (object), optional `_meta`.
  - `resources/read`: only `uri` (required string), optional `_meta`.
- **Batching:** one JSON-RPC message or a JSON array of messages per input line. Requests and
  notifications may be freely mixed in a batch; each is processed sequentially, in order.
  Notifications never produce a response entry, so an **all-notification batch** produces no
  output at all. An **empty array** (`[]`), however, is itself a malformed batch per the JSON-RPC
  2.0 spec and produces a single `-32600 Invalid Request` response object (not an empty array —
  an empty array is never printed). Batches over 64 entries are rejected as one `-32600` response.
  IDs may be arbitrary non-null strings or integers (booleans are rejected, matching the "bool is
  not an int" rule used throughout).

### Error codes

| Code | Meaning | When |
|---|---|---|
| `-32700` | Parse error | The line isn't valid JSON, including non-standard `NaN`/`Infinity` tokens. |
| `-32600` | Invalid Request | Wrong `jsonrpc` version, malformed request/notification shape, missing/empty/non-string `method`, non-object `params`, invalid `id` (null/bool/object), an empty or over-64-entry batch, an over-1,048,576-character line, `initialize` sent inside a batch, duplicate `initialize` after a successful one, or an operation attempted before `initialize` (except `ping`). |
| `-32601` | Method not found | `method` is a well-formed, non-empty string but doesn't match any registered method. |
| `-32602` | Invalid params | Malformed method-specific params shape (see above), unexpected/extra params fields, malformed `initialize` fields, an **unknown tool name**, or **schema-invalid tool arguments** (missing required field, wrong type, out-of-bounds, unexpected/extra property, over-length string — schemas are closed with `additionalProperties: false`). |
| `-32603` | Internal error | An unexpected bug in the server's own dispatch code (never used for a tool's own backend/business failure). The client only ever sees the generic message `"Internal error"` — the real exception is logged to stderr, never echoed back. |
| `-32002` | Resource not found | `resources/read` with an unknown `uri`. |

**Important distinction:** a tool *accepted* by validation (known name, schema-valid arguments)
that then fails for a business/backend reason (e.g. missing ROM, backend exit-code failure, file
not found) is **never** a JSON-RPC protocol error. It is a normal, successful `tools/call` result
with `"isError": true` in the result body (see [Backend result handling](#backend-result-handling)
below).

## Tools (21)

No generic command runner, and no patch/rebuild/repair-header/compile-event/import-midi/
export-midi/songexchange tools are exposed — this is a deliberately curated, closed subset of the
Click CLI's surface, not a 1:1 mirror. Every tool's `inputSchema` is closed
(`additionalProperties: false`) with explicit types/bounds/enums — no silent coercion (a JSON
`true`/`false` is never accepted where an integer is expected). Every agent-controlled free-form
string is also bounded: ROM/file paths (`rom_path`, `out_path`, `in_path`, `out_img`, `out_tsa`)
must be non-empty when present and are capped at 4,096 chars, `text_search`'s `query` is capped at
4,096 chars, `table` names at 128 chars, and palette `addr` at 64 chars. An empty required path or
an over-length value is rejected as `-32602`, never silently defaulted or truncated (see
[Output bounds](#output-bounds) for the separate, output-side truncation rules).

| # | Tool | Click equivalent | Notes |
|---|------|-------------------|-------|
| 1 | `backend_check` | `check` | Never errors; `available: false` is a normal result. |
| 2 | `session_open` | `session open` | Requires `rom_path`; rejects files that fail local GBA header validation before opening session state. |
| 3 | `session_close` | `session close` | Never errors when no session is open. |
| 4 | `session_status` | `session status` | |
| 5 | `session_history` | `session history` | `count` bounded 1..100 (default 10). |
| 6 | `rom_info` | `rom info` | Rejects files that fail local GBA header validation before backend invocation or version decoding. |
| 7 | `rom_validate` | `rom validate` | Header heuristic; never calls the backend. |
| 8 | `rom_list_tables` | `rom tables` | |
| 9 | `rom_checksum` | `rom checksum` | Exit 2 (invalid header) is a structured, non-error result. |
| 10 | `data_export` | `data export` | **Overwrites** `out_path` (or its expansion for `table: "all"`). |
| 11 | `data_import` | `data import` | **Overwrites ROM data in place.** |
| 12 | `data_roundtrip` | `data roundtrip` | Exit 2 (mismatches found) is a structured, non-error result. |
| 13 | `names_resolve` | `names` | `ids` bounded to 1..256 entries. |
| 14 | `text_search` | `text search` | `limit` bounded 1..500 (default 50); bounded/paginated result. |
| 15 | `text_roundtrip` | `text roundtrip` | Exit 2 (mismatches found) is a structured, non-error result. **No `out_prefix` param** — kept read-only; use the Click CLI for diagnostic diff files. |
| 16 | `rom_lint` | `lint` | `limit` bounded 1..1000 (default 200) per array. |
| 17 | `image_quantize` | `image quantize` | No ROM required. **Overwrites** `out_path`. |
| 18 | `image_convert_map` | `image convert-map` | No ROM required. **Overwrites** `out_img`/`out_tsa`. |
| 19 | `palette_export` | `palette export` | **Overwrites** `out_path`. |
| 20 | `palette_import` | `palette import` | **Overwrites ROM data in place.** |
| 21 | `lz77` | `lz77` (#1942) | No ROM required. **Overwrites** `out_path`. |

### Safety annotations

Every tool declares `openWorldHint: false` (it never reaches outside the local filesystem/backend).
Exactly 9 tools are `readOnlyHint: false, destructiveHint: true` (they write to the filesystem
and/or mutate ROM data or session state in place); the other 12 are
`readOnlyHint: true, destructiveHint: false`:

| Destructive (9) | Read-only (12) |
|---|---|
| `session_open`, `session_close`, `data_export`, `data_import`, `image_quantize`, `image_convert_map`, `palette_export`, `palette_import`, `lz77` | `backend_check`, `session_status`, `session_history`, `rom_info`, `rom_validate`, `rom_list_tables`, `rom_checksum`, `data_roundtrip`, `names_resolve`, `text_search`, `text_roundtrip`, `rom_lint` |

> **Overwrite warning:** every filesystem-output tool (`data_export`, `image_quantize`,
> `image_convert_map`, `palette_export`, `lz77`, and `data export --table=all`'s prefix-expanded
> per-table files) **overwrites the declared output path(s) without prompting** if they already
> exist. Choose output paths deliberately, especially for prefix-expanded multi-file exports.

## Resources (3)

All resources return `application/json` text — never raw ROM bytes or arbitrary file contents.
Reading an unknown `uri` is `-32002`. Output is bounded (session history is capped at the same 100
entries the `Session` class enforces on load and write, then sliced again by the resource).
Persisted session fields with invalid types or over-limit paths are discarded, so the session
loads closed rather than bypassing online tool schemas. ROM header metadata is decoded only after
the active path passes the same local GBA check (at least 1 MiB, a complete header, and the fixed
`0x96` byte plus the header complement checksum); stale or tampered non-ROM session paths fail
closed with `rom_header: null`.

| URI | Contents |
|---|---|
| `febuildergba://session` | `{"open": false, "truncated": false}` when closed, else `{"open": true, ...session info..., "truncated": bool}` (ROM path/version/size, `force_version`, `modified`, `history_count`, `session_file`). |
| `febuildergba://session/history` | `{"open": bool, "history": [...], "truncated": bool}` — up to 100 most-recent operation entries. |
| `febuildergba://rom/metadata` | Session state **plus** parsed GBA header metadata (`title`, `game_code`, `maker_code`, `unit_code`, `device_type`, `software_version`, `header_checksum`) — never raw bytes or arbitrary files. |

## Session semantics

- **Common ROM resolution:** explicit `rom_path` argument, else the active session's ROM. Missing
  both is a **tool execution error** (`isError: true`), not a protocol error.
- **Common force-version resolution:** explicit `force_version` argument, else the active
  session's `force_version` (independently of how `rom_path` was resolved).
- `session_open` uses the same locally validated `rom_info` + `Session.open_rom` as the Click CLI;
  a failed GBA check never reaches the backend and never creates session state.
  `session_close`/`session_status`/`session_history` operate on the same `Session`; persisted
  path/version/size/timestamp/modified/history fields are type-checked and bounded on load.
- A successful **session-owned** `data_export` (i.e. the resolved ROM path normalizes to the same
  file as the active session's ROM) records a history entry.
- A successful **in-place** `data_import`/`palette_import` records a history entry **and** sets
  `modified: true` — but only when the backend exited 0 **and** the normalized resolved target
  equals the active session's ROM. Failures, advisory-only checks, and explicit overrides to a
  *different* ROM never dirty the session and never fabricate history.
- Calling a ROM-mutating tool with an explicit `rom_path` that differs from the open session's ROM
  never creates or mutates session state — and if no session is open at all, none of these tools
  ever create "phantom" session state as a side effect.

## Backend result handling

- Exit code `0` is always a normal, non-error result.
- `rom_checksum`, `data_roundtrip`, and `text_roundtrip` additionally treat backend exit code `2`
  as a **structured, non-error advisory** result (e.g. "header is invalid", "round-trip found
  mismatches") — `isError` stays `false`; the structured payload (e.g. `valid: false`,
  `lossless: false`) carries the signal.
- Any other non-zero/unexpected exit code becomes a **tool execution error**
  (`isError: true`), with the backend's `stdout`/`stderr` preserved (bounded, see below).
- `backend_check` reporting `available: false` is itself a normal, non-error result (the backend
  simply isn't installed/built yet, or its version probe failed/returned no version text).
- The 300-second backend subprocess timeout (`core/verbs.py` / `febuildergba_backend.run_cli`) is
  unchanged.

## Output bounds

| Field / tool | Bound | Metadata |
|---|---|---|
| `stdout` / `stderr` / `raw_output` / `lint_output` / execution `error` (any tool) | 65,536 chars | `<field>_truncated`, `<field>_original_length` |
| `text_search` matches (count) | default 50, max 500 (`limit` param) | `total`, `returned`, `truncated` |
| `text_search` each match's `text` | 4,096 chars | per-match `text_truncated`, aggregate `matches_text_truncated_count` |
| `rom_lint` `errors`/`warnings`/`info` (count) | default 200, max 1000 (`limit` param, applied per array) | `<array>_total`, `<array>_truncated` |
| `rom_lint` each array item string | 4,096 chars | aggregate `<array>_items_truncated_count` |
| `session_history` entries | 1..100 (`count` param) | bounded slice of the capped 100-entry history |
| Every string in `session_status` / `session_history` tool output | 4,096 chars, applied recursively | top-level `truncated` boolean |
| `names_resolve` `ids` | 1..256 entries; each 0..4,294,967,295 | count and unsigned 32-bit value range are schema-enforced (`-32602` if exceeded) |
| Every string value inside a resource payload (session/history/rom_header) | 4,096 chars, applied recursively | top-level `truncated` boolean (never exposes raw bytes — resources never contained raw bytes to begin with) |
| Resource collection size / nesting | 100 items per object/array / 16 levels | extra entries or deeper containers are replaced/truncated with top-level `truncated: true` |
| Input line / JSON-RPC batch | 1,048,576 chars / 64 entries | schema-independent `-32600` request guard |
| ROM/file path arguments (`rom_path`, `out_path`, `in_path`, `out_img`, `out_tsa`) | 1..4,096 chars when present | schema `minLength`/`maxLength` — empty or over-length input is `-32602`, not silently defaulted/truncated |
| `query` (text_search) | 4,096 chars | schema `maxLength` |
| `table` (data tools) | 128 chars | schema `maxLength` |
| `addr` (palette tools) | 64 chars | schema `maxLength` |

All input-side bounds above are enforced by the closed JSON Schema itself (rejected as
`-32602 Invalid params`, never silently coerced/truncated); the output-side bounds
(stdout/stderr/matches/lint arrays/session tool and resource strings) are enforced by the server
after the backend call, with explicit truncation metadata so a client can always tell when it's
seeing a partial value.

## Click-group mapping

| MCP tool group | Click group/command |
|---|---|
| `session_*` | `session` group |
| `rom_info`, `rom_validate`, `rom_list_tables`, `rom_checksum` | `rom` group (`info`, `validate`, `tables`, `checksum`) |
| `data_*` | `data` group |
| `text_*` | `text` group |
| `rom_lint` | top-level `lint` |
| `image_*` | `image` group |
| `palette_*` | `palette` group |
| `names_resolve` | top-level `names` |
| `lz77` | top-level `lz77` (added in #1942) |
| `backend_check` | top-level `check` |

## FEHRR-style example

FEHRR (the maintainer's own decomp/build pipeline; see
[`febuilder-cli-as-llm-backend.md`](febuilder-cli-as-llm-backend.md)) already shells out to
FEBuilderGBA headlessly as a map/TSA converter. A FEHRR-style MCP client session might look like:

```jsonc
// 1) client -> server
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"fehrr","version":"1.0.0"}}}
// server -> client
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26","capabilities":{"tools":{"listChanged":false},"resources":{"subscribe":false,"listChanged":false}},"serverInfo":{"name":"febuildergba-cli","version":"1.0.0"}}}

// 2) client -> server (notification, no response)
{"jsonrpc":"2.0","method":"notifications/initialized"}

// 3) client -> server: open a session on a working ROM
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"session_open","arguments":{"rom_path":"roms/FE8U.gba"}}}

// 4) client -> server: convert a hand-edited map tile image + TSA for that ROM's chipset
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"image_convert_map","arguments":{"in_path":"map.png","out_img":"map_tiles.bin","out_tsa":"map.tsa"}}}

// 5) client -> server: LZ77-compress the converted tile blob before a manual --import-asset step
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"lz77","arguments":{"mode":"compress","in_path":"map_tiles.bin","out_path":"map_tiles.lz77"}}}
```

## Backend environment variables

Tool handlers that shell out to `FEBuilderGBA.CLI` resolve the backend executable exactly like the
Click CLI does (`utils/febuildergba_backend.find_febuildergba_cli`):

| Variable | Description |
|----------|-------------|
| `FEBUILDERGBA_CLI_EXE` | Explicit path to the `FEBuilderGBA.CLI` executable (preferred). |
| `FEBUILDERGBA_CLI` | Fallback explicit path to the `FEBuilderGBA.CLI` executable. |

If neither is set, the resolver falls back to a published/build-output exe under
`FEBuilderGBA.CLI/bin/...`, then to `dotnet run --project FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj`.
`backend_check` reports which command was resolved (or the error, if none was found) without ever
treating "backend unavailable" as a protocol-level failure.

## Explicit exclusions

- **No generic command runner.** Every capability is an explicit, schema-validated tool — an agent
  cannot pass arbitrary `FEBuilderGBA.CLI` flags through this server.
- **No patch/rebuild/repair-header/event/music mutation tools** (`patch apply`/`apply-bin`,
  `rebuild`, `repair-header`, `compile-event`, `import-midi`/`export-midi`, `songexchange`,
  `disasm-event`, `lint-oam`, `pointercalc`, `disasm`, `portrait`) — these remain Click-CLI-only
  for now; see [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933) for the harness's
  overall CLI-verb coverage tracking.
- **No optional `text_roundtrip` diagnostic output** — the tool never writes an `out_prefix` diff
  file, so it can stay `readOnlyHint: true`.
- **No MCP SDK dependency** — the adapter is one stdlib-only module; it is not built on top of any
  third-party MCP framework.

## Tests

`agent-harness/cli_anything/febuildergba/tests/test_mcp_server.py` — protocol version negotiation,
strict `initialize` conformance (required `protocolVersion`/`capabilities`/`clientInfo.name`/
`clientInfo.version`, missing/malformed-field rejection, duplicate-initialize rejection),
lifecycle, single/batch framing (including mixed request/notification/invalid batches, empty
batches, notification-only batches, arbitrary string/integer IDs), every protocol error code
(including missing/empty/non-string `method` as Invalid Request vs. an unknown-but-valid method
name as Method not found), method-specific params tightening (unknown fields rejected, `_meta`
always allowed) for `ping`/`tools/list`/`resources/list`/`tools/call`/`resources/read`, the
21-tool/3-resource discovery surface, closed schemas including `maxLength` bounds on every
agent-controlled free-form string, the exact safety-annotation matrix, schema validation
(including the bool-is-not-an-int rule), advisory-vs-hard tool errors (checksum/data
roundtrip/text roundtrip exit 2 vs. exit 1), session precedence/history/`modified`-flag semantics
(success, failure, and other-ROM-override cases for both `data_import` and `palette_import`),
`force_version` precedence/fallback, "no raw bytes" resource content, output bounds (including
per-item text-search/lint-array truncation metadata and recursive resource-string truncation), a
`serve()` regression proving an unexpected internal failure emits a generic `-32603` response and
does not stop later lines from being handled, unknown methods/tools/resources, a real subprocess
launcher framing/flushing round-trip, and `.mcp.json` registration. All of it is private-ROM-free;
`agent-harness/cli_anything/febuildergba/tests/test_verbs.py` carries the one synthetic (no-ROM),
skip-gated-on-backend-availability real-backend LZ77 compress/decompress roundtrip test.
