using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// Maps Avalonia view names (from GetAllEditorFactories) to WinForms Form types.
    /// Used by ScreenshotAllRunner to capture WinForms screenshots with filenames
    /// that match the Avalonia screenshot filenames for side-by-side comparison.
    /// </summary>
    static class ScreenshotFormRegistry
    {
        /// <summary>
        /// Returns all form factories: (AvaloniaViewName, FormFactory).
        /// Each entry maps an Avalonia view name to a factory that creates the
        /// corresponding WinForms Form instance.
        /// </summary>
        public static List<(string Name, Func<Form> Factory)> GetAllFormFactories()
        {
            return new List<(string Name, Func<Form> Factory)>
            {
                // Data Editors
                ("UnitEditorView", () => new UnitForm()),
                ("ItemEditorView", () => new ItemForm()),
                ("ClassEditorView", () => new ClassForm()),
                ("ClassFE6View", () => new ClassFE6Form()),
                ("CCBranchEditorView", () => new CCBranchForm()),
                ("MoveCostEditorView", () => new MoveCostForm()),
                ("TerrainNameEditorView", () => new MapTerrainNameForm()),
                ("SupportUnitEditorView", () => new SupportUnitForm()),
                ("SupportAttributeView", () => new SupportAttributeForm()),
                ("SupportTalkView", () => new SupportTalkForm()),
                ("UnitFE6View", () => new UnitFE6Form()),
                ("UnitActionPointerView", () => new UnitActionPointerForm()),
                ("UnitCustomBattleAnimeView", () => new UnitCustomBattleAnimeForm()),
                ("UnitIncreaseHeightView", () => new UnitIncreaseHeightForm()),
                ("UnitPaletteView", () => new UnitPaletteForm()),
                // ClassOPDemoForm, ClassOPFontForm excluded from WinForms build (Compile Remove)
                // ("ClassOPDemoView", ...)
                // ("ClassOPFontView", ...)
                ("ExtraUnitView", () => new ExtraUnitForm()),
                ("ExtraUnitFE8UView", () => new ExtraUnitFE8UForm()),

                // Item Editors
                ("ItemWeaponEffectViewerView", () => new ItemWeaponEffectForm()),
                ("ItemStatBonusesViewerView", () => new ItemStatBonusesForm()),
                ("ItemEffectivenessViewerView", () => new ItemEffectivenessForm()),
                ("ItemPromotionViewerView", () => new ItemPromotionForm()),
                ("ItemShopViewerView", () => new ItemShopForm()),
                ("ItemWeaponTriangleViewerView", () => new ItemWeaponTriangleForm()),
                ("ItemUsagePointerViewerView", () => new ItemUsagePointerForm()),
                ("ItemEffectPointerViewerView", () => new ItemEffectPointerForm()),
                ("ItemIconViewerView", () => new ImageItemIconForm()),

                // Map Editors
                ("MapSettingView", () => new MapSettingForm()),
                ("MapChangeView", () => new MapChangeForm()),
                ("MapExitPointView", () => new MapExitPointForm()),
                ("MapPointerView", () => new MapPointerForm()),
                // MapTileAnimationView has no WinForms Form — only MapTileAnimation1/2Form exist
                // ("MapTileAnimationView", ...)
                ("MapEditorView", () => new MapEditorForm()),
                ("MapSettingFE6View", () => new MapSettingFE6Form()),
                ("MapSettingFE7View", () => new MapSettingFE7Form()),
                ("MapSettingFE7UView", () => new MapSettingFE7UForm()),
                ("MapSettingDifficultyView", () => new MapSettingDifficultyForm()),
                ("MapStyleEditorView", () => new MapStyleEditorForm()),
                ("MapTerrainBGLookupView", () => new MapTerrainBGLookupTableForm()),
                ("MapTerrainFloorLookupView", () => new MapTerrainFloorLookupTableForm()),
                ("MapMiniMapTerrainImageView", () => new MapMiniMapTerrainImageForm()),
                ("MapTileAnimation1View", () => new MapTileAnimation1Form()),
                ("MapTileAnimation2View", () => new MapTileAnimation2Form()),
                ("MapLoadFunctionView", () => new MapLoadFunctionForm()),
                ("MapTerrainNameEngView", () => new MapTerrainNameEngForm()),

                // Event Script Editors
                ("EventCondView", () => new EventCondForm()),
                ("EventScriptView", () => new EventScriptForm()),
                ("EventUnitView", () => new EventUnitForm()),
                ("EventUnitFE6View", () => new EventUnitFE6Form()),
                ("EventUnitFE7View", () => new EventUnitFE7Form()),
                ("EventUnitColorView", () => new EventUnitColorForm()),
                ("EventUnitItemDropView", () => new EventUnitItemDropForm()),
                ("EventUnitNewAllocView", () => new EventUnitNewAllocForm()),
                ("EventBattleTalkView", () => new EventBattleTalkForm()),
                ("EventBattleTalkFE6View", () => new EventBattleTalkFE6Form()),
                ("EventBattleTalkFE7View", () => new EventBattleTalkFE7Form()),
                ("EventBattleDataFE7View", () => new EventBattleDataFE7Form()),
                ("EventHaikuView", () => new EventHaikuForm()),
                ("EventHaikuFE6View", () => new EventHaikuFE6Form()),
                ("EventHaikuFE7View", () => new EventHaikuFE7Form()),
                // EventMapChangeForm excluded from WinForms build (Compile Remove)
                // ("EventMapChangeView", ...)
                ("EventForceSortieView", () => new EventForceSortieForm()),
                ("EventForceSortieFE7View", () => new EventForceSortieFE7Form()),
                ("EventFunctionPointerView", () => new EventFunctionPointerForm()),
                ("EventFunctionPointerFE7View", () => new EventFunctionPointerFE7Form()),
                ("EventAssemblerView", () => new EventAssemblerForm()),
                ("ProcsScriptView", () => new ProcsScriptForm()),
                ("EventScriptTemplateView", () => new EventScriptTemplateForm()),

                // AI Script Editors
                ("AIScriptView", () => new AIScriptForm()),
                ("AIASMCALLTALKView", () => new AIASMCALLTALKForm()),
                ("AIASMCoordinateView", () => new AIASMCoordinateForm()),
                ("AIASMRangeView", () => new AIASMRangeForm()),
                ("AIMapSettingView", () => new AIMapSettingForm()),
                ("AIPerformItemView", () => new AIPerformItemForm()),
                ("AIPerformStaffView", () => new AIPerformStaffForm()),
                ("AIStealItemView", () => new AIStealItemForm()),
                ("AITargetView", () => new AITargetForm()),
                ("AITilesView", () => new AITilesForm()),
                ("AIUnitsView", () => new AIUnitsForm()),
                ("AOERANGEView", () => new AOERANGEForm()),

                // Image Editors
                // ImageViewerView has no direct WinForms Form equivalent (ImageFormRef is not a Form)
                // ("ImageViewerView", ...)
                ("PortraitViewerView", () => new ImagePortraitForm()),
                ("ImagePortraitView", () => new ImagePortraitForm()),
                ("ImagePortraitFE6View", () => new ImagePortraitFE6Form()),
                ("ImagePortraitImporterView", () => new ImagePortraitImporterForm()),
                ("ImageBGView", () => new ImageBGForm()),
                ("ImageBattleAnimeView", () => new ImageBattleAnimeForm()),
                ("ImageBattleAnimePalletView", () => new ImageBattleAnimePalletForm()),
                ("ImageBattleBGView", () => new ImageBattleBGForm()),
                ("ImageBattleScreenView", () => new ImageBattleScreenForm()),
                ("ImageCGView", () => new ImageCGForm()),
                ("ImageCGFE7UView", () => new ImageCGFE7UForm()),
                ("ImageUnitPaletteView", () => new ImageUnitPaletteForm()),
                // ImageUnitWaitIconView and ImageUnitMoveIconView have no WinForms Form counterparts
                // ("ImageUnitWaitIconView", ...)
                // ("ImageUnitMoveIconView", ...)
                ("ImageSystemAreaView", () => new ImageSystemAreaForm()),
                ("ImageGenericEnemyPortraitView", () => new ImageGenericEnemyPortraitForm()),
                ("ImageRomAnimeView", () => new ImageRomAnimeForm()),
                ("ImageTSAEditorView", () => new ImageTSAEditorForm()),
                ("ImageTSAAnimeView", () => new ImageTSAAnimeForm()),
                ("ImageTSAAnime2View", () => new ImageTSAAnime2Form()),
                ("ImagePalletView", () => new ImagePalletForm()),
                ("ImageMagicFEditorView", () => new ImageMagicFEditorForm()),
                ("ImageMagicCSACreatorView", () => new ImageMagicCSACreatorForm()),
                ("ImageMapActionAnimationView", () => new ImageMapActionAnimationForm()),
                ("DecreaseColorTSAToolView", () => new DecreaseColorTSAToolForm()),
                ("SystemIconViewerView", () => new ImageSystemIconForm()),
                // ImageSystemHoverColorForm excluded from WinForms build (Compile Remove)
                // ("SystemHoverColorViewerView", ...)
                ("BattleBGViewerView", () => new ImageBattleBGForm()),
                ("BattleTerrainViewerView", () => new ImageBattleTerrainForm()),
                ("ChapterTitleViewerView", () => new ImageChapterTitleForm()),
                ("ImageChapterTitleFE7View", () => new ImageChapterTitleFE7Form()),
                // BigCGForm excluded from WinForms build (Compile Remove)
                // ("BigCGViewerView", ...)
                ("OPClassDemoViewerView", () => new OPClassDemoForm()),
                ("OPClassFontViewerView", () => new OPClassFontForm()),
                ("OPPrologueViewerView", () => new OPPrologueForm()),

                // Audio Editors
                ("SongTableView", () => new SongTableForm()),
                ("SongTrackView", () => new SongTrackForm()),
                ("SongInstrumentView", () => new SongInstrumentForm()),
                ("SongInstrumentDirectSoundView", () => new SongInstrumentDirectSoundForm()),
                ("SongInstrumentImportWaveView", () => new SongInstrumentImportWaveForm()),
                ("SongTrackImportMidiView", () => new SongTrackImportMidiForm()),
                ("SongExchangeView", () => new SongExchangeForm()),
                ("SoundBossBGMViewerView", () => new SoundBossBGMForm()),
                ("SoundFootStepsViewerView", () => new SoundFootStepsForm()),
                ("SoundRoomViewerView", () => new SoundRoomForm()),
                ("SoundRoomFE6View", () => new SoundRoomFE6Form()),
                ("SoundRoomCGView", () => new SoundRoomCGForm()),

                // Arena / Monster / Summon Editors
                ("ArenaClassViewerView", () => new ArenaClassForm()),
                ("ArenaEnemyWeaponViewerView", () => new ArenaEnemyWeaponForm()),
                ("LinkArenaDenyUnitViewerView", () => new LinkArenaDenyUnitForm()),
                ("MonsterProbabilityViewerView", () => new MonsterProbabilityForm()),
                ("MonsterItemViewerView", () => new MonsterItemForm()),
                ("MonsterWMapProbabilityViewerView", () => new MonsterWMapProbabilityForm()),
                ("SummonUnitViewerView", () => new SummonUnitForm()),
                ("SummonsDemonKingViewerView", () => new SummonsDemonKingForm()),

                // Menu / ED / World Map Editors
                ("MenuDefinitionView", () => new MenuDefinitionForm()),
                ("MenuCommandView", () => new MenuCommandForm()),
                ("EDView", () => new EDForm()),
                ("EDStaffRollView", () => new EDStaffRollForm()),
                ("WorldMapPointView", () => new WorldMapPointForm()),
                ("WorldMapBGMView", () => new WorldMapBGMForm()),
                ("WorldMapEventPointerView", () => new WorldMapEventPointerForm()),
                ("WorldMapPathView", () => new WorldMapPathForm()),
                ("WorldMapPathEditorView", () => new WorldMapPathEditorForm()),
                ("WorldMapImageView", () => new WorldMapImageForm()),
                ("WorldMapImageFE6View", () => new WorldMapImageFE6Form()),
                ("WorldMapImageFE7View", () => new WorldMapImageFE7Form()),
                ("WorldMapEventPointerFE6View", () => new WorldMapEventPointerFE6Form()),
                ("WorldMapEventPointerFE7View", () => new WorldMapEventPointerFE7Form()),

                // Text / Translation Editors
                ("TextViewerView", () => new TextForm()),
                ("TextMainView", () => new TextForm()),
                ("OtherTextView", () => new OtherTextForm()),
                ("CStringView", () => new CStringForm()),
                ("FontEditorView", () => new FontForm()),
                ("FontZHView", () => new FontZHForm()),
                ("DevTranslateView", () => new DevTranslateForm()),
                ("ToolTranslateROMView", () => new ToolTranslateROMForm()),
                // TextEscapeEditorView has no WinForms Form counterpart
                // ("TextEscapeEditorView", ...)

                // Structural Data
                ("Command85PointerView", () => new Command85PointerForm()),
                ("FE8SpellMenuExtendsView", () => new FE8SpellMenuExtendsForm()),
                ("StatusOptionView", () => new StatusOptionForm()),
                ("OAMSPView", () => new OAMSPForm()),
                ("DumpStructSelectDialogView", () => new DumpStructSelectDialogForm()),

                // Patch / Skill Systems
                ("PatchManagerView", () => new PatchForm()),
                ("ToolCustomBuildView", () => new ToolCustomBuildForm()),
                ("SkillAssignmentUnitSkillSystemView", () => new SkillAssignmentUnitSkillSystemForm()),
                ("SkillAssignmentClassSkillSystemView", () => new SkillAssignmentClassSkillSystemForm()),
                ("SkillConfigSkillSystemView", () => new SkillConfigSkillSystemForm()),

                // Tools
                ("ToolUndoView", () => new ToolUndoForm()),
                ("ToolFELintView", () => new ToolFELintForm()),
                ("ToolROMRebuildView", () => new ToolROMRebuildForm()),
                ("ToolLZ77View", () => new ToolLZ77Form()),
                ("ToolDiffView", () => new ToolDiffForm()),
                ("ToolUPSPatchSimpleView", () => new ToolUPSPatchSimpleForm()),
                ("ToolUPSOpenSimpleView", () => new ToolUPSOpenSimpleForm()),
                ("ToolFlagNameView", () => new ToolFlagNameForm()),
                ("ToolUseFlagView", () => new ToolUseFlagForm()),
                ("ToolUnitTalkGroupView", () => new ToolUnitTalkGroupForm()),
                ("ToolASMInsertView", () => new ToolASMInsertForm()),
                ("HexEditorView", () => new HexEditorForm()),
                ("DisASMView", () => new DisASMForm()),
                ("LogViewerView", () => new LogForm()),
                // GrowSimulatorView has no WinForms Form counterpart (GrowSimulator is a utility class)
                // ("GrowSimulatorView", ...)
                ("OptionsView", () => new OptionForm()),

                // Status Screen Editors
                ("StatusParamView", () => new StatusParamForm()),
                ("StatusRMenuView", () => new StatusRMenuForm()),
                ("StatusUnitsMenuView", () => new StatusUnitsMenuForm()),
                ("StatusOptionOrderView", () => new StatusOptionOrderForm()),

                // Skill System Editors
                ("SkillAssignmentUnitCSkillSysView", () => new SkillAssignmentUnitCSkillSysForm()),
                ("SkillAssignmentClassCSkillSysView", () => new SkillAssignmentClassCSkillSysForm()),
                ("SkillAssignmentUnitFE8NView", () => new SkillAssignmentUnitFE8NForm()),
                ("SkillConfigFE8NSkillView", () => new SkillConfigFE8NSkillForm()),
                ("SkillConfigFE8NVer2SkillView", () => new SkillConfigFE8NVer2SkillForm()),
                ("SkillConfigFE8NVer3SkillView", () => new SkillConfigFE8NVer3SkillForm()),
                ("SkillConfigFE8UCSkillSys09xView", () => new SkillConfigCSkillSystem09xForm()),
                ("SkillSystemsEffectivenessReworkClassTypeView", () => new SkillSystemsEffectivenessReworkClassTypeForm()),

                // Song/Audio Dialogs
                ("ToolBGMMuteDialogView", () => new ToolBGMMuteDialogForm()),

                // Event/Text Sub-forms
                ("EventScriptCategorySelectView", () => new EventScriptFormCategorySelectForm()),
                // EventScriptPopupView maps to a UserControl, not a Form — skip
                // ("EventScriptPopupView", ...)
                ("ProcsScriptCategorySelectView", () => new ProcsScriptCategorySelectForm()),
                ("AIScriptCategorySelectView", () => new AIScriptCategorySelectForm()),
                ("TextScriptCategorySelectView", () => new TextScriptFormCategorySelectForm()),
                ("TextDicView", () => new TextDicForm()),
                ("TextCharCodeView", () => new TextCharCodeForm()),
                ("TextBadCharPopupView", () => new TextBadCharPopupForm()),
                ("TextRefAddDialogView", () => new TextRefAddDialogForm()),
                ("TextToSpeechView", () => new TextToSpeechForm()),

                // Graphics Tool Forms
                ("GraphicsToolView", () => new GraphicsToolForm()),
                ("GraphicsToolPatchMakerView", () => new GraphicsToolPatchMakerForm()),
                ("PaletteChangeColorsView", () => new PaletteChangeColorsForm()),
                ("PaletteClipboardView", () => new PaletteClipboardForm()),
                ("PaletteSwapView", () => new PaletteSwapForm()),
                ("ImageBGSelectPopupView", () => new ImageBGSelectPopupForm()),

                // Map Sub-dialog Forms
                ("MapEditorAddMapChangeDialogView", () => new MapEditorAddMapChangeDialogForm()),
                ("MapEditorMarSizeDialogView", () => new MapEditorMarSizeDialogForm()),
                ("MapEditorResizeDialogView", () => new MapEditorResizeDialogForm()),
                ("MapPointerNewPLISTPopupView", () => new MapPointerNewPLISTPopupForm()),
                ("MapStyleEditorAppendPopupView", () => new MapStyleEditorAppendPopupForm()),
                ("MapStyleEditorWarningOverrideView", () => new MapStyleEditorFormWarningVanillaTileOverraideForm()),
                ("MapStyleEditorImportImageOptionView", () => new MapStyleEditorImportImageOptionForm()),
                ("MapSettingDifficultyDialogView", () => new MapSettingDifficultyForm()),

                // Tool/Utility Forms Part 1
                ("DisASMDumpAllView", () => new DisASMDumpAllForm()),
                ("DisASMDumpAllArgGrepView", () => new DisASMDumpAllArgGrepForm()),
                ("HexEditorJumpView", () => new HexEditorJump()),
                ("HexEditorMarkView", () => new HexEditorMark()),
                ("HexEditorSearchView", () => new HexEditorSearch()),
                ("PointerToolView", () => new PointerToolForm()),
                ("PointerToolBatchInputView", () => new PointerToolBatchInputForm()),
                ("PointerToolCopyToView", () => new PointerToolCopyToForm()),
                ("PackedMemorySlotView", () => new PackedMemorySlotForm()),
                ("EmulatorMemoryView", () => new EmulatorMemoryForm()),

                // Tool/Utility Forms Part 2
                ("RAMRewriteToolMAPView", () => new RAMRewriteToolMAPForm()),
                ("ToolAnimationCreatorView", () => new ToolAnimationCreatorForm()),
                ("ToolThreeMargeView", () => new ToolThreeMargeForm()),
                ("ToolASMEditView", () => new ToolASMEditForm()),
                ("ToolExportEAEventView", () => new ToolExportEAEventForm()),
                ("ToolDecompileResultView", () => new ToolDecompileResultForm()),
                ("ToolChangeProjectnameView", () => new ToolChangeProjectnameForm()),
                ("ToolAutomaticRecoveryROMHeaderView", () => new ToolAutomaticRecoveryROMHeaderForm()),
                ("MoveToFreeSpaceView", () => new MoveToFreeSapceForm()),
                ("ToolSubtitleOverlayView", () => new ToolSubtitleOverlayForm()),
                ("ToolSubtitleSettingDialogView", () => new ToolSubtitleSetingDialogForm()),

                // Error/Dialog Forms
                ("ErrorReportView", () => new ErrorReportForm()),
                ("ErrorPaletteMissMatchView", () => new ErrorPaletteMissMatchForm()),
                ("ErrorPaletteShowView", () => new ErrorPaletteShowForm()),
                ("ErrorPaletteTransparentView", () => new ErrorPaletteTransparentForm()),
                ("ErrorTSAErrorView", () => new ErrorTSAErrorForm()),
                ("ErrorLongMessageDialogView", () => new ErrorLongMessageDialogForm()),
                ("ErrorUnknownROMView", () => new ErorrUnknownROM()),
                ("DumpStructSelectToTextDialogView", () => new DumpStructSelectToTextDialogForm()),
                ("HowDoYouLikePatchView", () => new HowDoYouLikePatchForm()),
                ("HowDoYouLikePatch2View", () => new HowDoYouLikePatch2Form()),
                ("PatchFilterExView", () => new PatchFilterExForm()),
                ("PatchFormUninstallDialogView", () => new PatchFormUninstallDialogForm()),

                // Version-Specific / Specialized Forms
                ("ItemFE6View", () => new ItemFE6Form()),
                ("MoveCostFE6View", () => new MoveCostFE6Form()),
                ("SupportUnitFE6View", () => new SupportUnitFE6Form()),
                ("SupportTalkFE6View", () => new SupportTalkFE6Form()),
                ("SupportTalkFE7View", () => new SupportTalkFE7Form()),
                ("UnitFE7View", () => new UnitFE7Form()),
                ("OPClassDemoFE7View", () => new OPClassDemoFE7Form()),
                ("OPClassDemoFE7UView", () => new OPClassDemoFE7UForm()),
                ("OPClassDemoFE8UView", () => new OPClassDemoFE8UForm()),
                ("OPClassFontFE8UView", () => new OPClassFontFE8UForm()),
                ("OPClassAlphaNameView", () => new OPClassAlphaNameForm()),
                ("OPClassAlphaNameFE6View", () => new OPClassAlphaNameFE6Form()),
                ("SomeClassListView", () => new SomeClassListForm()),
                ("VennouWeaponLockView", () => new VennouWeaponLockForm()),
                ("UnitsShortTextView", () => new UnitsShortTextForm()),
                ("UbyteBitFlagView", () => new UbyteBitFlagForm()),
                ("UshortBitFlagView", () => new UshortBitFlagForm()),
                ("UwordBitFlagView", () => new UwordBitFlagForm()),

                // Event Templates
                ("EventTemplate1View", () => new EventTemplate1Form()),
                ("EventTemplate2View", () => new EventTemplate2Form()),
                ("EventTemplate3View", () => new EventTemplate3Form()),
                ("EventTemplate4View", () => new EventTemplate4Form()),
                ("EventTemplate5View", () => new EventTemplate5Form()),
                ("EventTemplate6View", () => new EventTemplate6Form()),
                ("EventFinalSerifFE7View", () => new EventFinalSerifFE7Form()),
                ("EventMoveDataFE7View", () => new EventMoveDataFE7Form()),
                ("EventTalkGroupFE7View", () => new EventTalkGroupFE7Form()),

                // Audio Sub-Forms
                ("SongTrackChangeTrackView", () => new SongTrackChangeTrackForm()),
                ("SongTrackAllChangeTrackView", () => new SongTrackAllChangeTrackForm()),
                ("SongTrackImportSelectInstrumentView", () => new SongTrackImportSelectInstrumentForm()),
                ("SongTrackImportWaveView", () => new SongTrackImportWaveForm()),

                // ED/Credits + Item Variants
                ("EDFE6View", () => new EDFE6Form()),
                ("EDFE7View", () => new EDFE7Form()),
                ("EDSensekiCommentView", () => new EDSensekiCommentForm()),
                ("ItemStatBonusesSkillSystemsView", () => new ItemStatBonusesSkillSystemsForm()),
                ("ItemStatBonusesVennoView", () => new ItemStatBonusesVennoForm()),
                ("ItemEffectivenessSkillSystemsReworkView", () => new ItemEffectivenessSkillSystemsReworkForm()),
                ("ItemRandomChestView", () => new ItemRandomChestForm()),
                ("MenuExtendSplitMenuView", () => new MenuExtendSplitMenuForm()),

                // App Infrastructure
                ("VersionView", () => new VersionForm()),
                ("WelcomeView", () => new WelcomeForm()),
                ("ResourceView", () => new ResourceForm()),
                ("ToolInitWizardView", () => new ToolInitWizardForm()),
                ("ToolUndoPopupDialogView", () => new ToolUndoPopupDialogForm()),
                ("OpenLastSelectedFileView", () => new OpenLastSelectedFileForm()),
                ("ToolUpdateDialogView", () => new ToolUpdateDialogForm()),

                // Previously Unregistered On-Disk Views
                ("ToolAllWorkSupportView", () => new ToolAllWorkSupportForm()),
                ("ToolProblemReportView", () => new ToolProblemReportForm()),
                ("WorldMapPathMoveEditorView", () => new WorldMapPathMoveEditorForm()),
                ("MantAnimationView", () => new MantAnimationForm()),
                ("RAMRewriteToolView", () => new RAMRewriteToolForm()),
                ("MainSimpleMenuView", () => new MainSimpleMenuForm()),
                ("MainSimpleMenuEventErrorView", () => new MainSimpleMenuEventErrorForm()),
                ("MainSimpleMenuImageSubView", () => new MainSimpleMenuImageSubForm()),

                // Small Dialog/Message Views
                ("ToolEmulatorSetupMessageView", () => new ToolEmulatorSetupMessageForm()),
                ("ToolThreeMargeCloseAlertView", () => new ToolThreeMargeCloseAlertForm()),
                ("ToolClickWriteFloatControlPanelButtonView", () => new ToolClickWriteFloatControlPanelButtonForm()),
                ("ToolWorkSupport_UpdateQuestionDialogView", () => new ToolWorkSupport_UpdateQuestionDialogForm()),
                ("MainSimpleMenuEventErrorIgnoreErrorView", () => new MainSimpleMenuEventErrorIgnoreErrorForm()),
                ("ToolProblemReportSearchBackupView", () => new ToolProblemReportSearchBackupForm()),
                ("ToolProblemReportSearchSavView", () => new ToolProblemReportSearchSavForm()),

                // Tool Support Views
                ("ToolWorkSupportView", () => new ToolWorkSupportForm()),
                ("ToolWorkSupport_SelectUPSView", () => new ToolWorkSupport_SelectUPSForm()),
                ("ToolDiffDebugSelectView", () => new ToolDiffDebugSelectForm()),

                // Specialized Views
                ("SMEPromoListView", () => new SMEPromoListForm()),
                ("ToolRunHintMessageView", () => new ToolRunHintMessageForm()),
            };
        }
    }
}
