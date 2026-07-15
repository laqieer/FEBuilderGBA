---
name: "cli-anything-febuildergba"
description: "CLI harness for FEBuilderGBA — Fire Emblem GBA ROM hacking (data export/import, text translation, lint, patches, disassembly)"
---

# cli-anything-febuildergba

CLI harness for **FEBuilderGBA**, the Fire Emblem GBA ROM hacking suite. Wraps the .NET FEBuilderGBA.CLI backend with stateful sessions, JSON output, and an interactive REPL.

## Prerequisites

- .NET 10.0 SDK
- FEBuilderGBA.CLI built: `dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj`

## Installation

```bash
cd agent-harness && pip install -e .
```

## Command Groups

### `rom` — ROM information
```bash
cli-anything-febuildergba rom info roms/FE8U.gba          # Show ROM metadata
cli-anything-febuildergba rom validate roms/FE8U.gba       # Check if valid GBA ROM
cli-anything-febuildergba rom tables                        # List 40 supported data tables
```

### `data` — Struct data (40 tables)
```bash
cli-anything-febuildergba --rom roms/FE8U.gba data export units -o units.tsv
cli-anything-febuildergba --rom roms/FE8U.gba data export all -o data_prefix
cli-anything-febuildergba --rom roms/FE8U.gba data import units -i units.tsv
cli-anything-febuildergba --rom roms/FE8U.gba data roundtrip --table=all
cli-anything-febuildergba data inspect units.tsv
```

### `text` — Text export/import
```bash
cli-anything-febuildergba --rom roms/FE8U.gba text export -o texts.tsv
cli-anything-febuildergba --rom roms/FE8U.gba text import -i texts.tsv
cli-anything-febuildergba --rom roms/FE8U.gba text roundtrip
```

### `lint` — ROM validation
```bash
cli-anything-febuildergba --rom roms/FE8U.gba lint
```

### `patch` — UPS patches
```bash
cli-anything-febuildergba --rom modified.gba patch create -o patch.ups
cli-anything-febuildergba --rom clean.gba patch apply patch.ups
```

### `image` — Graphics
```bash
cli-anything-febuildergba image quantize -i input.png -o output.png
cli-anything-febuildergba image convert-map -i map.png --out-img tiles.png --out-tsa tiles.tsa
```

### `session` — State management
```bash
cli-anything-febuildergba session open roms/FE8U.gba
cli-anything-febuildergba session status
cli-anything-febuildergba session history
cli-anything-febuildergba session close
```

## Agent Usage

### JSON output
All commands support `--json` for machine-readable output:
```bash
cli-anything-febuildergba --json --rom roms/FE8U.gba data export units -o units.tsv
```

### Session-based workflow
```bash
# Open session (ROM remembered across commands)
cli-anything-febuildergba session open roms/FE8U.gba

# All subsequent commands use the session ROM
cli-anything-febuildergba --json data export units -o units.tsv
cli-anything-febuildergba --json lint
cli-anything-febuildergba --json text export -o texts.tsv
```

### Supported tables
`units`, `classes`, `items`, `portraits`, `sound_room`, `sound_boss_bgm`, `support_units`, `support_talks`, `support_attributes`, `event_haiku`, `event_battle_talk`, `event_force_sortie`, `worldmap_points`, `worldmap_paths`, `worldmap_bgm`, `map_settings`, `link_arena_deny`, `cc_branch`, `menu_definitions`, `item_weapon_triangle`, `map_exit_points`, `ai_map_settings`, `ai_perform_items`, `ai_perform_staff`, `ai_steal_items`, `ai_targets`, `generic_enemy_portraits`, `status_options`, `ed_retreat`, `ed_epithet`, `ed_epilogue_a`, `ed_epilogue_b`, `ed_epilogue_c`, `op_class_demo`, `op_class_font`, `op_prologue`, `class_alpha_names`, `summon_units`, `summons_demon_king`, `monster_probability`

### Error handling
Non-zero exit codes and `"error"` keys in JSON output indicate failures. Check `stderr` for details.
