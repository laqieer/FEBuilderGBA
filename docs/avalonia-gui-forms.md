# Avalonia GUI Forms — E2E Coverage & Alignment Tracker

This document lists all GUI forms (views) in `FEBuilderGBA.Avalonia` that are
accessible from `MainWindow` and tracks their E2E test coverage and visual
alignment status against the WinForms GUI.

**Total forms:** 323
**E2E Covered:** 323 / 323
**Visually Aligned:** 0 / 323

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
| 1 | UnitEditorView | E2E COVERED | YES | UNALIGNED |
| 2 | ItemEditorView | E2E COVERED | YES | UNALIGNED |
| 3 | ClassEditorView | E2E COVERED | YES | UNALIGNED |
| 4 | ClassFE6View | E2E COVERED | - | UNALIGNED |
| 5 | CCBranchEditorView | E2E COVERED | YES | UNALIGNED |
| 6 | MoveCostEditorView | E2E COVERED | YES | UNALIGNED |
| 7 | TerrainNameEditorView | E2E COVERED | YES | UNALIGNED |
| 8 | SupportUnitEditorView | E2E COVERED | YES | UNALIGNED |
| 9 | SupportAttributeView | E2E COVERED | YES | UNALIGNED |
| 10 | SupportTalkView | E2E COVERED | YES | UNALIGNED |
| 11 | UnitFE6View | E2E COVERED | - | UNALIGNED |
| 12 | UnitActionPointerView | E2E COVERED | - | UNALIGNED |
| 13 | UnitCustomBattleAnimeView | E2E COVERED | - | UNALIGNED |
| 14 | UnitIncreaseHeightView | E2E COVERED | - | UNALIGNED |
| 15 | UnitPaletteView | E2E COVERED | - | UNALIGNED |
| 16 | ClassOPDemoView | E2E COVERED | - | UNALIGNED |
| 17 | ClassOPFontView | E2E COVERED | - | UNALIGNED |
| 18 | ExtraUnitView | E2E COVERED | - | UNALIGNED |
| 19 | ExtraUnitFE8UView | E2E COVERED | - | UNALIGNED |
| 20 | UnitFE7View | E2E COVERED | - | UNALIGNED |
| 21 | ItemFE6View | E2E COVERED | - | UNALIGNED |
| 22 | MoveCostFE6View | E2E COVERED | - | UNALIGNED |
| 23 | SupportUnitFE6View | E2E COVERED | - | UNALIGNED |
| 24 | SupportTalkFE6View | E2E COVERED | - | UNALIGNED |
| 25 | SupportTalkFE7View | E2E COVERED | - | UNALIGNED |
| 26 | UnitsShortTextView | E2E COVERED | - | UNALIGNED |
| 27 | SomeClassListView | E2E COVERED | - | UNALIGNED |
| 28 | VennouWeaponLockView | E2E COVERED | - | UNALIGNED |

## Item Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 29 | ItemWeaponEffectViewerView | E2E COVERED | YES | UNALIGNED |
| 30 | ItemStatBonusesViewerView | E2E COVERED | YES | UNALIGNED |
| 31 | ItemEffectivenessViewerView | E2E COVERED | YES | UNALIGNED |
| 32 | ItemPromotionViewerView | E2E COVERED | YES | UNALIGNED |
| 33 | ItemShopViewerView | E2E COVERED | YES | UNALIGNED |
| 34 | ItemWeaponTriangleViewerView | E2E COVERED | YES | UNALIGNED |
| 35 | ItemUsagePointerViewerView | E2E COVERED | YES | UNALIGNED |
| 36 | ItemEffectPointerViewerView | E2E COVERED | YES | UNALIGNED |
| 37 | ItemIconViewerView | E2E COVERED | YES | UNALIGNED |

## Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 38 | MapSettingView | E2E COVERED | YES | UNALIGNED |
| 39 | MapChangeView | E2E COVERED | YES | UNALIGNED |
| 40 | MapExitPointView | E2E COVERED | YES | UNALIGNED |
| 41 | MapPointerView | E2E COVERED | YES | UNALIGNED |
| 42 | MapTileAnimationView | E2E COVERED | YES | UNALIGNED |
| 43 | MapEditorView | E2E COVERED | - | UNALIGNED |
| 44 | MapSettingFE6View | E2E COVERED | - | UNALIGNED |
| 45 | MapSettingFE7View | E2E COVERED | - | UNALIGNED |
| 46 | MapSettingFE7UView | E2E COVERED | - | UNALIGNED |
| 47 | MapSettingDifficultyView | E2E COVERED | - | UNALIGNED |
| 48 | MapStyleEditorView | E2E COVERED | - | UNALIGNED |
| 49 | MapTerrainBGLookupView | E2E COVERED | - | UNALIGNED |
| 50 | MapTerrainFloorLookupView | E2E COVERED | - | UNALIGNED |
| 51 | MapMiniMapTerrainImageView | E2E COVERED | - | UNALIGNED |
| 52 | MapTileAnimation1View | E2E COVERED | - | UNALIGNED |
| 53 | MapTileAnimation2View | E2E COVERED | - | UNALIGNED |
| 54 | MapLoadFunctionView | E2E COVERED | - | UNALIGNED |
| 55 | MapTerrainNameEngView | E2E COVERED | - | UNALIGNED |
| 56 | MapEditorAddMapChangeDialogView | E2E COVERED | YES | UNALIGNED |
| 57 | MapEditorMarSizeDialogView | E2E COVERED | YES | UNALIGNED |
| 58 | MapEditorResizeDialogView | E2E COVERED | YES | UNALIGNED |
| 59 | MapPointerNewPLISTPopupView | E2E COVERED | YES | UNALIGNED |
| 60 | MapStyleEditorAppendPopupView | E2E COVERED | YES | UNALIGNED |
| 61 | MapStyleEditorWarningOverrideView | E2E COVERED | YES | UNALIGNED |
| 62 | MapStyleEditorImportImageOptionView | E2E COVERED | YES | UNALIGNED |
| 63 | MapSettingDifficultyDialogView | E2E COVERED | YES | UNALIGNED |

## Event Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 64 | EventCondView | E2E COVERED | YES | UNALIGNED |
| 65 | EventScriptView | E2E COVERED | - | UNALIGNED |
| 66 | EventUnitView | E2E COVERED | - | UNALIGNED |
| 67 | EventUnitFE6View | E2E COVERED | - | UNALIGNED |
| 68 | EventUnitFE7View | E2E COVERED | - | UNALIGNED |
| 69 | EventUnitColorView | E2E COVERED | - | UNALIGNED |
| 70 | EventUnitItemDropView | E2E COVERED | - | UNALIGNED |
| 71 | EventUnitNewAllocView | E2E COVERED | - | UNALIGNED |
| 72 | EventBattleTalkView | E2E COVERED | - | UNALIGNED |
| 73 | EventBattleTalkFE6View | E2E COVERED | - | UNALIGNED |
| 74 | EventBattleTalkFE7View | E2E COVERED | - | UNALIGNED |
| 75 | EventBattleDataFE7View | E2E COVERED | - | UNALIGNED |
| 76 | EventHaikuView | E2E COVERED | - | UNALIGNED |
| 77 | EventHaikuFE6View | E2E COVERED | - | UNALIGNED |
| 78 | EventHaikuFE7View | E2E COVERED | - | UNALIGNED |
| 79 | EventMapChangeView | E2E COVERED | - | UNALIGNED |
| 80 | EventForceSortieView | E2E COVERED | - | UNALIGNED |
| 81 | EventForceSortieFE7View | E2E COVERED | - | UNALIGNED |
| 82 | EventFunctionPointerView | E2E COVERED | - | UNALIGNED |
| 83 | EventFunctionPointerFE7View | E2E COVERED | - | UNALIGNED |
| 84 | EventAssemblerView | E2E COVERED | - | UNALIGNED |
| 85 | ProcsScriptView | E2E COVERED | - | UNALIGNED |
| 86 | EventScriptTemplateView | E2E COVERED | - | UNALIGNED |
| 87 | EventScriptCategorySelectView | E2E COVERED | - | UNALIGNED |
| 88 | EventScriptPopupView | E2E COVERED | - | UNALIGNED |
| 89 | ProcsScriptCategorySelectView | E2E COVERED | - | UNALIGNED |
| 90 | AIScriptCategorySelectView | E2E COVERED | - | UNALIGNED |

