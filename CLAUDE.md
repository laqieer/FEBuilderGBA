# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FEBuilderGBA is a comprehensive ROM hacking suite for Fire Emblem GBA trilogy games, written in C# WinForms targeting .NET 9.0. It supports editing five ROM variants: FE6 (Japan), FE7J/FE7U (Japan/US), and FE8J/FE8U (Japan/US).

### Solution Structure
```
FEBuilderGBA.sln
├── FEBuilderGBA.Core/        # net9.0 — Cross-platform core library (ROM, Undo, utilities)
├── FEBuilderGBA/             # net9.0-windows — WinForms GUI application
├── FEBuilderGBA.CLI/         # net9.0 — Cross-platform CLI (--version, --help, --makeups)
├── FEBuilderGBA.SkiaSharp/   # net9.0 — SkiaSharp IImageService implementation
├── FEBuilderGBA.Avalonia/    # net9.0 — Cross-platform Avalonia UI preview
├── FEBuilderGBA.Tests/       # net9.0-windows — Unit tests (1666 tests)
├── FEBuilderGBA.Core.Tests/  # net9.0 — Cross-platform Core tests (1004 tests)
└── FEBuilderGBA.E2ETests/    # net9.0-windows — End-to-end tests
```

**FEBuilderGBA.Core** contains platform-independent logic: ROM manipulation (`Rom.cs`, `ROMFE*.cs`), undo system (`Undo.cs`), utility functions (`U.cs`), logging (`Log.cs`), and shared state (`CoreState.cs`). It defines abstraction interfaces (`IAppServices`, `IEtcCache`, `ISystemTextEncoder`, `IAsmMapCache`) so Core code can call platform-specific services without depending on WinForms.

## Build & Development Commands

### Building

```bash
# Build Release version (x86)
msbuild /m /p:Configuration=Release /p:Platform=x86 /t:build /restore FEBuilderGBA.sln

# Build Debug version
msbuild /m /p:Configuration=Debug /p:Platform=x86 /t:build /restore FEBuilderGBA.sln

# Build for x64
msbuild /m /p:Configuration=Release /p:Platform=x64 /t:build /restore FEBuilderGBA.sln

# Build Core library only (cross-platform, no WinForms dependency)
dotnet build FEBuilderGBA.Core/FEBuilderGBA.Core.csproj

# Build cross-platform projects (CLI, SkiaSharp, Avalonia)
dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj
dotnet build FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj

# Run cross-platform tests
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
```

### Running

```bash
# Run the application (after building)
./FEBuilderGBA/bin/Release/FEBuilderGBA.exe

# Load a specific ROM
./FEBuilderGBA.exe --rom path/to/rom.gba

# Load last opened ROM
./FEBuilderGBA.exe --lastrom

# Force specific version detection
./FEBuilderGBA.exe --rom path/to/rom.gba --force-version=FE8U
```

### Command-Line Tools

```bash
# Run lint check on ROM
./FEBuilderGBA.exe --rom rom.gba --lint

# Rebuild ROM
./FEBuilderGBA.exe --rom rom.gba --rebuild

# Create UPS patch
./FEBuilderGBA.exe --rom rom.gba --makeups

# Disassemble code
./FEBuilderGBA.exe --rom rom.gba --disasm [params]

# Show version
./FEBuilderGBA.exe --version

# Validate text export/import round-trip (exit 0=lossless, 2=mismatches)
./FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba
./FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba --out=diff  # saves diff.export1.tsv + diff.export2.tsv

# Export struct data to TSV (40 tables: units, classes, items, portraits, sound_room,
# sound_boss_bgm, support_units, support_talks, support_attributes, event_haiku,
# event_battle_talk, event_force_sortie, worldmap_points, worldmap_paths, worldmap_bgm,
# map_settings, link_arena_deny, cc_branch, menu_definitions,
# item_weapon_triangle, map_exit_points, ai_map_settings, ai_perform_items,
# ai_perform_staff, ai_steal_items, ai_targets, generic_enemy_portraits, status_options,
# ed_retreat, ed_epithet, ed_epilogue_a, ed_epilogue_b, ed_epilogue_c,
# op_class_demo, op_class_font, op_prologue, class_alpha_names,
# summon_units, summons_demon_king, monster_probability)
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --out=units.tsv
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=all --out=data  # data.{table}.tsv per table
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=csv --out=units.csv
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=ea --out=units.ea

# Import struct data from TSV
./FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.tsv

# Validate struct data round-trip (exit 0=lossless, 2=mismatches)
./FEBuilderGBA.CLI --data-roundtrip --rom=rom.gba --table=all

# Render unit portrait to PNG
./FEBuilderGBA.CLI --render-portrait --rom=rom.gba --unit-id=1 --out=portrait.png

# Export all portraits to PNG files
./FEBuilderGBA.CLI --export-portrait-all --rom=rom.gba --out=portraits/

# Export song to MIDI
./FEBuilderGBA.CLI --export-midi --rom=rom.gba --song-id=0x1A --out=song.mid

# Import MIDI file into ROM song slot
./FEBuilderGBA.CLI --import-midi --rom=rom.gba --song-id=0x1A --in=song.mid

# Compile event script with EA/ColorzCore
./FEBuilderGBA.CLI --compile-event --rom=rom.gba --in=script.event --out=modified.gba

# Import battle animation from .txt script or FEditor .bin
./FEBuilderGBA.CLI --import-battle-anime --rom=rom.gba --animation-id=1 --in=anim.txt
./FEBuilderGBA.CLI --import-battle-anime --rom=rom.gba --animation-id=1 --in=anim.bin

# Export battle animation to .txt + PNG files
./FEBuilderGBA.CLI --export-battle-anime --rom=rom.gba --animation-id=1 --out=anim.txt

# Export battle animation as animated GIF
./FEBuilderGBA.CLI --export-battle-anime --rom=rom.gba --animation-id=1 --gif --out=anim.gif
./FEBuilderGBA.CLI --export-battle-anime --rom=rom.gba --animation-id=1 --gif --section=2 --out=crit.gif

# Scan ROM free space
./FEBuilderGBA.CLI --freespace --rom=rom.gba --min-size=256

# Hex dump ROM region
./FEBuilderGBA.CLI --hex-dump --rom=rom.gba --addr=0x1000 --length=512

# Search for text across all ROM text entries
./FEBuilderGBA.CLI --search-text --rom=rom.gba --query=Eirika

# Disassemble event scripts
./FEBuilderGBA.CLI --disasm-event --rom=rom.gba --out=events.txt

# Lint OAM sprites
./FEBuilderGBA.CLI --lint-oam --rom=rom.gba

# Apply binary patch
./FEBuilderGBA.CLI --apply-patch --rom=rom.gba --patch-file=patch.txt

# List patches and install status
# List patches and install status (with optional name filter)
./FEBuilderGBA.CLI --list-patches --rom=rom.gba
./FEBuilderGBA.CLI --list-patches --rom=rom.gba --patch-name=SkillSystem

# Uninstall binary patch
./FEBuilderGBA.CLI --uninstall-patch --rom=rom.gba --patch-file=patch.txt --original-rom=clean.gba

# List FE-Repo/music resources
./FEBuilderGBA.CLI --list-resources [--category="Battle Animations"]

# Expand data table
./FEBuilderGBA.CLI --expand-table --rom=rom.gba --pointer=0x8000000 --entry-size=28 --count=100

# Three-way ROM merge
./FEBuilderGBA.CLI --merge3 --rom=base.gba --target=modA.gba --in=modB.gba --out=merged.gba

# Resolve name IDs
./FEBuilderGBA.CLI --resolve-names --rom=rom.gba --kind=unit --ids=0,1,2,3

# Batch import portraits from directory
./FEBuilderGBA.CLI --import-portrait-all --rom=rom.gba --dir=portraits/

# Export all map/chapter settings to TSV
./FEBuilderGBA.CLI --export-map-settings --rom=rom.gba --out=maps.tsv

# LZ77 compress/decompress
./FEBuilderGBA.CLI --lz77 --compress --in=data.bin --out=compressed.bin
./FEBuilderGBA.CLI --lz77 --decompress --in=compressed.bin --out=data.bin

# Validate GBA ROM header checksum
./FEBuilderGBA.CLI --checksum --rom=rom.gba

# Fix corrupted GBA ROM header checksum
./FEBuilderGBA.CLI --repair-header --rom=rom.gba

# Compare two ROMs byte-by-byte
./FEBuilderGBA.CLI --diff --rom=original.gba --rom2=modified.gba
./FEBuilderGBA.CLI --diff --rom=original.gba --rom2=modified.gba --out=diff.tsv

# Print ROM metadata (version, title, size, CRC32, header checksum)
./FEBuilderGBA.CLI --rom-info --rom=rom.gba

# List all exportable struct table names
./FEBuilderGBA.CLI --list-tables

# Export GBA palette to file (.pal=JASC, .act=ACT, .gpl=GIMP, .txt=HexText)
./FEBuilderGBA.CLI --export-palette --rom=rom.gba --addr=0x5524 --out=palette.pal --colors=16

# Import palette file into ROM (format auto-detected)
./FEBuilderGBA.CLI --import-palette --rom=rom.gba --addr=0x5524 --in=palette.pal
```

