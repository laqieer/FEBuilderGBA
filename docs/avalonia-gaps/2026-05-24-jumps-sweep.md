---
generated: "2026-05-23T18:55:41Z"
git-sha: ef359d2a5
sweep-type: jumps
---

# Avalonia vs WinForms — Jump/Navigation Parity Sweep

This report cross-references WinForms `InputFormRef.JumpForm<T>(addr)`
callsites against Avalonia `INavigationTargetSource` manifests to
surface every cross-editor navigation gap.

**Methodology:**

- WinForms callsites: Roslyn scans `FEBuilderGBA/**/*.cs` for
  `InputFormRef.JumpForm<T>(…)` / `JumpFormLow<T>(…)` invocations.
  Each match records (enclosing-class, target-type) for cross-ref.
- Avalonia manifests: Reflection over the loaded Avalonia assembly
  for every concrete `INavigationTargetSource`. Each VM is instantiated
  via its parameterless constructor (wrapped in try/catch); the
  `GetNavigationTargets()` result feeds the cross-ref.
- Pairing: `ListParityHelper.GetMapping(name)` maps AV view names ↔
  WF form names so the two sides align without manual lookups.

**Status legend:**

- `Match` — WF callsite + AV manifest agree on the (source, target) pair.
- `MissingAvManifest` — WF has the jump, AV does not (the backlog).
- `NoWfCallsite` — AV manifest declares a jump WF doesn't have.
- `KnownGap` — AV manifest row carries an open-issue `IssueRef`.

Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/JumpParityScanner.cs`.
Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-jumps --out=<path>`.

## Summary

| Metric | Count |
|---|---:|
| Total rows | 429 |
| Match | 52 |
| MissingAvManifest (backlog) | 334 |
| NoWfCallsite | 35 |
| KnownGap (issue-tagged) | 8 |

## Known Gaps (tracked by open issues)

