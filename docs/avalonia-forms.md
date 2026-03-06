# Avalonia GUI Forms — Complete Reference

Comprehensive documentation of all GUI forms (views) in **FEBuilderGBA.Avalonia**.

## Summary Statistics

| Metric | Count |
|--------|-------|
| Total AXAML Views | 354 |
| Total ViewModels | 367 |
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
| 39 | ItemShopViewerView | ItemShopViewerViewModel | Item shop inventory viewer |
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
| 57 | MapPointerView | MapPointerViewModel | Map pointer table editor |
| 58 | MapPointerMainView | MapPointerMainViewModel | Nested sub-view for map pointers |
| 59 | MapPointerNewPLISTView | MapPointerNewPLISTViewModel | New PLIST entry creator |
| 60 | MapPointerNewPLISTPopupView | MapPointerNewPLISTPopupViewModel | New PLIST popup dialog |
| 61 | MapTileAnimationView | MapTileAnimationViewModel | Map tile animation editor |
| 62 | MapTileAnimation1View | MapTileAnimation1ViewModel | Tile animation type 1 |
| 63 | MapTileAnimation2View | MapTileAnimation2ViewModel | Tile animation type 2 |
| 64 | MapEditorView | MapEditorViewModel | Visual map tile editor |
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
| 89 | EventScriptCategorySelectView | EventScriptCategorySelectViewModel | Event script category picker |
| 90 | EventScriptPopupView | EventScriptPopupViewModel | Event script popup editor |
| 91 | EventCondView | EventCondViewModel | Event condition editor |
| 92 | EventCondMainView | EventCondMainViewModel | Nested sub-view for conditions |
| 93 | EventUnitView | EventUnitViewModel | Event unit placement editor |
| 94 | EventUnitFE6View | EventUnitFE6ViewModel | Event unit placement (FE6) |
| 95 | EventUnitFE7View | EventUnitFE7ViewModel | Event unit placement (FE7) |
| 96 | EventUnitSimView | EventUnitSimViewModel | Simplified event unit editor |
| 97 | EventUnitColorView | EventUnitColorViewModel | Event unit color/team editor |
| 98 | EventUnitItemDropView | EventUnitItemDropViewModel | Event unit item drop editor |
| 99 | EventUnitNewAllocView | EventUnitNewAllocViewModel | Event unit new allocation editor |
| 100 | EventBattleTalkView | EventBattleTalkViewModel | Battle conversation editor |
| 101 | EventBattleTalkMainView | EventBattleTalkMainViewModel | Nested sub-view for battle talk |
| 102 | EventBattleTalkFE6View | EventBattleTalkFE6ViewModel | Battle conversation (FE6) |
| 103 | EventBattleTalkFE7View | EventBattleTalkFE7ViewModel | Battle conversation (FE7) |
| 104 | EventBattleDataFE7View | EventBattleDataFE7ViewModel | Battle data editor (FE7) |
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
| 116 | EventTalkGroupFE7View | EventTalkGroupFE7ViewModel | Talk group events (FE7) |
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
| 124 | AIScriptView | AIScriptViewModel | Main AI script editor |
| 125 | AIScriptCategorySelectView | AIScriptCategorySelectViewModel | AI script category picker |
| 126 | AIASMCALLTALKView | AIASMCALLTALKViewModel | AI ASM call/talk script |
| 127 | AIASMCoordinateView | AIASMCoordinateViewModel | AI ASM coordinate script |
| 128 | AIASMRangeView | AIASMRangeViewModel | AI ASM range script |
| 129 | AIMapSettingView | AIMapSettingViewModel | AI map setting editor |
| 130 | AIPerformItemView | AIPerformItemViewModel | AI item usage behavior |
| 131 | AIPerformStaffView | AIPerformStaffViewModel | AI staff usage behavior |
| 132 | AIStealItemView | AIStealItemViewModel | AI steal behavior |
| 133 | AITargetView | AITargetViewModel | AI target selection |
| 134 | AITilesView | AITilesViewModel | AI tile evaluation |
| 135 | AIUnitsView | AIUnitsViewModel | AI unit evaluation |
| 136 | AOERANGEView | AOERANGEViewModel | Area of effect range editor |

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
| 141 | ImagePortraitView | ImagePortraitViewModel | Portrait editor |
| 142 | ImagePortraitFE6View | ImagePortraitFE6ViewModel | Portrait editor (FE6) |
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
| 153 | ImageUnitPaletteView | ImageUnitPaletteViewModel | Unit sprite palette editor |
| 154 | ImageUnitWaitIconView | ImageUnitWaitIconViewModel | Unit wait/idle icon editor |
| 155 | ImageUnitMoveIconView | ImageUnitMoveIconViewModel | Unit move icon editor |
| 156 | ImageSystemAreaView | ImageSystemAreaViewModel | System area graphics editor |
| 157 | ImageGenericEnemyPortraitView | ImageGenericEnemyPortraitViewModel | Generic enemy portrait editor |
| 158 | ImageRomAnimeView | ImageRomAnimeViewModel | ROM-stored animation editor |
| 159 | ImageTSAEditorView | ImageTSAEditorViewModel | Tile Screen Arrangement editor |
| 160 | ImageTSAAnimeView | ImageTSAAnimeViewModel | TSA animation editor |
| 161 | ImageTSAAnime2View | ImageTSAAnime2ViewModel | TSA animation editor (type 2) |
| 162 | ImagePalletView | ImagePalletViewModel | Palette viewer/editor |
| 163 | ImageMagicFEditorView | ImageMagicFEditorViewModel | Magic effect frame editor |
| 164 | ImageMagicCSACreatorView | ImageMagicCSACreatorViewModel | Magic CSA (compressed sprite) creator |
| 165 | ImageMapActionAnimationView | ImageMapActionAnimationViewModel | Map action animation editor |
| 166 | ImageFormRefViewerView | ImageFormRefViewerViewModel | Image form reference viewer |
| 167 | InterpolatedPictureBoxViewerView | InterpolatedPictureBoxViewerViewModel | Interpolated image viewer |
| 168 | DecreaseColorTSAToolView | DecreaseColorTSAToolViewModel | Color reduction with TSA tool |
| 169 | SystemIconViewerView | SystemIconViewerViewModel | System icon viewer |
| 170 | SystemHoverColorViewerView | SystemHoverColorViewerViewModel | System hover color table viewer |
| 171 | BattleBGViewerView | BattleBGViewerViewModel | Battle background viewer |
| 172 | BattleTerrainViewerView | BattleTerrainViewerViewModel | Battle terrain viewer |
| 173 | ChapterTitleViewerView | ChapterTitleViewerViewModel | Chapter title image viewer |
| 174 | BigCGViewerView | BigCGViewerViewModel | Large CG image viewer |
| 175 | GraphicsToolView | GraphicsToolViewViewModel | Graphics tool main view |
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
| 189 | SongTrackImportWaveView | SongTrackImportWaveViewModel | Wave import for tracks |
| 190 | SongInstrumentView | SongInstrumentViewModel | Instrument editor |
| 191 | SongInstrumentDirectSoundView | SongInstrumentDirectSoundViewModel | Direct sound instrument editor |
| 192 | SongInstrumentImportWaveView | SongInstrumentImportWaveViewModel | Wave import for instruments |
| 193 | SongExchangeView | SongExchangeViewModel | Song exchange/swap tool |
| 194 | SoundBossBGMViewerView | SoundBossBGMViewerViewModel | Boss BGM assignment viewer |
| 195 | SoundFootStepsViewerView | SoundFootStepsViewerViewModel | Footstep sound assignment viewer |
| 196 | SoundRoomViewerView | SoundRoomViewerViewModel | Sound room entry viewer |
| 197 | SoundRoomFE6View | SoundRoomFE6ViewModel | Sound room (FE6 variant) |
| 198 | SoundRoomCGView | SoundRoomCGViewModel | Sound room CG display |
| 199 | ToolBGMMuteDialogView | ToolBGMMuteDialogViewModel | BGM mute settings dialog |

