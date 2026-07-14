# FEBuilderGBA MCP stdio server

Tracks issue [#1942](https://github.com/laqieer/FEBuilderGBA/issues/1942).

A **dependency-free** Model Context Protocol (MCP) stdio adapter for the CLI harness
(`cli-anything-febuildergba`, see [`agent-harness/FEBUILDER.md`](../agent-harness/FEBUILDER.md) /
[`agent-harness/cli_anything/febuildergba/README.md`](../agent-harness/cli_anything/febuildergba/README.md)),
implemented in one stdlib-only module:
[`agent-harness/cli_anything/febuildergba/mcp_server.py`](../agent-harness/cli_anything/febuildergba/mcp_server.py).

It reuses the shared `Session` and `core/*` wrappers, but applies MCP-specific backend isolation:
it never shells out to Click or imports an MCP SDK, and it accepts only an explicit or prebuilt
backend (apphost or DLL), never Click's development `dotnet run` fallback. It speaks
newline-delimited JSON-RPC 2.0 on stdin/stdout only.

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
`~/.cli-anything-febuildergba/sessions/default.json` is used. Startup accepts only the documented
option in `--session-file PATH` or `--session-file=PATH` form. Missing, empty, duplicate,
abbreviated, unknown, or positional arguments exit with status 2 before the server loop starts,
so malformed configuration cannot silently fall back to the default session.

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
  reconfigured to UTF-8 before the loop, overriding locale-dependent legacy pipe encodings.
  Only terminal CR/LF framing is removed; valid JSON space/tab whitespace is preserved for the
  decoder, and non-JSON whitespace such as vertical tab, form feed, or nonbreaking space is not
  normalized into an accepted request.
  Malformed UTF-8 bytes are preserved only long enough to reject that complete line with `-32700`;
  the following line is still processed. All logs/diagnostics go to stderr; nothing else is ever
  written to stdout. Input lines are capped at 1,048,576 characters and batches at 64 entries; an
  oversized line is drained before the next message is processed. Non-standard `NaN`/`Infinity`
  JSON tokens, integer tokens over 78 digits, JSON nesting beyond 64 levels, and any defensive
  decoder `RecursionError` are rejected as parse errors. Request IDs are limited to strings of at
  most 4,096 characters or integers with at most 256 bits of magnitude. Even an unexpected
  failure escaping the server's dispatch loop emits a single flushed, generic `-32603` response
  (`id: null`) rather than silently dying or dropping the line — the loop always keeps processing
  subsequent lines.
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
  shape and reject unexpected fields as `-32602` (forward-compatible `_meta` is accepted).
  JSON-RPC object and array params are both structurally valid; MCP request handlers below require
  objects, while an id-less notification with array params remains silent as required:
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
| `-32700` | Parse error | The line isn't valid UTF-8/JSON, including non-standard `NaN`/`Infinity` tokens. |
| `-32600` | Invalid Request | Wrong `jsonrpc` version, malformed request/notification shape, missing/empty/non-string `method`, scalar or null `params` (non-object, non-array; arrays are structurally accepted and then reach method validation as `-32602`), invalid `id` (null/bool/object), an empty or over-64-entry batch, an over-1,048,576-character line, `initialize` sent inside a batch, duplicate `initialize` after a successful one, or an operation attempted before `initialize` (except `ping`). |
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
| 1 | `backend_check` | `check` | Never errors; missing, non-executable, timed-out, and other OS-level launch failures are normalized to `available: false`. |
| 2 | `session_open` | `session open` | Requires `rom_path`; metadata comes only from local descriptor validation. `lint_output: ""` and `lint_exit_code: -1` permanently mean lint was not attempted; call `rom_lint`. |
| 3 | `session_close` | `session close` | Never errors when no session is open; returns `stale_session` without closing if another process reopened the session first. |
| 4 | `session_status` | `session status` | |
| 5 | `session_history` | `session history` | `count` bounded 1..100 (default 10). |
| 6 | `rom_info` | `rom info` | Rejects files outside 1..32 MiB or that fail local GBA header validation; it never invokes the backend. `lint_output: ""` and `lint_exit_code: -1` permanently mean lint was not attempted; call `rom_lint`. |
| 7 | `rom_validate` | `rom validate` | 1..32 MiB header heuristic; never calls the backend. |
| 8 | `rom_list_tables` | `rom tables` | |
| 9 | `rom_checksum` | `rom checksum` | Computes from one locally opened, regular 1..32 MiB descriptor; the backend never reopens the path. A checksum mismatch is exit 2 and remains a structured, non-error result. |
| 10 | `data_export` | `data export` | **Overwrites** `out_path` (or its expansion for `table: "all"`). |
| 11 | `data_import` | `data import` | **Overwrites ROM data in place.** |
| 12 | `data_roundtrip` | `data roundtrip` | Exit 2 (mismatches found) is a structured, non-error result. |
| 13 | `names_resolve` | `names` | `ids` bounded to 1..256 entries. |
| 14 | `text_search` | `text search` | `limit` bounded 1..500 (default 50); bounded/paginated result. |
| 15 | `text_roundtrip` | `text roundtrip` | Exit 2 (mismatches found) is a structured, non-error result. **No `out_prefix` param** — kept read-only; use the Click CLI for diagnostic diff files. |
| 16 | `rom_lint` | `lint` | Validates then snapshots the opened ROM descriptor to a temporary `.gba` before backend lint. `limit` bounded 1..1000 (default 200) per array. Only leading `[ERROR]` and `[WARNING]` CLI severity markers create findings; `Lint: No errors found.` remains informational. |
| 17 | `image_quantize` | `image quantize` | No ROM required. `palette_no` is a maximum color count (default 16, range 2..256; 1 is allowed only with `no_reserve_1st: true`). **Overwrites** `out_path`. |
| 18 | `image_convert_map` | `image convert-map` | No ROM required. **Overwrites** `out_img`/`out_tsa`. |
| 19 | `palette_export` | `palette export` | **Overwrites** `out_path`. |
| 20 | `palette_import` | `palette import` | **Overwrites ROM data in place.** |
| 21 | `lz77` | `lz77` (#1942) | No ROM required. **Overwrites** `out_path`. |

### Safety annotations

Every tool declares `openWorldHint: false` (it never reaches outside the local filesystem/backend).
These annotations remain truthful because MCP requires a prebuilt backend and never triggers
`dotnet run`, build, restore, or NuGet/network activity.
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
entries the `Session` class enforces on load and write, then sliced again by the resource; direct
in-memory overflow sets `truncated: true`). Persisted session fields with invalid types or
over-limit paths are discarded, so the session loads closed rather than bypassing online tool
schemas. ROM header metadata is decoded only after the opened descriptor is confirmed to be a
regular file and passes the same local GBA check (at least 1 MiB, a complete header, and the fixed
`0x96` byte plus the header complement checksum). Where supported, the descriptor is opened
nonblocking before its type is checked, so a pathname race to a FIFO cannot hang the server; stale
or tampered non-ROM session paths fail closed with `rom_header: null`.

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
- Parser-malformed persisted content (including invalid UTF-8, excessive integer digits,
  excessive nesting, non-standard numeric constants, or non-finite numeric overflow) and session
  files over 8 MiB load closed. Out-of-memory and programming faults remain unmasked; direct and
  startup session filesystem faults surface to the caller. Only per-line live-server refresh
  `OSError` uses the throttled last-known-state degradation described below.
- The shared Click/MCP session refreshes before each real MCP input line. Every mutation reloads
  and merges under a bounded five-second `<session>.lock` sidecar transaction, then atomically
  replaces the JSON file. The sidecar intentionally persists after close; it uses Windows'
  mandatory byte-zero lock and POSIX's advisory whole-sidecar `flock`, while the sidecar prevents
  atomic replacement from bypassing either lock. Every close is generation-guarded, so a stale
  close request returns `stale_session` instead of deleting a concurrently reopened session. If
  a session is reopened or closed while a backend call runs, its later operation attribution is
  skipped rather than reviving stale state.
  Constructing a session whose JSON file does not exist keeps the default closed state in memory
  without creating the parent directory or lock sidecar; explicit refreshes and all mutations
  still use the transaction lock. This keeps stateless Click commands usable when the default
  session location is read-only or unavailable.
  If a transaction body fails, an independent pre-mutation state snapshot is restored before the
  error is re-raised, so failed writes/deletes cannot leave a phantom open, history entry,
  modified flag, or close visible to a later request in the same process.
  A refresh filesystem failure emits at most one generic warning per minute and serves the last
  known read snapshot; mutations still reload transactionally, return `isError` on failure, and
  never stale-write.
- A successful **session-owned** `data_export` records a history entry. Ownership uses filesystem
  identity when both paths are available, so symlink and hardlink aliases of the active ROM count
  as the same file; normalized absolute-path comparison is retained only as the unavailable-path
  fallback.
- A successful **in-place** `data_import`/`palette_import` records a history entry **and** sets
  `modified: true` — but only when the backend exited 0 **and** the resolved target has the same
  filesystem identity as the active session's ROM (including symlink/hardlink aliases). Failures,
  advisory-only checks, and explicit overrides to a *different* ROM never dirty the session and
  never fabricate history.
- Shared Click/MCP history uses the same identifiers: `data_export`, `data_import`, and
  `import_palette`.
- Every Click history-producing ROM command applies the same filesystem-identity ownership rule,
  so an explicit operation on another ROM cannot contaminate the active session and a
  symlink/hardlink alias still counts as the active ROM. Click commands that write a separate
  output ROM record history without marking the active input ROM modified; they set `modified`
  only when the reported output destination identifies the active ROM.
- Calling a ROM-mutating tool with an explicit `rom_path` that differs from the open session's ROM
  never creates or mutates session state — and if no session is open at all, none of these tools
  ever create "phantom" session state as a side effect.

## ROM backend trust boundary

(Issue #1942 / PR #1971.) The backend executable is treated as **untrusted** for any `--rom`
argument it receives — this matters most for MCP, where the backend command is externally
configured (`FEBUILDERGBA_CLI_EXE`/`FEBUILDERGBA_CLI`) and its output is otherwise
attacker-influenced text fed straight back to the calling agent.

- **Every one of the nine backend-ROM tools** — `data_export`, `data_import`, `data_roundtrip`,
  `names_resolve`, `text_search`, `text_roundtrip`, `palette_export`, `palette_import`, and
  `rom_lint` (`image_quantize`/`image_convert_map`/`lz77`/`backend_check` never take a `--rom`) —
  opens the resolved ROM path itself exactly once, validates it as a regular 1..32 MiB file with a
  complete GBA header (and checksum, where applicable), and hands the backend a private temporary
  **snapshot** instead of the resolved path. This is the same pattern `rom_lint` already used,
  generalized to the rest of the ROM-touching surface — never a second, redundant snapshot.
- **Read-only tools** (`data_export`, `data_roundtrip`, `names_resolve`, `text_search`,
  `text_roundtrip`, `palette_export`, `rom_lint`) copy the validated bytes into the snapshot and
  never reopen the resolved path, so the backend only ever sees the bytes validated up front, even
  if the underlying file is replaced, removed, or resized mid-call.
- **Mutating tools** (`data_import`, `palette_import`) keep the original file descriptor open
  read-write for the whole call and hand the backend a snapshot copy to mutate. The mutated
  snapshot is committed back through that *same* descriptor only after the backend reports exit
  code `0`, and only once the snapshot itself revalidates as a 1..32 MiB GBA ROM, the resolved
  pathname still identifies the exact same file the descriptor was opened from (checked with
  `os.stat`/`os.fstat` via `os.path.samestat` **only** — never a string/normcase path comparison),
  and the bytes originally read through that descriptor are still byte-for-byte unchanged. The
  write-back itself (rewind/write/truncate/flush/`fsync`, then a final size+identity re-check) is
  **identity-safe but not crash-atomic**: interruption during write/truncate/flush/`fsync` can
  leave partially updated or mixed old/new bytes, retain an old trailing suffix when the
  replacement is shorter, or leave completed writes not durably persisted. Any failed check —
  including the backend itself failing, timing out, or raising — aborts with no write and no
  session history/modified flag, exactly as if the tool call itself had failed.
- Every temporary snapshot is removed once its tool call returns, whether it succeeded, the
  backend failed, or the call raised — never left behind.
- `run_cli` additionally enforces an **MCP-only seam guard**: while a tool handler is executing,
  every `--rom` argument passed to the backend (either `--rom=<path>` or `--rom <path>`) must name
  a path already registered as a private snapshot by the tool's wrapper, or the call is rejected
  *before* the backend is even resolved or
  spawned. This is a value/path check, not just an argv-shape allowlist, so it also fails closed
  on any future/unknown tool that forgets to snapshot. The guard is completely inert outside MCP's
  dynamic scope — the Click CLI's historic direct-path behavior is unchanged.
- Internal snapshot paths are stripped from backend stdout/stderr before they reach a tool result
  — callers only ever see their own resolved path, and `_BoundedOutput`'s `original_length`/
  `truncated` metadata (see [Output bounds](#output-bounds)) survives the substitution.
- `rom_checksum`'s advisory exit-2 "invalid header" result is computed locally and does not
  invoke the backend at all — it is unrelated to, and unaffected by, any of the above.

## Backend result handling

- Exit code `0` is always a normal, non-error result.
- `rom_checksum`, `data_roundtrip`, and `text_roundtrip` additionally treat backend exit code `2`
  as a **structured, non-error advisory** result (e.g. "header is invalid", "round-trip found
  mismatches") — `isError` stays `false`; the structured payload (e.g. `valid: false`,
  `lossless: false`) carries the signal.
- `rom_info` and `session_open` never run lint. Their permanent
  `lint_output: ""` / `lint_exit_code: -1` sentinels mean lint was not attempted; use
  `rom_lint` when lint findings are needed. Like every other read-only backend-ROM tool,
  `rom_lint` validates and snapshots the opened descriptor before invoking the backend (see
  [ROM backend trust boundary](#rom-backend-trust-boundary)); the snapshot
  pins the already-validated header and rejects length drift while copying from that descriptor,
  so replacing the original pathname cannot redirect lint. None of the read-only snapshots claim
  transaction-level atomicity for concurrent, same-length writes elsewhere in the ROM body.
- Outside those advisory cases, any other non-zero/unexpected exit code becomes a **tool
  execution error** (`isError: true`), with the backend's `stdout`/`stderr` preserved (bounded,
  see below).
- `backend_check` reporting `available: false` is itself a normal, non-error result (the backend
  simply isn't installed/built yet, or its version probe failed, emitted invalid UTF-8, or
  returned no version text).
- The 300-second backend subprocess timeout (`core/verbs.py` / `febuildergba_backend.run_cli`) is
  unchanged.

## Output bounds

| Field / tool | Bound | Metadata |
|---|---|---|
| `stdout` / `stderr` / `raw_output` / `lint_output` / execution `error` / `version` (any tool) | 65,536 chars | `<field>_truncated`, `<field>_original_length` |
| Successful backend version probe | 4,096 chars | Over-limit probes become `available: false`; no version text is returned |
| `names_resolve` each requested name | 4,096 chars, with unrequested backend keys omitted | `names_truncated`, `names_truncated_count`, `names_original_lengths`, `names_omitted_count` |
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
| Input JSON nesting | 64 object/array levels; delimiters inside quoted strings are ignored | over-limit input is `-32700` before decoder behavior can vary by Python version |
| JSON-RPC request ID | string up to 4,096 chars or integer magnitude up to 256 bits; integer tokens are pre-limited to 78 digits | invalid ID is `-32600` with `id: null`; an overlong integer token is `-32700` |
| ROM/file path arguments (`rom_path`, `out_path`, `in_path`, `out_img`, `out_tsa`) | 1..4,096 chars when present | schema `minLength`/`maxLength` — empty or over-length input is `-32602`, not silently defaulted/truncated |
| `query` (text_search) | 4,096 chars | schema `maxLength` |
| `table` (data tools) | 128 chars | schema `maxLength` |
| `addr` (palette tools) | 64 chars | schema `maxLength` |

For MCP backend invocations, stdout and stderr are bounded **while their pipes are concurrently
drained**, not only when the response is serialized. The server retains at most the 65,536-character
decoded prefix of each stream, continues discarding/draining the remainder to avoid a pipe deadlock,
and reports the exact decoded source length when truncation occurred. Click callers retain their
existing full-capture behavior. MCP backend stdin is detached to `DEVNULL`, preventing a backend
tool from consuming pending JSON-RPC frames from the server's protocol input. A bounded MCP call
with `capture=False` fails closed before backend resolution or subprocess launch, preventing
protocol stdout/stdin inheritance.

All input-side bounds above are enforced by the closed JSON Schema itself (rejected as
`-32602 Invalid params`, never silently coerced/truncated). Backend stdout/stderr are bounded
during pipe draining as described above; other output-side bounds (matches, lint arrays, session
tool and resource strings) are enforced by the server before serialization, with explicit
truncation metadata so a client can always tell when it is seeing a partial value.

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

MCP tool handlers resolve only an explicit backend executable, a prebuilt apphost, or a prebuilt
`FEBuilderGBA.CLI.dll` launched as `dotnet <dll>` (`utils/febuildergba_backend.find_febuildergba_cli`):

| Variable | Description |
|----------|-------------|
| `FEBUILDERGBA_CLI_EXE` | Explicit path to the `FEBuilderGBA.CLI` executable (preferred). |
| `FEBUILDERGBA_CLI` | Fallback explicit path to the `FEBuilderGBA.CLI` executable. |

If neither is set, MCP searches published/build output under `FEBuilderGBA.CLI/bin/...` for an
apphost or DLL. It fails closed rather than using `dotnet run`, so it never builds, restores, or
contacts NuGet. `backend_check` reports unavailable rather than treating that condition as a
protocol-level failure. Click callers alone retain the legacy
`dotnet run --project FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj --` fallback.

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

`agent-harness/cli_anything/febuildergba/tests/test_mcp_server.py` — protocol version negotiation
with the shared operational contract suite exercised under both advertised revisions,
strict `initialize` conformance (required `protocolVersion`/`capabilities`/`clientInfo.name`/
`clientInfo.version`, missing/malformed-field rejection, duplicate-initialize rejection),
lifecycle, single/batch framing (including mixed request/notification/invalid batches, empty
batches, notification-only batches, bounded string/integer IDs), every protocol error code
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
does not stop later lines from being handled, checksum-path rejection before backend invocation,
unknown methods/tools/resources, a real subprocess launcher framing/flushing round-trip with a
bounded read timeout, and `.mcp.json` registration. All of it is private-ROM-free.

`agent-harness/cli_anything/febuildergba/tests/test_core.py` — shared backend, project, session,
and Click-adapter behavior. Its lint parser regressions prove that the clean summary is not an
error and only explicit CLI severity markers create findings. Its ROM-backend trust-boundary
regressions (issue #1942 / PR #1971) cover: the MCP-only `run_cli` seam gate rejecting an
unregistered/raw `--rom` value in either supported argument form before the backend is resolved
or spawned, and staying inert
outside MCP scope; the `backend_rom_snapshot`/`backend_mutating_rom_snapshot` dispatch helpers
directly, pinning that they delegate to the always-on snapshot primitives inside MCP scope and
yield the caller's own path with a no-op commit outside it; `sanitize_snapshot_path` stripping a
leaked internal path from every occurrence in a string while preserving `_BoundedOutput`
truncation metadata; `MutatingRomSnapshot`/`mutating_rom_snapshot` directly (successful
write-back, an uncommitted mutation never touching the original, and commit refusing a replaced
path, same-length content drift, an oversized mutated result, or an invalid-header mutated
result); a class-wide table, exercised inside MCP scope, spanning all nine backend-ROM wrappers
proving each receives a once-registered, since-removed snapshot instead of the original path (so
`lint` is proven not to double-snapshot), rejects an oversized or non-ROM input before the backend
runs, and — for the seven read-only wrappers — still sees the originally validated bytes even if
the real path is replaced mid-call; a companion table, exercised outside MCP scope, proving the
eight non-`lint` wrappers instead hand the backend the caller's own path with no local validation
and a no-op mutating commit, matching their pre-#1942 behavior (`lint`'s always-on snapshot is
intentionally excluded from that table); sanitization actually wired into wrapper output (not just
the pure helper); and the two mutating wrappers' end-to-end commit protocol (success, backend
failure, an oversized/invalid-header mutated result, and a replaced-path attempt, tolerant of
either failing closed or being blocked outright by the platform's own file-sharing semantics),
all exercised inside MCP scope.

`agent-harness/cli_anything/febuildergba/tests/test_verbs.py` carries the one synthetic (no-ROM),
skip-gated-on-backend-availability real-backend LZ77 compress/decompress roundtrip test, plus
fake-backend/real-ROM-fixture regressions proving Click's `rom palette export`/`import` commands
keep their pre-#1942 direct-path behavior outside MCP scope (issue #1942 / PR #1971) — no local
validation, no snapshot, the backend receives the caller's own path unchanged — while a failing
backend still leaves the original file untouched; MCP-scoped snapshot/commit coverage for these
same two wrappers is part of `test_core.py`'s class-wide tables above.
