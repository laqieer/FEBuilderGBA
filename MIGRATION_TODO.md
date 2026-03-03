# Cross-Platform Migration TODO

Tracking document for Windows-only CLI args and GUI forms that need multi-platform equivalents in FEBuilderGBA.CLI and FEBuilderGBA.Avalonia.

---

## Windows-Only CLI Args (5 total) — ALL DONE

These args now exist in both `FEBuilderGBA/Program.cs` and `FEBuilderGBA.CLI/Program.cs`:

| # | Arg | Purpose | Status |
|---|-----|---------|--------|
| 1 | `--lastrom` | Opens last-used ROM file | DONE — CLI reads Last_Rom_Filename from config; Avalonia loads via StartupRomPath |
| 2 | `--force-detail` | Forces detail mode (vs easy mode) | DONE — CLI acknowledges flag; Avalonia sets ForceDetailMode property |
| 3 | `--translate_batch` | Batch translation via CLI | DONE — CLI RunTranslateBatch exports + imports text |
| 4 | `--test` | Runs self-test diagnostics | DONE — CLI RunSelfTest validates config, ROM, text, scripts |
| 5 | `--testonly` | Runs self-test then exits | DONE — CLI RunSelfTest with testonly mode |

**Cross-platform CLI args (19 total):** `--version`, `--help`, `--rom`, `--force-version`, `--makeups`, `--applyups`, `--lint`, `--disasm`, `--decreasecolor`, `--pointercalc`, `--rebuild`, `--songexchange`, `--convertmap1picture`, `--translate`, `--lastrom`, `--force-detail`, `--translate_batch`, `--test`, `--testonly`

---

## Already Ported to Avalonia (53 + 234 = 287 views) — ALL DONE

### Original 53 views (fully functional editors)
ArenaClassViewer, ArenaEnemyWeaponViewer, BattleBGViewer, BattleTerrainViewer, BigCGViewer, CCBranchEditor, ChapterTitleViewer, ClassEditor, ED, EDStaffRoll, EventCond, ImageViewer, ItemEditor, ItemEffectivenessViewer, ItemEffectPointerViewer, ItemIconViewer, ItemPromotionViewer, ItemShopViewer, ItemStatBonusesViewer, ItemUsagePointerViewer, ItemWeaponEffectViewer, ItemWeaponTriangleViewer, LinkArenaDenyUnitViewer, MapChange, MapExitPoint, MapPointer, MapSetting, MapTileAnimation, MenuCommand, MenuDefinition, MonsterItemViewer, MonsterProbabilityViewer, MonsterWMapProbabilityViewer, MoveCostEditor, OPClassDemoViewer, OPClassFontViewer, OPPrologueViewer, PortraitViewer, SongTable, SoundBossBGMViewer, SoundFootStepsViewer, SoundRoomViewer, SummonsDemonKingViewer, SummonUnitViewer, SupportAttribute, SupportTalk, SupportUnitEditor, SystemHoverColorViewer, SystemIconViewer, TerrainNameEditor, TextViewer, UnitEditor, WorldMapBGM, WorldMapEventPointer, WorldMapPoint

### Migrated 234 views (cross-platform Avalonia editors)

---

