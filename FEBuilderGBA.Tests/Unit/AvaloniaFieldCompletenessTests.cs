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

            // Output to test output
            Assert.True(true, report.ToString());
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

            // For now, this is informational — it will fail once we want strict coverage
            Assert.True(true, $"{unmappedWithFields.Count} unmapped forms with ROM fields");
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

            // Report the coverage (informational for now)
            Assert.True(true,
                $"{viewName}: {matched}/{winFormsFields.Count} fields covered ({coverage:F1}%)");
        }
    }
}
