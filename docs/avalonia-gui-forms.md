# Avalonia GUI Forms — E2E Coverage & Alignment Tracker

This document lists all GUI forms (views) in `FEBuilderGBA.Avalonia` that are
accessible from `MainWindow` and tracks their E2E test coverage and visual
alignment status against the WinForms GUI.

**Total forms:** 323
**E2E Covered:** 323 / 323
**Field Aligned:** 0 / 323 (UNVERIFIED — field completeness comparison in progress)

E2E coverage is provided by `AvaloniaAllEditorsSmokeTests` which uses the
`--smoke-test-all` flag to open and close every editor listed below.

Unit test `AvaloniaAllEditorsCoverageTests` verifies that the smoke test
factory list, MainWindow click handlers, and this document stay in sync.

### Data Verification

The **Data Verified** column tracks whether an editor's ViewModel implements
`IDataVerifiable` and its loaded data is cross-checked against raw ROM bytes
via the `--data-verify` mode. This goes beyond smoke testing (which only checks
that editors open without crashing) by verifying that field values match the
actual ROM data at the correct addresses and offsets.

---

## Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 1 | UnitEditorView | E2E COVERED | YES | UNVERIFIED |
| 2 | ItemEditorView | E2E COVERED | YES | UNVERIFIED |
| 3 | ClassEditorView | E2E COVERED | YES | UNVERIFIED |
| 4 | ClassFE6View | E2E COVERED | - | UNVERIFIED |
| 5 | CCBranchEditorView | E2E COVERED | YES | UNVERIFIED |
| 6 | MoveCostEditorView | E2E COVERED | YES | UNVERIFIED |
| 7 | TerrainNameEditorView | E2E COVERED | YES | UNVERIFIED |
| 8 | SupportUnitEditorView | E2E COVERED | YES | UNVERIFIED |
| 9 | SupportAttributeView | E2E COVERED | YES | UNVERIFIED |
| 10 | SupportTalkView | E2E COVERED | YES | UNVERIFIED |
| 11 | UnitFE6View | E2E COVERED | - | UNVERIFIED |
| 12 | UnitActionPointerView | E2E COVERED | - | UNVERIFIED |
| 13 | UnitCustomBattleAnimeView | E2E COVERED | - | UNVERIFIED |
| 14 | UnitIncreaseHeightView | E2E COVERED | - | UNVERIFIED |
| 15 | UnitPaletteView | E2E COVERED | - | UNVERIFIED |
| 16 | ClassOPDemoView | E2E COVERED | - | UNVERIFIED |
| 17 | ClassOPFontView | E2E COVERED | - | UNVERIFIED |
| 18 | ExtraUnitView | E2E COVERED | - | UNVERIFIED |
| 19 | ExtraUnitFE8UView | E2E COVERED | - | UNVERIFIED |
| 20 | UnitFE7View | E2E COVERED | - | UNVERIFIED |
| 21 | ItemFE6View | E2E COVERED | - | UNVERIFIED |
| 22 | MoveCostFE6View | E2E COVERED | - | UNVERIFIED |
| 23 | SupportUnitFE6View | E2E COVERED | - | UNVERIFIED |
| 24 | SupportTalkFE6View | E2E COVERED | - | UNVERIFIED |
| 25 | SupportTalkFE7View | E2E COVERED | - | UNVERIFIED |
| 26 | UnitsShortTextView | E2E COVERED | - | UNVERIFIED |
| 27 | SomeClassListView | E2E COVERED | - | UNVERIFIED |
| 28 | VennouWeaponLockView | E2E COVERED | - | UNVERIFIED |

