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
