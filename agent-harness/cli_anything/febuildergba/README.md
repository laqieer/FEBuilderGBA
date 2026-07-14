# cli-anything-febuildergba

CLI harness for **FEBuilderGBA** — Fire Emblem GBA ROM hacking suite.

## Prerequisites

- **Python 3.10+**
- **.NET 9.0 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **FEBuilderGBA source** — The CLI is built from the repo's `FEBuilderGBA.CLI` project

## Installation

```bash
# Build the FEBuilderGBA.CLI backend
cd /path/to/FEBuilderGBA
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj

# Install the Python CLI harness
cd agent-harness
pip install -e .
```

## Usage

### REPL Mode (default)

```bash
cli-anything-febuildergba
```

### One-shot Commands

```bash
# ROM info and header
cli-anything-febuildergba rom info roms/FE8U.gba
cli-anything-febuildergba rom header roms/FE8U.gba

# Export unit data, look up a single entry
cli-anything-febuildergba --rom roms/FE8U.gba data export units -o units.tsv
cli-anything-febuildergba data lookup units.tsv 1

# Compare two exports
cli-anything-febuildergba data diff units_before.tsv units_after.tsv

# Search ROM text
cli-anything-febuildergba --rom roms/FE8U.gba text search "Eirika"

# List available patches
cli-anything-febuildergba --rom roms/FE8U.gba patch list

# Save ROM copy
cli-anything-febuildergba --rom roms/FE8U.gba rom save -o backup.gba

# Lint check
cli-anything-febuildergba --rom roms/FE8U.gba lint

# Validate / repair the GBA header checksum; byte-diff two ROMs
cli-anything-febuildergba --rom roms/FE8U.gba rom checksum
cli-anything-febuildergba rom diff roms/base.gba roms/mod.gba

# Palette export/import
cli-anything-febuildergba --rom roms/FE8U.gba palette export --addr 0x5524 -o pal.pal
cli-anything-febuildergba --rom roms/FE8U.gba palette import --addr 0x5524 -i pal.pal

# Import MIDI, compile an EA event script
cli-anything-febuildergba --rom roms/FE8U.gba import-midi 1A -i song.mid
cli-anything-febuildergba --rom roms/FE8U.gba compile-event -i script.event

# LZ77 compress/decompress an arbitrary file (no ROM required)
cli-anything-febuildergba lz77 -i data.bin -o data.lz77 --compress
cli-anything-febuildergba lz77 -i data.lz77 -o data.bin --decompress

# JSON output (for agents)
cli-anything-febuildergba --json rom info roms/FE8U.gba
```

`rom info`, `rom header`, and `session open` reject existing files that fail the local GBA ROM
check (at least 1 MiB, a complete header, the fixed `0x96` byte, and the header complement
checksum) before invoking the backend or decoding header fields. Automatic version detection used
by `patch list` applies the same check and returns `unknown` for invalid files without decoding
their game-code bytes.

### Session Mode

```bash
# Open a persistent session
cli-anything-febuildergba session open roms/FE8U.gba

# Subsequent commands use the session ROM
cli-anything-febuildergba data export units -o units.tsv
cli-anything-febuildergba lint
cli-anything-febuildergba text search "Seth"

# Check session status
cli-anything-febuildergba session status
```

## Command Groups

| Group | Description |
|-------|-------------|
| `rom` | ROM info, header dump, validation, save, table listing, **checksum**, **repair-header**, **diff** |
| `data` | Struct data export/import/diff/lookup (40 tables) |
| `text` | Text export/import, search, roundtrip validation |
| `lint` | ROM integrity checks |
| `patch` | UPS patch creation/application, patch listing, BIN apply |
| `image` | Palette quantization, map tile conversion |
| `palette` | **GBA palette export/import** |
| `disasm` | ROM disassembly |
| `rebuild` | ROM defragmentation |
| `session` | Session management |
| `check` | Backend availability check |

Standalone commands: `lint`, `disasm`, `songexchange`, `names`, `portrait`, `export-midi`,
**`import-midi`**, `disasm-event`, **`compile-event`**, `lint-oam`, `rebuild`, `pointercalc`,
**`export-map-settings-raw`**, **`lz77`**, `check`.

Unwrapped standalone backend commands: `--export-buildfile`, `--build-buildfile`, `--buildfile-roundtrip`.

## MCP server (issue #1942)

In addition to the Click CLI, this package ships a **dependency-free stdio MCP (Model Context
Protocol) server** at `agent-harness/cli_anything/febuildergba/mcp_server.py`, launched via
`agent-harness/febuildergba_mcp.py` (registered in the repo's [`.mcp.json`](../../../.mcp.json) as
`febuildergba-cli`, alongside the existing Windows `febuildergba-computer-use` entry). It exposes
21 explicit tools (a closed, non-mutating-beyond-declared-scope subset — no generic command
runner, no patch/rebuild/repair/event/music tools) and 3 read-only resources over newline-delimited
JSON-RPC 2.0. See **[`docs/MCP-SERVER.md`](../../../docs/MCP-SERVER.md)** for the full reference
(protocol versions, tool/resource list, schemas, safety annotations, session semantics, bounds).

