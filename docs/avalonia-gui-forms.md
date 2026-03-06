# Avalonia GUI Forms — E2E Coverage & Alignment Tracker

This document lists all GUI forms (views) in `FEBuilderGBA.Avalonia` that are
accessible from `MainWindow` and tracks their E2E test coverage and visual
alignment status against the WinForms GUI.

**Total forms:** 323
**E2E Covered:** 323 / 323
**Visually Aligned:** 78 / 323

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
| 1 | UnitEditorView | E2E COVERED | YES | - |
| 2 | ItemEditorView | E2E COVERED | YES | - |
| 3 | ClassEditorView | E2E COVERED | YES | - |
| 4 | ClassFE6View | E2E COVERED | - | - |
| 5 | CCBranchEditorView | E2E COVERED | YES | - |
| 6 | MoveCostEditorView | E2E COVERED | YES | - |
| 7 | TerrainNameEditorView | E2E COVERED | YES | - |
| 8 | SupportUnitEditorView | E2E COVERED | YES | - |
| 9 | SupportAttributeView | E2E COVERED | YES | - |
| 10 | SupportTalkView | E2E COVERED | YES | - |
| 11 | UnitFE6View | E2E COVERED | - | - |
| 12 | UnitActionPointerView | E2E COVERED | - | - |
| 13 | UnitCustomBattleAnimeView | E2E COVERED | - | - |
| 14 | UnitIncreaseHeightView | E2E COVERED | - | - |
| 15 | UnitPaletteView | E2E COVERED | - | - |
| 16 | ClassOPDemoView | E2E COVERED | - | - |
| 17 | ClassOPFontView | E2E COVERED | - | - |
| 18 | ExtraUnitView | E2E COVERED | - | - |
| 19 | ExtraUnitFE8UView | E2E COVERED | - | - |
| 20 | UnitFE7View | E2E COVERED | - | - |
| 21 | ItemFE6View | E2E COVERED | - | - |
| 22 | MoveCostFE6View | E2E COVERED | - | - |
| 23 | SupportUnitFE6View | E2E COVERED | - | - |
| 24 | SupportTalkFE6View | E2E COVERED | - | - |
| 25 | SupportTalkFE7View | E2E COVERED | - | - |
| 26 | UnitsShortTextView | E2E COVERED | - | - |
| 27 | SomeClassListView | E2E COVERED | - | - |
| 28 | VennouWeaponLockView | E2E COVERED | - | - |

## Item Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 29 | ItemWeaponEffectViewerView | E2E COVERED | YES | ALIGNED |
| 30 | ItemStatBonusesViewerView | E2E COVERED | YES | ALIGNED |
| 31 | ItemEffectivenessViewerView | E2E COVERED | YES | ALIGNED |
| 32 | ItemPromotionViewerView | E2E COVERED | YES | ALIGNED |
| 33 | ItemShopViewerView | E2E COVERED | YES | ALIGNED |
| 34 | ItemWeaponTriangleViewerView | E2E COVERED | YES | ALIGNED |
| 35 | ItemUsagePointerViewerView | E2E COVERED | YES | ALIGNED |
| 36 | ItemEffectPointerViewerView | E2E COVERED | YES | ALIGNED |
| 37 | ItemIconViewerView | E2E COVERED | YES | ALIGNED |

## Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 38 | MapSettingView | E2E COVERED | YES | - |
| 39 | MapChangeView | E2E COVERED | YES | - |
| 40 | MapExitPointView | E2E COVERED | YES | - |
| 41 | MapPointerView | E2E COVERED | YES | - |
| 42 | MapTileAnimationView | E2E COVERED | YES | - |
| 43 | MapEditorView | E2E COVERED | - | - |
| 44 | MapSettingFE6View | E2E COVERED | - | - |
| 45 | MapSettingFE7View | E2E COVERED | - | - |
| 46 | MapSettingFE7UView | E2E COVERED | - | - |
| 47 | MapSettingDifficultyView | E2E COVERED | - | - |
| 48 | MapStyleEditorView | E2E COVERED | - | - |
| 49 | MapTerrainBGLookupView | E2E COVERED | - | - |
| 50 | MapTerrainFloorLookupView | E2E COVERED | - | - |
| 51 | MapMiniMapTerrainImageView | E2E COVERED | - | - |
| 52 | MapTileAnimation1View | E2E COVERED | - | - |
| 53 | MapTileAnimation2View | E2E COVERED | - | - |
| 54 | MapLoadFunctionView | E2E COVERED | - | - |
| 55 | MapTerrainNameEngView | E2E COVERED | - | - |
| 56 | MapEditorAddMapChangeDialogView | E2E COVERED | YES | ALIGNED |
| 57 | MapEditorMarSizeDialogView | E2E COVERED | YES | ALIGNED |
| 58 | MapEditorResizeDialogView | E2E COVERED | YES | ALIGNED |
| 59 | MapPointerNewPLISTPopupView | E2E COVERED | YES | ALIGNED |
| 60 | MapStyleEditorAppendPopupView | E2E COVERED | YES | - |
| 61 | MapStyleEditorWarningOverrideView | E2E COVERED | YES | ALIGNED |
| 62 | MapStyleEditorImportImageOptionView | E2E COVERED | YES | ALIGNED |
| 63 | MapSettingDifficultyDialogView | E2E COVERED | YES | ALIGNED |

## Event Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 64 | EventCondView | E2E COVERED | YES | - |
| 65 | EventScriptView | E2E COVERED | - | - |
| 66 | EventUnitView | E2E COVERED | - | - |
| 67 | EventUnitFE6View | E2E COVERED | - | - |
| 68 | EventUnitFE7View | E2E COVERED | - | - |
| 69 | EventUnitColorView | E2E COVERED | - | - |
| 70 | EventUnitItemDropView | E2E COVERED | - | - |
| 71 | EventUnitNewAllocView | E2E COVERED | - | - |
| 72 | EventBattleTalkView | E2E COVERED | - | - |
| 73 | EventBattleTalkFE6View | E2E COVERED | - | - |
| 74 | EventBattleTalkFE7View | E2E COVERED | - | - |
| 75 | EventBattleDataFE7View | E2E COVERED | - | - |
| 76 | EventHaikuView | E2E COVERED | - | - |
| 77 | EventHaikuFE6View | E2E COVERED | - | - |
| 78 | EventHaikuFE7View | E2E COVERED | - | - |
| 79 | EventMapChangeView | E2E COVERED | - | - |
| 80 | EventForceSortieView | E2E COVERED | - | - |
| 81 | EventForceSortieFE7View | E2E COVERED | - | - |
| 82 | EventFunctionPointerView | E2E COVERED | - | - |
| 83 | EventFunctionPointerFE7View | E2E COVERED | - | - |
| 84 | EventAssemblerView | E2E COVERED | - | - |
| 85 | ProcsScriptView | E2E COVERED | - | - |
| 86 | EventScriptTemplateView | E2E COVERED | - | - |
| 87 | EventScriptCategorySelectView | E2E COVERED | - | - |
| 88 | EventScriptPopupView | E2E COVERED | - | - |
| 89 | ProcsScriptCategorySelectView | E2E COVERED | - | - |
| 90 | AIScriptCategorySelectView | E2E COVERED | - | - |

## AI Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 91 | AIScriptView | E2E COVERED | - | - |
| 92 | AIASMCALLTALKView | E2E COVERED | - | - |
| 93 | AIASMCoordinateView | E2E COVERED | - | - |
| 94 | AIASMRangeView | E2E COVERED | - | - |
| 95 | AIMapSettingView | E2E COVERED | - | - |
| 96 | AIPerformItemView | E2E COVERED | - | - |
| 97 | AIPerformStaffView | E2E COVERED | - | - |
| 98 | AIStealItemView | E2E COVERED | - | - |
| 99 | AITargetView | E2E COVERED | - | - |
| 100 | AITilesView | E2E COVERED | - | - |
| 101 | AIUnitsView | E2E COVERED | - | - |
| 102 | AOERANGEView | E2E COVERED | - | - |

