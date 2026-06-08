// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for UnitEditorView (#413).
//
// Asserts the new controls added to close the WF-only label backlog (mirrors
// the UnitFE7View parity pattern from #428/PR #520):
//   - HardCoding warning hyperlink label.
//   - Address-bar infrastructure: ReadStartAddress / ReadCount / Reload / Size / SelectedAddress prefix.
//   - Export/Import options panel (8 checkboxes + 4 buttons) backed by UnitCsvManager.
//
// Density check asserts UnitForm moves to Verdict.Low (|delta%| < 25) after
// the controls are added. L10n check asserts ja+zh have zero untranslated
// literals for UnitEditorView.axaml.
using System;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class UnitEditorParityTests
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
            var view = new UnitEditorView();
            var lbl = FindByAutomationId<TextBlock>(view, "UnitEditor_HardCoding_Warning_Label");
            Assert.NotNull(lbl);
            Assert.False(lbl!.IsVisible);
        }

        // ---- WU2: Address-bar infrastructure (4 labels + prefix + reload) ----

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new UnitEditorView();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitEditor_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitEditor_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitEditor_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitEditor_Size_Label"));
            // Selected-address PREFIX label (the "Selected Address:" caption,
            // distinct from the existing UnitEditor_Addr_Label value).
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitEditor_SelectedAddressPrefix_Label"));
        }

        // ---- WU3: Export/Import options panel (8 checkboxes + 4 buttons) ----

        [AvaloniaFact]
        public void View_Hosts_ExportImportOptions_AllEightCheckboxes()
        {
            var view = new UnitEditorView();
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_UseClipboard_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeUID_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeHeader_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeName_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeBaseStats_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeGrowths_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeWepLevel_Check"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "UnitEditor_GrowthsAsDecimal_Check"));
        }

        [AvaloniaFact]
        public void View_Hosts_FourExportImportButtons()
        {
            var view = new UnitEditorView();
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitEditor_ExportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitEditor_ExportSelected_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitEditor_ImportAll_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitEditor_ImportSelected_Button"));
        }

        /// <summary>
        /// CSV option defaults must match WF UnitForm so a freshly-opened
        /// view + Export All produces useful output, not empty rows.
        /// WF defaults: UseClipboard=false, all 7 others=true.
        /// </summary>
        [AvaloniaFact]
        public void ExportImportOptions_DefaultsMatchWF()
        {
            var view = new UnitEditorView();
            Assert.False(FindByAutomationId<CheckBox>(view, "UnitEditor_UseClipboard_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeHeader_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeUID_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeName_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeBaseStats_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeGrowths_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_IncludeWepLevel_Check")!.IsChecked);
            Assert.True(FindByAutomationId<CheckBox>(view, "UnitEditor_GrowthsAsDecimal_Check")!.IsChecked);
        }

        // ---- HardCoding warning: visibility flips with IAsmMapCache result ----

        class TogglingAsmMapCache : IAsmMapCache
        {
            readonly System.Collections.Generic.HashSet<uint> _hardcoded;
            public TogglingAsmMapCache(params uint[] hardcoded) { _hardcoded = new(hardcoded); }
            public void ClearCache() { }
            public bool IsHardCodeUnit(uint unitId) => _hardcoded.Contains(unitId);
            // IsHardCodeClass added in #406 (additive interface member).
            // Stub returns false so existing unit-editor tests remain valid.
            public bool IsHardCodeClass(uint classId) => false;
        }

        [AvaloniaFact]
        public void HardCodingWarning_FlipsVisible_WhenCacheSaysSo()
        {
            // Swap in a cache that reports unit-id 1 as hardcoded. The view's
            // RefreshHardCodingWarning() reads the cache from CoreState; swap
            // it back at the end to keep other tests clean.
            var prevCache = CoreState.AsmMapFileAsmCache;
            try
            {
                CoreState.AsmMapFileAsmCache = new TogglingAsmMapCache(1u);
                var view = new UnitEditorView();
                var lbl = FindByAutomationId<TextBlock>(view, "UnitEditor_HardCoding_Warning_Label");
                Assert.NotNull(lbl);

                // Default - no selection - stays hidden.
                Assert.False(lbl!.IsVisible);

                // Push items into the AddressList so SelectedOriginalIndex
                // can resolve to a real value (matches the live runtime path
                // where LoadList() populates the list before selection).
                var unitList = FindByAutomationId<AddressListControl>(view, "UnitEditor_Unit_List");
                Assert.NotNull(unitList);
                unitList!.SetItems(new List<AddrResult>
                {
                    new AddrResult(0x100u, "01 Test1", 0),
                    new AddrResult(0x134u, "02 Test2", 1),
                });

                var refresh = typeof(UnitEditorView).GetMethod(
                    "RefreshHardCodingWarning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refresh);

                // With the AddressList populated and item 0 (unit-id 1) selected,
                // and the cache reporting unit-id 1 as hardcoded, the label MUST
                // flip visible.
                unitList.SelectByIndex(0);
                refresh!.Invoke(view, null);
                Assert.True(lbl.IsVisible, "Label should be visible: cache says unit-1 IS hardcoded and item 0 is selected (1-based unit-id = 1).");

                // Selecting item 1 (unit-id 2) - cache says NOT hardcoded - label hides.
                unitList.SelectByIndex(1);
                refresh!.Invoke(view, null);
                Assert.False(lbl.IsVisible, "Label should be hidden: cache says unit-2 is NOT hardcoded.");
            }
            finally
            {
                CoreState.AsmMapFileAsmCache = prevCache;
            }
        }

        // ---- WU5: density verdict ----

        [Fact]
        public void DensityVerdict_UnitForm_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "UnitForm");
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
                $"UnitForm density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        // ---- WU6: l10n coverage ----

        [Fact]
        public void L10nCoverage_UnitEditorView_HasNoUntranslated()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches the project's actual l10n gate - ko.txt
            // does not exist in this repo; see Known Limitations).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("UnitEditorView.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "UnitEditorView.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
        }

        // =================================================================
        // ---- UnitCsvManager behavioral tests (WU3 - pure logic, no ROM) ----
        // =================================================================

        /// <summary>
        /// Construct an in-memory <see cref="ROM"/> with a deterministic
        /// unit-table layout so the CSV manager has predictable bytes to
        /// read/write. The ROM has N unit rows starting at offset 0x100 with
        /// 52-byte FE8-shape entries. RomInfo is intentionally NOT set - the
        /// CSV manager reads via direct byte access (rom.u8 / write_u8) and
        /// does not need a per-version RomInfo.
        /// </summary>
        static (ROM rom, uint baseAddr, uint dataSize) MakeStubRom(int unitCount = 2)
        {
            const uint baseAddr = 0x100;
            const uint dataSize = 52;
            byte[] data = new byte[0x10000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            // Fill unit rows with distinct sentinel bytes per offset so we can
            // verify each "Include*" option pulls the right columns.
            for (int u = 0; u < unitCount; u++)
            {
                uint addr = baseAddr + (uint)(u * dataSize);
                // Each byte at offset O is (u*100 + O) (mod 128) so signed
                // reinterpretation stays meaningful.
                for (uint o = 0; o < dataSize; o++)
                {
                    data[addr + o] = (byte)((u * 100 + (int)o) % 128);
                }
                // u16 at offset 0 = unit-id (used by Name lookup via TextForm).
                // Use a small id we don't try to decode in tests.
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
            var mgr = new UnitCsvManager(
                useClipboard: false,
                includeUID: false,
                includeHeader: false,
                includeName: false,
                includeBaseStats: false,
                includeGrowths: false,
                includeWepLevel: false,
                growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr, baseAddr + dataSize });
            // Two empty rows (each ending in '\n').
            Assert.Equal("\n\n", csv);
        }

        [Fact]
        public void Export_IncludeHeader_PrependsColumnHeader()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
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
        }

        [Fact]
        public void Export_OutputIsCsvNotTsv()
        {
            // Output uses comma separators (matches WF CsvManager.ToCsv).
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            // Has at least one comma and zero tab characters.
            Assert.Contains(",", csv);
            Assert.DoesNotContain("\t", csv);
        }

        [Fact]
        public void Export_IncludeUID_AddsUidColumn()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(2);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr, baseAddr + dataSize });
            string[] lines = csv.Split('\n');
            // Two data lines (third element is the empty trailing newline).
            Assert.Equal("0", lines[0].Split(',')[0].Trim());
            Assert.Equal("1", lines[1].Split(',')[0].Trim());
        }

        [Fact]
        public void Export_IncludeBaseStats_AddsBaseStatColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            // 8 base-stat columns (HP, STR, SKL, SPD, DEF, RES, LUCK, CON)
            // (+ MAG if FE8UMAGIC patch installed; not in this stub ROM).
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(8, cols.Length);
        }

        [Fact]
        public void Export_IncludeGrowths_AddsGrowthColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            // 7 growth columns (HP, STR, SKL, SPD, DEF, RES, LUCK)
            // (+ MAG if FE8UMAGIC patch installed; not in this stub ROM).
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(7, cols.Length);
        }

        [Fact]
        public void Export_IncludeWepLevel_AddsWeaponColumns()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            // 8 weapon-level columns (Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark)
            string[] cols = csv.TrimEnd('\n').Split(',');
            Assert.Equal(8, cols.Length);
        }

        [Fact]
        public void Export_GrowthsAsDecimal_DividesBy100()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Compare raw vs decimal output.
            var raw = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: false);
            var dec = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: true,
                includeWepLevel: false, growthsAsDecimal: true);

            string rawCsv = raw.BuildExportCsv(rom, new[] { baseAddr });
            string decCsv = dec.BuildExportCsv(rom, new[] { baseAddr });
            // Decimal mode divides by 100; the first non-zero growth column
            // should be 100x smaller in decimal mode (e.g. raw=50 -> dec=0.5).
            string[] rawCols = rawCsv.TrimEnd('\n').Split(',');
            string[] decCols = decCsv.TrimEnd('\n').Split(',');
            // Both layouts have 7 growth columns (no FE8UMAGIC patch).
            Assert.Equal(rawCols.Length, decCols.Length);
            // First growth column (HP @ offset 28).
            int rawHp = int.Parse(rawCols[0].Trim());
            float decHp = float.Parse(decCols[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(rawHp / 100.0f, decHp, 2);
        }

        [Fact]
        public void ExportSelected_OnlyEmitsOneDataRow()
        {
            var (rom, baseAddr, _) = MakeStubRom(5);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: false, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            // ExportSelected takes a single address. Output should have exactly
            // one data row (one '\n').
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            Assert.Equal(1, csv.Count(c => c == '\n'));
        }

        [Fact]
        public void Import_Roundtrip_PreservesAllBytes()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Snapshot the unit-row bytes BEFORE export.
            byte[] before = new byte[52];
            Array.Copy(rom.Data, (int)baseAddr, before, 0, 52);

            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);

            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });

            // Zero out the relevant bytes so the import path has to write them.
            // Stats live at +12..+19, growths at +28..+34, weplevel at +20..+27.
            for (int o = 12; o < 35; o++) rom.Data[baseAddr + o] = 0;

            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(1, n);

            // After import the stats/growth/weplevel bytes should match the
            // pre-export snapshot.
            for (int o = 12; o < 35; o++)
            {
                Assert.Equal(before[o], rom.Data[baseAddr + o]);
            }
        }

        [Fact]
        public void Import_PreservesUnrelatedBytes()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            // Set a sentinel byte at +40 (outside all stat/growth/weplevel
            // ranges) and verify import doesn't touch it.
            rom.Data[baseAddr + 40] = 0xAB;

            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: true,
                includeWepLevel: true, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            Assert.Equal(0xAB, rom.Data[baseAddr + 40]);
        }

        /// <summary>
        /// Header should mirror WF CsvManager.SetupHeader: emits "Name, " only
        /// when includeName is set; UID alone does NOT introduce a header
        /// column (the bare numeric still appears in the data row).
        /// </summary>
        [Fact]
        public void Export_IncludeHeader_UidWithoutName_OmitsUidHeader()
        {
            var (rom, baseAddr, _) = MakeStubRom(1);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: true, includeHeader: true,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
            string[] lines = csv.Split('\n');
            // First line is the header. Must NOT contain "UID".
            Assert.DoesNotContain("UID", lines[0]);
            // Header still starts with "HP" because UID column is unnamed.
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
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: false, includeHeader: false,
                includeName: true, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);
            // Name with an embedded comma must be quoted; the parser should
            // treat it as a single field (matches WF TextFieldParser).
            string csv = "\"Eirika, Princess of Renais\", 12, 1, 2, 3, 4, 5, 6, 7\n";
            int n = mgr.ApplyImportCsv(rom, csv, new[] { baseAddr });
            // Exactly one row applied; stats[0] = 12.
            Assert.Equal(1, n);
            Assert.Equal((sbyte)12, (sbyte)rom.u8(baseAddr + 12));
        }

        /// <summary>
        /// Multi-row Import All should honor the embedded UID when present
        /// and route each CSV row to the correct unit address (matches WF
        /// CsvManager behavior). A reordered CSV must not write stats to
        /// the wrong unit.
        /// </summary>
        [Fact]
        public void ImportAll_ParsesUidAndRoutesToCorrectRow()
        {
            var (rom, baseAddr, dataSize) = MakeStubRom(3);
            var mgr = new UnitCsvManager(
                useClipboard: false, includeUID: true, includeHeader: false,
                includeName: false, includeBaseStats: true, includeGrowths: false,
                includeWepLevel: false, growthsAsDecimal: false);

            // Build the CSV for all 3 rows, then REORDER the rows so UID 2
            // comes first.
            var addrs = new[] { baseAddr, baseAddr + dataSize, baseAddr + 2 * dataSize };
            string csv = mgr.BuildExportCsv(rom, addrs);
            string[] lines = csv.Split('\n');
            string reordered = string.Join("\n", new[] { lines[2], lines[0], lines[1], lines[3] });

            // Snapshot unit-2 stats BEFORE import; zero them after.
            byte[] u2Before = new byte[8];
            Array.Copy(rom.Data, (int)(baseAddr + 2 * dataSize + 12), u2Before, 0, 8);
            for (int o = 12; o <= 19; o++)
            {
                rom.Data[baseAddr + o] = 0;
                rom.Data[baseAddr + dataSize + o] = 0;
                rom.Data[baseAddr + 2 * dataSize + o] = 0;
            }

            int n = mgr.ApplyImportCsv(rom, reordered, addrs);
            Assert.Equal(3, n);

            // Unit-2 stats restored despite being the FIRST row in the CSV.
            for (int o = 12; o <= 19; o++)
                Assert.Equal(u2Before[o - 12], rom.Data[baseAddr + 2 * dataSize + o]);
        }

        // =================================================================
        // ---- #1016: FE8U MagicSplit (FE8UMAGIC) MAG column tests ----
        // =================================================================

        /// <summary>
        /// Clean FE8U ROM with NO MagicSplit signature: SearchMagicSplit()==NO,
        /// so the Unit CSV header + column counts are byte-identical to the
        /// pre-#1016 baseline (regression guard).
        /// </summary>
        [Fact]
        public void Unit_VanillaFE8U_NoMagicColumn_HeaderUnchanged()
        {
            var prev = CoreState.ROM;
            try
            {
                byte[] data = new byte[0x1000000];
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("BE8E01"), 0, data, 0xAC, 6);
                var rom = new ROM();
                rom.LoadLow("test.gba", data, "BE8E01");
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.NO, MagicSplitUtil.SearchMagicSplit());

                uint baseAddr = 0x500;
                var mgr = new UnitCsvManager(
                    useClipboard: false, includeUID: false, includeHeader: true,
                    includeName: false, includeBaseStats: true, includeGrowths: true,
                    includeWepLevel: true, growthsAsDecimal: false);
                string csv = mgr.BuildExportCsv(rom, new[] { baseAddr });
                string header = csv.Split('\n')[0];
                // Exact baseline header (no MAG): base 8 + growth 7 + weplevel 8.
                Assert.Equal(
                    "HP, STR, SKL, SPD, DEF, RES, LUCK, CON, HP, STR, SKL, SPD, DEF, RES, LUCK, " +
                    "Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark",
                    header);
                Assert.DoesNotContain("MAG", header);
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        /// <summary>
        /// FE6 ROM never matches FE8UMAGIC, so no MAG column is emitted.
        /// </summary>
        [Fact]
        public void Unit_FE6_NoMagicColumn()
        {
            var prev = CoreState.ROM;
            try
            {
                byte[] data = new byte[0x800000];
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("AFEJ01"), 0, data, 0xAC, 6);
                var rom = new ROM();
                rom.LoadLow("test.gba", data, "AFEJ01");
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.NotEqual(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                uint baseAddr = 0x500;
                var mgr = new UnitCsvManager(
                    useClipboard: false, includeUID: false, includeHeader: true,
                    includeName: false, includeBaseStats: true, includeGrowths: true,
                    includeWepLevel: false, growthsAsDecimal: false);
                string header = mgr.BuildExportCsv(rom, new[] { baseAddr }).Split('\n')[0];
                Assert.DoesNotContain("MAG", header);
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        /// <summary>
        /// FE8UMAGIC ROM: the Unit CSV header has exactly one "MAG" immediately
        /// after the base block and one immediately after the growth block
        /// (positions, not just Contains), weplevel columns shifted by +1 per
        /// active block.
        /// </summary>
        [Fact]
        public void Unit_MagicSplitFE8U_HeaderHasMagAtBaseAndGrowthEnds()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = FE8UMagicSplitTestRom.Make();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                uint baseAddr = 0x500;
                var mgr = new UnitCsvManager(
                    useClipboard: false, includeUID: false, includeHeader: true,
                    includeName: false, includeBaseStats: true, includeGrowths: true,
                    includeWepLevel: true, growthsAsDecimal: false);
                string[] cols = mgr.BuildExportCsv(rom, new[] { baseAddr }).Split('\n')[0].Split(',');
                for (int i = 0; i < cols.Length; i++) cols[i] = cols[i].Trim();

                // Base block (0..7) then MAG at index 8.
                Assert.Equal("CON", cols[7]);
                Assert.Equal("MAG", cols[8]);
                // Growth block (9..15) then MAG at index 16.
                Assert.Equal("LUCK", cols[15]);
                Assert.Equal("MAG", cols[16]);
                // Weplevel shifted by +2 (one MAG per active block).
                Assert.Equal("Sword", cols[17]);
                Assert.Equal("Dark", cols[24]);
                Assert.Equal(25, cols.Length); // 8+1 + 7+1 + 8
                Assert.Equal(2, cols.Count(c => c == "MAG"));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        /// <summary>
        /// Full Unit MagicSplit VALUE round-trip INCLUDING a SELECTED unit id
        /// &gt; 0 (catches blockers 1+2: the right id is read/written). Seeds
        /// distinct base+growth MAG per unit, exports anchored to the selected
        /// id, then a SINGLE-ROW import (ExportSelected/ImportSelected shape)
        /// with DIFFERENT MAG values must land on the SELECTED unit, not unit 0.
        /// </summary>
        [Fact]
        public void Unit_MagicSplitFE8U_SelectedId_ValueRoundTrip()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = FE8UMagicSplitTestRom.Make();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                const uint dataSize = 52;
                uint a0 = 0x500, a4 = 0x500 + 4 * dataSize;

                var mgr = new UnitCsvManager(
                    useClipboard: false, includeUID: true, includeHeader: false,
                    includeName: false, includeBaseStats: true, includeGrowths: true,
                    includeWepLevel: false, growthsAsDecimal: false);

                // Seed: unit 0 -> base 1/grow 2 ; unit 4 -> base 7/grow 9.
                using (FE8UMagicSplitTestRom.BeginUndoScope(rom))
                {
                    var u = ROM.GetAmbientUndoData()!;
                    MagicSplitUtil.WriteUnitBaseMagicExtends(0, a0, ToByte(1), u);
                    MagicSplitUtil.WriteUnitGrowMagicExtends(0, a0, ToByte(2), u);
                    MagicSplitUtil.WriteUnitBaseMagicExtends(4, a4, ToByte(7), u);
                    MagicSplitUtil.WriteUnitGrowMagicExtends(4, a4, ToByte(9), u);
                }

                // Export the SELECTED unit (id 4) — anchored to startingUid 4.
                string selectedCsv = mgr.BuildExportCsv(rom, new[] { a4 }, startingUid: 4);
                // The exported MAG-base (col 8 = uid + 8 base) must be 7 (unit 4),
                // proving export reads MAG from the selected id, not unit 0.
                string[] expCols = selectedCsv.Split('\n')[0].Split(',');
                Assert.Equal("7", expCols[9].Trim()); // uid(0) + 8 base(1..8) -> MAG at 9

                // Now SINGLE-ROW import a CSV with DIFFERENT MAG for the SELECTED
                // unit id 4 (singleRowId=4): base 22 / grow 31. unit 0 must be
                // untouched. Row shape: uid, <8 base>, MAG_base, <7 growth>, MAG_grow.
                string importCsv =
                    "4, 1, 2, 3, 4, 5, 6, 7, 8, 22, 10, 11, 12, 13, 14, 15, 16, 31\n";
                using (FE8UMagicSplitTestRom.BeginUndoScope(rom))
                {
                    int n = mgr.ApplyImportCsv(rom, importCsv, new[] { a4 }, singleRowId: 4);
                    Assert.Equal(1, n);
                }

                // Selected unit 4 got the imported MAG.
                Assert.Equal((sbyte)22, (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(4, a4));
                Assert.Equal((sbyte)31, (sbyte)MagicSplitUtil.GetUnitGrowMagicExtends(4, a4));
                // Unit 0 unchanged (proves the write did NOT go to unit 0).
                Assert.Equal((sbyte)1, (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(0, a0));
                Assert.Equal((sbyte)2, (sbyte)MagicSplitUtil.GetUnitGrowMagicExtends(0, a0));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        /// <summary>
        /// MagicSplit import without an active ambient undo scope must FAIL FAST.
        /// </summary>
        [Fact]
        public void Unit_MagicSplitFE8U_ImportWithoutUndoScope_Throws()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = FE8UMagicSplitTestRom.Make();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                var mgr = new UnitCsvManager(
                    useClipboard: false, includeUID: false, includeHeader: false,
                    includeName: false, includeBaseStats: true, includeGrowths: false,
                    includeWepLevel: false, growthsAsDecimal: false);
                // base(8) + MAG(1) cols, no undo scope open.
                string csv = "1, 2, 3, 4, 5, 6, 7, 8, 9\n";
                Assert.Throws<InvalidOperationException>(
                    () => mgr.ApplyImportCsv(rom, csv, new[] { 0x500u }));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        static uint ToByte(int v) => (uint)(byte)(sbyte)v;

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
