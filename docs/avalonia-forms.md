# Avalonia GUI Forms — Complete Reference

Comprehensive documentation of all GUI forms (views) in **FEBuilderGBA.Avalonia**.

## Summary Statistics

| Metric | Count |
|--------|-------|
| Total AXAML Views | 353 |
| Total ViewModels | 366 |
| Shared Controls | 2 |
| Functional Categories | 24 |

> **Note:** The ViewModel count exceeds the View count because some ViewModels have
> both a `*ViewModel.cs` and a legacy `*ViewViewModel.cs` variant (e.g.
> `ToolASMEditViewViewModel.cs` alongside the view `ToolASMEditView.axaml`).

## Architecture Notes

### MVVM Pattern

Every View (`.axaml`) has a corresponding ViewModel (`.cs`) following the
Avalonia MVVM pattern. Views define the UI layout; ViewModels hold state and
logic. All ViewModels inherit from `ViewModelBase`.

### Shared Controls

| Control | File | Description |
|---------|------|-------------|
| AddressListControl | `Controls/AddressListControl.axaml` | Reusable list for ROM address entries |
| GbaImageControl | `Controls/GbaImageControl.axaml` | Reusable GBA image display control |

### Nested "Main" Sub-Views

Complex editors split their layout into a top-level View and one or more `*Main*`
sub-views embedded inside. These sub-views are not standalone — they are
composed into a parent editor:

| Sub-View | Parent View | Purpose |
|----------|-------------|---------|
| UnitMainView | UnitEditorView | Core unit editing fields |
| MapSettingMainView | MapSettingView | Core map setting fields |
| MapPointerMainView | MapPointerView | Core map pointer fields |
| MapChangeMainView | MapChangeView | Core map change fields |
| MapExitPointMainView | MapExitPointView | Core exit point fields |
| EventCondMainView | EventCondView | Core event condition fields |
| EventScriptMainView | EventScriptView | Core event script fields |
| EventScriptInnerView | EventScriptView | Inner script editing panel |
| EventBattleTalkMainView | EventBattleTalkView | Core battle talk fields |
| ItemEffectivenessMainView | ItemEffectivenessViewerView | Core effectiveness fields |
| SongTableMainView | SongTableView | Core song table fields |
| TextMainView | TextViewerView | Core text editing fields |

### Version-Specific Variants

Many editors have version-specific counterparts suffixed with the ROM version:

| Suffix | ROM Version | Example |
|--------|------------|---------|
| `FE6` | Fire Emblem 6 (JP) | `UnitFE6View`, `ClassFE6View` |
| `FE7` | Fire Emblem 7 (JP) | `EventUnitFE7View`, `MapSettingFE7View` |
| `FE7U` | Fire Emblem 7 (US) | `MapSettingFE7UView`, `ImageCGFE7UView` |
| `FE8N` | FE8 Japanese skill system | `SkillConfigFE8NSkillView` |
| `FE8U` | Fire Emblem 8 (US) | `ExtraUnitFE8UView`, `OPClassDemoFE8UView` |

---

## 1. Application Shell

The main application window and supporting startup/settings views.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 1 | MainWindow | MainWindowViewModel | Main application window with categorized editor buttons |
| 2 | WelcomeView | WelcomeViewModel | Welcome screen shown on first launch |
| 3 | VersionView | VersionViewModel | Version info dialog |
| 4 | OptionsView | OptionsViewModel | Application settings editor |
| 5 | OpenLastSelectedFileView | OpenLastSelectedFileViewModel | Recently opened file picker |
| 6 | LogViewerView | LogViewerViewModel | Application log viewer |
| 7 | ErrorUnknownROMView | ErrorUnknownROMViewModel | Unknown ROM detection dialog |
| 8 | ErorrUnknownROMView | ErorrUnknownROMViewModel | Legacy unknown ROM dialog (typo preserved) |

## 2. Unit & Class Editors

Editors for unit, class, and related data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 9 | UnitEditorView | UnitEditorViewModel | Main unit editor (FE8) |
| 10 | UnitMainView | UnitMainViewModel | Nested sub-view for unit data fields |
| 11 | UnitFE6View | UnitFE6ViewModel | Unit editor (FE6 variant) |
| 12 | UnitFE7View | UnitFE7ViewModel | Unit editor (FE7 variant) |
| 13 | UnitActionPointerView | UnitActionPointerViewModel | Unit action pointer table editor |
| 14 | UnitCustomBattleAnimeView | UnitCustomBattleAnimeViewModel | Per-unit custom battle animation |
| 15 | UnitIncreaseHeightView | UnitIncreaseHeightViewModel | Unit height increase data |
| 16 | UnitPaletteView | UnitPaletteViewModel | Unit palette assignment editor |
| 17 | UnitsShortTextView | UnitsShortTextViewModel | Unit short name text editor |
| 18 | ExtraUnitView | ExtraUnitViewModel | Extra unit data editor |
| 19 | ExtraUnitFE8UView | ExtraUnitFE8UViewModel | Extra unit data (FE8U variant) |
| 20 | ClassEditorView | ClassEditorViewModel | Main class editor |
| 21 | ClassFE6View | ClassFE6ViewModel | Class editor (FE6 variant) |
| 22 | CCBranchEditorView | CCBranchEditorViewModel | Class change / promotion branch editor |
| 23 | MoveCostEditorView | MoveCostEditorViewModel | Movement cost table editor |
| 24 | MoveCostFE6View | MoveCostFE6ViewModel | Movement cost table (FE6 variant) |
| 25 | SomeClassListView | SomeClassListViewModel | Miscellaneous class list editor |
| 26 | VennouWeaponLockView | VennouWeaponLockViewModel | Weapon lock by class editor |
| 27 | SMEPromoListView | SMEPromoListViewModel | Promotion list (Skill Menu Extend) |
| 28 | GrowSimulatorView | GrowSimulatorViewModel | Stat growth simulation tool |

## 3. Item Editors