## Image Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 103 | ImageViewerView | E2E COVERED | YES | - |
| 104 | PortraitViewerView | E2E COVERED | YES | ALIGNED |
| 105 | ImagePortraitView | E2E COVERED | - | - |
| 106 | ImagePortraitFE6View | E2E COVERED | - | - |
| 107 | ImagePortraitImporterView | E2E COVERED | - | - |
| 108 | ImageBGView | E2E COVERED | - | - |
| 109 | ImageBattleAnimeView | E2E COVERED | - | - |
| 110 | ImageBattleAnimePalletView | E2E COVERED | - | - |
| 111 | ImageBattleBGView | E2E COVERED | - | - |
| 112 | ImageBattleScreenView | E2E COVERED | - | - |
| 113 | ImageCGView | E2E COVERED | - | - |
| 114 | ImageCGFE7UView | E2E COVERED | - | - |
| 115 | ImageUnitPaletteView | E2E COVERED | - | - |
| 116 | ImageUnitWaitIconView | E2E COVERED | - | - |
| 117 | ImageUnitMoveIconView | E2E COVERED | - | - |
| 118 | ImageSystemAreaView | E2E COVERED | - | - |
| 119 | ImageGenericEnemyPortraitView | E2E COVERED | - | - |
| 120 | ImageRomAnimeView | E2E COVERED | - | - |
| 121 | ImageTSAEditorView | E2E COVERED | - | - |
| 122 | ImageTSAAnimeView | E2E COVERED | - | - |
| 123 | ImageTSAAnime2View | E2E COVERED | - | - |
| 124 | ImagePalletView | E2E COVERED | - | - |
| 125 | ImageMagicFEditorView | E2E COVERED | - | - |
| 126 | ImageMagicCSACreatorView | E2E COVERED | - | - |
| 127 | ImageMapActionAnimationView | E2E COVERED | - | - |
| 128 | DecreaseColorTSAToolView | E2E COVERED | - | - |
| 129 | SystemIconViewerView | E2E COVERED | YES | ALIGNED |
| 130 | SystemHoverColorViewerView | E2E COVERED | YES | ALIGNED |
| 131 | BattleBGViewerView | E2E COVERED | YES | ALIGNED |
| 132 | BattleTerrainViewerView | E2E COVERED | YES | ALIGNED |
| 133 | ChapterTitleViewerView | E2E COVERED | YES | ALIGNED |
| 134 | BigCGViewerView | E2E COVERED | YES | ALIGNED |
| 135 | OPClassDemoViewerView | E2E COVERED | YES | ALIGNED |
| 136 | OPClassFontViewerView | E2E COVERED | YES | ALIGNED |
| 137 | OPPrologueViewerView | E2E COVERED | YES | ALIGNED |
| 138 | GraphicsToolView | E2E COVERED | - | - |
| 139 | GraphicsToolPatchMakerView | E2E COVERED | YES | ALIGNED |
| 140 | PaletteChangeColorsView | E2E COVERED | - | - |
| 141 | PaletteClipboardView | E2E COVERED | - | - |
| 142 | PaletteSwapView | E2E COVERED | - | - |
| 143 | ImageBGSelectPopupView | E2E COVERED | - | - |

## Audio Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 144 | SongTableView | E2E COVERED | YES | ALIGNED |
| 145 | SongTrackView | E2E COVERED | - | - |
| 146 | SongInstrumentView | E2E COVERED | - | - |
| 147 | SongInstrumentDirectSoundView | E2E COVERED | - | - |
| 148 | SongInstrumentImportWaveView | E2E COVERED | - | - |
| 149 | SongTrackImportMidiView | E2E COVERED | - | - |
| 150 | SongExchangeView | E2E COVERED | - | - |
| 151 | SoundBossBGMViewerView | E2E COVERED | YES | ALIGNED |
| 152 | SoundFootStepsViewerView | E2E COVERED | YES | ALIGNED |
| 153 | SoundRoomViewerView | E2E COVERED | YES | ALIGNED |
| 154 | SoundRoomFE6View | E2E COVERED | - | - |
| 155 | SoundRoomCGView | E2E COVERED | - | - |
| 156 | ToolBGMMuteDialogView | E2E COVERED | - | ALIGNED |

## Arena / Monster / Summon Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 157 | ArenaClassViewerView | E2E COVERED | YES | ALIGNED |
| 158 | ArenaEnemyWeaponViewerView | E2E COVERED | YES | ALIGNED |
| 159 | LinkArenaDenyUnitViewerView | E2E COVERED | YES | ALIGNED |
| 160 | MonsterProbabilityViewerView | E2E COVERED | YES | ALIGNED |
| 161 | MonsterItemViewerView | E2E COVERED | YES | ALIGNED |
| 162 | MonsterWMapProbabilityViewerView | E2E COVERED | YES | ALIGNED |
| 163 | SummonUnitViewerView | E2E COVERED | YES | ALIGNED |
| 164 | SummonsDemonKingViewerView | E2E COVERED | YES | ALIGNED |