### ROM backend trust boundary (issue #1942 / PR #1971)

The backend executable (`FEBuilderGBA.CLI`) is treated as **untrusted** for any `--rom` argument
it receives, but only inside MCP's dynamic scope (`prebuilt_backend_only()`, entered for the
duration of every MCP `tools/call`). Nine wrappers touch a ROM — `data export`/`import`/`roundtrip`,
`names`, `text search`/`roundtrip`, `palette export`/`import`, and `lint` (the nine backend-ROM
surfaces; `image`/`lz77`/`check` never take a `--rom`). Inside MCP scope, all nine open the
caller's path themselves **exactly once**, validate it as a regular 1..32 MiB file with a complete
GBA header (and checksum, except for `rom checksum` itself), and hand the backend a private
temporary **snapshot** instead of the caller's path. `lint` is the one surface that also does this
outside MCP: its always-on snapshot predates this fix. The other eight wrappers retain their
original, pre-#1942 direct-path Click behavior outside MCP scope — the caller's own path is
handed straight to the backend, with no local validation, no snapshot, and no temporary file,
exactly as before this fix.

- **Read-only wrappers** (`export_table`, `roundtrip_table`, `resolve_names`, `search_text`,
  `roundtrip_text`, `export_palette`, `lint_rom`), inside MCP scope, copy the validated bytes into
  the snapshot and never reopen the caller's path — so even if the backend (or a concurrent
  process) replaces, removes, or grows/shrinks the original file mid-call, the backend still only
  ever sees the bytes that were validated up front. Outside MCP scope, only `lint_rom` does this;
  the other six pass the caller's path directly and apply no local validation, matching their
  pre-#1942 Click behavior.
- **Mutating wrappers** (`import_table`, `import_palette`), inside MCP scope, keep the *original*
  file descriptor open read-write for the whole call and hand the backend a snapshot copy to
  mutate. Only after the backend reports success (exit code 0) is the mutated snapshot committed
  back — and only once every one of the following holds: the mutated snapshot is itself a valid
  1..32 MiB GBA ROM; the original pathname still identifies the exact same file this descriptor
  was opened from (`os.stat` vs. `os.fstat` via `os.path.samestat` **only** — never a
  string/normcase fallback); and the bytes originally read through that descriptor are still
  byte-for-byte unchanged. Only then is the descriptor rewound, written, truncated, flushed, and
  `fsync`'d, with a final size/identity re-check immediately after. **This write-back is
  identity-safe but not crash-atomic**: interruption during write/truncate/flush/`fsync` can
  leave partially updated or mixed old/new bytes, retain an old trailing suffix when the
  replacement is shorter, or leave completed writes not durably persisted. Any check failing —
  including the backend itself failing or timing out — aborts with no write and no session
  history/modified flag. For a session-owned ROM, the session lock is acquired before write-back.
  A lock timeout therefore commits nothing; if writing the matching session history later fails,
  the committed bytes are verified unchanged and the exact pre-commit bytes are restored through
  the same descriptor before the tool returns an error. Rollback refuses any path, size, or
  content change detected before restoration; concurrent body writes during restoration remain
  outside the non-transactional filesystem contract. Outside MCP scope, both mutating wrappers
  hand the backend the caller's own path directly and their "commit" step is a no-op, because the
  backend already wrote the result straight to the real file — matching their pre-#1942 Click
  behavior.
- Every temporary snapshot created above is removed once its wrapper returns, on every path
  (success, backend failure, or an exception) — never left behind. Outside MCP scope, the eight
  non-`lint` wrappers never create one in the first place.
- Because MCP tool handlers are the only callers running with an untrusted, externally-configured
  backend command, `run_cli` additionally enforces an **MCP-only seam guard**: while a tool
  handler is executing, every `--rom` argument (either `--rom=<path>` or `--rom <path>`) must
  name a path already registered as a private snapshot by the wrapper, or the call is rejected
  *before* the backend is even resolved or
  spawned — closed by default for any raw, unregistered, or future/unknown `--rom` invocation.
  This guard is completely inert outside MCP's dynamic scope, so Click's historic direct-path
  behavior is unchanged.
- Internal snapshot paths are stripped from backend stdout/stderr before they reach a caller
  (Click output or an MCP tool result) — the caller only ever sees their own path. Outside MCP
  scope, the eight non-`lint` wrappers never have a snapshot path to strip in the first place.
- `rom checksum`'s advisory exit-2 "invalid header" behavior is local, byte-level, and unrelated
  to the backend/snapshot mechanism — it is unaffected by any of the above.

## CLI verb coverage (harness ↔ CLI)