Editors for items, weapons, shops, and item-related data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 29 | ItemEditorView | ItemEditorViewModel | Main item/weapon editor |
| 30 | ItemFE6View | ItemFE6ViewModel | Item editor (FE6 variant) |
| 31 | ItemWeaponEffectViewerView | ItemWeaponEffectViewerViewModel | Weapon effect table viewer |
| 32 | ItemStatBonusesViewerView | ItemStatBonusesViewerViewModel | Item stat bonuses viewer |
| 33 | ItemStatBonusesSkillSystemsView | ItemStatBonusesSkillSystemsViewModel | Stat bonuses (Skill Systems variant) |
| 34 | ItemStatBonusesVennoView | ItemStatBonusesVennoViewModel | Stat bonuses (Venno variant) |
| 35 | ItemEffectivenessViewerView | ItemEffectivenessViewerViewModel | Item effectiveness table viewer |
| 36 | ItemEffectivenessMainView | ItemEffectivenessMainViewModel | Nested sub-view for effectiveness |
| 37 | ItemEffectivenessSkillSystemsReworkView | ItemEffectivenessSkillSystemsReworkViewModel | Effectiveness (Skill Systems rework) |
| 38 | ItemPromotionViewerView | ItemPromotionViewerViewModel | Promotion item table viewer |
| 39 | ItemShopViewerView | ItemShopViewerViewModel | Item shop editor (3-region: shop list + slot list + editor; mirrors WinForms `ItemShopForm`) |
| 40 | ItemWeaponTriangleViewerView | ItemWeaponTriangleViewerViewModel | Weapon triangle data viewer |
| 41 | ItemUsagePointerViewerView | ItemUsagePointerViewerViewModel | Item usage function pointer viewer |
| 42 | ItemEffectPointerViewerView | ItemEffectPointerViewerViewModel | Item effect function pointer viewer |
| 43 | ItemIconViewerView | ItemIconViewerViewModel | Item icon viewer |
| 44 | ItemRandomChestView | ItemRandomChestViewModel | Random chest contents editor |

## 4. Map Editors