## AI Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 91 | AIScriptView | E2E COVERED | - | UNALIGNED |
| 92 | AIASMCALLTALKView | E2E COVERED | - | UNALIGNED |
| 93 | AIASMCoordinateView | E2E COVERED | - | UNALIGNED |
| 94 | AIASMRangeView | E2E COVERED | - | UNALIGNED |
| 95 | AIMapSettingView | E2E COVERED | - | UNALIGNED |
| 96 | AIPerformItemView | E2E COVERED | - | UNALIGNED |
| 97 | AIPerformStaffView | E2E COVERED | - | UNALIGNED |
| 98 | AIStealItemView | E2E COVERED | - | UNALIGNED |
| 99 | AITargetView | E2E COVERED | - | UNALIGNED |
| 100 | AITilesView | E2E COVERED | - | UNALIGNED |
| 101 | AIUnitsView | E2E COVERED | - | UNALIGNED |
| 102 | AOERANGEView | E2E COVERED | - | UNALIGNED |

## Image Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 103 | ImageViewerView | E2E COVERED | YES | UNALIGNED |
| 104 | PortraitViewerView | E2E COVERED | YES | UNALIGNED |
| 105 | ImagePortraitView | E2E COVERED | - | UNALIGNED |
| 106 | ImagePortraitFE6View | E2E COVERED | - | UNALIGNED |
| 107 | ImagePortraitImporterView | E2E COVERED | - | UNALIGNED |
| 108 | ImageBGView | E2E COVERED | - | UNALIGNED |
| 109 | ImageBattleAnimeView | E2E COVERED | - | UNALIGNED |
| 110 | ImageBattleAnimePalletView | E2E COVERED | - | UNALIGNED |
| 111 | ImageBattleBGView | E2E COVERED | - | UNALIGNED |
| 112 | ImageBattleScreenView | E2E COVERED | - | UNALIGNED |
| 113 | ImageCGView | E2E COVERED | - | UNALIGNED |
| 114 | ImageCGFE7UView | E2E COVERED | - | UNALIGNED |
| 115 | ImageUnitPaletteView | E2E COVERED | - | UNALIGNED |
| 116 | ImageUnitWaitIconView | E2E COVERED | - | UNALIGNED |
| 117 | ImageUnitMoveIconView | E2E COVERED | - | UNALIGNED |
| 118 | ImageSystemAreaView | E2E COVERED | - | UNALIGNED |
| 119 | ImageGenericEnemyPortraitView | E2E COVERED | - | UNALIGNED |
| 120 | ImageRomAnimeView | E2E COVERED | - | UNALIGNED |
| 121 | ImageTSAEditorView | E2E COVERED | - | UNALIGNED |
| 122 | ImageTSAAnimeView | E2E COVERED | - | UNALIGNED |
| 123 | ImageTSAAnime2View | E2E COVERED | - | UNALIGNED |
| 124 | ImagePalletView | E2E COVERED | - | UNALIGNED |
| 125 | ImageMagicFEditorView | E2E COVERED | - | UNALIGNED |
| 126 | ImageMagicCSACreatorView | E2E COVERED | - | UNALIGNED |
| 127 | ImageMapActionAnimationView | E2E COVERED | - | UNALIGNED |
| 128 | DecreaseColorTSAToolView | E2E COVERED | - | UNALIGNED |
| 129 | SystemIconViewerView | E2E COVERED | YES | UNALIGNED |
| 130 | SystemHoverColorViewerView | E2E COVERED | YES | UNALIGNED |
| 131 | BattleBGViewerView | E2E COVERED | YES | UNALIGNED |
| 132 | BattleTerrainViewerView | E2E COVERED | YES | UNALIGNED |
| 133 | ChapterTitleViewerView | E2E COVERED | YES | UNALIGNED |
| 134 | BigCGViewerView | E2E COVERED | YES | UNALIGNED |
| 135 | OPClassDemoViewerView | E2E COVERED | YES | UNALIGNED |
| 136 | OPClassFontViewerView | E2E COVERED | YES | UNALIGNED |
| 137 | OPPrologueViewerView | E2E COVERED | YES | UNALIGNED |
| 138 | GraphicsToolView | E2E COVERED | - | UNALIGNED |
| 139 | GraphicsToolPatchMakerView | E2E COVERED | YES | UNALIGNED |
| 140 | PaletteChangeColorsView | E2E COVERED | - | UNALIGNED |
| 141 | PaletteClipboardView | E2E COVERED | - | UNALIGNED |
| 142 | PaletteSwapView | E2E COVERED | - | UNALIGNED |
| 143 | ImageBGSelectPopupView | E2E COVERED | - | UNALIGNED |

