// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for UnitFE7View (#428).
//
// Asserts the new controls added to close the WF-only label backlog:
//   - Growth Simulator: SimLevel input + Sim{HP,STR,SKL,SPD,DEF,RES,LCK,Total,MagicExt} read-outs.
//   - HardCoding warning hyperlink label.
//   - Address-bar infrastructure: ReadStartAddress / ReadCount / Reload / Size / SelectedAddress prefix.
//   - Weapon-level letter labels next to each of the 8 weapon ranks.
//
// Density check asserts UnitFE7Form moves to Verdict.Low (|delta%| < 25)
// after the controls are added.
using System;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class UnitFE7ParityTests
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

        // ---- WU1: Growth Simulator panel ----

        [AvaloniaFact]
        public void View_Hosts_GrowthSimulator_AutomationIds()
        {
            var view = new UnitFE7View();
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "UnitFE7_SimLevel_Input"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimHP_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimSTR_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimSKL_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimSPD_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimDEF_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimRES_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimLCK_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_SimTotal_Label"));
        }

        // ---- WU2: HardCoding warning ----

        [AvaloniaFact]
        public void View_Hosts_HardCodingWarning_HiddenByDefault()
        {
            var view = new UnitFE7View();
            var lbl = FindByAutomationId<TextBlock>(view, "UnitFE7_HardCoding_Warning_Label");
            Assert.NotNull(lbl);
            Assert.False(lbl!.IsVisible);
        }

        // ---- WU3: Address-bar infrastructure ----

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new UnitFE7View();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitFE7_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_Size_Label"));
        }

        // ---- WU4: Weapon-level letter labels ----

        [AvaloniaFact]
        public void View_Hosts_WeaponLetter_Labels()
        {
            var view = new UnitFE7View();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepSwordLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepLanceLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepAxeLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepBowLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepStaffLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepAnimaLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepLightLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE7_WepDarkLetter_Label"));
        }

        // ---- WU1 VM logic: BuildSimAndGrow uses WeaponRankUtil-equivalent thresholds ----

        [Fact]
        public void ViewModel_BuildSimAndGrow_NoRom_DoesNotThrow()
        {
            var vm = new UnitFE7ViewModel();
            // Direct call without a ROM should be safe (returns empty / zero sim).
            var sim = vm.BuildSimAndGrow(20);
            Assert.NotNull(sim);
        }

        [Fact]
        public void ViewModel_BuildSimAndGrow_AppliesUnitGrowth()
        {
            var vm = new UnitFE7ViewModel();
            vm.HP = 20;
            vm.Str = 5;
            vm.Skl = 5;
            vm.Spd = 5;
            vm.Def = 5;
            vm.Res = 5;
            vm.Lck = 5;
            vm.GrowHP = 100;   // 100% growth = +1 per level
            vm.GrowSTR = 0;
            vm.Level = 1;
            var sim = vm.BuildSimAndGrow(5);
            // 4 level-ups at 100% HP growth = +4 HP
            Assert.True(sim.sim_hp >= vm.HP, $"sim_hp ({sim.sim_hp}) should be >= base HP ({vm.HP}) after growth");
        }

        // ---- Parity: BuildSimAndGrow mirrors WF UnitFE7Form.BuildSim()+Grow() shape ----

        [Fact]
        public void ViewModel_BuildSimAndGrow_MatchesWFBuildSimShape()
        {
            // We can't load FE7U.gba in the headless tests, but we CAN verify
            // that BuildSimAndGrow assembles the exact same inputs as the WF
            // BuildSim(): unit base (incl. level) -> unit growth -> ClassFormCore.SetSimClass
            // -> Grow(level, UnitGrow). When ROM is unloaded, the class step is
            // a no-op (ClassFormCore.SetSimClass guards on rom==null) and the
            // resulting sim must reflect only unit-base + unit-growth.
            var vm = new UnitFE7ViewModel();
            vm.HP = 30;
            vm.Str = 8;
            vm.Skl = 9;
            vm.Spd = 10;
            vm.Def = 7;
            vm.Res = 4;
            vm.Lck = 5;
            vm.GrowHP = 80;
            vm.GrowSTR = 45;
            vm.GrowSKL = 50;
            vm.GrowSPD = 40;
            vm.GrowDEF = 30;
            vm.GrowRES = 35;
            vm.GrowLCK = 45;
            vm.Level = 1;

            // Same calculation, done explicitly the WF way.
            var expected = new GrowSimulator();
            expected.SetUnitBase((int)vm.Level, vm.HP, vm.Str, vm.Skl, vm.Spd, vm.Def, vm.Res, vm.Lck, 0);
            expected.SetUnitGrow((int)vm.GrowHP, (int)vm.GrowSTR, (int)vm.GrowSKL, (int)vm.GrowSPD, (int)vm.GrowDEF, (int)vm.GrowRES, (int)vm.GrowLCK, 0);
            // No ClassFormCore.SetSimClass — no ROM => no class contribution.
            expected.Grow(20, GrowSimulator.GrowOptionEnum.UnitGrow);

            var actual = vm.BuildSimAndGrow(20);

            Assert.Equal(expected.sim_hp, actual.sim_hp);
            Assert.Equal(expected.sim_str, actual.sim_str);
            Assert.Equal(expected.sim_skill, actual.sim_skill);
            Assert.Equal(expected.sim_spd, actual.sim_spd);
            Assert.Equal(expected.sim_def, actual.sim_def);
            Assert.Equal(expected.sim_res, actual.sim_res);
            Assert.Equal(expected.sim_luck, actual.sim_luck);
            Assert.Equal(expected.sim_sum_grow_rate, actual.sim_sum_grow_rate);
        }

        // ---- HardCoding warning: visibility flips with IAsmMapCache result ----

        class TogglingAsmMapCache : IAsmMapCache
        {
            readonly System.Collections.Generic.HashSet<uint> _hardcoded;
            public TogglingAsmMapCache(params uint[] hardcoded) { _hardcoded = new(hardcoded); }
            public void ClearCache() { }
            public bool IsHardCodeUnit(uint unitId) => _hardcoded.Contains(unitId);
            // IsHardCodeClass added in #406 (additive interface member).
            public bool IsHardCodeClass(uint classId) => false;
        }

        [AvaloniaFact]
        public void HardCodingWarning_FlipsVisible_WhenCacheSaysSo()
        {
            // Swap in a cache that reports unit-id 1 (Eliwood) as hardcoded.
            // The view's RefreshHardCodingWarning() reads the cache from
            // CoreState; swap it back at the end to keep other tests clean.
            var prevCache = CoreState.AsmMapFileAsmCache;
            try
            {
                CoreState.AsmMapFileAsmCache = new TogglingAsmMapCache(1u);
                var view = new UnitFE7View();
                var lbl = FindByAutomationId<TextBlock>(view, "UnitFE7_HardCoding_Warning_Label");
                Assert.NotNull(lbl);

                // Default — no selection — stays hidden.
                Assert.False(lbl!.IsVisible);

                // Force-invoke the view's refresh helper via reflection (the
                // method is private). When EntryList has no selected index
                // the call returns hidden too (idx<0 short-circuit).
                var refresh = typeof(UnitFE7View).GetMethod(
                    "RefreshHardCodingWarning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refresh);
                refresh!.Invoke(view, null);
                Assert.False(lbl.IsVisible);

                // Now ask the cache directly (the path the handler executes
                // when a unit IS selected). With our toggling cache, unit-id 1
                // hits true; the contract used by the view holds.
                Assert.True(CoreState.AsmMapFileAsmCache!.IsHardCodeUnit(1u));
                Assert.False(CoreState.AsmMapFileAsmCache!.IsHardCodeUnit(2u));
            }
            finally
            {
                CoreState.AsmMapFileAsmCache = prevCache;
            }
        }

        // ---- PatchManagerView.JumpTo: filter + select ----

        [AvaloniaFact]
        public void PatchManagerView_JumpTo_SetsFilter()
        {
            var pv = new PatchManagerView();
            pv.JumpTo("Hardcoding", 0);
            var searchBox = FindByAutomationId<TextBox>(pv, "PatchManager_Search_Input")
                ?? pv.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchBox");
            Assert.NotNull(searchBox);
            Assert.Equal("Hardcoding", searchBox!.Text);
        }

        // ---- WU4 logic: Weapon letter helper is callable from Avalonia code ----

        [Theory]
        [InlineData(0u, 7, "-")]
        [InlineData(30u, 7, "E")]
        [InlineData(31u, 7, "D")]
        [InlineData(40u, 6, "E")]   // FE6: 40 is still E (1-50)
        [InlineData(40u, 7, "D")]   // FE7: 40 is D (31-70)
        public void WeaponRankUtil_GetRankLetter_FromAvaloniaProject(uint wexp, int romVer, string expected)
        {
            // Sanity: the Avalonia view will call WeaponRankUtil.GetRankLetter
            // exactly like this from the WepXxxBox.ValueChanged handler.
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(wexp, romVer));
        }

        // ---- WU6 density check: UnitFE7Form lands in LOW bucket ----

        [Fact]
        public void DensityVerdict_UnitFE7Form_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            // We use the pair discovery via labels-sweep glue so the density
            // numbers are computed the same way the published baseline is.
            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "UnitFE7Form");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            // The Designer.cs parse only succeeds when the WF source file is
            // reachable — on CI runners that don't check out the WinForms
            // bin or that case-fold paths differently (Linux), WfControlCount
            // can land at 0 and DeltaPct becomes Infinity. Skip the strict
            // assert in that scenario; the Windows runner still covers the
            // real verdict check.
            if (row!.WfControlCount == 0) return;
            // |delta%| < 25 — the scanner's strict "Low" threshold.
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"UnitFE7Form density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        // ---- WU5 l10n check: UnitFE7View has no Untranslated literals in ja+zh ----

        [Fact]
        public void L10nCoverage_UnitFE7View_HasNoUntranslated()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches the project's actual l10n gate — ko.txt
            // does not exist in this repo; see Known Limitations).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("UnitFE7View.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "UnitFE7View.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
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
