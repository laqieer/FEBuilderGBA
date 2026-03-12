# Avalonia Gap Closure Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the ~40% functional gap between WinForms and Avalonia GUI, prioritized by user impact.

**Architecture:** Fix critical bugs first, then build missing infrastructure (patch detection, data expand), then implement missing editor features domain by domain. Each task is independently deployable.

**Tech Stack:** C# / .NET 9.0 / Avalonia 11.2.3 / xUnit

---

## Chunk 1: Critical Bug Fixes (P0)

### Task 1: Fix CG/BG Image Import Parameter Swap

**Files:**
- Modify: `FEBuilderGBA.Avalonia/Views/ImageCGView.axaml.cs` (Import3Pointer call)
- Modify: `FEBuilderGBA.Avalonia/Views/ImageBGView.axaml.cs` (Import3Pointer call)
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ImageCGViewModel.cs` (fix P4/P8 comments)
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ImageBGViewModel.cs` (fix P4/P8 comments)
- Test: `FEBuilderGBA.Core.Tests/ImageImportCoreTests.cs`

- [ ] **Step 1: Write failing test** — Create test that verifies Import3Pointer parameter ordering matches WinForms layout (P0=image, P4=TSA, P8=palette)
- [ ] **Step 2: Fix ImageCGView.axaml.cs** — Change `Import3Pointer(rom, ..., addr+0, addr+8, addr+4)` to `Import3Pointer(rom, ..., addr+0, addr+4, addr+8)`
- [ ] **Step 3: Fix ImageBGView.axaml.cs** — Same parameter reordering
- [ ] **Step 4: Fix ViewModel comments** — Correct P4/P8 descriptions in both ViewModels
- [ ] **Step 5: Run tests** — `dotnet test FEBuilderGBA.Core.Tests`
- [ ] **Step 6: Commit** — `git commit -m "fix: correct CG/BG import parameter swap (P4↔P8)"`

### Task 2: Fix FE6 Class Move Cost Pointer Offsets

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ClassFE6ViewModel.cs`
- Test: `FEBuilderGBA.Core.Tests/CoreStateTests.cs`

- [ ] **Step 1: Write failing test** — Verify FE6 move cost pointer reads from +52/+56/+60/+64, not +56/+60/+64/+68
- [ ] **Step 2: Add version check** — In ClassEditorViewModel, check `rom.RomInfo.version == 6` and use FE6-specific offsets
- [ ] **Step 3: Verify ClassFE6ViewModel** — Confirm ClassFE6ViewModel already uses correct offsets (it should since it's FE6-specific)
- [ ] **Step 4: Run tests** — `dotnet test FEBuilderGBA.Core.Tests`
- [ ] **Step 5: Commit** — `git commit -m "fix: use correct FE6 move cost pointer offsets in ClassEditor"`

---

## Chunk 2: Computed Fields & Validation (P2)

### Task 3: Add Item Editor Computed Fields

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/ItemEditorView.axaml`
- Modify: `FEBuilderGBA.Avalonia/Views/ItemEditorView.axaml.cs`

- [ ] **Step 1: Add shop price properties** — `ShopBuyPrice`, `ShopSellPrice`, `ShopForgePrice` computed from Uses * Price
- [ ] **Step 2: Add stat bonus preview** — Read ItemStatBonuses table when P12 changes, expose 9 stat properties
- [ ] **Step 3: Add effectiveness class list** — Read effectiveness table when P16 changes, expose class name list
- [ ] **Step 4: Add null pointer warnings** — `ShowAllocStatBonuses`, `ShowAllocEffectiveness` when P12/P16 = 0
- [ ] **Step 5: Wire computed fields in View** — Add TextBlocks/Labels bound to new properties
- [ ] **Step 6: Run tests** — `dotnet test FEBuilderGBA.Core.Tests`
- [ ] **Step 7: Commit** — `git commit -m "feat: add computed fields to Avalonia ItemEditor (price, stats, effectiveness)"`

### Task 4: Add Unit Editor Real-Time Growth Simulation

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/UnitEditorView.axaml.cs`

- [ ] **Step 1: Add PropertyChanged handler** — Trigger growth recalculation when any stat/growth/level property changes
- [ ] **Step 2: Add configurable level input** — Replace hardcoded LV10/20 with user-selectable level
- [ ] **Step 3: Run tests** — `dotnet test FEBuilderGBA.Core.Tests`
- [ ] **Step 4: Commit** — `git commit -m "feat: real-time growth simulation in Avalonia UnitEditor"`

### Task 5: Add Class Editor Growth Sim Flexibility + FE6 Fix

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs`

- [ ] **Step 1: Add configurable level** — Replace hardcoded LV10/20 with user input
- [ ] **Step 2: Run tests and commit**

---

## Chunk 3: Patch Detection Infrastructure (P2)

### Task 6: Create PatchDetectionService for Avalonia

