# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FEBuilderGBA is a comprehensive ROM hacking suite for Fire Emblem GBA trilogy games, written in C# WinForms targeting .NET 10.0. It supports editing five ROM variants: FE6 (Japan), FE7J/FE7U (Japan/US), and FE8J/FE8U (Japan/US).

### Solution Structure
```
FEBuilderGBA.sln
├── FEBuilderGBA.Core/        # net10.0 — Cross-platform core library (ROM, Undo, utilities)
├── FEBuilderGBA/             # net10.0-windows — WinForms GUI application
├── FEBuilderGBA.CLI/         # net10.0 — Cross-platform CLI (--version, --help, --makeups)
├── FEBuilderGBA.SkiaSharp/   # net10.0 — SkiaSharp IImageService implementation
├── FEBuilderGBA.Avalonia/        # net10.0 — Cross-platform Avalonia UI preview
├── FEBuilderGBA.Tests/           # net10.0-windows — WinForms unit tests (~1.3k)
├── FEBuilderGBA.Core.Tests/      # net10.0 — Cross-platform Core tests (~5.5k)
├── FEBuilderGBA.Avalonia.Tests/  # net10.0 — Avalonia GUI / ViewModel tests (~4.6k)
├── FEBuilderGBA.Android.Tests/   # net10.0-android — on-device reflection-runner parity/version-guard head
└── FEBuilderGBA.E2ETests/        # net10.0-windows — End-to-end tests
```

> Test counts above are rounded approximations of declared `[Fact]`/`[Theory]` methods; `[Theory]`
> cases expand at runtime, so the authoritative live total for the four **desktop** projects (Core,
> Avalonia, WinForms, E2E) is the one reported by `dotnet test` / CI. `FEBuilderGBA.Android.Tests` is
> an Android **instrumentation** head (not run by `dotnet test`) — its results come from the
> `android-emulator-parity.yml` workflow.

**FEBuilderGBA.Core** contains platform-independent logic: ROM manipulation (`Rom.cs`, `ROMFE*.cs`), undo system (`Undo.cs`), utility functions (`U.cs`), logging (`Log.cs`), and shared state (`CoreState.cs`). It defines abstraction interfaces (`IAppServices`, `IEtcCache`, `ISystemTextEncoder`, `IAsmMapCache`) so Core code can call platform-specific services without depending on WinForms.

## Build & Development Commands

### Building

```bash
# Build Release version (x86)
dotnet msbuild /m /p:Configuration=Release /p:Platform=x86 /t:build /restore FEBuilderGBA.sln

# Build Debug version
dotnet msbuild /m /p:Configuration=Debug /p:Platform=x86 /t:build /restore FEBuilderGBA.sln

# Build for x64
dotnet msbuild /m /p:Configuration=Release /p:Platform=x64 /t:build /restore FEBuilderGBA.sln

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

> The examples below are a **representative subset**. The canonical full CLI reference (all 71
> commands — distinct top-level dispatch-table branches in `FEBuilderGBA.CLI/Program.cs`, collapsing
> the `--help`/`-h` and `--test`/`--testonly` aliases) lives in
> **[docs/cli-reference.md](docs/cli-reference.md)**; the argument table is in
> **[docs/cli-args.md](docs/cli-args.md)**.

```bash
# Run lint check on ROM
./FEBuilderGBA.exe --rom rom.gba --lint

# Rebuild ROM
./FEBuilderGBA.exe --rom rom.gba --rebuild

# Create UPS patch
./FEBuilderGBA.exe --rom rom.gba --makeups

# Disassemble code
./FEBuilderGBA.exe --rom rom.gba --disasm [params]

# Convert an image to GBA map tiles + TSA + matching palette (no ROM required)
./FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin --outPal=palette.bin

# Show version
./FEBuilderGBA.exe --version

# Deterministic headless mGBA verification (optional source-pinned dependency)
./FEBuilderGBA.CLI --playtest --check --python=/path/to/playtest-python
./FEBuilderGBA.CLI --playtest --rom=rom.gba --scenario=scenario.json --python=/path/to/playtest-python

