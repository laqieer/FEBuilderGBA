# Avalonia GUI Forms — E2E Coverage Tracker

This document lists all GUI forms (views) in `FEBuilderGBA.Avalonia` that are
accessible from `MainWindow` and tracks their E2E test coverage status.

**Total forms:** 185
**E2E Covered:** 185 / 185

E2E coverage is provided by `AvaloniaAllEditorsSmokeTests` which uses the
`--smoke-test-all` flag to open and close every editor listed below.

Unit test `AvaloniaAllEditorsCoverageTests` verifies that the smoke test
factory list, MainWindow click handlers, and this document stay in sync.

---

## Data Editors

| # | View | E2E Status |
|---|------|-----------|
| 1 | UnitEditorView | E2E COVERED |
| 2 | ItemEditorView | E2E COVERED |
| 3 | ClassEditorView | E2E COVERED |
| 4 | ClassFE6View | E2E COVERED |
| 5 | CCBranchEditorView | E2E COVERED |
| 6 | MoveCostEditorView | E2E COVERED |
| 7 | TerrainNameEditorView | E2E COVERED |
| 8 | SupportUnitEditorView | E2E COVERED |
| 9 | SupportAttributeView | E2E COVERED |
| 10 | SupportTalkView | E2E COVERED |
| 11 | UnitFE6View | E2E COVERED |
| 12 | UnitActionPointerView | E2E COVERED |
| 13 | UnitCustomBattleAnimeView | E2E COVERED |
| 14 | UnitIncreaseHeightView | E2E COVERED |
| 15 | UnitPaletteView | E2E COVERED |
| 16 | ClassOPDemoView | E2E COVERED |
| 17 | ClassOPFontView | E2E COVERED |
| 18 | ExtraUnitView | E2E COVERED |
| 19 | ExtraUnitFE8UView | E2E COVERED |

## Item Editors

| # | View | E2E Status |
|---|------|-----------|
| 20 | ItemWeaponEffectViewerView | E2E COVERED |
| 21 | ItemStatBonusesViewerView | E2E COVERED |
| 22 | ItemEffectivenessViewerView | E2E COVERED |
| 23 | ItemPromotionViewerView | E2E COVERED |
| 24 | ItemShopViewerView | E2E COVERED |
| 25 | ItemWeaponTriangleViewerView | E2E COVERED |
| 26 | ItemUsagePointerViewerView | E2E COVERED |
| 27 | ItemEffectPointerViewerView | E2E COVERED |
| 28 | ItemIconViewerView | E2E COVERED |

## Map Editors

| # | View | E2E Status |
|---|------|-----------|
| 29 | MapSettingView | E2E COVERED |
| 30 | MapChangeView | E2E COVERED |
| 31 | MapExitPointView | E2E COVERED |
| 32 | MapPointerView | E2E COVERED |
| 33 | MapTileAnimationView | E2E COVERED |
| 34 | MapEditorView | E2E COVERED |
| 35 | MapSettingFE6View | E2E COVERED |
| 36 | MapSettingFE7View | E2E COVERED |
| 37 | MapSettingFE7UView | E2E COVERED |
| 38 | MapSettingDifficultyView | E2E COVERED |
| 39 | MapStyleEditorView | E2E COVERED |
| 40 | MapTerrainBGLookupView | E2E COVERED |
| 41 | MapTerrainFloorLookupView | E2E COVERED |
| 42 | MapMiniMapTerrainImageView | E2E COVERED |
| 43 | MapTileAnimation1View | E2E COVERED |
| 44 | MapTileAnimation2View | E2E COVERED |
| 45 | MapLoadFunctionView | E2E COVERED |
| 46 | MapTerrainNameEngView | E2E COVERED |

## Event Script Editors