## Audio Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 144 | SongTableView | E2E COVERED | YES | UNALIGNED |
| 145 | SongTrackView | E2E COVERED | - | UNALIGNED |
| 146 | SongInstrumentView | E2E COVERED | - | UNALIGNED |
| 147 | SongInstrumentDirectSoundView | E2E COVERED | - | UNALIGNED |
| 148 | SongInstrumentImportWaveView | E2E COVERED | - | UNALIGNED |
| 149 | SongTrackImportMidiView | E2E COVERED | - | UNALIGNED |
| 150 | SongExchangeView | E2E COVERED | - | UNALIGNED |
| 151 | SoundBossBGMViewerView | E2E COVERED | YES | UNALIGNED |
| 152 | SoundFootStepsViewerView | E2E COVERED | YES | UNALIGNED |
| 153 | SoundRoomViewerView | E2E COVERED | YES | UNALIGNED |
| 154 | SoundRoomFE6View | E2E COVERED | - | UNALIGNED |
| 155 | SoundRoomCGView | E2E COVERED | - | UNALIGNED |
| 156 | ToolBGMMuteDialogView | E2E COVERED | - | UNALIGNED |

## Arena / Monster / Summon Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 157 | ArenaClassViewerView | E2E COVERED | YES | UNALIGNED |
| 158 | ArenaEnemyWeaponViewerView | E2E COVERED | YES | UNALIGNED |
| 159 | LinkArenaDenyUnitViewerView | E2E COVERED | YES | UNALIGNED |
| 160 | MonsterProbabilityViewerView | E2E COVERED | YES | UNALIGNED |
| 161 | MonsterItemViewerView | E2E COVERED | YES | UNALIGNED |
| 162 | MonsterWMapProbabilityViewerView | E2E COVERED | YES | UNALIGNED |
| 163 | SummonUnitViewerView | E2E COVERED | YES | UNALIGNED |
| 164 | SummonsDemonKingViewerView | E2E COVERED | YES | UNALIGNED |

## Menu / ED / World Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 165 | MenuDefinitionView | E2E COVERED | YES | UNALIGNED |
| 166 | MenuCommandView | E2E COVERED | YES | UNALIGNED |
| 167 | EDView | E2E COVERED | YES | UNALIGNED |
| 168 | EDStaffRollView | E2E COVERED | YES | UNALIGNED |
| 169 | WorldMapPointView | E2E COVERED | YES | UNALIGNED |
| 170 | WorldMapBGMView | E2E COVERED | YES | UNALIGNED |
| 171 | WorldMapEventPointerView | E2E COVERED | YES | UNALIGNED |
| 172 | WorldMapPathView | E2E COVERED | - | UNALIGNED |
| 173 | WorldMapPathEditorView | E2E COVERED | - | UNALIGNED |
| 174 | WorldMapImageView | E2E COVERED | - | UNALIGNED |
| 175 | WorldMapImageFE6View | E2E COVERED | - | UNALIGNED |
| 176 | WorldMapImageFE7View | E2E COVERED | - | UNALIGNED |
| 177 | WorldMapEventPointerFE6View | E2E COVERED | - | UNALIGNED |
| 178 | WorldMapEventPointerFE7View | E2E COVERED | - | UNALIGNED |