## Menu / ED / World Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 165 | MenuDefinitionView | E2E COVERED | YES | ALIGNED |
| 166 | MenuCommandView | E2E COVERED | YES | ALIGNED |
| 167 | EDView | E2E COVERED | YES | ALIGNED |
| 168 | EDStaffRollView | E2E COVERED | YES | ALIGNED |
| 169 | WorldMapPointView | E2E COVERED | YES | ALIGNED |
| 170 | WorldMapBGMView | E2E COVERED | YES | ALIGNED |
| 171 | WorldMapEventPointerView | E2E COVERED | YES | ALIGNED |
| 172 | WorldMapPathView | E2E COVERED | - | - |
| 173 | WorldMapPathEditorView | E2E COVERED | - | - |
| 174 | WorldMapImageView | E2E COVERED | - | - |
| 175 | WorldMapImageFE6View | E2E COVERED | - | - |
| 176 | WorldMapImageFE7View | E2E COVERED | - | - |
| 177 | WorldMapEventPointerFE6View | E2E COVERED | - | - |
| 178 | WorldMapEventPointerFE7View | E2E COVERED | - | - |

## Text / Translation Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 179 | TextViewerView | E2E COVERED | YES | ALIGNED |
| 180 | TextMainView | E2E COVERED | - | - |
| 181 | OtherTextView | E2E COVERED | - | - |
| 182 | CStringView | E2E COVERED | - | - |
| 183 | FontEditorView | E2E COVERED | - | - |
| 184 | FontZHView | E2E COVERED | - | - |
| 185 | DevTranslateView | E2E COVERED | - | - |
| 186 | ToolTranslateROMView | E2E COVERED | - | - |
| 187 | TextEscapeEditorView | E2E COVERED | - | - |
| 188 | TextScriptCategorySelectView | E2E COVERED | - | - |
| 189 | TextDicView | E2E COVERED | - | - |
| 190 | TextCharCodeView | E2E COVERED | - | - |
| 191 | TextBadCharPopupView | E2E COVERED | YES | ALIGNED |
| 192 | TextRefAddDialogView | E2E COVERED | - | - |
| 193 | TextToSpeechView | E2E COVERED | - | - |

## Structural Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 194 | Command85PointerView | E2E COVERED | - | - |
| 195 | FE8SpellMenuExtendsView | E2E COVERED | - | - |
| 196 | StatusOptionView | E2E COVERED | - | - |
| 197 | OAMSPView | E2E COVERED | - | - |
| 198 | DumpStructSelectDialogView | E2E COVERED | - | - |
| 199 | DumpStructSelectToTextDialogView | E2E COVERED | - | - |

## Status Screen Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 200 | StatusParamView | E2E COVERED | - | - |
| 201 | StatusRMenuView | E2E COVERED | - | - |
| 202 | StatusUnitsMenuView | E2E COVERED | - | - |
| 203 | StatusOptionOrderView | E2E COVERED | - | - |

## Patch / Skill Systems

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 204 | PatchManagerView | E2E COVERED | - | - |
| 205 | ToolCustomBuildView | E2E COVERED | - | - |
| 206 | SkillAssignmentUnitSkillSystemView | E2E COVERED | - | - |
| 207 | SkillAssignmentClassSkillSystemView | E2E COVERED | - | - |
| 208 | SkillConfigSkillSystemView | E2E COVERED | - | - |
| 209 | SkillAssignmentUnitCSkillSysView | E2E COVERED | - | - |
| 210 | SkillAssignmentClassCSkillSysView | E2E COVERED | - | - |
| 211 | SkillAssignmentUnitFE8NView | E2E COVERED | - | - |
| 212 | SkillConfigFE8NSkillView | E2E COVERED | - | - |
| 213 | SkillConfigFE8NVer2SkillView | E2E COVERED | - | - |
| 214 | SkillConfigFE8NVer3SkillView | E2E COVERED | - | - |
| 215 | SkillConfigFE8UCSkillSys09xView | E2E COVERED | - | - |
| 216 | SkillSystemsEffectivenessReworkClassTypeView | E2E COVERED | - | - |
| 217 | PatchFilterExView | E2E COVERED | - | - |
| 218 | PatchFormUninstallDialogView | E2E COVERED | - | ALIGNED |

## OP Class Editors (Version-Specific)

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 219 | OPClassDemoFE7View | E2E COVERED | - | - |
| 220 | OPClassDemoFE7UView | E2E COVERED | - | - |
| 221 | OPClassDemoFE8UView | E2E COVERED | - | - |
| 222 | OPClassFontFE8UView | E2E COVERED | - | - |
| 223 | OPClassAlphaNameView | E2E COVERED | - | - |
| 224 | OPClassAlphaNameFE6View | E2E COVERED | - | - |

## Bit Flag Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 225 | UbyteBitFlagView | E2E COVERED | YES | ALIGNED |
| 226 | UshortBitFlagView | E2E COVERED | YES | ALIGNED |
| 227 | UwordBitFlagView | E2E COVERED | YES | ALIGNED |

## Error / Dialog Forms

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 228 | ErrorReportView | E2E COVERED | - | - |
| 229 | ErrorPaletteMissMatchView | E2E COVERED | - | - |
| 230 | ErrorPaletteShowView | E2E COVERED | - | - |
| 231 | ErrorPaletteTransparentView | E2E COVERED | - | - |
| 232 | ErrorTSAErrorView | E2E COVERED | - | - |
| 233 | ErrorLongMessageDialogView | E2E COVERED | YES | ALIGNED |
| 234 | ErrorUnknownROMView | E2E COVERED | - | ALIGNED |
| 235 | HowDoYouLikePatchView | E2E COVERED | - | - |
| 236 | HowDoYouLikePatch2View | E2E COVERED | - | - |

