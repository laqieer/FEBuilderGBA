// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for ClassFE6View (#388).
//
// Mirror of ClassEditorParityTests.cs (#406) for the FE6-specific class
// editor. Asserts:
//   - HardCoding warning hyperlink hidden-by-default + flips visible when
//     IAsmMapCache.IsHardCodeClass returns true for the selected class id.
//   - Address-bar infra: ReadStartAddress / ReadCount / Reload / Size /
//     SelectedAddress prefix labels present.
//   - Export/Import Options panel (8 checkboxes + 4 buttons) backed by the
//     new ClassFE6CsvManager (FE6 weapon-rank offsets B40-B47).
//   - D68 / Unknown textbox present (FE6 has a u32 at offset 68).
//   - No CC Branch button (FE6 has no cc_branch table).
//   - Density verdict on ClassFE6Form moves toward Verdict.Low.
//   - L10n: zero Untranslated literals for ClassFE6View.axaml in ja+zh.
//   - Navigation manifest entries (BattleAnime, MoveCost FE6, terrain
//     pointers, HardCodingPatch, Text/Portrait).
//   - ClassFE6CsvManager behavioral tests (offset map, roundtrip, parse
//     errors, UID routing, comma-decimal growths, quoted fields).
//   - MoveCostFE6View.NavigateToWithCostType selects the matching cost type
//     for each FE6 terrain pointer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ClassFE6ParityTests
    {
        static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is T candidate)
                {
                    var aid = AutomationProperties.GetAutomationId(candidate);
                    if (aid == automationId) return candidate;
                }
            }
            return null;
        }

        // ---- WU1: HardCoding warning ----

        [AvaloniaFact]
        public void View_Hosts_HardCodingWarning_HiddenByDefault()
        {
            var view = new ClassFE6View();
            var lbl = FindByAutomationId<TextBlock>(view, "ClassFE6_HardCoding_Warning_Label");
            Assert.NotNull(lbl);
            Assert.False(lbl!.IsVisible);
        }

        // ---- WU2: Address-bar infrastructure ----

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new ClassFE6View();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassFE6_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassFE6_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassFE6_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassFE6_Size_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassFE6_SelectedAddressPrefix_Label"));
        }

        // ---- WU3: Export/Import options panel (8 checkboxes + 4 buttons) ----

        [AvaloniaFact]
        public void View_Hosts_ExportImportOptions_AllEightCheckboxes()
        {
            var view = new ClassFE6View();
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_UseClipboard_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeUID_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeHeader_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeName_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeBaseStats_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeGrowths_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeWepLevel_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassFE6_GrowthsAsDecimal_Check"));
        }

        [AvaloniaFact]
        public void View_Hosts_FourExportImportButtons()
        {
            var view = new ClassFE6View();
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassFE6_ExportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassFE6_ExportSelected_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassFE6_ImportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassFE6_ImportSelected_Button"));
        }

        [AvaloniaFact]
        public void ExportImportOptions_DefaultsMatchWF()
        {
            // WF defaults: UseClipboard=false, all 7 others=true.
            var view = new ClassFE6View();
            Assert.False(FindByAutomationId<CheckBox>(view, "ClassFE6_UseClipboard_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeHeader_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeUID_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeName_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeBaseStats_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeGrowths_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_IncludeWepLevel_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassFE6_GrowthsAsDecimal_Check")!.IsChecked);
        }

        // ---- WU3: D68 / Unknown control ----

        [AvaloniaFact]
        public void View_Hosts_UnknownD68_Input()
        {
            var view = new ClassFE6View();
            Assert.NotNull(FindByAutomationId<TextBox>(view, "ClassFE6_D68_Input"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassFE6_UnknownD68Label_Label"));
        }

        // ---- WU1: No CC Branch button (FE6 has no cc_branch table) ----

        [AvaloniaFact]
        public void View_Does_Not_Host_CCBranch_Button()
        {
            var view = new ClassFE6View();
            Assert.Null(FindByAutomationId<Button>(view, "ClassFE6_JumpToCCBranch_Button"));
        }

        // ---- WU1: Weapon-rank inputs use FE6 layout (B40-B47, not B44-B51) ----

        [AvaloniaFact]
        public void View_Hosts_WeaponRankInputs_FE6_B40_to_B47()
        {
            var view = new ClassFE6View();
            // FE6 weapon-rank slots use AutomationIds ClassFE6_B40_Input..ClassFE6_B47_Input.
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B40_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B41_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B42_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B43_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B44_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B45_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B46_Input"));
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B47_Input"));
            // FE6 must NOT host B48-B51 weapon slots (those are P48 pointer bytes).
            Assert.Null(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B48_Input"));
            Assert.Null(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B49_Input"));
            Assert.Null(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B50_Input"));
            Assert.Null(FindByAutomationId<NumericUpDown>(view, "ClassFE6_B51_Input"));
        }

        // ---- HardCoding warning visibility-with-cache ----

        class TogglingClassAsmMapCache : IAsmMapCache
        {
            readonly HashSet<uint> _hardcodedClasses;
            public TogglingClassAsmMapCache(params uint[] hardcoded) { _hardcodedClasses = new(hardcoded); }
            public void ClearCache() { }
            public bool IsHardCodeUnit(uint unitId) => false;
            public bool IsHardCodeClass(uint classId) => _hardcodedClasses.Contains(classId);
        }

        [AvaloniaFact]
        public void HardCodingWarning_FlipsVisible_WhenCacheSaysSo()
        {
            var prevCache = CoreState.AsmMapFileAsmCache;
            try
            {
                CoreState.AsmMapFileAsmCache = new TogglingClassAsmMapCache(0u);
                var view = new ClassFE6View();
                var lbl = FindByAutomationId<TextBlock>(view, "ClassFE6_HardCoding_Warning_Label");
                Assert.NotNull(lbl);

                Assert.False(lbl!.IsVisible);

                var list = FindByAutomationId<AddressListControl>(view, "ClassFE6_Entry_List");
                Assert.NotNull(list);
                list!.SetItems(new List<AddrResult>
                {
                    new AddrResult(0x100u, "00 Class0", 0),
                    new AddrResult(0x154u, "01 Class1", 1),
                });

                var refresh = typeof(ClassFE6View).GetMethod(
                    "RefreshHardCodingWarning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refresh);

                // Class 0 (cache says hardcoded): visible.
                list.SelectByIndex(0);
                refresh!.Invoke(view, null);
                Assert.True(lbl.IsVisible, "Label should be visible: cache says class-0 IS hardcoded.");

                // Class 1 (not hardcoded): hidden.
                list.SelectByIndex(1);
                refresh!.Invoke(view, null);
                Assert.False(lbl.IsVisible, "Label should be hidden: cache says class-1 is NOT hardcoded.");
            }
            finally
            {
                CoreState.AsmMapFileAsmCache = prevCache;
            }
        }

        // ---- Navigation manifest ----

        [Fact]
        public void NavigationManifest_IncludesAllExpectedTargets()
        {
            var vm = new ClassFE6ViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.NotNull(targets);
            Assert.Equal(9, targets.Count);

            Assert.Contains(targets, t => t.CommandName == "JumpToBattleAnime" && t.TargetViewType == typeof(ImageBattleAnimeView));
            Assert.Contains(targets, t => t.CommandName == "JumpToMoveCost_FE6" && t.TargetViewType == typeof(MoveCostFE6View));
            Assert.Contains(targets, t => t.CommandName == "JumpToTerrainAvoid_FE6" && t.TargetViewType == typeof(MoveCostFE6View));
            Assert.Contains(targets, t => t.CommandName == "JumpToTerrainDef_FE6" && t.TargetViewType == typeof(MoveCostFE6View));
            Assert.Contains(targets, t => t.CommandName == "JumpToTerrainRes_FE6" && t.TargetViewType == typeof(MoveCostFE6View));
            Assert.Contains(targets, t => t.CommandName == "JumpToHardCodingPatch" && t.TargetViewType == typeof(PatchManagerView));
            Assert.Contains(targets, t => t.CommandName == "JumpToNameText" && t.TargetViewType == typeof(TextViewerView));
            Assert.Contains(targets, t => t.CommandName == "JumpToDescText" && t.TargetViewType == typeof(TextViewerView));
            Assert.Contains(targets, t => t.CommandName == "JumpToPortrait" && t.TargetViewType == typeof(PortraitViewerView));

            // Sanity: NO CC Branch entry (FE8-only feature).
            Assert.DoesNotContain(targets, t => t.CommandName == "JumpToCCBranch_FE8");
        }

        // ---- WU6: density verdict ----

        [Fact]
        public void DensityVerdict_ClassFE6Form_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "ClassFE6Form");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            if (row!.WfControlCount == 0) return;
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"ClassFE6Form density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        // ---- WU6: l10n coverage (ja+zh only — ko intentionally out-of-scope) ----

        [Fact]
        public void L10nCoverage_ClassFE6View_HasNoUntranslated()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches the project's actual l10n gate — ko.txt
            // does not exist in this repo).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("ClassFE6View.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "ClassFE6View.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
        }

        // =================================================================
        // ---- ClassFE6CsvManager behavioral tests (WU3 — pure logic, no ROM) ----
        // =================================================================

        /// <summary>
        /// Construct an in-memory ROM with a deterministic FE6 class-table
        /// layout. Each class row is 72 bytes (FE6 class data size). The CSV
        /// manager reads via direct byte access (rom.u8 / write_u8).
        /// </summary>
        static (ROM rom, uint baseAddr, uint dataSize) MakeStubRom(int classCount = 2)
        {
            const uint baseAddr = 0x100;
            const uint dataSize = 72; // FE6 class data size.
            byte[] data = new byte[0x10000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            // Fill each class row with distinct sentinel bytes per offset.
            for (int u = 0; u < classCount; u++)
            {
                uint addr = baseAddr + (uint)(u * dataSize);
                for (uint o = 0; o < dataSize; o++)
                {
                    data[addr + o] = (byte)((u * 100 + (int)o) % 128);
                }
                // u16 at offset 0 = class name-id.
                data[addr + 0] = (byte)(u + 1);
                data[addr + 1] = 0;
            }
            return (rom, baseAddr, dataSize);
        }

        [Fact]
        public void Export_NoOptions_ProducesMinimalRow()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(2);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false,
                includeUID: false,
                includeHeader: false,
                includeName: false,
                includeBaseStats: false,
                includeGrowths: false,
                includeWepLevel: false,
                growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr, baseAddr + dataSize });
            Assert.Equal("\n\n", csv);
        }

        [Fact]
        public void Export_IncludeHeader_PrependsColumnHeader()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: true,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] lines = csv.Split('\n');
            Assert.True(lines.Length >= 2);
            Assert.Contains("HP", lines[0]);
            Assert.Contains("STR", lines[0]);
            Assert.Contains("DEF", lines[0]);
            Assert.Contains("CON", lines[0]);
        }

        [Fact]
        public void Export_OutputIsCsvNotTsv()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Contains(",", csv);
            Assert.DoesNotContain("\t", csv);
        }

        [Fact]
        public void Export_IncludeUID_AddsUidColumn()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(2);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr, baseAddr + dataSize });
            string[] lines = csv.Split('\n');
            Assert.Equal("0", lines[0].Split(',')[0].Trim());
            Assert.Equal("1", lines[1].Split(',')[0].Trim());
        }

        [Fact]
        public void Export_IncludeBaseStats_AddsClassShapeColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(7, cols.Length);
        }

        [Fact]
        public void Export_IncludeGrowths_AddsSevenGrowthColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(7, cols.Length);
        }

        [Fact]
        public void Export_IncludeWepLevel_AddsEightWeaponColumnsFromB40ToB47()
        {
            // FE6 weapon-rank column count = 8; sourced from B40-B47, NOT B44-B51.
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(8, cols.Length);
            // Each emitted byte should be the signed value at offsets 40..47.
            // MakeStubRom fills data[addr + o] = (u * 100 + o) % 128 (positive,
            // so signed interpretation matches the byte value).
            for (uint i = 0; i < 8; i++)
            {
                sbyte expected = (sbyte)rom.u8(baseAddr + 40 + i);
                int actual = int.Parse(cols[i].Trim());
                Assert.Equal((int)expected, actual);
            }
        }

        /// <summary>
        /// Regression guard for the FE6 weapon-rank offset map: B40-B47 are
        /// weapon ranks (Sword/Lance/Axe/Bow/Staff/Anima/Light/Dark), and the
        /// bytes at B48-B51 (the FE6 P48 BattleAnime pointer u32) must NOT be
        /// exported as weapon-rank columns. (#388 finding)
        /// </summary>
        [Fact]
        public void Export_WepLevel_DoesNotSourceFromB48ThroughB51()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Set distinctive sentinels at offsets 48-51 (the P48 pointer bytes).
            rom.Data[baseAddr + 48] = 0x88;
            rom.Data[baseAddr + 49] = 0x99;
            rom.Data[baseAddr + 50] = 0xAA;
            rom.Data[baseAddr + 51] = 0xBB;

            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(8, cols.Length);

            // The 8 weapon-rank columns must reflect offsets 40-47 (which
            // MakeStubRom set deterministically), not the 0x88/0x99/0xAA/0xBB
            // sentinels we put at 48-51.
            for (uint i = 0; i < 8; i++)
            {
                sbyte expectedFromB40Plus = (sbyte)rom.u8(baseAddr + 40 + i);
                int actual = int.Parse(cols[i].Trim());
                Assert.Equal((int)expectedFromB40Plus, actual);
            }

            // And the sentinel bytes at B48-B51 must NOT appear in the exported columns.
            sbyte b48 = unchecked((sbyte)0x88);
            sbyte b49 = unchecked((sbyte)0x99);
            sbyte b50 = unchecked((sbyte)0xAA);
            sbyte b51 = unchecked((sbyte)0xBB);
            foreach (var col in cols)
            {
                int v = int.Parse(col.Trim());
                Assert.NotEqual((int)b48, v);
                Assert.NotEqual((int)b49, v);
                Assert.NotEqual((int)b50, v);
                Assert.NotEqual((int)b51, v);
            }
        }

        [Fact]
        public void Export_GrowthsAsDecimal_DividesBy100()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var raw = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            var dec = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: true);

            string rawCsv = raw.BuildExportCsv(rom, new[] { baseAddr });
            string decCsv = dec.BuildExportCsv(rom, new[] { baseAddr });
            string[] rawCols = rawCsv.TrimEnd('\n').Split(',');
            string[] decCols = decCsv.TrimEnd('\n').Split(',');
            Assert.Equal(rawCols.Length, decCols.Length);
            int rawHp = int.Parse(rawCols[0].Trim());
            float decHp = float.Parse(decCols[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(rawHp / 100.0f, decHp, 2);
        }

        [Fact]
        public void ExportSelected_OnlyEmitsOneDataRow()
        {
            var (rom, baseAddr, _) = MakeStubRom(5);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Equal(1, csv.Count(c => c == '\n'));
        }

        [Fact]
        public void Export_WithStartingUid_AnchorsUidColumnToProvidedValue()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(3);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            uint thirdRowAddr = baseAddr + 2 * dataSize;
            string csv = mgr.BuildExportCsv(rom, new[] { thirdRowAddr }, startingUid: 2u);
            string[] lines = csv.Split('\n');
            Assert.Equal("2", lines[0].Split(',')[0].Trim());
        }

        [Fact]
        public void Export_WithoutStartingUid_RemainsZeroBased()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Equal("0", csv.Split('\n')[0].Split(',')[0].Trim());
        }

        [Fact]
        public void Import_Roundtrip_PreservesClassShapeBytes_FE6Offsets()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Snapshot the relevant bytes BEFORE export.
            byte[] before = new byte[72];
            Array.Copy(rom.Data, (int)baseAddr, before, 0, 72);

            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);

            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });

            // Zero out the relevant bytes — FE6 ranges: 11..17, 27..33, 40..47.
            for (int o = 11; o <= 17; o++) rom.Data[baseAddr + o] = 0;
            for (int o = 27; o <= 33; o++) rom.Data[baseAddr + o] = 0;
            for (int o = 40; o <= 47; o++) rom.Data[baseAddr + o] = 0;

            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(1, n);

            for (int o = 11; o <= 17; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
            for (int o = 27; o <= 33; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
            for (int o = 40; o <= 47; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
        }

        [Fact]
        public void Import_PreservesUnrelatedBytes_Including_B48_through_B51_and_D68()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Sentinels:
            //   B5 = identity range
            //   B20 = between base-stats and growths
            //   B36 = ability flags
            //   B48-B51 = P48 BattleAnime pointer u32 (FE6 layout)
            //   B68 = D68 Unknown u32
            rom.Data[baseAddr + 5] = 0xAB;
            rom.Data[baseAddr + 20] = 0xCD;
            rom.Data[baseAddr + 36] = 0xEF;
            rom.Data[baseAddr + 48] = 0x11;
            rom.Data[baseAddr + 49] = 0x22;
            rom.Data[baseAddr + 50] = 0x33;
            rom.Data[baseAddr + 51] = 0x44;
            rom.Data[baseAddr + 68] = 0x55;
            rom.Data[baseAddr + 71] = 0x66;

            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(0xAB, rom.Data[baseAddr + 5]);
            Assert.Equal(0xCD, rom.Data[baseAddr + 20]);
            Assert.Equal(0xEF, rom.Data[baseAddr + 36]);
            // FE6 P48 BattleAnime pointer bytes must NOT be touched.
            Assert.Equal(0x11, rom.Data[baseAddr + 48]);
            Assert.Equal(0x22, rom.Data[baseAddr + 49]);
            Assert.Equal(0x33, rom.Data[baseAddr + 50]);
            Assert.Equal(0x44, rom.Data[baseAddr + 51]);
            // D68 Unknown bytes must NOT be touched.
            Assert.Equal(0x55, rom.Data[baseAddr + 68]);
            Assert.Equal(0x66, rom.Data[baseAddr + 71]);
        }

        [Fact]
        public void Export_IncludeHeader_UidWithoutName_OmitsUidHeader()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: true,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] lines = csv.Split('\n');
            Assert.DoesNotContain("UID", lines[0]);
            Assert.StartsWith("HP", lines[0]);
        }

        [Fact]
        public void ApplyImportCsv_BaseStatParseFailure_Throws()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = "1, NOT_A_NUMBER, 3, 4, 5, 6, 7\n";
            var ex = Assert.Throws<FormatException>(() => mgr.ApplyImportCsv(rom, csv, new[] { baseAddr }));
            Assert.Contains("base stat", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NOT_A_NUMBER", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ApplyImportCsv_WepLevelParseFailure_Throws()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = "1, 2, 3, BAD, 5, 6, 7, 8\n";
            var ex = Assert.Throws<FormatException>(() => mgr.ApplyImportCsv(rom, csv, new[] { baseAddr }));
            Assert.Contains("weapon level", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ApplyImportCsv_MultiRow_WithUID_MissingUidThrows()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(2);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = "0, 1, 2, 3, 4, 5, 6, 7\nBAD, 11, 12, 13, 14, 15, 16, 17\n";
            var ex = Assert.Throws<FormatException>(() =>
                mgr.ApplyImportCsv(rom, csv, new[] { baseAddr, baseAddr + dataSize }));
            Assert.Contains("UID", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ApplyImportCsv_AcceptsCommaDecimalGrowthValues()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                var mgr = new ClassFE6CsvManager(
                    useClipboard: false, includeUID: false, includeHeader: false,
                    includeName: false, includeBaseStats: false, includeGrowths: true,
                    includeWepLevel: false, growthsAsDecimal: true);
                string csv = "0,25, 0,25, 0,25, 0,25, 0,25, 0,25, 0,25\n";
                int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
                Assert.Equal(1, n);
                Assert.Equal((sbyte)25, (sbyte)rom.u8(baseAddr + 27));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = savedCulture;
            }
        }

        [Fact]
        public void ApplyImportCsv_AcceptsQuotedFieldsWithEmbeddedComma()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: true, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = "\"Hero, Of Bern\", 12, 1, 2, 3, 4, 5, 6\n";
            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(1, n);
            Assert.Equal((sbyte)12, (sbyte)rom.u8(baseAddr + 11));
        }

        [Fact]
        public void ImportAll_ParsesUidAndRoutesToCorrectRow()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(3);
            var mgr = new ClassFE6CsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);

            var addrs = new[] { baseAddr, baseAddr + dataSize, baseAddr + 2 * dataSize };
            string csv = mgr.BuildExportCsv(rom, addrs);
            string[] lines = csv.Split('\n');
            string reordered = string.Join("\n", new[] { lines[2], lines[0], lines[1], lines[3] });

            byte[] u2Before = new byte[7];
            Array.Copy(rom.Data, (int)(baseAddr + 2 * dataSize + 11), u2Before, 0, 7);
            for (int o = 11; o <= 17; o++)
            {
                rom.Data[baseAddr + o] = 0;
                rom.Data[baseAddr + dataSize + o] = 0;
                rom.Data[baseAddr + 2 * dataSize + o] = 0;
            }

            int n = mgr.ApplyImportCsv(rom, reordered, addrs);
            Assert.Equal(3, n);

            for (int o = 11; o <= 17; o++)
                Assert.Equal(u2Before[o - 11], rom.Data[baseAddr + 2 * dataSize + o]);
        }

        // =================================================================
        // ---- MoveCostFE6View.NavigateToWithCostType jump regression guards ----
        // =================================================================

        [AvaloniaFact]
        public void NavigateToWithCostType_SetsCostType_MoveCostNormal()
        {
            var view = new MoveCostFE6View();
            view.NavigateToWithCostType(0x100u, CostType.MoveCostNormal);
            if (view.DataViewModel is MoveCostFE6ViewModel vm)
            {
                Assert.Equal(CostType.MoveCostNormal, vm.SelectedCostType);
            }
        }

        [AvaloniaFact]
        public void NavigateToWithCostType_SetsCostType_TerrainAvoid()
        {
            var view = new MoveCostFE6View();
            view.NavigateToWithCostType(0x100u, CostType.TerrainAvoid);
            if (view.DataViewModel is MoveCostFE6ViewModel vm)
            {
                Assert.Equal(CostType.TerrainAvoid, vm.SelectedCostType);
            }
        }

        [AvaloniaFact]
        public void NavigateToWithCostType_SetsCostType_TerrainDefense()
        {
            var view = new MoveCostFE6View();
            view.NavigateToWithCostType(0x100u, CostType.TerrainDefense);
            if (view.DataViewModel is MoveCostFE6ViewModel vm)
            {
                Assert.Equal(CostType.TerrainDefense, vm.SelectedCostType);
            }
        }

        [AvaloniaFact]
        public void NavigateToWithCostType_SetsCostType_TerrainResistance()
        {
            var view = new MoveCostFE6View();
            view.NavigateToWithCostType(0x100u, CostType.TerrainResistance);
            if (view.DataViewModel is MoveCostFE6ViewModel vm)
            {
                Assert.Equal(CostType.TerrainResistance, vm.SelectedCostType);
            }
        }

        /// <summary>
        /// The CostTypeCombo's SelectedIndex MUST also move so the UI combo
        /// reflects the requested cost type (v3 non-blocking review note).
        /// </summary>
        [AvaloniaFact]
        public void NavigateToWithCostType_UpdatesCostTypeComboSelectedIndex()
        {
            var view = new MoveCostFE6View();
            view.NavigateToWithCostType(0x100u, CostType.TerrainAvoid);

            var combo = FindByAutomationId<ComboBox>(view, "MoveCostFE6_CostType_Combo");
            // The view layout uses Name="CostTypeCombo" without AutomationId
            // — fall back to walking the logical tree for the named combo.
            if (combo == null)
            {
                foreach (var descendant in view.GetLogicalDescendants())
                {
                    if (descendant is ComboBox cb && cb.Name == "CostTypeCombo")
                    {
                        combo = cb;
                        break;
                    }
                }
            }
            Assert.NotNull(combo);
            // TerrainAvoid is the 2nd CostTypeItem (index 1) per
            // MoveCostFE6ViewModel.BuildCostTypeItems().
            Assert.Equal(1, combo!.SelectedIndex);
        }

        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