The harness wraps a growing subset of `FEBuilderGBA.CLI`'s ~70 verbs (see
[`docs/cli-reference.md`](../../../docs/cli-reference.md) for the authoritative list). This table
maps every backend verb to its harness command and coverage status; closing the remaining gap is
tracked in [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933).

**Status:** ✅ wrapped · 🆕 wrapped in #1933 · 🆕🔧 wrapped in #1942 (MCP server) · ⬜ not yet wrapped · ➖ n/a (dev/modifier/help). **~34 of ~70 wrapped.**

| CLI verb | Harness command | Status |
|---|---|---|
| `--version` | `check` (shows version) | ✅ |
| `--rom-info` | `rom info` | ✅ |
| `--checksum` | `rom checksum` | 🆕 |
| `--repair-header` | `rom repair-header` | 🆕 |
| `--diff` | `rom diff` | 🆕 |
| `--list-tables` | `rom tables` | ✅ |
| `--export-data` | `data export` | ✅ |
| `--import-data` | `data import` | ✅ |
| `--data-roundtrip` | `data roundtrip` | ✅ |
| `--resolve-names` | `names` | ✅ |
| `--translate` (export/import) | `text export` / `text import` | ✅ |
| `--search-text` | `text search` | ✅ |
| `--translate-roundtrip` | `text roundtrip` | ✅ |
| `--translate_batch` | — | ⬜ |
| `--text-refs` | — | ⬜ |
| `--lint` | `lint` | ✅ |
| `--lint-oam` | `lint-oam` | ✅ |
| `--makeups` | `patch create` | ✅ |
| `--applyups` | `patch apply` | ✅ |
| `--list-patches` | `patch list` | ✅ |
| `--apply-patch` (BIN) | `patch apply-bin` | ✅ |
| `--uninstall-patch` | — | ⬜ |
| `--list-resources` | — | ⬜ |
| `--decreasecolor` | `image quantize` | ✅ |
| `--convertmap1picture` | `image convert-map` | ✅ |
| `--export-palette` | `palette export` | 🆕 |
| `--import-palette` | `palette import` | 🆕 |
| `--render-portrait` | `portrait` | ✅ |
| `--export-portrait-all` | — | ⬜ |
| `--import-portrait` / `--import-portrait-all` | — | ⬜ |
| `--generate-font` | — | ⬜ |
| `--export-midi` | `export-midi` | ✅ |
| `--import-midi` | `import-midi` | 🆕 |
| `--disasm` | `disasm` | ✅ |
| `--disasm-event` | `disasm-event` | ✅ |
| `--compile-event` | `compile-event` | 🆕 |
| `--import-battle-anime` / `--export-battle-anime` | — | ⬜ |
| `--songexchange` | `songexchange` | ✅ |
| `--pointercalc` | `pointercalc` | ✅ |
| `--rebuild` | `rebuild` | ✅ |
| `--export-buildfile` | — | ⬜ |
| `--build-buildfile` | — | ⬜ |
| `--buildfile-roundtrip` | — | ⬜ |
| `--export-map-settings` | `export-map-settings-raw` | 🆕 |
| `--freespace` / `--hex-dump` | — | ⬜ |
| `--expand-table` / `--merge3` | — | ⬜ |
| `--lz77` | `lz77` | 🆕🔧 |
| Decomp-project verbs (`--project`, `--export-asset`, `--build-project`, …) | — | ⬜ |
| `--help` / `--force-detail` / `--test` / `--lastrom` | — | ➖ |

## Supported Data Tables

Units, classes, items, portraits, sound_room, support_units, support_talks,
map_settings, worldmap_points, worldmap_paths, event_haiku, event_battle_talk,
and 28 more. Run `cli-anything-febuildergba rom tables` for the full list.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `FEBUILDERGBA_CLI_EXE` | Explicit path to FEBuilderGBA.CLI executable (preferred) |
| `FEBUILDERGBA_CLI` | Fallback path to FEBuilderGBA.CLI executable |

## Running Tests

```bash
cd agent-harness
pip install -e .[test]   # bounded pytest>=8,<9
python -m pytest cli_anything/febuildergba/tests/ -v -s
```

If you run `pytest` from the **repo root** instead, set `PYTHONPATH` so the package resolves:

```bash
PYTHONPATH=agent-harness python -m pytest agent-harness/cli_anything/febuildergba/tests/ -q
```

`tests/test_mcp_server.py` covers the MCP JSON-RPC adapter (protocol negotiation, lifecycle,
batching, all protocol error codes, the 21-tool/3-resource surface, schema validation, session
semantics, and bounds) and is just as synthetic/private-ROM-free as the rest of the suite; the one
real-backend LZ77 roundtrip test in `test_verbs.py` is skip-gated on backend availability only.

Unit tests use synthetic data (no ROM/backend). The real-backend E2E tests are skip-gated on
`roms/*.gba` + a built `FEBuilderGBA.CLI` (set `FEBUILDERGBA_CLI_EXE` to override discovery); the
`compile-event` E2E additionally needs the EA/ColorzCore tools.