| # | View | E2E Status |
|---|------|-----------|
| 47 | EventCondView | E2E COVERED |
| 48 | EventScriptView | E2E COVERED |
| 49 | EventUnitView | E2E COVERED |
| 50 | EventUnitFE6View | E2E COVERED |
| 51 | EventUnitFE7View | E2E COVERED |
| 52 | EventUnitColorView | E2E COVERED |
| 53 | EventUnitItemDropView | E2E COVERED |
| 54 | EventUnitNewAllocView | E2E COVERED |
| 55 | EventBattleTalkView | E2E COVERED |
| 56 | EventBattleTalkFE6View | E2E COVERED |
| 57 | EventBattleTalkFE7View | E2E COVERED |
| 58 | EventBattleDataFE7View | E2E COVERED |
| 59 | EventHaikuView | E2E COVERED |
| 60 | EventHaikuFE6View | E2E COVERED |
| 61 | EventHaikuFE7View | E2E COVERED |
| 62 | EventMapChangeView | E2E COVERED |
| 63 | EventForceSortieView | E2E COVERED |
| 64 | EventForceSortieFE7View | E2E COVERED |
| 65 | EventFunctionPointerView | E2E COVERED |
| 66 | EventFunctionPointerFE7View | E2E COVERED |
| 67 | EventAssemblerView | E2E COVERED |
| 68 | ProcsScriptView | E2E COVERED |
| 69 | EventScriptTemplateView | E2E COVERED |

## AI Script Editors

| # | View | E2E Status |
|---|------|-----------|
| 70 | AIScriptView | E2E COVERED |
| 71 | AIASMCALLTALKView | E2E COVERED |
| 72 | AIASMCoordinateView | E2E COVERED |
| 73 | AIASMRangeView | E2E COVERED |
| 74 | AIMapSettingView | E2E COVERED |
| 75 | AIPerformItemView | E2E COVERED |
| 76 | AIPerformStaffView | E2E COVERED |
| 77 | AIStealItemView | E2E COVERED |
| 78 | AITargetView | E2E COVERED |
| 79 | AITilesView | E2E COVERED |
| 80 | AIUnitsView | E2E COVERED |
| 81 | AOERANGEView | E2E COVERED |

## Image Editors

| # | View | E2E Status |
|---|------|-----------|
| 82 | ImageViewerView | E2E COVERED |
| 83 | PortraitViewerView | E2E COVERED |
| 84 | ImagePortraitView | E2E COVERED |
| 85 | ImagePortraitFE6View | E2E COVERED |
| 86 | ImagePortraitImporterView | E2E COVERED |
| 87 | ImageBGView | E2E COVERED |
| 88 | ImageBattleAnimeView | E2E COVERED |
| 89 | ImageBattleAnimePalletView | E2E COVERED |
| 90 | ImageBattleBGView | E2E COVERED |
| 91 | ImageBattleScreenView | E2E COVERED |
| 92 | ImageCGView | E2E COVERED |
| 93 | ImageCGFE7UView | E2E COVERED |
| 94 | ImageUnitPaletteView | E2E COVERED |
| 95 | ImageUnitWaitIconView | E2E COVERED |
| 96 | ImageUnitMoveIconView | E2E COVERED |
| 97 | ImageSystemAreaView | E2E COVERED |
| 98 | ImageGenericEnemyPortraitView | E2E COVERED |
| 99 | ImageRomAnimeView | E2E COVERED |
| 100 | ImageTSAEditorView | E2E COVERED |
| 101 | ImageTSAAnimeView | E2E COVERED |
| 102 | ImageTSAAnime2View | E2E COVERED |
| 103 | ImagePalletView | E2E COVERED |
| 104 | ImageMagicFEditorView | E2E COVERED |
| 105 | ImageMagicCSACreatorView | E2E COVERED |
| 106 | ImageMapActionAnimationView | E2E COVERED |
| 107 | DecreaseColorTSAToolView | E2E COVERED |
| 108 | SystemIconViewerView | E2E COVERED |
| 109 | SystemHoverColorViewerView | E2E COVERED |
| 110 | BattleBGViewerView | E2E COVERED |
| 111 | BattleTerrainViewerView | E2E COVERED |
| 112 | ChapterTitleViewerView | E2E COVERED |
| 113 | BigCGViewerView | E2E COVERED |
| 114 | OPClassDemoViewerView | E2E COVERED |
| 115 | OPClassFontViewerView | E2E COVERED |
| 116 | OPPrologueViewerView | E2E COVERED |

## Audio Editors

| # | View | E2E Status |
|---|------|-----------|
| 117 | SongTableView | E2E COVERED |
| 118 | SongTrackView | E2E COVERED |
| 119 | SongInstrumentView | E2E COVERED |
| 120 | SongInstrumentDirectSoundView | E2E COVERED |
| 121 | SongInstrumentImportWaveView | E2E COVERED |
| 122 | SongTrackImportMidiView | E2E COVERED |
| 123 | SongExchangeView | E2E COVERED |
| 124 | SoundBossBGMViewerView | E2E COVERED |
| 125 | SoundFootStepsViewerView | E2E COVERED |
| 126 | SoundRoomViewerView | E2E COVERED |
| 127 | SoundRoomFE6View | E2E COVERED |
| 128 | SoundRoomCGView | E2E COVERED |

