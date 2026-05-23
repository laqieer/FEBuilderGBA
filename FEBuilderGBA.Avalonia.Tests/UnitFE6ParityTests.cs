// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for UnitFE6View (#407).
//
// Asserts the new controls added to close the WF-only label backlog:
//   - Growth Simulator: SimLevel input + Sim{HP,STR,SKL,SPD,DEF,RES,LCK,Total} read-outs.
//   - HardCoding warning hyperlink label.
//   - Address-bar infrastructure: ReadStartAddress / ReadCount / Reload / Size / SelectedAddress prefix.
//   - Weapon-level letter labels next to each of the 8 weapon ranks (FE6 thresholds).
//
// Density check asserts UnitFE6Form moves to Verdict.Low (|delta%| < 25)
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
    public class UnitFE6ParityTests
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

        // ---- WU4: Growth Simulator panel ----

        [AvaloniaFact]
        public void View_Hosts_GrowthSimulator_AutomationIds()
        {
            var view = new UnitFE6View();
            Assert.NotNull(FindByAutomationId<NumericUpDown>(view, "UnitFE6_SimLevel_Input"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimHP_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimSTR_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimSKL_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimSPD_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimDEF_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimRES_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimLCK_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SimTotal_Label"));
        }

        // ---- WU2: HardCoding warning ----

        [AvaloniaFact]
        public void View_Hosts_HardCodingWarning_HiddenByDefault()
        {
            var view = new UnitFE6View();
            var lbl = FindByAutomationId<TextBlock>(view, "UnitFE6_HardCoding_Warning_Label");
            Assert.NotNull(lbl);
            Assert.False(lbl!.IsVisible);
        }

        // ---- WU1: Address-bar infrastructure ----

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new UnitFE6View();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "UnitFE6_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_Size_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_SelectedAddress_PrefixLabel"));
        }

        // ---- WU3: Weapon-level letter labels ----

        [AvaloniaFact]
        public void View_Hosts_WeaponLetter_Labels()
        {
            var view = new UnitFE6View();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepSwordLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepLanceLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepAxeLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepBowLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepStaffLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepAnimaLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepLightLetter_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "UnitFE6_WepDarkLetter_Label"));
        }

        // ---- WU4 VM logic: BuildSimAndGrow basic safety ----

        [Fact]
        public void ViewModel_BuildSimAndGrow_NoRom_DoesNotThrow()
        {
            var vm = new UnitFE6ViewModel();
            // Direct call without a ROM should be safe (returns zero sim).
            var sim = vm.BuildSimAndGrow(20);
            Assert.NotNull(sim);
        }

        [Fact]
        public void ViewModel_BuildSimAndGrow_AppliesUnitGrowth()
        {
            var vm = new UnitFE6ViewModel();
            vm.HP = 20;
            vm.Str = 5;
            vm.Skl = 5;
            vm.Spd = 5;
            vm.Def = 5;
            vm.Res = 5;
            vm.Lck = 5;
            vm.GrowHP = 100;   // 100% growth = +1 per level
            vm.GrowStr = 0;
            vm.Level = 1;
            var sim = vm.BuildSimAndGrow(5);
            // 4 level-ups at 100% HP growth should yield sim_hp >= 20.
            Assert.True(sim.sim_hp >= vm.HP, $"sim_hp ({sim.sim_hp}) should be >= base HP ({vm.HP}) after growth");
        }

        // ---- Parity: BuildSimAndGrow mirrors WF UnitFE6Form.BuildSim()+Grow() shape ----

        [Fact]
        public void ViewModel_BuildSimAndGrow_MatchesWFBuildSimShape()
        {
            // Without a ROM the class step is a no-op (ClassFormCore.SetSimClass
            // guards on rom==null), so the resulting sim must reflect only
            // unit-base + unit-growth, identical to the manual WF assembly.
            var vm = new UnitFE6ViewModel();
            vm.HP = 30;
            vm.Str = 8;
            vm.Skl = 9;
            vm.Spd = 10;
            vm.Def = 7;
            vm.Res = 4;
            vm.Lck = 5;
            vm.GrowHP = 80;
            vm.GrowStr = 45;
            vm.GrowSkl = 50;
            vm.GrowSpd = 40;
            vm.GrowDef = 30;
            vm.GrowRes = 35;
            vm.GrowLck = 45;
            vm.Level = 1;

            // Same calculation, done explicitly the WF way.
            var expected = new GrowSimulator();
            expected.SetUnitBase((int)vm.Level, vm.HP, vm.Str, vm.Skl, vm.Spd, vm.Def, vm.Res, vm.Lck, 0);
            expected.SetUnitGrow((int)vm.GrowHP, (int)vm.GrowStr, (int)vm.GrowSkl, (int)vm.GrowSpd, (int)vm.GrowDef, (int)vm.GrowRes, (int)vm.GrowLck, 0);
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

        // ---- WU4 unsigned-stat parity (FE6-specific Copilot CLI plan concern) ----

        [Fact]
        public void ViewModel_BuildSimAndGrow_UnsignedFE6Stats()
        {
            // WF FE6 base stats are unsigned (0-255). The Avalonia VM stores
            // them as signed sbyte for binary compatibility (HP=200 displays
            // as -56). The sim helper MUST coerce back to unsigned before
            // calling GrowSimulator. Otherwise sim_hp would compute against
            // a negative base which doesn't match WF behavior.
            var vm = new UnitFE6ViewModel();
            // HP = 200 unsigned, stored as -56 (sbyte). Use the signed-int
            // representation to mimic the value the view's ReadFromUI()
            // would push into the VM after the user types 200 in the box
            // (or after LoadUnit sbyte-casts a u8=0xC8 ROM byte).
            vm.HP = unchecked((int)(sbyte)200); // -56
            vm.GrowHP = 0;            // freeze growth so sim_hp == base
            vm.Level = 1;

            var sim = vm.BuildSimAndGrow(1);
            // The sim should see 200, not -56 (because WF FE6 stats are
            // unsigned and the helper re-normalizes).
            Assert.Equal(200, sim.sim_hp);
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
            // Swap in a cache that reports unit-id 1 (Roy) as hardcoded.
            // The view's RefreshHardCodingWarning() reads the cache from
            // CoreState; swap it back at the end to keep other tests clean.
            var prevCache = CoreState.AsmMapFileAsmCache;
            try
            {
                CoreState.AsmMapFileAsmCache = new TogglingAsmMapCache(1u);
                var view = new UnitFE6View();
                var lbl = FindByAutomationId<TextBlock>(view, "UnitFE6_HardCoding_Warning_Label");
                Assert.NotNull(lbl);

                // (1) Default — no selection — stays hidden.
                Assert.False(lbl!.IsVisible);

                // Reflection-fetch the private refresh helper. We'll re-use
                // this for both branches of the visibility logic.
                var refresh = typeof(UnitFE6View).GetMethod(
                    "RefreshHardCodingWarning",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refresh);

                // (2) Force-invoke with no selected index — short-circuits hidden.
                refresh!.Invoke(view, null);
                Assert.False(lbl.IsVisible);

                // (3) Populate the EntryList with 3 mock items so we can drive
                // the selection deterministically (no ROM needed). The
                // displayed/original index for the first item is 0, so the
                // 1-based unit id passed to IsHardCodeUnit will be 1 (our
                // toggle-cache hit). The view's RefreshHardCodingWarning
                // computes unitId = (selectedOriginalIndex + 1) — verify
                // the visible=true branch executes.
                var entryList = view.FindControl<FEBuilderGBA.Avalonia.Controls.AddressListControl>("EntryList");
                Assert.NotNull(entryList);
                var items = new System.Collections.Generic.List<AddrResult>
                {
                    new AddrResult(0x06076D0, "01 Roy", 0),       // -> unit_id = 1 (hardcoded)
                    new AddrResult(0x0607700, "02 Clarine", 1),   // -> unit_id = 2 (not hardcoded)
                    new AddrResult(0x0607730, "03 Fa", 2),        // -> unit_id = 3 (not hardcoded)
                };
                entryList!.SetItems(items);   // SelectFirst() auto-selects index 0
                refresh.Invoke(view, null);
                Assert.True(lbl.IsVisible,
                    "RefreshHardCodingWarning should make the [HardCoding] label visible when the selected unit (unit_id=1, 1-based) is hardcoded.");

                // (4) Select the second row -> unit_id=2 (NOT hardcoded). Visible should flip back to false.
                entryList.SelectByIndex(1);
                refresh.Invoke(view, null);
                Assert.False(lbl.IsVisible,
                    "RefreshHardCodingWarning should hide the [HardCoding] label when the selected unit (unit_id=2, 1-based) is NOT in the cache.");

                // Sanity: cache contract holds.
                Assert.True(CoreState.AsmMapFileAsmCache!.IsHardCodeUnit(1u));
                Assert.False(CoreState.AsmMapFileAsmCache!.IsHardCodeUnit(2u));
            }
            finally
            {
                CoreState.AsmMapFileAsmCache = prevCache;
            }
        }

        // ---- WU3 logic: FE6-specific weapon-letter thresholds ----

        [Theory]
        [InlineData(0u, "-")]
        [InlineData(1u, "E")]    // FE6: 1-50 = E
        [InlineData(50u, "E")]   // FE6 boundary
        [InlineData(51u, "D")]   // FE6: 51-100 = D
        [InlineData(100u, "D")]  // FE6 boundary
        [InlineData(101u, "C")]  // FE6: 101-150 = C
        [InlineData(150u, "C")]  // FE6 boundary
        [InlineData(151u, "B")]  // FE6: 151-200 = B
        [InlineData(200u, "B")]  // FE6 boundary
        [InlineData(201u, "A")]  // FE6: 201-250 = A
        [InlineData(250u, "A")]  // FE6 boundary
        [InlineData(251u, "S")]  // FE6: 251+ = S
        [InlineData(255u, "S")]
        public void WeaponRankUtil_GetRankLetter_FE6Thresholds(uint wexp, string expected)
        {
            // The Avalonia view will call WeaponRankUtil.GetRankLetter(val, 6)
            // from RefreshWeaponLetter — pinning the FE6-specific boundary
            // points so the view's letter rendering matches WF behavior.
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(wexp, 6));
        }

        // ---- WU8 density check: UnitFE6Form lands in LOW bucket ----

        [Fact]
        public void DensityVerdict_UnitFE6Form_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "UnitFE6Form");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            // The Designer.cs parse only succeeds when the WF source file is
            // reachable — on CI runners that don't check out the WinForms
            // bin or case-fold paths differently (Linux), WfControlCount can
            // land at 0 and DeltaPct becomes Infinity. Skip the strict assert
            // in that scenario; the Windows runner still covers the real
            // verdict check (same pattern as UnitFE7ParityTests).
            if (row!.WfControlCount == 0) return;
            // |delta%| < 25 — the scanner's strict "Low" threshold.
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"UnitFE6Form density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        // ---- WU6 l10n check: UnitFE6View has no Untranslated literals in ja+zh ----

        [Fact]
        public void L10nCoverage_UnitFE6View_HasNoUntranslated()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches the project's actual l10n gate — ko.txt
            // does not exist in this repo; see Known Limitations).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("UnitFE6View.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "UnitFE6View.axaml has untranslated literals in ja+zh:\n" +
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