## Text / Translation Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 179 | TextViewerView | E2E COVERED | YES | UNALIGNED |
| 180 | TextMainView | E2E COVERED | - | UNALIGNED |
| 181 | OtherTextView | E2E COVERED | - | UNALIGNED |
| 182 | CStringView | E2E COVERED | - | UNALIGNED |
| 183 | FontEditorView | E2E COVERED | - | UNALIGNED |
| 184 | FontZHView | E2E COVERED | - | UNALIGNED |
| 185 | DevTranslateView | E2E COVERED | - | UNALIGNED |
| 186 | ToolTranslateROMView | E2E COVERED | - | UNALIGNED |
| 187 | TextEscapeEditorView | E2E COVERED | - | UNALIGNED |
| 188 | TextScriptCategorySelectView | E2E COVERED | - | UNALIGNED |
| 189 | TextDicView | E2E COVERED | - | UNALIGNED |
| 190 | TextCharCodeView | E2E COVERED | - | UNALIGNED |
| 191 | TextBadCharPopupView | E2E COVERED | YES | UNALIGNED |
| 192 | TextRefAddDialogView | E2E COVERED | - | UNALIGNED |
| 193 | TextToSpeechView | E2E COVERED | - | UNALIGNED |

## Structural Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 194 | Command85PointerView | E2E COVERED | - | UNALIGNED |
| 195 | FE8SpellMenuExtendsView | E2E COVERED | - | UNALIGNED |
| 196 | StatusOptionView | E2E COVERED | - | UNALIGNED |
| 197 | OAMSPView | E2E COVERED | - | UNALIGNED |
| 198 | DumpStructSelectDialogView | E2E COVERED | - | UNALIGNED |
| 199 | DumpStructSelectToTextDialogView | E2E COVERED | - | UNALIGNED |

## Status Screen Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 200 | StatusParamView | E2E COVERED | - | UNALIGNED |
| 201 | StatusRMenuView | E2E COVERED | - | UNALIGNED |
| 202 | StatusUnitsMenuView | E2E COVERED | - | UNALIGNED |
| 203 | StatusOptionOrderView | E2E COVERED | - | UNALIGNED |

## Patch / Skill Systems

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 204 | PatchManagerView | E2E COVERED | - | UNALIGNED |
| 205 | ToolCustomBuildView | E2E COVERED | - | UNALIGNED |
| 206 | SkillAssignmentUnitSkillSystemView | E2E COVERED | - | UNALIGNED |
| 207 | SkillAssignmentClassSkillSystemView | E2E COVERED | - | UNALIGNED |
| 208 | SkillConfigSkillSystemView | E2E COVERED | - | UNALIGNED |
| 209 | SkillAssignmentUnitCSkillSysView | E2E COVERED | - | UNALIGNED |
| 210 | SkillAssignmentClassCSkillSysView | E2E COVERED | - | UNALIGNED |
| 211 | SkillAssignmentUnitFE8NView | E2E COVERED | - | UNALIGNED |
| 212 | SkillConfigFE8NSkillView | E2E COVERED | - | UNALIGNED |
| 213 | SkillConfigFE8NVer2SkillView | E2E COVERED | - | UNALIGNED |
| 214 | SkillConfigFE8NVer3SkillView | E2E COVERED | - | UNALIGNED |
| 215 | SkillConfigFE8UCSkillSys09xView | E2E COVERED | - | UNALIGNED |
| 216 | SkillSystemsEffectivenessReworkClassTypeView | E2E COVERED | - | UNALIGNED |
| 217 | PatchFilterExView | E2E COVERED | - | UNALIGNED |
| 218 | PatchFormUninstallDialogView | E2E COVERED | - | UNALIGNED |

## OP Class Editors (Version-Specific)

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 219 | OPClassDemoFE7View | E2E COVERED | - | UNALIGNED |
| 220 | OPClassDemoFE7UView | E2E COVERED | - | UNALIGNED |
| 221 | OPClassDemoFE8UView | E2E COVERED | - | UNALIGNED |
| 222 | OPClassFontFE8UView | E2E COVERED | - | UNALIGNED |
| 223 | OPClassAlphaNameView | E2E COVERED | - | UNALIGNED |
| 224 | OPClassAlphaNameFE6View | E2E COVERED | - | UNALIGNED |

## Bit Flag Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 225 | UbyteBitFlagView | E2E COVERED | YES | UNALIGNED |
| 226 | UshortBitFlagView | E2E COVERED | YES | UNALIGNED |
| 227 | UwordBitFlagView | E2E COVERED | YES | UNALIGNED |