## 11. Text & Translation Editors

Editors for game text, fonts, translation, and text utilities.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 200 | TextViewerView | TextViewerViewModel | Main text viewer/editor |
| 201 | TextMainView | TextMainViewModel | Nested sub-view for text |
| 202 | OtherTextView | OtherTextViewModel | Other text data editor |
| 203 | CStringView | CStringViewModel | C-style string editor |
| 204 | FontEditorView | FontEditorViewModel | Font tile editor |
| 205 | FontZHView | FontZHViewModel | Chinese font editor |
| 206 | TextEscapeEditorView | TextEscapeEditorViewModel | Text escape code editor |
| 207 | TextScriptCategorySelectView | TextScriptCategorySelectViewModel | Text script category picker |
| 208 | TextDicView | TextDicViewModel | Text dictionary editor |
| 209 | TextCharCodeView | TextCharCodeViewModel | Character code table viewer |
| 210 | TextBadCharPopupView | TextBadCharPopupViewModel | Invalid character warning popup |
| 211 | TextRefAddDialogView | TextRefAddDialogViewModel | Text reference addition dialog |
| 212 | TextToSpeechView | TextToSpeechViewModel | Text-to-speech preview tool |
| 213 | DevTranslateView | DevTranslateViewModel | Developer translation tool |
| 214 | ToolTranslateROMView | ToolTranslateROMViewModel | ROM translation tool |

