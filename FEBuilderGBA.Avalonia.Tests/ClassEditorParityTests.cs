// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for ClassEditorView (#406).
//
// Asserts the new controls added to close the WF-only label backlog (mirrors
// the UnitEditorView parity pattern from PR #559):
//   - HardCoding warning hyperlink label.
//   - Address-bar infrastructure: ReadStartAddress / ReadCount / Reload /
//     Size / SelectedAddress prefix.
//   - Export/Import options panel (8 checkboxes + 4 buttons) backed by
//     the new ClassCsvManager.
//   - FE8-only CC Branch jump button (parity with WF ClassForm.J_5_Click).
//   - Navigation manifest entries for PatchManagerView + CCBranchEditorView.
//
// Density check asserts ClassForm moves toward Verdict.Low (|delta%| < 25)
// after the controls are added. L10n check asserts ja+zh have zero
// untranslated literals for ClassEditorView.axaml.
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
    public class ClassEditorParityTests
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
            var view = new ClassEditorView();
            var lbl = FindByAutomationId<TextBlock>(view, "ClassEditor_HardCoding_Warning_Label");
            Assert.NotNull(lbl);
            Assert.False(lbl!.IsVisible);
        }

        // ---- WU2: Address-bar infrastructure ----

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new ClassEditorView();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassEditor_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassEditor_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassEditor_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassEditor_Size_Label"));
            // Selected-address PREFIX label (the "Address:" caption,
            // distinct from the existing ClassEditor_Addr_Label value).
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ClassEditor_SelectedAddressPrefix_Label"));
        }

        // ---- WU3: Export/Import options panel (8 checkboxes + 4 buttons) ----

        [AvaloniaFact]
        public void View_Hosts_ExportImportOptions_AllEightCheckboxes()
        {
            var view = new ClassEditorView();
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_UseClipboard_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeUID_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeHeader_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeName_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeBaseStats_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeGrowths_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeWepLevel_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ClassEditor_GrowthsAsDecimal_Check"));
        }

        [AvaloniaFact]
        public void View_Hosts_FourExportImportButtons()
        {
            var view = new ClassEditorView();
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassEditor_ExportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassEditor_ExportSelected_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassEditor_ImportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ClassEditor_ImportSelected_Button"));
        }

        /// <summary>
        /// CSV option defaults must match WF ClassForm so a freshly-opened
        /// view + Export All produces useful output, not empty rows.
        /// WF defaults: UseClipboard=false, all 7 others=true.
        /// </summary>
        [AvaloniaFact]
        public void ExportImportOptions_DefaultsMatchWF()
        {
            var view = new ClassEditorView();
            Assert.False(FindByAutomationId<CheckBox>(view, "ClassEditor_UseClipboard_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeHeader_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeUID_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeName_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeBaseStats_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeGrowths_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_IncludeWepLevel_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "ClassEditor_GrowthsAsDecimal_Check")!.IsChecked);
        }

        // ---- WU4: CC Branch FE8 jump ----

        [AvaloniaFact]
        public void View_Hosts_CCBranchJump_Button_HiddenByDefault()
        {
            // Hidden by default until LoadList() detects FE8 — the gap-sweep
            // headless test exercises the *control existence*, not the live
            // version check (which depends on a loaded ROM).
            var view = new ClassEditorView();
            var btn = FindByAutomationId<Button>(view, "ClassEditor_JumpToCCBranch_Button");
            Assert.NotNull(btn);
            Assert.False(btn!.IsVisible);
        }

        // ---- HardCoding warning: visibility flips with IAsmMapCache result ----

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
            // Swap in a cache that reports class-id 0 as hardcoded. The
            // view's RefreshHardCodingWarning() reads the cache from
            // CoreState; swap it back at the end to keep other tests clean.
            var prevCache = CoreState.AsmMapFileAsmCache;
            try
            {
                CoreState.AsmMapFileAsmCache = new TogglingClassAsmMapCache(0u);
                var view = new ClassEditorView();
                var lbl = FindByAutomationId<TextBlock>(view, "ClassEditor_HardCoding_Warning_Label");
                Assert.NotNull(lbl);

                // Default - no selection - stays hidden.
                Assert.False(lbl!.IsVisible);

                // Push items into the ClassList so SelectedOriginalIndex
                // can resolve to a real value (matches the live runtime path
                // where LoadList() populates the list before selection).
                var classList = FindByAutomationId<AddressListControl>(view, "ClassEditor_Class_List");
                Assert.NotNull(classList);
                classList!.SetItems(new List<AddrResult>
                {
                    new AddrResult(0x100u, "00 Class0", 0),
                    new AddrResult(0x154u, "01 Class1", 1),
                });

                var refresh = typeof(ClassEditorView).GetMethod(
                    "RefreshHardCodingWarning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refresh);

                // With the ClassList populated and item 0 (class-id 0) selected,
                // and the cache reporting class-id 0 as hardcoded, the label MUST
                // flip visible.
                classList.SelectByIndex(0);
                refresh!.Invoke(view, null);
                Assert.True(lbl.IsVisible, "Label should be visible: cache says class-0 IS hardcoded and item 0 is selected.");

                // Selecting item 1 (class-id 1) - cache says NOT hardcoded - label hides.
                classList.SelectByIndex(1);
                refresh!.Invoke(view, null);
                Assert.False(lbl.IsVisible, "Label should be hidden: cache says class-1 is NOT hardcoded.");
            }
            finally
            {
                CoreState.AsmMapFileAsmCache = prevCache;
            }
        }

        // ---- WU4: Navigation manifest includes new entries ----

        [Fact]
        public void NavigationManifest_IncludesHardCodingPatch_And_CCBranch()
        {
            var vm = new ClassEditorViewModel();
            var targets = vm.GetNavigationTargets();
            Assert.NotNull(targets);

            // Existing 10 entries plus 2 new ones from #406.
            Assert.Contains(targets, t => t.CommandName == "JumpToHardCodingPatch" && t.TargetViewType == typeof(PatchManagerView));
            Assert.Contains(targets, t => t.CommandName == "JumpToCCBranch_FE8" && t.TargetViewType == typeof(CCBranchEditorView));

            // The 10 pre-existing entries are still there (sanity).
            Assert.Contains(targets, t => t.CommandName == "JumpToMoveCost");
            Assert.Contains(targets, t => t.CommandName == "JumpToBattleAnime");
            Assert.Contains(targets, t => t.CommandName == "JumpToNameText");
        }

        // ---- WU5: density verdict ----

        [Fact]
        public void DensityVerdict_ClassForm_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "ClassForm");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            // On CI runners where the WF Designer.cs path is unreachable
            // (case-fold, missing checkout), WfControlCount can land at 0 and
            // DeltaPct becomes Infinity. Skip the strict assert in that case;
            // the Windows runner still covers the real verdict check.
            if (row!.WfControlCount == 0) return;
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"ClassForm density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        // ---- WU5: l10n coverage ----

        [Fact]
        public void L10nCoverage_ClassEditorView_HasNoUntranslated()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches the project's actual l10n gate — ko.txt
            // does not exist in this repo; see Known Limitations).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("ClassEditorView.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "ClassEditorView.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
        }

        // =================================================================
        // ---- ClassCsvManager behavioral tests (WU3 — pure logic, no ROM) ----
        // =================================================================

        /// <summary>
        /// Construct an in-memory <see cref="ROM"/> with a deterministic
        /// class-table layout so the CSV manager has predictable bytes to
        /// read/write. The ROM has N class rows starting at offset 0x100 with
        /// 84-byte FE8-shape entries. RomInfo is intentionally NOT set — the
        /// CSV manager reads via direct byte access (rom.u8 / write_u8) and
        /// does not need a per-version RomInfo.
        /// </summary>
        static (ROM rom, uint baseAddr, uint dataSize) MakeStubRom(int classCount = 2)
        {
            const uint baseAddr = 0x100;
            const uint dataSize = 84;
            byte[] data = new byte[0x10000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            // Fill class rows with distinct sentinel bytes per offset so we can
            // verify each "Include*" option pulls the right columns.
            for (int u = 0; u < classCount; u++)
            {
                uint addr = baseAddr + (uint)(u * dataSize);
                // Each byte at offset O is (u*100 + O) (mod 128) so signed
                // reinterpretation stays meaningful.
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
            // With ALL 8 options false, export still emits one trailing newline
            // per row (matches WF CsvManager behavior).
            var (rom, baseAddr, dataSize) = MakeStubRom(2);
            var mgr = new ClassCsvManager(
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
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: true,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] lines = csv.Split('\n');
            Assert.True(lines.Length >= 2);
            // First line is the header (column names).
            Assert.Contains("HP", lines[0]);
            Assert.Contains("STR", lines[0]);
            Assert.Contains("DEF", lines[0]);
            // Class shape uses CON (not LUCK) in base stats.
            Assert.Contains("CON", lines[0]);
        }

        [Fact]
        public void Export_OutputIsCsvNotTsv()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
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
            var mgr = new ClassCsvManager(
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
            // Class shape: 7 base-stat columns (HP, STR, SKL, SPD, DEF, RES,
            // CON — no LUCK at base-stats; LUCK is a growth-only stat for
            // classes). MagicSplit out of scope.
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
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
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            // Class growth shape: 7 columns (HP, STR, SKL, SPD, DEF, RES, LUCK).
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(7, cols.Length);
        }

        [Fact]
        public void Export_IncludeWepLevel_AddsEightWeaponColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] cols = csv.TrimEnd('\n').Split(',');
            // Class weplevel shape: 8 columns (offsets 44..51 — Sword, Lance,
            // Axe, Bow, Staff, Anima, Light, Dark).
            Assert.Equal(8, cols.Length);
        }

        [Fact]
        public void Export_GrowthsAsDecimal_DividesBy100()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var raw = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            var dec = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: true);

            string rawCsv = raw.BuildExportCsv(rom, new[] { baseAddr });
            string decCsv = dec.BuildExportCsv(rom, new[] { baseAddr });
            string[] rawCols = rawCsv.TrimEnd('\n').Split(',');
            string[] decCols = decCsv.TrimEnd('\n').Split(',');
            Assert.Equal(rawCols.Length, decCols.Length);
            // First growth column (class-shape HP @ offset 27 — not 28).
            int rawHp = int.Parse(rawCols[0].Trim());
            float decHp = float.Parse(decCols[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(rawHp / 100.0f, decHp, 2);
        }

        [Fact]
        public void ExportSelected_OnlyEmitsOneDataRow()
        {
            var (rom, baseAddr, _) = MakeStubRom(5);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Equal(1, csv.Count(c => c == '\n'));
        }

        /// <summary>
        /// BuildExportCsv(rom, rowAddresses, startingUid) anchors the emitted
        /// UID column to <c>startingUid</c> instead of 0. The single-row
        /// "Export Selected" path uses this so the exported CSV carries the
        /// selected class's actual id (parity with WF
        /// <c>CsvManager.ExportSingle(InputFormRef, index)</c> — Copilot CLI
        /// inline review on PR #570).
        /// </summary>
        [Fact]
        public void Export_WithStartingUid_AnchorsUidColumnToProvidedValue()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(3);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            // Single-row export of the third class (UID 2), starting at uid=2.
            uint thirdRowAddr = baseAddr + 2 * dataSize;
            string csv = mgr.BuildExportCsv(rom, new[] { thirdRowAddr }, startingUid: 2u);
            string[] lines = csv.Split('\n');
            Assert.Equal("2", lines[0].Split(',')[0].Trim());
        }

        /// <summary>
        /// Verify the default zero-arg BuildExportCsv preserves the v1
        /// behavior (UID=0) so legacy callers / headless tests aren't
        /// affected by the new overload.
        /// </summary>
        [Fact]
        public void Export_WithoutStartingUid_RemainsZeroBased()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Equal("0", csv.Split('\n')[0].Split(',')[0].Trim());
        }

        [Fact]
        public void Import_Roundtrip_PreservesClassShapeBytes()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Snapshot the class-row bytes BEFORE export.
            byte[] before = new byte[84];
            Array.Copy(rom.Data, (int)baseAddr, before, 0, 84);

            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);

            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });

            // Zero out the relevant bytes so the import path has to write them.
            // Class-shape: stats at +11..+17, growths at +27..+33, weplevel at +44..+51.
            for (int o = 11; o <= 17; o++) rom.Data[baseAddr + o] = 0;
            for (int o = 27; o <= 33; o++) rom.Data[baseAddr + o] = 0;
            for (int o = 44; o <= 51; o++) rom.Data[baseAddr + o] = 0;

            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(1, n);

            // After import the stats/growth/weplevel bytes should match the
            // pre-export snapshot.
            for (int o = 11; o <= 17; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
            for (int o = 27; o <= 33; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
            for (int o = 44; o <= 51; o++) Assert.Equal(before[o], rom.Data[baseAddr + o]);
        }

        [Fact]
        public void Import_PreservesUnrelatedBytes()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Set sentinels in three "safe" offsets (outside the
            // class-shape stat/growth/weplevel ranges 11..17, 27..33, 44..51).
            rom.Data[baseAddr + 5] = 0xAB;   // identity range
            rom.Data[baseAddr + 20] = 0xCD;  // between base-stats and growths
            rom.Data[baseAddr + 40] = 0xEF;  // between growths and weplevel
            rom.Data[baseAddr + 60] = 0x99;  // after weplevel

            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(0xAB, rom.Data[baseAddr + 5]);
            Assert.Equal(0xCD, rom.Data[baseAddr + 20]);
            Assert.Equal(0xEF, rom.Data[baseAddr + 40]);
            Assert.Equal(0x99, rom.Data[baseAddr + 60]);
        }

        /// <summary>
        /// Header should mirror WF CsvManager.SetupHeader: emits "Name" only
        /// when includeName is set; UID alone does NOT introduce a header
        /// column (the bare numeric still appears in the data row).
        /// </summary>
        [Fact]
        public void Export_IncludeHeader_UidWithoutName_OmitsUidHeader()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: true, includeHeader: true,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] lines = csv.Split('\n');
            Assert.DoesNotContain("UID", lines[0]);
            // Header starts with "HP" because UID column is unnamed.
            Assert.StartsWith("HP", lines[0]);
        }

        /// <summary>
        /// ApplyImportCsv should accept quoted fields with embedded commas
        /// (matches WF CsvManager's TextFieldParser semantics).
        /// </summary>
        [Fact]
        public void ApplyImportCsv_AcceptsQuotedFieldsWithEmbeddedComma()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: true, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            // Class shape: 7 base-stat columns (HP..CON) follow the Name.
            string csv = "\"Lord, Of Renais\", 12, 1, 2, 3, 4, 5, 6\n";
            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(1, n);
            // stats[0] = 12 (HP at class offset 11).
            Assert.Equal((sbyte)12, (sbyte)rom.u8(baseAddr + 11));
        }

        /// <summary>
        /// Multi-row Import All should honor the embedded UID when present
        /// and route each CSV row to the correct class address (matches WF
        /// CsvManager behavior). A reordered CSV must not write stats to
        /// the wrong class.
        /// </summary>
        [Fact]
        public void ImportAll_ParsesUidAndRoutesToCorrectRow()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(3);
            var mgr = new ClassCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);

            // Build the CSV for all 3 rows, then REORDER the rows so UID 2
            // comes first.
            var addrs = new[] { baseAddr, baseAddr + dataSize, baseAddr + 2 * dataSize };
            string csv = mgr.BuildExportCsv(rom, addrs);
            string[] lines = csv.Split('\n');
            string reordered = string.Join("\n", new[] { lines[2], lines[0], lines[1], lines[3] });

            // Snapshot class-2 stats BEFORE import; zero them after.
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

            // Class-2 stats restored despite being the FIRST row in the CSV.
            for (int o = 11; o <= 17; o++)
                Assert.Equal(u2Before[o - 11], rom.Data[baseAddr + 2 * dataSize + o]);
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
