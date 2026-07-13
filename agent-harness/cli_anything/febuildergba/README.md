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
**`export-map-settings-raw`**, `check`.

Unwrapped standalone backend commands: `--export-buildfile`, `--build-buildfile`, `--buildfile-roundtrip`.

## CLI verb coverage (harness ↔ CLI)

The harness wraps a growing subset of `FEBuilderGBA.CLI`'s ~70 verbs (see
[`docs/cli-reference.md`](../../../docs/cli-reference.md) for the authoritative list). This table
maps every backend verb to its harness command and coverage status; closing the remaining gap is
tracked in [#1933](https://github.com/laqieer/FEBuilderGBA/issues/1933).

**Status:** ✅ wrapped · 🆕 wrapped in #1933 · ⬜ not yet wrapped · ➖ n/a (dev/modifier/help). **~33 of ~70 wrapped.**

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
| `--expand-table` / `--merge3` / `--lz77` | — | ⬜ |
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
python -m pytest cli_anything/febuildergba/tests/ -v -s
```

If you run `pytest` from the **repo root** instead, set `PYTHONPATH` so the package resolves:

```bash
PYTHONPATH=agent-harness python -m pytest agent-harness/cli_anything/febuildergba/tests/ -q
```

Unit tests use synthetic data (no ROM/backend). The real-backend E2E tests are skip-gated on
`roms/*.gba` + a built `FEBuilderGBA.CLI` (set `FEBUILDERGBA_CLI_EXE` to override discovery); the
`compile-event` E2E additionally needs the EA/ColorzCore tools.