### A. Image Editors (27 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| ImagePortraitForm | ImagePortraitView | DONE |
| ImagePortraitFE6Form | ImagePortraitFE6View | DONE |
| ImagePortraitImporterForm | ImagePortraitImporterView | DONE |
| ImageBGForm | ImageBGView | DONE |
| ImageBGSelectPopupForm | ImageBGSelectPopupView | DONE |
| ImageBattleAnimeForm | ImageBattleAnimeView | DONE |
| ImageBattleAnimePalletForm | ImageBattleAnimePalletView | DONE |
| ImageBattleBGForm | ImageBattleBGView | DONE |
| ImageBattleScreenForm | ImageBattleScreenView | DONE |
| ImageCGForm | ImageCGView | DONE |
| ImageCGFE7UForm | ImageCGFE7UView | DONE |
| ImageUnitPaletteForm | ImageUnitPaletteView | DONE |
| ImageUnitWaitIconFrom | ImageUnitWaitIconView | DONE |
| ImageUnitMoveIconFrom | ImageUnitMoveIconView | DONE |
| ImageSystemAreaForm | ImageSystemAreaView | DONE |
| ImageGenericEnemyPortraitForm | ImageGenericEnemyPortraitView | DONE |
| ImageRomAnimeForm | ImageRomAnimeView | DONE |
| ImageTSAEditorForm | ImageTSAEditorView | DONE |
| ImageTSAAnimeForm | ImageTSAAnimeView | DONE |
| ImageTSAAnime2Form | ImageTSAAnime2View | DONE |
| ImagePalletForm | ImagePalletView | DONE |
| ImageMagicFEditorForm | ImageMagicFEditorView | DONE |
| ImageMagicCSACreatorForm | ImageMagicCSACreatorView | DONE |
| ImageMapActionAnimationForm | ImageMapActionAnimationView | DONE |
| DecreaseColorTSAToolForm | DecreaseColorTSAToolView | DONE |
| ImageFormRef | ImageFormRefViewerView | DONE |
| InterpolatedPictureBox | InterpolatedPictureBoxViewerView | DONE |

### B. Event Script Editors (37 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| EventScriptForm | EventScriptView | DONE |
| EventScriptInnerControl | EventScriptInnerView | DONE |
| EventScriptPopupUserControl | EventScriptPopupView | DONE |
| EventScriptFormCategorySelectForm | EventScriptCategorySelectView | DONE |
| EventScriptTemplateForm | EventScriptTemplateView | DONE |
| EventTemplate1Form | EventTemplate1View | DONE |
| EventTemplate2Form | EventTemplate2View | DONE |
| EventTemplate3Form | EventTemplate3View | DONE |
| EventTemplate4Form | EventTemplate4View | DONE |
| EventTemplate5Form | EventTemplate5View | DONE |
| EventTemplate6Form | EventTemplate6View | DONE |
| EventTemplateImpl | EventTemplateImplView | DONE |
| EventUnitForm | EventUnitView | DONE |
| EventUnitFE6Form | EventUnitFE6View | DONE |
| EventUnitFE7Form | EventUnitFE7View | DONE |
| EventUnitSimUserControl | EventUnitSimView | DONE |
| EventUnitColorForm | EventUnitColorView | DONE |
| EventUnitItemDropForm | EventUnitItemDropView | DONE |
| EventUnitNewAllocForm | EventUnitNewAllocView | DONE |
| EventBattleTalkForm | EventBattleTalkView | DONE |
| EventBattleTalkFE6Form | EventBattleTalkFE6View | DONE |
| EventBattleTalkFE7Form | EventBattleTalkFE7View | DONE |
| EventBattleDataFE7Form | EventBattleDataFE7View | DONE |
| EventHaikuForm | EventHaikuView | DONE |
| EventHaikuFE6Form | EventHaikuFE6View | DONE |
| EventHaikuFE7Form | EventHaikuFE7View | DONE |
| EventMapChangeForm | EventMapChangeView | DONE |
| EventForceSortieForm | EventForceSortieView | DONE |
| EventForceSortieFE7Form | EventForceSortieFE7View | DONE |
| EventFinalSerifFE7Form | EventFinalSerifFE7View | DONE |
| EventTalkGroupFE7Form | EventTalkGroupFE7View | DONE |
| EventMoveDataFE7Form | EventMoveDataFE7View | DONE |
| EventFunctionPointerForm | EventFunctionPointerView | DONE |
| EventFunctionPointerFE7Form | EventFunctionPointerFE7View | DONE |
| EventAssemblerForm | EventAssemblerView | DONE |
| ProcsScriptForm | ProcsScriptView | DONE |
| ProcsScriptCategorySelectForm | ProcsScriptCategorySelectView | DONE |

