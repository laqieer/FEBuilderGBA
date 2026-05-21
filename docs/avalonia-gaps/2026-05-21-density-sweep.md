---
generated: 2026-05-21T14:26:51Z
git-sha: aa4b0ed94
sweep-type: density
---

# Avalonia vs WinForms — Control Density Sweep

This report ranks every paired editor by the absolute % delta between the
WinForms-designer control count and the Avalonia .axaml control count.
A large negative delta is a strong proxy for **missing fields in the
Avalonia migration** — the WinForms side has UI for inputs the Avalonia
counterpart does not expose. Use the top-20 HIGH subsections below as the
backlog seed for follow-up gap-fix PRs.

Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/ControlDensityScanner.cs`.
Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-density --out=<path>`.

## Summary

| Verdict | Threshold | Count |
|---|---|---|
| HIGH | |Δ%| ≥ 50 | 245 |
| MEDIUM | 25 ≤ |Δ%| < 50 | 80 |
| LOW | |Δ%| < 25 | 47 |

## Ranked Density Deltas

Negative `Δ%` = Avalonia has *fewer* controls than WinForms (probable gap).
Positive = Avalonia has *more* (often fine — refactoring or richer UI).
Rows are sorted by signed `Δ%` ascending so the biggest gaps come first.

Rows with WF=0 (no on-disk Designer.cs for the named form) live in
[Unmatched WinForms Counterparts](#unmatched-winforms-counterparts) below — they
represent ListParityHelper mappings whose WF file is renamed/missing rather
than a real migration gap.

| Verdict | WF Form | AV View | WF | AV | Δ | Δ% | Match |
|---|---|---|---:|---:|---:|---:|---|
| High | `EmulatorMemoryForm` | `EmulatorMemoryView` | 353 | 5 | -348 | -98.6% | Heuristic |
| High | `ImageBattleScreenForm` | `ImageBattleScreenView` | 133 | 3 | -130 | -97.7% | Heuristic |
| High | `MapSettingFE6Form` | `MapSettingFE6View` | 126 | 3 | -123 | -97.6% | ListParityHelper |
| High | `WorldMapImageForm` | `WorldMapImageView` | 107 | 3 | -104 | -97.2% | Heuristic |
| High | `ImageTSAEditorForm` | `ImageTSAEditorView` | 100 | 3 | -97 | -97.0% | Heuristic |
| High | `ImageBattleAnimePalletForm` | `ImageBattleAnimePalletView` | 99 | 3 | -96 | -97.0% | Heuristic |
| High | `ImagePalletForm` | `ImagePalletView` | 98 | 3 | -95 | -96.9% | Heuristic |
| High | `EDFE7Form` | `EDFE7View` | 81 | 3 | -78 | -96.3% | Heuristic |
| High | `ClassForm` | `ClassFE6View` | 211 | 8 | -203 | -96.2% | ListParityHelper |
| High | `OPClassDemoForm` | `ClassOPDemoView` | 63 | 3 | -60 | -95.2% | ListParityHelper |
| High | `EventBattleTalkFE7Form` | `EventBattleTalkFE7View` | 62 | 3 | -59 | -95.2% | ListParityHelper |
| High | `EventBattleTalkFE6Form` | `EventBattleTalkFE6View` | 61 | 3 | -58 | -95.1% | ListParityHelper |
| High | `EventHaikuFE7Form` | `EventHaikuFE7View` | 60 | 3 | -57 | -95.0% | ListParityHelper |
| High | `MapStyleEditorForm` | `MapStyleEditorView` | 153 | 8 | -145 | -94.8% | ListParityHelper |
| High | `ErrorPaletteTransparentForm` | `ErrorPaletteTransparentView` | 50 | 3 | -47 | -94.0% | Heuristic |
| High | `ToolLZ77Form` | `ToolLZ77View` | 50 | 3 | -47 | -94.0% | Heuristic |
| High | `ImagePortraitImporterForm` | `ImagePortraitImporterView` | 42 | 3 | -39 | -92.9% | Heuristic |
| High | `AIScriptForm` | `AIScriptView` | 37 | 3 | -34 | -91.9% | Heuristic |
| High | `ImageMagicCSACreatorForm` | `ImageMagicCSACreatorView` | 37 | 3 | -34 | -91.9% | Heuristic |
| High | `ImageMagicFEditorForm` | `ImageMagicFEditorView` | 37 | 3 | -34 | -91.9% | ListParityHelper |
| High | `EventMapChangeForm` | `EventMapChangeView` | 36 | 3 | -33 | -91.7% | ListParityHelper |
| High | `WorldMapImageFE6Form` | `WorldMapImageFE6View` | 36 | 3 | -33 | -91.7% | Heuristic |
| High | `ToolTranslateROMForm` | `ToolTranslateROMView` | 35 | 3 | -32 | -91.4% | Heuristic |
| High | `EventHaikuFE6Form` | `EventHaikuFE6View` | 34 | 3 | -31 | -91.2% | ListParityHelper |
| High | `FE8SpellMenuExtendsForm` | `FE8SpellMenuExtendsView` | 34 | 3 | -31 | -91.2% | Heuristic |
| High | `MonsterWMapProbabilityForm` | `MonsterWMapProbabilityViewerView` | 66 | 6 | -60 | -90.9% | ListParityHelper |
| High | `EventCondForm` | `EventCondView` | 414 | 41 | -373 | -90.1% | ListParityHelper |
| High | `EventBattleTalkForm` | `EventBattleTalkView` | 30 | 3 | -27 | -90.0% | ListParityHelper |
| High | `ToolInitWizardForm` | `ToolInitWizardView` | 80 | 8 | -72 | -90.0% | Heuristic |
| High | `EventHaikuForm` | `EventHaikuView` | 28 | 3 | -25 | -89.3% | ListParityHelper |
| High | `MonsterItemForm` | `MonsterItemViewerView` | 129 | 14 | -115 | -89.1% | ListParityHelper |
| High | `MoveCostForm` | `MoveCostEditorView` | 72 | 8 | -64 | -88.9% | ListParityHelper |
| High | `SkillConfigFE8NVer3SkillForm` | `SkillConfigFE8NVer3SkillView` | 166 | 19 | -147 | -88.6% | Heuristic |
| High | `SkillConfigFE8NVer2SkillForm` | `SkillConfigFE8NVer2SkillView` | 136 | 17 | -119 | -87.5% | Heuristic |
| High | `SongTrackImportWaveForm` | `SongTrackImportWaveView` | 23 | 3 | -20 | -87.0% | Heuristic |
| High | `WorldMapImageFE7Form` | `WorldMapImageFE7View` | 23 | 3 | -20 | -87.0% | Heuristic |
| High | `EventBattleDataFE7Form` | `EventBattleDataFE7View` | 21 | 3 | -18 | -85.7% | Heuristic |
| High | `FontForm` | `FontEditorView` | 21 | 3 | -18 | -85.7% | Heuristic |
| High | `ItemEffectivenessSkillSystemsReworkForm` | `ItemEffectivenessSkillSystemsReworkView` | 21 | 3 | -18 | -85.7% | Heuristic |
| High | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` | 20 | 3 | -17 | -85.0% | Heuristic |
| High | `ToolProblemReportForm` | `ToolProblemReportView` | 20 | 3 | -17 | -85.0% | Heuristic |
| High | `WorldMapEventPointerFE7Form` | `WorldMapEventPointerFE7View` | 20 | 3 | -17 | -85.0% | Heuristic |
| High | `WorldMapEventPointerForm` | `WorldMapEventPointerView` | 39 | 6 | -33 | -84.6% | ListParityHelper |
| High | `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | 19 | 3 | -16 | -84.2% | Heuristic |
| High | `EDFE6Form` | `EDFE6View` | 19 | 3 | -16 | -84.2% | Heuristic |
| High | `ToolASMInsertForm` | `ToolASMInsertView` | 19 | 3 | -16 | -84.2% | Heuristic |
| High | `SkillAssignmentClassCSkillSysForm` | `SkillAssignmentClassCSkillSysView` | 43 | 7 | -36 | -83.7% | Heuristic |
| High | `SkillAssignmentClassSkillSystemForm` | `SkillAssignmentClassSkillSystemView` | 43 | 7 | -36 | -83.7% | Heuristic |
| High | `PaletteSwapForm` | `PaletteSwapView` | 49 | 8 | -41 | -83.7% | Heuristic |
| High | `SongInstrumentImportWaveForm` | `SongInstrumentImportWaveView` | 18 | 3 | -15 | -83.3% | Heuristic |
| High | `SongInstrumentForm` | `SongInstrumentView` | 323 | 54 | -269 | -83.3% | Heuristic |
| High | `MoveCostFE6Form` | `MoveCostFE6View` | 57 | 10 | -47 | -82.5% | ListParityHelper |
| High | `FontZHForm` | `FontZHView` | 17 | 3 | -14 | -82.4% | Heuristic |
| High | `MantAnimationForm` | `MantAnimationView` | 17 | 3 | -14 | -82.4% | ListParityHelper |
| High | `ToolROMRebuildForm` | `ToolROMRebuildView` | 17 | 3 | -14 | -82.4% | Heuristic |
| High | `UnitIncreaseHeightForm` | `UnitIncreaseHeightView` | 17 | 3 | -14 | -82.4% | ListParityHelper |
| High | `EventFinalSerifFE7Form` | `EventFinalSerifFE7View` | 16 | 3 | -13 | -81.3% | ListParityHelper |
| High | `MainSimpleMenuImageSubForm` | `MainSimpleMenuImageSubView` | 16 | 3 | -13 | -81.3% | Heuristic |
| High | `MapExitPointForm` | `MapExitPointView` | 32 | 6 | -26 | -81.3% | ListParityHelper |
| High | `OPClassFontForm` | `ClassOPFontView` | 16 | 3 | -13 | -81.3% | ListParityHelper |
| High | `SkillConfigFE8NSkillForm` | `SkillConfigFE8NSkillView` | 169 | 33 | -136 | -80.5% | Heuristic |
| High | `EDForm` | `EDView` | 65 | 13 | -52 | -80.0% | ListParityHelper |
| High | `SkillAssignmentUnitCSkillSysForm` | `SkillAssignmentUnitCSkillSysView` | 35 | 7 | -28 | -80.0% | Heuristic |
| High | `SkillAssignmentUnitSkillSystemForm` | `SkillAssignmentUnitSkillSystemView` | 35 | 7 | -28 | -80.0% | Heuristic |
| High | `ToolDiffForm` | `ToolDiffView` | 15 | 3 | -12 | -80.0% | Heuristic |
| High | `OPClassAlphaNameForm` | `OPClassAlphaNameView` | 34 | 7 | -27 | -79.4% | ListParityHelper |
| High | `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` | 19 | 4 | -15 | -78.9% | Heuristic |
| High | `ArenaEnemyWeaponForm` | `ArenaEnemyWeaponViewerView` | 28 | 6 | -22 | -78.6% | ListParityHelper |
| High | `EventUnitColorForm` | `EventUnitColorView` | 14 | 3 | -11 | -78.6% | Heuristic |
| High | `ImageRomAnimeForm` | `ImageRomAnimeView` | 14 | 3 | -11 | -78.6% | Heuristic |
| High | `UnitActionPointerForm` | `UnitActionPointerView` | 14 | 3 | -11 | -78.6% | ListParityHelper |
| High | `WorldMapEventPointerFE6Form` | `WorldMapEventPointerFE6View` | 14 | 3 | -11 | -78.6% | Heuristic |
| High | `Command85PointerForm` | `Command85PointerView` | 13 | 3 | -10 | -76.9% | ListParityHelper |
| High | `SkillConfigSkillSystemForm` | `SkillConfigSkillSystemView` | 30 | 7 | -23 | -76.7% | Heuristic |
| High | `ImageUnitPaletteForm` | `ImageUnitPaletteView` | 133 | 32 | -101 | -75.9% | ListParityHelper |
| High | `TextForm` | `TextViewerView` | 62 | 15 | -47 | -75.8% | Heuristic |
| High | `AIMapSettingForm` | `AIMapSettingView` | 48 | 12 | -36 | -75.0% | ListParityHelper |
| High | `EventAssemblerForm` | `EventAssemblerView` | 12 | 3 | -9 | -75.0% | Heuristic |
| High | `EventTemplate2Form` | `EventTemplate2View` | 12 | 3 | -9 | -75.0% | Heuristic |
| High | `EventTemplate3Form` | `EventTemplate3View` | 12 | 3 | -9 | -75.0% | Heuristic |
| High | `MapMiniMapTerrainImageForm` | `MapMiniMapTerrainImageView` | 12 | 3 | -9 | -75.0% | ListParityHelper |
| High | `ToolFELintForm` | `ToolFELintView` | 12 | 3 | -9 | -75.0% | Heuristic |
| High | `ImageBGForm` | `ImageBGView` | 26 | 7 | -19 | -73.1% | ListParityHelper |
| High | `EventTemplate1Form` | `EventTemplate1View` | 11 | 3 | -8 | -72.7% | Heuristic |
| High | `ImageGenericEnemyPortraitForm` | `ImageGenericEnemyPortraitView` | 18 | 5 | -13 | -72.2% | ListParityHelper |
| High | `ImageCGForm` | `ImageCGView` | 24 | 7 | -17 | -70.8% | ListParityHelper |
| High | `DevTranslateForm` | `DevTranslateView` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `EventTemplate4Form` | `EventTemplate4View` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `MapSettingDifficultyForm` | `MapSettingDifficultyView` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `SongTrackChangeTrackForm` | `SongTrackChangeTrackView` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `ToolCustomBuildForm` | `ToolCustomBuildView` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `WorldMapPathEditorForm` | `WorldMapPathEditorView` | 10 | 3 | -7 | -70.0% | Heuristic |
| High | `ItemUsagePointerForm` | `ItemUsagePointerViewerView` | 23 | 7 | -16 | -69.6% | ListParityHelper |
| High | `SkillConfigFE8UCSkillSys09xForm` | `SkillConfigFE8UCSkillSys09xView` | 29 | 9 | -20 | -69.0% | Heuristic |
| High | `ImageTSAAnimeForm` | `ImageTSAAnimeView` | 22 | 7 | -15 | -68.2% | ListParityHelper |
| High | `UnitCustomBattleAnimeForm` | `UnitCustomBattleAnimeView` | 31 | 10 | -21 | -67.7% | ListParityHelper |
| High | `ErrorPaletteShowForm` | `ErrorPaletteShowView` | 9 | 3 | -6 | -66.7% | Heuristic |
| High | `ErrorReportForm` | `ErrorReportView` | 9 | 3 | -6 | -66.7% | Heuristic |
| High | `SongTrackAllChangeTrackForm` | `SongTrackAllChangeTrackView` | 9 | 3 | -6 | -66.7% | Heuristic |
| High | `MapTileAnimation2Form` | `MapTileAnimation2View` | 40 | 14 | -26 | -65.0% | ListParityHelper |
| High | `SkillAssignmentUnitFE8NForm` | `SkillAssignmentUnitFE8NView` | 31 | 11 | -20 | -64.5% | Heuristic |
| High | `UnitPaletteForm` | `UnitPaletteView` | 50 | 18 | -32 | -64.0% | ListParityHelper |
| High | `ItemEffectivenessForm` | `ItemEffectivenessViewerView` | 19 | 7 | -12 | -63.2% | ListParityHelper |
| High | `EventTemplate6Form` | `EventTemplate6View` | 8 | 3 | -5 | -62.5% | Heuristic |
| High | `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` | 16 | 6 | -10 | -62.5% | ListParityHelper |
| High | `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` | 16 | 6 | -10 | -62.5% | ListParityHelper |
| High | `SoundFootStepsForm` | `SoundFootStepsViewerView` | 16 | 6 | -10 | -62.5% | ListParityHelper |
| High | `ImageTSAAnime2Form` | `ImageTSAAnime2View` | 38 | 15 | -23 | -60.5% | ListParityHelper |
| High | `EventFunctionPointerForm` | `EventFunctionPointerView` | 15 | 6 | -9 | -60.0% | ListParityHelper |
| High | `ExtraUnitForm` | `ExtraUnitView` | 15 | 6 | -9 | -60.0% | ListParityHelper |
| High | `MapTileAnimation1Form` | `MapTileAnimation1View` | 25 | 10 | -15 | -60.0% | ListParityHelper |
| High | `SongTrackImportMidiForm` | `SongTrackImportMidiView` | 25 | 10 | -15 | -60.0% | Heuristic |
| High | `ArenaClassForm` | `ArenaClassViewerView` | 17 | 7 | -10 | -58.8% | ListParityHelper |
| High | `EventMoveDataFE7Form` | `EventMoveDataFE7View` | 17 | 7 | -10 | -58.8% | Heuristic |
| High | `ImageMapActionAnimationForm` | `ImageMapActionAnimationView` | 29 | 12 | -17 | -58.6% | ListParityHelper |
| High | `SongTrackForm` | `SongTrackView` | 45 | 19 | -26 | -57.8% | ListParityHelper |
| High | `AITilesForm` | `AITilesView` | 14 | 6 | -8 | -57.1% | Heuristic |
| High | `ErrorPaletteMissMatchForm` | `ErrorPaletteMissMatchView` | 7 | 3 | -4 | -57.1% | Heuristic |
| High | `ErrorTSAErrorForm` | `ErrorTSAErrorView` | 7 | 3 | -4 | -57.1% | Heuristic |
| High | `LinkArenaDenyUnitForm` | `LinkArenaDenyUnitViewerView` | 14 | 6 | -8 | -57.1% | ListParityHelper |
| High | `SoundRoomCGForm` | `SoundRoomCGView` | 14 | 6 | -8 | -57.1% | ListParityHelper |
| High | `StatusOptionOrderForm` | `StatusOptionOrderView` | 14 | 6 | -8 | -57.1% | ListParityHelper |
| High | `ToolFlagNameForm` | `ToolFlagNameView` | 7 | 3 | -4 | -57.1% | Heuristic |
| High | `ToolUpdateDialogForm` | `ToolUpdateDialogView` | 7 | 3 | -4 | -57.1% | Heuristic |
| High | `ItemPromotionForm` | `ItemPromotionViewerView` | 16 | 7 | -9 | -56.3% | ListParityHelper |
| High | `MapPointerForm` | `MapPointerView` | 16 | 7 | -9 | -56.3% | ListParityHelper |
| High | `OPClassFontFE8UForm` | `OPClassFontFE8UView` | 16 | 7 | -9 | -56.3% | ListParityHelper |
| High | `ItemFE6Form` | `ItemFE6View` | 121 | 53 | -68 | -56.2% | Heuristic |
| High | `TextDicForm` | `TextDicView` | 60 | 27 | -33 | -55.0% | ListParityHelper |
| High | `ImageItemIconForm` | `ItemIconViewerView` | 22 | 10 | -12 | -54.5% | ListParityHelper |
| High | `OPClassDemoFE7UForm` | `OPClassDemoFE7UView` | 56 | 26 | -30 | -53.6% | ListParityHelper |
| High | `EDStaffRollForm` | `EDStaffRollView` | 17 | 8 | -9 | -52.9% | ListParityHelper |
| High | `CCBranchForm` | `CCBranchEditorView` | 25 | 12 | -13 | -52.0% | ListParityHelper |
| High | `PaletteChangeColorsForm` | `PaletteChangeColorsView` | 25 | 12 | -13 | -52.0% | Heuristic |
| High | `AIUnitsForm` | `AIUnitsView` | 16 | 8 | -8 | -50.0% | Heuristic |
| High | `EventTalkGroupFE7Form` | `EventTalkGroupFE7View` | 14 | 7 | -7 | -50.0% | Heuristic |
| High | `EventTemplate5Form` | `EventTemplate5View` | 6 | 3 | -3 | -50.0% | Heuristic |
| High | `ExtraUnitFE8UForm` | `ExtraUnitFE8UView` | 16 | 8 | -8 | -50.0% | ListParityHelper |
| High | `ImageBattleBGForm` | `ImageBattleBGView` | 26 | 13 | -13 | -50.0% | ListParityHelper |
| High | `ItemRandomChestForm` | `ItemRandomChestView` | 16 | 8 | -8 | -50.0% | Heuristic |
| High | `OAMSPForm` | `OAMSPView` | 6 | 3 | -3 | -50.0% | Heuristic |
| High | `OPClassDemoFE7Form` | `OPClassDemoFE7View` | 64 | 32 | -32 | -50.0% | ListParityHelper |
| High | `SummonUnitForm` | `SummonUnitViewerView` | 16 | 8 | -8 | -50.0% | ListParityHelper |
| High | `VersionForm` | `VersionView` | 2 | 1 | -1 | -50.0% | Heuristic |
| Medium | `ImageChapterTitleForm` | `ChapterTitleViewerView` | 25 | 13 | -12 | -48.0% | ListParityHelper |
| Medium | `OPClassDemoFE8UForm` | `OPClassDemoFE8UView` | 50 | 26 | -24 | -48.0% | ListParityHelper |
| Medium | `MapEditorForm` | `MapEditorView` | 23 | 12 | -11 | -47.8% | ListParityHelper |
| Medium | `EventForceSortieForm` | `EventForceSortieView` | 19 | 10 | -9 | -47.4% | ListParityHelper |
| Medium | `WorldMapBGMForm` | `WorldMapBGMView` | 19 | 10 | -9 | -47.4% | ListParityHelper |
| Medium | `AIStealItemForm` | `AIStealItemView` | 17 | 9 | -8 | -47.1% | ListParityHelper |
| Medium | `ItemShopForm` | `ItemShopViewerView` | 17 | 9 | -8 | -47.1% | ListParityHelper |
| Medium | `MapLoadFunctionForm` | `MapLoadFunctionView` | 17 | 9 | -8 | -47.1% | ListParityHelper |
| Medium | `EventFunctionPointerFE7Form` | `EventFunctionPointerFE7View` | 15 | 8 | -7 | -46.7% | ListParityHelper |
| Medium | `ItemEffectPointerForm` | `ItemEffectPointerViewerView` | 13 | 7 | -6 | -46.2% | ListParityHelper |
| Medium | `OPPrologueForm` | `OPPrologueViewerView` | 26 | 14 | -12 | -46.2% | ListParityHelper |
| Medium | `SongTableForm` | `SongTableView` | 26 | 14 | -12 | -46.2% | ListParityHelper |
| Medium | `GraphicsToolForm` | `GraphicsToolView` | 33 | 18 | -15 | -45.5% | Heuristic |
| Medium | `ImageCGFE7UForm` | `ImageCGFE7UView` | 31 | 17 | -14 | -45.2% | ListParityHelper |
| Medium | `WorldMapPathMoveEditorForm` | `WorldMapPathMoveEditorView` | 20 | 11 | -9 | -45.0% | Heuristic |
| Medium | `PatchFilterExForm` | `PatchFilterExView` | 9 | 5 | -4 | -44.4% | Heuristic |
| Medium | `ImageChapterTitleFE7Form` | `ImageChapterTitleFE7View` | 16 | 9 | -7 | -43.8% | ListParityHelper |
| Medium | `EventUnitForm` | `EventUnitView` | 95 | 54 | -41 | -43.2% | ListParityHelper |
| Medium | `SomeClassListForm` | `SomeClassListView` | 14 | 8 | -6 | -42.9% | Heuristic |
| Medium | `AIPerformItemForm` | `AIPerformItemView` | 19 | 11 | -8 | -42.1% | ListParityHelper |
| Medium | `AIPerformStaffForm` | `AIPerformStaffView` | 19 | 11 | -8 | -42.1% | ListParityHelper |
| Medium | `EventForceSortieFE7Form` | `EventForceSortieFE7View` | 24 | 14 | -10 | -41.7% | ListParityHelper |
| Medium | `ImagePortraitForm` | `PortraitViewerView` | 63 | 37 | -26 | -41.3% | ListParityHelper |
| Medium | `ImageSystemAreaForm` | `ImageSystemAreaView` | 22 | 13 | -9 | -40.9% | ListParityHelper |
| Medium | `ItemWeaponTriangleForm` | `ItemWeaponTriangleViewerView` | 22 | 13 | -9 | -40.9% | ListParityHelper |
| Medium | `ImageBattleAnimeForm` | `ImageBattleAnimeView` | 79 | 47 | -32 | -40.5% | ListParityHelper |
| Medium | `EDSensekiCommentForm` | `EDSensekiCommentView` | 20 | 12 | -8 | -40.0% | ListParityHelper |
| Medium | `EventUnitItemDropForm` | `EventUnitItemDropView` | 5 | 3 | -2 | -40.0% | Heuristic |
| Medium | `EventUnitNewAllocForm` | `EventUnitNewAllocView` | 5 | 3 | -2 | -40.0% | Heuristic |
| Medium | `OtherTextForm` | `OtherTextView` | 5 | 3 | -2 | -40.0% | Heuristic |
| Medium | `SoundRoomFE6Form` | `SoundRoomFE6View` | 20 | 12 | -8 | -40.0% | ListParityHelper |
| Medium | `SummonsDemonKingForm` | `SummonsDemonKingViewerView` | 60 | 36 | -24 | -40.0% | ListParityHelper |
| Medium | `TextToSpeechForm` | `TextToSpeechView` | 10 | 6 | -4 | -40.0% | Heuristic |
| Medium | `ToolUPSOpenSimpleForm` | `ToolUPSOpenSimpleView` | 5 | 3 | -2 | -40.0% | Heuristic |
| Medium | `ToolUndoForm` | `ToolUndoView` | 5 | 3 | -2 | -40.0% | Heuristic |
| Medium | `DisASMDumpAllArgGrepForm` | `DisASMDumpAllArgGrepView` | 11 | 7 | -4 | -36.4% | Heuristic |
| Medium | `SoundRoomForm` | `SoundRoomViewerView` | 22 | 14 | -8 | -36.4% | ListParityHelper |
| Medium | `MapSettingFE7Form` | `MapSettingFE7View` | 229 | 146 | -83 | -36.2% | ListParityHelper |
| Medium | `MapSettingFE7UForm` | `MapSettingFE7UView` | 233 | 150 | -83 | -35.6% | ListParityHelper |
| Medium | `ImagePortraitFE6Form` | `ImagePortraitFE6View` | 34 | 22 | -12 | -35.3% | ListParityHelper |
| Medium | `ItemWeaponEffectForm` | `ItemWeaponEffectViewerView` | 40 | 26 | -14 | -35.0% | ListParityHelper |
| Medium | `UnitFE6Form` | `UnitFE6View` | 152 | 99 | -53 | -34.9% | ListParityHelper |
| Medium | `HowDoYouLikePatch2Form` | `HowDoYouLikePatch2View` | 6 | 4 | -2 | -33.3% | Heuristic |
| Medium | `MapTerrainNameEngForm` | `MapTerrainNameEngView` | 12 | 8 | -4 | -33.3% | ListParityHelper |
| Medium | `MenuCommandForm` | `MenuCommandView` | 39 | 26 | -13 | -33.3% | ListParityHelper |
| Medium | `TextCharCodeForm` | `TextCharCodeView` | 24 | 16 | -8 | -33.3% | Heuristic |
| Medium | `UnitFE7Form` | `UnitFE7View` | 160 | 107 | -53 | -33.1% | ListParityHelper |
| Medium | `MapChangeForm` | `MapChangeView` | 37 | 25 | -12 | -32.4% | ListParityHelper |
| Medium | `ClassForm` | `ClassEditorView` | 211 | 145 | -66 | -31.3% | ListParityHelper |
| Medium | `ItemForm` | `ItemEditorView` | 128 | 88 | -40 | -31.3% | ListParityHelper |
| Medium | `UnitForm` | `UnitEditorView` | 183 | 126 | -57 | -31.1% | ListParityHelper |
| Medium | `SupportAttributeForm` | `SupportAttributeView` | 29 | 20 | -9 | -31.0% | ListParityHelper |
| Medium | `WelcomeForm` | `WelcomeView` | 13 | 9 | -4 | -30.8% | Heuristic |
| Medium | `BigCGForm` | `BigCGViewerView` | 20 | 14 | -6 | -30.0% | ListParityHelper |
| Medium | `SkillSystemsEffectivenessReworkClassTypeForm` | `SkillSystemsEffectivenessReworkClassTypeView` | 10 | 7 | -3 | -30.0% | Heuristic |
| Medium | `WorldMapPathForm` | `WorldMapPathView` | 24 | 17 | -7 | -29.2% | ListParityHelper |
| Medium | `ImageBGSelectPopupForm` | `ImageBGSelectPopupView` | 7 | 5 | -2 | -28.6% | Heuristic |
| Medium | `ImagePortraitForm` | `ImagePortraitView` | 63 | 45 | -18 | -28.6% | ListParityHelper |
| Medium | `SupportTalkForm` | `SupportTalkView` | 32 | 23 | -9 | -28.1% | ListParityHelper |
| Medium | `VennouWeaponLockForm` | `VennouWeaponLockView` | 15 | 11 | -4 | -26.7% | Heuristic |
| Medium | `SupportTalkFE7Form` | `SupportTalkFE7View` | 34 | 25 | -9 | -26.5% | ListParityHelper |
| Medium | `MonsterProbabilityForm` | `MonsterProbabilityViewerView` | 38 | 28 | -10 | -26.3% | ListParityHelper |
| Medium | `StatusUnitsMenuForm` | `StatusUnitsMenuView` | 19 | 14 | -5 | -26.3% | ListParityHelper |
| Medium | `MainSimpleMenuEventErrorForm` | `MainSimpleMenuEventErrorView` | 4 | 3 | -1 | -25.0% | Heuristic |
| Medium | `SongExchangeForm` | `SongExchangeView` | 4 | 3 | -1 | -25.0% | Heuristic |
| Medium | `ToolAllWorkSupportForm` | `ToolAllWorkSupportView` | 4 | 3 | -1 | -25.0% | Heuristic |
| Medium | `ToolUPSPatchSimpleForm` | `ToolUPSPatchSimpleView` | 4 | 3 | -1 | -25.0% | Heuristic |
| Low | `StatusParamForm` | `StatusParamView` | 27 | 21 | -6 | -22.2% | ListParityHelper |
| Low | `HowDoYouLikePatchForm` | `HowDoYouLikePatchView` | 5 | 4 | -1 | -20.0% | Heuristic |
| Low | `ItemStatBonusesForm` | `ItemStatBonusesViewerView` | 35 | 28 | -7 | -20.0% | ListParityHelper |
| Low | `MapTerrainNameForm` | `TerrainNameEditorView` | 10 | 8 | -2 | -20.0% | ListParityHelper |
| Low | `MenuDefinitionForm` | `MenuDefinitionView` | 35 | 28 | -7 | -20.0% | ListParityHelper |
| Low | `SongInstrumentDirectSoundForm` | `SongInstrumentDirectSoundView` | 15 | 12 | -3 | -20.0% | Heuristic |
| Low | `WorldMapPointForm` | `WorldMapPointView` | 51 | 41 | -10 | -19.6% | ListParityHelper |
| Low | `SupportTalkFE6Form` | `SupportTalkFE6View` | 26 | 21 | -5 | -19.2% | ListParityHelper |
| Low | `EventUnitFE7Form` | `EventUnitFE7View` | 66 | 54 | -12 | -18.2% | ListParityHelper |
| Low | `StatusRMenuForm` | `StatusRMenuView` | 28 | 23 | -5 | -17.9% | ListParityHelper |
| Low | `EventUnitFE6Form` | `EventUnitFE6View` | 64 | 54 | -10 | -15.6% | ListParityHelper |
| Low | `AITargetForm` | `AITargetView` | 52 | 44 | -8 | -15.4% | ListParityHelper |
| Low | `UnitsShortTextForm` | `UnitsShortTextView` | 13 | 11 | -2 | -15.4% | Heuristic |
| Low | `SoundBossBGMForm` | `SoundBossBGMViewerView` | 20 | 17 | -3 | -15.0% | ListParityHelper |
| Low | `AOERANGEForm` | `AOERANGEView` | 15 | 13 | -2 | -13.3% | Heuristic |
| Low | `ItemStatBonusesVennoForm` | `ItemStatBonusesVennoView` | 41 | 36 | -5 | -12.2% | Heuristic |
| Low | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` | 9 | 8 | -1 | -11.1% | Heuristic |
| Low | `OPClassAlphaNameFE6Form` | `OPClassAlphaNameFE6View` | 10 | 9 | -1 | -10.0% | ListParityHelper |
| Low | `RAMRewriteToolMAPForm` | `RAMRewriteToolMAPView` | 11 | 10 | -1 | -9.1% | Heuristic |
| Low | `ItemStatBonusesSkillSystemsForm` | `ItemStatBonusesSkillSystemsView` | 51 | 47 | -4 | -7.8% | Heuristic |
| Low | `StatusOptionForm` | `StatusOptionView` | 50 | 47 | -3 | -6.0% | ListParityHelper |
| Low | `MapSettingForm` | `MapSettingView` | 224 | 220 | -4 | -1.8% | ListParityHelper |
| Low | `LogForm` | `LogViewerView` | 3 | 3 | 0 | 0.0% | Heuristic |
| Low | `MapEditorAddMapChangeDialogForm` | `MapEditorAddMapChangeDialogView` | 6 | 6 | 0 | 0.0% | Heuristic |
| Low | `MapEditorMarSizeDialogForm` | `MapEditorMarSizeDialogView` | 4 | 4 | 0 | 0.0% | Heuristic |
| Low | `MapEditorResizeDialogForm` | `MapEditorResizeDialogView` | 19 | 19 | 0 | 0.0% | Heuristic |
| Low | `MapStyleEditorImportImageOptionForm` | `MapStyleEditorImportImageOptionView` | 5 | 5 | 0 | 0.0% | Heuristic |
| Low | `OpenLastSelectedFileForm` | `OpenLastSelectedFileView` | 3 | 3 | 0 | 0.0% | Heuristic |
| Low | `TextBadCharPopupForm` | `TextBadCharPopupView` | 5 | 5 | 0 | 0.0% | Heuristic |
| Low | `ToolBGMMuteDialogForm` | `ToolBGMMuteDialogView` | 4 | 4 | 0 | 0.0% | Heuristic |
| Low | `ToolClickWriteFloatControlPanelButtonForm` | `ToolClickWriteFloatControlPanelButtonView` | 4 | 4 | 0 | 0.0% | Heuristic |
| Low | `ToolEmulatorSetupMessageForm` | `ToolEmulatorSetupMessageView` | 3 | 3 | 0 | 0.0% | Heuristic |
| Low | `ToolUndoPopupDialogForm` | `ToolUndoPopupDialogView` | 5 | 5 | 0 | 0.0% | Heuristic |
| Low | `MenuExtendSplitMenuForm` | `MenuExtendSplitMenuView` | 27 | 28 | 1 | +3.7% | ListParityHelper |
| Low | `PointerToolForm` | `PointerToolView` | 31 | 33 | 2 | +6.5% | Heuristic |
| Low | `AIASMCALLTALKForm` | `AIASMCALLTALKView` | 12 | 13 | 1 | +8.3% | Heuristic |
| Low | `AIASMRangeForm` | `AIASMRangeView` | 12 | 13 | 1 | +8.3% | Heuristic |
| Low | `AIASMCoordinateForm` | `AIASMCoordinateView` | 11 | 12 | 1 | +9.1% | Heuristic |
| Low | `UbyteBitFlagForm` | `UbyteBitFlagView` | 11 | 12 | 1 | +9.1% | Heuristic |
| Low | `SupportUnitForm` | `SupportUnitEditorView` | 50 | 55 | 5 | +10.0% | ListParityHelper |
| Low | `UshortBitFlagForm` | `UshortBitFlagView` | 20 | 22 | 2 | +10.0% | Heuristic |
| Low | `UwordBitFlagForm` | `UwordBitFlagView` | 38 | 42 | 4 | +10.5% | Heuristic |
| Low | `RAMRewriteToolForm` | `RAMRewriteToolView` | 8 | 9 | 1 | +12.5% | Heuristic |
| Low | `PointerToolCopyToForm` | `PointerToolCopyToView` | 6 | 7 | 1 | +16.7% | Heuristic |
| Low | `ToolWorkSupportForm` | `ToolWorkSupportView` | 17 | 20 | 3 | +17.6% | Heuristic |
| Low | `MainSimpleMenuEventErrorIgnoreErrorForm` | `MainSimpleMenuEventErrorIgnoreErrorView` | 5 | 6 | 1 | +20.0% | Heuristic |
| Low | `SupportUnitFE6Form` | `SupportUnitFE6View` | 58 | 71 | 13 | +22.4% | ListParityHelper |
| Medium | `PatchFormUninstallDialogForm` | `PatchFormUninstallDialogView` | 4 | 5 | 1 | +25.0% | Heuristic |
| Medium | `SMEPromoListForm` | `SMEPromoListView` | 16 | 20 | 4 | +25.0% | Heuristic |
| Medium | `ToolWorkSupport_SelectUPSForm` | `ToolWorkSupport_SelectUPSView` | 4 | 5 | 1 | +25.0% | Heuristic |
| Medium | `HexEditorForm` | `HexEditorView` | 6 | 8 | 2 | +33.3% | Heuristic |
| Medium | `ToolExportEAEventForm` | `ToolExportEAEventView` | 12 | 16 | 4 | +33.3% | Heuristic |
| Medium | `ToolProblemReportSearchBackupForm` | `ToolProblemReportSearchBackupView` | 3 | 4 | 1 | +33.3% | Heuristic |
| Medium | `ToolProblemReportSearchSavForm` | `ToolProblemReportSearchSavView` | 3 | 4 | 1 | +33.3% | Heuristic |
| Medium | `ToolRunHintMessageForm` | `ToolRunHintMessageView` | 3 | 4 | 1 | +33.3% | Heuristic |
| Medium | `ToolThreeMargeCloseAlertForm` | `ToolThreeMargeCloseAlertView` | 3 | 4 | 1 | +33.3% | Heuristic |
| Medium | `ToolWorkSupport_UpdateQuestionDialogForm` | `ToolWorkSupport_UpdateQuestionDialogView` | 3 | 4 | 1 | +33.3% | Heuristic |
| Medium | `ToolDiffDebugSelectForm` | `ToolDiffDebugSelectView` | 11 | 15 | 4 | +36.4% | Heuristic |
| Medium | `PackedMemorySlotForm` | `PackedMemorySlotView` | 5 | 7 | 2 | +40.0% | Heuristic |
| Medium | `TextRefAddDialogForm` | `TextRefAddDialogView` | 5 | 7 | 2 | +40.0% | Heuristic |
| High | `CStringForm` | `CStringView` | 2 | 3 | 1 | +50.0% | Heuristic |
| High | `ErrorLongMessageDialogForm` | `ErrorLongMessageDialogView` | 2 | 3 | 1 | +50.0% | Heuristic |
| High | `PointerToolBatchInputForm` | `PointerToolBatchInputView` | 2 | 3 | 1 | +50.0% | Heuristic |
| High | `SongTrackImportSelectInstrumentForm` | `SongTrackImportSelectInstrumentView` | 6 | 9 | 3 | +50.0% | Heuristic |
| High | `ToolAutomaticRecoveryROMHeaderForm` | `ToolAutomaticRecoveryROMHeaderView` | 4 | 6 | 2 | +50.0% | Heuristic |
| High | `ToolUseFlagForm` | `ToolUseFlagView` | 2 | 3 | 1 | +50.0% | Heuristic |
| High | `DisASMDumpAllForm` | `DisASMDumpAllView` | 9 | 14 | 5 | +55.6% | Heuristic |
| High | `ToolChangeProjectnameForm` | `ToolChangeProjectnameView` | 4 | 7 | 3 | +75.0% | Heuristic |
| High | `DumpStructSelectToTextDialogForm` | `DumpStructSelectToTextDialogView` | 3 | 6 | 3 | +100.0% | Heuristic |
| High | `PaletteClipboardForm` | `PaletteClipboardView` | 4 | 8 | 4 | +100.0% | Heuristic |
| High | `EventScriptTemplateForm` | `EventScriptTemplateView` | 1 | 3 | 2 | +200.0% | Heuristic |
| High | `ProcsScriptForm` | `ProcsScriptView` | 1 | 3 | 2 | +200.0% | Heuristic |
| High | `ToolUnitTalkGroupForm` | `ToolUnitTalkGroupView` | 1 | 3 | 2 | +200.0% | Heuristic |
| High | `ToolThreeMargeForm` | `ToolThreeMargeView` | 6 | 20 | 14 | +233.3% | Heuristic |
| High | `ResourceForm` | `ResourceView` | 2 | 8 | 6 | +300.0% | Heuristic |
| High | `ToolASMEditForm` | `ToolASMEditView` | 1 | 4 | 3 | +300.0% | Heuristic |
| High | `AIScriptCategorySelectForm` | `AIScriptCategorySelectView` | 3 | 13 | 10 | +333.3% | Heuristic |
| High | `ProcsScriptCategorySelectForm` | `ProcsScriptCategorySelectView` | 3 | 13 | 10 | +333.3% | Heuristic |
| High | `GraphicsToolPatchMakerForm` | `GraphicsToolPatchMakerView` | 3 | 14 | 11 | +366.7% | Heuristic |

## Unmatched WinForms Counterparts

Paired by name/heuristic but WF Designer.cs file not found (renamed, removed,
or a typo in `ListParityHelper`). Not a real migration gap — the AV side just
happens to lack a directly-named WF counterpart on disk.

| WF Form (claimed) | AV View | AV controls | Match |
|---|---|---:|---|
| `BattleTerrainForm` | `BattleTerrainViewerView` | 40 | ListParityHelper |
| `OPClassDemoViewerForm` | `OPClassDemoViewerView` | 31 | ListParityHelper |
| `BattleBGForm` | `BattleBGViewerView` | 14 | ListParityHelper |
| `MapTileAnimationView(Avalonia)` | `MapTileAnimationView` | 12 | ListParityHelper |
| `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` | 11 | Heuristic |
| `EventScriptForm` | `EventScriptView` | 10 | Heuristic |
| `DisASMForm` | `DisASMView` | 8 | Heuristic |
| `OPClassFontViewerForm` | `OPClassFontViewerView` | 7 | ListParityHelper |
| `ToolSubtitleOverlayForm` | `ToolSubtitleOverlayView` | 7 | Heuristic |
| `ToolDecompileResultForm` | `ToolDecompileResultView` | 4 | Heuristic |
| `ImageUnitMoveIconForm` | `ImageUnitMoveIconView` | 3 | ListParityHelper |
| `ImageUnitWaitIconForm` | `ImageUnitWaitIconView` | 3 | ListParityHelper |

## Top-20 HIGH Gaps — Triage Notes

Manual notes below each heading describe what specific labels / controls
appear in the WinForms form but are missing from the Avalonia view. Fill
each section by grepping the Designer.cs for `.Text = "…"` initialisers and
cross-checking against the .axaml literals.

### EmulatorMemoryForm
WF count: **353** · AV count: **5** · Δ: **-348** (-98.6%).

**Status: intentional cross-platform stub.** `EmulatorMemoryView.axaml` ships a
"Platform Notice" explaining that emulator memory reading needs Windows P/Invoke
and is not available in the cross-platform port. The 353 WF controls are tabs
for Event / Procs / RAM / EventHistory inspectors that drive a running mGBA /
VBA process. Not a migration gap — flagged here for traceability.

### ImageBattleScreenForm
WF count: **133** · AV count: **3** · Δ: **-130** (-97.7%).

**Status: stub.** `ImageBattleScreenView.axaml` is an address-list-only shell
(21 lines, AV controls = AddressList + title `TextBlock` + address label).
WinForms ships a full battle-screen layout editor: 100+ NumericUpDown widgets
arranged in groups for BG palette indices, weapon-status icon offsets, sprite
positions, transparency tables, and a preview PictureBox. Migration of the
editor body is fully outstanding.

### MapSettingFE6Form
WF count: **126** · AV count: **3** · Δ: **-123** (-97.6%).

**Status: stub.** The FE6-specific map-settings editor in WinForms covers all
the per-chapter chapter-data fields (map id, tile config, music, world-map
node, victory cond, defeat cond, character data offsets, opening event …).
`MapSettingFE6View.axaml` is an empty address-list shell. Note: the generic
`MapSettingForm ↔ MapSettingView` pair (FE7/FE8) sits at -1.8 % and is fine —
only the FE6 variant is missing.

### WorldMapImageForm
WF count: **107** · AV count: **3** · Δ: **-104** (-97.2%).

**Status: stub.** `WorldMapImageView.axaml` is the address-list shell.
WinForms provides world-map graphic editing with palette index pickers,
tilemap preview, chapter-node anchor coordinates, and an image-import flow.
Body migration is outstanding.

### ImageTSAEditorForm
WF count: **100** · AV count: **3** · Δ: **-97** (-97.0%).

**Status: stub.** TSA (Tile Sub-Assembly) editor for chapter-title /
support-card / battle-talk backgrounds. The WinForms editor has a tile
picker grid, palette controls, hflip/vflip checkboxes, priority spinners,
and a preview canvas. Avalonia view body is missing entirely.

### ImageBattleAnimePalletForm
WF count: **99** · AV count: **3** · Δ: **-96** (-97.0%).

**Status: stub.** Battle-animation palette editor — WF has 16-colour
palette swatches, "extract pallet" import buttons, transparency colour
picker, and a per-frame palette preview. AV view is the address-list shell.

### ImagePalletForm
WF count: **98** · AV count: **3** · Δ: **-95** (-96.9%).

**Status: stub.** Generic 16-colour palette editor with HSV/RGB pickers,
"copy to clipboard", "paste from clipboard", undo/redo of palette edits.
AV view is the address-list shell. Note: `PaletteClipboardView` (paired
elsewhere) covers a small slice — the main palette editor is still missing.

### EDFE7Form
WF count: **81** · AV count: **3** · Δ: **-78** (-96.3%).

**Status: stub.** FE7-specific ending-screen editor (per-character epilogue
text picker, epithet selector, optional spouse linkage, sprite frame
selection). AV view is the address-list shell.

### ClassForm
WF count: **211** · AV count: **8** · Δ: **-203** (-96.2%).

**Status: pair-via-`ClassFE6View`.** `ClassForm` is mapped twice in
`ListParityHelper`: once to the rich `ClassEditorView` (-31 % delta, healthy)
and once to the FE6-stub `ClassFE6View`. This row is the FE6 stub, which is
the same chrome-only shell as the other FE6 stubs. The base ClassEditorView
side is healthy; only the FE6-tail variant is outstanding.

### OPClassDemoForm
WF count: **63** · AV count: **3** · Δ: **-60** (-95.2%).

**Status: stub.** Opening cinematic class-demo editor — frame selection,
class id picker, position offsets, mounted-vs-foot variant. AV
`ClassOPDemoView` is the address-list shell.

### EventBattleTalkFE7Form
WF count: **62** · AV count: **3** · Δ: **-59** (-95.2%).

**Status: stub.** FE7 battle-talk dialogue editor (talk id, target unit /
class match, text pointer, conditions). AV view is the address-list shell.

### EventBattleTalkFE6Form
WF count: **61** · AV count: **3** · Δ: **-58** (-95.1%).

**Status: stub.** FE6 battle-talk dialogue editor. Same pattern as the
FE7 sibling above. AV view is the address-list shell.

### EventHaikuFE7Form
WF count: **60** · AV count: **3** · Δ: **-57** (-95.0%).

**Status: stub.** FE7 in-chapter epigraph ("haiku") editor — text pointer,
trigger conditions, position offsets. AV view is the address-list shell.

### MapStyleEditorForm
WF count: **153** · AV count: **8** · Δ: **-145** (-94.8%).

**Status: stub.** Map style editor for chapter-tileset chrome — tile-anim
indices, BG-style pointers, palette references. WinForms has multi-tab
layout with palette previews; AV view is a near-empty shell with only
8 controls (~5% coverage).

### ErrorPaletteTransparentForm
WF count: **50** · AV count: **3** · Δ: **-47** (-94.0%).

**Status: stub.** Error-dialog showing transparent-colour mismatches in
imported palettes. WF version has a palette-swatch comparator panel and
auto-fix button. AV view body is missing.

### ToolLZ77Form
WF count: **50** · AV count: **3** · Δ: **-47** (-94.0%).

**Status: stub.** LZ77 compress / decompress utility (file-in / data-bytes,
output preview, "save to ROM at addr" button). AV view body is missing.
Note: the CLI has `--lz77 --compress / --decompress` flags as a workaround.

### ImagePortraitImporterForm
WF count: **42** · AV count: **3** · Δ: **-39** (-92.9%).

**Status: stub.** Bulk-import portraits from a directory (FE-Repo flow).
WF dialog has per-character mappings, palette-preserve toggle, chibi-vs-full
selector. AV view body is missing. CLI workaround: `--import-portrait-all`.

### AIScriptForm
WF count: **37** · AV count: **3** · Δ: **-34** (-91.9%).

**Status: stub.** Enemy AI script editor (opcode list, parameter
spinners, target unit/class pickers). Generic AV `AIScriptView` is the
address-list shell — `AIScriptCategorySelectView`, `AIMapSettingView`, and
the various `AIPerform*View` siblings handle slices of the workflow but
the central script editor body is outstanding.

### ImageMagicCSACreatorForm
WF count: **37** · AV count: **3** · Δ: **-34** (-91.9%).

**Status: stub.** Magic-effect CSA (Compressed Sprite Animation) creator —
frame builder, palette picker, hflip/vflip per cell, preview. AV view body
is missing.

### ImageMagicFEditorForm
WF count: **37** · AV count: **3** · Δ: **-34** (-91.9%).

**Status: stub.** FEditor-format magic animation import/export tool.
Sibling to the CSA creator. AV view body is missing.

## Unpaired Orphans

These editors have only one side; they need manual triage to decide whether
the missing counterpart is expected (e.g., FE6-only forms not yet ported) or
a genuine gap.

### WinForms-only (15)

| WF Form | WF controls |
|---|---:|
| `ClassFE6Form` | 173 |
| `ClassOPDemoForm` | 71 |
| `ClassOPFontForm` | 16 |
| `EventScriptFormCategorySelectForm` | 4 |
| `FERepoMusicBrowserForm` | 4 |
| `FERepoResourceBrowserForm` | 4 |
| `ImageBattleTerrainForm` | 37 |
| `ImageSystemHoverColorForm` | 21 |
| `ImageSystemIconForm` | 122 |
| `MapStyleEditorFormWarningVanillaTileOverraideForm` | 6 |
| `MoveToFreeSapceForm` | 41 |
| `OptionForm` | 241 |
| `PatchForm` | 102 |
| `TextScriptFormCategorySelectForm` | 3 |
| `ToolSubtitleSetingDialogForm` | 9 |

### Avalonia-only (55)

| AV View | AV controls |
|---|---:|
| `DataExportView` | 5 |
| `ErrorUnknownROMView` | 10 |
| `EventBattleTalkMainView` | 3 |
| `EventCondMainView` | 3 |
| `EventScriptCategorySelectView` | 4 |
| `EventScriptInnerView` | 3 |
| `EventScriptMainView` | 3 |
| `EventScriptPopupView` | 12 |
| `EventTemplateImplView` | 3 |
| `EventUnitSimView` | 3 |
| `GrowSimulatorView` | 3 |
| `HexEditorJumpView` | 3 |
| `HexEditorMarkView` | 3 |
| `HexEditorSearchView` | 6 |
| `ImageFormRefViewerView` | 3 |
| `ImageViewerView` | 5 |
| `InterpolatedPictureBoxViewerView` | 3 |
| `ItemEffectivenessMainView` | 3 |
| `MainSimpleMenuView` | 3 |
| `MapChangeMainView` | 3 |
| `MapEditorAddMapChangeView` | 3 |
| `MapEditorMarSizeView` | 3 |
| `MapEditorResizeView` | 3 |
| `MapExitPointMainView` | 3 |
| `MapPictureBoxViewerView` | 3 |
| `MapPointerMainView` | 3 |
| `MapPointerNewPLISTView` | 3 |
| `MapSettingDifficultyDialogView` | 10 |
| `MapSettingDifficultyExtraView` | 3 |
| `MapSettingMainView` | 3 |
| `MapStyleEditorAppendView` | 3 |
| `MapStyleEditorWarningOverrideView` | 4 |
| `MapStyleEditorWarningView` | 3 |
| `MapTerrainBGLookupView` | 6 |
| `MapTerrainFloorLookupView` | 6 |
| `MapTerrainNameView` | 8 |
| `MoveToFreeSpaceView` | 10 |
| `NotifyDirectInjectionView` | 3 |
| `NotifyPleaseWaitView` | 3 |
| `NotifyWriteView` | 3 |
| `OAMSpriteViewerView` | 13 |
| `OPClassAlphaNameFE6ExtraView` | 3 |
| `OptionsView` | 86 |
| `PatchManagerView` | 24 |
| `PatchUninstallDialogView` | 3 |
| `ScriptCommandPickerView` | 7 |
| `SkillSystemsCSkillRechainView` | 3 |
| `SongTableMainView` | 3 |
| `SystemHoverColorViewerView` | 5 |
| `SystemIconViewerView` | 14 |
| `TextEscapeEditorView` | 3 |
| `TextMainView` | 3 |
| `TextScriptCategorySelectView` | 4 |
| `ToolSubtitleSettingDialogView` | 12 |
| `UnitMainView` | 3 |

