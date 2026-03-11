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
├── FEBuilderGBA.Tests/       # net9.0-windows — Unit tests (749 tests)
├── FEBuilderGBA.Core.Tests/  # net9.0 — Cross-platform Core tests (73 tests)
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

# Export struct data to TSV (19 tables: units, classes, items, portraits, sound_room,
# sound_boss_bgm, support_units, support_talks, support_attributes, event_haiku,
# event_battle_talk, event_force_sortie, worldmap_points, worldmap_paths, worldmap_bgm,
# map_settings, link_arena_deny, cc_branch, menu_definitions)
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=units --out=units.tsv
./FEBuilderGBA.CLI --export-data --rom=rom.gba --table=all --out=data  # data.{table}.tsv per table

# Import struct data from TSV
./FEBuilderGBA.CLI --import-data --rom=rom.gba --table=units --in=units.tsv

# Validate struct data round-trip (exit 0=lossless, 2=mismatches)
./FEBuilderGBA.CLI --data-roundtrip --rom=rom.gba --table=all
```

### Dependencies

The application requires these runtime files (copied to output by MSBuild targets):
- `config/` directory - Contains all game data definitions, patches, translations (copied from repo-root `config/`)
- `7-zip32.dll` (optional) - Native archive handling for maximum speed (source: `FEBuilderGBA/lib/`)

Archive handling:
- **If 7-zip32.dll exists**: Uses native DLL (very fast, no progress reporting)
- **If 7-zip32.dll missing**: Falls back to SharpCompress (pure .NET, slower but with progress)

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

### Caching System

Multiple cache layers for performance:

1. **InputFormRef Cache** - Data counts to avoid ROM scans
2. **AsmMapFileAsmCache** - Background thread for ASM/MAP file parsing
3. **EtcCache** - Resource tables, flags, text IDs

Cache invalidation occurs on ROM modifications.

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

## Workflow Preferences

- **Always push after every commit** — run `git push` immediately after `git commit`, no separate prompt needed
- **Always update docs and README** — reflect any code changes in README.md (and relevant docs) before committing
- **Always commit as `laqieer <laqieer@126.com>`** — never use the zhiwenzhu identity for any commit in this repo or its submodules
- **After creating or cloning any git repo**, immediately set: `git config user.name "laqieer" && git config user.email "laqieer@126.com"`

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
5. **Commit and push**
6. **Watch CI/CD** — after the push, wait for the E2E pipeline to finish, then inspect the results
7. **Fix any CI failures** and repeat from step 1 until all tests pass in CI

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