### C. AI Script Editors (13 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| AIScriptForm | AIScriptView | DONE |
| AIScriptCategorySelectForm | AIScriptCategorySelectView | DONE |
| AIASMCALLTALKForm | AIASMCALLTALKView | DONE |
| AIASMCoordinateForm | AIASMCoordinateView | DONE |
| AIASMRangeForm | AIASMRangeView | DONE |
| AIMapSettingForm | AIMapSettingView | DONE |
| AIPerformItemForm | AIPerformItemView | DONE |
| AIPerformStaffForm | AIPerformStaffView | DONE |
| AIStealItemForm | AIStealItemView | DONE |
| AITargetForm | AITargetView | DONE |
| AITilesForm | AITilesView | DONE |
| AIUnitsForm | AIUnitsView | DONE |
| AOERANGEForm | AOERANGEView | DONE |

### D. Map Editors (23 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| MapSettingForm | MapSettingMainView | DONE |
| MapSettingFE6Form | MapSettingFE6View | DONE |
| MapSettingFE7Form | MapSettingFE7View | DONE |
| MapSettingFE7UForm | MapSettingFE7UView | DONE |
| MapSettingDifficultyForm | MapSettingDifficultyView | DONE |
| MapPointerNewPLISTPopupForm | MapPointerNewPLISTView | DONE |
| MapEditorForm | MapEditorView | DONE |
| MapEditorResizeDialogForm | MapEditorResizeView | DONE |
| MapEditorMarSizeDialogForm | MapEditorMarSizeView | DONE |
| MapEditorAddMapChangeDialogForm | MapEditorAddMapChangeView | DONE |
| MapTerrainNameForm | MapTerrainNameView | DONE |
| MapTerrainNameEngForm | MapTerrainNameEngView | DONE |
| MapStyleEditorForm | MapStyleEditorView | DONE |
| MapStyleEditorAppendPopupForm | MapStyleEditorAppendView | DONE |
| MapStyleEditorFormWarningVanillaTileOverraideForm | MapStyleEditorWarningView | DONE |
| MapStyleEditorImportImageOptionForm | MapStyleEditorImportImageOptionView | DONE |
| MapTerrainBGLookupTableForm | MapTerrainBGLookupView | DONE |
| MapTerrainFloorLookupTableForm | MapTerrainFloorLookupView | DONE |
| MapMiniMapTerrainImageForm | MapMiniMapTerrainImageView | DONE |
| MapTileAnimation1Form | MapTileAnimation1View | DONE |
| MapTileAnimation2Form | MapTileAnimation2View | DONE |
| MapLoadFunctionForm | MapLoadFunctionView | DONE |
| MapPictureBox | MapPictureBoxViewerView | DONE |

### E. Audio/Sound Editors (12 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| SongTrackForm | SongTrackView | DONE |
| SongInstrumentForm | SongInstrumentView | DONE |
| SongInstrumentDirectSoundForm | SongInstrumentDirectSoundView | DONE |
| SongInstrumentImportWaveForm | SongInstrumentImportWaveView | DONE |
| SongTrackChangeTrackForm | SongTrackChangeTrackView | DONE |
| SongTrackAllChangeTrackForm | SongTrackAllChangeTrackView | DONE |
| SongTrackImportMidiForm | SongTrackImportMidiView | DONE |
| SongTrackImportSelectInstrumentForm | SongTrackImportSelectInstrumentView | DONE |
| SongTrackImportWaveForm | SongTrackImportWaveView | DONE |
| SongExchangeForm | SongExchangeView | DONE |
| SoundRoomCGForm | SoundRoomCGView | DONE |
| SoundRoomFE6Form | SoundRoomFE6View | DONE |