## Arena / Monster / Summon Editors

| # | View | E2E Status |
|---|------|-----------|
| 129 | ArenaClassViewerView | E2E COVERED |
| 130 | ArenaEnemyWeaponViewerView | E2E COVERED |
| 131 | LinkArenaDenyUnitViewerView | E2E COVERED |
| 132 | MonsterProbabilityViewerView | E2E COVERED |
| 133 | MonsterItemViewerView | E2E COVERED |
| 134 | MonsterWMapProbabilityViewerView | E2E COVERED |
| 135 | SummonUnitViewerView | E2E COVERED |
| 136 | SummonsDemonKingViewerView | E2E COVERED |

## Menu / ED / World Map Editors

| # | View | E2E Status |
|---|------|-----------|
| 137 | MenuDefinitionView | E2E COVERED |
| 138 | MenuCommandView | E2E COVERED |
| 139 | EDView | E2E COVERED |
| 140 | EDStaffRollView | E2E COVERED |
| 141 | WorldMapPointView | E2E COVERED |
| 142 | WorldMapBGMView | E2E COVERED |
| 143 | WorldMapEventPointerView | E2E COVERED |
| 144 | WorldMapPathView | E2E COVERED |
| 145 | WorldMapPathEditorView | E2E COVERED |
| 146 | WorldMapImageView | E2E COVERED |
| 147 | WorldMapImageFE6View | E2E COVERED |
| 148 | WorldMapImageFE7View | E2E COVERED |
| 149 | WorldMapEventPointerFE6View | E2E COVERED |
| 150 | WorldMapEventPointerFE7View | E2E COVERED |

## Text / Translation Editors

| # | View | E2E Status |
|---|------|-----------|
| 151 | TextViewerView | E2E COVERED |
| 152 | TextMainView | E2E COVERED |
| 153 | OtherTextView | E2E COVERED |
| 154 | CStringView | E2E COVERED |
| 155 | FontEditorView | E2E COVERED |
| 156 | FontZHView | E2E COVERED |
| 157 | DevTranslateView | E2E COVERED |
| 158 | ToolTranslateROMView | E2E COVERED |
| 159 | TextEscapeEditorView | E2E COVERED |

## Structural Data Editors

| # | View | E2E Status |
|---|------|-----------|
| 160 | Command85PointerView | E2E COVERED |
| 161 | FE8SpellMenuExtendsView | E2E COVERED |
| 162 | StatusOptionView | E2E COVERED |
| 163 | OAMSPView | E2E COVERED |
| 164 | DumpStructSelectDialogView | E2E COVERED |

## Patch / Skill Systems

| # | View | E2E Status |
|---|------|-----------|
| 165 | PatchManagerView | E2E COVERED |
| 166 | ToolCustomBuildView | E2E COVERED |
| 167 | SkillAssignmentUnitSkillSystemView | E2E COVERED |
| 168 | SkillAssignmentClassSkillSystemView | E2E COVERED |
| 169 | SkillConfigSkillSystemView | E2E COVERED |

## Tools

| # | View | E2E Status |
|---|------|-----------|
| 170 | ToolUndoView | E2E COVERED |
| 171 | ToolFELintView | E2E COVERED |
| 172 | ToolROMRebuildView | E2E COVERED |
| 173 | ToolLZ77View | E2E COVERED |
| 174 | ToolDiffView | E2E COVERED |
| 175 | ToolUPSPatchSimpleView | E2E COVERED |
| 176 | ToolUPSOpenSimpleView | E2E COVERED |
| 177 | ToolFlagNameView | E2E COVERED |
| 178 | ToolUseFlagView | E2E COVERED |
| 179 | ToolUnitTalkGroupView | E2E COVERED |
| 180 | ToolASMInsertView | E2E COVERED |
| 181 | HexEditorView | E2E COVERED |
| 182 | DisASMView | E2E COVERED |
| 183 | LogViewerView | E2E COVERED |
| 184 | GrowSimulatorView | E2E COVERED |
| 185 | OptionsView | E2E COVERED |
