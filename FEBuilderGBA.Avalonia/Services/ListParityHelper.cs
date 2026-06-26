using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Helper for comparing Avalonia editor lists against reference lists
    /// generated directly from ROM data using the same pointer/size info
    /// that WinForms InputFormRef uses.
    /// </summary>
    public static class ListParityHelper
    {
        /// <summary>
        /// Delegate for building a reference list from ROM data.
        /// </summary>
        public delegate List<AddrResult> ReferenceListBuilder(ROM rom);

        /// <summary>
        /// Maps Avalonia editor view name to a reference list builder function and
        /// the WinForms form name (for reporting).
        /// </summary>
        static readonly Dictionary<string, (string WinFormsName, ReferenceListBuilder Builder)> EditorMap
            = new(StringComparer.Ordinal);

        static ListParityHelper()
        {
            // ---- Core data editors ----
            Register("UnitEditorView", "UnitForm", BuildUnitList);
            Register("ItemEditorView", "ItemForm", BuildItemList);
            Register("ClassEditorView", "ClassForm", BuildClassList);
            Register("PortraitViewerView", "ImagePortraitForm", BuildPortraitList);
            // ImagePortraitView uses a different format (with unit names) than the generic portrait list;
            // skip it here since PortraitViewerView already covers the portrait table.
            Register("ImageGenericEnemyPortraitView", "ImageGenericEnemyPortraitForm", BuildGenericEnemyPortraitList);
            Register("SoundRoomViewerView", "SoundRoomForm", BuildSoundRoomList);

            // ---- Support editors ----
            Register("SupportUnitEditorView", "SupportUnitForm", BuildSupportUnitList);
            Register("SupportTalkView", "SupportTalkForm", BuildSupportTalkList);
            Register("SupportAttributeView", "SupportAttributeForm", BuildSupportAttributeList);

            // ---- Map editors ----
            Register("MapSettingView", "MapSettingForm", BuildMapSettingList);
            Register("MapExitPointView", "MapExitPointForm", BuildMapExitPointList);
            Register("TerrainNameEditorView", "MapTerrainNameForm", BuildTerrainNameList);

            // ---- Song/Sound editors ----
            Register("SongTableView", "SongTableForm", BuildSongTableList);
            Register("SoundBossBGMViewerView", "SoundBossBGMForm", BuildSoundBossBGMList);

            // ---- World map editors ----
            Register("WorldMapPointView", "WorldMapPointForm", BuildWorldMapPointList);
            Register("WorldMapPathView", "WorldMapPathForm", BuildWorldMapPathList);
            Register("WorldMapBGMView", "WorldMapBGMForm", BuildWorldMapBGMList);

            // ---- Item sub-editors ----
            Register("ItemWeaponEffectViewerView", "ItemWeaponEffectForm", BuildItemWeaponEffectList);
            Register("ItemWeaponTriangleViewerView", "ItemWeaponTriangleForm", BuildItemWeaponTriangleList);

            // ---- Event editors ----
            Register("EventBattleTalkView", "EventBattleTalkForm", BuildEventBattleTalkList);
            Register("EventHaikuView", "EventHaikuForm", BuildEventHaikuList);
            Register("EventForceSortieView", "EventForceSortieForm", BuildEventForceSortieList);

            // ---- ED (ending) editor ----
            Register("EDView", "EDForm", BuildEDList);

            // ---- Class sub-editors ----
            Register("CCBranchEditorView", "CCBranchForm", BuildCCBranchList);
            Register("ClassOPDemoView", "OPClassDemoForm", BuildClassOPDemoList);
            Register("ClassOPFontView", "OPClassFontForm", BuildClassOPFontList);

            // ---- Menu editor ----
            Register("MenuDefinitionView", "MenuDefinitionForm", BuildMenuDefinitionList);

            // ---- Status option editor ----
            Register("StatusOptionView", "StatusOptionForm", BuildStatusOptionList);

            // ---- Arena editors ----
            Register("ArenaClassViewerView", "ArenaClassForm", BuildArenaClassList);
            Register("ArenaEnemyWeaponViewerView", "ArenaEnemyWeaponForm", BuildArenaEnemyWeaponList);

            // ---- Link arena deny ----
            Register("LinkArenaDenyUnitViewerView", "LinkArenaDenyUnitForm", BuildLinkArenaDenyUnitList);

            // ---- Monster probability ----
            Register("MonsterProbabilityViewerView", "MonsterProbabilityForm", BuildMonsterProbabilityList);

            // ---- Summon editors ----
            Register("SummonUnitViewerView", "SummonUnitForm", BuildSummonUnitList);
            Register("SummonsDemonKingViewerView", "SummonsDemonKingForm", BuildSummonsDemonKingList);

            // ---- AI editors ----
            Register("AIMapSettingView", "AIMapSettingForm", BuildAIMapSettingList);
            Register("AIPerformItemView", "AIPerformItemForm", BuildAIPerformItemList);
            Register("AIPerformStaffView", "AIPerformStaffForm", BuildAIPerformStaffList);
            Register("AIStealItemView", "AIStealItemForm", BuildAIStealItemList);
            Register("AITargetView", "AITargetForm", BuildAITargetList);

            // ---- Item sub-editors (batch 3) ----
            Register("ItemStatBonusesViewerView", "ItemStatBonusesForm", BuildItemStatBonusesList);
            Register("ItemEffectivenessViewerView", "ItemEffectivenessForm", BuildItemEffectivenessList);
            Register("ItemPromotionViewerView", "ItemPromotionForm", BuildItemPromotionList);
            Register("ItemShopViewerView", "ItemShopForm", BuildItemShopList);
            Register("ItemUsagePointerViewerView", "ItemUsagePointerForm", BuildItemUsagePointerList);
            Register("ItemEffectPointerViewerView", "ItemEffectPointerForm", BuildItemEffectPointerList);
            Register("ItemIconViewerView", "ImageItemIconForm", BuildItemIconList);

            // ---- Map/Terrain editors (batch 3) ----
            Register("MoveCostEditorView", "MoveCostForm", BuildMoveCostList);
            Register("MapTileAnimationView", "MapTileAnimationView(Avalonia)", BuildMapTileAnimationList);

            // ---- Units/Classes (batch 3) ----
            Register("ClassFE6View", "ClassForm", BuildClassFE6List);
            Register("UnitFE6View", "UnitFE6Form", BuildUnitFE6List);
            Register("UnitPaletteView", "UnitPaletteForm", BuildUnitPaletteList);
            Register("ExtraUnitFE8UView", "ExtraUnitFE8UForm", BuildExtraUnitFE8UList);

            // ---- Event editors (batch 3) ----
            Register("EventFunctionPointerView", "EventFunctionPointerForm", BuildEventFunctionPointerList);

            // ---- Sound editors (batch 3) ----
            Register("SoundFootStepsViewerView", "SoundFootStepsForm", BuildSoundFootStepsList);

            // ---- Text editors (batch 3) ----
            // TextDicView and TextCharCodeView use Huffman tree/TBL data, not pointer tables.
            // StatusRMenuView uses linked address lists. MenuCommandView/MenuExtendSplitMenuView
            // use dynamic base addresses from menu definitions. StatusUnitsMenuView/StatusOptionOrderView
            // use different data sources. These are left unmapped for now.

            // ---- Image/Graphics editors (batch 3) ----
            Register("EDStaffRollView", "EDStaffRollForm", BuildEDStaffRollList);
            Register("OPPrologueViewerView", "OPPrologueForm", BuildOPPrologueList);
            Register("BigCGViewerView", "BigCGForm", BuildBigCGList);
            Register("ChapterTitleViewerView", "ImageChapterTitleForm", BuildChapterTitleList);

            // ---- Battle animation editors (batch 3) ----
            Register("Command85PointerView", "Command85PointerForm", BuildCommand85PointerList);

            // ==================================================================
            // Batch 4 registrations
            // ==================================================================

            // ---- Event editors (FE6/FE7 variants) ----
            Register("EventCondView", "EventCondForm", BuildMapSettingList);
            Register("EventUnitView", "EventUnitForm", BuildMapSettingList);
            Register("EventUnitFE6View", "EventUnitFE6Form", BuildMapSettingList);
            Register("EventUnitFE7View", "EventUnitFE7Form", BuildMapSettingList);
            Register("EventBattleTalkFE6View", "EventBattleTalkFE6Form", BuildEventBattleTalkFE6List);
            Register("EventBattleTalkFE7View", "EventBattleTalkFE7Form", BuildEventBattleTalkFE7List);
            Register("EventHaikuFE6View", "EventHaikuFE6Form", BuildEventHaikuFE6List);
            Register("EventHaikuFE7View", "EventHaikuFE7Form", BuildEventHaikuFE7List);
            Register("EventForceSortieFE7View", "EventForceSortieFE7Form", BuildEventForceSortieFE7List);

            // ---- Map editors (FE6/FE7 variants) ----
            Register("MapSettingFE6View", "MapSettingFE6Form", BuildMapSettingList);
            Register("MapSettingFE7View", "MapSettingFE7Form", BuildMapSettingList);
            Register("MapSettingFE7UView", "MapSettingFE7UForm", BuildMapSettingList);
            Register("MapChangeView", "MapChangeForm", BuildMapChangeList);
            Register("MapPointerView", "MapPointerForm", BuildMapPointerList);
            Register("MapLoadFunctionView", "MapLoadFunctionForm", BuildMapLoadFunctionList);

            // ---- Image/Graphics editors ----
            Register("ImagePortraitView", "ImagePortraitForm", BuildImagePortraitList);
            Register("ImagePortraitFE6View", "ImagePortraitFE6Form", BuildImagePortraitFE6List);
            Register("ImageBGView", "ImageBGForm", BuildImageBGList);
            Register("ImageBattleAnimeView", "ImageBattleAnimeForm", BuildImageBattleAnimeList);
            Register("ImageBattleBGView", "ImageBattleBGForm", BuildImageBattleBGList);
            Register("ImageCGView", "ImageCGForm", BuildImageCGList);
            Register("ImageUnitPaletteView", "ImageUnitPaletteForm", BuildImageUnitPaletteList);
            Register("ImageUnitWaitIconView", "ImageUnitWaitIconForm", BuildImageUnitWaitIconList);
            Register("ImageUnitMoveIconView", "ImageUnitMoveIconForm", BuildImageUnitMoveIconList);
            Register("ImageSystemAreaView", "ImageSystemAreaForm", BuildImageSystemAreaList);
            Register("ImageTSAAnimeView", "ImageTSAAnimeForm", BuildImageTSAAnimeList);
            Register("BattleBGViewerView", "BattleBGForm", BuildBattleBGViewerList);
            Register("BattleTerrainViewerView", "BattleTerrainForm", BuildBattleTerrainList);

            // ---- Sound editors (FE6 variant) ----
            Register("SoundRoomFE6View", "SoundRoomFE6Form", BuildSoundRoomFE6List);

            // ---- Monster editors ----
            // MonsterItemForm exposes 3 list builders in WinForms
            // (MakeAllDataLength registers MonsterItemForm / MonsterItemFormProbability /
            // MonsterItemFormTable). The Avalonia counterpart is a single 3-tab view,
            // so EditorMap holds the canonical pair (MonsterItemViewerView ->
            // MonsterItemForm). The additional builders `BuildMonsterItemProbabilityList`
            // and `BuildMonsterItemHoldingsList` are exposed below for the
            // MonsterItemParityTests round-trip tests covering tabs 2 + 3. See #394.
            Register("MonsterItemViewerView", "MonsterItemForm", BuildMonsterItemList);
            Register("MonsterWMapProbabilityViewerView", "MonsterWMapProbabilityForm", BuildMonsterWMapProbabilityList);

            // ---- Status/Menu editors ----
            Register("StatusOptionOrderView", "StatusOptionOrderForm", BuildStatusOptionOrderList);
            Register("StatusRMenuView", "StatusRMenuForm", BuildStatusRMenuList);
            Register("StatusUnitsMenuView", "StatusUnitsMenuForm", BuildStatusUnitsMenuList);
            Register("MenuCommandView", "MenuCommandForm", BuildMenuCommandList);
            Register("MenuExtendSplitMenuView", "MenuExtendSplitMenuForm", BuildMenuExtendSplitMenuList);

            // ---- Text editors ----
            Register("TextDicView", "TextDicForm", BuildTextDicList);

            // ---- Unit/Class FE7/FE6 variants ----
            Register("UnitFE7View", "UnitFE7Form", BuildUnitFE7List);
            Register("MoveCostFE6View", "MoveCostFE6Form", BuildMoveCostList);
            Register("SupportUnitFE6View", "SupportUnitFE6Form", BuildSupportUnitFE6List);
            Register("SupportTalkFE6View", "SupportTalkFE6Form", BuildSupportTalkFE6List);
            Register("SupportTalkFE7View", "SupportTalkFE7Form", BuildSupportTalkFE7List);

            // ---- OP Class editors ----
            Register("OPClassAlphaNameView", "OPClassAlphaNameForm", BuildOPClassAlphaNameList);
            Register("OPClassDemoViewerView", "OPClassDemoViewerForm", BuildOPClassDemoViewerList);
            Register("OPClassFontViewerView", "OPClassFontViewerForm", BuildOPClassFontViewerList);

            // ==================================================================
            // Batch 5 registrations — remaining coverable editors
            // ==================================================================

            // ---- Unit sub-editors ----
            Register("UnitActionPointerView", "UnitActionPointerForm", BuildUnitActionPointerList);
            Register("UnitIncreaseHeightView", "UnitIncreaseHeightForm", BuildUnitIncreaseHeightList);
            Register("UnitCustomBattleAnimeView", "UnitCustomBattleAnimeForm", BuildUnitCustomBattleAnimeList);
            Register("ExtraUnitView", "ExtraUnitForm", BuildExtraUnitList);

            // ---- Map editors ----
            Register("MapEditorView", "MapEditorForm", BuildMapEditorList);
            Register("MapStyleEditorView", "MapStyleEditorForm", BuildMapStyleEditorList);
            Register("MapTerrainBGLookupTableView", "MapTerrainBGLookupTableForm", BuildMapTerrainBGLookupTableList);
            Register("MapTerrainFloorLookupTableView", "MapTerrainFloorLookupTableForm", BuildMapTerrainFloorLookupTableList);
            Register("MapMiniMapTerrainImageView", "MapMiniMapTerrainImageForm", BuildMapMiniMapTerrainImageList);
            Register("MapTileAnimation1View", "MapTileAnimation1Form", BuildMapTileAnimation1List);
            Register("MapTileAnimation2View", "MapTileAnimation2Form", BuildMapTileAnimation2List);
            Register("MapTerrainNameEngView", "MapTerrainNameEngForm", BuildMapTerrainNameEngList);
            // MapChangeView already registered in batch 4 — no duplicate here

            // ---- Event editors (FE7 variants) ----
            Register("EventFunctionPointerFE7View", "EventFunctionPointerFE7Form", BuildEventFunctionPointerFE7List);
            // EventBattleTalkFE6View/FE7View already registered in batch 4 — no duplicates here
            Register("EventMapChangeView", "EventMapChangeForm", BuildEventMapChangeList);
            Register("EventFinalSerifFE7View", "EventFinalSerifFE7Form", BuildEventFinalSerifFE7List);

            // ---- Image/Graphics editors ----
            Register("ImageCGFE7UView", "ImageCGFE7UForm", BuildImageCGFE7UList);
            Register("ImageMagicFEditorView", "ImageMagicFEditorForm", BuildImageMagicFEditorList);
            Register("ImageMapActionAnimationView", "ImageMapActionAnimationForm", BuildImageMapActionAnimationList);
            Register("ImageChapterTitleFE7View", "ImageChapterTitleFE7Form", BuildImageChapterTitleFE7List);
            Register("ImageTSAAnime2View", "ImageTSAAnime2Form", BuildImageTSAAnime2List);

            // ---- Sound editors ----
            Register("SongTrackView", "SongTrackForm", BuildSongTrackList);
            Register("SoundRoomCGView", "SoundRoomCGForm", BuildSoundRoomCGList);

            // ---- World map editors ----
            Register("WorldMapEventPointerView", "WorldMapEventPointerForm", BuildWorldMapEventPointerList);

            // ---- ED (ending) editors ----
            Register("EDSensekiCommentView", "EDSensekiCommentForm", BuildEDSensekiCommentList);

            // ---- Status/Menu editors ----
            Register("StatusParamView", "StatusParamForm", BuildStatusParamList);

            // ---- OP Class editors (version-specific) ----
            Register("OPClassDemoFE7View", "OPClassDemoFE7Form", BuildOPClassDemoFE7List);
            Register("OPClassDemoFE7UView", "OPClassDemoFE7UForm", BuildOPClassDemoFE7UList);
            Register("OPClassDemoFE8UView", "OPClassDemoFE8UForm", BuildOPClassDemoFE8UList);
            Register("OPClassFontFE8UView", "OPClassFontFE8UForm", BuildOPClassFontFE8UList);
            Register("OPClassAlphaNameFE6View", "OPClassAlphaNameFE6Form", BuildOPClassAlphaNameFE6List);

            // ---- Monster editor ----
            Register("MantAnimationView", "MantAnimationForm", BuildMantAnimationList);

            // ItemStatBonusesSkillSystems, ItemStatBonusesVenno, EventMoveDataFE7,
            // EventTalkGroupFE7, EventBattleDataFE7 are all context/patch-dependent.
            // Moved to ContextDependentEditors set instead of stub-registered builders.
        }

        /// <summary>
        /// Set of Avalonia editor views that are tools/dialogs without an address list.
        /// These show "NO_LIST" instead of "SKIP" in --list-parity output.
        /// </summary>
        static readonly HashSet<string> NoListEditors = new(StringComparer.Ordinal)
        {
            // Tool / utility views
            "DataExportView", "DecreaseColorTSAToolView", "DevTranslateView",
            "DisASMView", "DisASMDumpAllView", "DisASMDumpAllArgGrepView",
            "DumpStructSelectDialogView", "DumpStructSelectToTextDialogView",
            "EmulatorMemoryView", "EventAssemblerView",
            "FontEditorView", "FontZHView",
            "GraphicsToolView", "GraphicsToolPatchMakerView",
            "GrowSimulatorView",
            "HexEditorView", "HexEditorJumpView", "HexEditorMarkView", "HexEditorSearchView",
            "HowDoYouLikePatchView", "HowDoYouLikePatch2View",
            // Error / dialog views
            "ErrorLongMessageDialogView", "ErrorPaletteMissMatchView",
            "ErrorPaletteShowView", "ErrorPaletteTransparentView",
            "ErrorReportView", "ErrorTSAErrorView", "ErrorUnknownROMView",
            // Template / script views (not list-based)
            "EventTemplate1View", "EventTemplate2View", "EventTemplate3View",
            "EventTemplate4View", "EventTemplate5View", "EventTemplate6View",
            "EventTemplateImplView",
            "EventScriptPopupView", "EventScriptTemplateView",
            "EventScriptCategorySelectView",
            "AIScriptCategorySelectView",
            // Misc tool/dialog views
            "CStringView",
            "ToolExportEAEventView",
            // Batch 4 — additional tool/dialog/popup views without address lists
            "ImageViewerView",
            "LogViewerView",
            "MainSimpleMenuEventErrorIgnoreErrorView",
            "MapEditorAddMapChangeDialogView",
            "MapEditorMarSizeDialogView",
            "MapEditorResizeDialogView",
            "MapPointerNewPLISTPopupView",
            "MapSettingDifficultyDialogView",
            "MapStyleEditorAppendPopupView",
            "MapStyleEditorImportImageOptionView",
            "MapStyleEditorWarningOverrideView",
            "MoveToFreeSpaceView",
            "OpenLastSelectedFileView",
            "OptionsView",
            "PackedMemorySlotView",
            "PaletteChangeColorsView",
            "PaletteClipboardView",
            "PaletteSwapView",
            "PatchFilterExView",
            "PatchFormUninstallDialogView",
            "PointerToolBatchInputView",
            "PointerToolCopyToView",
            "PointerToolView",
            "ProcsScriptCategorySelectView",
            "RAMRewriteToolMAPView",
            "RAMRewriteToolView",
            "ResourceView",
            "TextBadCharPopupView",
            "TextRefAddDialogView",
            "TextScriptCategorySelectView",
            "TextToSpeechView",
            "UbyteBitFlagView",
            "UshortBitFlagView",
            "UwordBitFlagView",
            "VersionView",
            "WelcomeView",
            // Batch 5 — additional tool/dialog/popup views without address lists
            "ToolUndoView", "ToolFELintView", "ToolROMRebuildView",
            "ToolLZ77View", "ToolDiffView",
            "ToolUPSPatchSimpleView", "ToolUPSOpenSimpleView",
            "ToolFlagNameView", "ToolUseFlagView",
            "ToolUnitTalkGroupView", "ToolASMInsertView",
            "ToolCustomBuildView",
            "ToolAnimationCreatorView", "ToolThreeMargeView",
            "ToolASMEditView", "ToolDecompileResultView",
            "ToolChangeProjectnameView", "ToolAutomaticRecoveryROMHeaderView",
            "ToolSubtitleOverlayView", "ToolSubtitleSettingDialogView",
            "ToolBGMMuteDialogView",
            "ToolInitWizardView", "ToolUndoPopupDialogView", "ToolUpdateDialogView",
            "ToolAllWorkSupportView", "ToolProblemReportView",
            "ToolEmulatorSetupMessageView", "ToolThreeMargeCloseAlertView",
            "ToolClickWriteFloatControlPanelButtonView",
            "ToolWorkSupport_UpdateQuestionDialogView",
            "ToolProblemReportSearchBackupView", "ToolProblemReportSearchSavView",
            "ToolWorkSupportView", "ToolWorkSupport_SelectUPSView",
            "ToolDiffDebugSelectView", "ToolRunHintMessageView",
            "ImageBGSelectPopupView",
            "ImagePortraitImporterView",
            "SongExchangeView",
            "SongInstrumentImportWaveView",
            "SongTrackImportMidiView",
            "SongTrackChangeTrackView", "SongTrackAllChangeTrackView",
            "SongTrackImportSelectInstrumentView",
            "ToolTranslateROMView",
            "PatchManagerView",
            "MainSimpleMenuView", "MainSimpleMenuEventErrorView", "MainSimpleMenuImageSubView",
            "MapSettingDifficultyView",
            "TextViewerView", "TextMainView", "OtherTextView",
            "TextEscapeEditorView", "TextCharCodeView",
            "OAMSPView",
            "EDFE6View", "EDFE7View",
            "WorldMapPathEditorView",
            "WorldMapImageView", "WorldMapImageFE6View", "WorldMapImageFE7View",
            "WorldMapEventPointerFE6View", "WorldMapEventPointerFE7View",
            "WorldMapPathMoveEditorView",
            "ImageBattleScreenView",
            "ImagePalletView",
            "ImageRomAnimeView",
            "ImageTSAEditorView",
            "ImageMagicCSACreatorView",
            "ProcsScriptView", "EventScriptView", "AIScriptView",
            // #1431 — AOE Range editor: a real address-driven sub-editor (manual
            // address input + dynamic w×h grid + repoint-on-write), NOT an
            // enumerable list. WinForms AOERANGEForm itself has no list (it exposes
            // ReadStartAddress + Reload). No-list, address-driven — like the System
            // image viewers above.
            "AOERANGEView",
            // #1444 — Unit Color picker: a real self-contained 4-slot colour
            // value picker (no ROM-table list; WinForms EventUnitColorForm is a
            // transient UNIT_COLOR argument picker). Surfaced both standalone and
            // from the event-script editor's UNIT_COLOR "Pick…" button.
            "EventUnitColorView",
        };

        /// <summary>
        /// Set of Avalonia editor views that are context-dependent sub-editors.
        /// These require a parent context (base address from parent editor) to display data.
        /// They show "CONTEXT_DEPENDENT" instead of "SKIP" in --list-parity output.
        /// </summary>
        static readonly HashSet<string> ContextDependentEditors = new(StringComparer.Ordinal)
        {
            // AI sub-editors (need script address from parent)
            "AIASMCALLTALKView", "AIASMCoordinateView", "AIASMRangeView",
            "AITilesView", "AIUnitsView",
            // Song sub-editors (need instrument base from parent SongTrack)
            "SongInstrumentView", "SongInstrumentDirectSoundView",
            // Map terrain lookup (need base address from parent terrain editor)
            "MapTerrainBGLookupView", "MapTerrainFloorLookupView",
            // Class/unit sub-editors (need base address from parent)
            "SomeClassListView", "UnitsShortTextView",
            // Event sub-editors (need parent event context)
            // #1444 — EventUnitColorView promoted to NoListEditors (now a real
            // self-contained 4-slot colour picker invoked from the event editor,
            // no longer a parent-context-dependent placeholder).
            "EventUnitItemDropView", "EventUnitNewAllocView",
            // #1431 — AOERANGEView moved to NoListEditors (now a real address-driven
            // editor, no longer a stub that depends on parent context).
            // System image viewers (single-entry, address-driven)
            "SystemIconViewerView", "SystemHoverColorViewerView",
            // Item sub-editors (patch-dependent, need parent context)
            "ItemRandomChestView",
            "ItemEffectivenessSkillSystemsReworkView",
            "VennouWeaponLockView",
            // Promo list sub-editor (needs parent address)
            "SMEPromoListView",
            // Skill editors (all patch-dependent, need patch to be installed)
            "SkillAssignmentUnitSkillSystemView", "SkillAssignmentClassSkillSystemView",
            "SkillConfigSkillSystemView",
            "SkillAssignmentUnitCSkillSysView", "SkillAssignmentClassCSkillSysView",
            "SkillAssignmentUnitFE8NView",
            "SkillConfigFE8NSkillView", "SkillConfigFE8NVer2SkillView",
            "SkillConfigFE8NVer3SkillView", "SkillConfigFE8UCSkillSys09xView",
            // FE8 SkillSystems spell-menu (Gaiden-style spell list) — patch-dependent;
            // empty on vanilla FE8U until the FE8SpellMenu patch is installed (#1167).
            "FE8SpellMenuExtendsView",
            // Image sub-editors (need parent context or specific address)
            "ImageBattleAnimePalletView",
            // Patch-dependent stat bonuses editors (need SkillSystem/Venno patch installed)
            "ItemStatBonusesSkillSystemsView", "ItemStatBonusesVennoView",
            // FE7 event sub-editors (context-dependent, find address from event scripts at runtime)
            "EventMoveDataFE7View", "EventTalkGroupFE7View", "EventBattleDataFE7View",
        };

        static void Register(string avaloniaName, string winFormsName, ReferenceListBuilder builder)
        {
            EditorMap[avaloniaName] = (winFormsName, builder);
        }

        /// <summary>Check if a given Avalonia editor name has a known reference list builder.</summary>
        public static bool HasMapping(string avaloniaEditorName) => EditorMap.ContainsKey(avaloniaEditorName);

        /// <summary>Check if an Avalonia editor is known to have no address list (tool/dialog).</summary>
        public static bool IsNoListEditor(string avaloniaEditorName) => NoListEditors.Contains(avaloniaEditorName);

        /// <summary>Check if an Avalonia editor is a context-dependent sub-editor (needs parent context).</summary>
        public static bool IsContextDependentEditor(string avaloniaEditorName) => ContextDependentEditors.Contains(avaloniaEditorName);

        /// <summary>Get the WinForms form name for reporting.</summary>
        public static (string FormType, string MethodName)? GetMapping(string avaloniaEditorName)
        {
            if (EditorMap.TryGetValue(avaloniaEditorName, out var entry))
                return (entry.WinFormsName, "MakeList");
            return null;
        }

        /// <summary>Get all mapped editor names.</summary>
        public static IReadOnlyCollection<string> GetAllMappedEditors() => EditorMap.Keys;

        /// <summary>
        /// Cross-editor mappings that are NOT list-based parity pairs but
        /// ARE relevant to the Phase 4 jump scanner. For example:
        /// `PatchForm` (the WF patch list) corresponds to `PatchManagerView`
        /// in Avalonia, but the AV view has its own data shape (categories,
        /// installed-state filtering) and isn't a row-by-row port. Without
        /// declaring the pair here, every `JumpForm&lt;PatchForm&gt;()` callsite
        /// in WinForms resolves to "no AV counterpart" in the jumps sweep
        /// (issue #442 / #441 / #438 / etc), which makes the report look like
        /// many AV nav gaps that are actually present in another shape.
        ///
        /// Maps Avalonia view name → WinForms form name. Consumed by
        /// <c>JumpParityScanner.BuildWfFormToAvViewsMap</c> after the
        /// authoritative ListParityHelper seed and the PairMatcher heuristic
        /// pass — strictly additive, never overrides earlier layers.
        /// </summary>
        static readonly Dictionary<string, string> KnownExtraCrossViewMappings = new(StringComparer.Ordinal)
        {
            // #442 / #441 — MapTerrain{BG,Floor}LookupTableForm jumps to PatchForm
            // to drive the user to install/configure the ExtendsBattleBG patch.
            // The Avalonia counterpart is PatchManagerView (different data shape;
            // not a list-parity port).
            { "PatchManagerView", "PatchForm" },
            // #433 / #500 — ImageMapActionAnimationForm, ImageMagicFEditorForm,
            // and ImageMagicCSACreatorForm all jump to ToolAnimationCreatorForm
            // via X_N_JumpEditor (the "edit animation" affordance). The Avalonia
            // ToolAnimationCreatorView is registered in NoListEditors (no
            // list-parity port — same shape as ToolDiffView, ToolUPSPatchSimpleView,
            // etc.), so without this entry the jump scanner resolves to "no
            // AV counterpart" and the manifest can never reach KnownGap /
            // Match status.
            { "ToolAnimationCreatorView", "ToolAnimationCreatorForm" },
            // #434 — Image*Form (BattleBG, ImageBG, ImageCG family) jumps to
            // these tool/utility views. Both are NoListEditors (above) so the
            // ListParityHelper.EditorMap doesn't register them; declare the
            // WF↔AV form pair here so JumpParityScanner can resolve them.
            { "GraphicsToolView", "GraphicsToolForm" },
            { "DecreaseColorTSAToolView", "DecreaseColorTSAToolForm" },
            // #430 — SkillConfigCSkillSystem09xForm (WF class name in the file
            // FEBuilderGBA/SkillConfigFE8UCSkillSys09xForm.cs - the FE8U prefix
            // is only on the filename, NOT on the actual class declaration)
            // jumps to ToolAnimationCreatorForm via X_N_JumpEditor (same shape
            // as #433). The Avalonia view is in ContextDependentEditors
            // (patch-dependent: it needs the CSkillSys patch installed to
            // populate its list, so the gap-sweep treats it as a sub-editor
            // that depends on parent context), so the EditorMap layer above
            // doesn't seed it. Without this entry, the
            // SkillConfigCSkillSystem09xForm WF callsite resolves to "no AV
            // counterpart" and the navigation manifest never lifts past
            // MissingAvManifest. Copilot bot review on PR #516 (round 4).
            { "SkillConfigFE8UCSkillSys09xView", "SkillConfigCSkillSystem09xForm" },
            // #427 — SkillConfigSkillSystemForm jumps to ToolAnimationCreatorForm
            // via X_N_JumpEditor (same shape as #430 / #433). The Avalonia view
            // is patch-dependent (needs the SkillSystems patch installed to
            // populate its list), so the EditorMap layer doesn't seed it.
            // Declare the WF↔AV form pair here so JumpParityScanner can
            // resolve the cross-ref.
            { "SkillConfigSkillSystemView", "SkillConfigSkillSystemForm" },
            // #396 — SkillConfigFE8NVer2SkillForm jumps to ToolAnimationCreatorForm
            // via X_N_JumpEditor (same shape as #427/#430/#433). The Avalonia view
            // is patch-dependent (needs the FE8N v2 skill patch installed to
            // populate its list), so the EditorMap layer doesn't seed it.
            // Declare the WF↔AV form pair here so JumpParityScanner can
            // resolve the cross-ref.
            { "SkillConfigFE8NVer2SkillView", "SkillConfigFE8NVer2SkillForm" },
            // #392 — SkillConfigFE8NVer3SkillForm jumps to PatchForm (combat-art
            // navigation), ErrorPaletteShowForm (palette-mismatch dialog), and
            // ToolAnimationCreatorForm (X_N_JumpEditor). The Avalonia view is
            // patch-dependent (needs the FE8N v3 skill patch installed to
            // populate its list), so the EditorMap layer doesn't seed it.
            // Declare the WF↔AV form pair here so JumpParityScanner can resolve
            // the cross-ref. Mirrors the exact pattern PR #598 used for v2.
            { "SkillConfigFE8NVer3SkillView", "SkillConfigFE8NVer3SkillForm" },
            // #390 — SkillConfigFE8NSkillForm (FE8N v1 / yugudora) jumps to
            // ToolAnimationCreatorForm via X_N_JumpEditor (same shape as
            // #396/#427/#430/#433). Patch-dependent (needs FE8N v1 / yugudora
            // skill patch installed to populate its list), so the EditorMap
            // layer doesn't seed it. Declare the WF↔AV form pair here so
            // JumpParityScanner can resolve the cross-ref.
            { "SkillConfigFE8NSkillView", "SkillConfigFE8NSkillForm" },
            // #416 - SkillAssignmentClassSkillSystemForm is patch-dependent on
            // SkillSystems and lives in ContextDependentEditors. Declare the
            // WF<->AV form pair here so JumpParityScanner can resolve the
            // cross-ref even though it is not in the EditorMap seed.
            { "SkillAssignmentClassSkillSystemView", "SkillAssignmentClassSkillSystemForm" },
            // #995 - SkillAssignmentUnitSkillSystemForm is patch-dependent on
            // SkillSystems and lives in ContextDependentEditors. Declare the
            // WF<->AV form pair here so JumpParityScanner can resolve the
            // cross-ref even though it is not in the EditorMap seed.
            { "SkillAssignmentUnitSkillSystemView", "SkillAssignmentUnitSkillSystemForm" },
            // #429 — ImageBGForm jumps to the BG-mode-select popup (16/255/224)
            // under the BG256Color patch. Popup is a dialog (NoListEditor);
            // declare the WF↔AV form pair so JumpParityScanner can resolve it.
            { "ImageBGSelectPopupView", "ImageBGSelectPopupForm" },
            // #420 — EventUnitForm jumps to the New Allocation modal dialog
            // (sets unit count to allocate) and the Item Drop Yes/No/Cancel
            // dialog. Both Avalonia views are stubs in ContextDependentEditors
            // (sub-editors of EventUnit*View), so the EditorMap layer doesn't
            // seed them. Declare the WF↔AV form pair so JumpParityScanner can
            // resolve the cross-ref and the navigation manifest reaches Match.
            { "EventUnitNewAllocView", "EventUnitNewAllocForm" },
            { "EventUnitItemDropView", "EventUnitItemDropForm" },
            // #386 — EventCondForm jumps to EventScriptForm (for the event
            // pointer) and to MapPointerNewPLISTPopupForm (PLIST allocation).
            // EventScriptView is registered in NoListEditors (script editor —
            // no list-parity port), and MapPointerNewPLISTPopupView is a popup
            // dialog (sub-editor of MapPointerForm). The EditorMap layer
            // doesn't seed them; declare the WF↔AV form pair so
            // JumpParityScanner can resolve the cross-refs from
            // EventCondViewModel.NavigationTargets.
            { "EventScriptView", "EventScriptForm" },
            { "MapPointerNewPLISTPopupView", "MapPointerNewPLISTPopupForm" },
        };

        /// <summary>
        /// Public accessor for <see cref="KnownExtraCrossViewMappings"/>.
        /// Used by <c>JumpParityScanner.BuildWfFormToAvViewsMap</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetExtraCrossViewMappings()
            => KnownExtraCrossViewMappings;

        /// <summary>
        /// Build a reference list for the given editor using Core ROM data.
        /// Returns null if the editor has no mapping.
        /// </summary>
        public static List<AddrResult> BuildReferenceList(string avaloniaEditorName)
        {
            if (!EditorMap.TryGetValue(avaloniaEditorName, out var entry))
                return null;

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return null;

            try
            {
                return entry.Builder(rom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LISTPARITY: Error building reference list for {avaloniaEditorName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compare two lists of AddrResult row-by-row.
        /// Returns a comparison result.
        /// </summary>
        public static ListParityResult CompareLists(string editorName, IReadOnlyList<AddrResult> avaloniaList, List<AddrResult> referenceList)
        {
            var result = new ListParityResult
            {
                EditorName = editorName,
                AvaloniaCount = avaloniaList.Count,
                WinFormsCount = referenceList.Count,
            };

            int minCount = Math.Min(avaloniaList.Count, referenceList.Count);
            int textMatches = 0;

            for (int i = 0; i < minCount; i++)
            {
                var av = avaloniaList[i];
                var rf = referenceList[i];

                bool addrMatch = av.addr == rf.addr;
                // Normalize whitespace and compare text (trim leading/trailing)
                string avText = (av.name ?? "").Trim();
                string rfText = (rf.name ?? "").Trim();
                bool textMatch = string.Equals(avText, rfText, StringComparison.Ordinal);

                if (textMatch)
                    textMatches++;

                if (!addrMatch && result.FirstAddrDiffIndex < 0)
                {
                    result.FirstAddrDiffIndex = i;
                    result.FirstAddrDiffAvalonia = av.addr;
                    result.FirstAddrDiffWinForms = rf.addr;
                }

                if (!textMatch && result.FirstTextDiffIndex < 0)
                {
                    result.FirstTextDiffIndex = i;
                    result.FirstTextDiffAvalonia = avText;
                    result.FirstTextDiffWinForms = rfText;
                }
            }

            result.TextMatches = textMatches;
            result.IsMatch = avaloniaList.Count == referenceList.Count
                          && result.FirstAddrDiffIndex < 0
                          && result.FirstTextDiffIndex < 0;

            return result;
        }

        // ------------------------------------------------------------------
        // Reference list builders — replicate InputFormRef.MakeList() logic
        // using Core ROM data directly, matching the Avalonia VM patterns
        // ------------------------------------------------------------------

        static string GetTextById(uint id)
        {
            try { return NameResolver.GetTextById(id); }
            catch { return "???"; }
        }

        /// <summary>Build unit list matching UnitEditorViewModel.LoadUnitList().</summary>
        static List<AddrResult> BuildUnitList(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            // FE6: skip first entry
            if (rom.RomInfo.version == 6)
                baseAddr += dataSize;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i + 1) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item list matching ItemEditorViewModel.LoadItemList().</summary>
        static List<AddrResult> BuildItemList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            bool fe8SingleByte = ItemListPredicate.IsFE8SingleByte(rom);
            var result = new List<AddrResult>();
            // Issue #364: same WinForms-mirroring stop predicate as ItemEditorViewModel.
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (!ItemListPredicate.IsValidEntry(rom, (int)i, addr, fe8SingleByte)) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build class list matching ClassEditorViewModel.LoadClassList().</summary>
        static List<AddrResult> BuildClassList(ROM rom)
        {
            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Match VM: stop when ClassNumber (u8 @ offset +4) is 0 for i > 0
                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build portrait list matching PortraitViewerViewModel.LoadPortraitList().</summary>
        static List<AddrResult> BuildPortraitList(ROM rom)
        {
            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            // PortraitViewerViewModel treats a datasize of 0 as 28 bytes.
            if (dataSize == 0) dataSize = 28;

            var result = new List<AddrResult>();
            int nullCount = 0;
            // PortraitViewerViewModel scans up to 0x400 entries with pointer-validity
            // and null-run heuristics to determine the list end.
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0)
                {
                    uint u0 = rom.u32(addr + 0);
                    uint u4 = rom.u32(addr + 4);
                    uint u8 = rom.u32(addr + 8);

                    if (!U.isPointerOrNULL(u0) || !U.isPointerOrNULL(u4) || !U.isPointerOrNULL(u8))
                        break;
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {
                        nullCount++;
                        if (nullCount >= 100) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build generic enemy portrait list matching ImageGenericEnemyPortraitViewModel.LoadList().</summary>
        static List<AddrResult> BuildGenericEnemyPortraitList(ROM rom)
        {
            uint ptr = rom.RomInfo.generic_enemy_portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint count = rom.RomInfo.generic_enemy_portrait_count;
            if (count == 0) return new List<AddrResult>();

            // Each entry is a pointer (4 bytes)
            uint dataSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Match ImageGenericEnemyPortraitViewModel.LoadList() formatting:
                // "0x{i:X2} {ptrStr}" where ptrStr is "0x????????" or "NULL".
                uint imgPtr = rom.u32(addr);
                string ptrStr = U.isPointer(imgPtr) ? $"0x{imgPtr:X08}" : "NULL";
                string name = $"0x{i:X2} {ptrStr}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build sound room list matching SoundRoomViewerViewModel.LoadSoundRoomList().</summary>
        static List<AddrResult> BuildSoundRoomList(ROM rom)
        {
            uint ptr = rom.RomInfo.sound_room_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.sound_room_datasize;
            // VM requires a non-zero data size; if zero, treat as not present.
            if (dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // End-of-list sentinel
                if (rom.u32(addr) == 0xFFFFFFFF) break;
                // Large empty block detection (matches VM: i > 10 && IsEmpty for 10 entries)
                if (i > 10 && rom.IsEmpty(addr, dataSize * 10)) break;

                uint songId = rom.u16(addr);
                string songName = NameResolver.GetSongName(songId);
                string name = $"{(i + 1):D3} {songName} (0x{songId:X04})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Support editors
        // ------------------------------------------------------------------

        /// <summary>Build support unit list matching SupportUnitEditorViewModel.
        /// Includes gap tolerance: when u16(addr)==0 for i>0, look ahead up to 4 entries.</summary>
        static List<AddrResult> BuildSupportUnitList(ROM rom)
        {
            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            const uint blockSize = 24;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint firstWord = rom.u16(addr);
                if (firstWord == 0 && i > 0)
                {
                    // Gap tolerance: look ahead up to 4 entries for more data
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = baseAddr + (i + j) * blockSize;
                        if (checkAddr + blockSize > (uint)rom.Data.Length) break;
                        if (rom.u16(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                string unitName = NameResolver.GetUnitName(i);
                string name = $"{U.ToHexString(i)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build support talk list matching SupportTalkViewModel.</summary>
        static List<AddrResult> BuildSupportTalkList(ROM rom)
        {
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, blockSize * 10)) break;

                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 2);
                // 1-based ROM-stored unit IDs. (#653)
                string n1 = NameResolver.GetUnitNameByOneBasedId(uid1);
                string n2 = NameResolver.GetUnitNameByOneBasedId(uid2);
                string name = $"{U.ToHexString(i)} {n1} (0x{uid1:X02}) & {n2} (0x{uid2:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build support attribute list matching SupportAttributeViewModel.</summary>
        static List<AddrResult> BuildSupportAttributeList(ROM rom)
        {
            uint ptr = rom.RomInfo.support_attribute_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            string[] affinityNames = { "None", "Fire", "Thunder", "Wind", "Ice", "Dark", "Light", "Anima" };
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint v = rom.u8(addr);
                if (v == 0) break;

                string affName = v < affinityNames.Length ? affinityNames[v] : $"0x{v:X02}";
                string name = $"{U.ToHexString(i + 1)} {affName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Map editors
        // ------------------------------------------------------------------

        /// <summary>Build map setting list matching MapSettingViewModel (delegates to MapSettingCore).</summary>
        static List<AddrResult> BuildMapSettingList(ROM rom)
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>Build map exit point list matching MapExitPointViewModel.</summary>
        static List<AddrResult> BuildMapExitPointList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_exit_point_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd;
            uint maxEntries = npcBlockAdd > 0 ? npcBlockAdd : 0x100;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxEntries; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint pointer = rom.u32(addr);
                if (!U.isPointerOrNULL(pointer)) break;

                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : "NULL";
                string name = U.ToHexString(i) + " ExitPoint " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build terrain name list matching TerrainNameEditorViewModel
        /// (#5 of #943). DUAL-MODE, mirroring WinForms MapTerrainNameForm:
        ///   * multibyte (JP) — 4-byte entries, each a pointer to a raw string;
        ///     stop on !U.isPointerOrNULL(u32(addr)).
        ///   * non-multibyte (US/EU) — 2-byte Huffman text IDs; stop on textId==0.
        /// </summary>
        static List<AddrResult> BuildTerrainNameList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_terrain_name_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();

            if (rom.RomInfo.is_multibyte)
            {
                const uint blockSize = 4;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint addr = baseAddr + i * blockSize;
                    if (addr + blockSize > (uint)rom.Data.Length) break;

                    uint rawPtr = rom.u32(addr);
                    if (!U.isPointerOrNULL(rawPtr)) break;

                    string decoded = "";
                    if (U.isPointer(rawPtr))
                    {
                        uint strOff = U.toOffset(rawPtr);
                        if (U.isSafetyOffset(strOff))
                        {
                            try { decoded = rom.getString(strOff); }
                            catch { decoded = ""; }
                        }
                    }

                    string name = U.ToHexString(i) + " " + decoded;
                    result.Add(new AddrResult(addr, name, i));
                }
            }
            else
            {
                const uint blockSize = 2;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint addr = baseAddr + i * blockSize;
                    if (addr + blockSize > (uint)rom.Data.Length) break;

                    uint textId = rom.u16(addr);
                    if (textId == 0x0000) break;

                    string decoded;
                    try { decoded = GetTextById(textId); }
                    catch { decoded = "???"; }

                    string name = U.ToHexString(i) + " " + decoded;
                    result.Add(new AddrResult(addr, name, i));
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Song/Sound editors
        // ------------------------------------------------------------------

        /// <summary>Build song table list matching SongTableViewModel.</summary>
        static List<AddrResult> BuildSongTableList(ROM rom)
        {
            uint ptr = rom.RomInfo.sound_table_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                string songName = NameResolver.GetSongName(i);
                string name = $"{U.ToHexString(i)} {songName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build sound boss BGM list matching SoundBossBGMViewerViewModel.</summary>
        static List<AddrResult> BuildSoundBossBGMList(ROM rom)
        {
            uint ptr = rom.RomInfo.sound_boss_bgm_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, blockSize * 10)) break;

                uint unitId = rom.u8(addr);
                uint songId = rom.u32(addr + 4);
                // 1-based ROM-stored unit ID (matches SoundBossBGMViewerViewModel).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                // Lockstep with SoundBossBGMViewerViewModel — WinForms format:
                //   "{unitHex} {unitName} : {songHex}{songName}" (#961 W2a).
                // Use the Core resolver DIRECTLY so an unresolved id yields "" (no
                // "Song 0x.." placeholder duplication) — same as the VM (#962 review).
                string songName = SongNameResolverCore.GetSongName(rom, songId);
                string name = $"{U.ToHexString(unitId)} {unitName} : {U.ToHexString(songId)}{songName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // World map editors
        // ------------------------------------------------------------------

        /// <summary>Build world map point list matching WorldMapPointViewModel.</summary>
        static List<AddrResult> BuildWorldMapPointList(ROM rom)
        {
            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 32;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // WinForms validation: offsets 12, 16, 20 must be pointer or null
                if (!U.isPointerOrNULL(rom.u32(addr + 12))
                    || !U.isPointerOrNULL(rom.u32(addr + 16))
                    || !U.isPointerOrNULL(rom.u32(addr + 20)))
                    break;

                uint nameTextId = rom.u16(addr + 28);
                string pointName = nameTextId != 0 ? GetTextById(nameTextId) : "???";
                string name = $"{U.ToHexString(i)} {pointName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build world map path list matching WorldMapPathViewModel.</summary>
        static List<AddrResult> BuildWorldMapPathList(ROM rom)
        {
            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                uint startId = rom.u8(addr + 4);
                uint endId = rom.u8(addr + 5);
                string name = $"{U.ToHexString(i)} Point {startId:X02} -> Point {endId:X02}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build world map BGM list matching WorldMapBGMViewModel.</summary>
        static List<AddrResult> BuildWorldMapBGMList(ROM rom)
        {
            uint ptr = rom.RomInfo.worldmap_bgm_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint songId1 = rom.u16(addr + 0);
                uint songId2 = rom.u16(addr + 2);
                // WinForms: stop when songId1 == 1 && songId2 == 0
                if (songId1 == 1 && songId2 == 0) break;

                string songName1 = NameResolver.GetSongName(songId1);
                string name = $"{U.ToHexString(i)} {songName1}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Item sub-editors
        // ------------------------------------------------------------------

        /// <summary>Build item weapon effect list matching ItemWeaponEffectViewerViewModel.</summary>
        static List<AddrResult> BuildItemWeaponEffectList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_effect_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, blockSize * 10)) break;

                uint itemId = rom.u8(addr);
                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item weapon triangle list matching ItemWeaponTriangleViewerViewModel.</summary>
        static List<AddrResult> BuildItemWeaponTriangleList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_cornered_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 255) break;

                uint w1 = rom.u8(addr);
                uint w2 = rom.u8(addr + 1);
                // Issue #370: label format must match WinForms
                // `DrawWeaponTypeIcon2AndText`:
                //   "{weapon1Hex} {weapon1Name} -> {weapon2Hex} {weapon2Name}"
                // The prefix MUST start with the weapon-type ID (not the row
                // index) so any DrawWeaponTypeIcon-style parser that uses
                // U.atoh(text) gets the correct icon. Mirrors the format
                // produced by ItemWeaponTriangleViewerViewModel.
                string n1 = ItemWeaponTriangleViewerViewModel.WeaponTypeNames.Get(w1);
                string n2 = ItemWeaponTriangleViewerViewModel.WeaponTypeNames.Get(w2);
                string name = $"{U.ToHexString(w1)} {n1} -> {U.ToHexString(w2)} {n2}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Event editors
        // ------------------------------------------------------------------

        /// <summary>Build event battle talk list matching EventBattleTalkViewModel.</summary>
        static List<AddrResult> BuildEventBattleTalkList(ROM rom)
        {
            uint ptr = rom.RomInfo.event_ballte_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;

                uint attacker = rom.u16(addr);
                uint defender = rom.u16(addr + 2);
                // 1-based ROM-stored unit IDs — battle-talk uses
                // GetUnitNameAndANY semantics (0 => "ANY"), matching
                // WinForms EventBattleTalkForm and EventBattleTalkViewModel.
                string atkName = NameResolver.GetUnitNameAndANYByOneBasedId(attacker);
                string defName = NameResolver.GetUnitNameAndANYByOneBasedId(defender);
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build event haiku (death quote) list matching EventHaikuViewModel.</summary>
        static List<AddrResult> BuildEventHaikuList(ROM rom)
        {
            uint ptr = rom.RomInfo.event_haiku_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID — haiku uses GetUnitNameAndANY
                // semantics (0 => "ANY"), matching WinForms EventHaikuForm
                // and EventHaikuViewModel.
                string unitName = NameResolver.GetUnitNameAndANYByOneBasedId(unitId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build event force sortie list matching EventForceSortieViewModel.</summary>
        static List<AddrResult> BuildEventForceSortieList(ROM rom)
        {
            uint ptr = rom.RomInfo.event_force_sortie_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;

                uint unitId = rom.u16(addr);
                // 1-based ROM-stored unit ID — force-sortie uses
                // GetUnitNameAndANY semantics (0 => "ANY"), matching
                // WinForms EventForceSortieForm and EventForceSortieViewModel.
                string unitName = NameResolver.GetUnitNameAndANYByOneBasedId(unitId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // ED (ending) editor
        // ------------------------------------------------------------------

        /// <summary>Build ED retreat list matching EDViewModel.</summary>
        static List<AddrResult> BuildEDList(ROM rom)
        {
            uint ptr = rom.RomInfo.ed_1_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0x00) break;

                uint uid = rom.u8(addr);
                // 1-based ROM-stored unit ID.
                string unitName = NameResolver.GetUnitNameByOneBasedId(uid);
                string name = $"{U.ToHexString(i)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Class sub-editors
        // ------------------------------------------------------------------

        /// <summary>Build CC branch list matching CCBranchEditorViewModel.</summary>
        static List<AddrResult> BuildCCBranchList(ROM rom)
        {
            uint ptr = rom.RomInfo.ccbranch_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Determine class count from class table
            uint classPtr = rom.RomInfo.class_pointer;
            uint classDataSize = rom.RomInfo.class_datasize;
            uint classBase = 0;
            int classCount = 0;
            if (classPtr != 0 && classDataSize > 0)
            {
                classBase = rom.p32(classPtr);
                if (U.isSafetyOffset(classBase))
                {
                    for (int c = 0; c <= 0xFF; c++)
                    {
                        uint cAddr = (uint)(classBase + c * classDataSize);
                        if (cAddr + classDataSize > (uint)rom.Data.Length) break;
                        if (c > 0 && rom.u8(cAddr + 4) == 0) break;
                        classCount++;
                    }
                }
            }
            if (classCount == 0) classCount = 0x80;

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (uint i = 0; i < (uint)classCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                // Match CCBranchEditorViewModel: resolve textId directly, return empty for 0
                string className = "???";
                try
                {
                    if (classBase != 0 && classDataSize > 0)
                    {
                        uint classTextId = rom.u16((uint)(classBase + i * classDataSize));
                        if (classTextId != 0)
                            className = GetTextById(classTextId);
                        else
                            className = "";
                    }
                }
                catch { className = "???"; }

                string name = U.ToHexString(i) + " " + className;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build class OP demo list matching ClassOPDemoViewModel.</summary>
        static List<AddrResult> BuildClassOPDemoList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            // Match VM: check isSafetyOffset on ptrAddr before dereferencing
            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 28;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Match VM: check if u32(addr) is pointer
                if (!U.isPointer(rom.u32(addr))) break;

                uint cid = rom.u8(addr + 14);
                string name = U.ToHexString(i) + " " + U.ToHexString(cid);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build class OP font list matching ClassOPFontViewModel.</summary>
        static List<AddrResult> BuildClassOPFontList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_font_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " OP Class Font";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Menu editor
        // ------------------------------------------------------------------

        /// <summary>Build menu definition list matching MenuDefinitionViewModel.</summary>
        static List<AddrResult> BuildMenuDefinitionList(ROM rom)
        {
            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 36;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr + 8))) break;

                string name = U.ToHexString(i) + " Menu Definition";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Status option editor
        // ------------------------------------------------------------------

        /// <summary>Build status option list matching StatusOptionViewModel.</summary>
        static List<AddrResult> BuildStatusOptionList(ROM rom)
        {
            uint ptr = rom.RomInfo.status_game_option_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 44;
            var result = new List<AddrResult>();
            for (int i = 0; i < 64; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // WinForms validation: u32(addr+40) must be pointer
                if (!U.isPointer(rom.u32(addr + 40))) break;

                uint nameTextId = rom.u16(addr + 4);
                string optName = nameTextId > 0 ? GetTextById(nameTextId) : $"Option {i}";
                result.Add(new AddrResult(addr, $"0x{i:X2} {optName}", (uint)i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Arena editors
        // ------------------------------------------------------------------

        /// <summary>Build arena class list matching ArenaClassViewerViewModel (near weapon type).</summary>
        static List<AddrResult> BuildArenaClassList(ROM rom)
        {
            uint ptr = rom.RomInfo.arena_class_near_weapon_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 1;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint classId = rom.u8(addr);
                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(i)} {className} (0x{classId:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build arena enemy weapon list matching ArenaEnemyWeaponViewerViewModel.</summary>
        static List<AddrResult> BuildArenaEnemyWeaponList(ROM rom)
        {
            // Single source of truth with the Avalonia view + WinForms form (#1465):
            // basic list = arena_enemy_weapon_basic_pointer, stride 1, 8 entries,
            // display string includes the per-slot type label (GetBasicTypeName).
            // The rank-up list (arena_enemy_weapon_rankup_pointer, 26 entries) is the
            // editor's second AddressListControl; its parity is pinned by the
            // dedicated ArenaEnemyWeaponRankupParityTests, since the registry is
            // one-builder-per-view.
            return ArenaEnemyWeaponCore.BuildBasicList(rom);
        }

        // ------------------------------------------------------------------
        // Link arena deny
        // ------------------------------------------------------------------

        /// <summary>Build link arena deny unit list matching LinkArenaDenyUnitViewerViewModel.</summary>
        static List<AddrResult> BuildLinkArenaDenyUnitList(ROM rom)
        {
            uint ptr = rom.RomInfo.link_arena_deny_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID.
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                string name = $"{U.ToHexString(unitId)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Monster probability
        // ------------------------------------------------------------------

        /// <summary>Build monster probability list matching MonsterProbabilityViewerViewModel.</summary>
        static List<AddrResult> BuildMonsterProbabilityList(ROM rom)
        {
            uint ptr = rom.RomInfo.monster_probability_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                string name = U.ToHexString(i) + " Monster Prob";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Summon editors
        // ------------------------------------------------------------------

        /// <summary>Build summon unit list matching SummonUnitViewerViewModel.</summary>
        static List<AddrResult> BuildSummonUnitList(ROM rom)
        {
            uint ptr = rom.RomInfo.summon_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID.
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                string name = $"{U.ToHexString(unitId)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build summons demon king list matching SummonsDemonKingViewerViewModel.</summary>
        static List<AddrResult> BuildSummonsDemonKingList(ROM rom)
        {
            uint ptr = rom.RomInfo.summons_demon_king_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            // Bounds-check against THIS rom (the builder's parameter), not ambient
            // CoreState.ROM, so the guard is consistent if called with a non-ambient ROM.
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // Match the WinForms / Core canon (SummonsDemonKingForm.cs:36-42,
            // StructExportCore.cs:975-978) and SummonsDemonKingViewerViewModel:
            // a missing count source or a corrupt count byte (>=100) means an
            // EMPTY list; count==0 yields exactly 1 row via the i<=maxCount loop
            // (NOT 21 fabricated rows from the old `maxCount = 20` — issue #1424).
            uint countAddr = rom.RomInfo.summons_demon_king_count_address;
            if (countAddr == 0 || !U.isSafetyOffset(countAddr, rom)) return new List<AddrResult>();
            uint maxCount = rom.u8(countAddr);
            if (maxCount >= 100) return new List<AddrResult>();

            const uint blockSize = 20;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= maxCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                // Match SummonsDemonKingViewerViewModel layout: unitId at offset 0, classId at offset 1
                uint unitId = rom.u8(addr);
                string name;
                if (unitId == 0)
                {
                    name = U.ToHexString(i) + " -EMPTY-";
                }
                else
                {
                    try
                    {
                        uint classId = rom.u8(addr + 1);
                        // 1-based ROM-stored unit ID; classId is 0-based.
                        string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                        string className = NameResolver.GetClassName(classId);
                        name = $"{U.ToHexString(i)} {unitName} ({className})";
                    }
                    catch
                    {
                        name = U.ToHexString(i);
                    }
                }
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // AI editors
        // ------------------------------------------------------------------

        /// <summary>Build AI map setting list matching AIMapSettingViewModel.</summary>
        static List<AddrResult> BuildAIMapSettingList(ROM rom)
        {
            uint ptr = rom.RomInfo.ai_map_setting_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                result.Add(new AddrResult(addr, $"0x{i:X2} Map {i}", (uint)i));
            }
            return result;
        }

        /// <summary>Build AI perform item list matching AIPerformItemViewModel.</summary>
        static List<AddrResult> BuildAIPerformItemList(ROM rom)
        {
            uint ptr = rom.RomInfo.ai_preform_item_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0x0) break;

                uint itemId = rom.u16(addr);
                string itemName = NameResolver.GetItemName(itemId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {itemName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build AI perform staff list matching AIPerformStaffViewModel.</summary>
        static List<AddrResult> BuildAIPerformStaffList(ROM rom)
        {
            uint ptr = rom.RomInfo.ai_preform_staff_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0x0) break;

                uint itemId = rom.u16(addr);
                string itemName = NameResolver.GetItemName(itemId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {itemName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build AI steal item list matching AIStealItemViewModel.</summary>
        static List<AddrResult> BuildAIStealItemList(ROM rom)
        {
            uint ptr = rom.RomInfo.ai_steal_item_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                uint itemId = rom.u8(addr);
                string itemName = NameResolver.GetItemName(itemId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {itemName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build AI target list matching AITargetViewModel.</summary>
        static List<AddrResult> BuildAITargetList(ROM rom)
        {
            uint ptr = rom.RomInfo.ai3_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 20;
            var result = new List<AddrResult>();
            // Fixed count of 8 profiles, matching AITargetViewModel.ProfileCount, WinForms
            // AITargetForm (i < 8), and StructExportCore (ai_targets hardcodes 8). No all-zero
            // early-stop and no >8 cap (issue #1419).
            for (uint i = 0; i < AITargetViewModel.ProfileCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                result.Add(new AddrResult(addr, $"0x{i:X02} AI Target Profile {i}", i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Item sub-editors
        // ==================================================================

        /// <summary>Build item stat bonuses list matching ItemStatBonusesViewerViewModel.
        /// Each item that has a non-null pointer at item_base + i*dataSize + 12 gets listed.</summary>
        static List<AddrResult> BuildItemStatBonusesList(ROM rom)
        {
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return new List<AddrResult>();
            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = itemBase + i * dataSize;
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;

                // Validation: offsets 12 and 16 must be pointer or null (same as WinForms Init)
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))
                    || !U.isPointerOrNULL(rom.u32(itemAddr + 16)))
                    break;

                uint statPtr = rom.u32(itemAddr + 12);
                if (!U.isPointer(statPtr)) continue;

                uint statAddr = U.toOffset(statPtr);
                uint nameId = rom.u16(itemAddr);
                string name = U.ToHexString(i) + " " + GetTextById(nameId);
                result.Add(new AddrResult(statAddr, name, i));
            }
            return result;
        }

        /// <summary>Build item effectiveness OUTER (item-driven) list matching the
        /// new ItemEffectivenessViewerViewModel layout (issue #368). Walks the
        /// item table by +16 effectiveness pointer; mirrors the WinForms
        /// ItemEffectivenessForm.Init iteration semantics. Replaces the old
        /// flat weapon_effectiveness_2x3x_address-only loader which only worked
        /// on FE8 and did not match WinForms behaviour.</summary>
        static List<AddrResult> BuildItemEffectivenessList(ROM rom)
        {
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return new List<AddrResult>();
            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return new List<AddrResult>();
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = itemBase + i * dataSize;
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 16))) break;
                uint critPtr = rom.u32(itemAddr + 16);
                if (!U.isPointer(critPtr)) continue;
                uint critOff = U.toOffset(critPtr);
                if (!U.isSafetyOffset(critOff)) continue;

                string itemName = NameResolver.GetItemName(i);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(itemAddr, name, i));
            }
            return result;
        }

        /// <summary>Build CC-item-driven OUTER list matching the new
        /// ItemPromotionViewerViewModel layout (issue #368). Returns the fixed
        /// set of CC items (Hero Crest, Knight Crest, ...). Replaces the old
        /// flat item_promotion1_array_pointer loader.</summary>
        static List<AddrResult> BuildItemPromotionList(ROM rom)
        {
            var result = new List<AddrResult>();

            void Add(uint itemId, uint pointer)
            {
                if (pointer == 0) return;
                string name = $"{U.ToHexString(itemId)} {NameResolver.GetItemName(itemId)}";
                result.Add(new AddrResult(pointer, name, itemId));
            }

            Add(rom.RomInfo.cc_item_hero_crest_itemid, rom.RomInfo.cc_item_hero_crest_pointer);
            Add(rom.RomInfo.cc_item_knight_crest_itemid, rom.RomInfo.cc_item_knight_crest_pointer);
            Add(rom.RomInfo.cc_item_orion_bolt_itemid, rom.RomInfo.cc_item_orion_bolt_pointer);
            Add(rom.RomInfo.cc_elysian_whip_itemid, rom.RomInfo.cc_elysian_whip_pointer);
            Add(rom.RomInfo.cc_guiding_ring_itemid, rom.RomInfo.cc_guiding_ring_pointer);
            if (rom.RomInfo.version >= 7)
            {
                Add(rom.RomInfo.cc_fallen_contract_itemid, rom.RomInfo.cc_fallen_contract_pointer);
                Add(rom.RomInfo.cc_master_seal_itemid, rom.RomInfo.cc_master_seal_pointer);
                Add(rom.RomInfo.cc_ocean_seal_itemid, rom.RomInfo.cc_ocean_seal_pointer);
                Add(rom.RomInfo.cc_moon_bracelet_itemid, rom.RomInfo.cc_moon_bracelet_pointer);
                Add(rom.RomInfo.cc_sun_bracelet_itemid, rom.RomInfo.cc_sun_bracelet_pointer);
            }
            return result;
        }

        /// <summary>Build item shop (hensei) list matching ItemShopViewerViewModel.
        /// Uses item_shop_hensei_pointer — 2-byte entries (item ID + quantity).</summary>
        static List<AddrResult> BuildItemShopList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_shop_hensei_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 2;
                if (addr + 1 >= (uint)rom.Data.Length) break;

                uint itemId = rom.u8(addr);
                if (itemId == 0x00) break;

                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item usage pointer list matching ItemUsagePointerViewerViewModel.
        /// Uses item_usability_array_pointer — 4-byte function pointer entries.</summary>
        static List<AddrResult> BuildItemUsagePointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_usability_array_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint startItemId = 0;
            uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
            if (switchAddr != 0 && U.isSafetyOffset(switchAddr + 2))
                startItemId = rom.u8(switchAddr);

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint funcPtr = rom.u32(addr);
                if (!U.isPointerOrNULL(funcPtr)) break;

                uint itemId = startItemId + i;
                string name = U.ToHexString(itemId) + " Func=0x" + funcPtr.ToString("X08");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item effect pointer list matching ItemEffectPointerViewerViewModel.
        /// Uses item_effect_pointer_table_pointer — 4-byte pointer entries.</summary>
        static List<AddrResult> BuildItemEffectPointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_effect_pointer_table_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint funcPtr = rom.u32(addr);
                if (!U.isPointerOrNULL(funcPtr)) break;
                if (funcPtr != 0 && funcPtr <= 0x08000100) break;
                if (i > 0xFD) break;

                // Match Avalonia VM format: "0x{i:X2} C{i:X02}"
                string name = U.ToHexString(i) + " C" + i.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item icon list matching ItemIconViewerViewModel.
        /// Uses icon_pointer — 128 bytes per icon (raw uncompressed icon data).</summary>
        static List<AddrResult> BuildItemIconList(ROM rom)
        {
            uint ptr = rom.RomInfo.icon_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 128;
                if (addr + 128 > (uint)rom.Data.Length) break;

                string name = U.ToHexString(i) + " Item Icon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Map/Terrain editors
        // ==================================================================

        /// <summary>Build move cost list matching MoveCostEditorViewModel.LoadClassList().
        /// Uses class_pointer — lists classes similar to the class editor.</summary>
        static List<AddrResult> BuildMoveCostList(ROM rom)
        {
            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build map tile animation list matching MapTileAnimationViewModel.
        /// Uses map_tileanime1_pointer — a PLIST pointer table whose 4-byte slots
        /// each hold a u32 POINTER to the real 8-byte animation struct. Lockstep
        /// with MapTileAnimationViewModel.LoadMapTileAnimationList (#952, #11,
        /// #1403): each slot index IS the ANIMATION PLIST id; the slot is
        /// DEREFERENCED via MapChangeCore.PlistToOffsetAddr so the row address is
        /// the struct address (not the raw slot — writing at the raw slot would
        /// corrupt the pointer table). Resolved to an "ANIME1/ANIME2 MapName"
        /// label via the shared resolver instead of a raw 0x… pointer.</summary>
        static List<AddrResult> BuildMapTileAnimationList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_tileanime1_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var cache = MapPListResolverCore.BuildCache(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                // Dereference the PLIST slot to the actual struct address.
                // PlistToOffsetAddr bounds by the version PLIST limit and does the
                // ROM-aware null/safety check, returning U.NOT_FOUND for a
                // broken/empty/out-of-range slot — skip those rows.
                uint dataAddr = MapChangeCore.PlistToOffsetAddr(
                    rom, MapChangeCore.PlistType.ANIMATION, i, out uint _);
                if (dataAddr == U.NOT_FOUND) continue;

                // Lockstep with the VM: require the full 8-byte struct in-bounds
                // (PlistToOffsetAddr only safety-checks the struct START), so the
                // two paths cannot silently diverge on a malformed/synthetic
                // pointer table (#1403 review).
                if (dataAddr + 8u > (uint)rom.Data.Length) continue;

                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.ANIMATION, i, cache);
                string name = U.ToHexString(i) + " " + label;
                result.Add(new AddrResult(dataAddr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Units/Classes
        // ==================================================================

        /// <summary>Build FE6 class list matching ClassFE6ViewModel.
        /// Uses class_pointer — same as ClassEditorView but for FE6 ROM version.</summary>
        static List<AddrResult> BuildClassFE6List(ROM rom)
        {
            // Identical to BuildClassList since the VM logic is the same
            return BuildClassList(rom);
        }

        /// <summary>Build FE6 unit list matching UnitFE6ViewModel.
        /// Uses unit_pointer — always skips first entry (unlike UnitEditorView which only skips for FE6).</summary>
        static List<AddrResult> BuildUnitFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            // UnitFE6ViewModel always skips the first entry
            baseAddr += dataSize;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i + 1) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build unit palette list matching UnitPaletteViewModel.
        /// Uses unit_palette_color_pointer — 7-byte entries, fixed count.</summary>
        static List<AddrResult> BuildUnitPaletteList(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_palette_color_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;
            const uint blockSize = 7;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                // Mirror UnitPaletteViewModel: 1-based unit ID with the WinForms convention.
                string unitName = NameResolver.GetUnitNameByOneBasedId(i + 1);
                // Match Avalonia VM format: "0x{id:X2} {name}"
                result.Add(new AddrResult(addr, $"0x{(i + 1):X2} {unitName}", i + 1));
            }
            return result;
        }

        /// <summary>Build extra unit FE8U list matching ExtraUnitFE8UViewModel.
        /// The VM dereferences p32(0x37D88) to get the actual table base, then walks 8-byte entries.</summary>
        static List<AddrResult> BuildExtraUnitFE8UList(ROM rom)
        {
            const uint pointerAddr = 0x37D88;
            const uint blockSize = 8;

            uint baseAddr = rom.p32(pointerAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                // Validation: u32(addr+4) must be a safe pointer
                if (!U.isSafetyPointer(rom.u32(addr + 4))) break;

                uint flagId = rom.u32(addr + 0);
                uint unitsAddr = rom.p32(addr + 4);
                uint unitId = 0;
                if (U.isSafetyOffset(unitsAddr))
                    unitId = rom.u8(unitsAddr);

                // 1-based ROM-stored unit ID (matches ExtraUnitFE8UViewModel).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                // Match Avalonia VM format: "{hexUnitId} {unitName} (Flag:0x{flagId:X})"
                string name = $"{U.ToHexString(unitId)} {unitName} (Flag:0x{flagId:X})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Event editors
        // ==================================================================

        /// <summary>Build event function pointer list matching EventFunctionPointerViewModel.
        /// Uses event_function_pointer_table_pointer — 4-byte pointer entries.</summary>
        static List<AddrResult> BuildEventFunctionPointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.event_function_pointer_table_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint a = rom.u32(addr);
                if (!U.isPointer(a)) break;
                // WinForms also checks IsValueOdd and addr > 0x08000100
                if (!U.IsValueOdd(a)) break;
                if (a <= 0x08000100) break;

                // Match Avalonia VM format: "0x{i:X2} 0x{ptr:X08}"
                string ptrStr = $"0x{a:X08}";
                result.Add(new AddrResult(addr, $"0x{i:X2} {ptrStr}", i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Sound editors
        // ==================================================================

        /// <summary>Build sound foot steps list matching SoundFootStepsViewerViewModel.
        /// Uses sound_foot_steps_pointer with switch2 offset for class ID base.</summary>
        static List<AddrResult> BuildSoundFootStepsList(ROM rom)
        {
            uint ptr = rom.RomInfo.sound_foot_steps_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint classIdBase = 0;
            uint switch2Addr = rom.RomInfo.sound_foot_steps_switch2_address;
            if (switch2Addr > 0 && switch2Addr < (uint)rom.Data.Length)
                classIdBase = rom.u8(switch2Addr);

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                uint classId = classIdBase + i;
                string className = NameResolver.GetClassName(classId);
                // Match Avalonia VM format: "{hexId} {className}"
                string name = $"{U.ToHexString(classId)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Image/Graphics editors
        // ==================================================================

        /// <summary>Build ED staff roll list matching EDStaffRollViewModel.
        /// Uses ed_staffroll_image_pointer — 8-byte entries, max 12.</summary>
        static List<AddrResult> BuildEDStaffRollList(ROM rom)
        {
            uint ptr = rom.RomInfo.ed_staffroll_image_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 12; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                // Match Avalonia VM: "0x{i:X2} Staff Roll"
                string name = U.ToHexString(i) + " Staff Roll";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OP prologue list matching OPPrologueViewerViewModel.
        /// Uses op_prologue_image_pointer — 12-byte entries with pointer validation.</summary>
        static List<AddrResult> BuildOPPrologueList(ROM rom)
        {
            uint ptr = rom.RomInfo.op_prologue_image_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                // Match Avalonia VM: "0x{i:X2} Prologue"
                string name = U.ToHexString(i) + " Prologue";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build big CG list matching BigCGViewerViewModel.
        /// Uses bigcg_pointer (indirect) — 12-byte entries with pointer validation.</summary>
        static List<AddrResult> BuildBigCGList(ROM rom)
        {
            uint ptr = rom.RomInfo.bigcg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                // Match Avalonia VM: "0x{i:X2} CG"
                string name = U.ToHexString(i) + " CG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build chapter title list matching ChapterTitleViewerViewModel.
        /// Uses image_chapter_title_pointer — 12-byte entries (FE8) or 4-byte (FE7).</summary>
        static List<AddrResult> BuildChapterTitleList(ROM rom)
        {
            uint ptr = rom.RomInfo.image_chapter_title_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Avalonia VM always uses 12-byte entries
            const uint blockSize = 12;

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                string name = U.ToHexString(i) + " Chapter Title";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 3 builders — Battle animation editors
        // ==================================================================

        /// <summary>Build command 85 pointer list matching Command85PointerViewModel.
        /// Uses command_85_pointer_table_pointer — 4-byte pointer entries starting at ID 0x19.</summary>
        static List<AddrResult> BuildCommand85PointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.command_85_pointer_table_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint a = rom.u32(addr);
                if (!U.isPointerOrNULL(a)) break;
                // null entries are allowed, but non-pointer non-null stops the list
                if (a != 0 && a <= 0x08000100) break;

                // Match Avalonia VM format: "0x{i:X2} {ptrStr}" where ptrStr is "0x????????" or "NULL"
                string ptrStr = a == 0 ? "NULL" : $"0x{a:X08}";
                result.Add(new AddrResult(addr, $"0x{i:X2} {ptrStr}", i));
            }
            return result;
        }
        // ==================================================================
        // Batch 4 builders — Event editors (FE6/FE7 variants)
        // ==================================================================

        /// <summary>Build FE6 event battle talk list — 12-byte entries, u8(addr) attacker, u8(addr+1) defender.</summary>
        static List<AddrResult> BuildEventBattleTalkFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.event_ballte_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u16(addr);
                if (unit == 0 || unit == 0xFFFF) break;

                // 1-based ROM-stored unit IDs (matches EventBattleTalkFE6ViewModel).
                string atkName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr));
                string defName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr + 1));
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build FE7 event battle talk list — 16-byte entries, u8(addr) attacker, u8(addr+1) defender.</summary>
        static List<AddrResult> BuildEventBattleTalkFE7List(ROM rom)
        {
            uint ptr = rom.RomInfo.event_ballte_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u16(addr);
                if (unit == 0 || unit == 0xFFFF) break;

                // 1-based ROM-stored unit IDs (matches EventBattleTalkFE7ViewModel).
                string atkName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr));
                string defName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr + 1));
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build FE6 event haiku (death quote) list — 16-byte entries, stop when u8(addr)==0.</summary>
        static List<AddrResult> BuildEventHaikuFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.event_haiku_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID (matches EventHaikuFE6ViewModel).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build FE7 event haiku (death quote) list — 16-byte entries, stop when u8(addr)==0.</summary>
        static List<AddrResult> BuildEventHaikuFE7List(ROM rom)
        {
            // Same structure as FE6 haiku
            return BuildEventHaikuFE6List(rom);
        }

        // ------------------------------------------------------------------
        // FE7 12-byte secondary tables (#957 W1b) — these tables live behind a
        // Table filter combo (NOT the default render), so they are NOT in the
        // EditorMap registration; tests call these golden builders directly to
        // lockstep the secondary EventHaikuFE7ViewModel / EventBattleTalkFE7ViewModel
        // lists against an independent ROM walk.
        // ------------------------------------------------------------------

        /// <summary>
        /// Build the FE7 N1 tutorial death-quote list (Lyn = tutorial 1,
        /// Eliwood = tutorial 2) — 12-byte entries, stop when u8(addr)==0.
        /// Mirrors WinForms EventHaikuFE7Form.N1_Init + the N1 list draw
        /// (single unit name) and EventHaikuFE7ViewModel.LoadList(Tutorial*).
        /// </summary>
        static List<AddrResult> BuildEventHaikuFE7TutorialList(ROM rom, uint pointerLocation)
        {
            if (pointerLocation == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointerLocation);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID (matches EventHaikuFE7ViewModel).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        /// <summary>Golden list for the FE7 Lyn-chapter tutorial death quotes (event_haiku_tutorial_1_pointer).</summary>
        public static List<AddrResult> BuildEventHaikuFE7Tutorial1List(ROM rom)
            => BuildEventHaikuFE7TutorialList(rom, rom.RomInfo.event_haiku_tutorial_1_pointer);

        /// <summary>Golden list for the FE7 Eliwood-chapter tutorial death quotes (event_haiku_tutorial_2_pointer).</summary>
        public static List<AddrResult> BuildEventHaikuFE7Tutorial2List(ROM rom)
            => BuildEventHaikuFE7TutorialList(rom, rom.RomInfo.event_haiku_tutorial_2_pointer);

        /// <summary>
        /// Build the FE7 secondary battle-talk list (event_ballte_talk2_pointer)
        /// — 12-byte entries, stop when u8(addr)==0 || u8(addr)==0xFF; the
        /// secondary list draws only the single unit name. Mirrors WinForms
        /// EventBattleTalkFE7Form.N1_Init + EventBattleTalkFE7ViewModel
        /// .LoadList(Secondary).
        /// </summary>
        public static List<AddrResult> BuildEventBattleTalkFE7SecondaryList(ROM rom)
        {
            uint ptr = rom.RomInfo.event_ballte_talk2_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u8(addr);
                if (unit == 0 || unit == 0xFF) break;

                // 1-based ROM-stored unit ID (matches EventBattleTalkFE7ViewModel).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unit);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        /// <summary>Build FE7 event force sortie list — fixed 23 entries starting at map 0x17.</summary>
        static List<AddrResult> BuildEventForceSortieFE7List(ROM rom)
        {
            uint ptr = rom.RomInfo.event_force_sortie_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 23; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint mapId = (uint)i + 0x17;
                result.Add(new AddrResult(addr, $"0x{i:X2} Map 0x{mapId:X2}", (uint)i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Map editors
        // ==================================================================

        /// <summary>Build map change list — 4-byte pointer entries, stop at 0xFFFFFFFF.</summary>
        static List<AddrResult> BuildMapChangeList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_mapchange_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Lockstep with MapChangeViewModel.LoadMapChangeList (#952):
            // resolve each CHANGE-type PLIST row to a "MAPCHANGE MapName"
            // label via the shared resolver instead of a raw 0x… pointer.
            var cache = MapPListResolverCore.BuildCache(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                if (pointer == 0xFFFFFFFF) break;

                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.CHANGE, i, cache);
                string name = U.ToHexString(i) + " " + label;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build map pointer list — uses map_map_pointer_pointer (default PLIST type 0).</summary>
        static List<AddrResult> BuildMapPointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_map_pointer_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Canonical PLIST limit (256 on split layouts) so the golden list
            // does not truncate vs the VM — keeps VM↔golden in lockstep (#953).
            uint limit = MapChangeCore.GetPlistLimit(rom);
            if (limit == 0) limit = 256;

            // Lockstep with MapPointerViewModel.LoadMapPointerList default
            // typeIndex=0 (MAP) (#952): resolve each row to a "MAP MapName"
            // label via the shared resolver instead of a raw 0x… pointer.
            var cache = MapPListResolverCore.BuildCache(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < limit; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 3 >= (uint)rom.Data.Length) break;

                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.MAP, i, cache);
                string name = $"{U.ToHexString(i)} {label}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build map load function list — function pointers with count from switch1.</summary>
        static List<AddrResult> BuildMapLoadFunctionList(ROM rom)
        {
            uint pointer = rom.RomInfo.map_load_function_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint switchAddr = rom.RomInfo.map_load_function_switch1_address;
            if (switchAddr == 0) return new List<AddrResult>();
            if (switchAddr + 4 > (uint)rom.Data.Length) return new List<AddrResult>();

            uint count = rom.u8(switchAddr + 0);
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i <= count; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint funcPtr = rom.u32(addr);
                string ptrStr = U.isPointer(funcPtr) ? $"0x{funcPtr:X08}" : (funcPtr == 0 ? "NULL" : $"0x{funcPtr:X08}");
                string display = $"0x{i:X2} {ptrStr}";
                result.Add(new AddrResult(addr, display, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Image/Graphics editors
        // ==================================================================

        /// <summary>Build image portrait list matching ImagePortraitViewModel — with unit name resolution.</summary>
        static List<AddrResult> BuildImagePortraitList(ROM rom)
        {
            uint pointer = rom.RomInfo.portrait_pointer;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (pointer == 0 || dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0) { nullCount++; if (nullCount > 3) break; }
                else nullCount = 0;

                string name = $"0x{i:X2}";
                // #656 — resolve the portrait OWNER's name (matches ImagePortraitViewModel.LoadList
                // and WinForms ImagePortraitForm.GetPortraitNameFast). Previously this used
                // NameResolver.GetUnitName((uint)i) which mis-interpreted the portrait index
                // as a 0-based unit-table row.
                try
                {
                    string pname = NameResolver.GetPortraitName((uint)i);
                    if (!string.IsNullOrEmpty(pname)) name += $" {pname}";
                }
                catch { /* skip name resolution errors */ }
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            return result;
        }

        /// <summary>Build FE6 image portrait list — pointer validation with null-run threshold of 10.</summary>
        static List<AddrResult> BuildImagePortraitFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 16; // FE6 default

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0)
                {
                    uint u0 = rom.u32(addr + 0);
                    uint u4 = rom.u32(addr + 4);
                    uint u8 = rom.u32(addr + 8);
                    if (!U.isPointerOrNULL(u0) || !U.isPointerOrNULL(u4) || !U.isPointerOrNULL(u8))
                        break;
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {
                        nullCount++;
                        if (nullCount >= 10) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait FE6";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build image BG list — 12-byte entries, validate pointers at offsets 0 and 4.</summary>
        static List<AddrResult> BuildImageBGList(ROM rom)
        {
            uint ptr = rom.RomInfo.bg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint a0 = rom.u32(addr + 0);
                uint a1 = rom.u32(addr + 4);
                if (!U.isPointerOrNULL(a0) || !U.isPointerOrNULL(a1)) break;

                string name = U.ToHexString(i) + " Background";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Build the Battle Animation Editor's CLASS-centric left list (#1377):
        /// one row per class, each row's address being that class's battle-anime
        /// SETTING pointer (<c>p32(classAddr + 52)</c> FE7/8 / <c>+48</c> FE6) —
        /// the exact 4-byte SP-record offset the editor's <c>LoadEntry</c>
        /// dereferences. Mirrors WF <c>ImageBattleAnimeForm</c>, whose per-class
        /// SP-record view is driven by the CLASS listbox re-basing
        /// <c>InputFormRef.ReInit(GetBattleAnimeAddrWhereID(cid))</c> — NOT by the
        /// 32-byte <c>image_battle_animelist_pointer</c> ANIME table. Must stay in
        /// lockstep with <c>ImageBattleAnimeViewModel.LoadList</c>.
        /// </summary>
        static List<AddrResult> BuildImageBattleAnimeList(ROM rom)
        {
            var rows = FEBuilderGBA.Core.ClassFormCore.GetBattleAnimeSettingRows(rom);
            var result = new List<AddrResult>(rows.Count);
            foreach (var (classId, settingOffset) in rows)
            {
                string className;
                try { className = NameResolver.GetClassName((uint)classId); }
                catch { className = ""; }
                string label = string.IsNullOrEmpty(className)
                    ? $"0x{classId:X2}"
                    : $"0x{classId:X2} {className}";
                result.Add(new AddrResult(settingOffset, label, (uint)classId));
            }
            return result;
        }

        /// <summary>Build image battle BG list — 12-byte entries, validate img and tsa pointers.</summary>
        static List<AddrResult> BuildImageBattleBGList(ROM rom)
        {
            uint ptr = rom.RomInfo.battle_bg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint img = rom.u32(addr + 0);
                uint tsa = rom.u32(addr + 4);
                if (!U.isPointer(img) || !U.isPointer(tsa)) break;

                string name = U.ToHexString(i) + " Battle BG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build image CG list — 12-byte entries, validate 10-split pointer table.</summary>
        static List<AddrResult> BuildImageCGList(ROM rom)
        {
            uint ptr = rom.RomInfo.bigcg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint p = rom.u32(addr);
                if (!U.isPointer(p) || !U.isSafetyPointer(p)) break;
                // Verify 10-split: first pointer in table must also be a pointer
                uint p2 = rom.u32(U.toOffset(p));
                if (!U.isPointer(p2) || !U.isSafetyPointer(p2)) break;

                string name = U.ToHexString(i) + " CG Image";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build image unit palette list — 16-byte entries, validate pointer at offset 12.</summary>
        static List<AddrResult> BuildImageUnitPaletteList(ROM rom)
        {
            uint pointer = rom.RomInfo.image_unit_palette_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr + 12);
                if (!U.isPointer(p)) break;

                // Read identifier string from bytes 0-11
                string ident = "";
                for (int j = 0; j < 12; j++)
                {
                    byte b = rom.Data[addr + (uint)j];
                    if (b >= 0x20 && b < 0x7F) ident += (char)b;
                    else if (b == 0) break;
                }

                string name = $"0x{i:X2} {ident}";
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            // Match ImageUnitPaletteViewModel: appends a trailing "Unit Palette Editor" entry
            result.Add(new AddrResult(0, "Unit Palette Editor", 0));
            return result;
        }

        /// <summary>Build image unit wait icon list — 8-byte entries, validate pointer at offset 4.</summary>
        static List<AddrResult> BuildImageUnitWaitIconList(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 8;
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint imgPtr = rom.u32(addr + 4);
                if (!U.isPointer(imgPtr)) break;

                // #991: append the owning class name (lockstep with
                // ImageUnitWaitIconViewModel.LoadList — golden test gated).
                string className = FEBuilderGBA.Core.ClassFormCore.GetClassNameWhereWaitIconId(rom, i);
                string name = U.ToHexString(i) + U.SA(className) + " WaitIcon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build image unit move icon list — 8-byte entries, validate pointer at offset 0.</summary>
        static List<AddrResult> BuildImageUnitMoveIconList(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_move_icon_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 8;
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint imgPtr = rom.u32(addr + 0);
                if (!U.isPointer(imgPtr)) break;

                // #1177: append the owning class name (WF
                // GetClassNameWhereNo(i) = GetClassName(i+1)). Lockstep with
                // ImageUnitMoveIconViewModel.LoadList.
                string className = NameResolver.GetClassName(i + 1) ?? string.Empty;
                string name = U.ToHexString(i) + U.SA(className) + " MoveIcon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build image system area list — 2-byte color entries (30 per filter), uses move gradation palette.</summary>
        static List<AddrResult> BuildImageSystemAreaList(ROM rom)
        {
            uint pointer = rom.RomInfo.systemarea_move_gradation_palette_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            int colorCount = 30;
            var result = new List<AddrResult>();
            for (int i = 0; i < colorCount; i++)
            {
                uint addr = baseAddr + (uint)(i * 2);
                if (addr + 2 > (uint)rom.Data.Length) break;
                uint color = rom.u16(addr);
                int r = (int)(color & 0x1F) * 8;
                int g = (int)((color >> 5) & 0x1F) * 8;
                int b = (int)((color >> 10) & 0x1F) * 8;
                result.Add(new AddrResult(addr, $"0x{i:X2} #{r:X2}{g:X2}{b:X2}", color));
            }
            return result;
        }

        /// <summary>Build image TSA anime list — ALL FRAMECOUNT frames per category.</summary>
        static List<AddrResult> BuildImageTSAAnimeList(ROM rom)
        {
            // Enumerate ALL FRAMECOUNT frames per category via the shared Core helper,
            // identical to ImageTSAAnimeViewModel.LoadList — the two surfaces must not
            // drift back to the old frame-0-only / 20-cap enumeration (#1457).
            var tsaAnime = U.LoadTSVResource(U.ConfigDataFilename("tsaanime_"), false);
            if (tsaAnime == null || tsaAnime.Count == 0) return new List<AddrResult>();
            return ImageTSAAnimeFrameEnumCore.EnumerateFrames(rom, tsaAnime);
        }

        /// <summary>Build BattleBGViewer list — 12-byte entries, validate img+tsa pointers. Index starts at 1.</summary>
        static List<AddrResult> BuildBattleBGViewerList(ROM rom)
        {
            uint ptr = rom.RomInfo.battle_bg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 12;
                if (addr + 12 > (uint)rom.Data.Length) break;

                uint img = rom.u32(addr + 0);
                uint tsa = rom.u32(addr + 4);
                if (!U.isPointer(img) || !U.isPointer(tsa)) break;

                string name = U.ToHexString(i + 1) + " Battle BG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build battle terrain list — 24-byte entries, validate pointer at offset 12.</summary>
        static List<AddrResult> BuildBattleTerrainList(ROM rom)
        {
            uint ptr = rom.RomInfo.battle_terrain_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 24;
                if (addr + 24 > (uint)rom.Data.Length) break;

                uint ptr12 = rom.u32(addr + 12);
                if (!U.isPointer(ptr12)) break;

                // Read terrain name from ASCII at offset 0
                string tname = "";
                try
                {
                    for (int c = 0; c < 11; c++)
                    {
                        uint b = rom.u8(addr + (uint)c);
                        if (b == 0) break;
                        tname += (char)b;
                    }
                }
                catch { /* skip name errors */ }

                string name = U.ToHexString(i) + " " + tname;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Sound editors (FE6 variant)
        // ==================================================================

        /// <summary>Build FE6 sound room list — same pointer as SoundRoomViewerView but with FE6-specific format.</summary>
        static List<AddrResult> BuildSoundRoomFE6List(ROM rom)
        {
            uint pointer = rom.RomInfo.sound_room_pointer;
            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (pointer == 0 || dataSize == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0xFFFFFFFF) break;
                // Match SoundRoomFE6ViewModel: custom IsEmptyBlock that checks for 0x00 and 0xFF
                if (i > 10)
                {
                    uint end = addr + dataSize * 10;
                    if (end > (uint)rom.Data.Length) end = (uint)rom.Data.Length;
                    bool empty = true;
                    for (uint a = addr; a < end; a++)
                        if (rom.Data[a] != 0x00 && rom.Data[a] != 0xFF) { empty = false; break; }
                    if (empty) break;
                }

                // Match SoundRoomFE6ViewModel.TryDecodeSongName: text ID at offset 4
                string songName;
                try
                {
                    uint textId = rom.u32(addr + 4);
                    if (textId > 0 && textId < 0xFFFF)
                        songName = GetTextById(textId);
                    else
                        songName = $"Song {rom.u32(addr):X}";
                }
                catch { songName = $"Song {rom.u32(addr):X}"; }
                string display = $"{(i + 1):D3} {songName}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Monster editors
        // ==================================================================

        /// <summary>Build monster item list — 5-byte entries, stop at 0xFF.</summary>
        static List<AddrResult> BuildMonsterItemList(ROM rom)
        {
            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 5;
                if (addr + 5 > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                uint itemId = rom.u8(addr);
                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Build monster item probability list — 5-byte entries at
        /// `monster_item_probability_pointer`, stop at 0xFF. Mirrors
        /// the WF N1 sub-list of MonsterItemForm. See #394.
        /// </summary>
        public static List<AddrResult> BuildMonsterItemProbabilityList(ROM rom)
        {
            uint ptr = rom.RomInfo.monster_item_probability_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 5;
                if (addr + 5 > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;
                result.Add(new AddrResult(addr, $"{U.ToHexString(i)} Prob", i));
            }
            return result;
        }

        /// <summary>
        /// Build monster item holdings list — 32-byte entries at
        /// `monster_item_table_pointer`, stop at 0xFF. Mirrors the WF
        /// N2 sub-list of MonsterItemForm. See #394.
        /// </summary>
        public static List<AddrResult> BuildMonsterItemHoldingsList(ROM rom)
        {
            uint ptr = rom.RomInfo.monster_item_table_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 32;
                if (addr + 32 > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                uint classId = rom.u8(addr);
                string className = NameResolver.GetClassName(classId);
                result.Add(new AddrResult(addr, $"{U.ToHexString(i)} {className}", i));
            }
            return result;
        }

        /// <summary>Build monster world map probability list — 9 fixed 1-byte entries.</summary>
        static List<AddrResult> BuildMonsterWMapProbabilityList(ROM rom)
        {
            uint ptr = rom.RomInfo.monster_wmap_base_point_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 9; i++)
            {
                uint addr = baseAddr + i;
                if (addr >= (uint)rom.Data.Length) break;

                uint basePointId = rom.u8(addr);
                string name = U.ToHexString(i) + " WMap Monster 0x" + basePointId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Status/Menu editors
        // ==================================================================

        /// <summary>Build status option order list — 1-byte entries, count from count address.</summary>
        static List<AddrResult> BuildStatusOptionOrderList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint countAddr = rom.RomInfo.status_game_option_order_count_address;
            uint count = 0;
            if (countAddr != 0 && U.isSafetyOffset(countAddr))
                count = rom.u8(countAddr);
            if (count == 0 || count > 0x40)
                count = 0x20;

            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = baseAddr + i;
                if (addr >= (uint)rom.Data.Length) break;

                uint optionId = rom.u8(addr);
                string name = $"{U.ToHexString(i)} Option {optionId}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Build status R-menu list — 28-byte nodes discovered via the WF
        /// directional-pointer traversal (#1459). Parity uses table 0 (the unit
        /// table), the same root the WF list opens on by default.
        /// </summary>
        static List<AddrResult> BuildStatusRMenuList(ROM rom)
        {
            return StatusRMenuListCore.BuildTableList(rom, 0);
        }

        /// <summary>Build status units menu list — 16-byte entries, stop when order >= 0xFF.</summary>
        static List<AddrResult> BuildStatusUnitsMenuList(ROM rom)
        {
            uint pointer = rom.RomInfo.status_units_menu_pointer;
            if (pointer == 0 || !U.isSafetyOffset(pointer)) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 16;
                if (addr + 16 > (uint)rom.Data.Length) break;

                uint order = rom.u32(addr);
                if (order >= 0xFF) break;

                string name = U.ToHexString(i) + " Order:" + order.ToString();
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build menu command list — dynamic list of 36-byte records from the
        /// menu definition table. The usability FUNCTION addresses (UsabilityAlways/Never)
        /// are intentionally NOT listed (ROM code, not records) — kept in lockstep with
        /// <see cref="MenuCommandViewModel.LoadMenuCommandList"/> (#1404).</summary>
        static List<AddrResult> BuildMenuCommandList(ROM rom)
        {
            var result = new List<AddrResult>();

            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr != 0)
            {
                uint defBase = rom.p32(ptr);
                if (U.isSafetyOffset(defBase))
                {
                    uint idx = 0;
                    for (uint i = 0; i < 0x100; i++)
                    {
                        uint defAddr = defBase + i * 36;
                        if (defAddr + 36 > (uint)rom.Data.Length) break;
                        if (!U.isPointer(rom.u32(defAddr + 8))) break;

                        uint menuCmdPtr = rom.p32(defAddr + 8);
                        if (!U.isSafetyOffset(menuCmdPtr)) continue;

                        for (uint j = 0; j < 0x40; j++)
                        {
                            uint cmdAddr = menuCmdPtr + j * 36;
                            if (cmdAddr + 36 > (uint)rom.Data.Length) break;
                            if (!U.isPointer(rom.u32(cmdAddr + 0xC))) break;

                            string name = U.ToHexString(idx) + " MenuCmd Def" + i.ToString() + "_" + j.ToString();
                            result.Add(new AddrResult(cmdAddr, name, idx));
                            idx++;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Build menu extend split menu list — canonical 36-byte
        /// menu-definition geometry, stop when <c>+8</c> (the command-array
        /// pointer) is no longer a pointer. Mirrors WinForms
        /// <c>MenuDefinitionForm.Init</c> over the split pointer and
        /// <see cref="MenuExtendSplitMenuViewModel.LoadList"/> (#1413). The old
        /// 40-byte / 32-entry walk fabricated rows and is removed.</summary>
        static List<AddrResult> BuildMenuExtendSplitMenuList(ROM rom)
        {
            uint ptr = rom.RomInfo.menu_definiton_split_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 36;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Stop when the +8 command-array pointer is no longer a pointer.
                if (!U.isPointer(rom.u32(addr + 8))) break;

                result.Add(new AddrResult(addr, $"0x{i:X02} Split Menu {i}", i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Text editors
        // ==================================================================

        /// <summary>Build text dictionary list from dic_main_pointer — 12-byte entries, stop when textId1 or textId2 == 0.</summary>
        static List<AddrResult> BuildTextDicList(ROM rom)
        {
            uint pointer = rom.RomInfo.dic_main_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint textId1 = rom.u16(addr + 2);
                uint textId2 = rom.u16(addr + 4);
                if (textId1 == 0 || textId2 == 0) break;

                string text = GetTextById(textId1);
                string display = $"0x{i:X2} {text}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — Unit/Class FE7/FE6 variants
        // ==================================================================

        /// <summary>Build FE7 unit list — no entry skip, starts from index 0.</summary>
        static List<AddrResult> BuildUnitFE7List(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 253;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build FE6 support unit list — 32-byte entries with gap tolerance.</summary>
        static List<AddrResult> BuildSupportUnitFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            const uint blockSize = 32;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint first = rom.u8(addr);
                if (first == 0 && i > 0)
                {
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = baseAddr + (i + j) * blockSize;
                        if (checkAddr + blockSize > (uint)rom.Data.Length) break;
                        if (rom.u8(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                string unitName = NameResolver.GetUnitName(i);
                string name = $"{U.ToHexString(i)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build FE6 support talk list — 16-byte entries, skip empty entries, stop after 10 consecutive empties.
        /// FE6 format: u8(addr+0) = unit1, u8(addr+1) = unit2.</summary>
        static List<AddrResult> BuildSupportTalkFE6List(ROM rom)
        {
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint dataSize = 16;
            var result = new List<AddrResult>();
            int emptyCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0)
                {
                    emptyCount++;
                    if (emptyCount >= 10) break;
                    continue;
                }
                emptyCount = 0;

                // FE6 VM reads u8(addr+0) and u8(addr+1) and shows "(0x{id:X02})" format.
                // uid1/uid2 are 1-based ROM-stored unit IDs (matches WinForms convention).
                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 1);
                string n1 = NameResolver.GetUnitNameByOneBasedId(uid1);
                string n2 = NameResolver.GetUnitNameByOneBasedId(uid2);
                string name = $"{U.ToHexString(i)} {n1} (0x{uid1:X02}) & {n2} (0x{uid2:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build FE7 support talk list — 20-byte entries, skip empty entries, stop after 10 consecutive empties.
        /// FE7 format: u8(addr+0) = unit1, u8(addr+1) = unit2.</summary>
        static List<AddrResult> BuildSupportTalkFE7List(ROM rom)
        {
            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint dataSize = 20;
            var result = new List<AddrResult>();
            int emptyCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0)
                {
                    emptyCount++;
                    if (emptyCount >= 10) break;
                    continue;
                }
                emptyCount = 0;

                // FE7 VM reads u8(addr+0) and u8(addr+1) and shows "(0x{id:X02})" format.
                // uid1/uid2 are 1-based ROM-stored unit IDs (matches WinForms convention).
                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 1);
                string n1 = NameResolver.GetUnitNameByOneBasedId(uid1);
                string n2 = NameResolver.GetUnitNameByOneBasedId(uid2);
                string name = $"{U.ToHexString(i)} {n1} (0x{uid1:X02}) & {n2} (0x{uid2:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 4 builders — OP Class editors
        // ==================================================================

        /// <summary>Build OP class alpha name list — 4-byte pointer entries from class_alphaname_pointer.</summary>
        static List<AddrResult> BuildOPClassAlphaNameList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.class_alphaname_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " Alpha Name";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassDemoViewer list — 28-byte entries, validate first dword pointer, show class name.</summary>
        static List<AddrResult> BuildOPClassDemoViewerList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 28;
                if (addr + 28 > (uint)rom.Data.Length) break;

                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;

                uint cid = rom.u8(addr + 14);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassFontViewer list — 4-byte pointer entries.</summary>
        static List<AddrResult> BuildOPClassFontViewerList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_font_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " OP Class Font";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // ==================================================================
        // Batch 5 builders — remaining coverable editors
        // ==================================================================

        /// <summary>Build unit action pointer list — 4-byte pointer entries. Honors the
        /// UnitActionRework patch (relocated base, 0-based ids, <c>&amp; 0x0FFFFFFF</c> masking)
        /// via <see cref="UnitActionPointerCore"/>, matching WinForms UnitActionPointerForm (#1415).</summary>
        static List<AddrResult> BuildUnitActionPointerList(ROM rom)
        {
            uint baseAddr = UnitActionPointerCore.ResolveBaseAddress(rom);
            if (baseAddr == 0) return new List<AddrResult>();

            bool isRework = UnitActionPointerCore.IsRework(rom);
            const uint entrySize = 4;
            return EditorFormRef.BuildListWithCount(rom, baseAddr, entrySize,
                (i, addr) => UnitActionPointerCore.IsDataExists(rom, addr, isRework),
                (i, addr) =>
                {
                    uint id = UnitActionPointerCore.ResolveActionId(i, isRework);
                    return $"{U.ToHexString(id)} Action {id}";
                });
        }

        /// <summary>Build unit increase height list — uses switch2 pattern detection.
        /// Matches UnitIncreaseHeightViewModel.LoadList(): uses GetPortraitName (not GetUnitName).</summary>
        static List<AddrResult> BuildUnitIncreaseHeightList(ROM rom)
        {
            uint switch2Addr = rom.RomInfo.unit_increase_height_switch2_address;
            uint pointer = rom.RomInfo.unit_increase_height_pointer;
            if (switch2Addr == 0 || pointer == 0) return new List<AddrResult>();

            // Check switch2 enable pattern (same as VM's IsSwitch2Enable)
            if (!U.isSafetyOffset(switch2Addr + 5)) return new List<AddrResult>();
            uint extraByte = 0;
            if (rom.u16(switch2Addr + 2) == 0x9A00) extraByte = 2;
            uint op1 = rom.u8(switch2Addr + 1);
            if (op1 < 0x38 || op1 > 0x3D) return new List<AddrResult>();
            uint op2 = rom.u8(switch2Addr + 3 + extraByte);
            if (op2 < 0x28 || op2 > 0x2D) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint count = rom.u8(switch2Addr + 2) + 1u;
            uint startId = rom.u8(switch2Addr);

            const uint entrySize = 4;
            return EditorFormRef.BuildList(rom, baseAddr, entrySize, (int)count,
                (i, addr) =>
                {
                    uint id = startId + (uint)i;
                    string name = NameResolver.GetPortraitName(id);
                    return $"{U.ToHexString(id)} {name}";
                });
        }

        /// <summary>
        /// Build the unit custom battle anime list — the FE7 POINTER TABLE at
        /// <c>unit_custom_battle_anime_pointer</c> (one u32 pointer per class). Mirrors the WinForms
        /// <c>UnitCustomBattleAnimeForm.N2_Init</c> rule (<c>i==0 || isPointer(u32)</c>), NOT a plain
        /// <c>val==0</c> stop (#1412 plan review point 5). Each row is a pointer-table SLOT, NOT a
        /// weapon-anime record — drilling into the inner list requires a SECOND p32 dereference.
        /// </summary>
        static List<AddrResult> BuildUnitCustomBattleAnimeList(ROM rom)
        {
            uint pointer = rom.RomInfo.unit_custom_battle_anime_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // WinForms N2 rule: i==0 ? true : isPointer(u32(addr)).
                if (i != 0 && !U.isPointer(rom.u32(addr))) break;

                // Label = the OWNING unit + lower/upper marker for this custom-battle-anime index
                // (WinForms UnitFE7Form.GetNameWhereCustomBattleAnime). NOT GetUnitName(i): `i` is the
                // custom-battle-anime id (matched against unit +37/+38), not a unit-table row (#1412).
                string label = NameResolver.GetCustomBattleAnimeName(rom, i);
                string name = label.Length == 0 ? U.ToHexString(i) : $"{U.ToHexString(i)} {label}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build extra unit list (FE8J) — hardcoded base 0x37EE4, 4-byte pointer entries.</summary>
        static List<AddrResult> BuildExtraUnitList(ROM rom)
        {
            const uint baseAddress = 0x37EE4;
            const uint entrySize = 4;
            return EditorFormRef.BuildListWithCount(rom, baseAddress, entrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr)),
                (i, addr) =>
                {
                    uint flagAddr = (uint)(i * 0x14 + 0x37E10);
                    uint flagId = rom.u8(flagAddr);
                    uint unitsAddr = rom.p32(addr);
                    uint unitId = U.isSafetyOffset(unitsAddr) ? rom.u8(unitsAddr) : 0;
                    // 1-based ROM-stored unit ID.
                    string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                    return $"{U.ToHexString((uint)i)} Flag=0x{flagId:X02} {unitName}";
                });
        }

        /// <summary>Build map editor list — uses MapSettingCore.MakeMapIDList().</summary>
        static List<AddrResult> BuildMapEditorList(ROM rom)
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>Build map style editor list — PLIST entries from map_obj_pointer.</summary>
        static List<AddrResult> BuildMapStyleEditorList(ROM rom)
        {
            uint objPointer = rom.RomInfo.map_obj_pointer;
            if (objPointer == 0) return new List<AddrResult>();
            uint tableBase = rom.p32(objPointer);
            if (!U.isSafetyOffset(tableBase, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint entryAddr = (uint)(tableBase + i * 4);
                if (entryAddr + 4 > (uint)rom.Data.Length) break;
                uint ptr = rom.u32(entryAddr);
                if (ptr == 0 || !U.isPointer(ptr)) continue;
                string label = $"0x{i:X2} Tileset";
                result.Add(new AddrResult(entryAddr, label, (uint)i));
            }
            return result;
        }

        /// <summary>Build map terrain BG lookup table list — byte entries from lookup_table_battle_bg_00_pointer.
        /// Matches MapTerrainBGLookupTableViewModel.LoadList(): count = map_terrain_type_count, format "0x{i:X02} Terrain {i}".</summary>
        static List<AddrResult> BuildMapTerrainBGLookupTableList(ROM rom)
        {
            uint ptr = rom.RomInfo.lookup_table_battle_bg_00_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        /// <summary>Build map terrain floor lookup table list — byte entries from lookup_table_battle_terrain_00_pointer.
        /// Matches MapTerrainFloorLookupTableViewModel.LoadList(): count = map_terrain_type_count, format "0x{i:X02} Terrain {i}".</summary>
        static List<AddrResult> BuildMapTerrainFloorLookupTableList(ROM rom)
        {
            uint ptr = rom.RomInfo.lookup_table_battle_terrain_00_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        /// <summary>Build minimap terrain image list — from map_minimap_tile_array_pointer.
        /// Matches MapMiniMapTerrainImageViewModel.LoadList(): 4-byte entries, count = map_terrain_type_count, format "0x{i:X02} Terrain {i}".</summary>
        static List<AddrResult> BuildMapMiniMapTerrainImageList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_minimap_tile_array_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
            const uint entrySize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i * entrySize);
                if (addr + entrySize > (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        /// <summary>Build map tile animation 1 list — PLIST-based (#955).
        /// Matches MapTileAnimation1ViewModel.LoadList() EXACTLY: build the
        /// anime1 PLIST filter via MapTileAnimation1Core.BuildPlistList, pick
        /// the FIRST non-broken PLIST (regardless of whether its data table is
        /// empty — the VM returns on the first non-broken row), then scan that
        /// PLIST's resolved data table (8-byte blocks, validated by
        /// isPointer(u32(addr+4))). #960: an earlier version skipped a
        /// non-broken-but-empty PLIST (`if (entries.Count == 0) continue;`),
        /// which the VM does NOT do — that mismatch made the VM↔golden lockstep
        /// test flaky on ROMs whose first non-broken PLIST is empty. Lockstep
        /// with MapTileAnimation1Core.ScanEntries.</summary>
        static List<AddrResult> BuildMapTileAnimation1List(ROM rom)
        {
            var plistRows = MapTileAnimation1Core.BuildPlistList(rom);
            foreach (var row in plistRows)
            {
                if (row.IsBroken) continue;
                // First non-broken PLIST wins — return its scan even when empty
                // (mirrors the VM's `return BuildList(row.Addr);` on the first
                // non-broken row).
                var entries = MapTileAnimation1Core.ScanEntries(rom, row.Addr, maxRows: 256);
                var result = new List<AddrResult>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    string display = $"0x{i:X2} Interval={e.Wait:X4} Count={e.Length:X4}";
                    result.Add(new AddrResult(e.Addr, display, (uint)i));
                }
                return result;
            }
            return new List<AddrResult>();
        }

        /// <summary>Build the MapTileAnimation1 FILTER-combo list (the anime1
        /// PLIST filter rows shown above the entry list), in lockstep with
        /// MapTileAnimation1ViewModel.LoadPlistList() →
        /// MapTileAnimation1Core.BuildPlistList(). Each filter row's Display is
        /// the resolved "ANIME1 MapName" label via the shared resolver (#952,
        /// #955), mirroring the anime2 "ANIME2 MapName" filter. Returns one
        /// AddrResult per filter row (addr = resolved data offset, name =
        /// resolved Display, tag = PLIST id) so parity tests can compare the VM
        /// filter labels against this golden builder.</summary>
        public static List<AddrResult> BuildMapTileAnimation1FilterList(ROM rom)
        {
            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            var result = new List<AddrResult>(rows.Count);
            foreach (var row in rows)
            {
                result.Add(new AddrResult(row.Addr, row.Display, row.Plist));
            }
            return result;
        }

        /// <summary>Build map tile animation 2 list — from map_tileanime2_pointer (PLIST-based).
        /// Matches MapTileAnimation2ViewModel.LoadList(): scans PLIST for first valid entry, then uses BuildList() on data.</summary>
        static List<AddrResult> BuildMapTileAnimation2List(ROM rom)
        {
            uint ptr = rom.RomInfo.map_tileanime2_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint plistBase = rom.p32(ptr);
            if (!U.isSafetyOffset(plistBase, rom)) return new List<AddrResult>();

            uint plistLimit = rom.RomInfo.map_map_pointer_list_default_size;
            if (plistLimit == 0) plistLimit = 256;

            // Scan PLIST entries for the first valid entry whose data passes validation (same as VM)
            for (uint i = 0; i < plistLimit; i++)
            {
                uint entryAddr = plistBase + i * 4;
                if (entryAddr + 4 > (uint)rom.Data.Length) break;
                uint entryPtr = rom.u32(entryAddr);
                if (!U.isPointer(entryPtr)) continue;
                uint dataAddr = U.toOffset(entryPtr);
                if (!U.isSafetyOffset(dataAddr, rom)) continue;

                // Build list from this data address (8-byte blocks, validate P0 is pointer)
                const uint blockSize = 8;
                var result = new List<AddrResult>();
                for (int j = 0; j < 256; j++)
                {
                    uint addr = dataAddr + (uint)(j * blockSize);
                    if (addr + blockSize > (uint)rom.Data.Length) break;
                    if (!U.isPointer(rom.u32(addr + 0))) break;

                    uint interval = rom.u8(addr + 4);
                    uint count = rom.u8(addr + 5);
                    string display = $"0x{j:X2} Palette Interval={interval:X2} Count={count:X2}";
                    result.Add(new AddrResult(addr, display, (uint)j));
                }
                if (result.Count > 0) return result;
            }

            return new List<AddrResult>();
        }

        /// <summary>Build the MapTileAnimation2 FILTER-combo list (the PLIST
        /// filter rows shown above the entry list), in lockstep with
        /// MapTileAnimation2ViewModel.LoadPlistList() →
        /// MapTileAnimation2Core.BuildPlistList(). Each filter row's Display is
        /// the resolved "ANIME2 MapName" label via the shared resolver (#952,
        /// #11), not the raw "タイルアニメーション2 パレットアニメ:{plist}" string.
        /// Returns one AddrResult per filter row (addr = resolved data offset,
        /// name = resolved Display, tag = PLIST id) so parity tests can compare
        /// the VM filter labels against this golden builder.</summary>
        public static List<AddrResult> BuildMapTileAnimation2FilterList(ROM rom)
        {
            var rows = MapTileAnimation2Core.BuildPlistList(rom);
            var result = new List<AddrResult>(rows.Count);
            foreach (var row in rows)
            {
                result.Add(new AddrResult(row.Addr, row.Display, row.Plist));
            }
            return result;
        }

        /// <summary>Build map terrain name (English) list — from map_terrain_name_pointer.
        /// Matches MapTerrainNameEngViewModel.LoadList(): 2-byte text ID entries, terminates on textId==0.</summary>
        static List<AddrResult> BuildMapTerrainNameEngList(ROM rom)
        {
            uint pointer = rom.RomInfo.map_terrain_name_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint textId = rom.u16(addr);
                if (textId == 0x0000) break;

                string name = NameResolver.GetTextById(textId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {name}", (uint)i));
            }
            return result;
        }

        /// <summary>Build event function pointer FE7 list — 8-byte entries matching EventFunctionPointerFE7ViewModel.LoadList().</summary>
        static List<AddrResult> BuildEventFunctionPointerFE7List(ROM rom)
        {
            uint pointer = rom.RomInfo.event_function_pointer_table_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint funcPtr = rom.u32(addr);
                if (!U.isPointer(funcPtr)) break;

                result.Add(new AddrResult(addr, $"0x{i:X2} 0x{funcPtr:X08}", (uint)i));
            }
            return result;
        }

        /// <summary>Build event map change list — matches EventMapChangeViewModel.LoadEventMapChangeList().
        /// Walks map settings to find first map with non-zero map change PLIST, resolves and validates data.</summary>
        static List<AddrResult> BuildEventMapChangeList(ROM rom)
        {
            uint mapPtr = rom.RomInfo.map_setting_pointer;
            uint mapDataSize = rom.RomInfo.map_setting_datasize;
            uint mapChangePtr = rom.RomInfo.map_mapchange_pointer;
            if (mapPtr == 0 || mapDataSize == 0 || mapChangePtr == 0)
                return new List<AddrResult>();

            uint mapBase = rom.p32(mapPtr);
            if (!U.isSafetyOffset(mapBase)) return new List<AddrResult>();

            uint plistBase = rom.p32(mapChangePtr);
            if (!U.isSafetyOffset(plistBase)) return new List<AddrResult>();

            uint romLen = (uint)rom.Data.Length;

            for (int mapId = 0; mapId < 256; mapId++)
            {
                uint mapAddr = (uint)(mapBase + mapId * mapDataSize);
                if (mapAddr + mapDataSize > romLen) break;

                uint plist = rom.u8(mapAddr + 11);
                if (plist == 0 || plist == 0xFF) continue;

                uint plistEntryAddr = (uint)(plistBase + plist * 4);
                if (plistEntryAddr + 4 > romLen) continue;

                uint changeAddr = rom.p32(plistEntryAddr);
                if (!U.isSafetyOffset(changeAddr) || changeAddr + 12 > romLen) continue;

                if (rom.u8(changeAddr) == 0xFF) continue;

                return new List<AddrResult> { new AddrResult(changeAddr, $"Map {mapId} Change 0", 0) };
            }

            return new List<AddrResult>();
        }

        /// <summary>Build event final serif FE7 list — from event_final_serif_pointer.</summary>
        static List<AddrResult> BuildEventFinalSerifFE7List(ROM rom)
        {
            uint pointer = rom.RomInfo.event_final_serif_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint val = rom.u32(addr);
                if (val == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Final Serif";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build ImageCGFE7U list — from bigcg_pointer, 12-byte entries.</summary>
        static List<AddrResult> BuildImageCGFE7UList(ROM rom)
        {
            uint ptr = rom.RomInfo.bigcg_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint SIZE = 12;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;
                string name = $"{U.ToHexString(i)} CG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build ImageMagicFEditor list — from magic_effect_pointer.</summary>
        static List<AddrResult> BuildImageMagicFEditorList(ROM rom)
        {
            uint pointer = rom.RomInfo.magic_effect_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint SIZE = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p) && p != 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Magic Effect";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build ImageMapActionAnimation list — uses binary signature search (FE8 only).</summary>
        static List<AddrResult> BuildImageMapActionAnimationList(ROM rom)
        {
            // Only FE8 has this editor
            if (rom.RomInfo.version != 8) return new List<AddrResult>();

            byte[] bin;
            if (rom.RomInfo.is_multibyte)
            {   // FE8J
                bin = new byte[] { 0x54, 0x3C, 0x08, 0x08, 0xEC, 0xE1, 0x03, 0x02,
                                   0xE8, 0xA4, 0x03, 0x02, 0x68, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }
            else
            {   // FE8U
                bin = new byte[] { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02,
                                   0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }

            uint startAddr = rom.RomInfo.compress_image_borderline_address;
            uint p = U.GrepEnd(rom.Data, bin, startAddr, 0, 4, 0, true);
            if (p == U.NOT_FOUND) return new List<AddrResult>();

            p = p - (uint)bin.Length - 4;
            uint a = rom.u32(p);
            if (!U.isPointer(a)) return new List<AddrResult>();

            uint baseAddr = rom.p32(p);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint SIZE = 8;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint val = rom.u32(addr);
                if (!U.isPointer(val) && val != 0) break;
                if (val == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Map Action Animation";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build ImageChapterTitleFE7 list — from image_chapter_title_pointer.</summary>
        static List<AddrResult> BuildImageChapterTitleFE7List(ROM rom)
        {
            uint pointer = rom.RomInfo.image_chapter_title_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p) && p != 0) break;
                if (p == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Chapter Title";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Build ImageTSAAnime2 list — from config TSV resource tsaanime2_.
        /// #1456: enumerate every per-category 12-byte entry (WinForms two-level
        /// editor), not one row per category, so --data-verify-full covers all
        /// reachable entries. Uses the same stop condition as the VM
        /// (<see cref="FEBuilderGBA.Avalonia.ViewModels.ImageTSAAnime2ViewModel.CountCategoryEntries"/>).
        /// </summary>
        static List<AddrResult> BuildImageTSAAnime2List(ROM rom)
        {
            var tsaAnime = U.LoadTSVResource1(U.ConfigDataFilename("tsaanime2_"), false);
            if (tsaAnime == null || tsaAnime.Count == 0) return new List<AddrResult>();

            const uint SIZE = 12;
            var result = new List<AddrResult>();
            foreach (var pair in tsaAnime)
            {
                uint pointer = pair.Key;
                string catName = pair.Value;
                uint offset = U.toOffset(pointer);
                if (!U.isSafetyOffset(offset, rom)) continue;
                uint dataAddr = rom.p32(offset);
                if (!U.isSafetyOffset(dataAddr, rom)) continue;

                uint entry0Addr = dataAddr + 20;
                uint count = FEBuilderGBA.Avalonia.ViewModels.ImageTSAAnime2ViewModel
                    .CountCategoryEntries(rom, entry0Addr);
                for (uint i = 0; i < count; i++)
                {
                    uint entryAddr = entry0Addr + i * SIZE;
                    string label = U.ToHexString(pointer) + " " + catName + " " + U.To0xHexString(i);
                    result.Add(new AddrResult(entryAddr, label, pointer));
                }
            }
            return result;
        }

        /// <summary>Build song track list — from sound_table_pointer.</summary>
        static List<AddrResult> BuildSongTrackList(ROM rom)
        {
            uint tablePtr = rom.RomInfo.sound_table_pointer;
            if (tablePtr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(tablePtr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint entrySize = 8;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * entrySize;
                if (addr + entrySize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p) && p != 0 && i > 0) break;
                string songName = NameResolver.GetSongName(i);
                string name = $"{U.ToHexString(i)} {songName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build SoundRoomCG list — from sound_room_cg_pointer.</summary>
        static List<AddrResult> BuildSoundRoomCGList(ROM rom)
        {
            uint pointer = rom.RomInfo.sound_room_cg_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (p == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} CG";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build world map event pointer list — from map_worldmapevent_pointer.</summary>
        static List<AddrResult> BuildWorldMapEventPointerList(ROM rom)
        {
            uint ptr = rom.RomInfo.map_worldmapevent_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p) && p != 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} WMap Event";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build EDSensekiComment list — from senseki_comment_pointer.</summary>
        static List<AddrResult> BuildEDSensekiCommentList(ROM rom)
        {
            uint ptr = rom.RomInfo.senseki_comment_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint first = rom.u32(addr);
                if (first == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Senseki Comment";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build status param list — from status_param1_pointer (default table).</summary>
        static List<AddrResult> BuildStatusParamList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.status_param1_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint strPtr = rom.u32(addr + 12);
                if (!U.isPointer(strPtr) && strPtr != 0 && i > 0) break;
                if (strPtr == 0 && i > 0) break;

                string name = $"{U.ToHexString(i)} Status Param";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassDemoFE7 list — 32-byte entries, classId at offset 15.</summary>
        static List<AddrResult> BuildOPClassDemoFE7List(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;
                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;
                uint cid = rom.u8(addr + 15);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassDemoFE7U list — 28-byte entries, classId at offset 11.</summary>
        static List<AddrResult> BuildOPClassDemoFE7UList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;
                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;
                uint cid = rom.u8(addr + 11);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassDemoFE8U list — 20-byte entries, classId at offset 5.</summary>
        static List<AddrResult> BuildOPClassDemoFE8UList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;
                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;
                uint cid = rom.u8(addr + 5);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassFontFE8U list — 4-byte pointer entries from op_class_font_pointer.</summary>
        static List<AddrResult> BuildOPClassFontFE8UList(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.op_class_font_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;
                string name = $"{U.ToHexString(i)} OP Class Font";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build OPClassAlphaNameFE6 list — 4-byte pointer entries from class_alphaname_pointer.</summary>
        static List<AddrResult> BuildOPClassAlphaNameFE6List(ROM rom)
        {
            uint ptrAddr = rom.RomInfo.class_alphaname_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;
                string name = $"{U.ToHexString(i)} Alpha Name";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build MantAnimation list — from mant_command_pointer.</summary>
        static List<AddrResult> BuildMantAnimationList(ROM rom)
        {
            uint pointer = rom.RomInfo.mant_command_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr);
                if (p == 0 && i > 0) break;
                string name = $"{U.ToHexString(i)} Mant Animation";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        // Removed stub builders for ItemStatBonusesSkillSystems, ItemStatBonusesVenno,
        // EventMoveDataFE7, EventTalkGroupFE7, EventBattleDataFE7.
        // These are now in ContextDependentEditors set instead.
    }

    /// <summary>Result of a list parity comparison for one editor.</summary>
    public class ListParityResult
    {
        public string EditorName { get; set; }
        public int AvaloniaCount { get; set; }
        public int WinFormsCount { get; set; }
        public int TextMatches { get; set; }
        public bool IsMatch { get; set; }

        /// <summary>Index of first address difference (-1 = none).</summary>
        public int FirstAddrDiffIndex { get; set; } = -1;
        public uint FirstAddrDiffAvalonia { get; set; }
        public uint FirstAddrDiffWinForms { get; set; }

        /// <summary>Index of first text difference (-1 = none).</summary>
        public int FirstTextDiffIndex { get; set; } = -1;
        public string FirstTextDiffAvalonia { get; set; }
        public string FirstTextDiffWinForms { get; set; }

        public string FormatResult()
        {
            string status = IsMatch ? "MATCH" : "MISMATCH";
            string line = $"LISTPARITY: {EditorName} | avalonia_count={AvaloniaCount} | winforms_count={WinFormsCount} | text_match={TextMatches}/{Math.Max(AvaloniaCount, WinFormsCount)} | {status}";

            if (!IsMatch)
            {
                if (AvaloniaCount != WinFormsCount)
                    line += $" (count differs: {AvaloniaCount} vs {WinFormsCount})";
                if (FirstAddrDiffIndex >= 0)
                    line += $" (first addr diff at [{FirstAddrDiffIndex}]: 0x{FirstAddrDiffAvalonia:X} vs 0x{FirstAddrDiffWinForms:X})";
                if (FirstTextDiffIndex >= 0)
                    line += $" (first text diff at [{FirstTextDiffIndex}]: \"{Truncate(FirstTextDiffAvalonia, 40)}\" vs \"{Truncate(FirstTextDiffWinForms, 40)}\")";
            }

            return line;
        }

        static string Truncate(string s, int max) =>
            s != null && s.Length > max ? s.Substring(0, max) + "..." : s ?? "";
    }
}
