# Avalonia vs WinForms Function Completeness Gap Analysis

**Generated:** 2026-03-11
**Scope:** All 356 Avalonia views vs their WinForms counterparts
**Overall Avalonia Completeness:** ~19% average across all domains

---

## Executive Summary

The Avalonia port of FEBuilderGBA provides basic data read/write scaffolding for ~356 editor views but is missing the vast majority of interactive features present in the WinForms implementation. The most critical systemic gaps are:

1. ~~**No Undo System**~~ **FIXED** -- Ambient undo tracking in `ROM.BeginUndoScope()` makes all 1496 `rom.write_*()` call sites undo-trackable. `UndoService` wraps this for Avalonia editors.
2. **No InputFormRef Equivalent** -- The 13,177-line convention-based auto-wiring framework has no Avalonia counterpart
3. ~~**No Context Menus**~~ **FIXED** -- `AddressListControl` now has Copy Address / Copy Name / Copy Hex Data context menu
4. **No Image Import/Export** -- No form can import or export images
5. ~~**No CSV Export/Import**~~ **FIXED** -- `DataExportView` wraps `StructExportCore` for 40-table TSV export/import via Tools menu
6. **No Visual Previews** -- No map rendering, animation playback, or portrait thumbnails in lists
7. ~~**No Cross-Form Navigation**~~ **PARTIALLY FIXED** -- `WindowManager` now supports `OpenModal`, `FindOpen`, `NavigateAndSelect` patterns
8. ~~**No Data Validation**~~ **FIXED** -- `WriteValidator` provides range/type/pointer/address validation. `NameResolver` provides cached entity name resolution.
9. ~~**No Dirty Tracking**~~ **FIXED** -- `ViewModelBase.IsDirty` / `IsLoading` / `MarkClean()` with automatic tracking via `SetField<T>`

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
| 1 | [Shared Infrastructure](#15-shared-infrastructure) | **20%** | 12 | No auto-wiring, no dirty tracking, no convention binding |
| 2 | [Unit Editors](#1-unit-editors) | **18%** | 10 | No growth simulator, no CSV, no skills, no undo |
| 3 | [Item Editors](#2-item-editors) | **30%** | 14 | No undo, no patch-aware UI, no combo resources |
| 4 | [Class Editors](#3-class-editors) | **18%** | 7 | No growth simulator, no skills, no magic split |
| 5 | [Map Editors](#4-map-editors) | **18%** | 22+ | Map editor 0%, style editor 0%, no rendering |
| 6 | [Image & Portrait Editors](#5-image--portrait-editors) | **20%** | 23 | No import/export, no drag-drop, no animation |
| 7 | [Event Editors](#6-event-editors) | **10%** | 20 | EventScript 2%, EventCond 3%, no map preview |
| 8 | [Sound & Music](#7-sound--music) | **20%** | 10 | No MIDI import, no playback, instruments 5% |
| 9 | [Text & Dialogue](#8-text--dialogue) | **15%** | 10 | No text editing, no dialogue preview, no search |
| 10 | [Support & Relationships](#9-support--relationships) | **27%** | 7 | No auto-collect, no reciprocal validation |
| 11 | [World Map](#10-world-map) | **23%** | 5 | No map preview, no dual event lists |
| 12 | [Skill Systems](#11-skill-systems) | **12%** | 11 | No icon rendering, no sublists, no pointer discovery |
| 13 | [Tool Windows](#12-tool-windows) | **7%** | 26 | Hex editor missing, patch manager missing |
| 14 | [Main Window & Navigation](#13-main-window--navigation) | **40%** | 7 | No dirty check, no search filter, no easy mode |
| 15 | [OP/ED/Status/Misc](#14-openingendingstatusmiscellaneous) | **31%** | 28 | No filter combos, no OwnerDraw, no undo |

---

## 1. Unit Editors

**Domain Average: ~18%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| UnitForm / UnitEditorVM | 25% | Growth Sim, CSV, Skills, Magic Split, Checkboxes, Undo, Jump Links |
| UnitFE7Form / UnitFE7VM | 25% | Growth Sim, Magic Split, Checkboxes, Undo, Jump Links |
| UnitFE6Form / (via UnitEditorVM) | 25% | Growth Sim, Checkboxes, Undo, Jump Links |
| ExtraUnitForm / ExtraUnitVM | 15% | Proper List, Flag Editor, Undo |
| ExtraUnitFE8UForm / ExtraUnitFE8UVM | 20% | Proper List, Address Tracking |
| UnitActionPointerForm / UnitActionPointerVM | 10% | Write, Proper List, Rework Patch, Action Names |
| UnitCustomBattleAnimeForm / UnitCustomBattleAnimeVM | 15% | Dual Lists, SP Names, Independence, Preview |
| UnitIncreaseHeightForm / UnitIncreaseHeightVM | 10% | Write, Proper List, Switch2, Height Options |
| UnitPaletteForm / UnitPaletteVM | 15% | Dual Tables, Battle Anime Preview, Synced Lists |
| UnitsShortTextForm / UnitsShortTextVM | 15% | Proper List, Alloc, JumpTo, Recycling |

### Cross-Cutting Gaps
- **No undo** -- All writes use raw `rom.write_*` with no rollback
- **No growth simulator** -- Cannot preview stat level-ups
- **No CSV export/import** -- No bulk data editing
- **No skill system integration** -- No skill buttons, no patch detection
- **No jump-to-related links** -- No navigation to portrait/class/text editors
- **No owner-drawn lists** -- No portrait thumbnails in address lists

---

## 2. Item Editors

**Domain Average: ~30%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ItemEditor / ItemEditorVM | 30% | Undo, checkboxes, combo resources, stat preview, validation |
| ItemFE6 / ItemFE6VM | 30% | Undo, checkboxes, price calc, hard-coding warning |
| ItemWeaponEffect / ItemWeaponEffectVM | 35% | Effect name cache, magic system detection, JumpTo |
| ItemWeaponTriangle / ItemWeaponTriangleVM | 45% | Weapon type icons, name lookup |
| ItemEffectiveness / ItemEffectivenessVM | 20% | Dual-list architecture, independence button, class names |
| ItemStatBonuses / ItemStatBonusesVM | 35% | Cross-reference list, magic split label |
| ItemStatBonusesSkillSystems / VM | 35% | Proper list building, cross-reference |
| ItemStatBonusesVenno / VM | 35% | Proper list building, cross-reference |
| ItemEffectPointer / VM | 30% | Effect name lookup, FELint |
| ItemUsagePointer / VM | 15% | 10-category filter, switch expand |
| ItemShop / VM | 20% | Multi-shop support (world map + event shops) |
| ItemRandomChest / VM | 40% | JumpTo with dynamic re-init |
| ItemIcon / VM | 50% | Cross-reference, expansion, palette validation |
| ItemPromotion / VM | 25% | Multi-pointer support (10 CC items) |

### Cross-Cutting Gaps
- **No patch-aware UI** -- IER, SkillSystems, Vennou variants not detected
- **No combo box resources** -- Raw numbers instead of named selections
- **No checkbox-based trait flags** -- Raw numeric ability fields

---

## 3. Class Editors

**Domain Average: ~18%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ClassForm / ClassEditorVM | 25% | Growth sim, skills, magic split, export/import, ability checkboxes |
| ClassFE6Form / ClassFE6VM | 25% | Growth sim, ability checkboxes, class extends |
| CCBranchForm / CCBranchEditorVM | 15% | CC3 patch, upstream chain display, class-sharing list |
| SMEPromoListForm / SMEPromoListVM | 10% | List loading is stub, JumpTo, dynamic address |
| SomeClassListForm / SomeClassListVM | 10% | No list management, no dynamic address |
| MoveCostForm / MoveCostEditorVM | 20% | Only 1 of 7+ cost types, shared-table/independence |
| MoveCostFE6Form / MoveCostFE6VM | 20% | Only 1 of multiple cost types |

---

## 4. Map Editors

**Domain Average: ~18%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| MapSettingForm (FE7/8) | 35% | Map picture preview, terrain combo, style change |
| MapSettingFE6Form | 35% | Map picture preview, terrain combo |
| MapPointerForm | 15% | PLIST type filter (7 types), split detection |
| MapChangeForm | 10% | Two-level list, map preview with change overlay |
| MapExitPointForm | 15% | Two-level list, enemy/NPC filter |
| **MapEditorForm** | **0%** | **Empty stub -- entire visual map editor missing** |
| **MapStyleEditorForm** | **0%** | **Empty stub -- tileset/palette editor missing** |
| **MapTerrainNameForm** | **0%** | **Empty stub -- terrain name list missing** |
| MapTerrainNameEngForm | 30% | Proper list, GetName helper |
| MapTileAnimation1Form | 20% | Filter combo, animation frame display |
| MapTileAnimation2Form | 20% | Three-level navigation, palette colors |
| MapLoadFunctionForm | 10% | Function pointer combo, switch validation |
| Dialog VMs (9 total) | ~20% avg | View layer only, no logic |

### Critical: Map Editor (0%) and Map Style Editor (0%)
These are the two most complex forms in the map domain (~2700 and ~1300 lines respectively) and are completely empty stubs. Without them, users cannot visually edit maps or tilesets.

---

## 5. Image & Portrait Editors

**Domain Average: ~20%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| ImagePortrait | 20% | Import/Export, Undo, Drag-drop, Palette link |
| ImagePortraitFE6 | 25% | Import/Export, Advanced import dialog |
| ImageBattleAnime | 8% | Two-level list, Animation playback, Import/Export (4 formats) |
| ImageBattleBG | 30% | Import/Export, Drag-drop, DecreaseColor |
| ImageCG / BigCGViewer | 30% | Import/Export, 10-split LZ77 compress |
| ImageCGFE7U | 30% | Import/Export, Drag-drop |
| ImageBG | 25% | Import/Export (2 modes), BG select popup |
| ImageGenericEnemyPortrait | 10% | Address list, Image rendering, Import/Export |
| ImageMagicFEditor | 8% | Animation playback, Import/Export (TXT/GIF) |
| ImageMagicCSACreator | 5% | Everything (placeholder) |
| ImageMapActionAnimation | 10% | Animation playback, Import/Export |
| ImageSystemArea | 20% | Color swatch rendering, Filter combo |
| ImageTSAAnime | 30% | Import/Export, DecreaseColor |
| ImageTSAAnime2 | 12% | Three-level navigation |
| ImageUnitPalette | 15% | PaletteFormRef (color picker), Sprite preview |
| ImageViewer | 40% | Utility viewer, mostly adequate |
| ChapterTitle (FE7+FE8) | 35% | Import/Export |
| GraphicsTool | 5% | All rendering logic, decode options |
| GraphicsToolPatchMaker | 3% | All patch generation logic |
| BattleTerrain | 30% | Import/Export, Drag-drop |

### Cross-Cutting Gaps
- **No image export** -- No PNG save dialog in any form
- **No image import** -- No image load + LZ77 compress + ROM write pipeline
- **No drag-and-drop** -- No file drop support
- **No PaletteFormRef** -- No per-color palette editing
- **No DecreaseColorTSAToolForm** -- No color reduction tool
- **No animation playback** -- No frame-by-frame preview

---

## 6. Event Editors

**Domain Average: ~10%** (most incomplete domain)

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| **EventCondForm / EventCondVM** | **3%** | Multi-tab (6 types), map preview, icon support |
| **EventScriptForm / EventScriptPopupVM** | **2%** | **Entire event script editor missing** |
| EventUnitForm / EventUnitVM | 8% | 3-level navigation, map preview, growth sim |
| EventUnitFE6Form / EventUnitFE6VM | 10% | Map preview, AI combos, growth sim |
| EventUnitFE7Form / EventUnitFE7VM | 8% | Map preview, coordinate handling |
| EventBattleTalkForm / EventBattleTalkVM | 25% | Auto-populated list, JumpTo, FELint |
| EventBattleTalkFE6/FE7 | 25% | Two tables, owner-drawn lists |
| EventHaikuForm / EventHaikuVM | 25% | Auto-populated list, patch detection |
| EventHaikuFE6/FE7 | 25-30% | Chapter filter, two tables |
| EventForceSortie/FE7 | 30-35% | Multi-level navigation, AllocIfNeed |
| EventFunctionPointer/FE7 | 15-20% | Filter combo, event info lookup |
| EventTalkGroupFE7 | 15% | AllocIfNeed, RecycleOldData |
| EventMoveDataFE7 | 10% | Variable-size records, direction types |

### Critical: EventScriptForm (2%)
The main event script editor (1,928 lines in WinForms) is reduced to a static help text popup in Avalonia. This is the most important editor in the event domain -- without it, users cannot edit event scripts.

---

## 7. Sound & Music

**Domain Average: ~20%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SongTable | 25% | Playback, song names, cross-references |
| **SongTrack** | **10%** | **MIDI/WAV import-export, track visualization, playback** |
| **SongInstrument** | **5%** | **128 instruments, type-specific panels, wave import** |
| SongInstrumentDirectSound | 35% | DPCM detection, Hz combo, validation |
| SoundRoom | 30% | Song names, position display, patch detection |
| SoundRoomFE6 | 20% | Proper list, song names |
| SoundRoomCG | 15% | Proper list, CG image preview |
| SoundBossBGM | 30% | Unit/song names, portraits |
| SoundFootSteps | 20% | Class names, switch enable check |
| WorldMapBGM | 35% | World map point names |

### Critical: Song Track (10%) and Song Instrument (5%)
These are the core music editing tools. Without MIDI import/export and instrument editing, users cannot modify game music.

---

## 8. Text & Dialogue

**Domain Average: ~15%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| **TextForm / TextViewerVM** | **15%** | **No text editing, no dialogue preview, no search, no validation** |
| TextCharCodeForm / TextCharCodeVM | 20% | No font preview, no frequency analysis |
| TextDicForm / TextDicVM | 15% | Missing 3-list structure, list not populated |
| TextRefAddDialog | 15% | No UseTextIDCache integration |
| TextToSpeech | 5% | No speech engine (Windows-only) |
| EDForm / EDVM | 20% | Only 1 of 3 sub-editors |
| EDStaffRoll | 15% | No image rendering/import/export |
| EDSensekiComment | 25% | No unit names, no text preview |
| TextBadCharPopup | 10% | Minimal shell |
| TextScriptCategorySelect | 10% | Hardcoded stub |

### Critical: TextForm (15%)
The text editor (3,941 lines in WinForms) is reduced to a read-only viewer in Avalonia. Individual text write-back with Huffman encoding is completely missing, blocking all text editing.

---

## 9. Support & Relationships

**Domain Average: ~27%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SupportUnitEditor (FE7/8) | 25% | Auto-collect, reciprocal validation, talk jumps |
| SupportUnitFE6 | 30% | Support talk jumps, unit name resolution |
| SupportTalk (FE8) | 30% | Unit name resolution, JumpTo pair search |
| SupportTalkFE6 | 30% | Unit name resolution, text preview |
| SupportTalkFE7 | 30% | Unit name resolution, text preview |
| SupportAttribute | 35% | Affinity name/icon resolution |
| TacticianAffinityFE7 | 10% | **No View file exists at all** |

---

## 10. World Map

**Domain Average: ~23%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| WorldMapPoint | 25% | Map preview, coordinate validation, tooltips |
| WorldMapPath | 20% | Map preview, path rendering |
| WorldMapPathMoveEditor | 15% | Non-functional (no path selector) |
| WorldMapEventPointer | 15% | Missing dual list (before/after), no opening/ending events |
| WorldMapBGM | 40% | Name resolution |

---

## 11. Skill Systems

**Domain Average: ~12%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| SkillAssignmentUnitSkillSystem | 10% | Icon rendering, levelup sublists, export/import |
| SkillAssignmentUnitCSkillSys | 10% | Same as above |
| SkillAssignmentUnitFE8N | 15% | Bit-flag tabs, version detection |
| SkillAssignmentClassSkillSystem | 10% | Same as unit variant |
| SkillAssignmentClassCSkillSys | 10% | Same as above |
| SkillConfigSkillSystem | 8% | Pointer finder, icon display, animation |
| SkillConfigFE8NSkill | 20% | Pointer discovery, icon rendering |
| SkillConfigFE8NVer2Skill | 15% | 4 sub-InputFormRefs, tabs |
| SkillConfigFE8NVer3Skill | 15% | 5 sub-InputFormRefs |
| SkillConfigFE8UCSkillSys09x | 12% | 1024 skills, icon rendering |
| EffectivenessReworkClassType | 10% | Bit-flag UI, class type icons |

---

## 12. Tool Windows

**Domain Average: ~7%** (lowest completeness domain)

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| **HexEditor (Jump/Mark/Search)** | **5%** | **No hex editor control exists** |
| PointerTool (Main/Batch/CopyTo) | 8% | No pointer search execution |
| DisASM (DumpAll/ArgGrep) | 5% | No disassembly engine |
| ToolASMEdit | 3% | No ASM compilation |
| ToolAnimationCreator | 3% | No animation creation |
| ToolDecompileResult | 10% | No decompilation invocation |
| ToolExportEAEvent | 12% | No EA export execution |
| ToolInitWizard | 2% | No wizard flow |
| ToolSubtitle (Overlay/Settings) | 8% | No subtitle overlay |
| **ToolThreeMarge (Main/CloseAlert)** | **5%** | **No three-way merge algorithm** |
| ToolWorkSupport (Main/UPS/Update) | 10% | No update checking/download |
| **MoveToFreeSpace** | **5%** | **No free space search or data move** |
| **PatchFormUninstall** | **5%** | **Entire patch manager missing** |
| PackedMemorySlot | 15% | No slot operations |
| EmulatorMemory | 2% | Platform limitation (P/Invoke) |
| RAMRewriteTool (Main/MAP) | 3% | Platform limitation (P/Invoke) |

### Critical: Hex Editor, Patch Manager, Three-Way Merge, Move to Free Space
These are fundamental ROM hacking tools. All are empty stubs in Avalonia.

---

## 13. Main Window & Navigation

**Domain Average: ~40%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| MainWindow vs MainFE6/7/8Form | 55% | Dirty check, drag-drop, search filter, smart routing |
| WelcomeView | 20% | Update check, language switch, drag-drop |
| VersionView | 40% | CRC32, original ROM detection |
| ErrorUnknownROMView | 80% | Minor ROM header detail |
| EventErrorIgnoreError | 70% | Lint workflow integration |
| ResourceView | 5% | Empty placeholder |
| MainSimpleMenu (Easy Mode) | 10% | Map-centric view, visual unit overlay |

---

## 14. Opening/Ending/Status/Miscellaneous

**Domain Average: ~31%**

| Form Pair | Completeness | Key Missing Features |
|-----------|:---:|---|
| OPClassDemo (4 VMs) | 30% | Patch detection, N1/N2 sub-lists |
| OPClassFont (2 VMs) | 35% | Image export/import |
| OPPrologue | 40% | Image export/import |
| OPClassAlphaName (2 VMs) | 40% | ASCII validation |
| ArenaClass | 25% | Filter combo (3 weapon types) |
| ArenaEnemyWeapon | 25% | Two sub-lists (basic + rank-up) |
| StatusOption | 25% | Address list not populated |
| StatusOptionOrder | 40% | Named option display |
| StatusParam | 35% | Filter combo (4 parameter tables) |
| StatusRMenu | 30% | Linked-list traversal, filter combo |
| StatusUnitsMenu | 35% | OwnerDraw, text decode |
| MenuCommand | 30% | Filter combo, color combo |
| MenuDefinition | 30% | Menu preview, multi-pointer |
| MenuExtendSplitMenu | 35% | No list enumeration |
| **AOERANGE** | **15%** | **Visual grid editor (core feature) missing** |
| MonsterItem | 20% | Three sub-lists (N1, N2) |
| MonsterProbability | 35% | Probability sum display |
| MonsterWMapProbability | 40% | Minimal |
| SummonUnit | 30% | OwnerDraw, expand event |
| SummonsDemonKing | 35% | AI combos, expand event |
| LinkArenaDenyUnit | 35% | Unit name display |
| TerrainName | 45% | Closest to parity |
| VennouWeaponLock | 20% | No list loading, no type resolution |
| BitFlag editors (3 VMs) | **75%** | Closest to complete |
| SystemHoverColor | 5% | Not functional |
| SystemIcon | 45% | Read-only, no import/export |
| MantAnimation | 10% | No write, no expand event |

---

## 15. Shared Infrastructure

**Overall Infrastructure Completeness: ~20%**

| Category | Coverage | Notes |
|----------|:---:|---|
| Convention-based auto-wiring | **0%** | Biggest gap -- InputFormRef's B/W/D/P naming |
| Write/Undo automation | 15% | UndoService exists but not auto-wired |
| Dirty tracking (yellow highlight) | **0%** | No visual change indicator |
| Address list management | 40% | Basic list works; no data layer |
| CSV export/import | **0%** | Missing entirely in GUI |
| Context menus | **0%** | No right-click menus anywhere |
| Pre/Post write hooks | **0%** | No standardized hooks |
| Form navigation / Jump | 35% | WindowManager basics; no injection callbacks |
| Write validation | 5% | IDataVerifiable for testing only |
| Image export | 60% | PNG export works in some forms |
| Image import + write-back | 25% | Quantize/remap; no LZ77/TSA/pointer write |
| Drag-and-drop | **0%** | Missing everywhere |
| Name resolution helpers | **0%** | Each ViewModel re-implements |
| Data expand/shrink | **0%** | Cannot add new ROM entries |
| Progress dialogs | **0%** | No AutoPleaseWait equivalent |

### Priority Recommendations for Infrastructure

**P0 -- Critical (blocks all editor development):**
1. **EditorFormRef** -- Convention-based auto-wiring equivalent for Avalonia
2. **Standardized Write/Undo** -- Auto-push undo, pre/post hooks, validation
3. **Dirty tracking** -- Visual indicator for unsaved changes

**P1 -- High (blocks quality parity):**
4. **Injection callback / select-and-return** for cross-editor navigation
5. **Image write-back** -- LZ77 compression, TSA generation, pointer updates
6. **Data validation** -- Address range checks, ALIGN4 enforcement

**P2 -- Medium (power user features):**
7. Context menus on address lists
8. CSV export/import in GUI
9. Data expand/shrink (add new ROM entries)
10. Progress dialogs for long operations

---

## Top 10 Priority Items for Closing the Gap

| Priority | Item | Impact | Domains Affected |
|:---:|---|---|---|
| 1 | **Undo system wiring** | All edits are irreversible | ALL (356 forms) |
| 2 | **InputFormRef equivalent** (EditorFormRef) | 80% of boilerplate | ALL (356 forms) |
| 3 | **EventScriptForm** implementation | Core editing blocked | Events |
| 4 | **TextForm** individual text editing | All text editing blocked | Text |
| 5 | **MapEditorForm** visual map editor | Map editing blocked | Maps |
| 6 | **Image import pipeline** (LZ77 + TSA + pointer) | No image editing | Images (23 forms) |
| 7 | **HexEditorForm** hex viewer/editor | Fundamental tool missing | Tools |
| 8 | **Patch management** system | Core feature missing | Tools |
| 9 | **Context menus** on address lists | Power user UX | ALL |
| 10 | **Dirty tracking** + unsaved changes warning | Data loss prevention | ALL |

---

## Methodology

This analysis was conducted by 15 parallel research agents, each analyzing one domain category by reading both WinForms source files and Avalonia ViewModel/View files. Completeness ratings consider:
- **0-10%**: Empty stub or placeholder
- **10-25%**: Basic data read/write only
- **25-50%**: Core fields work but missing undo, validation, navigation, previews
- **50-75%**: Most features present, missing polish/integration
- **75-100%**: Near feature parity with WinForms