## Item Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 29 | ItemWeaponEffectViewerView | E2E COVERED | YES | UNVERIFIED |
| 30 | ItemStatBonusesViewerView | E2E COVERED | YES | UNVERIFIED |
| 31 | ItemEffectivenessViewerView | E2E COVERED | YES | UNVERIFIED |
| 32 | ItemPromotionViewerView | E2E COVERED | YES | UNVERIFIED |
| 33 | ItemShopViewerView | E2E COVERED | YES | UNVERIFIED |
| 34 | ItemWeaponTriangleViewerView | E2E COVERED | YES | UNVERIFIED |
| 35 | ItemUsagePointerViewerView | E2E COVERED | YES | UNVERIFIED |
| 36 | ItemEffectPointerViewerView | E2E COVERED | YES | UNVERIFIED |
| 37 | ItemIconViewerView | E2E COVERED | YES | UNVERIFIED |

## Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 38 | MapSettingView | E2E COVERED | YES | UNVERIFIED |
| 39 | MapChangeView | E2E COVERED | YES | UNVERIFIED |
| 40 | MapExitPointView | E2E COVERED | YES | UNVERIFIED |
| 41 | MapPointerView | E2E COVERED | YES | UNVERIFIED |
| 42 | MapTileAnimationView | E2E COVERED | YES | UNVERIFIED |
| 43 | MapEditorView | E2E COVERED | - | UNVERIFIED |
| 44 | MapSettingFE6View | E2E COVERED | - | UNVERIFIED |
| 45 | MapSettingFE7View | E2E COVERED | - | UNVERIFIED |
| 46 | MapSettingFE7UView | E2E COVERED | - | UNVERIFIED |
| 47 | MapSettingDifficultyView | E2E COVERED | - | UNVERIFIED |
| 48 | MapStyleEditorView | E2E COVERED | - | UNVERIFIED |
| 49 | MapTerrainBGLookupView | E2E COVERED | - | UNVERIFIED |
| 50 | MapTerrainFloorLookupView | E2E COVERED | - | UNVERIFIED |
| 51 | MapMiniMapTerrainImageView | E2E COVERED | - | UNVERIFIED |
| 52 | MapTileAnimation1View | E2E COVERED | - | UNVERIFIED |
| 53 | MapTileAnimation2View | E2E COVERED | - | UNVERIFIED |
| 54 | MapLoadFunctionView | E2E COVERED | - | UNVERIFIED |
| 55 | MapTerrainNameEngView | E2E COVERED | - | UNVERIFIED |
| 56 | MapEditorAddMapChangeDialogView | E2E COVERED | YES | UNVERIFIED |
| 57 | MapEditorMarSizeDialogView | E2E COVERED | YES | UNVERIFIED |
| 58 | MapEditorResizeDialogView | E2E COVERED | YES | UNVERIFIED |
| 59 | MapPointerNewPLISTPopupView | E2E COVERED | YES | UNVERIFIED |
| 60 | MapStyleEditorAppendPopupView | E2E COVERED | YES | UNVERIFIED |
| 61 | MapStyleEditorWarningOverrideView | E2E COVERED | YES | UNVERIFIED |
| 62 | MapStyleEditorImportImageOptionView | E2E COVERED | YES | UNVERIFIED |
| 63 | MapSettingDifficultyDialogView | E2E COVERED | YES | UNVERIFIED |

