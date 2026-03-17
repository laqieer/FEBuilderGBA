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
# ROM info
cli-anything-febuildergba rom info roms/FE8U.gba

# Export unit data
cli-anything-febuildergba --rom roms/FE8U.gba data export units -o units.tsv

# Lint check
cli-anything-febuildergba --rom roms/FE8U.gba lint

# Export all text
cli-anything-febuildergba --rom roms/FE8U.gba text export -o texts.tsv

# JSON output (for agents)
cli-anything-febuildergba --json rom info roms/FE8U.gba
```

### Session Mode

```bash
# Open a persistent session
cli-anything-febuildergba session open roms/FE8U.gba

# Subsequent commands use the session ROM
cli-anything-febuildergba data export units -o units.tsv
cli-anything-febuildergba lint
cli-anything-febuildergba text export -o texts.tsv

# Check session status
cli-anything-febuildergba session status
```

## Command Groups

| Group | Description |
|-------|-------------|
| `rom` | ROM info, validation, table listing |
| `data` | Struct data export/import (40 tables) |
| `text` | Text export/import, roundtrip validation |
| `lint` | ROM integrity checks |
| `patch` | UPS patch creation/application |
| `image` | Palette quantization, map tile conversion |
| `disasm` | ROM disassembly |
| `rebuild` | ROM defragmentation |
| `session` | Session management |
| `check` | Backend availability check |

## Supported Data Tables

Units, classes, items, portraits, sound_room, support_units, support_talks,
map_settings, worldmap_points, worldmap_paths, event_haiku, event_battle_talk,
and 28 more. Run `cli-anything-febuildergba rom tables` for the full list.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `FEBUILDERGBA_CLI` | Explicit path to FEBuilderGBA.CLI executable |

## Running Tests

```bash
cd agent-harness
python -m pytest cli_anything/febuildergba/tests/ -v -s
```
