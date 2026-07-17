# FEBuilderGBA — CLI Harness SOP

## Software Overview

FEBuilderGBA is a comprehensive ROM hacking suite for the Fire Emblem GBA trilogy (FE6, FE7J/U, FE8J/U). Written in C# targeting .NET 10.0, it supports editing units, classes, items, maps, graphics, events, music, and more across five ROM variants.

## Backend

The real software is `FEBuilderGBA.CLI` — a cross-platform .NET 10.0 CLI that exposes **71 commands** (lint, rebuild, deterministic headless playtest, buildfile export/build/round-trip, text export/import, struct data export/import, disassembly, palette quantization, UPS patching, event compile/disassemble, portrait / battle-animation / MIDI / palette I/O, decomp-asset export, and more). The authoritative, always-current command list is [`docs/cli-reference.md`](../docs/cli-reference.md) (per-argument detail in [`docs/cli-args.md`](../docs/cli-args.md)).

The Python CLI harness wraps `FEBuilderGBA.CLI` via subprocess, adding:
- Stateful session management (track open ROM, undo history)
- JSON output mode for agent consumption
- Interactive REPL for exploratory editing
- Higher-level commands combining multiple CLI operations

## Architecture

```
Python CLI Harness (cli-anything-febuildergba)
    │
    ├── Stateful session (JSON) — tracks ROM path, version, undo log
    ├── JSON output mode — machine-readable for agents
    ├── REPL — interactive command loop
    ├── MCP stdio server (cli-anything-febuildergba-mcp / mcp_server.py, #1942)
    │     — dependency-free JSON-RPC 2.0 adapter reusing the same Session +
    │       core wrappers, exposing 21 tools + 3 resources (see
    │       docs/MCP-SERVER.md); registered in .mcp.json as "febuildergba-cli"
    │
    └── Backend: FEBuilderGBA.CLI (dotnet run / published exe)
         │
         └── FEBuilderGBA.Core (.NET 10.0 library)
              └── ROM manipulation, Huffman text, event scripts, etc.
```

### MCP server startup (#1942)

Two equivalent ways to start the stdio MCP server:

```bash
# 1. Installed console script (after `pip install -e agent-harness`)
cli-anything-febuildergba-mcp [--session-file PATH]

# 2. Manual no-install launcher (use python, python3, or py -3 for your platform)
python3 agent-harness/febuildergba_mcp.py [--session-file PATH]
```

After the editable install, the repo's [`.mcp.json`](../.mcp.json) registers option 1 as
`febuildergba-cli` (`command: "cli-anything-febuildergba-mcp"`, `args: []`), alongside the
pre-existing Windows `febuildergba-computer-use` entry. The installation's scripts directory
must be on the MCP host's `PATH`; option 2 remains the no-install fallback. The server speaks
newline-delimited JSON-RPC 2.0 on stdin/stdout only (protocol versions `2025-03-26` and
`2024-11-05`); it never shells out to Click and never imports an MCP SDK — see
[`docs/MCP-SERVER.md`](../docs/MCP-SERVER.md) for the full tool/resource reference.

## Command Layout

The Python harness (`cli_anything/febuildergba/febuildergba_cli.py`) exposes its surface as six
**Click command groups** (each with subcommands) plus a set of **standalone top-level commands**.
These harness commands are a *subset* of the backend's 71 `FEBuilderGBA.CLI` verbs — see the
Coverage note below.

### Command groups

| Group | Purpose |
|-------|---------|
| `rom` | ROM info, validate, version detection, tables, header, save |
| `data` | Struct data export/import (40 tables: units, classes, items, etc.), roundtrip, inspect, diff, lookup |
| `text` | Text export/import, search, roundtrip validation |
| `patch` | Patch operations — list, create UPS, apply UPS, apply BIN (`apply-bin`) |
| `image` | Palette quantization (`quantize`), map tile conversion (`convert-map`) |
| `session` | Session management (open, close, status, history) |

### Standalone commands

| Command | Purpose |
|---------|---------|
| `lint` | ROM integrity validation |
| `disasm` | Disassemble ROM to a text file |
| `songexchange` | Song exchange between ROMs |
| `names` | Resolve unit/class/item/song IDs to names |
| `portrait` | Render a unit portrait to PNG |
| `export-midi` | Export a ROM song to MIDI |
| `disasm-event` | Disassemble an event/procs/AI script at an address |
| `lint-oam` | Validate battle-animation OAM data |
| `rebuild` | ROM defragmentation/rebuild |
| `pointercalc` | Search for pointer references in ROM |
| `lz77` | LZ77 compress/decompress an arbitrary file (no ROM required, #1942) |
| `playtest` | Deterministic headless mGBA verification (Click only; not MCP) |
| `check` | Verify the `FEBuilderGBA.CLI` backend is available |

> A hidden interactive `repl` command also exists (not part of the normal command surface).
>
> **Coverage:** the harness currently wraps roughly **35 of 71** (`playtest` is Click-only and
> deliberately excluded from MCP; `lz77` was added in #1942) of the backend's
> `FEBuilderGBA.CLI` verbs — the harness Click commands above are a
> subset of the full CLI ([`docs/cli-reference.md`](../docs/cli-reference.md)). Closing that
> harness↔CLI coverage gap is tracked in
> [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933); the MCP stdio adapter surface
> itself (21 tools) is tracked in
> [#1942](https://github.com/laqieer/FEBuilderGBA/issues/1942) and is intentionally a curated,
> non-mutating-beyond-declared-scope subset of these Click commands, not a 1:1 mirror.

## Data Formats

- **ROM**: `.gba` binary files (8-32 MB)
- **Struct data**: TSV (tab-separated values) with column headers
- **Text**: TSV with ID → Text mapping
- **Patches**: UPS format
- **Session**: JSON file tracking state

## Version Detection

ROM versions are auto-detected via binary signature matching:
- `FE6` — Fire Emblem 6 (Binding Blade) Japanese
- `FE7J` — Fire Emblem 7 (Blazing Blade) Japanese
- `FE7U` — Fire Emblem 7 US/International
- `FE8J` — Fire Emblem 8 (Sacred Stones) Japanese
- `FE8U` — Fire Emblem 8 US/International

## Key Constraints

- ROM files are binary — all manipulation goes through the .NET Core library
- Text uses Huffman compression — encode/decode requires full ROM init
- Event scripts have 100+ argument types — parsing is complex
- Graphics use GBA-specific formats (4bpp tiles, LZ77 compression, OAM sprites)

## Tests

```bash
cd agent-harness
pip install -e .[test]   # bounded pytest>=8,<9
python -m pytest cli_anything/febuildergba/tests/ -v -s
```

Key suites: `tests/test_core.py` (session/project/data/text/lint units), `tests/test_verbs.py`
(issue #1933 verb wrappers + the #1942 `lz77_file`/`lz77` Click command, including a synthetic
skip-gated real-backend LZ77 roundtrip), and `tests/test_mcp_server.py` (issue #1942 — the MCP
JSON-RPC adapter: protocol version negotiation, lifecycle, single/batch framing, every protocol
error code, the 21-tool/3-resource surface, closed schemas, safety annotations, session
precedence/history/modified semantics, and output bounds). All of the above are private-ROM-free.
`tests/test_full_e2e.py` is the real-backend, real-ROM end-to-end suite.