## Event Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 64 | EventCondView | E2E COVERED | YES | UNVERIFIED |
| 65 | EventScriptView | E2E COVERED | - | UNVERIFIED |
| 66 | EventUnitView | E2E COVERED | - | UNVERIFIED |
| 67 | EventUnitFE6View | E2E COVERED | - | UNVERIFIED |
| 68 | EventUnitFE7View | E2E COVERED | - | UNVERIFIED |
| 69 | EventUnitColorView | E2E COVERED | - | UNVERIFIED |
| 70 | EventUnitItemDropView | E2E COVERED | - | UNVERIFIED |
| 71 | EventUnitNewAllocView | E2E COVERED | - | UNVERIFIED |
| 72 | EventBattleTalkView | E2E COVERED | - | UNVERIFIED |
| 73 | EventBattleTalkFE6View | E2E COVERED | - | UNVERIFIED |
| 74 | EventBattleTalkFE7View | E2E COVERED | - | UNVERIFIED |
| 75 | EventBattleDataFE7View | E2E COVERED | - | UNVERIFIED |
| 76 | EventHaikuView | E2E COVERED | - | UNVERIFIED |
| 77 | EventHaikuFE6View | E2E COVERED | - | UNVERIFIED |
| 78 | EventHaikuFE7View | E2E COVERED | - | UNVERIFIED |
| 79 | EventMapChangeView | E2E COVERED | - | UNVERIFIED |
| 80 | EventForceSortieView | E2E COVERED | - | UNVERIFIED |
| 81 | EventForceSortieFE7View | E2E COVERED | - | UNVERIFIED |
| 82 | EventFunctionPointerView | E2E COVERED | - | UNVERIFIED |
| 83 | EventFunctionPointerFE7View | E2E COVERED | - | UNVERIFIED |
| 84 | EventAssemblerView | E2E COVERED | - | UNVERIFIED |
| 85 | ProcsScriptView | E2E COVERED | - | UNVERIFIED |
| 86 | EventScriptTemplateView | E2E COVERED | - | UNVERIFIED |
| 87 | EventScriptCategorySelectView | E2E COVERED | - | UNVERIFIED |
| 88 | EventScriptPopupView | E2E COVERED | - | UNVERIFIED |
| 89 | ProcsScriptCategorySelectView | E2E COVERED | - | UNVERIFIED |
| 90 | AIScriptCategorySelectView | E2E COVERED | - | UNVERIFIED |

## AI Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 91 | AIScriptView | E2E COVERED | - | UNVERIFIED |
| 92 | AIASMCALLTALKView | E2E COVERED | - | UNVERIFIED |
| 93 | AIASMCoordinateView | E2E COVERED | - | UNVERIFIED |
| 94 | AIASMRangeView | E2E COVERED | - | UNVERIFIED |
| 95 | AIMapSettingView | E2E COVERED | - | UNVERIFIED |
| 96 | AIPerformItemView | E2E COVERED | - | UNVERIFIED |
| 97 | AIPerformStaffView | E2E COVERED | - | UNVERIFIED |
| 98 | AIStealItemView | E2E COVERED | - | UNVERIFIED |
| 99 | AITargetView | E2E COVERED | - | UNVERIFIED |
| 100 | AITilesView | E2E COVERED | - | UNVERIFIED |
| 101 | AIUnitsView | E2E COVERED | - | UNVERIFIED |
| 102 | AOERANGEView | E2E COVERED | - | UNVERIFIED |