## 12. Support System Editors

Editors for support conversations and affinity.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 215 | SupportUnitEditorView | SupportUnitEditorViewModel | Support unit pair editor |
| 216 | SupportUnitFE6View | SupportUnitFE6ViewModel | Support units (FE6 variant) |
| 217 | SupportAttributeView | SupportAttributeViewModel | Support affinity attribute editor |
| 218 | SupportTalkView | SupportTalkViewModel | Support conversation editor |
| 219 | SupportTalkFE6View | SupportTalkFE6ViewModel | Support conversations (FE6) |
| 220 | SupportTalkFE7View | SupportTalkFE7ViewModel | Support conversations (FE7) |

## 13. Arena, Monster & Summon Editors

Editors for arena, monster, and summon data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 221 | ArenaClassViewerView | ArenaClassViewerViewModel | Arena class table viewer |
| 222 | ArenaEnemyWeaponViewerView | ArenaEnemyWeaponViewerViewModel | Arena enemy weapon table viewer |
| 223 | LinkArenaDenyUnitViewerView | LinkArenaDenyUnitViewerViewModel | Link arena banned unit viewer |
| 224 | MonsterProbabilityViewerView | MonsterProbabilityViewerViewModel | Monster spawn probability viewer |
| 225 | MonsterItemViewerView | MonsterItemViewerViewModel | Monster item drop viewer |
| 226 | MonsterWMapProbabilityViewerView | MonsterWMapProbabilityViewerViewModel | World map monster probability viewer |
| 227 | SummonUnitViewerView | SummonUnitViewerViewModel | Summoned unit table viewer |
| 228 | SummonsDemonKingViewerView | SummonsDemonKingViewerViewModel | Demon King summon table viewer |

## 14. Menu Editors

Editors for in-game menus and commands.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 229 | MenuDefinitionView | MenuDefinitionViewModel | Menu definition table editor |
| 230 | MenuCommandView | MenuCommandViewModel | Menu command table editor |
| 231 | MenuExtendSplitMenuView | MenuExtendSplitMenuViewModel | Extended split menu editor |
| 232 | FE8SpellMenuExtendsView | FE8SpellMenuExtendsViewModel | FE8 spell menu extension editor |

## 15. Ending (ED) Editors

Editors for game endings, staff rolls, and credits.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 233 | EDView | EDViewModel | Main ending data editor |
| 234 | EDStaffRollView | EDStaffRollViewModel | Staff roll/credits editor |
| 235 | EDFE6View | EDFE6ViewModel | Ending data (FE6 variant) |
| 236 | EDFE7View | EDFE7ViewModel | Ending data (FE7 variant) |
| 237 | EDSensekiCommentView | EDSensekiCommentViewModel | Battle record comment editor |

## 16. World Map Editors

Editors for world map nodes, paths, events, and images.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 238 | WorldMapPointView | WorldMapPointViewModel | World map node/point editor |
| 239 | WorldMapBGMView | WorldMapBGMViewModel | World map BGM assignment editor |
| 240 | WorldMapEventPointerView | WorldMapEventPointerViewModel | World map event pointer editor |
| 241 | WorldMapEventPointerFE6View | WorldMapEventPointerFE6ViewModel | World map events (FE6) |
| 242 | WorldMapEventPointerFE7View | WorldMapEventPointerFE7ViewModel | World map events (FE7) |
| 243 | WorldMapPathView | WorldMapPathViewModel | World map path editor |
| 244 | WorldMapPathEditorView | WorldMapPathEditorViewModel | World map path visual editor |
| 245 | WorldMapPathMoveEditorView | WorldMapPathMoveEditorViewModel | World map path movement editor |
| 246 | WorldMapImageView | WorldMapImageViewModel | World map image editor |
| 247 | WorldMapImageFE6View | WorldMapImageFE6ViewModel | World map image (FE6) |
| 248 | WorldMapImageFE7View | WorldMapImageFE7ViewModel | World map image (FE7) |

