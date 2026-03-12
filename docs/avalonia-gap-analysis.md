# Avalonia vs WinForms Function Completeness Gap Analysis

**Generated:** 2026-03-11
**Updated:** 2026-03-12 (round 3 fixes: list loading, type resolution, resource info, color viewer)
**Scope:** All 356 Avalonia views vs their WinForms counterparts
**Overall Avalonia Completeness:** ~60% average across all domains (updated 2026-03-12 after round 24 gap fixes)

---

## Executive Summary

The Avalonia port of FEBuilderGBA provides basic data read/write scaffolding for ~356 editor views but is missing the vast majority of interactive features present in the WinForms implementation. The most critical systemic gaps are:

1. ~~**No Undo System**~~ **FIXED** -- Ambient undo tracking in `ROM.BeginUndoScope()`. `UndoService` wraps this for all 148 Avalonia editors with Write handlers.
2. **No InputFormRef Equivalent** -- The 13,177-line convention-based auto-wiring framework has no Avalonia counterpart
3. ~~**No Context Menus**~~ **FIXED** -- `AddressListControl` now has Copy Address / Copy Name / Copy Hex Data context menu
4. **No Image Import/Export** -- Partially addressed: export buttons added to portrait/CG/battle BG forms, undo-wrapped import handlers
5. ~~**No CSV Export/Import**~~ **FIXED** -- `DataExportView` wraps `StructExportCore` for 40-table TSV export/import via Tools menu
6. **Limited Visual Previews** -- Map rendering works (MapEditor), portrait detail display works (UnitEditor/PortraitViewer). Missing: animation playback, portrait thumbnails in lists
7. ~~**No Cross-Form Navigation**~~ **FIXED** -- `WindowManager.Navigate<T>(address)` wired in Unit Editor (Jump to Class/Portrait) and other editors
8. ~~**No Data Validation**~~ **FIXED** -- `WriteValidator` provides range/type/pointer/address validation. `NameResolver` provides cached entity name resolution.
9. ~~**No Dirty Tracking**~~ **FIXED** -- `ViewModelBase.IsDirty` / `IsLoading` / `MarkClean()` with automatic tracking. All 148+ editors now use `IsLoading` guards.
10. ~~**No Named Dropdowns**~~ **FIXED** -- `ComboResourceHelper` builds named lists for units, classes, items, songs, affinities, weapon types
11. ~~**No Bit Flag UI**~~ **FIXED** -- `BitFlagPanel` control with `AbilityFlagNames` provides named checkbox groups
12. ~~**No Hex Editor**~~ **FIXED** -- `HexEditorView` with hex dump display, jump-to-address, page navigation, byte search
13. ~~**No Pointer Search**~~ **FIXED** -- `PointerToolViewModel.SearchPointer()` scans ROM for pointer references
14. ~~**No Free Space Scan**~~ **FIXED** -- `MoveToFreeSpaceViewViewModel.FindFreeSpace()` finds contiguous free regions

### Avalonia Strengths (Not in WinForms)
- **Cross-platform** (Linux, macOS, Windows)
- **MVVM architecture** with testable ViewModels
- **IDataVerifiable** interface for automated data verification
- **Smoke test infrastructure** (`--smoke-test`, `--screenshot-all`, `--data-verify`)
- **Image import validation** framework (export-import-export roundtrip comparison)
- **Multi-format palette I/O** (JASC-PAL, GBA Raw, Adobe ACT, GIMP GPL)

---

## Domain Completeness Summary