Editors for map settings, tiles, terrain, and map style.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 45 | MapSettingView | MapSettingViewModel | Main map settings editor |
| 46 | MapSettingMainView | MapSettingMainViewModel | Nested sub-view for map settings |
| 47 | MapSettingFE6View | MapSettingFE6ViewModel | Map settings (FE6 variant) |
| 48 | MapSettingFE7View | MapSettingFE7ViewModel | Map settings (FE7 variant) |
| 49 | MapSettingFE7UView | MapSettingFE7UViewModel | Map settings (FE7U variant) |
| 50 | MapSettingDifficultyView | MapSettingDifficultyViewModel | Map difficulty settings |
| 51 | MapSettingDifficultyExtraView | MapSettingDifficultyExtraViewModel | Additional difficulty fields |
| 52 | MapSettingDifficultyDialogView | MapSettingDifficultyDialogViewModel | Difficulty settings dialog |
| 53 | MapChangeView | MapChangeViewModel | Map change data editor |
| 54 | MapChangeMainView | MapChangeMainViewModel | Nested sub-view for map changes |
| 55 | MapExitPointView | MapExitPointViewModel | Map exit point editor |
| 56 | MapExitPointMainView | MapExitPointMainViewModel | Nested sub-view for exit points |
| 57 | MapPointerView | MapPointerViewModel | Map pointer table editor (incl. PLIST Split/Expand `MapPlistSplitCore`, #1432) |
| 58 | MapPointerMainView | MapPointerMainViewModel | Nested sub-view for map pointers |
| 59 | MapPointerNewPLISTView | MapPointerNewPLISTViewModel | New PLIST entry creator |
| 60 | MapPointerNewPLISTPopupView | MapPointerNewPLISTPopupViewModel | New PLIST popup dialog |
| 61 | MapTileAnimationView | MapTileAnimationViewModel | Map tile animation editor |
| 62 | MapTileAnimation1View | MapTileAnimation1ViewModel | Tile animation type 1 |
| 63 | MapTileAnimation2View | MapTileAnimation2ViewModel | Tile animation type 2 |
| 64 | MapEditorView | MapEditorViewModel | Visual map tile editor (chipset palette + click-to-paint + CSV map export, see #658) |
| 65 | MapEditorAddMapChangeDialogView | MapEditorAddMapChangeDialogViewModel | Add map change dialog |
| 66 | MapEditorAddMapChangeView | MapEditorAddMapChangeViewModel | Map change addition form |
| 67 | MapEditorMarSizeDialogView | MapEditorMarSizeDialogViewModel | Map margin size dialog |
| 68 | MapEditorMarSizeView | MapEditorMarSizeViewModel | Margin size editing form |
| 69 | MapEditorResizeDialogView | MapEditorResizeDialogViewModel | Map resize dialog |
| 70 | MapEditorResizeView | MapEditorResizeViewModel | Map resize form |
| 71 | MapStyleEditorView | MapStyleEditorViewModel | Map tileset style editor |
| 72 | MapStyleEditorAppendView | MapStyleEditorAppendViewModel | Append tileset entry |
| 73 | MapStyleEditorAppendPopupView | MapStyleEditorAppendPopupViewModel | Append tileset popup |
| 74 | MapStyleEditorWarningView | MapStyleEditorWarningViewModel | Tileset warning display |
| 75 | MapStyleEditorWarningOverrideView | MapStyleEditorWarningOverrideViewModel | Override tileset warning dialog |
| 76 | MapStyleEditorImportImageOptionView | MapStyleEditorImportImageOptionViewModel | Import image options for tileset |
| 77 | MapTerrainNameView | MapTerrainNameViewModel | Terrain name editor |
| 78 | MapTerrainNameEngView | MapTerrainNameEngViewModel | Terrain name editor (English) |
| 79 | MapTerrainBGLookupView | MapTerrainBGLookupViewModel | Terrain background lookup table |
| 80 | MapTerrainFloorLookupView | MapTerrainFloorLookupViewModel | Terrain floor lookup table |
| 81 | MapMiniMapTerrainImageView | MapMiniMapTerrainImageViewModel | Minimap terrain image editor |
| 82 | MapLoadFunctionView | MapLoadFunctionViewModel | Map load function pointer editor |
| 83 | MapPictureBoxViewerView | MapPictureBoxViewerViewModel | Map image preview viewer |
| 84 | TerrainNameEditorView | TerrainNameEditorViewModel | Terrain name string editor |

## 5. Event Script Editors

Editors for event scripts, conditions, units, templates, and related data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 85 | EventScriptView | EventScriptViewModel | Main event script editor |
| 86 | EventScriptMainView | EventScriptMainViewModel | Nested sub-view for event scripts |
| 87 | EventScriptInnerView | EventScriptInnerViewModel | Inner script command editor |
| 88 | EventScriptTemplateView | EventScriptTemplateViewModel | Event script template selector |
| 89 | EventScriptCategorySelectView | EventScriptCategorySelectViewModel | Event script category picker (real categories + command list + returns a chosen `EventScript.Script`; #1443) |
| 90 | EventScriptPopupView | EventScriptPopupViewModel | Event script popup editor |
| 91 | EventCondView | EventCondViewModel | Event condition editor |
| 92 | EventCondMainView | EventCondMainViewModel | Nested sub-view for conditions |
| 93 | EventUnitView | EventUnitViewModel | Event unit placement editor — FE8 editable after-coord (move-path) list (#1017): row 0 is the synthetic START pos (X/Y/Ext persist to W4, synthetic fields disabled), rows 1+ are the 8-byte blob records (X/Y/Ext/Speed/UnitId/Unk1/Unk2/Wait); Add/Remove + in-place-vs-append+repoint write via Core `EventUnitCoordCore`; FE8-gated |
| 94 | EventUnitFE6View | EventUnitFE6ViewModel | Event unit placement (FE6) |
| 95 | EventUnitFE7View | EventUnitFE7ViewModel | Event unit placement (FE7) |
| 96 | EventUnitSimView | EventUnitSimViewModel | Simplified event unit editor |
| 97 | EventUnitColorView | EventUnitColorViewModel | Event UNIT_COLOR 4-slot colour picker (Player/Enemy/NPC/Fourth; packs `a\|b<<4\|c<<8\|d<<12`); surfaced from the event editor's UNIT_COLOR "Pick..." button (#1444) |
| 98 | EventUnitItemDropView | EventUnitItemDropViewModel | Event unit item drop editor |
| 99 | EventUnitNewAllocView | EventUnitNewAllocViewModel | Event unit New Allocation modal count-picker (NumericUpDown Min=1/Max=50/Value=1 + OK/Cancel; returns the count via `ShowDialog<uint?>`) — #776 |
| 100 | EventBattleTalkView | EventBattleTalkViewModel | Battle conversation editor |
| 101 | EventBattleTalkMainView | EventBattleTalkMainViewModel | Nested sub-view for battle talk |
| 102 | EventBattleTalkFE6View | EventBattleTalkFE6ViewModel | Battle conversation (FE6) — Table selector switches the main 12-byte table (`event_ballte_talk_pointer`) and the FE6-only boss generic-conversation 16-byte table (`event_ballte_talk2_pointer`; event pointer at +0x0C). #1438 |
| 103 | EventBattleTalkFE7View | EventBattleTalkFE7ViewModel | Battle conversation (FE7) — full editor: per-schema input fields (attacker/defender, text, event pointer, achievement flag) + undo-tracked Write; Main 16-byte / Secondary 12-byte tables (#1437) |
| 104 | EventBattleDataFE7View | EventBattleDataFE7ViewModel | Battle data editor (FE7) — editable AttackType/Attacker/Damage + undo-tracked Write (#1436) |
| 105 | EventHaikuView | EventHaikuViewModel | Haiku/death quote editor |
| 106 | EventHaikuFE6View | EventHaikuFE6ViewModel | Death quotes (FE6) |
| 107 | EventHaikuFE7View | EventHaikuFE7ViewModel | Death quotes (FE7) |
| 108 | EventMapChangeView | EventMapChangeViewModel | Map change event editor |
| 109 | EventForceSortieView | EventForceSortieViewModel | Forced sortie event editor |
| 110 | EventForceSortieFE7View | EventForceSortieFE7ViewModel | Forced sortie (FE7) |
| 111 | EventFunctionPointerView | EventFunctionPointerViewModel | Event function pointer editor |
| 112 | EventFunctionPointerFE7View | EventFunctionPointerFE7ViewModel | Event function pointer (FE7) |
| 113 | EventAssemblerView | EventAssemblerViewModel | Event assembler integration |
| 114 | EventFinalSerifFE7View | EventFinalSerifFE7ViewModel | Final chapter serif (FE7) |
| 115 | EventMoveDataFE7View | EventMoveDataFE7ViewModel | Unit move data events (FE7) |
| 116 | EventTalkGroupFE7View | EventTalkGroupFE7ViewModel | Talk group events (FE7) — 14-entry stride-4 list + repoint + NewAlloc (#1442) |
| 117 | EventTemplate1View | EventTemplate1ViewModel | Event template 1 |
| 118 | EventTemplate2View | EventTemplate2ViewModel | Event template 2 |
| 119 | EventTemplate3View | EventTemplate3ViewModel | Event template 3 |
| 120 | EventTemplate4View | EventTemplate4ViewModel | Event template 4 |
| 121 | EventTemplate5View | EventTemplate5ViewModel | Event template 5 |
| 122 | EventTemplate6View | EventTemplate6ViewModel | Event template 6 |
| 123 | EventTemplateImplView | EventTemplateImplViewModel | Event template implementation |

## 6. AI Script Editors

Editors for enemy AI behavior scripts.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 124 | AIScriptView | AIScriptViewModel | Main AI script editor (opcode edit + write-back + full byte-stream Export/Import, #965) |
| 125 | AIScriptCategorySelectView | AIScriptCategorySelectViewModel | AI script category picker |
| 126 | AIASMCALLTALKView | AIASMCALLTALKViewModel | AI ASM call/talk script — **context-dependent sub-editor** (#1414): no standalone main-menu button, reached only via the AIScript per-parameter dispatch; standalone open shows a placeholder at addr 0 so Write is a no-op |
| 127 | AIASMCoordinateView | AIASMCoordinateViewModel | AI ASM coordinate script — **context-dependent sub-editor** (#1414) |
| 128 | AIASMRangeView | AIASMRangeViewModel | AI ASM range script — **context-dependent sub-editor** (#1414) |
| 129 | AIMapSettingView | AIMapSettingViewModel | AI map setting editor |
| 130 | AIPerformItemView | AIPerformItemViewModel | AI item usage behavior |
| 131 | AIPerformStaffView | AIPerformStaffViewModel | AI staff usage behavior |
| 132 | AIStealItemView | AIStealItemViewModel | AI steal behavior |
| 133 | AITargetView | AITargetViewModel | AI target selection |
| 134 | AITilesView | AITilesViewModel | AI tile evaluation — **context-dependent sub-editor** (#1414) |
| 135 | AIUnitsView | AIUnitsViewModel | AI unit evaluation — **context-dependent sub-editor** (#1414) |
| 136 | AOERANGEView | AOERANGEViewModel | Area of effect range editor — **functional** (#1431): manual address + Reload, dynamic w×h AoE grid with center highlight, real repoint-on-write (`AoeRangeCore`) |

## 7. Procs Script Editors

Editors for process/animation scripts.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 137 | ProcsScriptView | ProcsScriptViewModel | Main procs script editor |
| 138 | ProcsScriptCategorySelectView | ProcsScriptCategorySelectViewModel | Procs script category picker |

## 8. Image & Graphics Editors

Editors for portraits, sprites, battle animations, tilesets, and other graphics.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 139 | ImageViewerView | ImageViewerViewModel | Generic image viewer |
| 140 | PortraitViewerView | PortraitViewerViewModel | Character portrait viewer |
| 141 | ImagePortraitView | ImagePortraitViewModel | Portrait editor (FE7/FE8 only — 28-byte entries; hidden on FE6, which uses the 16-byte editor below, #1411) |
| 142 | ImagePortraitFE6View | ImagePortraitFE6ViewModel | Portrait editor (FE6 — 16-byte entries) |
| 143 | ImagePortraitImporterView | ImagePortraitImporterViewModel | Portrait import tool |
| 144 | ImageBGView | ImageBGViewModel | Background image editor |
| 145 | ImageBGSelectPopupView | ImageBGSelectPopupViewModel | Background selection popup |
| 146 | ImageBattleAnimeView | ImageBattleAnimeViewModel | Battle animation editor |
| 147 | ImageBattleAnimePalletView | ImageBattleAnimePalletViewModel | Battle animation palette editor |
| 148 | ImageBattleBGView | ImageBattleBGViewModel | Battle background editor |
| 149 | ImageBattleScreenView | ImageBattleScreenViewModel | Battle screen editor |
| 150 | ImageCGView | ImageCGViewModel | CG (cutscene graphic) editor |
| 151 | ImageCGFE7UView | ImageCGFE7UViewModel | CG editor (FE7U variant) |
| 152 | ImageChapterTitleFE7View | ImageChapterTitleFE7ViewModel | Chapter title image (FE7) |
| 153 | ImageUnitPaletteView | ImageUnitPaletteViewModel | Unit sprite palette editor (live-recolors the sample battle-anime preview while editing R/G/B colors, #1022; Clipboard/Zoom/Undo/Redo controls wired, #1006 — Expand List + New Palette Allocation deferred to a follow-up) |
| 154 | ImageUnitWaitIconView | ImageUnitWaitIconViewModel | Unit wait/idle icon editor |
| 155 | ImageUnitMoveIconView | ImageUnitMoveIconViewModel | Unit move icon editor |
| 156 | ImageSystemAreaView | ImageSystemAreaViewModel | System area graphics editor |
| 157 | ImageGenericEnemyPortraitView | ImageGenericEnemyPortraitViewModel | Generic enemy portrait editor (preview + PNG Export / Image Import) |
| 158 | ImageRomAnimeView | ImageRomAnimeViewModel | ROM-stored animation editor |
| 159 | ImageTSAEditorView | ImageTSAEditorViewModel | Tile Screen Arrangement editor (palette write + raw-tilesheet import/export + per-cell TSA editing & TSA write-back for NON-header TSA, #1005; header-TSA per-cell editing deferred) |
| 160 | ImageTSAAnimeView | ImageTSAAnimeViewModel | TSA animation editor |
| 161 | ImageTSAAnime2View | ImageTSAAnime2ViewModel | TSA animation editor (type 2); two-level: per-category list enumerates EVERY 12-byte entry (walk dataAddr+20 stride 12 until u32(addr+8) is not pointer-shaped), HeaderBase keyed per entry so the shared IMAGE/PALETTE resolve for entry[i>0] (#1456) |
| 162 | ImagePalletView | ImagePalletViewModel | Palette viewer/editor |
| 163 | ImageMagicFEditorView | ImageMagicFEditorViewModel | Magic effect frame editor |
| 164 | ImageMagicCSACreatorView | ImageMagicCSACreatorViewModel | Magic CSA (compressed sprite) creator — live 240×128 frame preview (`MagicEffectExportCore.RenderCsaFramePreview`, READ-ONLY; BG→OBJ-back→OBJ-front composite, honors `IsExpandsBG`) on entry-select / Frame spinner / Zoom; working "Find new resources online" → MoreData wiki link (#1021) |
| 165 | ImageMapActionAnimationView | ImageMapActionAnimationViewModel | Map action animation editor |
| 166 | ImageFormRefViewerView | ImageFormRefViewerViewModel | Image form reference viewer |
| 167 | InterpolatedPictureBoxViewerView | InterpolatedPictureBoxViewerViewModel | Interpolated image viewer |
| 168 | DecreaseColorTSAToolView | DecreaseColorTSAToolViewModel | Color reduction with TSA tool (functional file→file reducer: Method presets + Reduce, #998) |
| 169 | SystemIconViewerView | SystemIconViewerViewModel | System icon viewer |
| 170 | SystemHoverColorViewerView | SystemHoverColorViewerViewModel | System hover color table viewer |
| 171 | BattleBGViewerView | BattleBGViewerViewModel | Battle background viewer |
| 172 | BattleTerrainViewerView | BattleTerrainViewerViewModel | Battle terrain viewer |
| 173 | ChapterTitleViewerView | ChapterTitleViewerViewModel | Chapter title image viewer |
| 174 | BigCGViewerView | BigCGViewerViewModel | Large CG image viewer |
| 175 | GraphicsToolView | GraphicsToolViewViewModel | Graphics tool main view (TSA-composited preview via ImageTSAEditorCore.TryRenderMainImage, #1030; image2-join + LZ77-compressed paletteType wired in via the 10-arg overload, #1074) |
| 176 | GraphicsToolPatchMakerView | GraphicsToolPatchMakerViewViewModel | Graphics patch creation tool |
| 177 | MantAnimationView | MantAnimationViewModel | Map/mant animation viewer |
| 178 | OAMSPView | OAMSPViewModel | OAM sprite editor |

## 9. Palette Tools

Editors for palette manipulation.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 179 | PaletteChangeColorsView | PaletteChangeColorsViewViewModel | Palette color adjustment tool |
| 180 | PaletteClipboardView | PaletteClipboardViewViewModel | Palette clipboard manager |
| 181 | PaletteSwapView | PaletteSwapViewViewModel | Palette swap tool |

## 10. Audio & Music Editors

Editors for songs, instruments, sound effects, and sound room.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 182 | SongTableView | SongTableViewModel | Song table editor |
| 183 | SongTableMainView | SongTableMainViewModel | Nested sub-view for song table |
| 184 | SongTrackView | SongTrackViewModel | Song track editor |
| 185 | SongTrackAllChangeTrackView | SongTrackAllChangeTrackViewModel | Bulk track change tool |
| 186 | SongTrackChangeTrackView | SongTrackChangeTrackViewModel | Single track change tool |
| 187 | SongTrackImportMidiView | SongTrackImportMidiViewModel | MIDI import tool |
| 188 | SongTrackImportSelectInstrumentView | SongTrackImportSelectInstrumentViewModel | Instrument selection for import |
| 189 | SongInstrumentView | SongInstrumentViewModel | Instrument editor |
| 190 | SongInstrumentDirectSoundView | SongInstrumentDirectSoundViewModel | Direct sound instrument editor |
| 191 | SongInstrumentImportWaveView | SongInstrumentImportWaveViewModel | Wave import for instruments |
| 192 | SongExchangeView | SongExchangeViewModel | Song exchange/swap tool |
| 193 | SoundBossBGMViewerView | SoundBossBGMViewerViewModel | Boss BGM assignment viewer |
| 194 | SoundFootStepsViewerView | SoundFootStepsViewerViewModel | Footstep sound assignment viewer |
| 195 | SoundRoomViewerView | SoundRoomViewerViewModel | Sound room entry viewer + List Expansion (255 / 1000 with soundroom_over255 patch, #1450) |
| 196 | SoundRoomFE6View | SoundRoomFE6ViewModel | Sound room (FE6 variant) |
| 197 | SoundRoomCGView | SoundRoomCGViewModel | Sound room CG display |
| 198 | ToolBGMMuteDialogView | ToolBGMMuteDialogViewModel | BGM mute settings dialog |

## 11. Text & Translation Editors

Editors for game text, fonts, translation, and text utilities.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 199 | TextViewerView | TextViewerViewModel | Main text viewer/editor |
| 200 | TextMainView | TextMainViewModel | Nested sub-view for text |
| 201 | OtherTextView | OtherTextViewModel | Other text data editor |
| 202 | CStringView | CStringViewModel | C-style string editor — **functional** (#1445): manual address + Reload, editable string TextBox, real read (`TextForm.Direct` decode) + repoint-on-write (`CStringCore`, encode + NUL, in-place else relocate) |
| 203 | FontEditorView | FontEditorViewModel | Font tile editor |
| 204 | FontZHView | FontZHViewModel | Chinese font editor (per-glyph + bulk export/import + .ttf auto-generate) |
| 205 | TextEscapeEditorView | TextEscapeEditorViewModel | Text escape code editor |
| 206 | TextScriptCategorySelectView | TextScriptCategorySelectViewModel | Text script category picker |
| 207 | TextDicView | TextDicViewModel | Text dictionary editor |
| 208 | TextCharCodeView | TextCharCodeViewModel | Character code table editor (Write button persists Char Code/Terminator + rebuilds text encoder) |
| 209 | TextBadCharPopupView | TextBadCharPopupViewModel | Invalid character warning popup |
| 210 | TextRefAddDialogView | TextRefAddDialogViewModel | Text reference addition dialog |
| 211 | TextToSpeechView | TextToSpeechViewModel | Text-to-speech preview tool |
| 212 | DevTranslateView | DevTranslateViewModel | Developer translation tool |
| 213 | ToolTranslateROMView | ToolTranslateROMViewModel | ROM translation tool |

## 12. Support System Editors

Editors for support conversations and affinity.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 214 | SupportUnitEditorView | SupportUnitEditorViewModel | Support unit pair editor |
| 215 | SupportUnitFE6View | SupportUnitFE6ViewModel | Support units (FE6 variant) |
| 216 | SupportAttributeView | SupportAttributeViewModel | Support affinity attribute editor |
| 217 | SupportTalkView | SupportTalkViewModel | Support conversation editor |
| 218 | SupportTalkFE6View | SupportTalkFE6ViewModel | Support conversations (FE6) |
| 219 | SupportTalkFE7View | SupportTalkFE7ViewModel | Support conversations (FE7) |

## 13. Arena, Monster & Summon Editors

Editors for arena, monster, and summon data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 220 | ArenaClassViewerView | ArenaClassViewerViewModel | Arena class table viewer |
| 221 | ArenaEnemyWeaponViewerView | ArenaEnemyWeaponViewerViewModel | Arena enemy weapon table viewer |
| 222 | LinkArenaDenyUnitViewerView | LinkArenaDenyUnitViewerViewModel | Link arena banned unit viewer |
| 223 | MonsterProbabilityViewerView | MonsterProbabilityViewerViewModel | Monster spawn probability viewer |
| 224 | MonsterItemViewerView | MonsterItemViewerViewModel | Monster item drop viewer |
| 225 | MonsterWMapProbabilityViewerView | MonsterWMapProbabilityViewerViewModel | World map monster probability viewer |
| 226 | SummonUnitViewerView | SummonUnitViewerViewModel | Summoned unit table viewer |
| 227 | SummonsDemonKingViewerView | SummonsDemonKingViewerViewModel | Demon King summon table viewer |

## 14. Menu Editors

Editors for in-game menus and commands.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 228 | MenuDefinitionView | MenuDefinitionViewModel | Menu definition table editor |
| 229 | MenuCommandView | MenuCommandViewModel | Menu command table editor |
| 230 | MenuExtendSplitMenuView | MenuExtendSplitMenuViewModel | Extended split menu editor |
| 231 | FE8SpellMenuExtendsView | FE8SpellMenuExtendsViewModel | FE8 spell menu extension editor |

## 15. Ending (ED) Editors

Editors for game endings, staff rolls, and credits.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 232 | EDView | EDViewModel | Main ending data editor |
| 233 | EDStaffRollView | EDStaffRollViewModel | Staff roll/credits editor |
| 234 | EDFE6View | EDFE6ViewModel | Ending data (FE6 variant) |
| 235 | EDFE7View | EDFE7ViewModel | Ending data (FE7 variant) |
| 236 | EDSensekiCommentView | EDSensekiCommentViewModel | Battle record comment editor |

## 16. World Map Editors

Editors for world map nodes, paths, events, and images.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 237 | WorldMapPointView | WorldMapPointViewModel | World map node/point editor |
| 238 | WorldMapBGMView | WorldMapBGMViewModel | World map BGM assignment editor |
| 239 | WorldMapEventPointerView | WorldMapEventPointerViewModel | World map event pointer editor |
| 240 | WorldMapEventPointerFE6View | WorldMapEventPointerFE6ViewModel | World map events (FE6) |
| 241 | WorldMapEventPointerFE7View | WorldMapEventPointerFE7ViewModel | World map events (FE7) |
| 242 | WorldMapPathView | WorldMapPathViewModel | World map path editor |
| 243 | WorldMapPathEditorView | WorldMapPathEditorViewModel | World map path visual editor |
| 244 | WorldMapPathMoveEditorView | WorldMapPathMoveEditorViewModel | World map path movement editor |
| 245 | WorldMapImageView | WorldMapImageViewModel | World map image editor — Main/Dark import (#875), Mini/Point1/Point2/Road strip image imports wired via `ImageWorldMapCore.ImportIconStrip` (single-LZ77-stream, image-only nearest-color remap, FE8-only gates, #1000); Event/Border image imports + legacy full Export are deferred follow-ups |
| 246 | WorldMapImageFE6View | WorldMapImageFE6ViewModel | World map image (FE6) |
| 247 | WorldMapImageFE7View | WorldMapImageFE7ViewModel | World map image (FE7) |

## 17. Opening (OP) Editors

Editors for opening demos, fonts, prologues, and alpha names.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 248 | OPClassDemoViewerView | OPClassDemoViewerViewModel | Opening class demo viewer |
| 249 | OPClassDemoFE7View | OPClassDemoFE7ViewModel | Class demo (FE7 variant) |
| 250 | OPClassDemoFE7UView | OPClassDemoFE7UViewModel | Class demo (FE7U variant) |
| 251 | OPClassDemoFE8UView | OPClassDemoFE8UViewModel | Class demo (FE8U variant) |
| 252 | OPClassFontViewerView | OPClassFontViewerViewModel | Opening class font editor (Write + Export PNG + Import PNG, #999) |
| 253 | OPClassFontFE8UView | OPClassFontFE8UViewModel | Class font (FE8U variant) |
| 254 | OPClassAlphaNameView | OPClassAlphaNameViewModel | Opening alpha name editor |
| 255 | OPClassAlphaNameFE6View | OPClassAlphaNameFE6ViewModel | Alpha name (FE6 variant) |
| 256 | OPClassAlphaNameFE6ExtraView | OPClassAlphaNameFE6ExtraViewModel | Alpha name extra (FE6) |
| 257 | OPPrologueViewerView | OPPrologueViewerViewModel | Opening prologue viewer |
| 258 | ClassOPDemoView | ClassOPDemoViewModel | Class opening demo data editor |
| 259 | ClassOPFontView | ClassOPFontViewModel | Class opening font data editor |

## 18. Status Screen Editors

Editors for status screen layout and parameters.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 260 | StatusParamView | StatusParamViewModel | Status parameter display editor |
| 261 | StatusRMenuView | StatusRMenuViewModel | Status R-button menu editor |
| 262 | StatusUnitsMenuView | StatusUnitsMenuViewModel | Status units menu editor |
| 263 | StatusOptionOrderView | StatusOptionOrderViewModel | Status option order editor |
| 264 | StatusOptionView | StatusOptionViewModel | Status option data editor |

## 19. Skill System Editors

Editors for various skill system implementations.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 265 | SkillAssignmentUnitSkillSystemView | SkillAssignmentUnitSkillSystemViewModel | Unit skill assignment (SkillSystem) |
| 266 | SkillAssignmentClassSkillSystemView | SkillAssignmentClassSkillSystemViewModel | Class skill assignment (SkillSystem) |
| 267 | SkillConfigSkillSystemView | SkillConfigSkillSystemViewModel | Skill config (SkillSystem) — renders the per-frame animation preview via Core `SkillSystemsAnimeExportCore` (#1010) |
| 268 | SkillAssignmentUnitCSkillSysView | SkillAssignmentUnitCSkillSysViewModel | Unit skill assignment (CSkillSys) |
| 269 | SkillAssignmentClassCSkillSysView | SkillAssignmentClassCSkillSysViewModel | Class skill assignment (CSkillSys) |
| 270 | SkillAssignmentUnitFE8NView | SkillAssignmentUnitFE8NViewModel | Unit skill assignment (FE8N) |
| 271 | SkillConfigFE8NSkillView | SkillConfigFE8NSkillViewModel | Skill config (FE8N) |
| 272 | SkillConfigFE8NVer2SkillView | SkillConfigFE8NVer2SkillViewModel | Skill config (FE8N v2) — renders the per-frame animation preview via Core `SkillSystemsAnimeExportCore` (#1010) |
| 273 | SkillConfigFE8NVer3SkillView | SkillConfigFE8NVer3SkillViewModel | Skill config (FE8N v3) — renders the per-frame animation preview via Core `SkillSystemsAnimeExportCore` (#1010) |
| 274 | SkillConfigFE8UCSkillSys09xView | SkillConfigFE8UCSkillSys09xViewModel | Skill config (FE8U CSkillSys 0.9.x) — renders the per-frame animation preview via Core `SkillSystemsAnimeExportCore` (#1010) |
| 275 | SkillSystemsCSkillRechainView | SkillSystemsCSkillRechainViewModel | CSkill rechain editor |

## 20. Patch Management

Editors for patch installation, filtering, and custom builds.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 276 | PatchManagerView | PatchManagerViewModel | Patch manager main view |
| 277 | PatchFilterExView | PatchFilterExViewModel | Patch filter editor |
| 278 | PatchFormUninstallDialogView | PatchFormUninstallDialogViewModel | Patch uninstall confirmation |
| 279 | PatchUninstallDialogView | PatchUninstallDialogViewModel | Patch uninstall dialog |
| 280 | HowDoYouLikePatchView | HowDoYouLikePatchViewModel | Patch feedback dialog |
| 281 | HowDoYouLikePatch2View | HowDoYouLikePatch2ViewModel | Patch feedback dialog (v2) |
| 282 | ToolCustomBuildView | ToolCustomBuildViewModel | Custom build configuration |

## 21. Hex Editor & Disassembly Tools

Low-level ROM editing and disassembly tools.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 283 | HexEditorView | HexEditorViewModel | Hex editor main view |
| 284 | HexEditorJumpView | HexEditorJumpViewModel | Hex editor jump-to-address dialog |
| 285 | HexEditorMarkView | HexEditorMarkViewModel | Hex editor bookmark manager |
| 286 | HexEditorSearchView | HexEditorSearchViewModel | Hex editor search dialog |
| 287 | DisASMView | DisASMViewModel | Disassembler main view |
| 288 | DisASMDumpAllView | DisASMDumpAllViewModel | Dump all disassembly |
| 289 | DisASMDumpAllArgGrepView | DisASMDumpAllArgGrepViewModel | Disassembly argument grep |
| 290 | ToolASMEditView | ToolASMEditViewViewModel | ASM code editor |
| 291 | ToolASMInsertView | ToolASMInsertViewModel | ASM code insertion tool |
| 292 | ToolDecompileResultView | ToolDecompileResultViewViewModel | Decompilation result viewer |

## 22. Pointer & Memory Tools

Pointer manipulation and emulator memory access tools.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 293 | PointerToolView | PointerToolViewModel | Pointer manipulation tool (cross-ROM raw + LDR literal-pool reference search via `U.GrepPointerAll` / `U.GrepPointerAllOnLDR`, #966) |
| 294 | PointerToolBatchInputView | PointerToolBatchInputViewModel | Batch pointer input |
| 295 | PointerToolCopyToView | PointerToolCopyToViewModel | Copy pointer data tool |
| 296 | MoveToFreeSpaceView | MoveToFreeSpaceViewViewModel | Move data to free space tool |
| 297 | PackedMemorySlotView | PackedMemorySlotViewModel | Packed memory slot viewer |
| 298 | EmulatorMemoryView | EmulatorMemoryViewModel | Emulator memory viewer |
| 299 | RAMRewriteToolView | RAMRewriteToolViewModel | RAM rewrite tool |
| 300 | RAMRewriteToolMAPView | RAMRewriteToolMAPViewViewModel | RAM rewrite tool (map view) |

## 23. Bit Flag Editors

Generic bit flag editing dialogs for different data widths.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 301 | UbyteBitFlagView | UbyteBitFlagViewModel | 8-bit (byte) flag editor |
| 302 | UshortBitFlagView | UshortBitFlagViewModel | 16-bit (ushort) flag editor |
| 303 | UwordBitFlagView | UwordBitFlagViewModel | 32-bit (uint) flag editor |

## 24. Structural Data Editors

Editors for miscellaneous ROM structure data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 304 | Command85PointerView | Command85PointerViewModel | Command 0x85 pointer table editor |
| 305 | DumpStructSelectDialogView | DumpStructSelectDialogViewModel | Struct dump selection dialog (CSV/TSV/EA/STRUCT/NMM all export struct-aware output for resolved tables via `StructExportCore`; STRUCT=.h C-header, NMM=No$gba memory map — #1012) |
| 306 | DumpStructSelectToTextDialogView | DumpStructSelectToTextDialogViewModel | Struct dump to text dialog |
| 307 | ResourceView | ResourceViewModel | Resource table viewer |

## 25. Error & Notification Dialogs

Error reporting, palette warnings, and notification overlays.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 308 | ErrorReportView | ErrorReportViewModel | Error report dialog |
| 309 | ErrorPaletteMissMatchView | ErrorPaletteMissMatchViewModel | Palette mismatch warning |
| 310 | ErrorPaletteShowView | ErrorPaletteShowViewModel | Palette error display |
| 311 | ErrorPaletteTransparentView | ErrorPaletteTransparentViewModel | Transparent palette error |
| 312 | ErrorTSAErrorView | ErrorTSAErrorViewModel | TSA error display |
| 313 | ErrorLongMessageDialogView | ErrorLongMessageDialogViewModel | Long error message dialog |
| 314 | NotifyDirectInjectionView | NotifyDirectInjectionViewModel | Direct injection notification |
| 315 | NotifyPleaseWaitView | NotifyPleaseWaitViewModel | Please wait notification overlay |
| 316 | NotifyWriteView | NotifyWriteViewModel | Write notification overlay |

## 26. Tool Utilities

Miscellaneous tools for ROM management, lint, diff, UPS, and more.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 317 | ToolUndoView | ToolUndoViewModel | Undo history viewer |
| 318 | ToolUndoPopupDialogView | ToolUndoPopupDialogViewModel | Undo popup dialog |
| 319 | ToolFELintView | ToolFELintViewModel | FE Lint ROM validation tool — read-only viewer over the cross-platform `FELintScanner` (same scan as CLI `--lint`): per-finding list, severity/category/address/message detail, error+warning summary, Rescan, and double-click/Enter jump-to-HexEditor for jumpable findings (#1168) |
| 320 | ToolROMRebuildView | ToolROMRebuildViewModel | ROM rebuild tool |
| 321 | ToolLZ77View | ToolLZ77ViewModel | LZ77 compression/decompression tool |
| 322 | ToolDiffView | ToolDiffViewModel | ROM diff comparison tool |
| 323 | ToolDiffDebugSelectView | ToolDiffDebugSelectViewModel | Diff debug selection dialog |
| 324 | ToolUPSPatchSimpleView | ToolUPSPatchSimpleViewModel | UPS patch application tool |
| 325 | ToolUPSOpenSimpleView | ToolUPSOpenSimpleViewModel | UPS patch open tool |
| 326 | ToolFlagNameView | ToolFlagNameViewModel | Flag name editor |
| 327 | ToolUseFlagView | ToolUseFlagViewModel | Flag usage viewer |
| 328 | ToolUnitTalkGroupView | ToolUnitTalkGroupViewModel | Unit talk group editor |
| 329 | ToolAnimationCreatorView | ToolAnimationCreatorViewViewModel | Animation creator (#500): `InitFromRom` / `InitFromFile` flows, frame list browser, per-frame editor |
| 330 | ToolThreeMargeView | ToolThreeMargeViewModel | Three-way merge tool |
| 331 | ToolThreeMargeCloseAlertView | ToolThreeMargeCloseAlertViewModel | Three-way merge close alert |
| 332 | ToolExportEAEventView | ToolExportEAEventViewViewModel | Export events to EA format |
| 333 | ToolChangeProjectnameView | ToolChangeProjectnameViewViewModel | Project name change tool |
| 334 | ToolAutomaticRecoveryROMHeaderView | ToolAutomaticRecoveryROMHeaderViewViewModel | ROM header recovery tool |
| 335 | ToolClickWriteFloatControlPanelButtonView | ToolClickWriteFloatControlPanelButtonViewModel | Float control panel button tool |
| 336 | ToolSubtitleOverlayView | ToolSubtitleOverlayViewModel | Subtitle overlay tool |
| 337 | ToolSubtitleSettingDialogView | ToolSubtitleSettingDialogViewViewModel | Subtitle settings dialog |
| 338 | ToolInitWizardView | ToolInitWizardViewModel | Initial setup wizard |
| 339 | ToolUpdateDialogView | ToolUpdateDialogViewModel | Application update dialog |
| 340 | ToolRunHintMessageView | ToolRunHintMessageViewModel | Run hint message dialog |
| 341 | ToolEmulatorSetupMessageView | ToolEmulatorSetupMessageViewModel | Emulator setup message |
| 342 | ToolProblemReportView | ToolProblemReportViewModel | Problem report tool |
| 343 | ToolProblemReportSearchBackupView | ToolProblemReportSearchBackupViewModel | Problem report backup search |
| 344 | ToolProblemReportSearchSavView | ToolProblemReportSearchSavViewModel | Problem report save search |
| 345 | ToolAllWorkSupportView | ToolAllWorkSupportViewModel | All work support tool |
| 346 | ToolWorkSupportView | ToolWorkSupportViewModel | Work support tool |
| 347 | ToolWorkSupport_SelectUPSView | ToolWorkSupport_SelectUPSViewModel | Work support UPS selection |
| 348 | ToolWorkSupport_UpdateQuestionDialogView | ToolWorkSupport_UpdateQuestionDialogViewModel | Work support update question |

## 27. Simple Menu Mode

Simplified beginner-mode views.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 349 | MainSimpleMenuView | MainSimpleMenuViewModel | Simplified main menu |
| 350 | MainSimpleMenuEventErrorView | MainSimpleMenuEventErrorViewModel | Simple menu event error display |
| 351 | MainSimpleMenuEventErrorIgnoreErrorView | MainSimpleMenuEventErrorIgnoreErrorViewModel | Simple menu ignore error dialog |
| 352 | MainSimpleMenuImageSubView | MainSimpleMenuImageSubViewModel | Simple menu image sub-view |

---

## Cross-Reference: Views Without Tracked E2E Coverage

The following 31 views exist as AXAML files but are not listed in the
[E2E coverage tracker](avalonia-gui-forms.md). Most are nested sub-views,
internal components, or notification overlays that are exercised indirectly
through their parent editors:

| View | Reason Not Separately Tracked |
|------|-------------------------------|
| MainWindow | Application shell, not an editor |
| UnitMainView | Nested sub-view of UnitEditorView |
| MapSettingMainView | Nested sub-view of MapSettingView |
| MapPointerMainView | Nested sub-view of MapPointerView |
| MapChangeMainView | Nested sub-view of MapChangeView |
| MapExitPointMainView | Nested sub-view of MapExitPointView |
| EventCondMainView | Nested sub-view of EventCondView |
| EventScriptMainView | Nested sub-view of EventScriptView |
| EventScriptInnerView | Nested sub-view of EventScriptView |
| EventBattleTalkMainView | Nested sub-view of EventBattleTalkView |
| ItemEffectivenessMainView | Nested sub-view of ItemEffectivenessViewerView |
| SongTableMainView | Nested sub-view of SongTableView |
| TextMainView | Nested sub-view of TextViewerView |
| MapEditorAddMapChangeView | Inner form of MapEditorAddMapChangeDialogView |
| MapEditorMarSizeView | Inner form of MapEditorMarSizeDialogView |
| MapEditorResizeView | Inner form of MapEditorResizeDialogView |
| MapPointerNewPLISTView | Inner form of MapPointerNewPLISTPopupView |
| MapStyleEditorAppendView | Inner form of MapStyleEditorAppendPopupView |
| MapStyleEditorWarningView | Inner form of MapStyleEditorWarningOverrideView |
| MapSettingDifficultyExtraView | Extra fields of MapSettingDifficultyView |
| MapTerrainNameView | Sub-view of TerrainNameEditorView |
| MapPictureBoxViewerView | Reusable map image component |
| ImageFormRefViewerView | Reusable image reference component |
| InterpolatedPictureBoxViewerView | Reusable scaled image component |
| EventUnitSimView | Simplified event unit sub-view |
| OPClassAlphaNameFE6ExtraView | Extra fields of OPClassAlphaNameFE6View |
| NotifyDirectInjectionView | Notification overlay |
| NotifyPleaseWaitView | Notification overlay |
| NotifyWriteView | Notification overlay |
| PatchUninstallDialogView | Dialog variant of PatchFormUninstallDialogView |
| SkillSystemsCSkillRechainView | CSkill rechain sub-editor |
| ErorrUnknownROMView | Legacy dialog (typo preserved from WinForms) |