## 17. Opening (OP) Editors

Editors for opening demos, fonts, prologues, and alpha names.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 249 | OPClassDemoViewerView | OPClassDemoViewerViewModel | Opening class demo viewer |
| 250 | OPClassDemoFE7View | OPClassDemoFE7ViewModel | Class demo (FE7 variant) |
| 251 | OPClassDemoFE7UView | OPClassDemoFE7UViewModel | Class demo (FE7U variant) |
| 252 | OPClassDemoFE8UView | OPClassDemoFE8UViewModel | Class demo (FE8U variant) |
| 253 | OPClassFontViewerView | OPClassFontViewerViewModel | Opening class font viewer |
| 254 | OPClassFontFE8UView | OPClassFontFE8UViewModel | Class font (FE8U variant) |
| 255 | OPClassAlphaNameView | OPClassAlphaNameViewModel | Opening alpha name editor |
| 256 | OPClassAlphaNameFE6View | OPClassAlphaNameFE6ViewModel | Alpha name (FE6 variant) |
| 257 | OPClassAlphaNameFE6ExtraView | OPClassAlphaNameFE6ExtraViewModel | Alpha name extra (FE6) |
| 258 | OPPrologueViewerView | OPPrologueViewerViewModel | Opening prologue viewer |
| 259 | ClassOPDemoView | ClassOPDemoViewModel | Class opening demo data editor |
| 260 | ClassOPFontView | ClassOPFontViewModel | Class opening font data editor |

## 18. Status Screen Editors

Editors for status screen layout and parameters.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 261 | StatusParamView | StatusParamViewModel | Status parameter display editor |
| 262 | StatusRMenuView | StatusRMenuViewModel | Status R-button menu editor |
| 263 | StatusUnitsMenuView | StatusUnitsMenuViewModel | Status units menu editor |
| 264 | StatusOptionOrderView | StatusOptionOrderViewModel | Status option order editor |
| 265 | StatusOptionView | StatusOptionViewModel | Status option data editor |

## 19. Skill System Editors

Editors for various skill system implementations.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 266 | SkillAssignmentUnitSkillSystemView | SkillAssignmentUnitSkillSystemViewModel | Unit skill assignment (SkillSystem) |
| 267 | SkillAssignmentClassSkillSystemView | SkillAssignmentClassSkillSystemViewModel | Class skill assignment (SkillSystem) |
| 268 | SkillConfigSkillSystemView | SkillConfigSkillSystemViewModel | Skill config (SkillSystem) |
| 269 | SkillAssignmentUnitCSkillSysView | SkillAssignmentUnitCSkillSysViewModel | Unit skill assignment (CSkillSys) |
| 270 | SkillAssignmentClassCSkillSysView | SkillAssignmentClassCSkillSysViewModel | Class skill assignment (CSkillSys) |
| 271 | SkillAssignmentUnitFE8NView | SkillAssignmentUnitFE8NViewModel | Unit skill assignment (FE8N) |
| 272 | SkillConfigFE8NSkillView | SkillConfigFE8NSkillViewModel | Skill config (FE8N) |
| 273 | SkillConfigFE8NVer2SkillView | SkillConfigFE8NVer2SkillViewModel | Skill config (FE8N v2) |
| 274 | SkillConfigFE8NVer3SkillView | SkillConfigFE8NVer3SkillViewModel | Skill config (FE8N v3) |
| 275 | SkillConfigFE8UCSkillSys09xView | SkillConfigFE8UCSkillSys09xViewModel | Skill config (FE8U CSkillSys 0.9.x) |
| 276 | SkillSystemsEffectivenessReworkClassTypeView | SkillSystemsEffectivenessReworkClassTypeViewModel | Effectiveness rework class type |
| 277 | SkillSystemsCSkillRechainView | SkillSystemsCSkillRechainViewModel | CSkill rechain editor |

## 20. Patch Management

