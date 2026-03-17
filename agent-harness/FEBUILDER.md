# FEBuilderGBA — CLI Harness SOP

## Software Overview

FEBuilderGBA is a comprehensive ROM hacking suite for the Fire Emblem GBA trilogy (FE6, FE7J/U, FE8J/U). Written in C# targeting .NET 9.0, it supports editing units, classes, items, maps, graphics, events, music, and more across five ROM variants.

## Backend

The real software is `FEBuilderGBA.CLI` — a cross-platform .NET 9.0 CLI that already supports 15+ commands (lint, rebuild, text export/import, struct data export/import, disassembly, palette quantization, UPS patching, etc.).

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

## Command Groups

| Group | Purpose |
|-------|---------|
| `rom` | ROM loading, info, save, version detection |
| `data` | Struct data export/import (40 tables: units, classes, items, etc.) |
| `text` | Text export/import, roundtrip validation |
| `lint` | ROM integrity validation |
| `patch` | UPS patch creation/application |
| `asm` | Disassembly |
| `image` | Palette quantization, graphics operations |
| `map` | Map tile conversion |
| `music` | Song exchange between ROMs |
| `rebuild` | ROM defragmentation/rebuild |
| `session` | Session management (open, close, status, undo) |

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