## Image Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 103 | ImageViewerView | E2E COVERED | YES | UNVERIFIED |
| 104 | PortraitViewerView | E2E COVERED | YES | UNVERIFIED |
| 105 | ImagePortraitView | E2E COVERED | - | UNVERIFIED |
| 106 | ImagePortraitFE6View | E2E COVERED | - | UNVERIFIED |
| 107 | ImagePortraitImporterView | E2E COVERED | - | UNVERIFIED |
| 108 | ImageBGView | E2E COVERED | - | UNVERIFIED |
| 109 | ImageBattleAnimeView | E2E COVERED | - | UNVERIFIED |
| 110 | ImageBattleAnimePalletView | E2E COVERED | - | UNVERIFIED |
| 111 | ImageBattleBGView | E2E COVERED | - | UNVERIFIED |
| 112 | ImageBattleScreenView | E2E COVERED | - | UNVERIFIED |
| 113 | ImageCGView | E2E COVERED | - | UNVERIFIED |
| 114 | ImageCGFE7UView | E2E COVERED | - | UNVERIFIED |
| 115 | ImageUnitPaletteView | E2E COVERED | - | UNVERIFIED |
| 116 | ImageUnitWaitIconView | E2E COVERED | - | UNVERIFIED |
| 117 | ImageUnitMoveIconView | E2E COVERED | - | UNVERIFIED |
| 118 | ImageSystemAreaView | E2E COVERED | - | UNVERIFIED |
| 119 | ImageGenericEnemyPortraitView | E2E COVERED | - | UNVERIFIED |
| 120 | ImageRomAnimeView | E2E COVERED | - | UNVERIFIED |
| 121 | ImageTSAEditorView | E2E COVERED | - | UNVERIFIED |
| 122 | ImageTSAAnimeView | E2E COVERED | - | UNVERIFIED |
| 123 | ImageTSAAnime2View | E2E COVERED | - | UNVERIFIED |
| 124 | ImagePalletView | E2E COVERED | - | UNVERIFIED |
| 125 | ImageMagicFEditorView | E2E COVERED | - | UNVERIFIED |
| 126 | ImageMagicCSACreatorView | E2E COVERED | - | UNVERIFIED |
| 127 | ImageMapActionAnimationView | E2E COVERED | - | UNVERIFIED |
| 128 | DecreaseColorTSAToolView | E2E COVERED | - | UNVERIFIED |
| 129 | SystemIconViewerView | E2E COVERED | YES | UNVERIFIED |
| 130 | SystemHoverColorViewerView | E2E COVERED | YES | UNVERIFIED |
| 131 | BattleBGViewerView | E2E COVERED | YES | UNVERIFIED |
| 132 | BattleTerrainViewerView | E2E COVERED | YES | UNVERIFIED |
| 133 | ChapterTitleViewerView | E2E COVERED | YES | UNVERIFIED |
| 134 | BigCGViewerView | E2E COVERED | YES | UNVERIFIED |
| 135 | OPClassDemoViewerView | E2E COVERED | YES | UNVERIFIED |
| 136 | OPClassFontViewerView | E2E COVERED | YES | UNVERIFIED |
| 137 | OPPrologueViewerView | E2E COVERED | YES | UNVERIFIED |
| 138 | GraphicsToolView | E2E COVERED | - | UNVERIFIED |
| 139 | GraphicsToolPatchMakerView | E2E COVERED | YES | UNVERIFIED |
| 140 | PaletteChangeColorsView | E2E COVERED | - | UNVERIFIED |
| 141 | PaletteClipboardView | E2E COVERED | - | UNVERIFIED |
| 142 | PaletteSwapView | E2E COVERED | - | UNVERIFIED |
| 143 | ImageBGSelectPopupView | E2E COVERED | - | UNVERIFIED |

## Audio Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 144 | SongTableView | E2E COVERED | YES | UNVERIFIED |
| 145 | SongTrackView | E2E COVERED | - | UNVERIFIED |
| 146 | SongInstrumentView | E2E COVERED | - | UNVERIFIED |
| 147 | SongInstrumentDirectSoundView | E2E COVERED | - | UNVERIFIED |
| 148 | SongInstrumentImportWaveView | E2E COVERED | - | UNVERIFIED |
| 149 | SongTrackImportMidiView | E2E COVERED | - | UNVERIFIED |
| 150 | SongExchangeView | E2E COVERED | - | UNVERIFIED |
| 151 | SoundBossBGMViewerView | E2E COVERED | YES | UNVERIFIED |
| 152 | SoundFootStepsViewerView | E2E COVERED | YES | UNVERIFIED |
| 153 | SoundRoomViewerView | E2E COVERED | YES | UNVERIFIED |
| 154 | SoundRoomFE6View | E2E COVERED | - | UNVERIFIED |
| 155 | SoundRoomCGView | E2E COVERED | - | UNVERIFIED |
| 156 | ToolBGMMuteDialogView | E2E COVERED | - | UNVERIFIED |