### Dependencies

The application requires these runtime files (copied to output by MSBuild targets):
- `config/` directory - Contains all game data definitions, patches, translations (copied from repo-root `config/`)
- `7-zip32.dll` (optional) - Native archive handling for maximum speed (source: `FEBuilderGBA/lib/`)
- `tools/bin/` (optional) - Bundled EA/ColorzCore tools (built from submodules `tools/ColorzCore` and `tools/Event-Assembler`)

Archive handling:
- **If 7-zip32.dll exists**: Uses native DLL (very fast, no progress reporting)
- **If 7-zip32.dll missing**: Falls back to SharpCompress (pure .NET, slower but with progress)

Event Assembler / ColorzCore:
- **If user sets external path**: Uses configured path (takes priority)
- **If no path configured**: `ToolPathResolver` searches for bundled tools in `tools/bin/`, `tools/ColorzCore/`, and `tools/Event-Assembler/`
- **To build bundled tools locally (Windows)**: `git submodule update --init tools/Event-Assembler tools/ColorzCore && dotnet build tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release`
- **To build bundled tools locally (Linux/macOS)**: `git submodule update --init tools/Event-Assembler tools/ColorzCore && dotnet publish tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release -r linux-x64 --self-contained true -o tools/bin` (replace `linux-x64` with your RID)

MCP Computer Use server (optional, Windows-only):
- Located in `tools/mcp-computer-use/` — gives Claude Code screenshot + mouse/keyboard control for GUI testing
- Requires Python 3.10+ and a local venv: `cd tools/mcp-computer-use && python -m venv .venv && .venv/Scripts/pip install -r requirements.txt`
- `.mcp.json` at repo root auto-configures the server for Claude Code sessions
- Does NOT use the MCP Python SDK — implements a minimal JSON-RPC server for fast startup (< 1 s)

## Architecture Overview

### ROM Version System

The application uses **polymorphic ROM classes** to handle different game versions:

```
ROMFEINFO (Abstract base class - 450+ properties defining ROM data locations)
├── ROMFE6JP - Fire Emblem 6 (Binding Blade) Japanese
├── ROMFE7JP - Fire Emblem 7 (Blazing Blade) Japanese
├── ROMFE7U  - Fire Emblem 7 US/International
├── ROMFE8JP - Fire Emblem 8 (Sacred Stones) Japanese
├── ROMFE8U  - Fire Emblem 8 US/International
└── ROMFE0   - Special testing version
```

Each ROM class defines 450+ address pointers for game data locations (units, classes, items, maps, graphics, text, music, etc.). Version detection occurs via binary signature matching in ROM headers.

**Key Files (in FEBuilderGBA.Core/):**
- `Rom.cs` - Core ROM manipulation logic (ROMFEINFO base class + ROM class)
- `ROMFE6JP.cs`, `ROMFE7JP.cs`, `ROMFE7U.cs`, `ROMFE8JP.cs`, `ROMFE8U.cs` - Version-specific data
- `CoreState.cs` - Central static state holder (replaces Program.cs static fields for Core code)
- `Undo.cs` - Multi-level undo system
- `IAppServices.cs` - Abstraction for platform-dependent services (dialogs, etc.)
- `U.cs` - Pure utility functions (internal, used by Core types)

### Main Entry Point & Initialization

**File:** `Program.cs`

Startup sequence:
1. **Environment Setup** - Register encodings, load config from `config/config.xml`
2. **ROM Loading** - Detect and load ROM (supports `.gba`, `.ups`, `.7z`, `.rebuild`)
3. **Version-Specific Main Form** - Open appropriate main form (`MainFE6Form`, `MainFE7Form`, `MainFE8Form`, or `MainSimpleMenuForm` for easy mode)
4. **System Initialization** - Load text encoding (Huffman trees), event scripts, caches, patches/mods

### Core Data Access Pattern

All ROM data access goes through the `Program.ROM` static instance:

```csharp
// Reading
byte val = Program.ROM.u8(address);        // Read byte
ushort val = Program.ROM.u16(address);     // Read word
uint val = Program.ROM.u32(address);       // Read dword
uint ptr = Program.ROM.p32(address);       // Read pointer

// Writing (with undo support)
Program.ROM.SetU8(address, val, undo);
Program.ROM.SetU16(address, val, undo);
Program.ROM.SetU32(address, val, undo);

// Data extraction
byte[] data = Program.ROM.getBinaryData(offset, length);
string text = Program.ROM.getDecodedText(offset);
```

Pointers are automatically converted from GBA format (add 0x08000000 offset).

### Text Encoding System

Three-layer architecture for text handling:

1. **FETextEncode** (`FETextEncode.cs`) - Huffman compression/decompression for game text
2. **SystemTextEncoder** (`SystemTextEncoder.cs`) - OS encoding (Shift-JIS, UTF-8, etc.) via TBL files
3. **TextEscape** (`TextEscape.cs`) - Special escape sequences (color codes, pauses, etc.)

Flow: Human text → SystemTextEncoder → FETextEncode (Huffman) → ROM binary

### Event Script System

**File:** `EventScript.cs`

Manages three script types:
- **Event** - Main story/gameplay events
- **Procs** - Process/animation scripts
- **AI** - Enemy AI behaviors

