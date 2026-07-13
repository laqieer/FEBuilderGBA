# FEBuilderGBA — CLI Harness SOP

## Software Overview

FEBuilderGBA is a comprehensive ROM hacking suite for the Fire Emblem GBA trilogy (FE6, FE7J/U, FE8J/U). Written in C# targeting .NET 9.0, it supports editing units, classes, items, maps, graphics, events, music, and more across five ROM variants.

## Backend

The real software is `FEBuilderGBA.CLI` — a cross-platform .NET 9.0 CLI that exposes **~70 commands** (lint, rebuild, buildfile export/build/round-trip, text export/import, struct data export/import, disassembly, palette quantization, UPS patching, event compile/disassemble, portrait / battle-animation / MIDI / palette I/O, decomp-asset export, and more). The authoritative, always-current command list is [`docs/cli-reference.md`](../docs/cli-reference.md) (per-argument detail in [`docs/cli-args.md`](../docs/cli-args.md)); the exact count drifts as verbs are added, so treat that reference — not this number — as canonical.

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
    │
    └── Backend: FEBuilderGBA.CLI (dotnet run / published exe)
         │
         └── FEBuilderGBA.Core (.NET 9.0 library)
              └── ROM manipulation, Huffman text, event scripts, etc.
```

## Command Layout

The Python harness (`cli_anything/febuildergba/febuildergba_cli.py`) exposes its surface as six
**Click command groups** (each with subcommands) plus a set of **standalone top-level commands**.
These harness commands are a *subset* of the backend's ~70 `FEBuilderGBA.CLI` verbs — see the
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
| `check` | Verify the `FEBuilderGBA.CLI` backend is available |

> A hidden interactive `repl` command also exists (not part of the normal command surface).
>
> **Coverage:** the harness currently wraps roughly a third of the backend's ~70
> `FEBuilderGBA.CLI` verbs — the harness Click commands above are a subset of the full CLI
> ([`docs/cli-reference.md`](../docs/cli-reference.md)). Closing that harness↔CLI coverage gap is
> tracked in [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933).

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