## Arena / Monster / Summon Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 157 | ArenaClassViewerView | E2E COVERED | YES | UNVERIFIED |
| 158 | ArenaEnemyWeaponViewerView | E2E COVERED | YES | UNVERIFIED |
| 159 | LinkArenaDenyUnitViewerView | E2E COVERED | YES | UNVERIFIED |
| 160 | MonsterProbabilityViewerView | E2E COVERED | YES | UNVERIFIED |
| 161 | MonsterItemViewerView | E2E COVERED | YES | UNVERIFIED |
| 162 | MonsterWMapProbabilityViewerView | E2E COVERED | YES | UNVERIFIED |
| 163 | SummonUnitViewerView | E2E COVERED | YES | UNVERIFIED |
| 164 | SummonsDemonKingViewerView | E2E COVERED | YES | UNVERIFIED |

## Menu / ED / World Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 165 | MenuDefinitionView | E2E COVERED | YES | UNVERIFIED |
| 166 | MenuCommandView | E2E COVERED | YES | UNVERIFIED |
| 167 | EDView | E2E COVERED | YES | UNVERIFIED |
| 168 | EDStaffRollView | E2E COVERED | YES | UNVERIFIED |
| 169 | WorldMapPointView | E2E COVERED | YES | UNVERIFIED |
| 170 | WorldMapBGMView | E2E COVERED | YES | UNVERIFIED |
| 171 | WorldMapEventPointerView | E2E COVERED | YES | UNVERIFIED |
| 172 | WorldMapPathView | E2E COVERED | - | UNVERIFIED |
| 173 | WorldMapPathEditorView | E2E COVERED | - | UNVERIFIED |
| 174 | WorldMapImageView | E2E COVERED | - | UNVERIFIED |
| 175 | WorldMapImageFE6View | E2E COVERED | - | UNVERIFIED |
| 176 | WorldMapImageFE7View | E2E COVERED | - | UNVERIFIED |
| 177 | WorldMapEventPointerFE6View | E2E COVERED | - | UNVERIFIED |
| 178 | WorldMapEventPointerFE7View | E2E COVERED | - | UNVERIFIED |

## Text / Translation Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 179 | TextViewerView | E2E COVERED | YES | UNVERIFIED |
| 180 | TextMainView | E2E COVERED | - | UNVERIFIED |
| 181 | OtherTextView | E2E COVERED | - | UNVERIFIED |
| 182 | CStringView | E2E COVERED | - | UNVERIFIED |
| 183 | FontEditorView | E2E COVERED | - | UNVERIFIED |
| 184 | FontZHView | E2E COVERED | - | UNVERIFIED |
| 185 | DevTranslateView | E2E COVERED | - | UNVERIFIED |
| 186 | ToolTranslateROMView | E2E COVERED | - | UNVERIFIED |
| 187 | TextEscapeEditorView | E2E COVERED | - | UNVERIFIED |
| 188 | TextScriptCategorySelectView | E2E COVERED | - | UNVERIFIED |
| 189 | TextDicView | E2E COVERED | - | UNVERIFIED |
| 190 | TextCharCodeView | E2E COVERED | - | UNVERIFIED |
| 191 | TextBadCharPopupView | E2E COVERED | YES | UNVERIFIED |
| 192 | TextRefAddDialogView | E2E COVERED | - | UNVERIFIED |
| 193 | TextToSpeechView | E2E COVERED | - | UNVERIFIED |

## Structural Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 194 | Command85PointerView | E2E COVERED | - | UNVERIFIED |
| 195 | FE8SpellMenuExtendsView | E2E COVERED | - | UNVERIFIED |
| 196 | StatusOptionView | E2E COVERED | - | UNVERIFIED |
| 197 | OAMSPView | E2E COVERED | - | UNVERIFIED |
| 198 | DumpStructSelectDialogView | E2E COVERED | - | UNVERIFIED |
| 199 | DumpStructSelectToTextDialogView | E2E COVERED | - | UNVERIFIED |