**Files:**
- Create: `FEBuilderGBA.Avalonia/Services/PatchDetectionService.cs`
- Test: `FEBuilderGBA.Core.Tests/PatchDetectionTests.cs`

- [ ] **Step 1: Write tests** — Verify detection of MagicSplit, SkillSystem, Vennou, IER, HALFBODY patches
- [ ] **Step 2: Implement service** — Wrap `PatchDetection` core class, cache results, expose properties
- [ ] **Step 3: Wire into DI** — Register as singleton in AvaloniaAppServices
- [ ] **Step 4: Run tests and commit**

### Task 7: Wire Patch Detection into Unit/Item/Class Editors

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/UnitEditorViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ItemEditorViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ClassEditorViewModel.cs`
- Modify: corresponding Views

- [ ] **Step 1: Unit Editor** — Add magic extension fields (FE8N/FE8U), skill buttons (FE8), conditional visibility
- [ ] **Step 2: Item Editor** — Add skill book detection (FE8N B21/B30), item skill (FE8U B35), Vennou weapon lock, dynamic trait flags
- [ ] **Step 3: Class Editor** — Add magic split fields, SkillSystems class type dropdown, CC branch navigation
- [ ] **Step 4: Run tests and commit**

---

## Chunk 4: Image Pipeline (P1/P2)

### Task 8: Portrait Full Rendering

**Files:**
- Modify: `FEBuilderGBA.Core/PortraitRendererCore.cs`
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/ImagePortraitView.axaml`

- [ ] **Step 1: Extend PortraitRendererCore** — Add mouth frame rendering (9 frames), eye states (2), mini portrait
- [ ] **Step 2: Add frame navigation** — Mouth/eye frame selector in ViewModel
- [ ] **Step 3: Add mini portrait display** — Separate image control for map face
- [ ] **Step 4: Run tests and commit**

### Task 9: Portrait Export (PNG/GIF/Sheet)

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/ImagePortraitViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/ImagePortraitView.axaml.cs`

- [ ] **Step 1: Add sheet export** — Composite 160x160 sheet matching WinForms layout
- [ ] **Step 2: Add PNG export button** — Save dialog + export
- [ ] **Step 3: Run tests and commit**

### Task 10: Image Import Pipeline (LZ77 + TSA + Pointer Write-Back)

**Files:**
- Modify: `FEBuilderGBA.Core/ImageImportCore.cs`
- Modify: `FEBuilderGBA.Avalonia/Services/ImageImportService.cs`

- [ ] **Step 1: Implement LZ77 compress + ROM write** — Compress tile data, write to free space, update pointer
- [ ] **Step 2: Implement TSA generation** — Generate tile selection array from image
- [ ] **Step 3: Implement pointer write-back** — Update ROM pointer to new compressed data
- [ ] **Step 4: Wire into portrait/CG/BG/battle BG import buttons**
- [ ] **Step 5: Run tests and commit**

---

## Chunk 5: Event System (P1 — Largest Chunk)

### Task 11: EventScript Command Parameter Editor

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/EventScriptPopupViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/EventScriptPopupView.axaml`
- Create: `FEBuilderGBA.Avalonia/ViewModels/EventScriptCommandViewModel.cs`

- [ ] **Step 1: Create command ViewModel** — Properties for each ArgType, parameter value input, jump-to support
- [ ] **Step 2: Implement script editing** — Select command → show parameter fields → write back to ROM
- [ ] **Step 3: Add argument type handling** — TEXT, UNIT, CLASS, ITEM, MUSIC, POINTER types with name resolution
- [ ] **Step 4: Add jump-to buttons** — Navigation to referenced editors (Unit, Class, Item, etc.)
- [ ] **Step 5: Add script insertion/deletion** — Add/remove commands with pointer reallocation
- [ ] **Step 6: Run tests and commit**

### Task 12: EventCond Multi-Type Editor

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/EventCondViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/EventCondView.axaml`

- [ ] **Step 1: Add condition type enum** — 13 types from config
- [ ] **Step 2: Add type-specific field panels** — TURN, TALK, OBJECT, ALWAYS, TRAP with correct offsets
- [ ] **Step 3: Add filter combo** — Switch between condition types
- [ ] **Step 4: Add event pointer linking** — Navigate to referenced event scripts
- [ ] **Step 5: Run tests and commit**

### Task 13: EventUnit Multi-Level Navigation

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/EventUnitViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/EventUnitView.axaml`

- [ ] **Step 1: Add map selection list** — Enumerate maps with names
- [ ] **Step 2: Add event group list** — Per-map event group enumeration
- [ ] **Step 3: Add unit list** — Per-group unit entries
- [ ] **Step 4: Add AI combo boxes** — AI1/2/3 name resolution from config
- [ ] **Step 5: Add FE8 coordinate management** — Multi-coord list with add/remove
- [ ] **Step 6: Run tests and commit**