Editors for patch installation, filtering, and custom builds.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 278 | PatchManagerView | PatchManagerViewModel | Patch manager main view |
| 279 | PatchFilterExView | PatchFilterExViewModel | Patch filter editor |
| 280 | PatchFormUninstallDialogView | PatchFormUninstallDialogViewModel | Patch uninstall confirmation |
| 281 | PatchUninstallDialogView | PatchUninstallDialogViewModel | Patch uninstall dialog |
| 282 | HowDoYouLikePatchView | HowDoYouLikePatchViewModel | Patch feedback dialog |
| 283 | HowDoYouLikePatch2View | HowDoYouLikePatch2ViewModel | Patch feedback dialog (v2) |
| 284 | ToolCustomBuildView | ToolCustomBuildViewModel | Custom build configuration |

## 21. Hex Editor & Disassembly Tools

Low-level ROM editing and disassembly tools.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 285 | HexEditorView | HexEditorViewModel | Hex editor main view |
| 286 | HexEditorJumpView | HexEditorJumpViewModel | Hex editor jump-to-address dialog |
| 287 | HexEditorMarkView | HexEditorMarkViewModel | Hex editor bookmark manager |
| 288 | HexEditorSearchView | HexEditorSearchViewModel | Hex editor search dialog |
| 289 | DisASMView | DisASMViewModel | Disassembler main view |
| 290 | DisASMDumpAllView | DisASMDumpAllViewModel | Dump all disassembly |
| 291 | DisASMDumpAllArgGrepView | DisASMDumpAllArgGrepViewModel | Disassembly argument grep |
| 292 | ToolASMEditView | ToolASMEditViewViewModel | ASM code editor |
| 293 | ToolASMInsertView | ToolASMInsertViewModel | ASM code insertion tool |
| 294 | ToolDecompileResultView | ToolDecompileResultViewViewModel | Decompilation result viewer |

## 22. Pointer & Memory Tools

Pointer manipulation and emulator memory access tools.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 295 | PointerToolView | PointerToolViewModel | Pointer manipulation tool |
| 296 | PointerToolBatchInputView | PointerToolBatchInputViewModel | Batch pointer input |
| 297 | PointerToolCopyToView | PointerToolCopyToViewModel | Copy pointer data tool |
| 298 | MoveToFreeSpaceView | MoveToFreeSpaceViewViewModel | Move data to free space tool |
| 299 | PackedMemorySlotView | PackedMemorySlotViewModel | Packed memory slot viewer |
| 300 | EmulatorMemoryView | EmulatorMemoryViewModel | Emulator memory viewer |
| 301 | RAMRewriteToolView | RAMRewriteToolViewModel | RAM rewrite tool |
| 302 | RAMRewriteToolMAPView | RAMRewriteToolMAPViewViewModel | RAM rewrite tool (map view) |

## 23. Bit Flag Editors

Generic bit flag editing dialogs for different data widths.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 303 | UbyteBitFlagView | UbyteBitFlagViewModel | 8-bit (byte) flag editor |
| 304 | UshortBitFlagView | UshortBitFlagViewModel | 16-bit (ushort) flag editor |
| 305 | UwordBitFlagView | UwordBitFlagViewModel | 32-bit (uint) flag editor |

## 24. Structural Data Editors

Editors for miscellaneous ROM structure data.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 306 | Command85PointerView | Command85PointerViewModel | Command 0x85 pointer table editor |
| 307 | DumpStructSelectDialogView | DumpStructSelectDialogViewModel | Struct dump selection dialog |
| 308 | DumpStructSelectToTextDialogView | DumpStructSelectToTextDialogViewModel | Struct dump to text dialog |
| 309 | ResourceView | ResourceViewModel | Resource table viewer |

## 25. Error & Notification Dialogs

Error reporting, palette warnings, and notification overlays.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 310 | ErrorReportView | ErrorReportViewModel | Error report dialog |
| 311 | ErrorPaletteMissMatchView | ErrorPaletteMissMatchViewModel | Palette mismatch warning |
| 312 | ErrorPaletteShowView | ErrorPaletteShowViewModel | Palette error display |
| 313 | ErrorPaletteTransparentView | ErrorPaletteTransparentViewModel | Transparent palette error |
| 314 | ErrorTSAErrorView | ErrorTSAErrorViewModel | TSA error display |
| 315 | ErrorLongMessageDialogView | ErrorLongMessageDialogViewModel | Long error message dialog |
| 316 | NotifyDirectInjectionView | NotifyDirectInjectionViewModel | Direct injection notification |
| 317 | NotifyPleaseWaitView | NotifyPleaseWaitViewModel | Please wait notification overlay |
| 318 | NotifyWriteView | NotifyWriteViewModel | Write notification overlay |