## Error / Dialog Forms

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 228 | ErrorReportView | E2E COVERED | - | UNALIGNED |
| 229 | ErrorPaletteMissMatchView | E2E COVERED | - | UNALIGNED |
| 230 | ErrorPaletteShowView | E2E COVERED | - | UNALIGNED |
| 231 | ErrorPaletteTransparentView | E2E COVERED | - | UNALIGNED |
| 232 | ErrorTSAErrorView | E2E COVERED | - | UNALIGNED |
| 233 | ErrorLongMessageDialogView | E2E COVERED | YES | UNALIGNED |
| 234 | ErrorUnknownROMView | E2E COVERED | - | UNALIGNED |
| 235 | HowDoYouLikePatchView | E2E COVERED | - | UNALIGNED |
| 236 | HowDoYouLikePatch2View | E2E COVERED | - | UNALIGNED |

## Tools

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 237 | ToolUndoView | E2E COVERED | - | UNALIGNED |
| 238 | ToolFELintView | E2E COVERED | - | UNALIGNED |
| 239 | ToolROMRebuildView | E2E COVERED | - | UNALIGNED |
| 240 | ToolLZ77View | E2E COVERED | - | UNALIGNED |
| 241 | ToolDiffView | E2E COVERED | - | UNALIGNED |
| 242 | ToolUPSPatchSimpleView | E2E COVERED | - | UNALIGNED |
| 243 | ToolUPSOpenSimpleView | E2E COVERED | - | UNALIGNED |
| 244 | ToolFlagNameView | E2E COVERED | - | UNALIGNED |
| 245 | ToolUseFlagView | E2E COVERED | - | UNALIGNED |
| 246 | ToolUnitTalkGroupView | E2E COVERED | - | UNALIGNED |
| 247 | ToolASMInsertView | E2E COVERED | - | UNALIGNED |
| 248 | HexEditorView | E2E COVERED | - | UNALIGNED |
| 249 | DisASMView | E2E COVERED | - | UNALIGNED |
| 250 | LogViewerView | E2E COVERED | - | UNALIGNED |
| 251 | GrowSimulatorView | E2E COVERED | - | UNALIGNED |
| 252 | OptionsView | E2E COVERED | - | UNALIGNED |
| 253 | DisASMDumpAllView | E2E COVERED | YES | UNALIGNED |
| 254 | DisASMDumpAllArgGrepView | E2E COVERED | YES | UNALIGNED |
| 255 | HexEditorJumpView | E2E COVERED | YES | UNALIGNED |
| 256 | HexEditorMarkView | E2E COVERED | YES | UNALIGNED |
| 257 | HexEditorSearchView | E2E COVERED | YES | UNALIGNED |
| 258 | PointerToolView | E2E COVERED | - | UNALIGNED |
| 259 | PointerToolBatchInputView | E2E COVERED | YES | UNALIGNED |
| 260 | PointerToolCopyToView | E2E COVERED | YES | UNALIGNED |
| 261 | PackedMemorySlotView | E2E COVERED | YES | UNALIGNED |
| 262 | EmulatorMemoryView | E2E COVERED | - | UNALIGNED |
| 263 | RAMRewriteToolMAPView | E2E COVERED | - | UNALIGNED |
| 264 | ToolAnimationCreatorView | E2E COVERED | - | UNALIGNED |
| 265 | ToolThreeMargeView | E2E COVERED | - | UNALIGNED |
| 266 | ToolASMEditView | E2E COVERED | - | UNALIGNED |
| 267 | ToolExportEAEventView | E2E COVERED | - | UNALIGNED |
| 268 | ToolDecompileResultView | E2E COVERED | - | UNALIGNED |
| 269 | ToolChangeProjectnameView | E2E COVERED | YES | UNALIGNED |
| 270 | ToolAutomaticRecoveryROMHeaderView | E2E COVERED | YES | UNALIGNED |
| 271 | MoveToFreeSpaceView | E2E COVERED | - | UNALIGNED |
| 272 | ToolSubtitleOverlayView | E2E COVERED | - | UNALIGNED |
| 273 | ToolSubtitleSettingDialogView | E2E COVERED | - | UNALIGNED |
| 274 | EDFE6View | E2E COVERED | - | UNALIGNED |
| 275 | EDFE7View | E2E COVERED | - | UNALIGNED |
| 276 | EDSensekiCommentView | E2E COVERED | - | UNALIGNED |
| 277 | EventFinalSerifFE7View | E2E COVERED | - | UNALIGNED |
| 278 | EventMoveDataFE7View | E2E COVERED | - | UNALIGNED |
| 279 | EventTalkGroupFE7View | E2E COVERED | - | UNALIGNED |
| 280 | EventTemplate1View | E2E COVERED | - | UNALIGNED |
| 281 | EventTemplate2View | E2E COVERED | - | UNALIGNED |
| 282 | EventTemplate3View | E2E COVERED | - | UNALIGNED |
| 283 | EventTemplate4View | E2E COVERED | - | UNALIGNED |
| 284 | EventTemplate5View | E2E COVERED | - | UNALIGNED |
| 285 | EventTemplate6View | E2E COVERED | - | UNALIGNED |
| 286 | EventTemplateImplView | E2E COVERED | - | UNALIGNED |
| 287 | ItemEffectivenessSkillSystemsReworkView | E2E COVERED | - | UNALIGNED |
| 288 | ItemRandomChestView | E2E COVERED | - | UNALIGNED |
| 289 | ItemStatBonusesSkillSystemsView | E2E COVERED | - | UNALIGNED |
| 290 | ItemStatBonusesVennoView | E2E COVERED | - | UNALIGNED |
| 291 | MenuExtendSplitMenuView | E2E COVERED | - | UNALIGNED |
| 292 | OpenLastSelectedFileView | E2E COVERED | - | UNALIGNED |
| 293 | ResourceView | E2E COVERED | - | UNALIGNED |
| 294 | SongTrackAllChangeTrackView | E2E COVERED | - | UNALIGNED |
| 295 | SongTrackChangeTrackView | E2E COVERED | - | UNALIGNED |
| 296 | SongTrackImportSelectInstrumentView | E2E COVERED | - | UNALIGNED |
| 297 | SongTrackImportWaveView | E2E COVERED | - | UNALIGNED |
| 298 | ToolInitWizardView | E2E COVERED | - | UNALIGNED |
| 299 | ToolUndoPopupDialogView | E2E COVERED | YES | UNALIGNED |
| 300 | ToolUpdateDialogView | E2E COVERED | - | UNALIGNED |
| 301 | VersionView | E2E COVERED | - | UNALIGNED |
| 302 | WelcomeView | E2E COVERED | YES | UNALIGNED |
| 303 | ToolAllWorkSupportView | E2E COVERED | - | UNALIGNED |
| 304 | ToolProblemReportView | E2E COVERED | - | UNALIGNED |
| 305 | WorldMapPathMoveEditorView | E2E COVERED | - | UNALIGNED |
| 306 | MantAnimationView | E2E COVERED | - | UNALIGNED |
| 307 | RAMRewriteToolView | E2E COVERED | - | UNALIGNED |
| 308 | MainSimpleMenuView | E2E COVERED | - | UNALIGNED |
| 309 | MainSimpleMenuEventErrorView | E2E COVERED | - | UNALIGNED |
| 310 | MainSimpleMenuImageSubView | E2E COVERED | - | UNALIGNED |
| 311 | ToolEmulatorSetupMessageView | E2E COVERED | - | UNALIGNED |
| 312 | ToolThreeMargeCloseAlertView | E2E COVERED | - | UNALIGNED |
| 313 | ToolClickWriteFloatControlPanelButtonView | E2E COVERED | - | UNALIGNED |
| 314 | ToolWorkSupport_UpdateQuestionDialogView | E2E COVERED | - | UNALIGNED |
| 315 | MainSimpleMenuEventErrorIgnoreErrorView | E2E COVERED | - | UNALIGNED |
| 316 | ToolProblemReportSearchBackupView | E2E COVERED | - | UNALIGNED |
| 317 | ToolProblemReportSearchSavView | E2E COVERED | - | UNALIGNED |
| 318 | ToolWorkSupportView | E2E COVERED | - | UNALIGNED |
| 319 | ToolWorkSupport_SelectUPSView | E2E COVERED | - | UNALIGNED |
| 320 | ToolDiffDebugSelectView | E2E COVERED | - | UNALIGNED |
| 321 | SMEPromoListView | E2E COVERED | - | UNALIGNED |
| 322 | ToolRunHintMessageView | E2E COVERED | - | UNALIGNED |
| 323 | ImageChapterTitleFE7View | E2E COVERED | YES | UNALIGNED |