### F. Unit/Class Specialized (17 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| UnitForm | UnitMainView | DONE |
| UnitFE6Form | UnitFE6View | DONE |
| UnitActionPointerForm | UnitActionPointerView | DONE |
| UnitCustomBattleAnimeForm | UnitCustomBattleAnimeView | DONE |
| UnitIncreaseHeightForm | UnitIncreaseHeightView | DONE |
| UnitPaletteForm | UnitPaletteView | DONE |
| ClassOPDemoForm | ClassOPDemoView | DONE |
| ClassOPFontForm | ClassOPFontView | DONE |
| OPClassAlphaNameForm | OPClassAlphaNameView | DONE |
| OPClassAlphaNameFE6Form | OPClassAlphaNameFE6View | DONE |
| OPClassDemoFE7Form | OPClassDemoFE7View | DONE |
| OPClassDemoFE7UForm | OPClassDemoFE7UView | DONE |
| OPClassDemoFE8UForm | OPClassDemoFE8UView | DONE |
| OPClassFontFE8UForm | OPClassFontFE8UView | DONE |
| ExtraUnitForm | ExtraUnitView | DONE |
| ExtraUnitFE8UForm | ExtraUnitFE8UView | DONE |
| ClassFE6Form | ClassFE6View | DONE |

### G. Text/Translation (12 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| TextForm | TextMainView | DONE |
| OtherTextForm | OtherTextView | DONE |
| TextRefAddDialogForm | TextRefAddDialogView | DONE |
| TextBadCharPopupForm | TextBadCharPopupView | DONE |
| TextScriptFormCategorySelectForm | TextScriptCategorySelectView | DONE |
| TextToSpeechForm | TextToSpeechView | DONE |
| TextEscapeForm | TextEscapeEditorView | DONE |
| DevTranslateForm | DevTranslateView | DONE |
| ToolTranslateROMForm | ToolTranslateROMView | DONE |
| CStringForm | CStringView | DONE |
| FontForm | FontEditorView | DONE |
| FontZHForm | FontZHView | DONE |

### H. Patch/Mod Management (6 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| PatchForm | PatchManagerView | DONE |
| PatchFilterExForm | PatchFilterExView | DONE |
| PatchFormUninstallDialogForm | PatchUninstallDialogView | DONE |
| HowDoYouLikePatchForm | HowDoYouLikePatchView | DONE |
| HowDoYouLikePatch2Form | HowDoYouLikePatch2View | DONE |
| ToolCustomBuildForm | ToolCustomBuildView | DONE |

### I. Skill Systems (12 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| SkillAssignmentUnitSkillSystemForm | SkillAssignmentUnitSkillSystemView | DONE |
| SkillAssignmentUnitCSkillSysForm | SkillAssignmentUnitCSkillSysView | DONE |
| SkillAssignmentUnitFE8NForm | SkillAssignmentUnitFE8NView | DONE |
| SkillAssignmentClassSkillSystemForm | SkillAssignmentClassSkillSystemView | DONE |
| SkillAssignmentClassCSkillSysForm | SkillAssignmentClassCSkillSysView | DONE |
| SkillConfigSkillSystemForm | SkillConfigSkillSystemView | DONE |
| SkillConfigFE8UCSkillSys09xForm | SkillConfigFE8UCSkillSys09xView | DONE |
| SkillConfigFE8NSkillForm | SkillConfigFE8NSkillView | DONE |
| SkillConfigFE8NVer2SkillForm | SkillConfigFE8NVer2SkillView | DONE |
| SkillConfigFE8NVer3SkillForm | SkillConfigFE8NVer3SkillView | DONE |
| SkillSystemsEffectivenessReworkClassTypeForm | SkillSystemsEffectivenessReworkClassTypeView | DONE |
| SkillSystemsCSkillRechainForm | SkillSystemsCSkillRechainView | DONE |