## 26. Tool Utilities

Miscellaneous tools for ROM management, lint, diff, UPS, and more.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 319 | ToolUndoView | ToolUndoViewModel | Undo history viewer |
| 320 | ToolUndoPopupDialogView | ToolUndoPopupDialogViewModel | Undo popup dialog |
| 321 | ToolFELintView | ToolFELintViewModel | FE Lint ROM validation tool |
| 322 | ToolROMRebuildView | ToolROMRebuildViewModel | ROM rebuild tool |
| 323 | ToolLZ77View | ToolLZ77ViewModel | LZ77 compression/decompression tool |
| 324 | ToolDiffView | ToolDiffViewModel | ROM diff comparison tool |
| 325 | ToolDiffDebugSelectView | ToolDiffDebugSelectViewModel | Diff debug selection dialog |
| 326 | ToolUPSPatchSimpleView | ToolUPSPatchSimpleViewModel | UPS patch application tool |
| 327 | ToolUPSOpenSimpleView | ToolUPSOpenSimpleViewModel | UPS patch open tool |
| 328 | ToolFlagNameView | ToolFlagNameViewModel | Flag name editor |
| 329 | ToolUseFlagView | ToolUseFlagViewModel | Flag usage viewer |
| 330 | ToolUnitTalkGroupView | ToolUnitTalkGroupViewModel | Unit talk group editor |
| 331 | ToolAnimationCreatorView | ToolAnimationCreatorViewModel | Animation creator tool |
| 332 | ToolThreeMargeView | ToolThreeMargeViewModel | Three-way merge tool |
| 333 | ToolThreeMargeCloseAlertView | ToolThreeMargeCloseAlertViewModel | Three-way merge close alert |
| 334 | ToolExportEAEventView | ToolExportEAEventViewViewModel | Export events to EA format |
| 335 | ToolChangeProjectnameView | ToolChangeProjectnameViewViewModel | Project name change tool |
| 336 | ToolAutomaticRecoveryROMHeaderView | ToolAutomaticRecoveryROMHeaderViewViewModel | ROM header recovery tool |
| 337 | ToolClickWriteFloatControlPanelButtonView | ToolClickWriteFloatControlPanelButtonViewModel | Float control panel button tool |
| 338 | ToolSubtitleOverlayView | ToolSubtitleOverlayViewModel | Subtitle overlay tool |
| 339 | ToolSubtitleSettingDialogView | ToolSubtitleSettingDialogViewViewModel | Subtitle settings dialog |
| 340 | ToolInitWizardView | ToolInitWizardViewModel | Initial setup wizard |
| 341 | ToolUpdateDialogView | ToolUpdateDialogViewModel | Application update dialog |
| 342 | ToolRunHintMessageView | ToolRunHintMessageViewModel | Run hint message dialog |
| 343 | ToolEmulatorSetupMessageView | ToolEmulatorSetupMessageViewModel | Emulator setup message |
| 344 | ToolProblemReportView | ToolProblemReportViewModel | Problem report tool |
| 345 | ToolProblemReportSearchBackupView | ToolProblemReportSearchBackupViewModel | Problem report backup search |
| 346 | ToolProblemReportSearchSavView | ToolProblemReportSearchSavViewModel | Problem report save search |
| 347 | ToolAllWorkSupportView | ToolAllWorkSupportViewModel | All work support tool |
| 348 | ToolWorkSupportView | ToolWorkSupportViewModel | Work support tool |
| 349 | ToolWorkSupport_SelectUPSView | ToolWorkSupport_SelectUPSViewModel | Work support UPS selection |
| 350 | ToolWorkSupport_UpdateQuestionDialogView | ToolWorkSupport_UpdateQuestionDialogViewModel | Work support update question |

## 27. Simple Menu Mode

Simplified beginner-mode views.

| # | View | ViewModel | Description |
|---|------|-----------|-------------|
| 351 | MainSimpleMenuView | MainSimpleMenuViewModel | Simplified main menu |
| 352 | MainSimpleMenuEventErrorView | MainSimpleMenuEventErrorViewModel | Simple menu event error display |
| 353 | MainSimpleMenuEventErrorIgnoreErrorView | MainSimpleMenuEventErrorIgnoreErrorViewModel | Simple menu ignore error dialog |
| 354 | MainSimpleMenuImageSubView | MainSimpleMenuImageSubViewModel | Simple menu image sub-view |

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