| Source Form | Source View | Command | Target WF | Target AV | Issue |
|---|---|---|---|---|---|
| `CCBranchForm` | `CCBranchEditorView` | `JumpToPromotionClass1` | `ClassForm` | `ClassEditorView` | [#365](https://github.com/laqieer/FEBuilderGBA/issues/365) |
| `CCBranchForm` | `CCBranchEditorView` | `JumpToPromotionClass2` | `ClassForm` | `ClassEditorView` | [#365](https://github.com/laqieer/FEBuilderGBA/issues/365) |
| `ImageMagicFEditorForm` | `ImageMagicFEditorView` | `JumpToToolAnimationCreator` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` | [#500](https://github.com/laqieer/FEBuilderGBA/issues/500) |
| `ImageMapActionAnimationForm` | `ImageMapActionAnimationView` | `JumpToAnimationCreator` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` | [#500](https://github.com/laqieer/FEBuilderGBA/issues/500) |
| `SkillConfigCSkillSystem09xForm` | `SkillConfigFE8UCSkillSys09xView` | `JumpToAnimationCreator` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` | [#500](https://github.com/laqieer/FEBuilderGBA/issues/500) |
| `SkillConfigSkillSystemForm` | `SkillConfigSkillSystemView` | `JumpToAnimationCreator` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` | [#500](https://github.com/laqieer/FEBuilderGBA/issues/500) |
| `SupportTalkForm` | `SupportTalkView` | `JumpToPartner1` | `UnitForm` | `UnitEditorView` | [#360](https://github.com/laqieer/FEBuilderGBA/issues/360) |
| `SupportTalkForm` | `SupportTalkView` | `JumpToPartner2` | `UnitForm` | `UnitEditorView` | [#360](https://github.com/laqieer/FEBuilderGBA/issues/360) |

## Missing AV Manifest (backlog — WF has the jump, AV does not)

| Source Form | Source View | Command | Target WF | Target AV |
|---|---|---|---|---|
| `DisASMInnerControl` | `—` | `—` | `DisASMDumpAllForm` | `DisASMDumpAllView` |
| `DisASMInnerControl` | `—` | `—` | `HexEditorForm` | `HexEditorView` |
| `DisASMInnerControl` | `—` | `—` | `ToolASMEditForm` | `ToolASMEditView` |
| `DisASMInnerControl` | `—` | `—` | `ToolASMInsertForm` | `ToolASMInsertView` |
| `DisASMInnerControl` | `—` | `—` | `ToolDecompileResultForm` | `ToolDecompileResultView` |
| `EventScriptInnerControl` | `—` | `—` | `EventScriptFormCategorySelectForm` | `—` |
| `EventScriptInnerControl` | `—` | `—` | `AIASMCALLTALKForm` | `AIASMCALLTALKView` |
| `EventScriptInnerControl` | `—` | `—` | `AIASMCoordinateForm` | `AIASMCoordinateView` |
| `EventScriptInnerControl` | `—` | `—` | `AIASMRangeForm` | `AIASMRangeView` |
| `EventScriptInnerControl` | `—` | `—` | `CStringForm` | `CStringView` |
| `EventScriptInnerControl` | `—` | `—` | `ClassForm` | `ClassEditorView` |
| `EventScriptInnerControl` | `—` | `—` | `ClassFE6Form` | `ClassFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `ClassForm` | `ClassFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `DisASMForm` | `DisASMView` |
| `EventScriptInnerControl` | `—` | `—` | `EventBattleDataFE7Form` | `EventBattleDataFE7View` |
| `EventScriptInnerControl` | `—` | `—` | `EventMoveDataFE7Form` | `EventMoveDataFE7View` |
| `EventScriptInnerControl` | `—` | `—` | `EventScriptTemplateForm` | `EventScriptTemplateView` |
| `EventScriptInnerControl` | `—` | `—` | `EventTalkGroupFE7Form` | `EventTalkGroupFE7View` |
| `EventScriptInnerControl` | `—` | `—` | `EventUnitColorForm` | `EventUnitColorView` |
| `EventScriptInnerControl` | `—` | `—` | `EventUnitFE6Form` | `EventUnitFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `EventUnitFE7Form` | `EventUnitFE7View` |
| `EventScriptInnerControl` | `—` | `—` | `EventUnitForm` | `EventUnitView` |
| `EventScriptInnerControl` | `—` | `—` | `ImageBGForm` | `ImageBGView` |
| `EventScriptInnerControl` | `—` | `—` | `ImageCGFE7UForm` | `ImageCGFE7UView` |
| `EventScriptInnerControl` | `—` | `—` | `ImageCGForm` | `ImageCGView` |
| `EventScriptInnerControl` | `—` | `—` | `ImageMapActionAnimationForm` | `ImageMapActionAnimationView` |
| `EventScriptInnerControl` | `—` | `—` | `ImagePortraitFE6Form` | `ImagePortraitFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `ImagePortraitForm` | `ImagePortraitView` |
| `EventScriptInnerControl` | `—` | `—` | `ItemForm` | `ItemEditorView` |
| `EventScriptInnerControl` | `—` | `—` | `MapSettingForm` | `MapSettingView` |
| `EventScriptInnerControl` | `—` | `—` | `MenuExtendSplitMenuForm` | `MenuExtendSplitMenuView` |
| `EventScriptInnerControl` | `—` | `—` | `PackedMemorySlotForm` | `PackedMemorySlotView` |
| `EventScriptInnerControl` | `—` | `—` | `PointerToolCopyToForm` | `PointerToolCopyToView` |
| `EventScriptInnerControl` | `—` | `—` | `ImagePortraitForm` | `PortraitViewerView` |
| `EventScriptInnerControl` | `—` | `—` | `ProcsScriptForm` | `ProcsScriptView` |
| `EventScriptInnerControl` | `—` | `—` | `SongTableForm` | `SongTableView` |
| `EventScriptInnerControl` | `—` | `—` | `SoundRoomFE6Form` | `SoundRoomFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `SoundRoomForm` | `SoundRoomViewerView` |
| `EventScriptInnerControl` | `—` | `—` | `StatusOptionForm` | `StatusOptionView` |
| `EventScriptInnerControl` | `—` | `—` | `TextForm` | `TextViewerView` |
| `EventScriptInnerControl` | `—` | `—` | `ToolFlagNameForm` | `ToolFlagNameView` |
| `EventScriptInnerControl` | `—` | `—` | `UbyteBitFlagForm` | `UbyteBitFlagView` |
| `EventScriptInnerControl` | `—` | `—` | `UnitForm` | `UnitEditorView` |
| `EventScriptInnerControl` | `—` | `—` | `UnitFE6Form` | `UnitFE6View` |
| `EventScriptInnerControl` | `—` | `—` | `UnitFE7Form` | `UnitFE7View` |
| `EventScriptInnerControl` | `—` | `—` | `UnitsShortTextForm` | `UnitsShortTextView` |
| `EventScriptInnerControl` | `—` | `—` | `UshortBitFlagForm` | `UshortBitFlagView` |
| `EventScriptInnerControl` | `—` | `—` | `UwordBitFlagForm` | `UwordBitFlagView` |
| `EventScriptInnerControl` | `—` | `—` | `WorldMapPathForm` | `WorldMapPathEditorView` |
| `EventScriptInnerControl` | `—` | `—` | `WorldMapPathForm` | `WorldMapPathView` |
| `EventScriptInnerControl` | `—` | `—` | `WorldMapPointForm` | `WorldMapPointView` |
| `ImageBattleTerrainForm` | `—` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImageFormRef` | `—` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageFormRef` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageFormRef` | `—` | `—` | `ErrorTSAErrorForm` | `ErrorTSAErrorView` |
| `ImageFormRef` | `—` | `—` | `ErrorTSAErrorForm` | `ErrorTSAErrorView` |
| `ImageFormRef` | `—` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImageFormRef` | `—` | `—` | `ImagePalletForm` | `ImagePalletView` |
| `ImageFormRef` | `—` | `—` | `ImageTSAEditorForm` | `ImageTSAEditorView` |
| `ImageSystemIconForm` | `—` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImageSystemIconForm` | `—` | `—` | `ImagePalletForm` | `ImagePalletView` |
| `ImageSystemIconForm` | `—` | `—` | `ImageSystemAreaForm` | `ImageSystemAreaView` |
| `ImageSystemIconForm` | `—` | `—` | `ImageSystemAreaForm` | `ImageSystemAreaView` |
| `ImageSystemIconForm` | `—` | `—` | `ImageSystemAreaForm` | `ImageSystemAreaView` |
| `ImageSystemIconForm` | `—` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageSystemIconForm` | `—` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageSystemIconForm` | `—` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageUnitMoveIconFrom` | `—` | `—` | `ImageSystemIconForm` | `—` |
| `ImageUnitMoveIconFrom` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageUnitMoveIconFrom` | `—` | `—` | `SoundFootStepsForm` | `SoundFootStepsViewerView` |
| `ImageUnitWaitIconFrom` | `—` | `—` | `ImageSystemIconForm` | `—` |
| `ImageUnitWaitIconFrom` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageUtil` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageUtil` | `—` | `—` | `ErrorPaletteTransparentForm` | `ErrorPaletteTransparentView` |
| `ImageUtil` | `—` | `—` | `ErrorTSAErrorForm` | `ErrorTSAErrorView` |
| `ImageUtilMap` | `—` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `ImageUtilMap` | `—` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `ImageUtilMap` | `—` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `ImageUtilMap` | `—` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `ImportOAM` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImportOAM` | `—` | `—` | `ErrorPaletteTransparentForm` | `ErrorPaletteTransparentView` |
| `MainFormUtil` | `—` | `—` | `OverraideCheckWithErrorDialog` | `—` |
| `MainFormUtil` | `—` | `—` | `OverraideCheckWithErrorDialog` | `—` |
| `MainFormUtil` | `—` | `—` | `ToolDisasmSourceCode` | `—` |
| `MainFormUtil` | `—` | `—` | `EmulatorMemoryForm` | `EmulatorMemoryView` |
| `MainFormUtil` | `—` | `—` | `HexEditorForm` | `HexEditorView` |
| `MainFormUtil` | `—` | `—` | `ToolFELintForm` | `ToolFELintView` |
| `MainFormUtil` | `—` | `—` | `ToolFELintForm` | `ToolFELintView` |
| `MainFormUtil` | `—` | `—` | `ToolInitWizardForm` | `ToolInitWizardView` |
| `MainFormUtil` | `—` | `—` | `ToolUPSOpenSimpleForm` | `ToolUPSOpenSimpleView` |
| `MainFormUtil` | `—` | `—` | `WelcomeForm` | `WelcomeView` |
| `MapStyleEditorFormWarningVanillaTileOverraideForm` | `—` | `—` | `MapStyleEditorFormWarningVanillaTileOverraideForm` | `—` |
| `PaletteFormRef` | `—` | `—` | `ErrorPaletteMissMatchForm` | `ErrorPaletteMissMatchView` |
| `PaletteFormRef` | `—` | `—` | `ErrorPaletteMissMatchForm` | `ErrorPaletteMissMatchView` |
| `PaletteFormRef` | `—` | `—` | `PaletteChangeColorsForm` | `PaletteChangeColorsView` |
| `PaletteFormRef` | `—` | `—` | `PaletteClipboardForm` | `PaletteClipboardView` |
| `PaletteFormRef` | `—` | `—` | `PaletteSwapForm` | `PaletteSwapView` |
| `PatchMainFilter` | `—` | `—` | `PatchForm` | `PatchManagerView` |
| `ProcsScriptInnerControl` | `—` | `—` | `AIUnitsForm` | `AIUnitsView` |
| `ProcsScriptInnerControl` | `—` | `—` | `CStringForm` | `CStringView` |
| `ProcsScriptInnerControl` | `—` | `—` | `DisASMForm` | `DisASMView` |
| `ProcsScriptInnerControl` | `—` | `—` | `PointerToolCopyToForm` | `PointerToolCopyToView` |
| `ProcsScriptInnerControl` | `—` | `—` | `ProcsScriptCategorySelectForm` | `ProcsScriptCategorySelectView` |
| `ProcsScriptInnerControl` | `—` | `—` | `ProcsScriptCategorySelectForm` | `ProcsScriptCategorySelectView` |
| `ProcsScriptInnerControl` | `—` | `—` | `ProcsScriptForm` | `ProcsScriptView` |
| `Program` | `—` | `—` | `ErorrUnknownROM` | `—` |
| `Program` | `—` | `—` | `MainFE0Form` | `—` |
| `Program` | `—` | `—` | `MainFE6Form` | `—` |
| `Program` | `—` | `—` | `MainFE7Form` | `—` |
| `Program` | `—` | `—` | `MainFE8Form` | `—` |
| `Program` | `—` | `—` | `MainSimpleMenuForm` | `—` |
| `Program` | `—` | `—` | `ErrorReportForm` | `ErrorReportView` |
| `Program` | `—` | `—` | `WelcomeForm` | `WelcomeView` |
| `Program` | `—` | `—` | `WelcomeForm` | `WelcomeView` |
| `R` | `—` | `—` | `ErrorLongMessageDialogForm` | `ErrorLongMessageDialogView` |
| `R` | `—` | `—` | `ErrorLongMessageDialogForm` | `ErrorLongMessageDialogView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `ImageBattleAnimeForm` | `ImageBattleAnimeView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `ImageMagicCSACreatorForm` | `ImageMagicCSACreatorView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `ImageMagicFEditorForm` | `ImageMagicFEditorView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `SkillConfigFE8NVer2SkillForm` | `SkillConfigFE8NVer2SkillView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `SkillConfigSkillSystemForm` | `SkillConfigSkillSystemView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `SongTableForm` | `SongTableView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `SongTableForm` | `SongTableView` |
| `ToolAnimationCreatorUserControl` | `—` | `—` | `SongTableForm` | `SongTableView` |
| `ToolSubtitleSetingDialogForm` | `—` | `—` | `ToolSubtitleSetingDialogForm` | `—` |
| `ToolSubtitleSetingDialogForm` | `—` | `—` | `ToolSubtitleOverlayForm` | `ToolSubtitleOverlayView` |
| `UpdateCheck` | `—` | `—` | `ToolUpdateDialogForm` | `ToolUpdateDialogView` |
| `UpdateCheck` | `—` | `—` | `ToolUpdateDialogForm` | `ToolUpdateDialogView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIASMCALLTALKForm` | `AIASMCALLTALKView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIASMCoordinateForm` | `AIASMCoordinateView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIASMRangeForm` | `AIASMRangeView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIScriptCategorySelectForm` | `AIScriptCategorySelectView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIScriptCategorySelectForm` | `AIScriptCategorySelectView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AITilesForm` | `AITilesView` |
| `AIScriptForm` | `AIScriptView` | `—` | `AIUnitsForm` | `AIUnitsView` |
| `AIScriptForm` | `AIScriptView` | `—` | `ClassForm` | `ClassEditorView` |
| `AIScriptForm` | `AIScriptView` | `—` | `ClassFE6Form` | `ClassFE6View` |
| `AIScriptForm` | `AIScriptView` | `—` | `ClassForm` | `ClassFE6View` |
| `AIScriptForm` | `AIScriptView` | `—` | `DisASMForm` | `DisASMView` |
| `AIScriptForm` | `AIScriptView` | `—` | `PointerToolCopyToForm` | `PointerToolCopyToView` |
| `AIScriptForm` | `AIScriptView` | `—` | `UnitForm` | `UnitEditorView` |
| `AIScriptForm` | `AIScriptView` | `—` | `UnitFE6Form` | `UnitFE6View` |
| `AIScriptForm` | `AIScriptView` | `—` | `UnitFE7Form` | `UnitFE7View` |
| `ImageChapterTitleForm` | `ChapterTitleViewerView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageChapterTitleForm` | `ChapterTitleViewerView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageChapterTitleForm` | `ChapterTitleViewerView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ClassForm` | `ClassEditorView` | `—` | `CCBranchForm` | `CCBranchEditorView` |
| `ClassForm` | `ClassEditorView` | `—` | `PatchForm` | `PatchManagerView` |
| `ClassFE6Form` | `ClassFE6View` | `—` | `PatchForm` | `PatchManagerView` |
| `ClassForm` | `ClassFE6View` | `—` | `CCBranchForm` | `CCBranchEditorView` |
| `ClassForm` | `ClassFE6View` | `—` | `PatchForm` | `PatchManagerView` |
| `DisASMDumpAllArgGrepForm` | `DisASMDumpAllArgGrepView` | `—` | `DumpStructSelectToTextDialogForm` | `DumpStructSelectToTextDialogView` |
| `DisASMDumpAllForm` | `DisASMDumpAllView` | `—` | `DisASMDumpAllArgGrepForm` | `DisASMDumpAllArgGrepView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `EventScriptForm` | `EventScriptView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `EventScriptForm` | `EventScriptView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `EventScriptForm` | `EventScriptView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `HexEditorForm` | `HexEditorView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `MapChangeForm` | `MapChangeView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `ProcsScriptForm` | `ProcsScriptView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `RAMRewriteToolMAPForm` | `RAMRewriteToolMAPView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `RAMRewriteToolMAPForm` | `RAMRewriteToolMAPView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `RAMRewriteToolForm` | `RAMRewriteToolView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `SongTableForm` | `SongTableView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `TextForm` | `TextViewerView` |
| `EmulatorMemoryForm` | `EmulatorMemoryView` | `—` | `ToolBGMMuteDialogForm` | `ToolBGMMuteDialogView` |
| `EventAssemblerForm` | `EventAssemblerView` | `—` | `PatchForm` | `PatchManagerView` |
| `EventCondForm` | `EventCondView` | `—` | `EventScriptForm` | `EventScriptView` |
| `EventCondForm` | `EventCondView` | `—` | `EventUnitFE6Form` | `EventUnitFE6View` |
| `EventCondForm` | `EventCondView` | `—` | `EventUnitFE7Form` | `EventUnitFE7View` |
| `EventCondForm` | `EventCondView` | `—` | `EventUnitForm` | `EventUnitView` |
| `EventCondForm` | `EventCondView` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `EventUnitFE6Form` | `EventUnitFE6View` | `—` | `EventBattleTalkFE6Form` | `EventBattleTalkFE6View` |
| `EventUnitFE6Form` | `EventUnitFE6View` | `—` | `EventHaikuFE6Form` | `EventHaikuFE6View` |
| `GraphicsToolForm` | `GraphicsToolView` | `—` | `GraphicsToolPatchMakerForm` | `GraphicsToolPatchMakerView` |
| `GraphicsToolForm` | `GraphicsToolView` | `—` | `ImagePalletForm` | `ImagePalletView` |
| `GraphicsToolForm` | `GraphicsToolView` | `—` | `ImageTSAEditorForm` | `ImageTSAEditorView` |
| `HexEditorForm` | `HexEditorView` | `—` | `HexEditorJump` | `—` |
| `HexEditorForm` | `HexEditorView` | `—` | `HexEditorMark` | `—` |
| `HexEditorForm` | `HexEditorView` | `—` | `HexEditorSearch` | `—` |
| `HexEditorForm` | `HexEditorView` | `—` | `DisASMForm` | `DisASMView` |
| `HowDoYouLikePatch2Form` | `HowDoYouLikePatch2View` | `—` | `HowDoYouLikePatch2Form` | `HowDoYouLikePatch2View` |
| `HowDoYouLikePatch2Form` | `HowDoYouLikePatch2View` | `—` | `PatchForm` | `PatchManagerView` |
| `HowDoYouLikePatch2Form` | `HowDoYouLikePatch2View` | `—` | `PatchForm` | `PatchManagerView` |
| `HowDoYouLikePatchForm` | `HowDoYouLikePatchView` | `—` | `HowDoYouLikePatchForm` | `HowDoYouLikePatchView` |
| `HowDoYouLikePatchForm` | `HowDoYouLikePatchView` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageBattleAnimeForm` | `ImageBattleAnimeView` | `—` | `ImageBattleAnimePalletForm` | `ImageBattleAnimePalletView` |
| `ImageBattleAnimeForm` | `ImageBattleAnimeView` | `—` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` |
| `ImageCGFE7UForm` | `ImageCGFE7UView` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageCGForm` | `ImageCGView` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageChapterTitleFE7Form` | `ImageChapterTitleFE7View` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ImageGenericEnemyPortraitForm` | `ImageGenericEnemyPortraitView` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageMagicCSACreatorForm` | `ImageMagicCSACreatorView` | `—` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` |
| `ImagePortraitFE6Form` | `ImagePortraitFE6View` | `—` | `ImagePalletForm` | `ImagePalletView` |
| `ImagePortraitFE6Form` | `ImagePortraitFE6View` | `—` | `ImagePortraitImporterForm` | `ImagePortraitImporterView` |
| `ImageRomAnimeForm` | `ImageRomAnimeView` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImageTSAAnimeForm` | `ImageTSAAnimeView` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageTSAAnimeForm` | `ImageTSAAnimeView` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `ItemForm` | `ItemEditorView` | `—` | `ItemWeaponEffectForm` | `ItemWeaponEffectViewerView` |
| `ItemForm` | `ItemEditorView` | `—` | `PatchForm` | `PatchManagerView` |
| `ItemForm` | `ItemEditorView` | `—` | `PatchForm` | `PatchManagerView` |
| `ItemFE6Form` | `ItemFE6View` | `—` | `ItemWeaponEffectForm` | `ItemWeaponEffectViewerView` |
| `ItemFE6Form` | `ItemFE6View` | `—` | `PatchForm` | `PatchManagerView` |
| `ImageItemIconForm` | `ItemIconViewerView` | `—` | `ImageSystemIconForm` | `—` |
| `ImageItemIconForm` | `ItemIconViewerView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `ItemPromotionForm` | `ItemPromotionViewerView` | `—` | `PatchForm` | `PatchManagerView` |
| `ItemStatBonusesSkillSystemsForm` | `ItemStatBonusesSkillSystemsView` | `—` | `ItemForm` | `ItemEditorView` |
| `ItemStatBonusesSkillSystemsForm` | `ItemStatBonusesSkillSystemsView` | `—` | `ItemFE6Form` | `ItemFE6View` |
| `ItemStatBonusesVennoForm` | `ItemStatBonusesVennoView` | `—` | `ItemForm` | `ItemEditorView` |
| `ItemStatBonusesVennoForm` | `ItemStatBonusesVennoView` | `—` | `ItemFE6Form` | `ItemFE6View` |
| `ItemStatBonusesForm` | `ItemStatBonusesViewerView` | `—` | `ItemForm` | `ItemEditorView` |
| `ItemStatBonusesForm` | `ItemStatBonusesViewerView` | `—` | `ItemFE6Form` | `ItemFE6View` |
| `MapChangeForm` | `MapChangeView` | `—` | `MapEditorForm` | `MapEditorView` |
| `MapChangeForm` | `MapChangeView` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `MapEditorForm` | `MapEditorView` | `—` | `MapChangeForm` | `MapChangeView` |
| `MapEditorForm` | `MapEditorView` | `—` | `MapEditorAddMapChangeDialogForm` | `MapEditorAddMapChangeDialogView` |
| `MapEditorForm` | `MapEditorView` | `—` | `MapEditorMarSizeDialogForm` | `MapEditorMarSizeDialogView` |
| `MapEditorForm` | `MapEditorView` | `—` | `MapEditorResizeDialogForm` | `MapEditorResizeDialogView` |
| `MapEditorForm` | `MapEditorView` | `—` | `MapStyleEditorForm` | `MapStyleEditorView` |
| `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` | `—` | `MapPointerForm` | `MapPointerView` |
| `MapSettingFE6Form` | `MapSettingFE6View` | `—` | `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` |
| `MapSettingFE7UForm` | `MapSettingFE7UView` | `—` | `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` |
| `MapSettingFE7Form` | `MapSettingFE7View` | `—` | `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` |
| `MapSettingForm` | `MapSettingView` | `—` | `EventScriptForm` | `EventScriptView` |
| `MapSettingForm` | `MapSettingView` | `—` | `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` |
| `MapStyleEditorAppendPopupForm` | `MapStyleEditorAppendPopupView` | `—` | `MapPointerForm` | `MapPointerView` |
| `MapStyleEditorForm` | `MapStyleEditorView` | `—` | `MapStyleEditorImportImageOptionForm` | `MapStyleEditorImportImageOptionView` |
| `MapTileAnimation1Form` | `MapTileAnimation1View` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `MapTileAnimation2Form` | `MapTileAnimation2View` | `—` | `MapPointerNewPLISTPopupForm` | `MapPointerNewPLISTPopupView` |
| `MonsterWMapProbabilityForm` | `MonsterWMapProbabilityViewerView` | `—` | `EventScriptForm` | `EventScriptView` |
| `MonsterWMapProbabilityForm` | `MonsterWMapProbabilityViewerView` | `—` | `EventScriptForm` | `EventScriptView` |
| `OPClassDemoFE7Form` | `OPClassDemoFE7View` | `—` | `GraphicsToolForm` | `GraphicsToolView` |
| `PatchForm` | `PatchManagerView` | `—` | `PatchFilterExForm` | `PatchFilterExView` |
| `PatchForm` | `PatchManagerView` | `—` | `PatchFormUninstallDialogForm` | `PatchFormUninstallDialogView` |
| `PointerToolCopyToForm` | `PointerToolCopyToView` | `—` | `HexEditorForm` | `HexEditorView` |
| `ImagePortraitForm` | `PortraitViewerView` | `—` | `ImagePalletForm` | `ImagePalletView` |
| `ImagePortraitForm` | `PortraitViewerView` | `—` | `ImagePortraitImporterForm` | `ImagePortraitImporterView` |
| `ImagePortraitForm` | `PortraitViewerView` | `—` | `UnitIncreaseHeightForm` | `UnitIncreaseHeightView` |
| `SkillConfigFE8NVer2SkillForm` | `SkillConfigFE8NVer2SkillView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `SkillConfigFE8NVer2SkillForm` | `SkillConfigFE8NVer2SkillView` | `—` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` |
| `SkillConfigFE8NVer3SkillForm` | `SkillConfigFE8NVer3SkillView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `SkillConfigFE8NVer3SkillForm` | `SkillConfigFE8NVer3SkillView` | `—` | `PatchForm` | `PatchManagerView` |
| `SkillConfigFE8NVer3SkillForm` | `SkillConfigFE8NVer3SkillView` | `—` | `ToolAnimationCreatorForm` | `ToolAnimationCreatorView` |
| `SkillConfigCSkillSystem09xForm` | `SkillConfigFE8UCSkillSys09xView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `SkillConfigSkillSystemForm` | `SkillConfigSkillSystemView` | `—` | `ErrorPaletteShowForm` | `ErrorPaletteShowView` |
| `SongExchangeForm` | `SongExchangeView` | `—` | `SongExchangeForm` | `SongExchangeView` |
| `SongInstrumentForm` | `SongInstrumentView` | `—` | `SongInstrumentImportWaveForm` | `SongInstrumentImportWaveView` |
| `SongTrackForm` | `SongTrackView` | `—` | `SongTrackImportMidiForm` | `SongTrackImportMidiView` |
| `SongTrackForm` | `SongTrackView` | `—` | `SongTrackImportSelectInstrumentForm` | `SongTrackImportSelectInstrumentView` |
| `SongTrackForm` | `SongTrackView` | `—` | `SongTrackImportWaveForm` | `SongTrackImportWaveView` |
| `SupportUnitFE6Form` | `SupportUnitFE6View` | `—` | `SupportTalkFE7Form` | `SupportTalkFE7View` |
| `SupportUnitFE6Form` | `SupportUnitFE6View` | `—` | `SupportTalkForm` | `SupportTalkView` |
| `TextBadCharPopupForm` | `TextBadCharPopupView` | `—` | `PatchForm` | `PatchManagerView` |
| `TextBadCharPopupForm` | `TextBadCharPopupView` | `—` | `TextCharCodeForm` | `TextCharCodeView` |
| `TextToSpeechForm` | `TextToSpeechView` | `—` | `TextToSpeechForm` | `TextToSpeechView` |
| `TextForm` | `TextViewerView` | `—` | `TextScriptFormCategorySelectForm` | `—` |
| `TextForm` | `TextViewerView` | `—` | `ImagePortraitFE6Form` | `ImagePortraitFE6View` |
| `TextForm` | `TextViewerView` | `—` | `ImagePortraitForm` | `ImagePortraitView` |
| `TextForm` | `TextViewerView` | `—` | `ImagePortraitForm` | `PortraitViewerView` |
| `TextForm` | `TextViewerView` | `—` | `TextBadCharPopupForm` | `TextBadCharPopupView` |
| `TextForm` | `TextViewerView` | `—` | `TextRefAddDialogForm` | `TextRefAddDialogView` |
| `ToolCustomBuildForm` | `ToolCustomBuildView` | `—` | `PatchForm` | `PatchManagerView` |
| `ToolDiffDebugSelectForm` | `ToolDiffDebugSelectView` | `—` | `ToolDiffDebugSelectMethodPopup` | `—` |
| `ToolDiffDebugSelectForm` | `ToolDiffDebugSelectView` | `—` | `ToolThreeMargeForm` | `ToolThreeMargeView` |
| `ToolEmulatorSetupMessageForm` | `ToolEmulatorSetupMessageView` | `—` | `OptionForm` | `—` |
| `ToolEmulatorSetupMessageForm` | `ToolEmulatorSetupMessageView` | `—` | `ToolInitWizardForm` | `ToolInitWizardView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `AIStealItemForm` | `AIStealItemView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `AITargetForm` | `AITargetView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ClassForm` | `ClassEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ClassForm` | `ClassEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ClassFE6Form` | `ClassFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ClassForm` | `ClassFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ClassForm` | `ClassFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventBattleTalkFE6Form` | `EventBattleTalkFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventBattleTalkFE6Form` | `EventBattleTalkFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventBattleTalkFE7Form` | `EventBattleTalkFE7View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventBattleTalkFE7Form` | `EventBattleTalkFE7View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventBattleTalkForm` | `EventBattleTalkView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventHaikuFE6Form` | `EventHaikuFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventHaikuFE7Form` | `EventHaikuFE7View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `EventHaikuForm` | `EventHaikuView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ItemForm` | `ItemEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ItemForm` | `ItemEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ItemFE6Form` | `ItemFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ItemWeaponEffectForm` | `ItemWeaponEffectViewerView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `ItemWeaponTriangleForm` | `ItemWeaponTriangleViewerView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `MapSettingFE6Form` | `MapSettingFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `MapSettingFE7UForm` | `MapSettingFE7UView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `MapSettingFE7Form` | `MapSettingFE7View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `MapSettingForm` | `MapSettingView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SongTableForm` | `SongTableView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SoundRoomFE6Form` | `SoundRoomFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SoundRoomForm` | `SoundRoomViewerView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SoundRoomForm` | `SoundRoomViewerView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportAttributeForm` | `SupportAttributeView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportTalkFE6Form` | `SupportTalkFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportTalkForm` | `SupportTalkView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportUnitForm` | `SupportUnitEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportUnitForm` | `SupportUnitEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `SupportUnitFE6Form` | `SupportUnitFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `UnitForm` | `UnitEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `UnitFE6Form` | `UnitFE6View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `UnitFE7Form` | `UnitFE7View` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `UnitPaletteForm` | `UnitPaletteView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `WorldMapPathForm` | `WorldMapPathEditorView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `WorldMapPathForm` | `WorldMapPathView` |
| `ToolExportEAEventForm` | `ToolExportEAEventView` | `—` | `WorldMapPointForm` | `WorldMapPointView` |
| `ToolFELintForm` | `ToolFELintView` | `—` | `MainSimpleMenuEventErrorForm` | `MainSimpleMenuEventErrorView` |
| `ToolFELintForm` | `ToolFELintView` | `—` | `ToolDiffDebugSelectForm` | `ToolDiffDebugSelectView` |
| `ToolFlagNameForm` | `ToolFlagNameView` | `—` | `ToolUseFlagForm` | `ToolUseFlagView` |
| `ToolProblemReportForm` | `ToolProblemReportView` | `—` | `ToolProblemReportSearchBackupForm` | `ToolProblemReportSearchBackupView` |
| `ToolProblemReportForm` | `ToolProblemReportView` | `—` | `ToolProblemReportSearchSavForm` | `ToolProblemReportSearchSavView` |
| `ToolRunHintMessageForm` | `ToolRunHintMessageView` | `—` | `ToolEmulatorSetupMessageForm` | `ToolEmulatorSetupMessageView` |
| `ToolRunHintMessageForm` | `ToolRunHintMessageView` | `—` | `ToolRunHintMessageForm` | `ToolRunHintMessageView` |
| `ToolThreeMargeForm` | `ToolThreeMargeView` | `—` | `HexEditorMark` | `—` |
| `ToolThreeMargeForm` | `ToolThreeMargeView` | `—` | `ToolThreeMargeCloseAlertForm` | `ToolThreeMargeCloseAlertView` |
| `ToolTranslateROMForm` | `ToolTranslateROMView` | `—` | `ToolTranslateROMForm` | `ToolTranslateROMView` |
| `ToolUPSPatchSimpleForm` | `ToolUPSPatchSimpleView` | `—` | `ToolUPSPatchSimpleForm` | `ToolUPSPatchSimpleView` |
| `ToolUndoForm` | `ToolUndoView` | `—` | `ToolUndoPopupDialogForm` | `ToolUndoPopupDialogView` |
| `ToolWorkSupportForm` | `ToolWorkSupportView` | `—` | `ToolAllWorkSupportForm` | `ToolAllWorkSupportView` |
| `ToolWorkSupportForm` | `ToolWorkSupportView` | `—` | `ToolWorkSupportForm` | `ToolWorkSupportView` |
| `ToolWorkSupportForm` | `ToolWorkSupportView` | `—` | `ToolWorkSupport_SelectUPSForm` | `ToolWorkSupport_SelectUPSView` |
| `ToolWorkSupportForm` | `ToolWorkSupportView` | `—` | `ToolWorkSupport_UpdateQuestionDialogForm` | `ToolWorkSupport_UpdateQuestionDialogView` |
| `UnitForm` | `UnitEditorView` | `—` | `PatchForm` | `PatchManagerView` |
| `UnitFE6Form` | `UnitFE6View` | `—` | `PatchForm` | `PatchManagerView` |
| `UnitFE7Form` | `UnitFE7View` | `—` | `PatchForm` | `PatchManagerView` |
| `VersionForm` | `VersionView` | `—` | `DevTranslateForm` | `DevTranslateView` |
| `WorldMapEventPointerFE7Form` | `WorldMapEventPointerFE7View` | `—` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerFE7Form` | `WorldMapEventPointerFE7View` | `—` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `—` | `WorldMapPathForm` | `WorldMapPathEditorView` |
| `WorldMapImageFE7Form` | `WorldMapImageFE7View` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `WorldMapImageFE7Form` | `WorldMapImageFE7View` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `WorldMapImageForm` | `WorldMapImageView` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `WorldMapImageForm` | `WorldMapImageView` | `—` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |

## No WinForms Callsite (AV-only manifest entries)

| Source Form | Source View | Command | Target WF | Target AV |
|---|---|---|---|---|
| `ArenaClassForm` | `ArenaClassViewerView` | `JumpToClass` | `ClassForm` | `ClassEditorView` |
| `ArenaClassForm` | `ArenaClassViewerView` | `JumpToClassFE6` | `ClassForm` | `ClassFE6View` |
| `ClassForm` | `ClassEditorView` | `JumpToBattleAnime` | `ImageBattleAnimeForm` | `ImageBattleAnimeView` |
| `ClassForm` | `ClassEditorView` | `JumpToMoveCost` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToMoveCostRain` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToMoveCostSnow` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToTerrainAvoid` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToTerrainDef` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToTerrainRes` | `MoveCostForm` | `MoveCostEditorView` |
| `ClassForm` | `ClassEditorView` | `JumpToPortrait` | `ImagePortraitForm` | `PortraitViewerView` |
| `ClassForm` | `ClassEditorView` | `JumpToDescText` | `TextForm` | `TextViewerView` |
| `ClassForm` | `ClassEditorView` | `JumpToNameText` | `TextForm` | `TextViewerView` |
| `ItemForm` | `ItemEditorView` | `JumpToEffectivenessSkillSystem` | `ItemEffectivenessSkillSystemsReworkForm` | `ItemEffectivenessSkillSystemsReworkView` |
| `ItemForm` | `ItemEditorView` | `JumpToEffectivenessVanilla` | `ItemEffectivenessForm` | `ItemEffectivenessViewerView` |
| `ItemForm` | `ItemEditorView` | `JumpToStatBonusesSkillSystem` | `ItemStatBonusesSkillSystemsForm` | `ItemStatBonusesSkillSystemsView` |
| `ItemForm` | `ItemEditorView` | `JumpToStatBonusesVenno` | `ItemStatBonusesVennoForm` | `ItemStatBonusesVennoView` |
| `ItemForm` | `ItemEditorView` | `JumpToStatBonusesVanilla` | `ItemStatBonusesForm` | `ItemStatBonusesViewerView` |
| `ItemForm` | `ItemEditorView` | `JumpToDescText` | `TextForm` | `TextViewerView` |
| `ItemForm` | `ItemEditorView` | `JumpToNameText` | `TextForm` | `TextViewerView` |
| `ItemForm` | `ItemEditorView` | `JumpToUseDescText` | `TextForm` | `TextViewerView` |
| `—` | `ItemFE6View` | `JumpToDescText` | `TextForm` | `TextViewerView` |
| `SupportTalkForm` | `SupportTalkView` | `JumpToTextA` | `TextForm` | `TextViewerView` |
| `SupportTalkForm` | `SupportTalkView` | `JumpToTextB` | `TextForm` | `TextViewerView` |
| `SupportTalkForm` | `SupportTalkView` | `JumpToTextC` | `TextForm` | `TextViewerView` |
| `SupportUnitForm` | `SupportUnitEditorView` | `JumpToSourceUnit_FE8` | `UnitForm` | `UnitEditorView` |
| `SupportUnitForm` | `SupportUnitEditorView` | `JumpToSourceUnit_FE7` | `UnitFE7Form` | `UnitFE7View` |
| `SupportUnitFE6Form` | `SupportUnitFE6View` | `JumpToSourceUnit_FE6` | `UnitFE6Form` | `UnitFE6View` |
| `UnitForm` | `UnitEditorView` | `JumpToClass` | `ClassForm` | `ClassEditorView` |
| `UnitForm` | `UnitEditorView` | `JumpToClassFE6` | `ClassForm` | `ClassFE6View` |
| `UnitForm` | `UnitEditorView` | `JumpToPortrait` | `ImagePortraitForm` | `PortraitViewerView` |
| `UnitForm` | `UnitEditorView` | `JumpToSupportUnit` | `SupportUnitForm` | `SupportUnitEditorView` |
| `UnitForm` | `UnitEditorView` | `JumpToDescText` | `TextForm` | `TextViewerView` |
| `UnitForm` | `UnitEditorView` | `JumpToNameText` | `TextForm` | `TextViewerView` |
| `UnitFE6Form` | `UnitFE6View` | `JumpToSupportUnit` | `SupportUnitFE6Form` | `SupportUnitFE6View` |
| `UnitFE7Form` | `UnitFE7View` | `JumpToSupportUnit` | `SupportUnitForm` | `SupportUnitEditorView` |

## Matches (WF + AV agree)

| Source Form | Source View | Command | Target WF | Target AV |
|---|---|---|---|---|
| `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | `JumpToSelf` | `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` |
| `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | `JumpToSelf` | `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` |
| `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | `ShowExportText` | `DumpStructSelectToTextDialogForm` | `DumpStructSelectToTextDialogView` |
| `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | `JumpToHexEditor` | `HexEditorForm` | `HexEditorView` |
| `DumpStructSelectDialogForm` | `DumpStructSelectDialogView` | `JumpToPointerTool` | `PointerToolCopyToForm` | `PointerToolCopyToView` |
| `EventUnitFE7Form` | `EventUnitFE7View` | `JumpToBattleTalk` | `EventBattleTalkFE7Form` | `EventBattleTalkFE7View` |
| `EventUnitFE7Form` | `EventUnitFE7View` | `JumpToHaiku` | `EventHaikuFE7Form` | `EventHaikuFE7View` |
| `EventUnitFE7Form` | `EventUnitFE7View` | `JumpToBattleBGM` | `SoundBossBGMForm` | `SoundBossBGMViewerView` |
| `EventUnitForm` | `EventUnitView` | `JumpToBattleTalk` | `EventBattleTalkForm` | `EventBattleTalkView` |
| `EventUnitForm` | `EventUnitView` | `JumpToHaiku` | `EventHaikuForm` | `EventHaikuView` |
| `EventUnitForm` | `EventUnitView` | `JumpToItemDrop` | `EventUnitItemDropForm` | `EventUnitItemDropView` |
| `EventUnitForm` | `EventUnitView` | `JumpToNewAlloc` | `EventUnitNewAllocForm` | `EventUnitNewAllocView` |
| `EventUnitForm` | `EventUnitView` | `JumpToMonsterProbability` | `MonsterProbabilityForm` | `MonsterProbabilityViewerView` |
| `EventUnitForm` | `EventUnitView` | `JumpToBattleBGM` | `SoundBossBGMForm` | `SoundBossBGMViewerView` |
| `ImageBGForm` | `ImageBGView` | `JumpToDecreaseColor` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageBGForm` | `ImageBGView` | `JumpToGraphicsTool` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImageBGForm` | `ImageBGView` | `JumpToBGSelectPopup` | `ImageBGSelectPopupForm` | `ImageBGSelectPopupView` |
| `ImageBattleBGForm` | `ImageBattleBGView` | `JumpToDecreaseColor` | `DecreaseColorTSAToolForm` | `DecreaseColorTSAToolView` |
| `ImageBattleBGForm` | `ImageBattleBGView` | `JumpToGraphicsTool` | `GraphicsToolForm` | `GraphicsToolView` |
| `ImagePortraitForm` | `ImagePortraitView` | `JumpToPalette` | `ImagePalletForm` | `ImagePalletView` |
| `ImagePortraitForm` | `ImagePortraitView` | `JumpToImporter` | `ImagePortraitImporterForm` | `ImagePortraitImporterView` |
| `ImagePortraitForm` | `ImagePortraitView` | `JumpToStatusHeight` | `UnitIncreaseHeightForm` | `UnitIncreaseHeightView` |
| `ItemUsagePointerForm` | `ItemUsagePointerViewerView` | `JumpToPromotion` | `ItemPromotionForm` | `ItemPromotionViewerView` |
| `ItemUsagePointerForm` | `ItemUsagePointerViewerView` | `JumpToStatBonuses` | `ItemStatBonusesForm` | `ItemStatBonusesViewerView` |
| `ItemUsagePointerForm` | `ItemUsagePointerViewerView` | `JumpToIerPatch` | `PatchForm` | `PatchManagerView` |
| `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` | `JumpToSelfFromRef` | `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` |
| `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` | `JumpToFloorLookup` | `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` |
| `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` | `JumpToPatchExtendsBattleBG` | `PatchForm` | `PatchManagerView` |
| `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` | `JumpToBGLookup` | `MapTerrainBGLookupTableForm` | `MapTerrainBGLookupTableView` |
| `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` | `JumpToSelfFromRef` | `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` |
| `MapTerrainFloorLookupTableForm` | `MapTerrainFloorLookupTableView` | `JumpToPatchExtendsBattleBG` | `PatchForm` | `PatchManagerView` |
| `PointerToolForm` | `PointerToolView` | `JumpToBatchInput` | `PointerToolBatchInputForm` | `PointerToolBatchInputView` |
| `PointerToolForm` | `PointerToolView` | `JumpToCopyTo` | `PointerToolCopyToForm` | `PointerToolCopyToView` |
| `PointerToolForm` | `PointerToolView` | `JumpToSelf` | `PointerToolForm` | `PointerToolView` |
| `SongTrackForm` | `SongTrackView` | `JumpToSongExchange` | `SongExchangeForm` | `SongExchangeView` |
| `SongTrackForm` | `SongTrackView` | `JumpToSongTrackAllChangeTrack` | `SongTrackAllChangeTrackForm` | `SongTrackAllChangeTrackView` |
| `SongTrackForm` | `SongTrackView` | `JumpToSongTrackChangeTrack` | `SongTrackChangeTrackForm` | `SongTrackChangeTrackView` |
| `SupportUnitForm` | `SupportUnitEditorView` | `JumpToSupportTalk_FE6` | `SupportTalkFE6Form` | `SupportTalkFE6View` |
| `SupportUnitForm` | `SupportUnitEditorView` | `JumpToSupportTalk_FE7` | `SupportTalkFE7Form` | `SupportTalkFE7View` |
| `SupportUnitForm` | `SupportUnitEditorView` | `JumpToSupportTalk_FE8` | `SupportTalkForm` | `SupportTalkView` |
| `SupportUnitFE6Form` | `SupportUnitFE6View` | `JumpToSupportTalk_FE6` | `SupportTalkFE6Form` | `SupportTalkFE6View` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding1Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding1Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding1Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding2Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding2Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToEnding2Event` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToOpeningEvent` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToOpeningEvent` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToOpeningEvent` | `EventScriptForm` | `EventScriptView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToWorldMapPath` | `WorldMapPathForm` | `WorldMapPathView` |
| `WorldMapEventPointerForm` | `WorldMapEventPointerView` | `JumpToWorldMapPoint` | `WorldMapPointForm` | `WorldMapPointView` |