---

## Chunk 6: Sound & Music (P2)

### Task 14: Song Track Parsing and Display

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/SongTrackView.axaml`

- [ ] **Step 1: Implement track parsing** — Call SongUtil equivalent to enumerate tracks
- [ ] **Step 2: Add track list display** — Show up to 16 tracks with info
- [ ] **Step 3: Run tests and commit**

### Task 15: Instrument Type Support (5 Missing Types)

**Files:**
- Create: `FEBuilderGBA.Avalonia/ViewModels/SongInstrumentViewModel.cs`
- Modify: `FEBuilderGBA.Avalonia/Views/SongInstrumentDirectSoundView.axaml`

- [ ] **Step 1: Add Wave instrument support** — Type 0x03/0x0B with data pointer
- [ ] **Step 2: Add MultiSample support** — Type 0x40 with dual pointers
- [ ] **Step 3: Add Drum support** — Type 0x80 with nested set
- [ ] **Step 4: Add Square/Noise support** — Types 0x01/0x02/0x04/0x09/0x0A/0x0C
- [ ] **Step 5: Run tests and commit**

### Task 16: MIDI Import/Export

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/SongTrackViewModel.cs`

- [ ] **Step 1: Implement MIDI export** — Wrap SongUtil.ExportMidiFile equivalent
- [ ] **Step 2: Implement MIDI import** — Wrap SongUtil.ImportMidiFile with options
- [ ] **Step 3: Add WAV import** — Dialog + loop detection
- [ ] **Step 4: Run tests and commit**

---

## Chunk 7: Map Editing (P2)

### Task 17: MapEditor Tile Editing

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/MapEditorViewModel.cs` (currently read-only)

- [ ] **Step 1: Add tile selection** — Click to select tile from tileset
- [ ] **Step 2: Add paint mode** — Click on map to place tile
- [ ] **Step 3: Add map save** — Write modified tile data back to ROM with undo
- [ ] **Step 4: Run tests and commit**

### Task 18: MapChange Record Editing

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/MapChangeViewModel.cs`

- [ ] **Step 1: Implement two-level list** — Outer (map pointer) + inner (12-byte records)
- [ ] **Step 2: Add record editing** — ChangeID, X, Y, Width, Height, TilePointer fields
- [ ] **Step 3: Add duplicate ID validation** — Warn on duplicate change IDs
- [ ] **Step 4: Run tests and commit**

---

## Chunk 8: Data Infrastructure (P2)

### Task 19: Data Expand/Shrink

**Files:**
- Create: `FEBuilderGBA.Avalonia/Services/DataExpansionService.cs`

- [ ] **Step 1: Implement entry addition** — Allocate space, update count, repoint
- [ ] **Step 2: Implement entry deletion** — Mark as unused, compact if possible
- [ ] **Step 3: Wire into address list controls** — Add/Remove buttons
- [ ] **Step 4: Run tests and commit**

### Task 20: Patch Manager

**Files:**
- Modify: `FEBuilderGBA.Avalonia/ViewModels/PatchFormUninstallDialogViewModel.cs`
- Create: `FEBuilderGBA.Avalonia/ViewModels/PatchManagerViewModel.cs`
- Create: `FEBuilderGBA.Avalonia/Views/PatchManagerView.axaml`

- [ ] **Step 1: Implement patch listing** — Enumerate patches from config/patch2/{VERSION}/
- [ ] **Step 2: Add installation status** — Detect installed patches
- [ ] **Step 3: Add install/uninstall** — Apply/remove patches with undo
- [ ] **Step 4: Run tests and commit**

---

## Dependency Graph

```
Task 1 (CG/BG bug fix) ─── no dependencies
Task 2 (FE6 class bug) ─── no dependencies
Task 3 (Item computed)  ─── no dependencies
Task 4 (Unit growth)    ─── no dependencies
Task 5 (Class growth)   ─── no dependencies
Task 6 (PatchDetection) ─── no dependencies
Task 7 (Wire patches)   ─── depends on Task 6
Task 8 (Portrait render) ── no dependencies
Task 9 (Portrait export) ── depends on Task 8
Task 10 (Image import)  ─── no dependencies
Task 11 (EventScript)   ─── no dependencies
Task 12 (EventCond)     ─── no dependencies
Task 13 (EventUnit)     ─── no dependencies
Task 14 (Song tracks)   ─── no dependencies
Task 15 (Instruments)   ─── no dependencies
Task 16 (MIDI)          ─── depends on Task 14
Task 17 (Map editing)   ─── no dependencies
Task 18 (MapChange)     ─── no dependencies
Task 19 (Data expand)   ─── no dependencies
Task 20 (Patch manager) ─── depends on Task 6
```

Tasks 1-6, 8, 10-15, 17-19 can all be executed in parallel.
