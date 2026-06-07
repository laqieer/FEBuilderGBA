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

**Avalonia equivalent** (`FEBuilderGBA.Avalonia/Services/WindowManager.cs`): `WindowManager.Navigate<TView>(addr)` opens an editor positioned at an address; `WindowManager.PickFromEditor<TView>(addr, owner)` opens an editor in pick mode and awaits the user's selection. For type-ID fields (Class ID, etc.), the reusable `Controls/IdFieldControl` (#366) bundles a hyperlink label + NumericUpDown + inline name preview + Jump button + Pick button, with `JumpRequested` / `PickRequested` / `ValueChanged` routed events the host wires per call site.

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
- `LZ77ToolCore.cs` (Core) - Cross-platform helpers for the LZ77 Tool's Move + Recompress tabs.
  Used by both WinForms `ToolLZ77Form` and Avalonia `ToolLZ77ViewModel`.
  Uses ambient undo via `ROM.BeginUndoScope()`; LDR-first / raw-fallback pointer search
  (event-aware path is explicitly out of scope — `MissingEventAwareCoverage` flag always set).
- `SkillSystemsAnimeExportCore.cs` (Core, READ-ONLY) - Cross-platform EXPORT seam for
  SkillSystems skill animations (ported from WinForms `ImageUtilSkillSystemsAnimeCreator.Export`).
  `SkipCode(rom, animeAddr, out isDefender)` resolves the anime-config (FE8J direct;
  FE8U skips the embedded `config/patch2/FE8U/skill/skillanimtemplate*.dmp` program and
  flags the defender variant); `ExportSkillAnimation` walks frames to the `0xFFFF`
  terminator, LZ77-decompresses OBJ+TSA, renders each 240×(≥160) via
  `ImageUtilCore.DecodeTSA` (TILE-unit args, `opaqueIndex0:true` to match WF
  `ByteToImage16Tile`); `BuildScriptLines` emits the `.txt` script. The shared
  Avalonia `SkillConfigAnimeExportHelper` writes `.txt`+PNGs or an animated GIF
  (`U.GameFrameSecToGifFrameSec` delays) for the 4 animation-bearing SkillConfig
  views (FE8N Ver1 stays a stub) (#910).
- `SkillSystemsAnimeImportCore.cs` (Core, ROM-MUTATING) - Cross-platform IMPORT seam
  for SkillSystems skill animations (SLICE 1 #916 FE8J + SLICE 2 #917 FE8U; ported
  from WinForms `ImageUtilSkillSystemsAnimeCreator.Import`). **FE8J AND FE8U.**
  `ParseScript` handles `D`/`S{hex}`/`{wait} {png}` lines (sound-id default `0x3d1`);
  `ImportSkillAnimation` validates EVERYTHING before any mutation, encodes each
  UNIQUE frame (dedup by **filename**) via `ImageImportCore.EncodeTSA` +
  `LZ77.compress` (tiles + TSA) while keeping the **palette RAW 0x20 bytes (NEVER
  compressed)**, forces 240×160, builds the frames table terminated by a **4-byte
  `0xFFFF,0xFFFF`**, writes the 5-word config block (frames/tsalist/imagelist/pallist
  as `U.toPointer`, then `sound_id` as a **RAW u32**), and repoints the slot via
  `RecycleAddress.WriteAndWritePointerAmbient` LAST — all under one
  `ROM.BeginUndoScope`, with a defensive `rom.Data` snapshot restored in-place on
  ANY fault so a partial write never half-flips the slot or leaks freespace bytes.
  **FE8U** (NOT `is_multibyte`) additionally PREPENDS the per-skill program template
  (`config/patch2/FE8U/skill/skillanimtemplate*.dmp`, defender vs attack by the
  leading `D` line; WF :589-598) to `mainData` before the config block. SLICE-2
  guards: the template is read ONCE (`File.ReadAllBytes`) in the
  validate-before-mutate phase (GUARD A) and carried verbatim into the write
  (GUARD C, no endian/pad/truncate) — a missing/unreadable `.dmp` returns a clean
  no-mutation error THERE, so the validated bytes == the written bytes and the
  forced-failure byte-identity guarantee holds for FE8U too. The dir + 2 filenames
  live in the SINGLE shared `FE8USkillTemplate` constants (GUARD E) used by BOTH
  this prepend and the export `SkipCode` (which skips exactly this prefix on
  re-read — the repointed slot points to the template START), so the two seams
  can never drift. The shared Avalonia `SkillConfigAnimeImportHelper` wires the 4
  SkillConfig views' Animation Import buttons (FE8N Ver1 stays a stub) (#913).
  OLD-REGION RECYCLE (#914): the former parity gap is now CLOSED for single-import.
  `EnumerateOldAnimeRegions(rom, oldAnimeAddress)` is a literal, STRICTLY
  READ-ONLY port of WF `RecycleOldAnime` (`:637-784`) — it enumerates the slot's
  CURRENT anime sub-regions (per-frame OBJ/TSA LZ77 + RAW 0x20 palette + the
  program/config block + the three pointer lists, `count` per-frame so freed
  block lengths byte-match WF) with WF's two-tier semantics (pre-walk guard
  failure → EMPTY list; mid-walk bail → PARTIAL list, never cleared). It runs in
  the validate phase BEFORE the defensive snapshot clone, so #885 byte-identity
  on fault never depends on it, and threads into `WriteCore`'s `RecycleAddress`
  pool via the new `recycleOldRegion` parameter (default `true`). Single-import
  (Avalonia helper) reuses the freed region.
  CROSS-SLOT SHARED-REGION SAFETY (#929): the `SubConfilctArea`-equivalent
  skill-anime lacked is now `BuildSkillAnimeRegionRefcount(rom, animeBase, count)`
  — a STRICTLY READ-ONLY refcount over EVERY slot's recyclable regions, keyed by
  normalized data address (`Address.Addr`, NOT `Address.Pointer` — different
  pointer slots referencing the SAME bytes is the normal sharing case). It counts
  SLOT OWNERSHIP (per-slot `HashSet<uint>` dedup) NOT raw per-frame entries, so a
  slot reusing a frame id stays `count==1`; only a region owned by ≥2 distinct
  slots reaches `count>1`. An exclude-aware overload
  `EnumerateOldAnimeRegions(rom, addr, IReadOnlySet<uint> excludeRegionAddrs)`
  SKIPS any per-frame region OR config/list block whose data address is excluded
  (the original `(rom, addr)` overload delegates with `null` — no behavior
  change), and `ImportSkillAnimation` gained a trailing `excludeRegions = null`
  param threaded into the recycle-pool build. **Bulk
  (`SkillConfigSkillSystemBulkImportCore`) now recycles NON-SHARED regions**: it
  computes the shared set once from the ORIGINAL pre-mutation state and passes
  `recycleOldRegion:true, excludeRegions:shared` — conservative (originally-shared
  regions stay excluded for the whole transaction; may leak but never corrupts a
  co-owner; dynamic reclaim out of scope).
- `SkillConfigSkillSystemBulkExportCore.cs` (Core, READ-ONLY) - Cross-platform BULK-EXPORT
  seam for the SkillSystems skill config (SLICE 1 of #920; ported from WinForms
  `SkillConfigSkillSystemForm.ExportAllData`). `ExportAll(rom, textPointerLocation,
  animePointerLocation, tsvPath, writeAnime) → string error` derefs BOTH pointer
  LOCATIONS to bases (`rom.p32`), walks the `i < 255`-capped row count via
  `Rom.getBlockDataCount`, writes a `*.SkillConfig.tsv` of `textID<TAB>animePtr`
  hex rows, and for each EXTENDED-area anime (`animePtr != 0 && animePtr >=
  U.toOffset(extends_address)`) renders via the merged
  `SkillSystemsAnimeExportCore.ExportSkillAnimation` (#912) and hands the result +
  the `anime{i:hex}` dir name to the `writeAnime` delegate (so Core stays free of
  GUI image-save). Guards: CoreState.ROM-identity, NOT_FOUND locations (patch not
  installed), unsafe bases. ZERO ROM mutation, no undo. The Avalonia
  `SkillConfigSkillSystemView.BulkExport_Click` provides `writeAnime` — it writes
  `anime{i:hex}/anime.txt` (via `SkillSystemsAnimeExportCore.BuildScriptLines`) +
  per-frame PNGs and disposes each unique `IImage` once. BULK IMPORT = SLICE 2
  (separate PR, #923).
- `SkillConfigSkillSystemBulkImportCore.cs` (Core, ROM-MUTATING, BULK-ATOMIC) -
  Cross-platform BULK-IMPORT seam for the SkillSystems skill config (SLICE 2 of
  #923 / #885; ported from WinForms `SkillConfigSkillSystemForm.ImportAllData`).
  `ImportAll(rom, textPointerLocation, animePointerLocation, tsvPath,
  animeScriptDirResolver, imageProvider, applyRecycle=true) → string error`
  reads a `*.SkillConfig.tsv` (one `textID<TAB>animePtr` hex row per skill, as
  written by the export seam), derefs BOTH pointer LOCATIONS (after a `+3`
  isSafetyOffset guard, #922 lesson), walks the `i < 255`-capped row count, and
  for each skill with an `anime{i:hex}/anime.txt` re-imports the animation via
  the merged `SkillSystemsAnimeImportCore.ImportSkillAnimation` (now with an
  additive `manageSnapshot` param). The WHOLE multi-skill import is ONE ATOMIC
  transaction: either every skill commits (exactly ONE undo record) or the ROM
  is restored byte-identical to the pre-bulk snapshot (ZERO undo records). The
  3 HIGH corruption fixes from the approved #923 plan: **H1** length-aware
  restore — a per-skill anime import can GROW `rom.Data` (via RecycleAddress →
  `write_resize_data`); the fault restore down-resizes back to `snap.Length`
  BEFORE the in-place `Array.Copy`, so the trailing grown bytes can't survive.
  **H2** return-value fault detection — `ImportSkillAnimation` signals failure
  by RETURNING a non-empty string (not only by throwing); the bulk treats a
  non-empty returned error OR a thrown exception OR a `NOT_FOUND` as a fault.
  **H3** ONE `ROM.BeginUndoScope` wraps the whole loop, every per-skill import
  runs with `manageSnapshot:false` (composing into the bulk scope instead of
  opening its own non-reentrant scope), the scope is asserted alive across all
  skills (`Rom.IsAmbientUndoScopeActive`), and exactly one record is pushed on
  success. M/L: textID written ONLY when non-zero (M1); `applyRecycle` toggle
  through the ported pure `SkillConfigSkillTextIDRecycle.Convert` (M2);
  VALIDATE-ALL-BEFORE-MUTATE pre-loads every script + all PNGs (+ FE8U .dmp
  template) so any validation failure mutates ZERO bytes (M3); malformed
  `< 2`-field TSV rows skipped (L1); textID write uses the ambient `write_u16`
  overload (L2). The Avalonia `SkillConfigSkillSystemView.BulkImport_Click`
  wires it (reusing the single-import quantize loader for per-frame PNGs); the
  Core seam OWNS the undo scope so the view does NOT open a UI UndoService scope
  (that would clobber the non-reentrant ambient scope) (#923).
  CROSS-SLOT RECYCLE (#929): bulk now RECYCLES old anime regions (closing the
  #914 leak) — BEFORE the mutation loop it runs the read-only
  `SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount(rom, animeBase,
  count)` over the ORIGINAL state, builds `shared = {addr : count>1}`, and passes
  `recycleOldRegion:true, excludeRegions:shared` into each per-skill
  `ImportSkillAnimation`. A region owned by ≥2 distinct slots is NEVER recycled
  (so a co-owning skill's bytes can't be overwritten); unshared old regions are
  reclaimed so bulk re-import no longer grows the ROM unboundedly. Conservative
  static pre-pass — an originally-shared region stays excluded for the whole
  transaction (safe, may leak; dynamic reclaim out of scope).
- `MapPListResolverCore.cs` (Core, READ-ONLY) - Cross-platform port of the
  WinForms map-PLIST label resolver (`MapPointerForm.GetPListNameSplited` /
  `GetPListNameNotSplite` / `ConvertBaseAddrToType` + `MapSettingForm.PLists` /
  `GetMapPListsWhereAddr`) used by the MapPointer + MapChange + MapTileAnimation
  Avalonia editors to show resolved map names (`MAP Ch1`, `MAPCHANGE Ch5`,
  `ANIME1 Prologue`, `ANIME2 Ch18 …`, `OBJ …`, `NULL`, `-EMPTY-`, `UNK`) instead
  of raw `0x08……` pointers / PLIST-hex labels (#952). `PLists`
  reads each field from its REAL per-version source — `event_plist` from
  `RomInfo.map_setting_event_plist_pos`, FE6 worldmap from
  `map_setting_worldmap_plist_pos`, and the **PAL2 offset (146 vs 45) from the
  ported `PatchDetection.SearchFlag0x28ToMapSecondPalettePatch`** (WF `PatchUtil`
  now delegates to that single Core detector). EXTENDS (does NOT fork) the
  `MapChangeCore.PlistType` enum (added MAP/EVENT/ANIMATION/ANIMATION2/
  WORLDMAP_FE6ONLY + `GetPlistBasePointer`) and `MapSettingCore.GetMapNameWhereAddr`.
  Subtleties preserved verbatim: OBJ is a packed u16 (`& 0xFF` low + `>>8 & 0xFF`
  high both resolve to `OBJ`); ANIME1/ANIME2 both match under ANIMATION; PAL/PAL2
  both under OBJECT; the FE6 WMEVENT branch fires on `worldmapevent_plist == 0`
  (after the `plist==0 → NULL` early-return); split → `-EMPTY-` on no match,
  non-split → `UNK`. Per-call LOCAL `ResolveCache` (mapAddr → PLists + name), no
  global/static state. The MapPointer VM's old `GetPlistPointer(7)` bug (returned
  `worldmap_point_pointer`) is FIXED to `map_worldmapevent_pointer` via the shared
  base-pointer seam. The `ListParityHelper.BuildMapPointerList` /
  `BuildMapChangeList` golden builders call the SAME resolver in lockstep;
  independent Core oracle tests (`MapPListResolverCoreTests`) hand-build
  expectations from raw map bytes on synthetic + real FE6/FE7U/FE8U ROMs.
  MapTileAnimation rewire (#952 T5 slice B, bug #11): `MapTileAnimationView`
  (the simple, menu-reachable editor) resolves each `map_tileanime1_pointer`
  slot index — an ANIMATION PLIST id — via `ResolveLabel(rom, ANIMATION, i)`
  (ANIME1 and ANIME2 both match under ANIMATION); `MapTileAnimation2Core.BuildPlistList`
  resolves each FILTER-combo row via `ResolveLabel(rom, ANIMATION2, plist)` (the
  broken-PLIST `(破損)` suffix is still appended). Lockstep golden builders:
  `ListParityHelper.BuildMapTileAnimationList` + the new public
  `BuildMapTileAnimation2FilterList`. **`MapTileAnimation1` anime1 PLIST filter
  (#955, #957 W1c) — DONE** (the former deferral is CLOSED): the new
  `MapTileAnimation1Core.BuildPlistList` enumerates the distinct anime1 PLISTs
  referenced by the map settings (`anime1_plist` at map-setting `+9`), resolves
  each FILTER-combo row via `ResolveLabel(rom, ANIMATION, plist)` → `ANIME1
  MapName` (ANIME1/ANIME2 share the `map_tileanime1_pointer` base; both match
  under ANIMATION), and resolves the selected PLIST's data table via
  `MapChangeCore.PlistToOffsetAddr(ANIMATION, plist)` (broken-PLIST `(破損)`
  suffix appended). `MapTileAnimation1Core.ScanEntries` walks the SELECTED
  PLIST's 8-byte data records — **the anime1 schema is the inverse of anime2**:
  `wait = u16@+0`, `length = u16@+2`, `imagePointer = p32@+4`, terminated by
  `!isPointer(u32(addr+4))` (the image pointer is at `+4`, NOT `+0`). The
  Avalonia `MapTileAnimation1View` now hosts the filter combo + selection bar
  (mirroring anime2); the VM no longer treats `map_tileanime1_pointer` (the
  PLIST TABLE) as a flat entry table. Lockstep golden builders:
  `ListParityHelper.BuildMapTileAnimation1List` (rewired to PLIST-based) + the
  new public `BuildMapTileAnimation1FilterList`.
- `MapRenderCore.cs` (Core, READ-ONLY) - Cross-platform chapter-map +
  change-map overlay renderer (ports WinForms `ImageUtilMap.DrawMap` /
  `DrawChangeMap`). **FE7 obj2 (MR4) RESOLVED (#961 W2c):** `RenderMapImage` /
  `RenderChangeMap` gained an OPTIONAL trailing `obj2Offset` parameter (default
  0). The map-setting `obj_plist` (`map_setting +4`) is a packed u16 — LOW byte
  = primary OBJ tileset PLIST, HIGH byte = FE7 secondary obj2 tileset PLIST
  (FE6/FE8 keep the high byte 0). When the caller resolves a non-zero obj2 plist
  (`(obj_plist >> 8) & 0xFF`), it passes that tileset's ROM offset; the obj2
  LZ77 stream is decompressed and **concatenated onto the primary OBJ bytes
  (primary first)** before `ImageUtilCore.DecodeTSA` — byte-for-byte the WF
  `DrawMapChipOnly` order. A non-zero-but-truncated obj2 fails the whole render
  (WF bail-to-BlankDummy parity). Avalonia `EventMapChangeViewModel.RenderChangePreview`
  resolves the high byte and passes `obj2Offset`; FE6/FE8 are byte-identical to
  the pre-#961 single-tileset path.
- `MapStyleEditorViewModel.TryImportObjImage` (Avalonia, ROM-MUTATING, #976) —
  the Map Style Editor OBJ import now supports the FE7 obj2 dual-tileset split
  (ports WF `MapStyleEditorForm.WriteMapChipImage`). When a secondary obj2 PLIST
  is present (high byte of `obj_plist != 0`, persisted as `_currentObjPlist2`),
  the encoded tile sheet is split in half by BYTE length — first half →
  primary OBJECT PLIST, second half → obj2 PLIST — each LZ77-compressed
  independently and written via `MapChangeCore.WritePlistData`. Both writes
  share the view's single ambient undo scope, so a failed second write rolls
  BOTH back (atomicity). `CanImportObj` is gated on the secondary plist being
  in-limit; FE6/FE8 single-tileset styles are unchanged.
- `EventMapChangeViewModel.ImportChangeDataFromPointer` (Avalonia, ROM-MUTATING,
  #961 W2c) — pointer-import for the Map Change Event editor (mirrors the intent
  of WF `EventMapChangeForm` `button1` "変化データ ポインタ先へのインポート").
  Reads `B3 × B4 × 2` RAW u16 bytes from a user-supplied SOURCE change-data
  address, appends a COPY to ROM free space via
  `RecycleAddress.WriteAndWritePointerAmbient`, and repoints the current
  record's P8 (`CurrentAddr+8`) at the copy — the standard append+repoint
  pattern (never overwrites in place, so a size mismatch can't corrupt
  neighbours). All writes route through the ambient overload so the View's
  `UndoService.Begin/Commit/Rollback` scope captures the whole transaction; any
  returned error rolls back. `EventMapChangeView.PointerImport_Click` prompts for
  the source address via `NumberInputDialog`, wraps the call in the undo scope,
  and re-renders the change-overlay preview on success.
- `TranslateTextUtilCore.cs` (Core, READ-ONLY, NETWORK-OPTIONAL) - Cross-platform
  port of the two safe, high-value pieces of WinForms `TranslateTextUtil` for the
  Avalonia Text Editor Translate tab (#967, follow-up to the #949 MVP).
  `SplitEscapeSegments(text)` is a CORRECTED port of WF `SplitEscapeString`:
  it splits into alternating literal-text and `@XXXX` (5-char hex) escape
  segments (bundling a `@0003` immediately followed by `\r\n` into ONE segment)
  and — unlike WF, which drops trailing literal after the last code — flushes the
  tail so `string.Concat(SplitEscapeSegments(x)) == x` for ALL x. A `@` is only a
  code boundary when it is followed by EXACTLY four hex digits (the position-based
  `IsCodeAt` twin of `IsEscapeSegment`, single source of truth): a literal at-sign
  in ordinary text (`email@example.com`, `hello@catworld`, a trailing `@`, or `@`
  + fewer than four hex / non-hex chars) stays glued to its surrounding literal
  segment instead of being fragmented (#971). `IsEscapeSegment` classifies a code
  as `@` + exactly four hex digits. `LoadFixedDic(from, to)`
  loads the shipped glossary `config/translate/dic_<from>_<to>.txt` (the single
  `dic_ja_en.txt` serves both `ja→en` and reversed `en→ja`), tab-separated
  `source\ttarget`, keys upper-cased, `\r\n` un-escaped, first-wins on dups;
  missing file / null `CoreState.BaseDirectory` → empty dict, NO throw, and
  (W2a SongNameResolverCore lesson) only a SUCCESSFUL load is cached — the cache
  is `lock`-guarded and keyed by `(BaseDirectory, from, to)`. The orchestrator
  `TranslateText(text, from, to, dic, useGoogle, translator=null)` splits → keeps
  code segments VERBATIM (never sent to the translator) → glossary-first
  (case-insensitive, trimmed) → else the injectable `translator` delegate
  (default `new TranslateManage().Trans`, so unit tests run offline) → reassembles
  in order. DEFERRED (documented): `InsertSerifnl` line-breaking (WinForms
  System.Drawing font metrics), `FE8SkipFace48` face-code shift, and the full WF
  ROM-pair text-id glossary. The Avalonia `TextViewerView.OnTranslateClick` calls
  it (preserving the #949 off-UI-thread `Task.Run` + WebException-safe handling +
  status label + empty-result rejection) instead of the raw `Trans`.
- `UnitPaletteClassResolverCore.cs` (Core, READ-ONLY) - Cross-platform port of
  WinForms `ImageUnitPaletteForm.MakeClassList` palette→class resolution (#985).
  `ResolveDefaultPreviewClass(rom, slotIndex)` returns the FIRST class id that
  uses unit-palette slot `slotIndex` (0-based = WF `AddressList.SelectedIndex`),
  else 0 — so the Avalonia Unit Palette Editor's Edit tab can populate the
  Battle Animation id + sample preview on every selection (the original bug:
  empty Battle Animation + no preview). FE8 (`version >= 8`) scans the dedicated
  `unit_palette_color/class_pointer` byte tables (`colorBase + i*7 + n`,
  7 palettes/unit; `paletteid>0 && paletteid-1==slot` → `u8(classBase + i*7 + n)`);
  FE6/FE7 reads the per-unit-record palette ids (`+35` low / `+36` high) and
  resolves the base class (`unit+5`) or the PROMOTED class via a faithful
  `GetHighClass` port (base class at `unit+5`; if it is a low class, promote via
  the class table — `isHighClass` flag at `class+37` FE6 / `class+41` FE7, change
  class at `class+5`). First match wins. `FindFirstClassWithAnime(rom)` is the
  view's fallback (first class 1..N with `ClassFormCore.GetAnimeIDByClassID > 0`).
  Pure, takes `rom` (no `CoreState.ROM`); guards EVERY pointer-location + computed
  address (`U.isSafetyOffset`) — never throws. The Avalonia
  `ImageUnitPaletteView.OnSelected` calls it after `SelectedPaletteSlot` is set,
  before `UpdateUI`, then `RefreshSamplePreview` renders.

### Caching System

Multiple cache layers for performance:

1. **InputFormRef Cache** - Data counts to avoid ROM scans
2. **AsmMapFileAsmCache** - Background thread for ASM/MAP file parsing
3. **EtcCache** - Resource tables, flags, text IDs (`IEtcCache` interface in Core)
   - Exposes `RepointEtcData(oldAddr, oldSize, newAddr)` so table-expansion
     helpers can relocate per-row comment/lint keys when a table moves
     (used by `DataExpansionCore.ExpandTableTo` for #501 action-anime list).

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
