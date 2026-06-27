README
===

[![MSBuild](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml)
[![E2E: No ROM](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-norom.yml)
[![E2E: FE6](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe6.yml)
[![E2E: FE7J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7j.yml)
[![E2E: FE7U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe7u.yml)
[![E2E: FE8J](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8j.yml)
[![E2E: FE8U](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/e2e-fe8u.yml)
[![GitHub Release](https://img.shields.io/github/v/release/laqieer/FEBuilderGBA)](https://github.com/laqieer/FEBuilderGBA/releases/latest)
[<img src="https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg" height="16" style="height: 16px; vertical-align: sub">Nightly Build](https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master)
[![codecov](https://codecov.io/gh/laqieer/FEBuilderGBA/branch/master/graph/badge.svg)](https://codecov.io/gh/laqieer/FEBuilderGBA)
[![Cross-Platform](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/crossplatform.yml)
[![Android](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android.yml)
[![Android Emulator Parity](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android-emulator-parity.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/android-emulator-parity.yml)
[![Release](https://github.com/laqieer/FEBuilderGBA/actions/workflows/release.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/release.yml)

Mirrors for Chinese mainland users (面向中国大陆用户的镜像发布地址): [![Gitee Release](https://gitee-badge.vercel.app/svg/release/laqieer/FEBuilderGBA?style=flat)](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) [![Gitee Go Build](https://gitee.com/laqieer/FEBuilderGBA/widgets/widget_5.svg)](https://gitee.com/laqieer/FEBuilderGBA/gitee_go/pipelines?tab=release)

## 🚀 Getting Started

### Project Structure

| Project | Target | Description |
|---------|--------|-------------|
| `FEBuilderGBA.Core` | net9.0 | Cross-platform core library: ROM manipulation, undo, LZ77, Huffman/text encoding, patch detection, translation, caching, git/archive, event ASM/disassembler, struct export, and ~100 other per-class seams. See [docs/CORE-SEAMS.md](docs/CORE-SEAMS.md) for the full catalog. |
| `FEBuilderGBA` | net9.0-windows | WinForms GUI application |
| `FEBuilderGBA.CLI` | net9.0 | Cross-platform command-line tool (50+ commands — UPS/patch, lint, rebuild, disasm, translate, struct/data export-import, portrait/MIDI/battle-anime/palette, decomp project mode, and more). Full reference: [docs/cli-reference.md](docs/cli-reference.md) · arg table: [docs/cli-args.md](docs/cli-args.md). |
| `FEBuilderGBA.SkiaSharp` | net9.0 | SkiaSharp `IImageService` (GBA 4bpp/8bpp tiles, palette conversion) + `SkiaFontRasterizer` (cross-platform GDI-parity glyph rendering for translation-font auto-generation). |
| `FEBuilderGBA.Avalonia` | net9.0 | Cross-platform Avalonia GUI: 356 editors (unit/item/class/map/event/AI/text/audio/graphics/portrait/world-map/support/arena/monster/summon/menu/credits) with read/write + undo, image PNG import, hex editor, pointer/free-space tools, cross-editor jump/pick navigation, and decomp-project mode. Full editor inventory: [docs/avalonia-forms.md](docs/avalonia-forms.md) · gap analysis: [docs/avalonia-gap-analysis.md](docs/avalonia-gap-analysis.md). |
| `FEBuilderGBA.Tests` | net9.0-windows | Unit and integration tests |
| `FEBuilderGBA.Core.Tests` | net9.0 | Cross-platform Core unit tests (Linux/macOS/Windows), including the SkiaSharp native-version guard and render byte-parity smoke tests. |
| `FEBuilderGBA.E2ETests` | net9.0-windows | End-to-end GUI/CLI tests |

### Cloning the Repository

This repository uses **git submodules** for patch management. Clone with:

```bash
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
```

Or if you already cloned without `--recursive`:

```bash
git submodule update --init --recursive
```

**Note:** The patch repository ([FEBuilderGBA-patch2](https://github.com/laqieer/FEBuilderGBA-patch2)) is maintained separately for independent versioning and faster updates.

**Bundled Tools:** [Event Assembler](https://github.com/laqieer/Event-Assembler) and [ColorzCore](https://github.com/FireEmblemUniverse/ColorzCore) are included as submodules in `tools/`. If no external EA path is configured, FEBuilderGBA automatically uses the bundled tools. To build them locally:
```bash
git submodule update --init tools/Event-Assembler tools/ColorzCore
# Windows:
dotnet build tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release
# Linux/macOS (produces a runnable executable in tools/bin/):
# Replace linux-x64 with your platform's RID (e.g. osx-arm64, osx-x64)
dotnet publish tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release -r linux-x64 --self-contained true -o tools/bin
```

**Runtime note:** All releases (WinForms, CLI, Avalonia) ship ColorzCore as a self-contained executable, requiring no additional .NET runtime.

**Public Resources:** [FE-Repo](https://github.com/Klokinator/FE-Repo) (graphics) and [FE-Repo-Music-No-Preview](https://github.com/laqieer/FE-Repo-Music-No-Preview) (music) ship as submodules under `resources/`. Run `git submodule update --init resources/FE-Repo` (and `resources/FE-Repo-Music-No-Preview` for music) before using the **FE-Repo** / **FE-Repo-Music** buttons, which are wired into the portrait, icon, background, CG, battle-background, skill-icon, and song editors (WinForms + Avalonia) to browse and import community assets directly. An uninitialized submodule shows an actionable *"run git submodule update"* message with a **Copy git command** button instead of an empty tree.

### Cross-Platform Build (Linux / macOS / Windows)

The Core library, CLI, SkiaSharp backend, and Avalonia GUI scaffold all target `net9.0` and build on any platform:

```bash
# Build Core library
dotnet build FEBuilderGBA.Core/FEBuilderGBA.Core.csproj

# Build cross-platform CLI
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj

# Run CLI
dotnet run --project FEBuilderGBA.CLI -- --version
dotnet run --project FEBuilderGBA.CLI -- --makeups=out.ups --rom=modified.gba --fromrom=original.gba
dotnet run --project FEBuilderGBA.CLI -- --applyups=output.gba --rom=original.gba --patch=patch.ups
dotnet run --project FEBuilderGBA.CLI -- --lint --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --disasm=output.asm --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --decreasecolor --rom=rom.gba --in=input.png --out=output.png --paletteno=0
dotnet run --project FEBuilderGBA.CLI -- --pointercalc --rom=source.gba --target=target.gba --address=0x1234
dotnet run --project FEBuilderGBA.CLI -- --rebuild --rom=modified.gba --fromrom=vanilla.gba
dotnet run --project FEBuilderGBA.CLI -- --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=1 --tosong=2
dotnet run --project FEBuilderGBA.CLI -- --convertmap1picture --rom=rom.gba --in=map.png
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate --rom=rom.gba --in=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --translate-roundtrip --rom=rom.gba --out=diff
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=units --out=units.tsv  # 40 tables: units, classes, items, portraits, sound_room, sound_boss_bgm, support_units, support_talks, support_attributes, event_haiku, event_battle_talk, event_force_sortie, worldmap_points, worldmap_paths, worldmap_bgm, map_settings, link_arena_deny, cc_branch, menu_definitions, item_weapon_triangle, map_exit_points, ai_map_settings, ai_perform_items, ai_perform_staff, ai_steal_items, ai_targets, generic_enemy_portraits, status_options, ed_retreat, ed_epithet, ed_epilogue_a, ed_epilogue_b, ed_epilogue_c, op_class_demo, op_class_font, op_prologue, class_alpha_names, summon_units, summons_demon_king, monster_probability
dotnet run --project FEBuilderGBA.CLI -- --export-data --rom=rom.gba --table=all --out=data
dotnet run --project FEBuilderGBA.CLI -- --import-data --rom=rom.gba --table=units --in=units.tsv
dotnet run --project FEBuilderGBA.CLI -- --data-roundtrip --rom=rom.gba --table=all
dotnet run --project FEBuilderGBA.CLI -- --lastrom
dotnet run --project FEBuilderGBA.CLI -- --force-detail
dotnet run --project FEBuilderGBA.CLI -- --translate_batch --rom=rom.gba --out=texts.tsv
dotnet run --project FEBuilderGBA.CLI -- --test --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --testonly --rom=rom.gba
dotnet run --project FEBuilderGBA.CLI -- --rom-info --rom=rom.gba
# --- Decomp project mode & asset export/import (preview) ---
# FEBuilderGBA can open a decompilation project and migrate edits back to source.
# Representative commands (see docs/cli-reference.md for the full set + every --kind):
dotnet run --project FEBuilderGBA.CLI -- --project=path/to/decomp --rom-info
dotnet run --project FEBuilderGBA.CLI -- --project=path/to/decomp --resolve-addr=0x801234
dotnet run --project FEBuilderGBA.CLI -- --migrate-diff --project=path/to/decomp --rom2=edited.gba
dotnet run --project FEBuilderGBA.CLI -- --write-source --project=path/to/decomp --table=units --id=1 --field=pow --value=7
dotnet run --project FEBuilderGBA.CLI -- --export-asset --project=path/to/decomp --kind=graphics --addr=0x... --out=src/
dotnet run --project FEBuilderGBA.CLI -- --decomp-audit --format=md   # round-trip coverage matrix
dotnet run --project FEBuilderGBA.CLI -- --build-project --project=path/to/decomp --reload --yes
# Full flag/--kind reference, exit codes, and source-owner rules: docs/cli-reference.md

# Build SkiaSharp image backend / Avalonia GUI
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Run the Avalonia GUI with a ROM (or open a decomp project preview)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba
dotnet run --project FEBuilderGBA.Avalonia -- --project path/to/decomp

# Headless smoke / data-verification / screenshot / image+palette round-trip modes
# (used by E2E + the cross-platform gap sweeps; see docs/avalonia-gaps/README.md)
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --smoke-test-all
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --data-verify
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --screenshot-all --screenshot-dir=./shots
dotnet run --project FEBuilderGBA.Avalonia -- --rom path/to/rom.gba --validate-image-roundtrip

# Avalonia ↔ WinForms gap sweeps (static analysis → docs/avalonia-gaps/)
# Use a dated output dir, e.g. docs/avalonia-gaps/YYYY-MM-DD
dotnet run --project FEBuilderGBA.Avalonia -- --gap-sweep-all --out=docs/avalonia-gaps/YYYY-MM-DD

# Cross-platform publish (self-contained) + tests
./scripts/publish-all.sh linux-x64 osx-arm64 win-x64
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
```

### Decomp Project Support (preview)

FEBuilderGBA can open a **decompilation project** directory (a source tree that
builds a `.gba` ROM, e.g. a `fireemblem8u` / `fe6` decomp). The built ROM is loaded
as a **read-only preview** — the source is the source of truth, so saving over the
built ROM is intentionally blocked (edit the source and rebuild instead). A project is
detected by a `febuilder.project.json` manifest or by Makefile/`*.sha1`/`ldscript.txt`
heuristics.

Open it via **File → *Open Decomp Project...*** (Avalonia) or `--project=<dir>` (CLI).
In decomp mode the suite can:

- **Resolve addresses to source symbols** (`--resolve-addr`, via the project `.map`/ELF/`.sym`/JSON).
- **Migrate edits back to source** with the advisory, read-only diff assistant (`--migrate-diff`).
- **Rewrite owning C/JSON table rows in place** (`--write-source` for items/units/classes/map_settings/support/shops) instead of mutating the preview ROM.
- **Export/import/round-trip/verify assets** (`--export-asset` / `--import-asset` / `--roundtrip-asset` / `--verify-asset`) across many `--kind`s (palette, graphics, map, mapchange, objtiles, mapchipconfig, portrait-package, …).
- **Build + reload** the project ROM (`--build-project`).

The exact flags, `--kind` set, exit codes, and source-owner/ROM-only rules are documented in
**[docs/cli-reference.md](docs/cli-reference.md)**; the full feature inventory (with the PRs that
landed each slice) is in **[docs/DECOMP-FEATURE-INVENTORY.md](docs/DECOMP-FEATURE-INVENTORY.md)**.
The maintained round-trip coverage matrix below shows how each editor/action migrates back to source.


#### Round-trip coverage matrix

The decomp round-trip coverage matrix below is generated by
`--decomp-audit --format=md` (single source of truth:
`DecompRoundTripAuditCore.BuildMatrix()`; a cross-project test asserts this block stays
byte-identical). It maps each editor/action to how its edit migrates back to source —
**SourceBackedWriter** (in-place C/JSON row rewrite), **SourceTreeExporter** (export an
asset + rebuild), **ImportPreviewOnly** (view only), **ManualMigration** (hand-edit
required for variable-length / pointer / raw-binary data), **RomOnlyUnsupported**.

<!-- decomp-audit-matrix:start -->

| Editor | Table | Action | Coverage | Notes |
| --- | --- | --- | --- | --- |
| Item Editor | items | Row save | SourceBackedWriter | Main structured-row save only |
| Unit Editor | units | Row save | SourceBackedWriter | Main structured-row save only (manifest alias: characters) |
| Class Editor | classes | Row save | SourceBackedWriter | Main structured-row save only |
| Map Settings Editor | map_settings | Row save | SourceBackedWriter | Main structured-row save only |
| Support Unit Editor | support_units | Row save | SourceBackedWriter | Main structured-row save only |
| Support Attribute Editor | support_attributes | Row save | SourceBackedWriter | Main structured-row save only |
| Support Talk Editor | support_talks | Row save | SourceBackedWriter | Main structured-row save only |
| Map Settings Editor | map_settings | Chapter pointer fields (EventDataPtr, difficulty) | ManualMigration | Pointer fields (D0/EventDataPtr, D96-D108 difficulty) are not source-backed |
| Palette Editor | palette | Palette export | SourceTreeExporter | JASC .pal export (faithful, lossless round-trip) |
| Graphics Editor | graphics | Graphics export | SourceTreeExporter | Indexed PNG (color type 3) + sidecar .pal |
| Portrait Editor | portrait | Portrait export | SourceTreeExporter | Export via --export-portrait-all (PNG package) |
| Portrait Editor | portrait_package | Portrait package import/round-trip | SourceTreeExporter | Source-tree write-back of a validated 128x112 composite sheet + name-matched JASC sidecar — import (--import-asset --kind=portrait-package, identity copy into an unambiguous project-confined owner; --overwrite an existing owner, ambiguous/multi-PNG destinations refused) + structural round-trip against an explicit baseline (--roundtrip-asset --kind=portrait-package --expect=<baselineDir>); reuses the #1350/#1353 portrait-package validator; never mutates the preview ROM. Source-level structure-exact identity vs a supplied baseline; NO ROM byte-pin (no canonical ROM->128x112-sheet builder exists, so the preview ROM is never the source of truth) |
| Icon Editor | icon | Icon export | SourceTreeExporter | Indexed PNG via graphics exporter (16x16 tiles) |
| Map Editor | map | Map layout export | SourceTreeExporter | .mar tilemap + sidecar .mar.json — export AND re-import/verify (lossless u16 layout body for raw entries < 0x2000, i.e. palette/flag bits 13-15 clear); compressed container re-derived by the build, not byte-pinned |
| Map Editor | map | Map layout import/verify | SourceTreeExporter | Re-import .mar to raw uncompressed tilemap blob + roundtrip-verify; never mutates the preview ROM |
| Map Editor | map_change_overlay | Map-change overlay import/verify | SourceTreeExporter | Raw uncompressed u16 overlay tile data block — export (--export-asset --kind=mapchange) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapchange) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact ROM compare; NOT the .mar layout and NOT the 12-byte change-record chain |
| Map Editor | map_tileanime2_palette | Map tile-animation-2 palette import/verify | SourceTreeExporter | Raw uncompressed u16 palette data block (count*2 bytes reached by each anime-2 entry's +0 pointer) — export (--export-asset --kind=mapanime2pal) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapanime2pal) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact ROM compare; NOT the anime2 entry/PLIST table and NOT LZ77 |
| Map Editor | map_obj_tileset | OBJ tileset import/verify | SourceTreeExporter | LZ77 OBJ tile block — export the DECOMPRESSED 4bpp payload (--export-asset --kind=objtiles) + import (--import-asset) + read-only decompress-and-byte-compare ROM verify (--verify-asset --kind=objtiles) + structural roundtrip; never mutates the preview ROM. Decompressed-payload equivalence, NOT compressed-stream byte identity (FEBuilder's LZ77 packer is non-canonical so the build re-compresses); --addr is the DEREFERENCED OBJ LZ77 stream address (FE7 obj2 secondary tileset is a separate stream/address, never concatenated). NOT chipset TSA/config, NOT tile animations 1/2 |
| Map Editor | map_chipset_config | Chipset TSA/config import/verify | SourceTreeExporter | LZ77 chipset TSA/config block — export the DECOMPRESSED config payload (--export-asset --kind=mapchipconfig) + import (--import-asset) + read-only decompress-and-byte-compare ROM verify (--verify-asset --kind=mapchipconfig) + structural roundtrip; never mutates the preview ROM. Decompressed-payload equivalence, NOT compressed-stream byte identity (FEBuilder's LZ77 packer is non-canonical so the build re-compresses); --addr is the DEREFERENCED config LZ77 stream address (e.g. the CONFIG-PLIST pointer dereferenced, NOT RomInfo.map_config_pointer; FE7 split layouts use a separate per-plist --addr). NOT the anime-1/anime-2 entry tables, NOT the map-change record chain, NOT the .mar layout |
| Map Editor | map_tileanime1_graphics | Map tile-animation-1 graphics import/verify | SourceTreeExporter | Raw uncompressed 4bpp graphics data block (the entry +2 byte length, reached by each anime-1 entry's +4 pointer — the inverse of anime-2's +0) — export (--export-asset --kind=mapanime1gfx --addr=<deref +4> --length=<+2>) + import (--import-asset) + byte-exact ROM verify (--verify-asset --kind=mapanime1gfx) + structural roundtrip; never mutates the preview ROM. Source-level structure-exact identity AND byte-exact RAW ROM compare; the WF read/import/rebuild paths treat this block as raw ImageToByte16Tile 4bpp bytes (a rebuild IMG block), NOT LZ77 (so NOT the objtiles/mapchipconfig decompress pattern). NOT the anime-1 ENTRY/PLIST table and NOT the .mar layout |
| Text Editor | text | Text export | SourceTreeExporter | texts.txt + textdefs.txt (migration format, not lossless macro round-trip) |
| Item Shop Editor | shops | Shop list save | ManualMigration | Decomp-mode GUI save now routes to SOURCE when the shop's ROM address resolves to a manifest u16-list owner (symbol-resolved) for BOTH literal raw-hex lists AND resolvable symbolic ITEM_* item-id-only lists (#1354) (#1347 Slice 5a); otherwise ROM-only/manual (variable-length ITEM_NONE-terminated lists via scattered hensei/worldmap/event-cond pointers; nonzero-quantity symbolic writes, unknown/ambiguous macros, and unresolved/unnamed shops degrade to --export-asset --kind=shop) |
| Item Shop Editor | shops | Shop list export | SourceTreeExporter | EA .event migration artifact via --export-asset --kind=shop; recreates each u16 ITEM_NONE-terminated list at its source address (migration aid, not source-backed in-place editing, not a byte-pinned round-trip) |
| Item Shop Editor | shops | Shop list source save | SourceBackedWriter | In-place source-backed rewrite of a u16 ITEM_NONE-terminated list (manifest list-owner: format=u16-list, symbol-resolved) via --write-shop; requires decomp-mode .map/.elf carrying the list symbol AND a manifest list-owner; degrades to --export-asset --kind=shop otherwise (#1347). Supports BOTH a LITERAL raw-hex list AND a SYMBOLIC ITEM_* (item-id-only, quantity 0) list whose macro names resolve from the constants header (owner.constantsHeader / artifacts.itemConstants / include/constants/items.h); a non-zero quantity or an id with no ITEM_* constant is an actionable refusal, not a clobber (#1354) |
| Map Editor | map_asset_binaries | Raw map asset save (GUI: anim/map-change record chain) | ManualMigration | GUI raw-ROM-save path for the remaining POINTER-HEAVY map STRUCTURAL tables: the tile-animation-1 ENTRY/PLIST table (8-byte entry rows + wait/length metadata; the per-entry raw 4bpp GRAPHICS block via the +4 pointer is now source-backed export/import/verify above), the anime-2 ENTRY/PLIST table (8-byte entry rows + wait/count/startIndex metadata; the per-entry raw PALETTE block via the +0 pointer is source-backed above) AND the 12-byte map-change RECORD chain (0xFF-terminated, width/height/flagID/PLIST metadata; the per-record raw overlay block via the +8 pointer is source-backed above) — NOT the map-change overlay tile data block NOR the anime-2 PALETTE block NOR the anime-1 GRAPHICS block NOR the OBJ tileset NOR the chipset TSA/config (all five source-backed export/import/verify above) and NOT the .mar tile layout. These ENTRY/PLIST/RECORD tables stay guarded (pointer-per-row/record, ambiguous source ownership — they need a manifest source owner not yet defined; sub-issues #1389/#1390/#1391 track each); migrate the dereferenced data blocks via --export-asset |
| Event Editor | chapter_event_pointers | Event/difficulty pointer fields | ManualMigration | Chapter pointer fields (EventDataPtr, difficulty pointers) are not source-backed |
| Battle Animation Editor | battle_anime | Animation view | ImportPreviewOnly | Preview-only in decomp mode; no source write-back (export via --export-battle-anime) |
| Song Table Editor | song_table | Song view | ImportPreviewOnly | Preview-only; song data edits must be made in source by hand |
| Magic Editor | magic_effects | Magic view | ImportPreviewOnly | Preview-only; magic effect edits are not source-backed yet |
| Hex Editor | raw_rom | Raw byte edit | RomOnlyUnsupported | Arbitrary ROM bytes; not representable as a clean source edit |
| Patch Manager | patches | Patch install/uninstall | RomOnlyUnsupported | ASM/binary patches apply to the built ROM; not a decomp source migration |

<!-- decomp-audit-matrix:end -->

Slice 1 (#1129) delivered open + preview; slice 2 (#1130) adds address-to-source
symbol resolution; slice 3 (#1131) adds the diff-to-source migration assistant;
the source-backed table writer is #1132, extended to JSON + units/classes + signed
fields + multi-field in #1141 (full-document JSON validation + width-aware signed no-op
hardening in #1145). Asset exporters (#1133) and in-app build/reload (#1134)
round out the suite.

`--decomp-audit --summary` prints the per-tier coverage counts (with an explicit
`Unclassified = N` line and the size of the maintained editor inventory). The matrix is
**complete relative to the maintained audit inventory** — a maintained classification, not
exhaustive byte-level runtime round-trip proof; full byte-level round-trip editing of every
format remains partial by design (#1150).

> **Decomp feature inventory & release status:** the full decomp project-mode feature set —
> and the PRs/commits that landed each slice — is enumerated in
> [docs/DECOMP-FEATURE-INVENTORY.md](docs/DECOMP-FEATURE-INVENTORY.md). These features
> currently live on **`master`, ahead of any tagged release** (the latest tag is
> `ver_20260204.22`; all decomp work landed after it), so they are not part of an existing
> released build yet. Build from `master` to use them.

### Running on Android (experimental)

Two distinct paths exist, both covered in detail in [docs/CROSS_PLATFORM.md → Running on Android](docs/CROSS_PLATFORM.md#running-on-android):

- **Emulation (Gamenative/Winlator)** — run the Windows *desktop* build under Wine + Box86/Box64. User-side, **experimental / unsupported / community-tested**; try the Avalonia `win-x64` build first.
- **Native Android app** — a separate port of the Avalonia GUI, tracked as exploration epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). Not shipped. See the evidence-backed feasibility assessment in [docs/ANDROID.md](docs/ANDROID.md) (Avalonia.Android lifetime, SkiaSharp native pin, SAF ROM access, `config/` bundling, the multi-window→single-activity gap) and the authored head skeleton in [`FEBuilderGBA.Android/`](FEBuilderGBA.Android/README.md).
  - **Stream-based ROM I/O for SAF (#1124)** — `ROM.LoadFromStream`/`SaveToStream` (+ async) share the byte-level seam with the existing path `Load`/`Save`, so the Avalonia head can open/save a ROM picked via `IStorageProvider` even when the SAF `content://` handle has no local filesystem path (it retains the `IStorageFile` and reads/writes through `OpenReadAsync`/`OpenWriteAsync`). Desktop path I/O is unchanged. The auto-save sidecar is redirected into app-private `{BaseDirectory}/autosave/` on Android (where the ROM's parent dir is not writable); the log and `config.xml` already resolve under `BaseDirectory` (`Context.FilesDir` on Android via #1123).
  - **Single-activity navigation model (#1122)** — the desktop multi-window editor model is reworked behind an `INavigationService` abstraction so the same `FEBuilderGBA.Avalonia/Services/WindowManager` API drives two backends. Desktop uses `DesktopNavigationService` (the original `.Show()`/`.ShowDialog()` multi-window behavior, **verbatim** — regression-safe), while Android uses `AndroidNavigationService`, a **single-view page/view-stack host with a back stack** built on a pure, desktop-unit-tested `NavigationStack` (modal-as-page, `PickFromEditor` result-await). `App` sets a `Views/MainView` shell under `ISingleViewApplicationLifetime` so the booted Android app presents the editor launcher. **Build-only validated** (no device): the nav-stack core is unit-tested, desktop nav is regression-verified behavior-identical, and the Android head builds to a `MainView`; the on-device runtime UX (touch + per-editor attached-`Window` dialogs/file pickers) is tracked under #1070.

### Architecture Diagram

```
FEBuilderGBA.sln
├── FEBuilderGBA.Core/           net9.0    (cross-platform core)
│   ├── IAppServices.cs                     Platform abstraction
│   ├── IImageService.cs                    Image service abstraction
│   ├── Rom.cs / ROMFE*.cs                  ROM manipulation
│   ├── UPSUtil.cs                          UPS patch creation
│   ├── FELintCore.cs                       Lint validation
│   ├── PathUtil.cs                         Cross-platform paths
│   ├── PointerCalcCore.cs                 Pointer search engine
│   ├── RebuildCore.cs                     ROM defragmentation
│   ├── SongExchangeCore.cs                Cross-ROM song transplant (InstrumentMap/Rip/Burn + sample recycle)
│   ├── MapConvertCore.cs                  Map tile conversion
│   ├── NameResolver.cs                    Entity name resolution with caching
│   ├── SongNameResolverCore.cs            Song name resolution (Sound Room name + SE-list fallback)
│   └── WriteValidator.cs                  ROM write validation utilities
├── FEBuilderGBA.CLI/            net9.0    (cross-platform CLI — 51 commands)
├── FEBuilderGBA.SkiaSharp/      net9.0    (image backend)
├── FEBuilderGBA.Avalonia/       net9.0    (cross-platform GUI — 325 editors, with ambient undo, dirty tracking, data export/import, full Options dialog with 20+ external tool paths)
├── FEBuilderGBA/                net9.0-windows (WinForms GUI)
├── FEBuilderGBA.Tests/          net9.0-windows (unit tests)
├── FEBuilderGBA.Core.Tests/     net9.0    (cross-platform tests)
└── FEBuilderGBA.E2ETests/       net9.0-windows (E2E tests)
```

## Testing & Coverage

- ✅ **2670 unit/integration tests** passing (1666 WinForms/Avalonia + 1004 Core cross-platform)
- ✅ **30 E2E tests** passing without ROMs (CLI + GUI automation + output log capture); **140 E2E tests** passing with all 5 ROMs (including 325-editor Avalonia smoke test, screenshot capture for both GUIs, + CLI output log capture for both CLI and WinForms executables)
- 📊 [View Full Coverage Report on Codecov](https://codecov.io/gh/laqieer/FEBuilderGBA)
- 🔍 Latest test results and coverage reports available as [GitHub Actions artifacts](https://github.com/laqieer/FEBuilderGBA/actions)
- 🧪 **Test Coverage:**
  - Unit tests for core utilities (RegexCache, LZ77, U, TextEscape, CoreState, Elf, SystemTextEncoderTBLEncode, MultiByteJPUtil, MyTranslateResource, EtcCacheResource, GitUtil, GitInstaller, AddrResult, ArchSevenZip, NewEventASM, ExportFunction, UpdateInfo, TranslateManager, DisassemblerTrumb, AsmMapSt, GbaBiosCall, R, Log, Mod, PatchDetection, FETextEncode, FETextDecode, TranslateCore, DecreaseColorCore sub-flags)
  - UpdateInfo version tracking and comparison
  - Core package download logic
  - Integration tests for update system
  - E2E CLI tests (`--version` flag, exit codes, output content, `--help` coverage)
  - CLI arg parsing tests (all 19 commands with complete argument sets)
  - E2E GUI tests (startup window detection, child controls, graceful shutdown)
  - ROM-based E2E CLI tests (`--lint`, `--makeups` × 5 ROMs, `--rebuild` × 2 representative ROMs — skipped without ROMs)
  - ROM-based E2E GUI tests (main form loads, title, child controls × 5 ROMs — skipped without ROMs)
  - Form smoke tests (all toolbar buttons × 5 ROMs — skipped without ROMs)
  - Avalonia editor smoke tests: Unit/Item editor selection (× 5 ROMs — skipped without ROMs)
  - Avalonia all-editors smoke test: all 325 GUI editors open/close (× 5 ROMs — skipped without ROMs)
- Avalonia data verification: `--data-verify` mode cross-checks ViewModel fields against raw ROM bytes, verifies NumericUpDown UI controls display values, validates text encoding (Shift-JIS for JP ROMs, ISO-8859-1 for US ROMs), and skips helper/context-only editors when they have no comparable ROM-backed record instead of reporting false mismatches (× 5 ROMs — skipped without ROMs). `--data-verify-full` mode iterates ALL list items per editor (not just the first) and performs per-field cross-checking via `GetFieldOffsetMap()` to verify each ViewModel field maps to the correct raw ROM byte offset, reporting `FIELDMISMATCH` lines for any discrepancy.
  - **Field completeness tests**: `AvaloniaFieldCompletenessTests` compares WinForms Designer.cs ROM data field controls against Avalonia ViewModel ROM access patterns across all 170 mapped forms (1562 WinForms fields, 0 gaps). Tests are **strict** — they fail on any gap, type/offset mismatch, or unmapped ROM-field form. Includes cross-checks: `AllFormFields_TypeAndOffsetMatch` verifies ROM read types match WinForms field types, `AllViewModels_ReportMethodsAreConsistent` verifies GetDataReport/GetRawRomReport key consistency, `MappedVMs_RawRomReport_CoversRomReads` enforces ≥60% raw ROM report coverage for all mapped VMs, `NoOrphanVMs_ImplementIDataVerifiable` prevents non-data-editor VMs from implementing IDataVerifiable, and `AllDesignerFilesWithRomFields_HaveAvaloniaMapping` auto-discovers ALL Designer.cs files with ROM fields to prevent new forms from being invisible to tests. Orphan cleanup removed IDataVerifiable from 49 non-editor VMs (dialogs, tools, infrastructure). Reports in `docs/field-completeness-report.txt`

## E2E Automation Tests

The project includes a dedicated end-to-end test suite (`FEBuilderGBA.E2ETests`) that covers both CLI and GUI behavior by launching the real application executable.

### Test Categories

| Test File | ROMs required | What it tests |
|-----------|--------------|--------------|
| `Tests/CliTests.cs` | No | CLI flag `--version`: exit code 0, output contains "FEBuilderGBA" and version info |
| `Tests/CliArgsE2ETests.cs` | No | All 18 CLI primary commands via `FEBuilderGBA.CLI`: `--help/-h`, `--version`, `--makeups`, `--applyups`, `--lint`, `--disasm`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--translate-roundtrip`, `--lastrom`, `--force-detail`, `--translate_batch`, `--test/--testonly` — 38 tests ([docs/cli-args.md](docs/cli-args.md)) |
| `Tests/GuiStartupTests.cs` | No | GUI startup: window appears within 30 s, has non-empty title, has child controls, responds to WM_CLOSE |
| `Tests/DiagnosticTests.cs` | No | Diagnostic: logs all window handles, titles (hex-encoded), and class names — always passes |
| `Tests/RomCliTests.cs` | Yes (×5/×2) | `--lint`, `--makeups` × 5 ROMs; `--rebuild` × 2 representative ROMs (FE8U, FE6) — 12 tests, skipped without ROMs |
| `Tests/RomGuiTests.cs` | Yes (×5) | Main form loads per ROM: window appears, non-empty title, ≥10 child controls — 15 tests, skipped without ROMs |
| `Tests/FormSmokeTests.cs` | Yes (×5) | All toolbar buttons clicked per ROM; verifies ≥1 opens a form — 5 tests, skipped without ROMs |
| `Tests/AvaloniaEditorSmokeTests.cs` | Yes (×5) | Avalonia: ROM load + Unit/Item editor selection per ROM — 10 tests, skipped without ROMs |
| `Tests/AvaloniaAllEditorsSmokeTests.cs` | Yes (×5) | Avalonia: all 325 GUI editors opened/closed per ROM via `--smoke-test-all` — 10 tests, skipped without ROMs ([docs/avalonia-gui-forms.md](docs/avalonia-gui-forms.md), [docs/avalonia-forms.md](docs/avalonia-forms.md)) |
| `Tests/CliOutputLogNoRomTests.cs` | No | New CLI output log capture: `--help`, `-h`, `--version`, `--force-detail`, `--test`, `--testonly`, no args, `--bogus-command` — 8 tests |
| `Tests/CliOutputLogRomPart1Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--lint` ×5, `--disasm` ×5, `--translate` ×5, `--rebuild` ×2 — 17 tests, skipped without ROMs |
| `Tests/CliOutputLogRomPart2Tests.cs` | Yes (×5/×2) | New CLI ROM output logs: `--makeups` ×5, `--applyups` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 11 tests, skipped without ROMs |
| `Tests/CliOutputLogImageTests.cs` | No | New CLI image output logs: `--decreasecolor` (5 flag variants), `--convertmap1picture` — 6 tests |
| `Tests/WinFormsCliOutputLogNoRomTests.cs` | No | WinForms CLI output log capture: `--version`, no args, `--bogus-command` — 3 tests |
| `Tests/WinFormsCliOutputLogRomTests.cs` | Yes (×5/×2) | WinForms CLI ROM output logs: `--lint` ×5, `--rebuild` ×2, `--makeups` ×5, `--disasm` ×2, `--translate` ×2, `--pointercalc` ×2, `--songexchange` ×2 — 20 tests, skipped without ROMs |
| `Tests/AvaloniaScreenshotTests.cs` | Yes (×2) | Avalonia: captures PNG screenshots of all 325 editors via `--screenshot-all` — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllTests.cs` | Yes (×2) | WinForms: screenshots of main form + all toolbar-openable editor forms — 4 tests, skipped without ROMs |
| `Tests/WinFormsScreenshotAllCliTests.cs` | Yes (×2) | WinForms: captures screenshots of all editors via `--screenshot-all` CLI flag — 4 tests, skipped without ROMs |
| `Tests/EditorImageComparisonTests.cs` | Yes (×1) | Cross-platform image export + pixel-perfect comparison for 16 editors: `--export-editor-images` on both WinForms and Avalonia — 3 tests, strict assertions, skipped without ROMs |

**Without ROMs:** 30 passed, 112 skipped. **With all 5 ROMs:** 142 passed, 0 skipped.

### Avalonia UI Automation Testing

All 361 Avalonia `.axaml` files (360 views + 1 dialog) have `AutomationProperties.AutomationId` attributes on every interactive control, enabling reliable UI automation testing with tools like Appium, FlaUI, or MCP Computer Use.

**3,132 unique AutomationIds** follow the naming convention `{EditorName}_{FieldName}_{ControlType}`:

| Suffix | Control Types |
|--------|--------------|
| `_Input` | TextBox, NumericUpDown, Slider |
| `_Combo` | ComboBox |
| `_Button` | Button, MenuItem |
| `_List` | ListBox, ListView, ItemsControl |
| `_Check` | CheckBox, ToggleButton, RadioButton, BitFlagPanel |
| `_Expander` | Expander |
| `_TabControl` / `_Tab` | TabControl, TabItem |
| `_Image` | Image, GbaImageControl, IconPreviewControl |
| `_Label` | TextBlock (dynamic/bound only) |

**Exempt files** (no AutomationIds — reusable controls instantiated multiple times):
- `Controls/BitFlagPanel.axaml`, `Controls/AddressListControl.axaml`, `Controls/GbaImageControl.axaml`, `Controls/IconPreviewControl.axaml`, `Controls/IdFieldControl.axaml`, `App.axaml`

**Scripts:**
- `scripts/add-automation-ids.ps1` — adds/refreshes AutomationIds across all .axaml files
- `scripts/validate-automation-ids.ps1` — validates coverage, naming, and uniqueness (exit 0 = pass, 1 = fail)

**Tests** (`FEBuilderGBA.Avalonia.Tests/AutomationIdTests.cs`):
- Per-editor assertions (UnitEditor, ClassEditor, ItemEditor, MessageBox)
- Naming convention compliance (>99% threshold)
- No duplicate IDs within any single view
- Minimum coverage threshold (>2000 IDs, >90% view coverage)
- Static .axaml source file checks (>95% files have IDs)
- Exempt file verification (reusable controls have no IDs)

### Running E2E Tests Locally

**Prerequisites:**  Build the main app first.

```bash
# Build the main application (Release, x86)
msbuild FEBuilderGBA.sln /p:Configuration=Release /p:Platform=x86 /t:build /restore

# Run without ROMs — 13 passed, 32 skipped (fast, ~20 s)
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build

# Run with ROMs — all 45 tests execute
ROMS_DIR=/path/to/roms dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

ROM files expected in `ROMS_DIR`: `FE6.gba`, `FE7J.gba`, `FE7U.gba`, `FE8J.gba`, `FE8U.gba`.

If `ROMS_DIR` is **not set at all**, `RomLocator` falls back to a `roms/` directory beside `FEBuilderGBA.sln` (useful during local development).  Set `ROMS_DIR=""` to explicitly suppress that fallback and force all ROM tests to skip.

Or point to an already-built binary:

```bash
export FEBUILDERGBA_EXE=/path/to/FEBuilderGBA.exe
ROMS_DIR="" dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

### CI/CD Integration

E2E tests are split into 6 parallel GitHub Actions workflows (`.github/workflows/e2e-*.yml`) — one no-ROM workflow and one per ROM variant (FE6, FE7J, FE7U, FE8J, FE8U). All share a reusable workflow (`e2e-run.yml`) and run in parallel, reducing wall-clock time from ~30 min to ~12 min. Each per-ROM workflow downloads `roms.zip` but keeps only its target ROM, so tests for other ROMs auto-skip.

ROM-based tests are gated on the `ROMS_URL` repository secret.  When the secret is present the workflow attempts to download `roms.zip`, validate it, extract it, and set `ROMS_DIR` for the test run.  When the secret is absent (forks, external PRs) the Download ROMs step is skipped entirely and all 35 ROM tests skip cleanly.

**ROM download — tiered failure policy:**
| Situation | Behaviour |
|-----------|-----------|
| `ROMS_URL` secret absent | Step skipped; ROM tests skip via `Assert.Skip()` |
| Network/HTTP error (unreachable URL) | Hard fail → pipeline blocked |
| Downloaded file not a valid zip (magic bytes ≠ `PK`) | Warning + exit 0; ROM tests skip |
| Zip structurally corrupt (`ZipFile::OpenRead` fails) | Warning + exit 0; ROM tests skip |
| Zip valid, all 5 ROMs extracted | All 45 tests run |

The step lists every zip entry with its uncompressed size before extraction, so the log shows exactly what is inside `roms.zip`.

**Artifacts produced:**
- `e2e-test-report` — TRX test report (viewable via the **E2E Test Results** check-run posted by `dorny/test-reporter`)
- `e2e-screenshots` — PNG screenshots of all GUI forms captured during E2E tests (Avalonia `Avalonia_*.png` + WinForms `WinForms_*.png`)
- `cli-output-logs` — `.log` files capturing stdout/stderr/exit code for every CLI command (both New CLI and WinForms CLI), useful for regression tracking

**Implementation notes:**
- Tests run sequentially (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) — each GUI test launches an exclusive app process; concurrent launches cause window-detection races
- Window detection polls **all process windows** via `EnumWindows` rather than relying on `Process.MainWindowHandle`, which can point to a transient splash/startup dialog before the main editor form appears
- Win32 `GetWindowText` P/Invoke uses `CharSet.Unicode` to correctly handle CJK characters; title-based detection is avoided for startup state (the app shows a Chinese "初始设置向导" Init Wizard on first run)
- CLI argument values must use `--key=value` (equals) syntax — `Program.ArgsDic` is built by `U.OptionMap` which only recognises the `=` separator (space-separated values are only picked up via a `File.Exists` fallback, which does not apply to output paths that don't yet exist)
- `AppRunner.Run()` calls `WaitForExit()` (no-param) after `WaitForExit(timeout)` to flush async `OutputDataReceived` events before reading captured stdout
- `RomLocator` treats any explicit `ROMS_DIR` value (even empty string) as an override — only when the variable is **absent** from the environment does the walk-up fallback activate

## 🔄 Update System

> **Cutting a release?** See the full-suite release runbook in **[docs/RELEASE.md](docs/RELEASE.md)** — it covers tagging (`ver_YYYYMMDD.NN`), the per-platform artifacts (WinForms / CLI / Avalonia / Android), the GitHub-release step, and the automatic Gitee mirror.

FEBuilderGBA uses a two-track update model that keeps the application and patch data independent:

### How It Works

| Component | What it contains | How it updates |
|-----------|-----------------|----------------|
| **Core** | FEBuilderGBA.exe, DLLs, config data | Download `FEBuilderGBA_YYYYMMDD.HH.zip` from GitHub Releases or nightly.link |
| **Patch2** | ~44,000 patch files in `config/patch2/` | `git fetch` + `git reset --hard` via the built-in Git updater |

When you check for updates the app compares the remote version against the local assembly build date and shows only the relevant update button(s).

> **Publishing a release (maintainers):** pushing a `ver_YYYYMMDD.HH` tag triggers
> [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds the WinForms desktop package,
> the per-RID self-contained CLI and Avalonia bundles, and the Android APK, then creates the GitHub Release with
> every platform package attached as a zipped asset. The release body is **auto-generated** from the
> conventional-commit history by [`scripts/generate-changelog.sh`](scripts/generate-changelog.sh) — grouped into
> Features / Bug Fixes / Documentation / CI / Maintenance sections (#1632); the full backlog log lives in
> [`CHANGELOG.md`](CHANGELOG.md). Publishing the release auto-fires
> [`sync-release-to-gitee.yml`](.github/workflows/sync-release-to-gitee.yml) so the Gitee mirror gets the full
> asset set. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#option-0-automated-tag-triggered-release-recommended) for details.

### Updating Patch2 via Git

Patch2 is a [git submodule](https://github.com/laqieer/FEBuilderGBA-patch2) updated independently of core releases.

- **In-app:** Tools → Check for Updates → "Gitでパッチデータを更新します"
- **Manual:** `cd config/patch2 && git pull`
- **First run:** The app detects missing patch2 directories and offers to clone them automatically. If Git is not installed, empty directories are created so the app still starts.

The app automatically selects the patch2 git source based on your **Options → Release Source** setting — the same setting that controls where the core update is downloaded from:

| Release Source setting | Patch2 git remote used |
|------------------------|------------------------|
| Auto (Chinese language detected) | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| Gitee | `gitee.com/laqieer/FEBuilderGBA-patch2` |
| GitHub / Nightly | `github.com/laqieer/FEBuilderGBA-patch2` |

### Benefits

- ✅ **Incremental patch updates** — only changed patch files are transferred via git
- ✅ **Faster patch updates** — no ZIP download or extraction required
- ✅ **Offline-friendly** — patch2 can be updated separately from the core app
- ✅ **Git history** — full audit trail of every patch data change

### Version Information

- **Core version:** Help → About
- **Patch2 version:** `git -C config/patch2 log -1 --format="%h %s"`

[This fork](https://github.com/laqieer/FEBuilderGBA/) is an integration of several forks of FEBuilderGBA and continues development based on it.

## MCP Computer Use (Windows)

An MCP (Model Context Protocol) server that gives Claude Code screenshot, mouse, and keyboard control for GUI testing. Windows-only, requires Python 3.10+.

### Setup

```bash
# Create venv and install dependencies
cd tools/mcp-computer-use
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt

# Verify server starts (Ctrl+C to stop)
.venv/Scripts/python server.py
```

The `.mcp.json` at the repo root auto-configures Claude Code to use the server as `febuildergba-computer-use`. After setup, its tools (screenshot, click, type_text, key_press, mouse_move, scroll, drag, get_screen_size, wait, find_window, focus_window) appear in Claude Code sessions opened from this repo.

README for Korean character table
===

It is from an [unofficial build](https://github.com/delvier/FEBuilderGBA) of FEBuilderGBA that supports Korean character table.

The character table used is **Johab**, only for the Hangul Syllables part. If you want to use another character table like Wansung or Windows-949, you may replace __FE\[678\].tbl__ in __./config/translate/ko_tbl__.

Since this fork is incomplete, there might be some issues that raw code points appear can be occurred, e.g. '@61A0' rather than '마' (0xA061) appears. This is likely because the upper bytes from 0xA0 to 0xDF are used for single-byte representation in Shift JIS and Windows-932.

You should change "Text Encoding in ROM" in Options manually every time the ROM is loaded.

Original README
===

FE_Builder_GBA
===
This is a ROM hacking suite for the Trilogy of Fire Emblem games for the Game Boy Advance.
The editor supports
 * FE6 (The Binding Blade)
 * FE7J/FE7U (The Blazing Blade)
 * FE8J/FE8U (The Sacred Stones)
Essentially, both Japanese and North American releases of all games (with the exception of FE6 being Japan-only) are supported.

Starting from the main screen, FEBuilder supports a wide range of functions from image displaying, importing and export of most data, map remodeling, table editing, community patch management, music insertion, and much more.

This suite was made at first to help make my Kaitou patch easier to create!

The origin of the name is from 某LAND.
However, the development language is C#. (We're in this together...)

Of course, it's open source.
The license of the source code is GPL3.
Please use it freely with no limitations.

Much of this project's functions are thanks to the data collected by various communities and people.
We would like to thank our hacking predecessors who have publicly shared any analyzed data.

Details (There is a commentary at the bottom of the page, and the wiki provides other instructions)
https://dw.ngmansion.xyz/doku.php?id=en:guide:febuildergba:index

### FE8 Skill Systems

Several FE8 editors — **Spell Menu Extensions**, the **Skill** editors, and **Effectiveness (Skill Systems Rework)** — only show data once a community **Skill System** is installed on the ROM. Recommended sources:

- **FE8U** (US/International): [FireEmblemUniverse/SkillSystem_FE8](https://github.com/FireEmblemUniverse/SkillSystem_FE8) (the canonical Event Assembler buildfile) or [MokhaLeee/fe8u-cskillsys-kernel](https://github.com/MokhaLeee/fe8u-cskillsys-kernel) (a modern C kernel).
- **FE8J** (Japan): [ngmansion/FE8N](https://github.com/ngmansion/FE8N) (the de facto standard FE8J hacking base).

These projects distribute **patches / source**, not the game — apply them to a clean FE8 ROM you dumped yourself. See the [Skill Systems (FE8) wiki page](https://github.com/laqieer/FEBuilderGBA/wiki/Skill-Systems) for installation details.

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus.
The source code is all available on github, so you can build it yourself if you are worried.


This software has no association with the official products.
We do not need any donations as we are making this software non-commercial.

If you really want to donate to someone, donate to the charitable organization supporting the freedom of speech on the Internet, **Freedom of Expression**, including the **EFF Electronic Frontier Foundation**.

Of course, you are free to write articles about FEBuilderGBA.
In some cases, you may earn some pocket money through affiliates. :)
However, please do it at your own risk. :(

If you have something you do not understand through hacking or the editor, please read "Manual" in "Help".
If you find a bug that you can not solve by any means, please create report.7z from 'File' -> 'Menu' -> 'Create Report Issue' and consult with the community.
https://discordapp.com/invite/Yzztqqa
Do NOT send your ROM (.gba) directly.

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
FE GBA 3部作のROMエディターです。
FE8J FE7J FE6 FE8U FE7U に対応しています。

Project_FE_GBA の画面を参考に、
新規に判明した部分を追加しました。
画像表示やインポートエクスポート、マップ改造まで幅広い機能をサポートします。

怪盗パッチを作っているときに思った、こんな機能が欲しい!!という機能をすべて入れ込みました。

名前の由来は、 某LANDのアレからです。
ただし、開発言語はC# です。 (中の人達は一緒だしね・・・)
C#でありますが、特にパフォーマンスに注意しているので、サクサク動くかと思います。

当然、オープンソース。ソースコードのライセンスは GPL3 です。
ご自由にご利用ください。

これを作るのに、いろいろいなデータ、コミニティを参考にしました。
解析したデータを公開してくれた先人にお礼を申し上げます。


詳細 (ページ下部に解説集があるよ)
https://dw.ngmansion.xyz/doku.php?id=guide:febuildergba:index

一部の出来の悪いアンチウイルスソフトが、FEBuilderGBAをウイルスと誤認することがあるようです。
これは、FEBuilderGBAがエミュレータと通信するためにWindowsDebugAPIを利用しているからだと思います。
もしそうなったら、アンチウイルスの設定で、FEBuilderGBAディレクトリを除外してください。
FEBuilderGBAはウイルスではありません。
ソースコードはすべてgithubで公開しているので、心配な場合は自分でビルドしてください。


このソフトウェアは、公式とは一切関係ありません。
私達は非営利でこのソフトウェアを作っているので、寄付を必要としません。
どうしても寄付したい方は、EFF 電子フロンティア財団を始めとする、インターネットでの言論の自由、表現の自由を支援している慈善団体にでも寄付してください。

もちろん、あなたがFEBuilderGBAに関する記事を書くのは自由です。
場合によっては、アフェリエイトでお小遣いを稼ぐこともできるでしょう。 :)
ただし、あなたの責任において実施してください。 :(

もし、hackromでわからないことがあれば、「ヘルプ」の「マニュアル」を読んでください。
どうしても解決しないバグが発生した場合は、「メニュー」の「ファイル」->「問題報告ツール」から、report.7zを作成して、コミニティに相談してください。
https://discordapp.com/invite/Yzztqqa
(ROMは送信しないでください。)

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
它是FE GBA三部曲的ROM编辑器。
它对应于 FE8J FE7J FE6 FE8U FE7U.

参考Project_FE_GBA的屏幕，
我添加了一个新发现的部分。
我们支持图像显示，导入导出，地图重构等功能。

当我制作一个kaitou补丁时，我想要这样的功能

这个名字的起源是来自 某LAND。
但是，开发语言是C＃。 （里面的人在一起...）
它是C＃，但我担心性能，所以我认为它会工作很好。

当然，开源。源代码的许可证是GPL3。
请自由使用。

我参考了各种数据和社区来做到这一点。
我要感谢发布分析数据的前辈。


详细信息（页面底部有评论）
https://dw.ngmansion.xyz/doku.php?id=zh:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus.
The source code is all available on github, so you can build it yourself if you are worried.


这个软件与官方无关。
我们不需要捐赠，因为我们正在制作该软件的非营利。
如果你真的想捐赠，
捐赠给支持言论自由的慈善组织，包括EFF电子前沿基金会在内的言论自由

当然，您可以自由撰写关于FEBuilderGBA的文章。
在某些情况下，您可以通过会员赚取零用钱。 :)
但是，请自行承担风险。 :(

如果你有一些你从hackrom不能理解的东西，请阅读“帮助”中的“手册”。
如果您发现无法解决的错误，请在'菜单'的'文件' -> '问题报告工具'中创建report.7z，并咨询社区。
https://discordapp.com/invite/Yzztqqa
（请不要发送ROM。）

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe
