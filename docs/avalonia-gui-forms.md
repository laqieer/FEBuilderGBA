# Avalonia GUI Forms — E2E Coverage & Alignment Tracker

This document lists all GUI forms (views) in `FEBuilderGBA.Avalonia` that are
accessible from `MainWindow` and tracks their E2E test coverage and visual
alignment status against the WinForms GUI.

**Total forms:** 324
**E2E Covered:** 324 / 324
**Field Aligned:** 324 / 324 (verified via AvaloniaFieldCompletenessTests — 0 gaps across 80 ROM-field forms)

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

### Per-editor user guides

Standalone usage docs for specific editors that need extra explanation:

- `VennouWeaponLockView` (#28 in *Data Editors*) — [weapon-lock-vennou-editor.md](weapon-lock-vennou-editor.md)

---

## Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 1 | UnitEditorView | E2E COVERED | YES | ALIGNED |
| 2 | ItemEditorView | E2E COVERED | YES | ALIGNED |
| 3 | ClassEditorView | E2E COVERED | YES | ALIGNED |
| 4 | ClassFE6View | E2E COVERED | - | ALIGNED |
| 5 | CCBranchEditorView | E2E COVERED | YES | ALIGNED |
| 6 | MoveCostEditorView | E2E COVERED | YES | ALIGNED |
| 7 | TerrainNameEditorView | E2E COVERED | YES | ALIGNED |
| 8 | SupportUnitEditorView | E2E COVERED | YES | ALIGNED |
| 9 | SupportAttributeView | E2E COVERED | YES | ALIGNED |
| 10 | SupportTalkView | E2E COVERED | YES | ALIGNED |
| 11 | UnitFE6View | E2E COVERED | - | ALIGNED |
| 12 | UnitActionPointerView | E2E COVERED | - | ALIGNED |
| 13 | UnitCustomBattleAnimeView | E2E COVERED | - | ALIGNED |
| 14 | UnitIncreaseHeightView | E2E COVERED | - | ALIGNED |
| 15 | UnitPaletteView | E2E COVERED | - | ALIGNED |
| 16 | ClassOPDemoView | E2E COVERED | - | ALIGNED |
| 17 | ClassOPFontView | E2E COVERED | - | ALIGNED |
| 18 | ExtraUnitView | E2E COVERED | - | ALIGNED |
| 19 | ExtraUnitFE8UView | E2E COVERED | - | ALIGNED |
| 20 | UnitFE7View | E2E COVERED | - | ALIGNED |
| 21 | ItemFE6View | E2E COVERED | - | ALIGNED |
| 22 | MoveCostFE6View | E2E COVERED | - | ALIGNED |
| 23 | SupportUnitFE6View | E2E COVERED | - | ALIGNED |
| 24 | SupportTalkFE6View | E2E COVERED | - | ALIGNED |
| 25 | SupportTalkFE7View | E2E COVERED | - | ALIGNED |
| 26 | UnitsShortTextView | E2E COVERED | - | ALIGNED |
| 27 | SomeClassListView | E2E COVERED | - | ALIGNED |
| 28 | VennouWeaponLockView | E2E COVERED | - | ALIGNED |

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
| 38 | MapSettingView | E2E COVERED | YES | ALIGNED |
| 39 | MapChangeView | E2E COVERED | YES | ALIGNED |
| 40 | MapExitPointView | E2E COVERED | YES | ALIGNED |
| 41 | MapPointerView | E2E COVERED | YES | ALIGNED |
| 42 | MapTileAnimationView | E2E COVERED | YES | ALIGNED |
| 43 | MapEditorView | E2E COVERED | - | ALIGNED |
| 44 | MapSettingFE6View | E2E COVERED | - | ALIGNED |
| 45 | MapSettingFE7View | E2E COVERED | - | ALIGNED |
| 46 | MapSettingFE7UView | E2E COVERED | - | ALIGNED |
| 47 | MapSettingDifficultyView | E2E COVERED | - | ALIGNED |
| 48 | MapStyleEditorView | E2E COVERED | - | ALIGNED |
| 49 | MapTerrainBGLookupView | E2E COVERED | - | ALIGNED |
| 50 | MapTerrainFloorLookupView | E2E COVERED | - | ALIGNED |
| 51 | MapTerrainBGLookupTableView | E2E COVERED | - | ALIGNED |
| 52 | MapTerrainFloorLookupTableView | E2E COVERED | - | ALIGNED |
| 53 | MapMiniMapTerrainImageView | E2E COVERED | - | ALIGNED |
| 54 | MapTileAnimation1View | E2E COVERED | - | ALIGNED |
| 55 | MapTileAnimation2View | E2E COVERED | - | ALIGNED |
| 56 | MapLoadFunctionView | E2E COVERED | - | ALIGNED |
| 57 | MapTerrainNameEngView | E2E COVERED | - | ALIGNED |
| 58 | MapEditorAddMapChangeDialogView | E2E COVERED | YES | ALIGNED |
| 59 | MapEditorMarSizeDialogView | E2E COVERED | YES | ALIGNED |
| 60 | MapEditorResizeDialogView | E2E COVERED | YES | ALIGNED |
| 61 | MapPointerNewPLISTPopupView | E2E COVERED | YES | ALIGNED |
| 62 | MapStyleEditorAppendPopupView | E2E COVERED | YES | ALIGNED |
| 63 | MapStyleEditorWarningOverrideView | E2E COVERED | YES | ALIGNED |
| 64 | MapStyleEditorImportImageOptionView | E2E COVERED | YES | ALIGNED |
| 65 | MapSettingDifficultyDialogView | E2E COVERED | YES | ALIGNED |

## Event Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 66 | EventCondView | E2E COVERED | YES | ALIGNED |
| 67 | EventScriptView | E2E COVERED | - | ALIGNED |
| 68 | EventUnitView | E2E COVERED | - | ALIGNED |
| 69 | EventUnitFE6View | E2E COVERED | - | ALIGNED |
| 70 | EventUnitFE7View | E2E COVERED | - | ALIGNED |
| 71 | EventUnitColorView | E2E COVERED | - | ALIGNED |
| 72 | EventUnitItemDropView | E2E COVERED | - | ALIGNED |
| 73 | EventUnitNewAllocView | E2E COVERED | - | ALIGNED |
| 74 | EventBattleTalkView | E2E COVERED | - | ALIGNED |
| 75 | EventBattleTalkFE6View | E2E COVERED | - | ALIGNED |
| 76 | EventBattleTalkFE7View | E2E COVERED | - | ALIGNED |
| 77 | EventBattleDataFE7View | E2E COVERED | - | ALIGNED |
| 78 | EventHaikuView | E2E COVERED | - | ALIGNED |
| 79 | EventHaikuFE6View | E2E COVERED | - | ALIGNED |
| 80 | EventHaikuFE7View | E2E COVERED | - | ALIGNED |
| 81 | EventMapChangeView | E2E COVERED | - | ALIGNED |
| 82 | EventForceSortieView | E2E COVERED | - | ALIGNED |
| 83 | EventForceSortieFE7View | E2E COVERED | - | ALIGNED |
| 84 | EventFunctionPointerView | E2E COVERED | - | ALIGNED |
| 85 | EventFunctionPointerFE7View | E2E COVERED | - | ALIGNED |
| 86 | EventAssemblerView | E2E COVERED | - | ALIGNED |
| 87 | ProcsScriptView | E2E COVERED | - | ALIGNED |
| 88 | EventScriptTemplateView | E2E COVERED | - | ALIGNED |
| 89 | EventScriptCategorySelectView | E2E COVERED | - | ALIGNED |
| 90 | EventScriptPopupView | E2E COVERED | - | ALIGNED |
| 91 | ProcsScriptCategorySelectView | E2E COVERED | - | ALIGNED |
| 92 | AIScriptCategorySelectView | E2E COVERED | - | ALIGNED |

## AI Script Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 93 | AIScriptView | E2E COVERED | - | ALIGNED |
| 94 | AIASMCALLTALKView | E2E COVERED | - | ALIGNED |
| 95 | AIASMCoordinateView | E2E COVERED | - | ALIGNED |
| 96 | AIASMRangeView | E2E COVERED | - | ALIGNED |
| 97 | AIMapSettingView | E2E COVERED | - | ALIGNED |
| 98 | AIPerformItemView | E2E COVERED | - | ALIGNED |
| 99 | AIPerformStaffView | E2E COVERED | - | ALIGNED |
| 100 | AIStealItemView | E2E COVERED | - | ALIGNED |
| 101 | AITargetView | E2E COVERED | - | ALIGNED |
| 102 | AITilesView | E2E COVERED | - | ALIGNED |
| 103 | AIUnitsView | E2E COVERED | - | ALIGNED |
| 104 | AOERANGEView | E2E COVERED | - | ALIGNED |

## Image Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 105 | ImageViewerView | E2E COVERED | YES | ALIGNED |
| 106 | PortraitViewerView | E2E COVERED | YES | ALIGNED |
| 107 | ImagePortraitView | E2E COVERED | - | ALIGNED |
| 108 | ImagePortraitFE6View | E2E COVERED | - | ALIGNED |
| 109 | ImagePortraitImporterView | E2E COVERED | - | ALIGNED |
| 110 | ImageBGView | E2E COVERED | - | ALIGNED |
| 111 | ImageBattleAnimeView | E2E COVERED | - | ALIGNED |
| 112 | ImageBattleAnimePalletView | E2E COVERED | - | ALIGNED |
| 113 | ImageBattleBGView | E2E COVERED | - | ALIGNED |
| 114 | ImageBattleScreenView | E2E COVERED | - | ALIGNED |
| 115 | ImageCGView | E2E COVERED | - | ALIGNED |
| 116 | ImageCGFE7UView | E2E COVERED | - | ALIGNED |
| 117 | ImageUnitPaletteView | E2E COVERED | - | ALIGNED |
| 118 | ImageUnitWaitIconView | E2E COVERED | - | ALIGNED |
| 119 | ImageUnitMoveIconView | E2E COVERED | - | ALIGNED |
| 120 | ImageSystemAreaView | E2E COVERED | - | ALIGNED |
| 121 | ImageGenericEnemyPortraitView | E2E COVERED | - | ALIGNED |
| 122 | ImageRomAnimeView | E2E COVERED | - | ALIGNED |
| 123 | ImageTSAEditorView | E2E COVERED | - | ALIGNED |
| 124 | ImageTSAAnimeView | E2E COVERED | - | ALIGNED |
| 125 | ImageTSAAnime2View | E2E COVERED | - | ALIGNED |
| 126 | ImagePalletView | E2E COVERED | - | ALIGNED |
| 127 | ImageMagicFEditorView | E2E COVERED | - | ALIGNED |
| 128 | ImageMagicCSACreatorView | E2E COVERED | - | ALIGNED |
| 129 | ImageMapActionAnimationView | E2E COVERED | - | ALIGNED |
| 130 | DecreaseColorTSAToolView | E2E COVERED | - | ALIGNED |
| 131 | SystemIconViewerView | E2E COVERED | YES | ALIGNED |
| 132 | SystemHoverColorViewerView | E2E COVERED | YES | ALIGNED |
| 133 | BattleBGViewerView | E2E COVERED | YES | ALIGNED |
| 134 | BattleTerrainViewerView | E2E COVERED | YES | ALIGNED |
| 135 | ChapterTitleViewerView | E2E COVERED | YES | ALIGNED |
| 136 | BigCGViewerView | E2E COVERED | YES | ALIGNED |
| 137 | OPClassDemoViewerView | E2E COVERED | YES | ALIGNED |
| 138 | OPClassFontViewerView | E2E COVERED | YES | ALIGNED |
| 139 | OPPrologueViewerView | E2E COVERED | YES | ALIGNED |
| 140 | GraphicsToolView | E2E COVERED | - | ALIGNED |
| 141 | GraphicsToolPatchMakerView | E2E COVERED | YES | ALIGNED |
| 142 | PaletteChangeColorsView | E2E COVERED | - | ALIGNED |
| 143 | PaletteClipboardView | E2E COVERED | - | ALIGNED |
| 144 | PaletteSwapView | E2E COVERED | - | ALIGNED |
| 145 | ImageBGSelectPopupView | E2E COVERED | - | ALIGNED |

## Audio Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 146 | SongTableView | E2E COVERED | YES | ALIGNED |
| 147 | SongTrackView | E2E COVERED | - | ALIGNED |
| 148 | SongInstrumentView | E2E COVERED | - | ALIGNED |
| 149 | SongInstrumentDirectSoundView | E2E COVERED | - | ALIGNED |
| 150 | SongInstrumentImportWaveView | E2E COVERED | - | ALIGNED |
| 151 | SongTrackImportMidiView | E2E COVERED | - | ALIGNED |
| 152 | SongExchangeView | E2E COVERED | - | ALIGNED |
| 153 | SoundBossBGMViewerView | E2E COVERED | YES | ALIGNED |
| 154 | SoundFootStepsViewerView | E2E COVERED | YES | ALIGNED |
| 155 | SoundRoomViewerView | E2E COVERED | YES | ALIGNED |
| 156 | SoundRoomFE6View | E2E COVERED | - | ALIGNED |
| 157 | SoundRoomCGView | E2E COVERED | - | ALIGNED |
| 158 | ToolBGMMuteDialogView | E2E COVERED | - | ALIGNED |

## Arena / Monster / Summon Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 159 | ArenaClassViewerView | E2E COVERED | YES | ALIGNED |
| 160 | ArenaEnemyWeaponViewerView | E2E COVERED | YES | ALIGNED |
| 161 | LinkArenaDenyUnitViewerView | E2E COVERED | YES | ALIGNED |
| 162 | MonsterProbabilityViewerView | E2E COVERED | YES | ALIGNED |
| 163 | MonsterItemViewerView | E2E COVERED | YES | ALIGNED |
| 164 | MonsterWMapProbabilityViewerView | E2E COVERED | YES | ALIGNED |
| 165 | SummonUnitViewerView | E2E COVERED | YES | ALIGNED |
| 166 | SummonsDemonKingViewerView | E2E COVERED | YES | ALIGNED |

## Menu / ED / World Map Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 167 | MenuDefinitionView | E2E COVERED | YES | ALIGNED |
| 168 | MenuCommandView | E2E COVERED | YES | ALIGNED |
| 169 | EDView | E2E COVERED | YES | ALIGNED |
| 170 | EDStaffRollView | E2E COVERED | YES | ALIGNED |
| 171 | WorldMapPointView | E2E COVERED | YES | ALIGNED |
| 172 | WorldMapBGMView | E2E COVERED | YES | ALIGNED |
| 173 | WorldMapEventPointerView | E2E COVERED | YES | ALIGNED |
| 174 | WorldMapPathView | E2E COVERED | - | ALIGNED |
| 175 | WorldMapPathEditorView | E2E COVERED | - | ALIGNED |
| 176 | WorldMapImageView | E2E COVERED | - | ALIGNED |
| 177 | WorldMapImageFE6View | E2E COVERED | - | ALIGNED |
| 178 | WorldMapImageFE7View | E2E COVERED | - | ALIGNED |
| 179 | WorldMapEventPointerFE6View | E2E COVERED | - | ALIGNED |
| 180 | WorldMapEventPointerFE7View | E2E COVERED | - | ALIGNED |

## Text / Translation Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 181 | TextViewerView | E2E COVERED | YES | ALIGNED |
| 182 | TextMainView | E2E COVERED | - | ALIGNED |
| 183 | OtherTextView | E2E COVERED | - | ALIGNED |
| 184 | CStringView | E2E COVERED | - | ALIGNED |
| 185 | FontEditorView | E2E COVERED | - | ALIGNED |
| 186 | FontZHView | E2E COVERED | - | ALIGNED |
| 187 | DevTranslateView | E2E COVERED | - | ALIGNED |
| 188 | ToolTranslateROMView | E2E COVERED | - | ALIGNED |
| 189 | TextEscapeEditorView | E2E COVERED | - | ALIGNED |
| 190 | TextScriptCategorySelectView | E2E COVERED | - | ALIGNED |
| 191 | TextDicView | E2E COVERED | - | ALIGNED |
| 192 | TextCharCodeView | E2E COVERED | - | ALIGNED |
| 193 | TextBadCharPopupView | E2E COVERED | YES | ALIGNED |
| 194 | TextRefAddDialogView | E2E COVERED | - | ALIGNED |
| 195 | TextToSpeechView | E2E COVERED | - | ALIGNED |

## Structural Data Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 196 | Command85PointerView | E2E COVERED | - | ALIGNED |
| 197 | FE8SpellMenuExtendsView | E2E COVERED | - | ALIGNED |
| 198 | StatusOptionView | E2E COVERED | - | ALIGNED |
| 199 | OAMSPView | E2E COVERED | - | ALIGNED |
| 200 | DumpStructSelectDialogView | E2E COVERED | - | ALIGNED |
| 201 | DumpStructSelectToTextDialogView | E2E COVERED | - | ALIGNED |

## Status Screen Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 202 | StatusParamView | E2E COVERED | - | ALIGNED |
| 203 | StatusRMenuView | E2E COVERED | - | ALIGNED |
| 204 | StatusUnitsMenuView | E2E COVERED | - | ALIGNED |
| 205 | StatusOptionOrderView | E2E COVERED | - | ALIGNED |

## Patch / Skill Systems

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 206 | PatchManagerView | E2E COVERED | - | ALIGNED |
| 207 | ToolCustomBuildView | E2E COVERED | - | ALIGNED |
| 208 | SkillAssignmentUnitSkillSystemView | E2E COVERED | - | ALIGNED |
| 209 | SkillAssignmentClassSkillSystemView | E2E COVERED | - | ALIGNED |
| 210 | SkillConfigSkillSystemView | E2E COVERED | - | ALIGNED |
| 211 | SkillAssignmentUnitCSkillSysView | E2E COVERED | - | ALIGNED |
| 212 | SkillAssignmentClassCSkillSysView | E2E COVERED | - | ALIGNED |
| 213 | SkillAssignmentUnitFE8NView | E2E COVERED | - | ALIGNED |
| 214 | SkillConfigFE8NSkillView | E2E COVERED | - | ALIGNED |
| 215 | SkillConfigFE8NVer2SkillView | E2E COVERED | - | ALIGNED |
| 216 | SkillConfigFE8NVer3SkillView | E2E COVERED | - | ALIGNED |
| 217 | SkillConfigFE8UCSkillSys09xView | E2E COVERED | - | ALIGNED |
| 218 | PatchFilterExView | E2E COVERED | - | ALIGNED |
| 219 | PatchFormUninstallDialogView | E2E COVERED | - | ALIGNED |

## OP Class Editors (Version-Specific)

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 220 | OPClassDemoFE7View | E2E COVERED | - | ALIGNED |
| 221 | OPClassDemoFE7UView | E2E COVERED | - | ALIGNED |
| 222 | OPClassDemoFE8UView | E2E COVERED | - | ALIGNED |
| 223 | OPClassFontFE8UView | E2E COVERED | - | ALIGNED |
| 224 | OPClassAlphaNameView | E2E COVERED | - | ALIGNED |
| 225 | OPClassAlphaNameFE6View | E2E COVERED | - | ALIGNED |

## Bit Flag Editors

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 226 | UbyteBitFlagView | E2E COVERED | YES | ALIGNED |
| 227 | UshortBitFlagView | E2E COVERED | YES | ALIGNED |
| 228 | UwordBitFlagView | E2E COVERED | YES | ALIGNED |

## Error / Dialog Forms

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 229 | ErrorReportView | E2E COVERED | - | ALIGNED |
| 230 | ErrorPaletteMissMatchView | E2E COVERED | - | ALIGNED |
| 231 | ErrorPaletteShowView | E2E COVERED | - | ALIGNED |
| 232 | ErrorPaletteTransparentView | E2E COVERED | - | ALIGNED |
| 233 | ErrorTSAErrorView | E2E COVERED | - | ALIGNED |
| 234 | ErrorLongMessageDialogView | E2E COVERED | YES | ALIGNED |
| 235 | ErrorUnknownROMView | E2E COVERED | - | ALIGNED |
| 236 | HowDoYouLikePatchView | E2E COVERED | - | ALIGNED |
| 237 | HowDoYouLikePatch2View | E2E COVERED | - | ALIGNED |

## Tools

| # | View | E2E Status | Data Verified | Aligned |
|---|------|-----------|---------------|---------|
| 238 | ToolUndoView | E2E COVERED | - | ALIGNED |
| 239 | ToolFELintView | E2E COVERED | - | ALIGNED |
| 240 | ToolROMRebuildView | E2E COVERED | - | ALIGNED |
| 241 | ToolLZ77View | E2E COVERED | - | ALIGNED |
| 242 | ToolDiffView | E2E COVERED | - | ALIGNED |
| 243 | ToolUPSPatchSimpleView | E2E COVERED | - | ALIGNED |
| 244 | ToolUPSOpenSimpleView | E2E COVERED | - | ALIGNED |
| 245 | ToolFlagNameView | E2E COVERED | - | ALIGNED |
| 246 | ToolUseFlagView | E2E COVERED | - | ALIGNED |
| 247 | ToolUnitTalkGroupView | E2E COVERED | - | ALIGNED |
| 248 | ToolASMInsertView | E2E COVERED | - | ALIGNED |
| 249 | HexEditorView | E2E COVERED | - | ALIGNED |
| 250 | DisASMView | E2E COVERED | - | ALIGNED |
| 251 | LogViewerView | E2E COVERED | - | ALIGNED |
| 252 | GrowSimulatorView | E2E COVERED | - | ALIGNED |
| 253 | OptionsView | E2E COVERED | - | ALIGNED |
| 254 | DisASMDumpAllView | E2E COVERED | YES | ALIGNED |
| 255 | DisASMDumpAllArgGrepView | E2E COVERED | YES | ALIGNED |
| 256 | HexEditorJumpView | E2E COVERED | YES | ALIGNED |
| 257 | HexEditorMarkView | E2E COVERED | YES | ALIGNED |
| 258 | HexEditorSearchView | E2E COVERED | YES | ALIGNED |
| 259 | PointerToolView | E2E COVERED | - | ALIGNED |
| 260 | PointerToolBatchInputView | E2E COVERED | YES | ALIGNED |
| 261 | PointerToolCopyToView | E2E COVERED | YES | ALIGNED |
| 262 | PackedMemorySlotView | E2E COVERED | YES | ALIGNED |
| 263 | EmulatorMemoryView | E2E COVERED | - | ALIGNED |
| 264 | RAMRewriteToolMAPView | E2E COVERED | - | ALIGNED |
| 265 | ToolAnimationCreatorView | E2E COVERED | - | ALIGNED |
| 266 | ToolThreeMargeView | E2E COVERED | - | ALIGNED |
| 267 | ToolASMEditView | E2E COVERED | - | ALIGNED |
| 268 | ToolExportEAEventView | E2E COVERED | - | ALIGNED |
| 269 | ToolDecompileResultView | E2E COVERED | - | ALIGNED |
| 270 | ToolChangeProjectnameView | E2E COVERED | YES | ALIGNED |
| 271 | ToolAutomaticRecoveryROMHeaderView | E2E COVERED | YES | ALIGNED |
| 272 | MoveToFreeSpaceView | E2E COVERED | - | ALIGNED |
| 273 | ToolSubtitleOverlayView | E2E COVERED | - | ALIGNED |
| 274 | ToolSubtitleSettingDialogView | E2E COVERED | - | ALIGNED |
| 275 | EDFE6View | E2E COVERED | - | ALIGNED |
| 276 | EDFE7View | E2E COVERED | - | ALIGNED |
| 277 | EDSensekiCommentView | E2E COVERED | - | ALIGNED |
| 278 | EventFinalSerifFE7View | E2E COVERED | - | ALIGNED |
| 279 | EventMoveDataFE7View | E2E COVERED | - | ALIGNED |
| 280 | EventTalkGroupFE7View | E2E COVERED | - | ALIGNED |
| 281 | EventTemplate1View | E2E COVERED | - | ALIGNED |
| 282 | EventTemplate2View | E2E COVERED | - | ALIGNED |
| 283 | EventTemplate3View | E2E COVERED | - | ALIGNED |
| 284 | EventTemplate4View | E2E COVERED | - | ALIGNED |
| 285 | EventTemplate5View | E2E COVERED | - | ALIGNED |
| 286 | EventTemplate6View | E2E COVERED | - | ALIGNED |
| 287 | EventTemplateImplView | E2E COVERED | - | ALIGNED |
| 288 | ItemEffectivenessSkillSystemsReworkView | E2E COVERED | - | ALIGNED |
| 289 | ItemRandomChestView | E2E COVERED | - | ALIGNED |
| 290 | ItemStatBonusesSkillSystemsView | E2E COVERED | - | ALIGNED |
| 291 | ItemStatBonusesVennoView | E2E COVERED | - | ALIGNED |
| 292 | MenuExtendSplitMenuView | E2E COVERED | - | ALIGNED |
| 293 | OpenLastSelectedFileView | E2E COVERED | - | ALIGNED |
| 294 | ResourceView | E2E COVERED | - | ALIGNED |
| 295 | SongTrackAllChangeTrackView | E2E COVERED | - | ALIGNED |
| 296 | SongTrackChangeTrackView | E2E COVERED | - | ALIGNED |
| 297 | SongTrackImportSelectInstrumentView | E2E COVERED | - | ALIGNED |
| 298 | ToolInitWizardView | E2E COVERED | - | ALIGNED |
| 299 | ToolUndoPopupDialogView | E2E COVERED | YES | ALIGNED |
| 300 | ToolUpdateDialogView | E2E COVERED | - | ALIGNED |
| 301 | VersionView | E2E COVERED | - | ALIGNED |
| 302 | WelcomeView | E2E COVERED | YES | ALIGNED |
| 303 | ToolAllWorkSupportView | E2E COVERED | - | ALIGNED |
| 304 | ToolProblemReportView | E2E COVERED | - | ALIGNED |
| 305 | WorldMapPathMoveEditorView | E2E COVERED | - | ALIGNED |
| 306 | MantAnimationView | E2E COVERED | - | ALIGNED |
| 307 | RAMRewriteToolView | E2E COVERED | - | ALIGNED |
| 308 | MainSimpleMenuView | E2E COVERED | - | ALIGNED |
| 309 | MainSimpleMenuEventErrorView | E2E COVERED | - | ALIGNED |
| 310 | MainSimpleMenuImageSubView | E2E COVERED | - | ALIGNED |
| 311 | ToolEmulatorSetupMessageView | E2E COVERED | - | ALIGNED |
| 312 | ToolThreeMargeCloseAlertView | E2E COVERED | - | ALIGNED |
| 313 | ToolClickWriteFloatControlPanelButtonView | E2E COVERED | - | ALIGNED |
| 314 | ToolWorkSupport_UpdateQuestionDialogView | E2E COVERED | - | ALIGNED |
| 315 | MainSimpleMenuEventErrorIgnoreErrorView | E2E COVERED | - | ALIGNED |
| 316 | ToolProblemReportSearchBackupView | E2E COVERED | - | ALIGNED |
| 317 | ToolProblemReportSearchSavView | E2E COVERED | - | ALIGNED |
| 318 | ToolWorkSupportView | E2E COVERED | - | ALIGNED |
| 319 | ToolWorkSupport_SelectUPSView | E2E COVERED | - | ALIGNED |
| 320 | ToolDiffDebugSelectView | E2E COVERED | - | ALIGNED |
| 321 | SMEPromoListView | E2E COVERED | - | ALIGNED |
| 322 | ToolRunHintMessageView | E2E COVERED | - | ALIGNED |
| 323 | ImageChapterTitleFE7View | E2E COVERED | YES | ALIGNED |
| 324 | MapTerrainNameView | E2E COVERED | YES | ALIGNED |