## Status Screen Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 200 | StatusParamView | E2E COVERED | - | UNVERIFIED |
| 201 | StatusRMenuView | E2E COVERED | - | UNVERIFIED |
| 202 | StatusUnitsMenuView | E2E COVERED | - | UNVERIFIED |
| 203 | StatusOptionOrderView | E2E COVERED | - | UNVERIFIED |

## Patch / Skill Systems

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 204 | PatchManagerView | E2E COVERED | - | UNVERIFIED |
| 205 | ToolCustomBuildView | E2E COVERED | - | UNVERIFIED |
| 206 | SkillAssignmentUnitSkillSystemView | E2E COVERED | - | UNVERIFIED |
| 207 | SkillAssignmentClassSkillSystemView | E2E COVERED | - | UNVERIFIED |
| 208 | SkillConfigSkillSystemView | E2E COVERED | - | UNVERIFIED |
| 209 | SkillAssignmentUnitCSkillSysView | E2E COVERED | - | UNVERIFIED |
| 210 | SkillAssignmentClassCSkillSysView | E2E COVERED | - | UNVERIFIED |
| 211 | SkillAssignmentUnitFE8NView | E2E COVERED | - | UNVERIFIED |
| 212 | SkillConfigFE8NSkillView | E2E COVERED | - | UNVERIFIED |
| 213 | SkillConfigFE8NVer2SkillView | E2E COVERED | - | UNVERIFIED |
| 214 | SkillConfigFE8NVer3SkillView | E2E COVERED | - | UNVERIFIED |
| 215 | SkillConfigFE8UCSkillSys09xView | E2E COVERED | - | UNVERIFIED |
| 216 | SkillSystemsEffectivenessReworkClassTypeView | E2E COVERED | - | UNVERIFIED |
| 217 | PatchFilterExView | E2E COVERED | - | UNVERIFIED |
| 218 | PatchFormUninstallDialogView | E2E COVERED | - | UNVERIFIED |

## OP Class Editors (Version-Specific)

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 219 | OPClassDemoFE7View | E2E COVERED | - | UNVERIFIED |
| 220 | OPClassDemoFE7UView | E2E COVERED | - | UNVERIFIED |
| 221 | OPClassDemoFE8UView | E2E COVERED | - | UNVERIFIED |
| 222 | OPClassFontFE8UView | E2E COVERED | - | UNVERIFIED |
| 223 | OPClassAlphaNameView | E2E COVERED | - | UNVERIFIED |
| 224 | OPClassAlphaNameFE6View | E2E COVERED | - | UNVERIFIED |

## Bit Flag Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 225 | UbyteBitFlagView | E2E COVERED | YES | UNVERIFIED |
| 226 | UshortBitFlagView | E2E COVERED | YES | UNVERIFIED |
| 227 | UwordBitFlagView | E2E COVERED | YES | UNVERIFIED |

## Error / Dialog Forms

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 228 | ErrorReportView | E2E COVERED | - | UNVERIFIED |
| 229 | ErrorPaletteMissMatchView | E2E COVERED | - | UNVERIFIED |
| 230 | ErrorPaletteShowView | E2E COVERED | - | UNVERIFIED |
| 231 | ErrorPaletteTransparentView | E2E COVERED | - | UNVERIFIED |
| 232 | ErrorTSAErrorView | E2E COVERED | - | UNVERIFIED |
| 233 | ErrorLongMessageDialogView | E2E COVERED | YES | UNVERIFIED |
| 234 | ErrorUnknownROMView | E2E COVERED | - | UNVERIFIED |
| 235 | HowDoYouLikePatchView | E2E COVERED | - | UNVERIFIED |
| 236 | HowDoYouLikePatch2View | E2E COVERED | - | UNVERIFIED |