Supports 100+ argument types (`ArgType` enum) for parsing event parameters:
- Map coordinates (MAPX, MAPY)
- Units, Classes, Items, Skills
- Pointers (code, data, events)
- Text IDs, Flags, Music IDs
- Composite types

Event editing forms: `EventScriptForm`, `EventScriptInnerControl`, `ProcsScriptForm`, `AIScriptForm`

### Undo System

**File:** `Undo.cs`

Multi-level undo buffer that records:
- Modified address ranges
- Binary deltas (before/after bytes)
- File size changes
- Named snapshots

Always pass undo object to write operations:
```csharp
Undo undo = new Undo();
Program.ROM.SetU32(address, value, undo);
// Can rollback with undo.Rollback()
```

### Form Navigation Pattern

**File:** `InputFormRef.cs`

Central "service locator" for form management:

```csharp
// Open form with jump to specific data address
InputFormRef.JumpForm<UnitForm>(dataAddress);

// Open modal dialog
InputFormRef.JumpFormLow<OptionForm>();

// Auto-wire control events by naming convention
InputFormRef.MakeLinkEvent(this);
```

**Convention-based linking:** Controls named `L_{id}_{linktype}_{args}` automatically get event handlers wired.

**Avalonia equivalent** (`FEBuilderGBA.Avalonia/Services/WindowManager.cs`): `WindowManager.Navigate<TView>(addr)` opens an editor positioned at an address; `WindowManager.PickFromEditor<TView>(addr, owner)` opens an editor in pick mode and awaits the user's selection. `WindowManager` is a stable facade over `INavigationService` (#1122): `DesktopNavigationService` = today's multi-window behavior (verbatim); `AndroidNavigationService` = single-view view-stack host (pure `NavigationStack`; `Open<T>` content-detaches the view Window into a page; modal-as-page; pick-await) via `OperatingSystem.IsAndroid()`; `App` sets `Views/MainView` under `ISingleViewApplicationLifetime` (see docs/ANDROID.md §2). For type-ID fields, `Controls/IdFieldControl` (#366) bundles label + NumericUpDown + name preview + Jump + Pick.

### Patch & Mod System

**Files:** `PatchForm.cs`, `Mod.cs`

Two extension mechanisms:

1. **Patches** - Functional ROM modifications
   - Located in `config/patch2/{VERSION}/`
   - Binary/ASM code modifications
   - Can have parameters
   - Installation status tracked

2. **MODs** - UI/interface customizations
   - `MOD_*.txt` files
   - Applied per-form
   - Lighter weight than patches

### Graphics System

**Files:** `ImageUtil*.cs` family

Specialized utilities for different graphic types:
- `ImageUtil.cs` - Core image loading/saving
- `ImageUtilOAM.cs` - Sprite/OAM handling
- `ImageUtilMap.cs` - Map tiles
- `ImageUtilMagic.cs` - Magic effects
- `LZ77.cs` - GBA LZ77 compression (LZSS variant)
- `LZ77ToolCore.cs` (Core) — LZ77 Tool Move + Recompress tabs (WinForms `ToolLZ77Form` + Avalonia
  `ToolLZ77ViewModel`). Ambient undo via `ROM.BeginUndoScope()`; LDR-first / raw-fallback pointer search
  (event-aware path out of scope — `MissingEventAwareCoverage` always set).
- `ImageBattleScreenCore` — `EncodeTSAKeep` (Core, PURE) + `ImportBattleScreenBulk` (Core, ROM-MUTATING):
  BULK whole-screen import for the Battle Screen Layout editor (#988). `EncodeTSAKeep` keeps the TSA + applies
  the INVERSE per-cell flip; `ImportBattleScreenBulk` validate-all-before-mutate (5 strips LZ77-written +
  repointed, then palette, ambient undo, no partial commit). **Palette = SAFE single bank** (`BULK_MAX_COLORS=16`;
  >16-color REJECTED; merges into bank 0, banks 1–15 kept). #989.
- `SkillSystemsAnimeExportCore.cs` (Core, READ-ONLY) — SkillSystems skill-anime EXPORT: `SkipCode` (FE8J
  direct; FE8U skips `.dmp` + flags defender), `ExportSkillAnimation` (frames to `0xFFFF`, LZ77-decompresses
  OBJ+TSA), `BuildScriptLines`, `GetFrameImage` (#1010). Avalonia `SkillConfigAnimeExportHelper` → `.txt`+PNG/GIF
  (FE8N Ver1 stub); `SkillConfigAnimePreview` per-frame preview on the 4 SkillConfig editors. #910/#912/#1010.
- `SkillSystemsAnimeImportCore.cs` (Core, ROM-MUTATING) — SkillSystems skill-anime IMPORT (FE8J #916 / FE8U #917).
  `ParseScript` reads `D`/`S{hex}`/`{wait} {png}`; `ImportSkillAnimation` validate-all-before-mutate, palette RAW
  0x20, forces 240×160, repoints LAST under one undo scope, byte-identical restore. FE8U prepends `.dmp` template
  (`FE8USkillTemplate`). Old-region recycle (`EnumerateOldAnimeRegions` #914); ≥2-owner region never recycled (#929).
- `SkillConfigSkillSystemBulkExportCore.cs` (Core, READ-ONLY) — `ExportAll` bulk-exports the
  SkillSystems skill config to `*.SkillConfig.tsv` (`textID<TAB>animePtr` rows) + per-skill anime
  dirs; `writeAnime` delegate keeps Core GUI-free. #920.
- `SkillConfigSkillSystemBulkImportCore.cs` (Core, ROM-MUTATING, BULK-ATOMIC) — `ImportAll` re-imports every
  skill as **ONE atomic transaction**: all commit (one undo record) or byte-identical restore. Length-aware
  restore + return-value fault detection + one shared `ROM.BeginUndoScope` (per-skill `manageSnapshot:false`);
  validate-all-before-mutate; cross-slot recycle of NON-shared regions only (#929). #923/#885.
- `MapPListResolverCore.cs` (Core, READ-ONLY) — map-PLIST label resolver (`MAP Ch1`, `MAPCHANGE Ch5`,
  `ANIME1/2`, `OBJ`, `NULL`, `-EMPTY-`, `UNK`) for the MapPointer/MapChange/MapTileAnimation editors,
  via `PLists` + `ResolveLabel`. Extends (does NOT fork) `MapChangeCore.PlistType`; per-call LOCAL cache. Drives
  the anime1 filter (`MapTileAnimation1Core.BuildPlistList`/`ScanEntries`) + anime2 — **anime1 schema is the
  inverse of anime2** (`imagePointer` at `+4`, not `+0`). #952/#955/#957.
- `MapRenderCore.cs` (Core, READ-ONLY) — chapter-map + change-map overlay renderer (ports
  `ImageUtilMap.DrawMap`/`DrawChangeMap`). `RenderMapImage`/`RenderChangeMap` take an optional `obj2Offset` for
  the **FE7 obj2 dual-tileset split**: `obj_plist` high byte selects a secondary tileset whose LZ77 bytes are
  concatenated AFTER the primary OBJ bytes (WF order). FE6/8 unchanged. #961.
- `MapStyleEditorViewModel.TryImportObjImage` (Avalonia, ROM-MUTATING) — Map Style Editor OBJ import
  with the FE7 obj2 split: tile sheet split by BYTE length → primary OBJECT + obj2 PLISTs, each
  LZ77-compressed and written under ONE undo scope (a failed 2nd write rolls both back). #976.
- `EventMapChangeViewModel.ImportChangeDataFromPointer` (Avalonia, ROM-MUTATING) — Map Change Event
  pointer-import: append a COPY of `B3×B4×2` source u16 bytes to free space + repoint P8
  (append+repoint, never overwrites in place). All writes through the ambient undo overload. #961.
- `TranslateTextUtilCore.cs` (Core, READ-ONLY, NETWORK-OPTIONAL) — Text Editor Translate tab:
  `SplitEscapeSegments` (lossless literal/`@XXXX` split; `@` is a code boundary only before exactly
  4 hex digits, so `email@x` stays literal), `LoadFixedDic` (shipped glossary; only successful loads
  cached), `TranslateText` (code segments kept VERBATIM, glossary-first, injectable translator so
  tests run offline). #949/#967/#971.
- `UnitPaletteClassResolverCore.cs` (Core, READ-ONLY) — `ResolveDefaultPreviewClass(rom, slotIndex)`
  returns the first class using a unit-palette slot so the Unit Palette Editor seeds Battle Animation
  + preview (FE8 scans dedicated tables; FE6/7 read per-unit palette ids + a `GetHighClass` port).
  `FindFirstClassWithAnime` is the fallback. Pure, guards every address, never throws. #985.
- `WaitIconRenderCore.cs` (Core, READ-ONLY) — Unit Wait Icon decode + crop (#991), extracted verbatim
  from `PreviewIconHelper.LoadClassWaitIcon` (now a thin wrapper → single source of truth).
  `RenderFrame`/`RenderFullSheet`/`RenderClassWaitIcon` crop per animType (0/1/2; the #342/#667 Y=8
  offset for type 1); `GetPaletteColors` maps 0..6 self/npc/enemy/gray/four/lightrune/sepia. Reads
  palettes via the rom-aware `ImageUtilCore.GetPalette(rom, …)` overload (never the ambient ROM) (#993).
- `WaitIconImportCore.cs` (Core, ROM-MUTATING) — static PNG/BMP wait-icon sheet import (#991).
  `Import` validates dims → animType (16x48→0 / 16x96→1 / 32x96→2, else NO mutation), encodes, LZ77-
  writes + repoints `+4`, writes b2 @ `+2`, ambient undo, byte-identical fault restore (#885/#923). Class
  back-refs in `ClassFormCore` (`GetClassIdWhereWaitIconId`, `GetClassMoveIcon` move-icon id 1-BASED #993).
- `EventScriptReferenceScanner.cs` (Core, READ-ONLY) — generic event-script cross-reference scanner
  (#990; ports `EventCondForm.MakeEventScriptPointer` + FE7 tutorial table). `EnumerateEventEntries`
  yields every event entry point with verified per-cond geometry (TURN/TALK/OBJECT/ALWAYS/TUTORIAL/
  START/END, shop/chest skipped); `FindAllArgReferences(rom, argType, keepZeroId)` GATES on
  `CoreState.EventScript`/`CommentCache` wired AND `ReferenceEquals(CoreState.ROM, rom)`, disassembles
  each entry, buckets refs by id, dedups by script-start. (Text-id path's patch+ASM/MAP refs closed by #1027.)
- `BGReferenceFinder.cs` (Core, READ-ONLY) — thin `ArgType.BG` wrapper over
  `EventScriptReferenceScanner.FindAllArgReferences` with a per-ROM-instance cache (#990; Background Image
  editor References list). **Cache-poisoning guard (#992):** returns empty WITHOUT caching when scanner
  prerequisites unsatisfied, so an early/headless call can't pin a permanently-empty cache.
- `BattleAnimeRendererCore.CountAnimationPaletteBanks`/`MaxOamPaletteBank` (Core, READ-ONLY) — 32-color
  banner detector (#1033) replacing WF `GetPalette16Count`: scans all sections/frames' OAM for the max
  non-affine, non-bug (`bank<4`) 16-color bank → `max+1` (≥2 ⇒ banner); CONSERVATIVE; safe default 1.
- `ClassOPDemoFontRenderCore.cs` (Core, R/O) — Class OP Demo N1 JP-name font-glyph preview (#1032; ports WF
  `OPClassFontForm.DrawFontByID`). `RenderGlyphById(rom,id)` bounded-scans the 4-byte-pointer table at
  `op_class_font_pointer` → `RenderGlyphImage` LZ77-decompresses the 4bpp glyph → 32×32. Avalonia `ClassOPDemoView`.
- `OPClassFontImportCore.cs` (Core, ROM-MUTATING) — OP Class Font glyph PNG import (#999; wait-icon pattern).
  `Import` validates dims (%8), `EncodeDirectTiles4bpp` + LZ77-writes + repoints the D0 glyph pointer (ambient
  undo, byte-identical fault restore #885/#923). Avalonia `OPClassFontViewerView.ImportPng_Click` remaps onto palette.
- `FontGlyphRenderCore`/`FontBulk{Export,Import}Core` (Core) — main game-font glyph editor (#1165; WF `FontForm`):
  `EnumerateGlyphs`/`RenderGlyph` (2bpp-linear 16x16, fixed 4-color palette, NOT 4bpp-tiled) + `ImportGlyph`
  (find-or-append, repoint, ambient undo, byte-identical restore) + atomic `.fontall.txt` bulk. Avalonia
  `FontEditorView` glyph-grid icons; `.ttf`/`.otf` auto-gen deferred.
- `ImageWorldMapCore.ImportIconStrip`/`TryGetStripPalette` (Core, ROM-MUTATING / READ-ONLY) — World Map
  Mini/Point1/Point2/Road single-LZ77 strip imports (#1000; wait-icon pattern): validate dims (%8), encode +
  LZ77-write + repoint the ONE image pointer; palette NOT written; fault-restore. Avalonia `WorldMapImageView`
  4 handlers + FE8-only `CanImport{strip}` gates.
- `WorldMapEventResolverCore.GetEventByMapID(rom,mapid,isSelect)` (Core, READ-ONLY) — version-branched world-map-event resolver for the Avalonia Export-EA-Event tool (#1420; ports WF `ToolExportEAEventForm` dispatch): FE8 indexes stageclear/stageselect tables; FE7 = WMAP-plist byte→stageselect table (stageclear=0x0); FE6→`MapChangeCore.PlistToOffsetAddr(WORLDMAP_FE6ONLY)`. FE6/FE7 select→NOT_FOUND; full-slot guards.
- `WorldMapPathCore.ExportPathBinFromRom`/`DecodePathBin` (Core, R/O, #1458; WF `WorldMapPathEditorForm` SaveAS/Load) — `.road.bin` file I/O for the Avalonia Road (Path) editor: Save exports the RAW packed stream via `RebuildProducerCore.CalcPathDataLength`+`getBinaryData` (NOT a PackPath re-emit, so non-canonical streams round-trip byte-for-byte); Load `DecodePathBin` buffer-decodes (all-or-nothing hardening vs WF permissive `LoadPathLow`)→VM `ImportPathBin` replaces `Chips`+MarkDirty, NO ROM undo (buffer replace; Write stays the undo-tracked commit). FE8-only; `WorldMapPathEditorView` Save/Load `.road.bin` buttons via `FileDialogHelper`.
- `StructExportCore.FormatSTRUCT`/`FormatNMM` (Core, READ-ONLY, PURE) — Struct Dump Selector STRUCT (.h
  C-header) + NMM (No$gba memory map) export over `StructMetadata.StructDef`; Avalonia
  `DumpStructSelectDialogViewModel.MakeExportText` routes STRUCT/NMM (+CSV/TSV/EA), hex stub for unresolved. #1012.
- `BattleAnimeRendererCore.RenderSampleBattleAnime` optional EXACT-32-byte `overridePaletteBlock` (Core, READ-ONLY)
  — live-recolor the Unit Palette editor sample preview from R/G/B spinners: Avalonia
  `ImageUnitPaletteView` packs 16 spinners (`UnitPaletteWriteCore.PackRgb555`) → `RenderClassSamplePreview(...,
  editedBlock)` ONLY when `PaletteTypeIndex==EditableBlockIndex`. #1022.
- `SongTrackChangeCore.ApplyTrackChange` (ROM-MUTATING) + `SongMidiCore.ParseSingleTrackFromDataOffset` (R/O) —
  single-track Track Change writer (#1002 Slice 1; ports WF `SongUtil.ChangeTrackAndWrite`): voice(0xBD) remap +
  VOL/PAN/TEMPO clamp + gated note-velocity; validate-before-mutate, ambient undo, byte-identical fault restore.
  Avalonia `SongTrackChangeTrackView`/`SongTrackAllChangeTrackView`. Slice 3 = `SongExchangeCore.ConvertSong`
  cross-ROM transplant (Instrument Rip/Burn, sample recycle, NO ROM growth); `SongExchangeView` + CLI `--songexchange`.
- `NameResolver.GetFaceTranslateInfo` + `ToolTranslateROMCore.AppendAIHintMessage` (Core, R/O) — Text Editor AI Hints export (#1028): `GetTranslateInfoByFaceID` (unit-table-by-face-at-+6) + `AppendAIHintMessage` (escape forms, dedup, mob fallback); Avalonia `ExportAllTexts`
- `PatchDetection.SearchAntiHuffmanPatch(rom)` (Core, R/O) — Text Editor bad-char popup (#1028 Slice D): 6 un-Huffman sigs; `PatchDetectionService.DetectAntiHuffman`. `TextViewerViewModel.WriteText` = encode-fail→`TextBadCharPopupView`→ABORT via `EncodeAbortedException`.
- `ExportFilterCore.BuildFilteredTextIds(rom,idx)` (Core, R/O) — Text Editor Export Filter (#1028): `InitExportFilter` 11 cats (0=All→null); BattleTalk/Haiku event-ptr-when-0; Skill via `SkillSystemTextScanner`; EventCond via `EventScriptReferenceScanner.CollectEventCondTextIds`.
- `MakeVarsIDArrayCore.BuildAllUsedRefs/BuildFreeAreaUsedSet` + `TextFreeAreaCore.FindUnreferencedTextIds` (Core, R/O) — DEFINITIVE Text Editor free-area + cross-ref (#1027; faithful `U.MakeVarsIDArray`): typed TEXTID∪SONG union over Unit/Class/Item/EventCond + collectors, reusing ExportFilterCore; `AsmMapTextSymbolReader` + `PatchTextRefScannerCore` + `ITextIDCache`. PREREQ-GUARD when EventScript/CommentCache unwired or ROM≠active. Avalonia `FindUnreferencedTexts`/`FindCrossReferences`.
- `ToolAnimationCreatorViewViewModel.InitFromMagicRom`/`InitFromSkillRom` (Avalonia, R/O) — Animation-Creator jump seeds: magic #996 (FEditor/CSA 0x86 via `MagicEffectExportCore`) + skill #1115 (`ExportSkillAnimation`; `CountSkillFrames`; `SkillConfigAnimeJumpHelper` 4 variants, Ver1 render-only). MapAction write-back only.
- `PatchMacroAddressResolverCore.cs` (Core, R/O) — `$GREP`/`$XGREP`/`$FGREP`/`$P32`/`$TEXTID` resolver; adds `GrepPatternMatchEnd/Begin` to `U.cs`; wires `PatchTextRefScannerCore` so grep-resolved TEXT/SONG/EVENT refs reach the free-area union. #1027.
- `DecompProject.cs` (Core, READ-ONLY, never-throws) — decomp open mode (#1129): `Detect`+`ResolveBuiltRom`; `IsDecompMode`; CLI `--project`. #1130 `DecompSymbolResolver` (`--resolve-addr`); #1131 `DecompDiffMigrationCore` (`--migrate-diff`); #1133 `DecompAssetExportCore`+`IndexedPngWriter` (`--export-asset`; #1148 `ImportMap`/`--import-asset`/`--roundtrip-asset`: `.mar`→blob). #1132/#1141/#1148 `DecompSourceWriterCore` C+JSON (items/units/classes/map_settings); chapter scalars source-backed; pointers+raw map ASSETs ROM-only/manual (`DecompMapAssetGuard`). #1150 `DecompRoundTripAuditCore` (`--decomp-audit`)+`NmmSchemaBridgeCore` (`--nmm-to-manifest`/`--manifest-to-nmm`)+`IndexedPngReader`/`DecompAssetValidatorCore` (`--validate-asset`)
- `AoeRangeCore.cs` (Core, #1431; WF `AOERANGEForm`) — `ReadAoeRange`/`WriteAoeRange` (4+w*h; in-place else append+`RepointAllReferences`+ZERO old; orphan-refuse; ambient undo); Avalonia `AOERANGEView` manual-addr + dynamic decimal-cell grid (NumericUpDown `FormatString="0"` — Avalonia hex formats throw); NoList.
- `CStringCore.cs` (Core, #1445; WF `CStringForm`) — `ReadCString` (R/O; `TextForm.Direct` pointer decode = `getString`+`RevConvertSPMoji`+`@001F`-strip+`ConvertEscapeText`; unsafe⇒"") + `OldRegionLength` (`Padding2(len+1)`, EOF tail-clamp like `WriteBinaryData`) + `WriteCString` (encode raw `SystemTextEncoder.Encode`+NUL, in-place else `AoeRangeCore.Move` skeleton: early-orphan-guard→`RecycleAddress.WriteAmbient`+`RepointAllReferences`+parent-slot+ZERO old; ambient undo). Avalonia `CStringView` manual-addr + editable TextBox + Write; NoList.
- `EventScriptEditorCore.cs` (Core, #1435; WF `EventScriptInnerControl`) — editable `List<OneCode>` engine: `BuildFromRom`/`ScanLength` (es-driven, not `SearchEveneLength`), `Insert`(TERM-before, clone)/`Delete`/`Move{Up,Down}`/`InsertRange`(clone,skip-null)/`SetCodes`(clone,skip-null)/`NewCodeFromScript`(`CloneScriptDefaultByte`)/`ImportFromText`(guard null decode)/`Serialize`(auto-term, MAPTERM always-terminal)/`WriteAll` (`isSafetyOffset`+ALIGN4 gate src+dest; originalSize clamped to EOF; in-place else `FindFreeSpace`+`RepointAllReferences`+`NotifyChangePointer`, one ambient undo, **refs==0 REFUSAL**, byte-identical restore). Avalonia `EventScriptViewModel`/`EventScriptView` toolbar; event-kind ONE-SHOT via `StageEventKind` (no stale-leak in cached view); `WriteAll` via `CommitExternal` (no nested scope). Script-type agnostic; Procs/AI view-wiring deferred.
- `EventMoveDataFE7Core.cs` (Core, R/O, #1440; WF `EventMoveDataFE7Form`) — FE7 move-command walk: `IsEnableData`/`IsAppendedData` (WF-typo alias `IsAppnedData`)/`Stride` (9/0xC stride 2 = +B1/time byte; 0xA single-byte) + `WalkCommands` (one `AddrResult` row per command, stops at first non-enable). Single source of truth shared with `EventSubEditorHelper.ValidateMoveData` (fixes 0xA mis-skip). Avalonia `EventMoveDataFE7View` per-command list + B1/Time row shown live for 9/0xC (mirrors `B0_ValueChanged`); Write under ambient undo (correct offset gating, NOT the inverted WF guard); VM `GetListCount`=walked count so `--data-verify-full` covers every command.
- `EventUnitColorCore.cs` (Core, R/O, PURE, #1444; WF `EventUnitColorForm`) — `UNIT_COLOR` 4-slot picker: `Pack`/`Unpack` (`a|b<<4|c<<8|d<<12`; 4th=nibble d, fixes WF `JumpTo` c-bug) + `GetUNIT_COLOR` label (WF `InputFormRef` delegates). Avalonia `EventUnitColorView` real 4-combo picker (was placeholder); event editor `ScriptEditorHelper` "Pick..." `ShowDialog<uint?>` write-back + `ResolveDisplayName` label; ListParity NoList.
- `SongWaveConvertCore.cs`/`SongSoxConvertCore.cs` (Core, #1448; WF `SongInstrumentImportWaveForm`/`SongUtilDPCM`) — DirectSound wav-import ENCODE/preview side: `WavToDPCMByte` (verbatim WF lookahead DPCM encoder; decodes back via `SongDirectSoundWavCore.ByteToWavForDPCM`), `CalculateSNR`, `LoadWavS` (.s `.byte`/`.word`), `HasHqMixer` (m4a gate via `PatchDetection.SearchPatchBool`), `Convert`/`Preview` orchestrators (DPCM gated OFF unless HQ-mixer present); `SongSoxConvertCore.ConvertWaveBySox` runs the configured external `sox` (`Config.at("sox")`; no-op early-exit; clear error when unset — no bundled resampler). `SongDirectSoundWavCore.ImportSampleBytes` appends ready (raw|DPCM) sample bytes. Avalonia `SongInstrumentImportWaveView` real options dialog (was empty stub) returns bytes via `ShowDialog<byte[]?>`; Cancel = strict no-op; ListParity NoList.
- `WorkSupportUpdateDownloadCore.cs` (Core, #1454; WF `ToolWorkSupportForm`) — work/ROM-hack update download+apply pipeline (the half not ported by #1196): `ResolveDownloadUrl` (UPDATE_URL/UPDATE_REGEX with `@CHECK_URL`/`@DIRECT_URL` fallbacks + `EscapeURLToDecode`), `DownloadAndStage` (256-byte floor; raw-UPS copy vs `ArchSevenZip.Extract`+ported `CopyDirectory1Trim`; enumerate `*.ups`), `ApplyUpsAgainstOriginal` (**validate-ALL-before-write**: apply every UPS in memory first, write all `.gba` only if all succeed → no partial output; CRC mismatches collected as `Warnings`, not flattened). All network/extract/ROM-apply injected as delegates → offline tests. Avalonia `ToolWorkSupportViewModel`/`ToolWorkSupportView.Update_Click` now check the loaded hack's own `.updateinfo.txt` (was the editor's GitHub release): CheckReady guard, `WorkSupportUpdateCheckCore.Check`, force-update via `ToolWorkSupport_UpdateQuestionDialogView`, CRC32 vanilla-ROM auto-find in `ToolWorkSupport_SelectUPSView`, reopen via `MainWindow.LoadRomFile`. Deferred (non-goals): silent background auto-update, AutoFeedback telemetry, `DISABLE_CHEAT`.
- `SupportUnitAutoCollectCore.cs` (Core, ROM-MUTATING, #1455; WF `SupportUnitForm.AutoCollect`) — FE7/8 reciprocal-support mirroring: `RecomputePartnerCount` (B21=non-zero of 7 slots) + `AutoCollect`/`AutoCollectByTargetSupport` (resolve partner row via `SupportUnitNavigation.GetSupportAddrForUnitId`, scan 7 slots for owner uid, write init `+SUPPORT_LIMIT`/growth `+2*SUPPORT_LIMIT`; **full-row safety guard** = true max touched byte `targetAddr+(LIMIT-1)+2*LIMIT` AND 24-byte range, not WF base-only; ambient undo). Avalonia `SupportUnitEditorViewModel.WriteSupportUnit` runs it inside the View's `BeginUndoScope`; "Auto-adjust partner values" CheckBox default-on, **disabled in decomp mode** (source-backed path field-scoped, no reciprocal source write).
- `ImageTSAAnimeFrameEnumCore.cs` (Core, R/O, PURE, #1457; WF `ImageTSAAnimeForm`) — `EnumerateFrames(rom,tsaAnime)` lists ALL FRAMECOUNT frames per `tsaanime_` category at `base+i*12` (mirrors `ReInitPointer(pointer,count)`; FRAMECOUNT=col0, NAME=col1; ROM-explicit `isSafetyOffset`, EOF bounds-guard, no 20-cap). Shared by Avalonia `ImageTSAAnimeViewModel.LoadList` (was `LoadTSVResource1` frame-0-only) + `ListParityHelper.BuildImageTSAAnimeList` so frames 1..N-1 are reachable/editable; v1 View import fixed to WF slot order (img@+0, **palette raw**@+4, TSA@+8) + raw ExportPal/ImportPal.
- `StatusRMenuListCore.cs` (Core, R/O, #1459; WF `StatusRMenuForm`) — `TableCount` (6 on FE8, 5 otherwise; version gate) + `GetTablePointer` (0..5 → the six `status_rmenu*_pointer` roots) + `BuildTableList` (port of `Init`/`ListFounder`: BFS from `p32(root)` following the 4 directional pointers @+0/+4/+8/+12, dedup, stride 28, terminal nodes included) + `GetMenuName` (tid≤0x10 blank; first-line `\r\n` cut on the RAW `FETextDecode.Direct(rom,...)` decode BEFORE strip; reads ambient encoder, so R/O not strictly pure). Avalonia `StatusRMenuViewModel.SelectedTableIndex` + `StatusRMenuView` FilterComboBox now expose ALL up-to-6 RMenu tables (was unit-only + weak linear `+i*28`); `ListParityHelper.BuildStatusRMenuList` re-points to the same traversal.
- `ProjectRenameCore.cs` (Core, FILESYSTEM-MUTATING, FS-injectable, #1461; WF `ToolChangeProjectnameForm`) — real project-file rename (was an Avalonia no-op UI shell): `Validate` (refuse modified/virtual ROM, bad/empty/same name) + PURE `BuildPlan` (prefix-match every file whose name starts with the old project name → ROM + backups, suffix/ext preserved) + `ExecutePlan` (delete-then-move per dest; etc dir moved last) + `Rename` (resolves the `config/etc/<title>/` dirs via `U.ConfigEtcFilename`, returns the new ROM path). `IProjectRenameFileSystem`/`RealProjectRenameFileSystem` so tests run without disk; `U.IsBadFilename` ported to Core. Avalonia `ToolChangeProjectnameViewViewModel.TryRename` + `ToolChangeProjectnameView.OK_Click` reload via `MainWindow.LoadRomFile` (mirrors WF `ReOpenMainForm`+`LoadROM`).
- `PatchMetadataCore` clean-ROM-diff uninstall (Core, ROM-MUTATING, #1462; WF `PatchForm.UninstallPatchInner`) — `CollectPatchRegionsWithBytes`/`CollectPatchRegions`(out untraceableCount)/`RomContainsPatch`/`IsCompatibleRom`/`UninstallPatchWithCleanRom`: uninstall a prior-session BIN patch (no per-patch backup) by diffing vs a user-picked patch-free ROM. Patch-absence check is FAITHFUL to WF `SearchContainThisPatchBy` — `RomContainsPatch` memcmps each region's candidate bytes against the patch's OWN `.bin` bytes (`PatchRegion.PatchBytes`=WF `t.bin`), NOT the current ROM (so a candidate that still contains the patch but is edited elsewhere is REJECTED; JUMP regions PatchBytes=null⇒skipped). Gates BEFORE mutation: GBA-header (0xA0..0xB0) compat + PREFLIGHT size gate (clean ROM must cover every region, else Fail not partial-Ok) + patch-absence. CORRECTION-ONLY restore writes only DIFFERING bytes (batched `write_range`) so over-estimated region/JUMP lengths never clobber neighbours; EA/`$FREEAREA`/`$GREP` ⇒ PARTIAL report (never over-claim); ambient undo, byte-identical rollback. Wires the orphaned Avalonia `PatchFormUninstallDialogView` into `PatchManagerView` Uninstall (backup fast-path kept; `CanUninstall` no longer backup-gated).
- `DisASMArgGrepCore.cs` (Core, READ-ONLY, PURE, #1463; WF `DisASMDumpAllArgGrepForm.Grep`/`IsSearchRegister`) — register-flow "Disassembly Argument Grep" over cached disasm lines (was a flat case-insensitive substring grep with all 5 options dead): `IsSearchRegister` (`" mov"`/`" ldr"` gate + literal `" rN"` substring — case-SENSITIVE, prefix-wide like WinForms) + `NormalizeSearchFunction` (hex→`0x`-pointer via `U.atoh`/`toPointer`/`To0xHexString`, symbol pass-through) + `Grep` (anchor on a register-set, within `allowNumber` rows find the target call, emit the block from anchor→call, hide-call drops the call line, hide-unknown skips `'('` sets, re-anchor on closer set, `i=regLine` rewind on window overflow; `regLine==0` line-0 sentinel quirk preserved verbatim). Avalonia `DisASMDumpAllArgGrepView` now surfaces all 5 options (target function, r0-r8 register, allowed-rows 1..20 default 5, hide-function-call, hide-unknown-arg) bound to `DisASMDumpAllArgGrepViewModel`.

### Caching System

Multiple cache layers for performance:

1. **InputFormRef Cache** - Data counts to avoid ROM scans
2. **AsmMapFileAsmCache** - Background thread for ASM/MAP file parsing
3. **EtcCache** - Resource tables, flags, text IDs (`IEtcCache` interface in Core)
   - Exposes `RepointEtcData(oldAddr, oldSize, newAddr)` so table-expansion
     helpers can relocate per-row comment/lint keys when a table moves
     (used by `DataExpansionCore.ExpandTableTo` for #501 action-anime list).
4. **TextIDCacheCore** (Core, `ITextIDCache`) — text-id reference-comment cache (#1028 Slice A):
   `Update`/`Save`/`GetName` ported verbatim from WF `EtcCacheTextID` (which now implements the
   same interface); `GetName` = direct user→system dict lookup (no WF `UseValsID`). `CoreState.UseTextIDCache`
   typed `ITextIDCache`; (re)created on every Avalonia ROM load (ROM/path/lang-sensitive), shared from WF Program.
5. **PatchHardCodeScanner + CoreAsmMapCache** (Core, `IAsmMapCache`) — hardcode detection (#1035): WF `MakeHardCodeWarning`+`CheckIFFast` gate; lazy, lights Unit/Class/Item `[HardCoding]`. `GetAsmMapFile()`→`AsmMapSymbolFile` backs the Pointer Tool "What is this address?" (#1026) + `PointerToolAutoSearchCore` (READ-ONLY, never-throws, #1113) cross-ROM AutoSearch: NAME+LDR-pool symmetry+window/slide retry; `MakeLDRMap` +4-hardened.

Cache invalidation occurs on ROM modifications.

### Table Expansion Helpers

**File:** `FEBuilderGBA.Core/DataExpansionCore.cs`

Cross-platform helpers for growing pointer-based ROM tables:
- `ExpandTable(rom, ptr, entrySize, currentCount)` — adds **one** entry; uses
  `0xFF` recycling for the old region.
- `ExpandTableTo(rom, ptr, entrySize, currentCount, newCount)` — grows to a
  specific row count; mirrors WinForms `InputFormRef.ExpandsArea(ExpandsFillOption.NO, ...)`:
  copies existing rows verbatim, zero-fills new rows, writes a `0xFFFFFFFF`
  terminator at `newBase + newCount * entrySize` (pointer-first scan stop),
  wipes the old region with `0x00`, repoints `CoreState.CommentCache` +
  `LintCache` entries via `IEtcCache.RepointEtcData`. Used by Avalonia
  `ImageMapActionAnimationViewModel.ExpandList` (#501).
- `RepointAllReferences(rom, oldBase, newBase, undo)` — opt-in, all-reference
  rescan that repoints EVERY reference to a moved table base: raw 32-bit
  pointers (`U.GrepPointerAll`) **and** ARM Thumb LDR literal-pool loads
  (`U.GrepPointerAllOnLDR`, ported to Core with EOF-safety guards in #781).
  De-duplicates the combined slot list via a `HashSet` (a valid LDR slot is
  also a raw hit), writes only literal/pointer slots via `rom.write_p32`
  (ambient-undo or the passed `undo`), and refuses safely (returns 0, no throw)
  on a no-reference ROM or a danger-zone (0x0–0x200) base. Mirrors WF
  `MoveToFreeSapceForm.SearchPointer`'s repoint minus the WinForms UI dialogs /
  event-aware `GrepPointerAllOnEvent` pass / `IsFixedASM` ASM-code guard
  (`InputFormRef`-dependent — out of scope). `ExpandTable`/`ExpandTableTo` keep
  their correct single-slot repoint for unshared tables.

Known WF parity gap in `ExpandTable`/`ExpandTableTo` (documented in XML doc, no
follow-up issue filed): forward-only cache repoint (rollback does NOT reverse it
— matches WF). The LDR-pointer rescan gap is now closed via the opt-in
`RepointAllReferences` helper (#781).

## File Organization

### Main Forms (Version-Specific)
- `MainFE6Form.cs` - FE6 editor
- `MainFE7Form.cs` - FE7 editor
- `MainFE8Form.cs` - FE8 editor
- `MainSimpleMenuForm.cs` - Simplified beginner mode

### Domain-Specific Forms

**Units & Classes:** `UnitForm`, `UnitFE6Form`, `ClassForm`, `SupportUnitForm`

**Items:** `ItemForm`, `ItemWeaponEffectForm`, `ItemStatBonusesForm`, `ItemShopForm`

**Graphics:** `ImagePortraitForm`, `ImageBattleAnimeForm`, `ImageItemIconForm`, `ImageTSAEditorForm`

**Events:** `EventScriptForm`, `EventCondForm`, `EventUnitForm`, `AIScriptForm`, `ProcsScriptForm`

**Maps:** `MapSettingForm`, `MapEditorForm`, `MapPointerForm`, `MapTerrainNameForm`

**Audio:** `SongTableForm`, `SongTrackForm`, `SongInstrumentForm`, `SoundRoomForm`

**Tools:** `PatchForm`, `HexEditorForm`, `DisASMForm`, `ToolROMRebuildForm`

### Utility Classes

- `U.cs` - Global utility functions (hex conversion, file I/O, clipboard)
- `R.cs` - Resource/localization helpers
- `Log.cs` - Application logging
- `Config.cs` - Configuration management (loads from `config/config.xml`)
- `SkillUtil.cs` - Skill system utilities
- `GrowSimulator.cs` - Stat growth calculations
- `CsvManager.cs` - CSV export functionality

## Version-Specific Differences

Key data structure differences between ROM versions:

- **Huffman tree locations** - Different per version
- **Map settings size** - FE6: 68-72 bytes, FE7/8: different
- **Event condition size** - FE6/8: 12 bytes, FE7: 16 bytes
- **Text encoding** - Japanese: Shift-JIS, US: ASCII subset
- **Support system** - FE6 has different structure from FE7/8

Always check `Program.ROM.RomInfo.version` when implementing version-specific logic.

## Development Workflow

See **[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md)** for the mandatory development workflow with Copilot CLI review gates. The `dev-flow` superpowers skill enforces this workflow automatically — it activates before any code changes and blocks implementation until the plan is reviewed. Key phases: Issue → Plan → Copilot Review → Implement → PR → Copilot Review → Merge. Plan-first, code-second.

## Workflow Preferences

- **Always push after every commit** — run `git push` immediately after `git commit`, no separate prompt needed
- **Always update docs and README** — reflect any code changes in README.md (and relevant docs) before committing
- **Always commit as `laqieer <laqieer@126.com>`** — never use the zhiwenzhu identity for any commit in this repo or its submodules
- **After creating or cloning any git repo**, immediately set: `git config user.name "laqieer" && git config user.email "laqieer@126.com"`
- **ALL GitHub operations (`gh`) MUST target the fork `laqieer/FEBuilderGBA`** — always pass `-R laqieer/FEBuilderGBA`. This includes creating issues/PRs, listing issues, checking issue status, commenting, and any other `gh` command. NEVER interact with the upstream org repo `FEBuilderGBA/FEBuilderGBA`. This is non-negotiable.
- **ALWAYS use isolated worktree for new tasks** — every implementation task MUST use `isolation: "worktree"` in the Agent tool. Never run `git checkout`, `git stash`, or `git switch` in the main worktree. Multiple Claude Code sessions share the repo — touching the main worktree's git state will break other sessions. Run `git fetch origin` before spawning the worktree agent to ensure remote refs are current.

## Pre-Commit Checklist (MANDATORY)

For **every** code change, follow this checklist in order before committing:

1. **Add unit tests** (in `FEBuilderGBA.Tests/`) covering the changed logic
2. **Add E2E tests** (in `FEBuilderGBA.E2ETests/`) if the change affects CLI behaviour, GUI startup, or ROM handling
3. **Run tests locally** — all must pass before committing:
   ```bash
   dotnet test FEBuilderGBA.Tests/FEBuilderGBA.Tests.csproj
   dotnet test FEBuilderGBA.E2ETests/FEBuilderGBA.E2ETests.csproj --configuration Release --no-build
   ```
4. **Update README and docs** to reflect the change
   - **For GUI feat/fix changes (Avalonia or WinForms):** Run GUI validation via MCP computer-use (see Dependencies section for setup). Launch app, exercise the feature, capture test report. Include `## GUI Test Report` in PR body. If MCP is not available, perform manual GUI testing and note why in the PR.
5. **Capture screenshot(s)** for `feat`/`fix` PRs proving the feature or bugfix works (save locally; attach to PR description when opening/updating the PR). Acceptable: UI screenshot, CLI/terminal output, test run output, or diff screenshot. For `docs`/`chore` PRs, screenshots are optional.
6. **Commit and push**
7. **Watch CI/CD** — after the push, wait for the E2E pipeline to finish, then inspect the results
8. **Fix any CI failures** and repeat from step 1 until all tests pass in CI

## Important Patterns

### Thread Safety
- Main thread ID stored at startup
- Background thread for ASM cache parsing
- Always use `Control.Invoke()` for UI updates from background threads

### Memory Management
- ROM data loaded as single `byte[]` array
- Lazy loading for graphics/cached data
- Binary deltas for undo (not full copies)

### Localization
- Multi-language support via `config/translate/{lang}.txt`
- `R._("key")` for translated strings
- System text encoding via TBL files

### Windows API Integration
- `RAM.cs` uses P/Invoke for emulator memory access
- Reads running GBA emulator process memory for live debugging

## Configuration Files

Critical runtime dependencies in `config/` directory:

- `config.xml` - User preferences
- `data/` - Game data definitions (scripts, names, etc.)
- `patch2/{VERSION}/` - Version-specific patches
- `translate/{LANG}.txt` - Localization files
- `etc/` - Additional ROM-specific data

## Common Development Tasks

### Adding New Event Command
1. Add to event script definition in `config/data/6c_script_*.txt`
2. Define argument types in `EventScript.ArgType` enum
3. Add parsing logic in `EventScript.cs`
4. Update UI in `EventScriptForm.cs`

### Supporting New Patch
1. Place patch files in `config/patch2/{VERSION}/`
2. Ensure proper metadata (name, description, dependencies)
3. Test installation/uninstallation
4. Add to patch filter if needed (`PatchMainFilter.cs`)

### Browsing FE-Repo Resources
The FE-Repo submodule (`resources/FE-Repo/`) contains public GBA Fire Emblem graphics (portraits, battle animations, sprites, icons). Users can browse and insert these resources from the portrait editor via the "FE-Repo" button (WinForms) or the FE-Repo Resource Browser window (Avalonia). The `FERepoResourceBrowser` Core class provides cross-platform resource discovery.

### Adding Form/Feature
1. Create Form class (inherit from `Form`)
2. Use Designer for UI layout
3. Call `InputFormRef.MakeLinkEvent(this)` in constructor
4. Follow naming convention for auto-wiring
5. Add navigation link from main form

### Modifying ROM Data
1. Always use `Undo` object for modifications
2. Clear relevant caches after changes
3. Update UI to reflect changes
4. Test with multiple ROM versions if applicable

## Agent Team PUA Config
All teammates must load the pua skill before starting work.
Teammates report to Leader in [PUA-REPORT] format after 2+ failures.
Leader manages global pressure levels and cross-teammate failure transfer.