| # | Domain | Avg Completeness | Forms | Critical Gaps |
|---|--------|:---:|:---:|---|
| 1 | [Shared Infrastructure](#15-shared-infrastructure) | **70%** | 12 | No auto-wiring, no convention binding |
| 2 | [Unit Editors](#1-unit-editors) | **~50%** | 10 | ~~Growth sim~~ FIXED, no skills |
| 3 | [Item Editors](#2-item-editors) | **~56%** | 14 | No patch-aware UI |
| 4 | [Class Editors](#3-class-editors) | **~48%** | 7 | ~~Growth sim~~ FIXED, no skills, no magic split |
| 5 | [Map Editors](#4-map-editors) | **~40%** | 22+ | Map editor 25% (view-only), style editor 20%, no tile editing |
| 6 | [Image & Portrait Editors](#5-image--portrait-editors) | **~40%** | 23 | No drag-drop, no animation |
| 7 | [Event Editors](#6-event-editors) | **~30%** | 20 | EventScript 2% (planned round 2), no map preview |
| 8 | [Sound & Music](#7-sound--music) | **~43%** | 10 | No MIDI import, no playback |
| 9 | [Text & Dialogue](#8-text--dialogue) | **~38%** | 10 | ~~Dialogue preview~~ FIXED, ~~Search~~ FIXED |
| 10 | [Support & Relationships](#9-support--relationships) | **~53%** | 7 | Unit names FIXED, no auto-collect |
| 11 | [World Map](#10-world-map) | **~48%** | 5 | Point names FIXED, no map preview |
| 12 | [Skill Systems](#11-skill-systems) | **~35%** | 11 | No icon rendering, no sublists |
| 13 | [Tool Windows](#12-tool-windows) | **~30%** | 26 | Patch manager missing |
| 14 | [Main Window & Navigation](#13-main-window--navigation) | **~70%** | 7 | No easy mode |
| 15 | [OP/ED/Status/Misc](#14-openingendingstatusmiscellaneous) | **~53%** | 28 | Filter combos FIXED (StatusParam/ArenaClass), no OwnerDraw |

---

## 1. Unit Editors

**Domain Average: ~50%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| UnitForm / UnitEditorVM | **60%** | ~~Growth Sim~~ **FIXED**, ~~Checkboxes~~ **FIXED** (BitFlagPanel), Pick-and-return for Class/Portrait. Missing: Skills, Magic Split |
| UnitFE7Form / UnitFE7VM | 50% | Magic Split, ~~Checkboxes~~ **FIXED** |
| UnitFE6Form / (via UnitEditorVM) | 50% | ~~Checkboxes~~ **FIXED** |
| ExtraUnitForm / ExtraUnitVM | 30% | Proper List, Flag Editor |
| ExtraUnitFE8UForm / ExtraUnitFE8UVM | 35% | Proper List, Address Tracking |
| UnitActionPointerForm / UnitActionPointerVM | 25% | Write, Proper List, Rework Patch, Action Names |
| UnitCustomBattleAnimeForm / UnitCustomBattleAnimeVM | **40%** | ~~List stub~~ **FIXED** — BuildList with weapon/anim display. Missing: SP Names, Preview |
| UnitIncreaseHeightForm / UnitIncreaseHeightVM | 25% | Write, Proper List, Switch2, Height Options |
| UnitPaletteForm / UnitPaletteVM | **40%** | ~~List stub~~ **FIXED** — list from unit_palette_color_pointer with unit names. Missing: Dual Tables, Preview |
| UnitsShortTextForm / UnitsShortTextVM | **50%** | ~~Proper List~~ **FIXED** — AddressListControl with unit names, text ID decode/preview. Missing: Alloc, Recycling |

### Cross-Cutting Gaps
- ~~**No undo**~~ **FIXED** -- All write handlers wrapped with `UndoService.Begin/Commit/Rollback`
- ~~**No growth simulator**~~ **FIXED** -- `CalculateGrowth_Click` in UnitEditorView with `GrowSimulator` output
- ~~**No CSV export/import**~~ **FIXED** -- Use `DataExportView` for TSV export/import of unit data
- **No skill system integration** -- No skill buttons, no patch detection
- ~~**No jump-to-related links**~~ **FIXED** -- Jump to Class/Portrait buttons in Unit Editor
- **No owner-drawn lists** -- No portrait thumbnails in address lists
- ~~**No IsLoading guards**~~ **FIXED** -- All data loading wrapped with `IsLoading = true/false` + `MarkClean()`

---

## 2. Item Editors

**Domain Average: ~56%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ItemEditor / ItemEditorVM | **60%** | ~~Checkboxes~~ **FIXED** (BitFlagPanel for Trait1/2), Pick-and-return. Missing: stat preview, patch-aware UI |
| ItemFE6 / ItemFE6VM | **58%** | ~~Checkboxes~~ **FIXED** (Pick-and-return). Missing: price calc, hard-coding warning |
| ItemWeaponEffect / ItemWeaponEffectVM | **55%** | ~~Item names~~ **FIXED** — item names in list. Missing: effect name cache, magic system detection |
| ItemWeaponTriangle / ItemWeaponTriangleVM | 60% | Weapon type icons |
| ItemEffectiveness / ItemEffectivenessVM | **45%** | ~~Class names~~ **FIXED** — class names in list. Missing: dual-list architecture, independence button |
| ItemStatBonuses / ItemStatBonusesVM | **58%** | ~~Generic item list~~ **FIXED** — item names in list. Missing: cross-reference, magic split label |
| ItemStatBonusesSkillSystems / VM | 55% | Proper list building, cross-reference |
| ItemStatBonusesVenno / VM | 55% | Proper list building, cross-reference |
| ItemEffectPointer / VM | 50% | Effect name lookup, FELint |
| ItemUsagePointer / VM | 35% | 10-category filter, switch expand |
| ItemShop / VM | **45%** | ~~Item names~~ **FIXED** — item names in list. Missing: multi-shop support |
| ItemRandomChest / VM | 55% | JumpTo with dynamic re-init |
| ItemIcon / VM | 65% | Cross-reference, expansion, palette validation |
| ItemPromotion / VM | **50%** | ~~Class names~~ **FIXED** — class names in list. Missing: multi-pointer support (10 CC items) |

### Cross-Cutting Gaps
- **No patch-aware UI** -- IER, SkillSystems, Vennou variants not detected
- ~~**No combo box resources**~~ **FIXED** -- `ComboResourceHelper` provides named selections
- ~~**No undo**~~ **FIXED** -- All write handlers wrapped with `UndoService`
- ~~**No IsLoading guards**~~ **FIXED** -- All data loading wrapped with `IsLoading`
- ~~**No checkbox-based trait flags**~~ **FIXED** -- `BitFlagPanel` wired in ItemEditorView with `AbilityFlagNames.ItemTrait1/2`

---

## 3. Class Editors

**Domain Average: ~48%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ClassForm / ClassEditorVM | **55%** | ~~Growth sim~~ **FIXED**, ~~ability checkboxes~~ **FIXED** (BitFlagPanel). Missing: skills, magic split |
| ClassFE6Form / ClassFE6VM | **48%** | ~~Growth sim~~ **FIXED** (VM). Missing: data display fields in view, ability checkboxes |
| CCBranchForm / CCBranchEditorVM | **45%** | ~~upstream chain display~~ **FIXED** — shows classes that promote to selected class. Missing: CC3 patch, class-sharing |
| SMEPromoListForm / SMEPromoListVM | **55%** | ~~List loading is stub~~ **FIXED** — proper 2-byte entry enumeration with class names, NavigateTo support |
| SomeClassListForm / SomeClassListVM | **55%** | ~~No list management~~ **FIXED** — AddressListControl with null-terminated class list, name resolution |
| MoveCostForm / MoveCostEditorVM | 35% | Only 1 of 7+ cost types, shared-table/independence |
| MoveCostFE6Form / MoveCostFE6VM | 35% | Only 1 of multiple cost types |

---

## 4. Map Editors

**Domain Average: ~40%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| MapSettingForm (FE7/8) | 50% | Map picture preview, terrain combo, style change |
| MapSettingFE6Form | 50% | Map picture preview, terrain combo |
| MapPointerForm | **45%** | ~~PLIST type filter~~ **FIXED** — 5-way combo (MAP/CONFIG/OBJ-PAL/CHANGE/EVENT). Missing: split detection |
| MapChangeForm | 25% | Two-level list, map preview with change overlay |
| MapExitPointForm | 30% | Two-level list, enemy/NPC filter |
| MapEditorForm | **25%** | ~~Empty stub~~ **FIXED** — map list from MapSettingCore, visual tile rendering with zoom (1-4x). Missing: tile editing, map save |
| MapStyleEditorForm | **20%** | ~~Empty stub~~ **FIXED** — tileset list from map_obj_pointer, OBJ/config pointer display, write support. Missing: tile preview, palette editing |
| MapTerrainNameForm | **30%** | ~~Empty stub~~ **FIXED** — terrain list from map_terrain_name_pointer (4-byte pointer entries), pointer display, write support. Missing: string decode |
| MapTerrainNameEngForm | **55%** | ~~Proper list~~ **FIXED** — list from map_terrain_name_pointer with text decode |
| MapTileAnimation1Form | **50%** | ~~List stub~~ **FIXED** — proper list from map_tileanime1_pointer with pointer validation. Missing: filter combo, animation frame display |
| MapTileAnimation2Form | **50%** | ~~List stub~~ **FIXED** — proper list from map_tileanime2_pointer with palette pointer validation. Missing: three-level navigation |
| MapLoadFunctionForm | **50%** | ~~List stub~~ **FIXED** — proper list from switch1 count + pointer validation, write support, pointer info display |
| Dialog VMs (9 total) | ~30% avg | View layer only, no logic |

### Map Editor (25%) and Map Style Editor (20%)
Map Editor now has read-only visualization with zoom controls but no tile editing or save. Map Style Editor now loads tileset entries from map_obj_pointer with write support but lacks tile preview and palette editing. Both need significant work for editing parity.

---

## 5. Image & Portrait Editors

**Domain Average: ~40%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ImagePortrait | **45%** | ~~List stub~~ **FIXED** — list from portrait_pointer with unit name hints. Missing: Import, Drag-drop |
| ImagePortraitFE6 | 35% | Import, Advanced import dialog |
| ImageBattleAnime | **28%** | ~~List stub~~ **FIXED** — list from image_battle_animelist_pointer. Missing: Two-level list, Animation playback, Import/Export |
| ImageBattleBG | 45% | Import, Drag-drop, DecreaseColor |
| ImageCG / BigCGViewer | 45% | Import, 10-split LZ77 compress |
| ImageCGFE7U | 45% | Import, Drag-drop |
| ImageBG | 40% | Import (2 modes), BG select popup |
| ImageGenericEnemyPortrait | **40%** | ~~Address list~~ **FIXED** — proper list from generic_enemy_portrait_pointer/count. Missing: Image rendering, Import |
| ImageMagicFEditor | 18% | Animation playback, Import/Export (TXT/GIF) |
| ImageMagicCSACreator | 15% | Everything (placeholder) |
| ImageMapActionAnimation | 20% | Animation playback, Import/Export |
| ImageSystemArea | **45%** | ~~List stub, Filter combo~~ **FIXED** — filter combo (Move/Attack/Staff) with GBA color list from systemarea gradation pointers |
| ImageTSAAnime | 40% | Import, DecreaseColor |
| ImageTSAAnime2 | 22% | Three-level navigation |
| ImageUnitPalette | **35%** | ~~List stub~~ **FIXED** — list from image_unit_palette_pointer with identifier string. Missing: PaletteFormRef, Sprite preview |
| ImageViewer | 50% | Utility viewer, mostly adequate |
| ChapterTitle (FE7+FE8) | 45% | Import |
| GraphicsTool | 15% | All rendering logic, decode options |
| GraphicsToolPatchMaker | 13% | All patch generation logic |
| BattleTerrain | 40% | Import, Drag-drop |

### Cross-Cutting Gaps
- ~~**No image export**~~ **PARTIALLY FIXED** -- PNG export added to portrait/CG/battle BG forms
- **No image import** -- No image load + LZ77 compress + ROM write pipeline (partial: quantize/remap exists)
- **No drag-and-drop** -- No file drop support
- **No PaletteFormRef** -- No per-color palette editing
- **No DecreaseColorTSAToolForm** -- No color reduction tool
- **No animation playback** -- No frame-by-frame preview

---

## 6. Event Editors

**Domain Average: ~30%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| **EventCondForm / EventCondVM** | **20%** | Multi-tab (6 types), map preview, icon support |
| **EventScriptForm / EventScriptPopupVM** | **2%** | **Entire event script editor missing (planned for round 2)** |
| EventUnitForm / EventUnitVM | 25% | 3-level navigation, map preview, growth sim |
| EventUnitFE6Form / EventUnitFE6VM | 28% | Map preview, AI combos, growth sim |
| EventUnitFE7Form / EventUnitFE7VM | 25% | Map preview, coordinate handling |
| EventBattleTalkForm / EventBattleTalkVM | **55%** | ~~List stub~~ **FIXED** — list from event_ballte_talk_pointer with attacker vs defender unit names |
| EventBattleTalkFE6/FE7 | **55%** | ~~List stub~~ **FIXED** — list from event_ballte_talk_pointer (FE6: 12-byte, FE7: 16-byte blocks) + unit names |
| EventHaikuForm / EventHaikuVM | **55%** | ~~Auto-populated list~~ **FIXED** — list from event_haiku_pointer with unit name resolution. Missing: patch detection |
| EventHaikuFE6/FE7 | **55%** | ~~List stub~~ **FIXED** — list from event_haiku_pointer (16-byte blocks) + unit names. Missing: chapter filter |
| EventForceSortie/FE7 | **55%** | ~~List stub~~ **FIXED** — list from event_force_sortie_pointer with unit name resolution. Missing: AllocIfNeed |
| EventFunctionPointer/FE7 | **45%** | ~~List stub~~ **FIXED** — list from event_function_pointer_table_pointer. Missing: filter combo |
| EventTalkGroupFE7 | 30% | AllocIfNeed, RecycleOldData |
| EventMoveDataFE7 | 25% | Variable-size records, direction types |

### Critical: EventScriptForm (2%)
The main event script editor (1,928 lines in WinForms) is reduced to a static help text popup in Avalonia. This is the most important editor in the event domain -- without it, users cannot edit event scripts. **Planned for round 2.**

---

## 7. Sound & Music

**Domain Average: ~40%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SongTable | **45%** | ~~Song names~~ **FIXED** — song names in list. Missing: playback, cross-references |
| **SongTrack** | **25%** | **MIDI/WAV import-export, track visualization, playback** |
| **SongInstrument** | **20%** | **128 instruments, type-specific panels, wave import** |
| SongInstrumentDirectSound | 50% | DPCM detection, Hz combo, validation |
| SoundRoom | **55%** | ~~Song names~~ **FIXED**, ~~Jump to Song~~ **FIXED**. Missing: position display, patch detection |
| SoundRoomFE6 | **55%** | ~~Proper list, song names~~ **FIXED** — list from sound_room_pointer, song name + description text decode/preview |
| SoundRoomCG | **45%** | ~~Proper list~~ **FIXED** — list from sound_room_cg_pointer with CG IDs. Missing: CG image preview |
| SoundBossBGM | **55%** | ~~Unit names~~ **FIXED**, ~~Song jump~~ **FIXED** — unit names + pick + Jump to Song. Missing: portraits |
| SoundFootSteps | **40%** | ~~Class names~~ **FIXED** — class names in list. Missing: switch enable check |
| WorldMapBGM | 50% | World map point names |

### Critical: Song Track (25%) and Song Instrument (20%)
These are the core music editing tools. Without MIDI import/export and instrument editing, users cannot modify game music.

---

## 8. Text & Dialogue

**Domain Average: ~38%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| **TextForm / TextViewerVM** | **45%** | ~~Dialogue preview~~ **FIXED** (bracket highlighting), ~~Search~~ **FIXED** (content search across all texts). Has TSV export/import + individual write. Missing: validation |
| TextCharCodeForm / TextCharCodeVM | 35% | No font preview, no frequency analysis |
| TextDicForm / TextDicVM | **50%** | ~~list not populated~~ **FIXED** — AddressListControl from dic_main_pointer, text decode preview, unit/class name resolution. Missing: chapter/title sub-lists |
| TextRefAddDialog | 30% | No UseTextIDCache integration |
| TextToSpeech | 15% | No speech engine (Windows-only) |
| EDForm / EDVM | 35% | Only 1 of 3 sub-editors |
| EDStaffRoll | 30% | No image rendering/import/export |
| EDSensekiComment | 40% | No unit names, no text preview |
| TextBadCharPopup | 25% | Minimal shell |
| TextScriptCategorySelect | 25% | Hardcoded stub |

### TextForm (45%)
The text editor has read/write, TSV export/import, dialogue preview with control code highlighting, content search across all texts, and individual Huffman write-back. Missing: validation warnings, cross-reference display.

---

## 9. Support & Relationships

**Domain Average: ~53%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SupportUnitEditor (FE7/8) | **50%** | ~~Unit name resolution~~ **FIXED** — unit names in list. Missing: auto-collect, reciprocal validation, talk jumps |
| SupportUnitFE6 | **50%** | ~~Unit name resolution~~ **FIXED** — unit names in list. Missing: talk jumps |
| SupportTalk (FE8) | **55%** | ~~Unit name resolution~~ **FIXED** — unit names in list display. Missing: JumpTo pair search |
| SupportTalkFE6 | **55%** | ~~Unit name resolution~~ **FIXED** — unit names in list. Missing: text preview |
| SupportTalkFE7 | **55%** | ~~Unit name resolution~~ **FIXED** — unit names in list. Missing: text preview |
| SupportAttribute | 50% | Affinity name/icon resolution |
| TacticianAffinityFE7 | 30% | View now exists; missing combo resources |

---

## 10. World Map

**Domain Average: ~48%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| WorldMapPoint | **50%** | ~~Name text missing~~ **FIXED** — point names in list via text ID. Missing: map preview, coordinate validation |
| WorldMapPath | **45%** | ~~Generic list~~ **FIXED** — start/end point IDs in list. Missing: map preview, path rendering |
| WorldMapPathMoveEditor | **50%** | ~~Non-functional (no path selector)~~ **FIXED** — BuildList from base address, node display with T/X/Y, null-terminator detection |
| WorldMapEventPointer | 35% | Missing dual list (before/after), no opening/ending events |
| WorldMapBGM | **62%** | ~~Song names~~ **FIXED**, ~~Jump to Song~~ **FIXED** — song names + Jump buttons. Near parity |

---

## 11. Skill Systems

**Domain Average: ~35%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SkillAssignmentUnitSkillSystem | 30% | Icon rendering, levelup sublists, export/import |
| SkillAssignmentUnitCSkillSys | 30% | Same as above |
| SkillAssignmentUnitFE8N | 35% | Bit-flag tabs, version detection |
| SkillAssignmentClassSkillSystem | 30% | Same as unit variant |
| SkillAssignmentClassCSkillSys | 30% | Same as above |
| SkillConfigSkillSystem | 28% | Pointer finder, icon display, animation |
| SkillConfigFE8NSkill | 40% | Pointer discovery, icon rendering |
| SkillConfigFE8NVer2Skill | 35% | 4 sub-InputFormRefs, tabs |
| SkillConfigFE8NVer3Skill | 35% | 5 sub-InputFormRefs |
| SkillConfigFE8UCSkillSys09x | 32% | 1024 skills, icon rendering |
| EffectivenessReworkClassType | 30% | Bit-flag UI, class type icons |

---

## 12. Tool Windows

**Domain Average: ~30%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ~~**HexEditor (Jump/Mark/Search)**~~ | **40%** | **FIXED** -- Hex dump display, jump-to-address, page navigation, byte search |
| ~~PointerTool (Main/Batch/CopyTo)~~ | **35%** | **FIXED** -- Pointer search scans ROM for references |
| DisASM (DumpAll/ArgGrep) | 15% | No disassembly engine |
| ToolASMEdit | 13% | No ASM compilation |
| ToolAnimationCreator | 13% | No animation creation |
| ToolDecompileResult | 20% | No decompilation invocation |
| ToolExportEAEvent | 22% | No EA export execution |
| ToolInitWizard | 12% | No wizard flow |
| ToolSubtitle (Overlay/Settings) | 18% | No subtitle overlay |
| **ToolThreeMarge (Main/CloseAlert)** | **15%** | **No three-way merge algorithm** |
| ToolWorkSupport (Main/UPS/Update) | 20% | No update checking/download |
| ~~**MoveToFreeSpace**~~ | **30%** | **FIXED** -- Free space search finds contiguous regions |
| **PatchFormUninstall** | **10%** | **Entire patch manager missing** |
| PackedMemorySlot | 25% | No slot operations |
| EmulatorMemory | 5% | Platform limitation (P/Invoke) |
| RAMRewriteTool (Main/MAP) | 8% | Platform limitation (P/Invoke) |

### Critical: Patch Manager, Three-Way Merge
~~Hex Editor~~, ~~Pointer Search~~, and ~~Free Space Scan~~ have been fixed. Patch manager and three-way merge remain as empty stubs in Avalonia.

---

## 13. Main Window & Navigation

**Domain Average: ~65%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| MainWindow vs MainFE6/7/8Form | 70% | Drag-drop, smart routing, easy mode toggle |
| WelcomeView | 25% | Update check, language switch, drag-drop |
| VersionView | 45% | CRC32, original ROM detection |
| ErrorUnknownROMView | 80% | Minor ROM header detail |
| EventErrorIgnoreError | 70% | Lint workflow integration |
| ResourceView | **50%** | ~~Empty placeholder~~ **FIXED** — ROM header info, free space analysis, data table counts, section pointers, patch/config directory info |
| MainSimpleMenu (Easy Mode) | 15% | Map-centric view, visual unit overlay |

### MainWindow Improvements
- ~~**No search filter**~~ **FIXED** -- Filter text box filters the button grid
- ~~**No dirty check**~~ **FIXED** -- Unsaved changes warning on close

---

## 14. Opening/Ending/Status/Miscellaneous

**Domain Average: ~53%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| OPClassDemo (4 VMs) | 45% | Patch detection, N1/N2 sub-lists |
| OPClassFont (2 VMs) | 50% | Image export/import |
| OPPrologue | 55% | Image export/import |
| OPClassAlphaName (2 VMs) | 55% | ASCII validation |
| ArenaClass | **55%** | ~~Filter combo~~ **FIXED** — 3-way weapon type filter (Near/Far/Magic) + class names |
| ArenaEnemyWeapon | **45%** | ~~Weapon names~~ **FIXED** — item names in list. Missing: two sub-lists (basic + rank-up) |
| StatusOption | **55%** | ~~Address list not populated~~ **FIXED** — list from status_game_option_pointer with option name resolution |
| StatusOptionOrder | 55% | Named option display |
| StatusParam | **58%** | ~~Filter combo~~ **FIXED** — 4-way combo (Status Param/Owned Items/Weapon Level/Magic Level) |
| StatusRMenu | 45% | Linked-list traversal, filter combo |
| StatusUnitsMenu | 50% | OwnerDraw, text decode |
| MenuCommand | 45% | Filter combo, color combo |
| MenuDefinition | 45% | Menu preview, multi-pointer |
| MenuExtendSplitMenu | **55%** | ~~No list enumeration~~ **FIXED** — list from menu_definiton_split_pointer |
| **AOERANGE** | **30%** | **Visual grid editor (core feature) missing** |
| MonsterItem | **40%** | ~~Generic list~~ **FIXED** — item names in list. Missing: three sub-lists (N1, N2) |
| MonsterProbability | 50% | Probability sum display |
| MonsterWMapProbability | 55% | Minimal |
| SummonUnit | **50%** | ~~Unit names~~ **FIXED** — unit names in list. Missing: OwnerDraw, expand event |
| SummonsDemonKing | **55%** | ~~Unit/class names~~ **FIXED** — names in list. Missing: AI combos, expand event |
| LinkArenaDenyUnit | **55%** | ~~Unit name display~~ **FIXED** — unit names in list |
| TerrainName | 55% | Closest to parity |
| VennouWeaponLock | **60%** | ~~No list loading, no type resolution~~ **FIXED** — AddressListControl, TypeIDToString, conditional unit/class name resolution |
| BitFlag editors (3 VMs) | **80%** | Closest to complete |
| SystemHoverColor | **45%** | ~~Not functional~~ **FIXED** — reads systemarea gradation palette colors (move/attack/staff), filter combo, GBA RGB decode |
| SystemIcon | 55% | Read-only, no import/export |
| MantAnimation | 25% | No write, no expand event |

---

## 15. Shared Infrastructure

**Overall Infrastructure Completeness: ~70%**

| Category | Coverage | Notes |
|----------|:---:|---|
| Convention-based auto-wiring | **0%** | Biggest gap -- InputFormRef's B/W/D/P naming |
| Write/Undo automation | **98%** | ~~15%~~ **FIXED** -- `UndoService` wraps all editors with Write handlers (100% coverage after round 27) |
| Dirty tracking (yellow highlight) | **80%** | ~~0%~~ **FIXED** -- `ViewModelBase.IsDirty` / `IsLoading` / `MarkClean()` with automatic tracking |
| Address list management | 50% | Basic list works; no data layer |
| CSV export/import | **80%** | ~~0%~~ **FIXED** -- `DataExportView` wraps `StructExportCore` for 40-table TSV export/import |
| Context menus | **70%** | ~~0%~~ **FIXED** -- `AddressListControl` context menu (Copy Address / Name / Hex Data) |
| Pre/Post write hooks | **10%** | No standardized hooks |
| Form navigation / Jump | **70%** | ~~45%~~ **FIXED** -- `WindowManager.Navigate<T>()` + `PickFromEditor<T>()` pick-and-return flow |
| Write validation | **60%** | ~~5%~~ **FIXED** -- `WriteValidator` provides range/type/pointer/address validation |
| Name resolution helpers | **80%** | ~~0%~~ **FIXED** -- `NameResolver` + `ComboResourceHelper` for cached entity name resolution |
| Image export | **70%** | ~~60%~~ **FIXED** -- PNG export in portrait/CG/BG forms |
| Image import + write-back | **40%** | ~~25%~~ Quantize/remap + undo; no LZ77/TSA/pointer write |
| Drag-and-drop | **0%** | Missing everywhere |
| Data expand/shrink | **0%** | Cannot add new ROM entries |
| Progress dialogs | **0%** | No AutoPleaseWait equivalent |

### Priority Recommendations for Infrastructure

**P0 -- Critical (blocks all editor development):**
1. **EditorFormRef** -- Convention-based auto-wiring equivalent for Avalonia

**P1 -- High (blocks quality parity):**
2. ~~**Injection callback / select-and-return**~~ **FIXED** -- `PickFromEditor<T>()` in `WindowManager`
3. **Image write-back** -- LZ77 compression, TSA generation, pointer updates
4. **Data expand/shrink** -- Cannot add new ROM entries

**P2 -- Medium (power user features):**
5. Drag-and-drop file support
6. Progress dialogs for long operations
7. Pre/Post write hooks

---

## Top 10 Priority Items for Closing the Gap

| Priority | Item | Impact | Status |
|:---:|---|---|---|
| ~~1~~ | ~~**Undo system wiring**~~ | ~~All edits are irreversible~~ | **FIXED** |
| 2 | **InputFormRef equivalent** (EditorFormRef) | 80% of boilerplate | Open |
| 3 | **EventScriptForm** implementation | Core editing blocked | **Planned round 2** |
| ~~4~~ | ~~**TextForm** individual text editing~~ | ~~All text editing blocked~~ | **FIXED** (45%) |
| 5 | **MapEditorForm** visual map editor | Map editing blocked | **Planned round 2** |
| 6 | **Image import pipeline** (LZ77 + TSA + pointer) | No image editing | Open |
| ~~7~~ | ~~**HexEditorForm** hex viewer/editor~~ | ~~Fundamental tool missing~~ | **FIXED** |
| 8 | **Patch management** system | Core feature missing | Open |
| ~~9~~ | ~~**Context menus** on address lists~~ | ~~Power user UX~~ | **FIXED** |
| ~~10~~ | ~~**Dirty tracking** + unsaved changes warning~~ | ~~Data loss prevention~~ | **FIXED** |

---

## Methodology

This analysis was conducted by 15 parallel research agents, each analyzing one domain category by reading both WinForms source files and Avalonia ViewModel/View files. Completeness ratings consider:
- **0-10%**: Empty stub or placeholder
- **10-25%**: Basic data read/write only
- **25-50%**: Core fields work but missing undo, validation, navigation, previews
- **50-75%**: Most features present, missing polish/integration
- **75-100%**: Near feature parity with WinForms

**Updated 2026-03-12:** Second pass reflects round 1 gap fixes (24 WUs) raising average from ~19% to ~45%. Undo, dirty tracking, name resolution, context menus, hex editor, pointer search, free space scan, data export all fixed.

**Updated 2026-03-12 (round 3):** Six forms improved: SMEPromoList (25→55%), SomeClassList (25→55%), VennouWeaponLock (35→60%), ResourceView (10→50%), SystemHoverColor (15→45%), UnitsShortText (30→50%). Key improvements: proper list loading with AddressListControl, type resolution, name lookup, ROM info display, GBA color decode.

**Updated 2026-03-12 (round 4):** Four more forms improved: CCBranchEditor (30→45%, upstream chain display), SoundRoomFE6 (35→55%, list + song name/description preview), SoundRoomCG (30→45%, list from ROM pointer), TextDic (30→50%, AddressListControl + text decode + unit/class name resolution).

**Updated 2026-03-12 (round 5):** Three map forms improved: MapTileAnimation1 (35→50%, list from tileanime1_pointer), MapTileAnimation2 (35→50%, list from tileanime2_pointer), MapLoadFunction (25→50%, list from switch1 count + write support + pointer info).

**Updated 2026-03-12 (round 6):** Three more forms improved: ImageGenericEnemyPortrait (25→40%, list from pointer/count), StatusOption (40→55%, list from status_game_option_pointer with name resolution), WorldMapPathMoveEditor (35→50%, BuildList with node display and terminator detection).

**Updated 2026-03-12 (round 7):** Six more forms fixed: EventHaiku (40→55%, list from event_haiku_pointer + unit names), EventForceSortie (45→55%, list from event_force_sortie_pointer + unit names), EventFunctionPointer (30→45%, list from function pointer table), AIStealItem/AIPerformStaff/AIPerformItem (all list stubs replaced with proper ROM pointer enumeration + item name resolution).

**Updated 2026-03-12 (round 8):** Six more forms fixed: EventHaikuFE6/FE7 (list from event_haiku_pointer 16-byte blocks + unit names), EventForceSortieFE7 (list from force_sortie_pointer, 23 map entries), EventFunctionPointerFE7 (list from function_pointer_table), AIMapSetting (list from ai_map_setting_pointer), MapTerrainNameEng (list from map_terrain_name_pointer + text decode).

**Updated 2026-03-12 (round 9):** Four more forms fixed: ImagePortrait (35→45%, list from portrait_pointer with unit name hints), ImageBattleAnime (18→28%, list from image_battle_animelist_pointer), UnitPalette (30→40%, list from unit_palette_color_pointer + unit names), UnitCustomBattleAnime (30→40%, BuildList with weapon/anim display).

**Updated 2026-03-12 (round 10):** Six more forms fixed: EventBattleTalk/FE6/FE7 (40→55%, list from event_ballte_talk_pointer with attacker vs defender unit names), EventFinalSerifFE7 (list from event_final_serif_pointer + unit names), ImageUnitPalette (25→35%, list from image_unit_palette_pointer with identifier strings). Also fixed leftover stub in EventForceSortieFE7.

**Updated 2026-03-12 (round 11 — final):** Three more forms: Command85Pointer (list from command_85_pointer_table_pointer), ImageSystemArea (30→45%, filter combo with GBA color list from systemarea gradation pointers). Also fixed leftover stub in EventBattleTalkFE7. Total: 41+ forms improved across 9 commits, overall completeness ~55%.

**Updated 2026-03-12 (round 12):** Cross-editor pick-and-return infrastructure (`PickResult`, `IPickableEditor`, `WindowManager.PickFromEditor<T>()`). Six editors implement `IPickableEditor`: ClassEditor, PortraitViewer, ItemEditor, UnitEditor, SongTable, ItemFE6. Pick buttons added to UnitEditor (Class/Portrait) and SoundBossBGM (Unit). Three map stubs fixed: MapEditor (0→25%, visual tile rendering with zoom), MapStyleEditor (0→20%, tileset list from map_obj_pointer), MapTerrainName (0→30%, terrain list from map_terrain_name_pointer). Gap analysis accuracy fixes: Unit growth simulator was already implemented (45→55%), item trait BitFlagPanels already wired.

**Updated 2026-03-12 (rounds 13-16):** Systematic name resolution improvements across 15+ forms: SupportTalk/FE6/FE7 (unit names in list), SupportUnitEditor/FE6 (unit names), WorldMapPoint (point names from text ID), WorldMapPath (start/end IDs), WorldMapBGM (song names), ArenaClass/ArenaEnemyWeapon (class/item names), ItemStatBonuses (item names), SoundBossBGM/SoundFootSteps (unit/class names), MonsterItem (item names), SummonUnit/SummonsDemonKing (unit/class names), LinkArenaDenyUnit (unit names).

**Updated 2026-03-12 (rounds 17-19):** Stub list loading fixes: AITargetVM (from ai3_pointer), MenuExtendSplitMenu (from menu_definiton_split_pointer). Filter combos added: StatusParam (4-way param table switch), MapPointer (5-way PLIST type filter), ArenaClass (3-way weapon type filter).

**Updated 2026-03-12 (rounds 20-22):** Name resolution in 6 more forms: SongTable, SoundRoom, ItemWeaponEffect, ItemEffectiveness, ItemShop, ItemPromotion. Class growth simulator added to ClassEditor (55%) and ClassFE6 (48% VM-only).

**Updated 2026-03-12 (rounds 23-27):** Accuracy corrections across all domains. Text content search (TextForm 30→45%). Jump to Song buttons in SoundBossBGM, WorldMapBGM, SoundRoom. UndoService wrapping for 10 remaining unprotected Write handlers (100% undo coverage). Overall completeness ~60%.