# Validate text export/import round-trip (exit 0=lossless, 2=mismatches)
./FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba
./FEBuilderGBA.CLI --translate-roundtrip --rom=rom.gba --out=diff  # saves diff.export1.tsv + diff.export2.tsv

# Export struct data (TSV default; CSV/EA/JSON/GNU11 C supported; 40 tables: units, classes, items, portraits, sound_room,
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
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=json --out=units.json  # LLM-backend format: JSON array of string-valued objects (docs/febuilder-cli-as-llm-backend.md)
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --format=c --c-symbol=gUnitData --out=units.c  # GNU11 decomp-C backend (docs/febuilder-cli-as-decomp-c-backend.md)

# Import struct data from TSV or JSON (format auto-detected from a .json --in, or pass --format=json)
./FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.tsv
./FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.json

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

# Disassemble event scripts (requires --addr; optional --type=event|procs|ai)
./FEBuilderGBA.CLI --disasm-event --rom=rom.gba --addr=0x123456 --out=events.txt

# Lint OAM sprites (requires --addr)
./FEBuilderGBA.CLI --lint-oam --rom=rom.gba --addr=0x123456

# Apply binary patch
./FEBuilderGBA.CLI --apply-patch --rom=rom.gba --patch-file=patch.txt

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
./FEBuilderGBA.CLI --merge3 --base=base.gba --mine=modA.gba --theirs=modB.gba --out=merged.gba

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
- `LZ77ToolCore.cs`, `ImageBattleScreenCore`, the SkillSystems anime export/import seams, the
  Map/World-Map/Event/Song/Wait-Icon/Font/Status/Patch/Arena/AI Core classes, and ~100 other
  per-file Core seams — **see [docs/CORE-SEAMS.md](docs/CORE-SEAMS.md)** for the full catalog
  (one paragraph per class: behavioural contract, ROM-mutation/undo/fault-restore rules, and the
  issue that introduced it). **New seam entries go in `docs/CORE-SEAMS.md`, NOT here** — this
  keeps `CLAUDE.md` under the ~40,000-char harness truncation limit (issue #1645; the earlier
  #1039 trim had silently regrown).

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
- `ExpandTable(rom, ptr, entrySize, currentCount)` — adds **one** entry (`0xFF` old-region recycle).
- `ExpandTableTo(rom, ptr, entrySize, currentCount, newCount)` — grows to a specific row count
  (mirrors WinForms `InputFormRef.ExpandsArea(ExpandsFillOption.NO, ...)`; repoints comment/lint caches).
- `RepointAllReferences(rom, oldBase, newBase, undo)` — opt-in all-reference rescan (raw 32-bit
  pointers + ARM Thumb LDR literal-pool loads) that repoints every reference to a moved table base.

See **[docs/CORE-SEAMS.md](docs/CORE-SEAMS.md)** ("Table Expansion Helpers") for the full
behavioural contract, the `0xFFFFFFFF`-terminator / old-region-wipe details, the danger-zone
refusal, and the known forward-only cache-repoint WF parity gap (#781).

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

See **[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md)** for the mandatory development workflow with developer-aware review gates (Copilot CLI when Claude Code develops; an in-session cross-model board when Copilot CLI develops). The `dev-flow` superpowers skill enforces this workflow automatically — it activates before any code changes and blocks implementation until the plan is reviewed. Key phases: Issue → Plan → Review Gate → Implement → PR → Review Gate (except the canonical, independently verified screenshot-only helper PR exemption) → Merge. Plan-first, code-second.

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

Avalonia portrait import supports FE-Repo Standard Hackboxes (128×112) and FE8 HALFBODY Halfbody Hackboxes (160×160, requires the HALFBODY portrait-extension patch). Twoparter Hackboxes (144×304) are intentionally rejected until a verified PART1/PART2 slicing layout exists.

### Adding Form/Feature
> **GUI policy:** new **features** target the **Avalonia GUI** (`FEBuilderGBA.Avalonia`).
> The WinForms GUI is in **stable mode — bug fixes only**; touch a WinForms form only
> to fix a bug/regression, never to add a new feature. See
> [docs/GUI-STRATEGY.md](docs/GUI-STRATEGY.md).
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