### J. Tools & Advanced (23 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| ToolUndoForm | ToolUndoView | DONE |
| ToolFELintForm | ToolFELintView | DONE |
| ToolROMRebuildForm | ToolROMRebuildView | DONE |
| ToolLZ77Form | ToolLZ77View | DONE |
| ToolDiffForm | ToolDiffView | DONE |
| ToolUpdateDialogForm | ToolUpdateDialogView | DONE |
| ToolUPSPatchSimpleForm | ToolUPSPatchSimpleView | DONE |
| ToolUPSOpenSimpleForm | ToolUPSOpenSimpleView | DONE |
| ToolAllWorkSupportForm | ToolAllWorkSupportView | DONE |
| ToolProblemReportForm | ToolProblemReportView | DONE |
| ToolAnimationCreatorForm | ToolAnimationCreatorView | DONE |
| ToolFlagNameForm | ToolFlagNameView | DONE |
| ToolUseFlagForm | ToolUseFlagView | DONE |
| ToolUnitTalkGroupForm | ToolUnitTalkGroupView | DONE |
| ToolSubtitleOverlayForm | ToolSubtitleOverlayView | DONE |
| ToolASMInsertForm | ToolASMInsertView | DONE |
| ToolThreMargeForm | ToolThreeMargeView | DONE |
| RAMRewriteToolForm | RAMRewriteToolView | DONE |
| EmulatorMemoryForm | EmulatorMemoryView | DONE |
| GrowSimulatorForm | GrowSimulatorView | DONE |
| HexEditorForm | HexEditorView | DONE |
| DisASMForm | DisASMView | DONE |
| LogForm | LogViewerView | DONE |
| OptionForm | OptionsView | DONE |

### K. World Map Specialized (8 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| WorldMapPathForm | WorldMapPathView | DONE |
| WorldMapPathEditorForm | WorldMapPathEditorView | DONE |
| WorldMapPathMoveEditorForm | WorldMapPathMoveEditorView | DONE |
| WorldMapImageForm | WorldMapImageView | DONE |
| WorldMapImageFE6Form | WorldMapImageFE6View | DONE |
| WorldMapImageFE7Form | WorldMapImageFE7View | DONE |
| WorldMapEventPointerFE6Form | WorldMapEventPointerFE6View | DONE |
| WorldMapEventPointerFE7Form | WorldMapEventPointerFE7View | DONE |

### L. Structural Data (7 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| Command85PointerForm | Command85PointerView | DONE |
| FE8SpellMenuExtendsForm | FE8SpellMenuExtendsView | DONE |
| StatusOptionForm | StatusOptionView | DONE |
| StatusOptionOrderForm | StatusOptionOrderView | DONE |
| OAMSPForm | OAMSPView | DONE |
| DumpStructSelectDialogForm | DumpStructSelectDialogView | DONE |
| DumpStructSelectToTextDialogForm | DumpStructSelectToTextDialogView | DONE |

### M. Notification/UI Controls (6 forms) — DONE

| Form | Avalonia View | Status |
|------|--------------|--------|
| NotifyWriteUserControl | NotifyWriteView | DONE |
| NotifyPleaseWaitUserControl | NotifyPleaseWaitView | DONE |
| NotifyDirectInjectionNotifyUserControl | NotifyDirectInjectionView | DONE |
| MainSimpleMenuForm | MainSimpleMenuView | DONE |
| MainSimpleMenuEventErrorForm | MainSimpleMenuEventErrorView | DONE |
| MainSimpleMenuImageSubForm | MainSimpleMenuImageSubView | DONE |

---

## Summary Statistics

| Category | Count | Status |
|----------|-------|--------|
| Cross-platform CLI args | 19 | ALL DONE |
| Windows-only CLI args | 0 | ALL MIGRATED |
| Avalonia views (ported) | 287 | ALL DONE |
| Windows-only forms | 0 | ALL MIGRATED |
| **Total Avalonia views** | **287** |
| **Port coverage** | **100%** |
