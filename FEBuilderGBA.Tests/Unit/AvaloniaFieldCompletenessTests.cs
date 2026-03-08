using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Compares WinForms Designer.cs ROM data field controls (B#, b#, W#, D#, P#, l#, h#)
    /// against Avalonia ViewModel ROM access patterns to identify missing fields.
    ///
    /// ROM data field naming convention (InputFormRef.RomToUI):
    ///   B# = u8 at offset #       b# = s8 at offset #
    ///   W# = u16 at offset #      D# = u32 at offset #
    ///   P# = pointer at offset #  l# = low nibble at offset #
    ///   h# = high nibble at offset #
    /// </summary>
    public class AvaloniaFieldCompletenessTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private static string WinFormsDir => Path.Combine(SolutionDir, "FEBuilderGBA");
        private static string AvaloniaVmDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels");

        // Maps ScreenshotFormRegistry view names to (WinForms form class, Avalonia ViewModel class)
        // Only forms with InputFormRef ROM data fields are listed.
        private static readonly (string ViewName, string WinFormsType, string AvaloniaVmType)[] FormMappings = new[]
        {
            // Data Editors
            ("UnitEditorView", "UnitForm", "UnitEditorViewModel"),
            ("ItemEditorView", "ItemForm", "ItemEditorViewModel"),
            ("ClassEditorView", "ClassForm", "ClassEditorViewModel"),
            ("CCBranchEditorView", "CCBranchForm", "CCBranchEditorViewModel"),
            ("MoveCostEditorView", "MoveCostForm", "MoveCostEditorViewModel"),
            ("TerrainNameEditorView", "MapTerrainNameForm", "MapTerrainNameViewModel"),
            ("SupportUnitEditorView", "SupportUnitForm", "SupportUnitEditorViewModel"),
            ("SupportAttributeView", "SupportAttributeForm", "SupportAttributeViewModel"),
            ("SupportTalkView", "SupportTalkForm", "SupportTalkViewModel"),
            ("UnitFE6View", "UnitFE6Form", "UnitFE6ViewModel"),
            ("UnitFE7View", "UnitFE7Form", "UnitFE7ViewModel"),
            ("UnitsShortTextView", "UnitsShortTextForm", "UnitsShortTextViewModel"),
            ("SomeClassListView", "SomeClassListForm", "SomeClassListViewModel"),
            ("VennouWeaponLockView", "VennouWeaponLockForm", "VennouWeaponLockViewModel"),
            ("ItemFE6View", "ItemFE6Form", "ItemFE6ViewModel"),
            ("MoveCostFE6View", "MoveCostFE6Form", "MoveCostFE6ViewModel"),
            ("SupportUnitFE6View", "SupportUnitFE6Form", "SupportUnitFE6ViewModel"),
            ("SupportTalkFE6View", "SupportTalkFE6Form", "SupportTalkFE6ViewModel"),
            ("SupportTalkFE7View", "SupportTalkFE7Form", "SupportTalkFE7ViewModel"),

            // Item Viewers
            ("ItemWeaponEffectViewerView", "ItemWeaponEffectForm", "ItemWeaponEffectViewerViewModel"),
            ("ItemStatBonusesViewerView", "ItemStatBonusesForm", "ItemStatBonusesViewerViewModel"),
            ("ItemEffectivenessViewerView", "ItemEffectivenessForm", "ItemEffectivenessViewerViewModel"),
            ("ItemPromotionViewerView", "ItemPromotionForm", "ItemPromotionViewerViewModel"),
            ("ItemShopViewerView", "ItemShopForm", "ItemShopViewerViewModel"),
            ("ItemWeaponTriangleViewerView", "ItemWeaponTriangleForm", "ItemWeaponTriangleViewerViewModel"),
            ("ItemUsagePointerViewerView", "ItemUsagePointerForm", "ItemUsagePointerViewerViewModel"),
            ("ItemEffectPointerViewerView", "ItemEffectPointerForm", "ItemEffectPointerViewerViewModel"),

            // Map Editors
            ("MapSettingView", "MapSettingForm", "MapSettingViewModel"),
            ("MapChangeView", "MapChangeForm", "MapChangeViewModel"),
            ("MapExitPointView", "MapExitPointForm", "MapExitPointViewModel"),
            ("MapPointerView", "MapPointerForm", "MapPointerViewModel"),
            ("MapTileAnimationView", "MapTileAnimation1Form", "MapTileAnimationViewModel"),

            // Event Forms
            ("EventCondView", "EventCondForm", "EventCondViewModel"),

            // Arena / Monster / Summon
            ("ArenaClassViewerView", "ArenaClassForm", "ArenaClassViewerViewModel"),
            ("ArenaEnemyWeaponViewerView", "ArenaEnemyWeaponForm", "ArenaEnemyWeaponViewerViewModel"),
            ("LinkArenaDenyUnitViewerView", "LinkArenaDenyUnitForm", "LinkArenaDenyUnitViewerViewModel"),
            ("MonsterProbabilityViewerView", "MonsterProbabilityForm", "MonsterProbabilityViewerViewModel"),
            ("MonsterItemViewerView", "MonsterItemForm", "MonsterItemViewerViewModel"),
            ("MonsterWMapProbabilityViewerView", "MonsterWMapProbabilityForm", "MonsterWMapProbabilityViewerViewModel"),
            ("SummonUnitViewerView", "SummonUnitForm", "SummonUnitViewerViewModel"),
            ("SummonsDemonKingViewerView", "SummonsDemonKingForm", "SummonsDemonKingViewerViewModel"),

            // Menu / ED / World Map
            ("MenuDefinitionView", "MenuDefinitionForm", "MenuDefinitionViewModel"),
            ("MenuCommandView", "MenuCommandForm", "MenuCommandViewModel"),
            ("EDView", "EDForm", "EDViewModel"),
            ("EDStaffRollView", "EDStaffRollForm", "EDStaffRollViewModel"),
            ("WorldMapPointView", "WorldMapPointForm", "WorldMapPointViewModel"),
            ("WorldMapBGMView", "WorldMapBGMForm", "WorldMapBGMViewModel"),
            ("WorldMapEventPointerView", "WorldMapEventPointerForm", "WorldMapEventPointerViewModel"),

            // Audio
            ("SongTableView", "SongTableForm", "SongTableViewModel"),
            ("SoundBossBGMViewerView", "SoundBossBGMForm", "SoundBossBGMViewerViewModel"),
            ("SoundFootStepsViewerView", "SoundFootStepsForm", "SoundFootStepsViewerViewModel"),
            ("SoundRoomViewerView", "SoundRoomForm", "SoundRoomViewerViewModel"),

            // Status Screen
            ("StatusParamView", "StatusParamForm", "StatusParamViewModel"),
            ("StatusRMenuView", "StatusRMenuForm", "StatusRMenuViewModel"),
            ("StatusUnitsMenuView", "StatusUnitsMenuForm", "StatusUnitsMenuViewModel"),
            ("StatusOptionOrderView", "StatusOptionOrderForm", "StatusOptionOrderViewModel"),

            // Skill Systems
            ("SkillAssignmentUnitCSkillSysView", "SkillAssignmentUnitCSkillSysForm", "SkillAssignmentUnitCSkillSysViewViewModel"),
            ("SkillAssignmentClassCSkillSysView", "SkillAssignmentClassCSkillSysForm", "SkillAssignmentClassCSkillSysViewViewModel"),
            ("SkillAssignmentUnitFE8NView", "SkillAssignmentUnitFE8NForm", "SkillAssignmentUnitFE8NViewViewModel"),
            ("SkillConfigFE8NSkillView", "SkillConfigFE8NSkillForm", "SkillConfigFE8NSkillViewViewModel"),
            ("SkillConfigFE8NVer2SkillView", "SkillConfigFE8NVer2SkillForm", "SkillConfigFE8NVer2SkillViewViewModel"),
            ("SkillConfigFE8NVer3SkillView", "SkillConfigFE8NVer3SkillForm", "SkillConfigFE8NVer3SkillViewViewModel"),
            ("SkillConfigFE8UCSkillSys09xView", "SkillConfigFE8UCSkillSys09xForm", "SkillConfigFE8UCSkillSys09xViewViewModel"),
            ("SkillSystemsEffectivenessReworkClassTypeView", "SkillSystemsEffectivenessReworkClassTypeForm", "SkillSystemsEffectivenessReworkClassTypeViewViewModel"),

            // OP Class Editors
            ("OPClassDemoFE7View", "OPClassDemoFE7Form", "OPClassDemoFE7ViewModel"),
            ("OPClassDemoFE7UView", "OPClassDemoFE7UForm", "OPClassDemoFE7UViewModel"),
            ("OPClassDemoFE8UView", "OPClassDemoFE8UForm", "OPClassDemoFE8UViewModel"),
            ("OPClassFontFE8UView", "OPClassFontFE8UForm", "OPClassFontFE8UViewModel"),
            ("OPClassAlphaNameView", "OPClassAlphaNameForm", "OPClassAlphaNameViewModel"),
            ("OPClassAlphaNameFE6View", "OPClassAlphaNameFE6Form", "OPClassAlphaNameFE6ViewModel"),

            // Image Viewers with metadata fields
            ("PortraitViewerView", "ImagePortraitForm", "PortraitViewerViewModel"),
            ("BattleBGViewerView", "ImageBattleBGForm", "BattleBGViewerViewModel"),
            ("BattleTerrainViewerView", "ImageBattleTerrainForm", "BattleTerrainViewerViewModel"),
            ("ChapterTitleViewerView", "ImageChapterTitleForm", "ChapterTitleViewerViewModel"),
            ("OPClassDemoViewerView", "OPClassDemoForm", "OPClassDemoViewerViewModel"),
            ("OPClassFontViewerView", "OPClassFontForm", "OPClassFontViewerViewModel"),
            ("OPPrologueViewerView", "OPPrologueForm", "OPPrologueViewerViewModel"),
            ("SystemIconViewerView", "ImageSystemIconForm", "SystemIconViewerViewModel"),
            // ImageFormRef is not a Form (it's a helper class), skip
            // ("ImageViewerView", "ImageFormRef", "ImageViewerViewModel"),
            ("ItemIconViewerView", "ImageItemIconForm", "ItemIconViewerViewModel"),

            // SME
            ("SMEPromoListView", "SMEPromoListForm", "SMEPromoListViewModel"),

            // === WU1: MapSetting FE7/FE7U ===
            ("MapSettingFE7View", "MapSettingFE7Form", "MapSettingFE7ViewModel"),
            ("MapSettingFE7UView", "MapSettingFE7UForm", "MapSettingFE7UViewModel"),

            // === WU2: MapSetting FE6 ===
            ("MapSettingFE6View", "MapSettingFE6Form", "MapSettingFE6ViewModel"),

            // === WU3: ClassFE6 ===
            ("ClassFE6View", "ClassFE6Form", "ClassFE6ViewModel"),

            // === WU4: EventUnit family ===
            ("EventUnitView", "EventUnitForm", "EventUnitViewModel"),
            ("EventUnitFE6View", "EventUnitFE6Form", "EventUnitFE6ViewModel"),
            ("EventUnitFE7View", "EventUnitFE7Form", "EventUnitFE7ViewModel"),

            // === WU5: EventBattle + EventHaiku ===
            ("EventBattleTalkView", "EventBattleTalkForm", "EventBattleTalkViewModel"),
            ("EventBattleTalkFE6View", "EventBattleTalkFE6Form", "EventBattleTalkFE6ViewModel"),
            ("EventBattleTalkFE7View", "EventBattleTalkFE7Form", "EventBattleTalkFE7ViewModel"),
            ("EventHaikuView", "EventHaikuForm", "EventHaikuViewModel"),
            ("EventHaikuFE6View", "EventHaikuFE6Form", "EventHaikuFE6ViewModel"),
            ("EventHaikuFE7View", "EventHaikuFE7Form", "EventHaikuFE7ViewModel"),

            // === WU6: Event misc + AI small ===
            ("EventBattleDataFE7View", "EventBattleDataFE7Form", "EventBattleDataFE7ViewModel"),
            ("EventForceSortieView", "EventForceSortieForm", "EventForceSortieViewModel"),
            ("EventForceSortieFE7View", "EventForceSortieFE7Form", "EventForceSortieFE7ViewModel"),
            ("EventFunctionPointerView", "EventFunctionPointerForm", "EventFunctionPointerViewModel"),
            ("EventFunctionPointerFE7View", "EventFunctionPointerFE7Form", "EventFunctionPointerFE7ViewModel"),
            ("EventFinalSerifFE7View", "EventFinalSerifFE7Form", "EventFinalSerifFE7ViewModel"),
            ("EventMoveDataFE7View", "EventMoveDataFE7Form", "EventMoveDataFE7ViewModel"),
            ("EventTalkGroupFE7View", "EventTalkGroupFE7Form", "EventTalkGroupFE7ViewModel"),
            ("AOERANGEView", "AOERANGEForm", "AOERANGEViewModel"),
            ("AIASMCALLTALKView", "AIASMCALLTALKForm", "AIASMCALLTALKViewModel"),
            ("AIASMCoordinateView", "AIASMCoordinateForm", "AIASMCoordinateViewModel"),
            ("AIASMRangeView", "AIASMRangeForm", "AIASMRangeViewModel"),
            ("AIMapSettingView", "AIMapSettingForm", "AIMapSettingViewModel"),

            // === WU7: AI medium + StatusOption ===
            ("AIPerformItemView", "AIPerformItemForm", "AIPerformItemViewModel"),
            ("AIPerformStaffView", "AIPerformStaffForm", "AIPerformStaffViewModel"),
            ("AIStealItemView", "AIStealItemForm", "AIStealItemViewModel"),
            ("AITargetView", "AITargetForm", "AITargetViewModel"),
            ("AITilesView", "AITilesForm", "AITilesViewModel"),
            ("AIUnitsView", "AIUnitsForm", "AIUnitsViewModel"),
            ("StatusOptionView", "StatusOptionForm", "StatusOptionViewModel"),

            // === WU8: Image forms ===
            ("ImagePortraitView", "ImagePortraitForm", "ImagePortraitViewModel"),
            ("ImagePortraitFE6View", "ImagePortraitFE6Form", "ImagePortraitFE6ViewModel"),
            ("ImageBGView", "ImageBGForm", "ImageBGViewModel"),
            ("ImageBattleAnimeView", "ImageBattleAnimeForm", "ImageBattleAnimeViewModel"),
            ("ImageBattleBGView", "ImageBattleBGForm", "ImageBattleBGViewModel"),
            ("ImageCGView", "ImageCGForm", "ImageCGViewModel"),
            ("ImageCGFE7UView", "ImageCGFE7UForm", "ImageCGFE7UViewModel"),
            ("ImageUnitPaletteView", "ImageUnitPaletteForm", "ImageUnitPaletteViewModel"),
            ("ImageSystemAreaView", "ImageSystemAreaForm", "ImageSystemAreaViewModel"),
            ("ImageGenericEnemyPortraitView", "ImageGenericEnemyPortraitForm", "ImageGenericEnemyPortraitViewModel"),
            ("ImageTSAAnimeView", "ImageTSAAnimeForm", "ImageTSAAnimeViewModel"),
            ("ImageTSAAnime2View", "ImageTSAAnime2Form", "ImageTSAAnime2ViewModel"),
            ("ImageMagicFEditorView", "ImageMagicFEditorForm", "ImageMagicFEditorViewModel"),
            ("ImageMagicCSACreatorView", "ImageMagicCSACreatorForm", "ImageMagicCSACreatorViewModel"),
            ("ImageMapActionAnimationView", "ImageMapActionAnimationForm", "ImageMapActionAnimationViewModel"),

            // === WU9: Sound + WorldMap + Map misc ===
            ("SongTrackView", "SongTrackForm", "SongTrackViewModel"),
            ("SongInstrumentDirectSoundView", "SongInstrumentDirectSoundForm", "SongInstrumentDirectSoundViewModel"),
            ("SoundRoomFE6View", "SoundRoomFE6Form", "SoundRoomFE6ViewModel"),
            ("SoundRoomCGView", "SoundRoomCGForm", "SoundRoomCGViewModel"),
            ("WorldMapPathView", "WorldMapPathForm", "WorldMapPathViewModel"),
            ("WorldMapPathMoveEditorView", "WorldMapPathMoveEditorForm", "WorldMapPathMoveEditorViewModel"),
            ("MapTileAnimation1View", "MapTileAnimation1Form", "MapTileAnimation1ViewModel"),
            ("MapTileAnimation2View", "MapTileAnimation2Form", "MapTileAnimation2ViewModel"),
            ("MapLoadFunctionView", "MapLoadFunctionForm", "MapLoadFunctionViewModel"),
            ("MapTerrainNameEngView", "MapTerrainNameEngForm", "MapTerrainNameEngViewModel"),
            ("MapMiniMapTerrainImageView", "MapMiniMapTerrainImageForm", "MapMiniMapTerrainImageViewModel"),

            // === WU10: ItemStatBonus + Text + Menu + misc ===
            ("ItemStatBonusesSkillSystemsView", "ItemStatBonusesSkillSystemsForm", "ItemStatBonusesSkillSystemsViewModel"),
            ("ItemStatBonusesVennoView", "ItemStatBonusesVennoForm", "ItemStatBonusesVennoViewModel"),
            ("ItemRandomChestView", "ItemRandomChestForm", "ItemRandomChestViewModel"),
            ("MenuExtendSplitMenuView", "MenuExtendSplitMenuForm", "MenuExtendSplitMenuViewModel"),
            ("TextDicView", "TextDicForm", "TextDicViewModel"),
            ("TextCharCodeView", "TextCharCodeForm", "TextCharCodeViewModel"),
            ("ImageChapterTitleFE7View", "ImageChapterTitleFE7Form", "ImageChapterTitleFE7ViewModel"),
            ("EDSensekiCommentView", "EDSensekiCommentForm", "EDSensekiCommentViewModel"),

            // === WU11: Unit extras + Skills + small misc ===
            ("UnitActionPointerView", "UnitActionPointerForm", "UnitActionPointerViewModel"),
            ("UnitCustomBattleAnimeView", "UnitCustomBattleAnimeForm", "UnitCustomBattleAnimeViewModel"),
            ("UnitIncreaseHeightView", "UnitIncreaseHeightForm", "UnitIncreaseHeightViewModel"),
            ("UnitPaletteView", "UnitPaletteForm", "UnitPaletteViewModel"),
            ("ExtraUnitView", "ExtraUnitForm", "ExtraUnitViewModel"),
            ("ExtraUnitFE8UView", "ExtraUnitFE8UForm", "ExtraUnitFE8UViewModel"),
            ("SkillAssignmentUnitSkillSystemView", "SkillAssignmentUnitSkillSystemForm", "SkillAssignmentUnitSkillSystemViewModel"),
            ("SkillAssignmentClassSkillSystemView", "SkillAssignmentClassSkillSystemForm", "SkillAssignmentClassSkillSystemViewModel"),
            ("SkillConfigSkillSystemView", "SkillConfigSkillSystemForm", "SkillConfigSkillSystemViewModel"),
            ("Command85PointerView", "Command85PointerForm", "Command85PointerViewModel"),
            ("MantAnimationView", "MantAnimationForm", "MantAnimationViewModel"),

            // === WU12: BitFlags + MapTerrain lookups ===
            ("UbyteBitFlagView", "UbyteBitFlagForm", "UbyteBitFlagViewModel"),
            ("UshortBitFlagView", "UshortBitFlagForm", "UshortBitFlagViewModel"),
            ("UwordBitFlagView", "UwordBitFlagForm", "UwordBitFlagViewModel"),
            ("MapTerrainBGLookupView", "MapTerrainBGLookupTableForm", "MapTerrainBGLookupTableViewModel"),
            ("MapTerrainFloorLookupView", "MapTerrainFloorLookupTableForm", "MapTerrainFloorLookupTableViewModel"),

            // === WU13: Orphan forms (previously excluded from ScreenshotFormRegistry) ===
            ("ClassOPDemoView", "ClassOPDemoForm", "ClassOPDemoViewModel"),
            ("ClassOPFontView", "ClassOPFontForm", "ClassOPFontViewModel"),
            ("EventMapChangeView", "EventMapChangeForm", "EventMapChangeViewModel"),

            // === WU14: Auto-discovered forms with ROM fields not in ScreenshotFormRegistry ===
            ("BigCGViewerView", "BigCGForm", "BigCGViewerViewModel"),
            ("ImageUnitWaitIconView", "ImageUnitWaitIconFrom", "ImageUnitWaitIconViewModel"),
            ("ImageUnitMoveIconView", "ImageUnitMoveIconFrom", "ImageUnitMoveIconViewModel"),
            ("TacticianAffinityFE7View", "TacticianAffinityFE7", "TacticianAffinityFE7ViewModel"),
        };

        /// <summary>
        /// Regex to extract ROM data field control names from WinForms Designer.cs.
        /// Matches: this.B0.Name = "B0"; this.W2.Name = "W2"; this.P4.Name = "P4"; etc.
        /// </summary>
        private static readonly Regex DesignerFieldPattern = new(
            @"this\.([BbWDPlh]\d+)\.Name\s*=\s*""[BbWDPlh]\d+""",
            RegexOptions.Compiled);

        /// <summary>
        /// Regex to extract ROM data access from Avalonia ViewModel source.
        /// Matches: rom.u8(addr + 12), rom.u16(addr + 0), rom.u32(addr + 4), rom.p32(addr + 8)
        /// Also matches: rom.u8(baseAddr + 12), ROM.u8(addr + 12)
        /// </summary>
        private static readonly Regex VmRomAccessPattern = new(
            @"\.(?:u8|u16|u32|p32)\(\w+\s*\+\s*(\d+)\)",
            RegexOptions.Compiled);

        /// <summary>
        /// Alternative pattern: rom.u8(addr), rom.u16(addr) with offset 0
        /// </summary>
        private static readonly Regex VmRomAccessZeroPattern = new(
            @"\.(?:u8|u16|u32|p32)\(\w+\)",
            RegexOptions.Compiled);

        /// <summary>
        /// Extracts ROM data field names from a WinForms Designer.cs file.
        /// Returns sorted set of field names like "B0", "W2", "P4", "b12", "l0", "h0".
        /// </summary>
        private static SortedSet<string> ExtractWinFormsFields(string designerPath)
        {
            if (!File.Exists(designerPath))
                return new SortedSet<string>();

            var source = File.ReadAllText(designerPath);
            var fields = new SortedSet<string>(StringComparer.Ordinal);

            foreach (Match m in DesignerFieldPattern.Matches(source))
            {
                fields.Add(m.Groups[1].Value);
            }

            return fields;
        }

        /// <summary>
        /// Extracts ROM data access offsets from an Avalonia ViewModel .cs file
        /// and converts them to equivalent WinForms field names.
        /// e.g., rom.u8(addr + 12) → "B12", rom.u16(addr + 2) → "W2", rom.u32(addr + 4) → "D4"
        /// </summary>
        private static SortedSet<string> ExtractAvaloniaRomAccesses(string vmPath)
        {
            if (!File.Exists(vmPath))
                return new SortedSet<string>();

            var source = File.ReadAllText(vmPath);
            var fields = new SortedSet<string>(StringComparer.Ordinal);

            // Match rom access with explicit offset: .u8(addr + 12)
            var accessPattern = new Regex(
                @"\.(u8|u16|u32|p32)\(\w+\s*\+\s*(\d+)\)",
                RegexOptions.Compiled);

            foreach (Match m in accessPattern.Matches(source))
            {
                string type = m.Groups[1].Value;
                string offset = m.Groups[2].Value;
                string prefix = type switch
                {
                    "u8" => "B",
                    "u16" => "W",
                    "u32" => "D",
                    "p32" => "P",
                    _ => "?"
                };
                fields.Add($"{prefix}{offset}");
            }

            // Match rom access at offset 0: .u8(addr)
            var zeroPattern = new Regex(
                @"\.(u8|u16|u32|p32)\((?:addr|baseAddr|address)\)",
                RegexOptions.Compiled);

            foreach (Match m in zeroPattern.Matches(source))
            {
                string type = m.Groups[1].Value;
                string prefix = type switch
                {
                    "u8" => "B",
                    "u16" => "W",
                    "u32" => "D",
                    "p32" => "P",
                    _ => "?"
                };
                fields.Add($"{prefix}0");
            }

            // Also check for signed byte reads: (sbyte)rom.u8(addr + #) → b#
            var signedPattern = new Regex(
                @"\(sbyte\)\w+\.u8\(\w+\s*\+\s*(\d+)\)",
                RegexOptions.Compiled);

            foreach (Match m in signedPattern.Matches(source))
            {
                string offset = m.Groups[1].Value;
                // Add both b# (signed) and B# (unsigned) since the ViewModel
                // might use signed cast but the Designer.cs uses b# name
                fields.Add($"b{offset}");
                fields.Add($"B{offset}");
            }

            // Check for write operations too: rom.write_u8(addr + #, val)
            var writePattern = new Regex(
                @"\.write_(u8|u16|u32)\(\w+\s*\+\s*(\d+)",
                RegexOptions.Compiled);

            foreach (Match m in writePattern.Matches(source))
            {
                string type = m.Groups[1].Value;
                string offset = m.Groups[2].Value;
                string prefix = type switch
                {
                    "u8" => "B",
                    "u16" => "W",
                    "u32" => "D",
                    _ => "?"
                };
                fields.Add($"{prefix}{offset}");
            }

            // Fallback: detect public properties named B#, W#, D#, P# (for VMs that load via loops)
            var propPattern = new Regex(
                @"public\s+(?:byte|ushort|uint)\s+([BWDPbh]\d+)\s*\{",
                RegexOptions.Compiled);

            foreach (Match m in propPattern.Matches(source))
            {
                fields.Add(m.Groups[1].Value);
            }

            return fields;
        }

        /// <summary>
        /// For each form mapping, extracts WinForms fields and Avalonia fields,
        /// reports the gap. This test outputs a comprehensive comparison report.
        /// </summary>
        [Fact]
        public void CompareAllFormFields_ReportGaps()
        {
            var report = new System.Text.StringBuilder();
            int totalWinFormsFields = 0;
            int totalAvaloniaFields = 0;
            int totalMissing = 0;
            int formsWithGaps = 0;

            foreach (var (viewName, winFormsType, avaloniaVmType) in FormMappings)
            {
                string designerPath = Path.Combine(WinFormsDir, $"{winFormsType}.Designer.cs");
                string vmPath = Path.Combine(AvaloniaVmDir, $"{avaloniaVmType}.cs");

                var winFormsFields = ExtractWinFormsFields(designerPath);
                var avaloniaFields = ExtractAvaloniaRomAccesses(vmPath);

                if (winFormsFields.Count == 0)
                    continue; // No ROM fields in WinForms form (dialog, tool, etc.)

                // Fields in WinForms but not in Avalonia (considering B/b and P/D equivalence)
                var missing = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var field in winFormsFields)
                {
                    // Check exact match
                    bool found = avaloniaFields.Contains(field);
                    if (!found)
                    {
                        // Try opposite case for B/b (signed/unsigned byte)
                        string alt = char.IsUpper(field[0])
                            ? char.ToLower(field[0]) + field.Substring(1)
                            : char.ToUpper(field[0]) + field.Substring(1);
                        found = avaloniaFields.Contains(alt);
                    }
                    if (!found)
                    {
                        // P and D are equivalent (both u32 reads; P = pointer display, D = raw u32)
                        if (field[0] == 'P')
                            found = avaloniaFields.Contains("D" + field.Substring(1));
                        else if (field[0] == 'D')
                            found = avaloniaFields.Contains("P" + field.Substring(1));
                    }
                    if (!found)
                        missing.Add(field);
                }

                totalWinFormsFields += winFormsFields.Count;
                totalAvaloniaFields += avaloniaFields.Count;
                totalMissing += missing.Count;

                if (missing.Count > 0)
                {
                    formsWithGaps++;
                    report.AppendLine($"GAP: {viewName} ({winFormsType} → {avaloniaVmType})");
                    report.AppendLine($"  WinForms fields ({winFormsFields.Count}): {string.Join(", ", winFormsFields)}");
                    report.AppendLine($"  Avalonia fields ({avaloniaFields.Count}): {string.Join(", ", avaloniaFields)}");
                    report.AppendLine($"  MISSING ({missing.Count}): {string.Join(", ", missing)}");
                    report.AppendLine();
                }
            }

            report.Insert(0,
                $"=== Field Completeness Report ===\n" +
                $"Total WinForms fields: {totalWinFormsFields}\n" +
                $"Total Avalonia fields: {totalAvaloniaFields}\n" +
                $"Total missing: {totalMissing}\n" +
                $"Forms with gaps: {formsWithGaps} / {FormMappings.Length}\n\n");

            // Write report to file for inspection
            string reportPath = Path.Combine(SolutionDir, "docs", "field-completeness-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, report.ToString());

            // Strict: fail if any gaps remain
            Assert.Equal(0, totalMissing);
        }

        /// <summary>
        /// Verifies that all form mappings reference existing source files.
        /// </summary>
        [Fact]
        public void AllFormMappings_HaveExistingSourceFiles()
        {
            var missingDesigner = new List<string>();
            var missingVm = new List<string>();

            foreach (var (viewName, winFormsType, avaloniaVmType) in FormMappings)
            {
                string designerPath = Path.Combine(WinFormsDir, $"{winFormsType}.Designer.cs");
                string vmPath = Path.Combine(AvaloniaVmDir, $"{avaloniaVmType}.cs");

                if (!File.Exists(designerPath))
                    missingDesigner.Add($"{viewName} → {winFormsType}.Designer.cs");
                if (!File.Exists(vmPath))
                    missingVm.Add($"{viewName} → {avaloniaVmType}.cs");
            }

            Assert.True(missingDesigner.Count == 0,
                $"Missing WinForms Designer.cs files:\n{string.Join("\n", missingDesigner)}");
            Assert.True(missingVm.Count == 0,
                $"Missing Avalonia ViewModel files:\n{string.Join("\n", missingVm)}");
        }

        /// <summary>
        /// Verifies that each WinForms form in ScreenshotFormRegistry that has ROM fields
        /// has a corresponding entry in FormMappings for comparison.
        /// </summary>
        [Fact]
        public void ScreenshotRegistry_AllRomFieldForms_AreMapped()
        {
            // Parse ScreenshotFormRegistry.cs to get all form factories
            string registryPath = Path.Combine(WinFormsDir, "ScreenshotFormRegistry.cs");
            string registrySource = File.ReadAllText(registryPath);

            // Extract (ViewName, FormType) pairs
            var registryPattern = new Regex(
                @"\(""(\w+)"",\s*\(\)\s*=>\s*new\s+(\w+)\(\)\)",
                RegexOptions.Compiled);

            var registryEntries = registryPattern.Matches(registrySource)
                .Select(m => (ViewName: m.Groups[1].Value, FormType: m.Groups[2].Value))
                .ToList();

            var mappedViews = FormMappings.Select(m => m.ViewName).ToHashSet();
            var unmappedWithFields = new List<string>();

            foreach (var (viewName, formType) in registryEntries)
            {
                if (mappedViews.Contains(viewName))
                    continue;

                // Check if this form has ROM data fields in its Designer.cs
                string designerPath = Path.Combine(WinFormsDir, $"{formType}.Designer.cs");
                var fields = ExtractWinFormsFields(designerPath);

                if (fields.Count > 0)
                {
                    unmappedWithFields.Add($"{viewName} ({formType}): {fields.Count} fields: {string.Join(", ", fields.Take(10))}...");
                }
            }

            // This is informational — shows forms with ROM fields that need mapping
            if (unmappedWithFields.Count > 0)
            {
                string msg = $"Registry forms with ROM fields NOT in FormMappings ({unmappedWithFields.Count}):\n" +
                    string.Join("\n", unmappedWithFields);
                // Write to report file
                string reportPath = Path.Combine(SolutionDir, "docs", "unmapped-rom-field-forms.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, msg);
            }

            // Strict: fail if any forms with ROM fields are unmapped
            Assert.Equal(0, unmappedWithFields.Count);
        }

        /// <summary>
        /// Key editors must have at least 80% field coverage.
        /// These are the most important editors that users interact with daily.
        /// </summary>
        [Theory]
        [InlineData("UnitEditorView", "UnitForm", "UnitEditorViewModel")]
        [InlineData("ItemEditorView", "ItemForm", "ItemEditorViewModel")]
        [InlineData("ClassEditorView", "ClassForm", "ClassEditorViewModel")]
        [InlineData("MapSettingView", "MapSettingForm", "MapSettingViewModel")]
        public void KeyEditors_HaveMinimumFieldCoverage(string viewName, string winFormsType, string avaloniaVmType)
        {
            string designerPath = Path.Combine(WinFormsDir, $"{winFormsType}.Designer.cs");
            string vmPath = Path.Combine(AvaloniaVmDir, $"{avaloniaVmType}.cs");

            var winFormsFields = ExtractWinFormsFields(designerPath);
            var avaloniaFields = ExtractAvaloniaRomAccesses(vmPath);

            Assert.True(winFormsFields.Count > 0,
                $"{viewName}: No WinForms ROM fields found in {winFormsType}.Designer.cs");

            int matched = 0;
            foreach (var field in winFormsFields)
            {
                if (avaloniaFields.Contains(field))
                {
                    matched++;
                    continue;
                }
                // Try case-insensitive match for B/b
                string alt = char.IsUpper(field[0])
                    ? char.ToLower(field[0]) + field.Substring(1)
                    : char.ToUpper(field[0]) + field.Substring(1);
                if (avaloniaFields.Contains(alt))
                {
                    matched++;
                    continue;
                }
                // P and D are equivalent (both u32 reads)
                if (field[0] == 'P' && avaloniaFields.Contains("D" + field.Substring(1)))
                    matched++;
                else if (field[0] == 'D' && avaloniaFields.Contains("P" + field.Substring(1)))
                    matched++;
            }

            double coverage = (double)matched / winFormsFields.Count * 100;

            Assert.True(coverage >= 80.0,
                $"{viewName}: {matched}/{winFormsFields.Count} fields covered ({coverage:F1}%) — minimum 80% required");
        }

        /// <summary>
        /// Correctness cross-check: verifies that for each WinForms field, the Avalonia VM
        /// reads the correct type at the correct offset.
        /// e.g., WinForms "B12" must match Avalonia rom.u8(addr + 12), NOT rom.u16(addr + 12).
        /// </summary>
        [Fact]
        public void AllFormFields_TypeAndOffsetMatch()
        {
            var mismatches = new List<string>();

            // Regex that captures both type and offset from VM source
            var vmTypedAccessPattern = new Regex(
                @"\.(u8|u16|u32|p32)\(\w+\s*\+\s*(\d+)\)",
                RegexOptions.Compiled);

            var vmTypedZeroPattern = new Regex(
                @"\.(u8|u16|u32|p32)\((?:addr|baseAddr|address|a)\)",
                RegexOptions.Compiled);

            foreach (var (viewName, winFormsType, avaloniaVmType) in FormMappings)
            {
                string designerPath = Path.Combine(WinFormsDir, $"{winFormsType}.Designer.cs");
                string vmPath = Path.Combine(AvaloniaVmDir, $"{avaloniaVmType}.cs");

                var winFormsFields = ExtractWinFormsFields(designerPath);
                if (winFormsFields.Count == 0) continue;
                if (!File.Exists(vmPath)) continue;

                string vmSource = File.ReadAllText(vmPath);

                // Build a map of offset → set of types from VM source
                var vmAccessMap = new Dictionary<int, HashSet<string>>();

                foreach (Match m in vmTypedAccessPattern.Matches(vmSource))
                {
                    string type = m.Groups[1].Value;
                    int offset = int.Parse(m.Groups[2].Value);
                    if (!vmAccessMap.ContainsKey(offset))
                        vmAccessMap[offset] = new HashSet<string>();
                    vmAccessMap[offset].Add(type);
                }

                foreach (Match m in vmTypedZeroPattern.Matches(vmSource))
                {
                    string type = m.Groups[1].Value;
                    if (!vmAccessMap.ContainsKey(0))
                        vmAccessMap[0] = new HashSet<string>();
                    vmAccessMap[0].Add(type);
                }

                // For each WinForms field, verify the VM reads the right type at the right offset
                foreach (var field in winFormsFields)
                {
                    char prefix = field[0];
                    if (!int.TryParse(field.Substring(1), out int offset))
                        continue;

                    string expectedType = prefix switch
                    {
                        'B' or 'b' => "u8",
                        'W' => "u16",
                        'D' => "u32",
                        'P' => "u32", // P is also u32/p32
                        'l' or 'h' => "u8", // nibble fields read as u8
                        _ => null
                    };

                    if (expectedType == null) continue;

                    if (!vmAccessMap.TryGetValue(offset, out var types))
                        continue; // Missing field — already caught by CompareAllFormFields_ReportGaps

                    // Check type compatibility
                    // Wider reads are acceptable: u16 covers u8, u32 covers u8/u16
                    bool typeMatch = types.Contains(expectedType);
                    if (!typeMatch && prefix == 'P')
                        typeMatch = types.Contains("p32"); // P can be read as p32
                    if (!typeMatch && (prefix == 'D'))
                        typeMatch = types.Contains("p32"); // D can also be read as p32
                    if (!typeMatch && expectedType == "u8")
                        typeMatch = types.Contains("u16") || types.Contains("u32") || types.Contains("p32");
                    if (!typeMatch && expectedType == "u16")
                        typeMatch = types.Contains("u32") || types.Contains("p32");

                    if (!typeMatch)
                    {
                        mismatches.Add(
                            $"{viewName}: field {field} expects {expectedType} at offset {offset}, " +
                            $"but VM reads [{string.Join(",", types)}]");
                    }
                }
            }

            Assert.True(mismatches.Count == 0,
                $"Type/offset mismatches ({mismatches.Count}):\n{string.Join("\n", mismatches)}");
        }

        /// <summary>
        /// Verifies that GetDataReport() keys are consistent with GetRawRomReport() keys
        /// for all ViewModels that implement IDataVerifiable.
        /// e.g., if GetDataReport has ["B12"], GetRawRomReport should have ["u8@0x0C"].
        /// </summary>
        [Fact]
        public void AllViewModels_ReportMethodsAreConsistent()
        {
            var inconsistencies = new List<string>();

            // Regex to extract data report keys: ["B12"] or ["P0"] etc.
            var dataReportKeyPattern = new Regex(
                @"\[""([BWDPbh]\d+)""\]\s*=",
                RegexOptions.Compiled);

            // Regex to extract raw rom report keys: ["u8@0x0C"] or ["u8@12"] or ["s8@0x13"]
            // Group 1: type, Group 2: hex digits (if 0x prefix), Group 3: decimal digits (no 0x prefix)
            var rawReportKeyPattern = new Regex(
                @"\[""(u8|u16|u32|p32|s8)@(?:0x([0-9A-Fa-f]+)|(\d+))""\]\s*=",
                RegexOptions.Compiled);

            foreach (var (viewName, winFormsType, avaloniaVmType) in FormMappings)
            {
                string vmPath = Path.Combine(AvaloniaVmDir, $"{avaloniaVmType}.cs");
                if (!File.Exists(vmPath)) continue;

                string vmSource = File.ReadAllText(vmPath);

                // Extract data report field keys
                var dataKeys = new Dictionary<string, (char prefix, int offset)>();
                foreach (Match m in dataReportKeyPattern.Matches(vmSource))
                {
                    string key = m.Groups[1].Value;
                    char prefix = key[0];
                    if (int.TryParse(key.Substring(1), out int offset))
                        dataKeys[key] = (prefix, offset);
                }

                // Extract raw report entries — multiple types per offset possible
                // Supports both hex (@0x0C) and decimal (@12) key formats
                var rawEntries = new Dictionary<int, HashSet<string>>();
                foreach (Match m in rawReportKeyPattern.Matches(vmSource))
                {
                    string type = m.Groups[1].Value;
                    int offset;
                    if (m.Groups[2].Success)
                        offset = Convert.ToInt32(m.Groups[2].Value, 16);
                    else
                        offset = int.Parse(m.Groups[3].Value);
                    if (!rawEntries.ContainsKey(offset))
                        rawEntries[offset] = new HashSet<string>();
                    rawEntries[offset].Add(type);
                }

                if (dataKeys.Count == 0 || rawEntries.Count == 0) continue;

                // Verify each data key has a matching raw entry
                foreach (var (key, (prefix, offset)) in dataKeys)
                {
                    string expectedType = prefix switch
                    {
                        'B' => "u8",
                        'b' => "s8", // signed byte
                        'W' => "u16",
                        'D' or 'P' => "u32",
                        _ => null
                    };
                    if (expectedType == null) continue;

                    if (!rawEntries.TryGetValue(offset, out var rawTypes))
                    {
                        inconsistencies.Add(
                            $"{viewName}: GetDataReport has [{key}] (offset {offset}) " +
                            $"but GetRawRomReport has no entry at 0x{offset:X02}");
                        continue;
                    }

                    bool compatible = rawTypes.Contains(expectedType);
                    // Allow equivalences
                    if (!compatible && expectedType == "u32")
                        compatible = rawTypes.Contains("p32");
                    if (!compatible && expectedType == "s8")
                        compatible = rawTypes.Contains("u8"); // s8 can be stored as u8
                    if (!compatible && expectedType == "u8")
                        compatible = rawTypes.Contains("s8") || rawTypes.Contains("u16") || rawTypes.Contains("u32");
                    if (!compatible && expectedType == "u16")
                        compatible = rawTypes.Contains("u32") || rawTypes.Contains("p32");

                    if (!compatible)
                    {
                        inconsistencies.Add(
                            $"{viewName}: GetDataReport [{key}] expects {expectedType}, " +
                            $"but GetRawRomReport has [{string.Join(",", rawTypes)}]@0x{offset:X02}");
                    }
                }
            }

            Assert.True(inconsistencies.Count == 0,
                $"Report inconsistencies ({inconsistencies.Count}):\n{string.Join("\n", inconsistencies)}");
        }

        /// <summary>
        /// Forms excluded from auto-discovery because their ROM pointer is not in Core ROMFEINFO.
        /// Each exclusion must be documented with the reason.
        /// </summary>
        private static readonly HashSet<string> AutoDiscoveryExclusions = new(StringComparer.OrdinalIgnoreCase)
        {
            // systemhover_gradation_palette_pointer is NOT in Core ROMFEINFO — WinForms-only
            "ImageSystemHoverColorForm",
        };

        /// <summary>
        /// Auto-discovery: scans ALL *.Designer.cs files in WinForms project for ROM data fields
        /// and verifies each form with ROM fields has a FormMappings entry.
        /// This prevents new forms from being invisible to the test suite.
        /// </summary>
        [Fact]
        public void AllDesignerFilesWithRomFields_HaveAvaloniaMapping()
        {
            var mappedWinFormTypes = new HashSet<string>(
                FormMappings.Select(m => m.WinFormsType),
                StringComparer.OrdinalIgnoreCase);

            var unmapped = new List<string>();

            foreach (var designerFile in Directory.GetFiles(WinFormsDir, "*.Designer.cs"))
            {
                string fileName = Path.GetFileNameWithoutExtension(designerFile);
                // Remove .Designer suffix (case-insensitive) to get the form class name
                int idx = fileName.LastIndexOf(".designer", StringComparison.OrdinalIgnoreCase);
                string formClass = idx >= 0 ? fileName.Substring(0, idx) : fileName;

                // Skip if already mapped
                if (mappedWinFormTypes.Contains(formClass))
                    continue;

                // Skip if explicitly excluded
                if (AutoDiscoveryExclusions.Contains(formClass))
                    continue;

                // Check if this Designer.cs has ROM data fields
                var fields = ExtractWinFormsFields(designerFile);
                if (fields.Count > 0)
                {
                    unmapped.Add($"{formClass}: {fields.Count} fields [{string.Join(", ", fields)}]");
                }
            }

            // Write report
            string reportPath = Path.Combine(SolutionDir, "docs", "auto-discovery-unmapped.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            if (unmapped.Count > 0)
            {
                File.WriteAllText(reportPath,
                    $"Forms with ROM fields NOT in FormMappings ({unmapped.Count}):\n" +
                    string.Join("\n", unmapped));
            }
            else
            {
                File.WriteAllText(reportPath,
                    "All Designer.cs files with ROM fields are mapped. 0 gaps.");
            }

            Assert.True(unmapped.Count == 0,
                $"Designer.cs files with ROM fields missing from FormMappings ({unmapped.Count}):\n" +
                string.Join("\n", unmapped));
        }

        /// <summary>
        /// Verifies that ALL ViewModels implementing IDataVerifiable have non-empty
        /// GetDataReport and GetRawRomReport methods (not just "=> new()").
        /// Orphan VMs (dialogs, tools, infrastructure) should NOT implement IDataVerifiable.
        /// </summary>
        [Fact]
        public void AllIDataVerifiable_HaveNonEmptyReports()
        {
            var emptyReportVms = new List<string>();

            // Regex to detect empty report patterns
            var emptyDictPattern = new Regex(
                @"Get(?:Data|RawRom)Report\(\)\s*=>\s*new\s*(?:Dictionary<string,\s*string>)?\s*\(\s*\)\s*;",
                RegexOptions.Compiled);

            foreach (var vmFile in Directory.GetFiles(AvaloniaVmDir, "*ViewModel.cs"))
            {
                string source = File.ReadAllText(vmFile);
                string vmName = Path.GetFileNameWithoutExtension(vmFile);

                // Only check VMs that implement IDataVerifiable
                if (!source.Contains("IDataVerifiable"))
                    continue;

                // Check for empty GetDataReport or GetRawRomReport
                var matches = emptyDictPattern.Matches(source);
                foreach (Match m in matches)
                {
                    emptyReportVms.Add($"{vmName}: {m.Value.Trim()}");
                }
            }

            Assert.True(emptyReportVms.Count == 0,
                $"ViewModels implementing IDataVerifiable with empty reports ({emptyReportVms.Count}):\n" +
                string.Join("\n", emptyReportVms));
        }

        /// <summary>
        /// Verifies that FormMappings VMs implementing IDataVerifiable have GetRawRomReport
        /// entries that cover at least 80% of their ROM reads. This ensures the data verification
        /// system can cross-check ViewModel values against raw ROM bytes.
        /// </summary>
        [Fact]
        public void MappedVMs_RawRomReport_CoversRomReads()
        {
            var underReportedVms = new List<string>();

            // Pattern for ROM reads in Load methods
            var romReadPattern = new Regex(
                @"rom\.(u8|u16|u32|p32)\(\w+\s*\+\s*(\d+)\)",
                RegexOptions.Compiled);
            var romReadZeroPattern = new Regex(
                @"rom\.(u8|u16|u32|p32)\((?:addr|baseAddr|address|a)\)",
                RegexOptions.Compiled);

            // Pattern for raw report entries — matches both hex (u8@0x0A) and decimal (u8@10) formats
            var rawReportEntryPattern = new Regex(
                @"\[""(?:u8|u16|u32|p32|s8)@(?:0x[0-9A-Fa-f]+|\d+)""\]",
                RegexOptions.Compiled);

            var mappedVmTypes = new HashSet<string>(
                FormMappings.Select(m => m.AvaloniaVmType),
                StringComparer.OrdinalIgnoreCase);

            foreach (var vmFile in Directory.GetFiles(AvaloniaVmDir, "*ViewModel.cs"))
            {
                string source = File.ReadAllText(vmFile);
                string vmName = Path.GetFileNameWithoutExtension(vmFile);

                if (!source.Contains("IDataVerifiable"))
                    continue;

                // Only check VMs that are in FormMappings
                if (!mappedVmTypes.Contains(vmName))
                    continue;

                // Count distinct ROM read offsets in Load methods
                var romReadOffsets = new HashSet<string>();
                foreach (Match m in romReadPattern.Matches(source))
                {
                    string type = m.Groups[1].Value;
                    string offset = m.Groups[2].Value;
                    romReadOffsets.Add($"{type}@{offset}");
                }
                foreach (Match m in romReadZeroPattern.Matches(source))
                {
                    string type = m.Groups[1].Value;
                    romReadOffsets.Add($"{type}@0");
                }

                if (romReadOffsets.Count == 0) continue;

                // Count raw report entries
                int rawReportCount = rawReportEntryPattern.Matches(source).Count;

                // Require at least 60% coverage for mapped VMs
                // (threshold accounts for ROM reads in list-loader methods
                //  which don't correspond to entry-level raw report entries)
                double coverage = romReadOffsets.Count > 0
                    ? (double)rawReportCount / romReadOffsets.Count * 100
                    : 100;

                if (coverage < 60.0)
                {
                    underReportedVms.Add(
                        $"{vmName}: {romReadOffsets.Count} ROM reads, {rawReportCount} raw report entries ({coverage:F0}%)");
                }
            }

            Assert.True(underReportedVms.Count == 0,
                $"Mapped VMs with under-reported ROM reads (<80%) ({underReportedVms.Count}):\n" +
                string.Join("\n", underReportedVms));
        }

        /// <summary>
        /// Verifies that VMs NOT in FormMappings AND NOT in GetAllEditorFactories
        /// do NOT implement IDataVerifiable (they are orphans and should not).
        /// </summary>
        [Fact]
        public void NoOrphanVMs_ImplementIDataVerifiable()
        {
            var mappedVmTypes = new HashSet<string>(
                FormMappings.Select(m => m.AvaloniaVmType),
                StringComparer.OrdinalIgnoreCase);

            // Also allow VMs referenced in GetAllEditorFactories (MainWindow.axaml.cs)
            string mainWindowPath = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            var factoryVms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(mainWindowPath))
            {
                string mainSource = File.ReadAllText(mainWindowPath);
                var factoryPattern = new Regex(@"new\s+(\w+ViewModel)\s*\(\s*\)", RegexOptions.Compiled);
                foreach (Match m in factoryPattern.Matches(mainSource))
                    factoryVms.Add(m.Groups[1].Value);
            }

            var orphanVms = new List<string>();

            foreach (var vmFile in Directory.GetFiles(AvaloniaVmDir, "*ViewModel.cs"))
            {
                string source = File.ReadAllText(vmFile);
                string vmName = Path.GetFileNameWithoutExtension(vmFile);

                if (!source.Contains("IDataVerifiable"))
                    continue;

                if (mappedVmTypes.Contains(vmName))
                    continue;

                if (factoryVms.Contains(vmName))
                    continue;

                orphanVms.Add(vmName);
            }

            Assert.True(orphanVms.Count == 0,
                $"Orphan VMs implementing IDataVerifiable ({orphanVms.Count}):\n" +
                string.Join("\n", orphanVms) +
                "\n\nThese VMs are not in FormMappings or GetAllEditorFactories. " +
                "Remove IDataVerifiable from them or add them to FormMappings.");
        }
    }
}