## Tools

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 237 | ToolUndoView | E2E COVERED | - | - |
| 238 | ToolFELintView | E2E COVERED | - | - |
| 239 | ToolROMRebuildView | E2E COVERED | - | - |
| 240 | ToolLZ77View | E2E COVERED | - | - |
| 241 | ToolDiffView | E2E COVERED | - | - |
| 242 | ToolUPSPatchSimpleView | E2E COVERED | - | - |
| 243 | ToolUPSOpenSimpleView | E2E COVERED | - | - |
| 244 | ToolFlagNameView | E2E COVERED | - | - |
| 245 | ToolUseFlagView | E2E COVERED | - | - |
| 246 | ToolUnitTalkGroupView | E2E COVERED | - | - |
| 247 | ToolASMInsertView | E2E COVERED | - | - |
| 248 | HexEditorView | E2E COVERED | - | - |
| 249 | DisASMView | E2E COVERED | - | - |
| 250 | LogViewerView | E2E COVERED | - | - |
| 251 | GrowSimulatorView | E2E COVERED | - | - |
| 252 | OptionsView | E2E COVERED | - | - |
| 253 | DisASMDumpAllView | E2E COVERED | YES | - |
| 254 | DisASMDumpAllArgGrepView | E2E COVERED | YES | - |
| 255 | HexEditorJumpView | E2E COVERED | YES | ALIGNED |
| 256 | HexEditorMarkView | E2E COVERED | YES | ALIGNED |
| 257 | HexEditorSearchView | E2E COVERED | YES | ALIGNED |
| 258 | PointerToolView | E2E COVERED | - | - |
| 259 | PointerToolBatchInputView | E2E COVERED | YES | ALIGNED |
| 260 | PointerToolCopyToView | E2E COVERED | YES | ALIGNED |
| 261 | PackedMemorySlotView | E2E COVERED | YES | ALIGNED |
| 262 | EmulatorMemoryView | E2E COVERED | - | - |
| 263 | RAMRewriteToolMAPView | E2E COVERED | - | - |
| 264 | ToolAnimationCreatorView | E2E COVERED | - | - |
| 265 | ToolThreeMargeView | E2E COVERED | - | - |
| 266 | ToolASMEditView | E2E COVERED | - | - |
| 267 | ToolExportEAEventView | E2E COVERED | - | - |
| 268 | ToolDecompileResultView | E2E COVERED | - | - |
| 269 | ToolChangeProjectnameView | E2E COVERED | YES | ALIGNED |
| 270 | ToolAutomaticRecoveryROMHeaderView | E2E COVERED | YES | ALIGNED |
| 271 | MoveToFreeSpaceView | E2E COVERED | - | - |
| 272 | ToolSubtitleOverlayView | E2E COVERED | - | - |
| 273 | ToolSubtitleSettingDialogView | E2E COVERED | - | - |
| 274 | EDFE6View | E2E COVERED | - | - |
| 275 | EDFE7View | E2E COVERED | - | - |
| 276 | EDSensekiCommentView | E2E COVERED | - | - |
| 277 | EventFinalSerifFE7View | E2E COVERED | - | - |
| 278 | EventMoveDataFE7View | E2E COVERED | - | - |
| 279 | EventTalkGroupFE7View | E2E COVERED | - | - |
| 280 | EventTemplate1View | E2E COVERED | - | - |
| 281 | EventTemplate2View | E2E COVERED | - | - |
| 282 | EventTemplate3View | E2E COVERED | - | - |
| 283 | EventTemplate4View | E2E COVERED | - | - |
| 284 | EventTemplate5View | E2E COVERED | - | - |
| 285 | EventTemplate6View | E2E COVERED | - | - |
| 286 | EventTemplateImplView | E2E COVERED | - | ALIGNED |
| 287 | ItemEffectivenessSkillSystemsReworkView | E2E COVERED | - | - |
| 288 | ItemRandomChestView | E2E COVERED | - | - |
| 289 | ItemStatBonusesSkillSystemsView | E2E COVERED | - | - |
| 290 | ItemStatBonusesVennoView | E2E COVERED | - | - |
| 291 | MenuExtendSplitMenuView | E2E COVERED | - | - |
| 292 | OpenLastSelectedFileView | E2E COVERED | - | - |
| 293 | ResourceView | E2E COVERED | - | - |
| 294 | SongTrackAllChangeTrackView | E2E COVERED | - | - |
| 295 | SongTrackChangeTrackView | E2E COVERED | - | - |
| 296 | SongTrackImportSelectInstrumentView | E2E COVERED | - | - |
| 297 | SongTrackImportWaveView | E2E COVERED | - | - |
| 298 | ToolInitWizardView | E2E COVERED | - | - |
| 299 | ToolUndoPopupDialogView | E2E COVERED | YES | ALIGNED |
| 300 | ToolUpdateDialogView | E2E COVERED | - | - |
| 301 | VersionView | E2E COVERED | - | ALIGNED |
| 302 | WelcomeView | E2E COVERED | YES | ALIGNED |
| 303 | ToolAllWorkSupportView | E2E COVERED | - | - |
| 304 | ToolProblemReportView | E2E COVERED | - | - |
| 305 | WorldMapPathMoveEditorView | E2E COVERED | - | - |
| 306 | MantAnimationView | E2E COVERED | - | - |
| 307 | RAMRewriteToolView | E2E COVERED | - | - |
| 308 | MainSimpleMenuView | E2E COVERED | - | - |
| 309 | MainSimpleMenuEventErrorView | E2E COVERED | - | - |
| 310 | MainSimpleMenuImageSubView | E2E COVERED | - | - |
| 311 | ToolEmulatorSetupMessageView | E2E COVERED | - | ALIGNED |
| 312 | ToolThreeMargeCloseAlertView | E2E COVERED | - | ALIGNED |
| 313 | ToolClickWriteFloatControlPanelButtonView | E2E COVERED | - | ALIGNED |
| 314 | ToolWorkSupport_UpdateQuestionDialogView | E2E COVERED | - | ALIGNED |
| 315 | MainSimpleMenuEventErrorIgnoreErrorView | E2E COVERED | - | ALIGNED |
| 316 | ToolProblemReportSearchBackupView | E2E COVERED | - | ALIGNED |
| 317 | ToolProblemReportSearchSavView | E2E COVERED | - | ALIGNED |
| 318 | ToolWorkSupportView | E2E COVERED | - | - |
| 319 | ToolWorkSupport_SelectUPSView | E2E COVERED | - | ALIGNED |
| 320 | ToolDiffDebugSelectView | E2E COVERED | - | ALIGNED |
| 321 | SMEPromoListView | E2E COVERED | - | ALIGNED |
| 322 | ToolRunHintMessageView | E2E COVERED | - | ALIGNED |
| 323 | ImageChapterTitleFE7View | E2E COVERED | YES | - |