## Tools

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 237 | ToolUndoView | E2E COVERED | - | UNVERIFIED |
| 238 | ToolFELintView | E2E COVERED | - | UNVERIFIED |
| 239 | ToolROMRebuildView | E2E COVERED | - | UNVERIFIED |
| 240 | ToolLZ77View | E2E COVERED | - | UNVERIFIED |
| 241 | ToolDiffView | E2E COVERED | - | UNVERIFIED |
| 242 | ToolUPSPatchSimpleView | E2E COVERED | - | UNVERIFIED |
| 243 | ToolUPSOpenSimpleView | E2E COVERED | - | UNVERIFIED |
| 244 | ToolFlagNameView | E2E COVERED | - | UNVERIFIED |
| 245 | ToolUseFlagView | E2E COVERED | - | UNVERIFIED |
| 246 | ToolUnitTalkGroupView | E2E COVERED | - | UNVERIFIED |
| 247 | ToolASMInsertView | E2E COVERED | - | UNVERIFIED |
| 248 | HexEditorView | E2E COVERED | - | UNVERIFIED |
| 249 | DisASMView | E2E COVERED | - | UNVERIFIED |
| 250 | LogViewerView | E2E COVERED | - | UNVERIFIED |
| 251 | GrowSimulatorView | E2E COVERED | - | UNVERIFIED |
| 252 | OptionsView | E2E COVERED | - | UNVERIFIED |
| 253 | DisASMDumpAllView | E2E COVERED | YES | UNVERIFIED |
| 254 | DisASMDumpAllArgGrepView | E2E COVERED | YES | UNVERIFIED |
| 255 | HexEditorJumpView | E2E COVERED | YES | UNVERIFIED |
| 256 | HexEditorMarkView | E2E COVERED | YES | UNVERIFIED |
| 257 | HexEditorSearchView | E2E COVERED | YES | UNVERIFIED |
| 258 | PointerToolView | E2E COVERED | - | UNVERIFIED |
| 259 | PointerToolBatchInputView | E2E COVERED | YES | UNVERIFIED |
| 260 | PointerToolCopyToView | E2E COVERED | YES | UNVERIFIED |
| 261 | PackedMemorySlotView | E2E COVERED | YES | UNVERIFIED |
| 262 | EmulatorMemoryView | E2E COVERED | - | UNVERIFIED |
| 263 | RAMRewriteToolMAPView | E2E COVERED | - | UNVERIFIED |
| 264 | ToolAnimationCreatorView | E2E COVERED | - | UNVERIFIED |
| 265 | ToolThreeMargeView | E2E COVERED | - | UNVERIFIED |
| 266 | ToolASMEditView | E2E COVERED | - | UNVERIFIED |
| 267 | ToolExportEAEventView | E2E COVERED | - | UNVERIFIED |
| 268 | ToolDecompileResultView | E2E COVERED | - | UNVERIFIED |
| 269 | ToolChangeProjectnameView | E2E COVERED | YES | UNVERIFIED |
| 270 | ToolAutomaticRecoveryROMHeaderView | E2E COVERED | YES | UNVERIFIED |
| 271 | MoveToFreeSpaceView | E2E COVERED | - | UNVERIFIED |
| 272 | ToolSubtitleOverlayView | E2E COVERED | - | UNVERIFIED |
| 273 | ToolSubtitleSettingDialogView | E2E COVERED | - | UNVERIFIED |
| 274 | EDFE6View | E2E COVERED | - | UNVERIFIED |
| 275 | EDFE7View | E2E COVERED | - | UNVERIFIED |
| 276 | EDSensekiCommentView | E2E COVERED | - | UNVERIFIED |
| 277 | EventFinalSerifFE7View | E2E COVERED | - | UNVERIFIED |
| 278 | EventMoveDataFE7View | E2E COVERED | - | UNVERIFIED |
| 279 | EventTalkGroupFE7View | E2E COVERED | - | UNVERIFIED |
| 280 | EventTemplate1View | E2E COVERED | - | UNVERIFIED |
| 281 | EventTemplate2View | E2E COVERED | - | UNVERIFIED |
| 282 | EventTemplate3View | E2E COVERED | - | UNVERIFIED |
| 283 | EventTemplate4View | E2E COVERED | - | UNVERIFIED |
| 284 | EventTemplate5View | E2E COVERED | - | UNVERIFIED |
| 285 | EventTemplate6View | E2E COVERED | - | UNVERIFIED |
| 286 | EventTemplateImplView | E2E COVERED | - | UNVERIFIED |
| 287 | ItemEffectivenessSkillSystemsReworkView | E2E COVERED | - | UNVERIFIED |
| 288 | ItemRandomChestView | E2E COVERED | - | UNVERIFIED |
| 289 | ItemStatBonusesSkillSystemsView | E2E COVERED | - | UNVERIFIED |
| 290 | ItemStatBonusesVennoView | E2E COVERED | - | UNVERIFIED |
| 291 | MenuExtendSplitMenuView | E2E COVERED | - | UNVERIFIED |
| 292 | OpenLastSelectedFileView | E2E COVERED | - | UNVERIFIED |
| 293 | ResourceView | E2E COVERED | - | UNVERIFIED |
| 294 | SongTrackAllChangeTrackView | E2E COVERED | - | UNVERIFIED |
| 295 | SongTrackChangeTrackView | E2E COVERED | - | UNVERIFIED |
| 296 | SongTrackImportSelectInstrumentView | E2E COVERED | - | UNVERIFIED |
| 297 | SongTrackImportWaveView | E2E COVERED | - | UNVERIFIED |
| 298 | ToolInitWizardView | E2E COVERED | - | UNVERIFIED |
| 299 | ToolUndoPopupDialogView | E2E COVERED | YES | UNVERIFIED |
| 300 | ToolUpdateDialogView | E2E COVERED | - | UNVERIFIED |
| 301 | VersionView | E2E COVERED | - | UNVERIFIED |
| 302 | WelcomeView | E2E COVERED | YES | UNVERIFIED |
| 303 | ToolAllWorkSupportView | E2E COVERED | - | UNVERIFIED |
| 304 | ToolProblemReportView | E2E COVERED | - | UNVERIFIED |
| 305 | WorldMapPathMoveEditorView | E2E COVERED | - | UNVERIFIED |
| 306 | MantAnimationView | E2E COVERED | - | UNVERIFIED |
| 307 | RAMRewriteToolView | E2E COVERED | - | UNVERIFIED |
| 308 | MainSimpleMenuView | E2E COVERED | - | UNVERIFIED |
| 309 | MainSimpleMenuEventErrorView | E2E COVERED | - | UNVERIFIED |
| 310 | MainSimpleMenuImageSubView | E2E COVERED | - | UNVERIFIED |
| 311 | ToolEmulatorSetupMessageView | E2E COVERED | - | UNVERIFIED |
| 312 | ToolThreeMargeCloseAlertView | E2E COVERED | - | UNVERIFIED |
| 313 | ToolClickWriteFloatControlPanelButtonView | E2E COVERED | - | UNVERIFIED |
| 314 | ToolWorkSupport_UpdateQuestionDialogView | E2E COVERED | - | UNVERIFIED |
| 315 | MainSimpleMenuEventErrorIgnoreErrorView | E2E COVERED | - | UNVERIFIED |
| 316 | ToolProblemReportSearchBackupView | E2E COVERED | - | UNVERIFIED |
| 317 | ToolProblemReportSearchSavView | E2E COVERED | - | UNVERIFIED |
| 318 | ToolWorkSupportView | E2E COVERED | - | UNVERIFIED |
| 319 | ToolWorkSupport_SelectUPSView | E2E COVERED | - | UNVERIFIED |
| 320 | ToolDiffDebugSelectView | E2E COVERED | - | UNVERIFIED |
| 321 | SMEPromoListView | E2E COVERED | - | UNVERIFIED |
| 322 | ToolRunHintMessageView | E2E COVERED | - | UNVERIFIED |
| 323 | ImageChapterTitleFE7View | E2E COVERED | YES | UNVERIFIED |
