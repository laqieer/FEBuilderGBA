// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 gap-sweep regression tests for MapTerrainBGLookupTableView. (#441)
//
// Covers the 13 gaps the issue called out: 10 missing controls (density)
// + 3 missing INavigationTargetSource entries (jumps). The Floor sister
// editor's parity tests (MapTerrainFloorLookupTableParityTests, added in
// #482) cover the cross-view filter+row preservation contract from the
// Floor side; this file covers the BG side.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the BG LookupTable parity raise (#441) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
///
/// Marked [Collection("SharedState")] because the tests mutate
/// CoreState.ROM and CoreState.BaseDirectory — without serialization,
/// xUnit's per-class parallel runner can race a sibling test's ROM swap
/// between two LoadList calls.
/// </summary>
[Collection("SharedState")]
public class MapTerrainBGLookupTableParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 16 control instantiations. To leave the
    /// HIGH verdict we need AV >= 12 (75% of 16, the MEDIUM threshold).
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapTerrainBGLookupTableView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer has 16 controls — see PR description / sweep report.
        const int WfControlCount = 16;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 12
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all three callsites.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllThreeJumpTargets()
    {
        var vm = new MapTerrainBGLookupTableViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t => t.TargetViewType == typeof(MapTerrainFloorLookupTableView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(PatchManagerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(MapTerrainBGLookupTableView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #441, NONE of the three rows should still
        // carry an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new MapTerrainBGLookupTableViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate a WF callsite for PatchForm and confirm
    // it MATCHes the new manifest row (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_BGToPatchForm_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "MapTerrainBGLookupTableForm",
                TargetForm: "PatchForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "MapTerrainBGLookupTableViewModel",
                SourceView: "MapTerrainBGLookupTableView",
                Command: "JumpToPatchExtendsBattleBG",
                TargetView: "PatchManagerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "MapTerrainBGLookupTableForm" &&
            r.TargetWfType == "PatchForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("PatchManagerView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BGToFloor_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "MapTerrainBGLookupTableForm",
                TargetForm: "MapTerrainFloorLookupTableForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "MapTerrainBGLookupTableViewModel",
                SourceView: "MapTerrainBGLookupTableView",
                Command: "JumpToFloorLookup",
                TargetView: "MapTerrainFloorLookupTableView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "MapTerrainBGLookupTableForm" &&
            r.TargetWfType == "MapTerrainFloorLookupTableForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("MapTerrainFloorLookupTableView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BGToSelf_NowMatchesManifest()
    {
        // Mirrors WinForms `MapTerrainBGLookupTableForm.JumpToRef` static
        // self-jump — the deep-link entry point.
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "MapTerrainBGLookupTableForm",
                TargetForm: "MapTerrainBGLookupTableForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "MapTerrainBGLookupTableViewModel",
                SourceView: "MapTerrainBGLookupTableView",
                Command: "JumpToSelfFromRef",
                TargetView: "MapTerrainBGLookupTableView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "MapTerrainBGLookupTableForm" &&
            r.TargetWfType == "MapTerrainBGLookupTableForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("MapTerrainBGLookupTableView", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // ViewModel filter / patch-aware properties — mirror Floor parity.
    // -----------------------------------------------------------------

    [Fact]
    public void BgViewModel_LoadFilterEntries_PopulatesAtLeastVanillaCount()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTerrainBGLookupTableViewModel();
            vm.LoadFilterEntries();
            // Vanilla FE8U has 21 BG pointer slots. The list builder must
            // include at least the vanilla count (extends patch is not
            // installed in the synthetic ROM).
            Assert.True(vm.FilterEntries.Count >= 21,
                $"Expected >= 21 filter entries (vanilla FE8U), got {vm.FilterEntries.Count}");
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void BgViewModel_LoadList_NonZeroFilter_PicksDifferentBase()
    {
        // Same synthetic ROM technique #482 used for Floor: plant pointers in
        // slot 0 + slot 1 that resolve to two distinct in-ROM addresses, and
        // verify LoadList(filterIndex) follows the correct pointer.
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTerrainBGLookupTableViewModel();
            vm.LoadFilterEntries();

            var list0 = vm.LoadList(0);
            var list1 = vm.LoadList(1);
            Assert.NotEmpty(list0);
            Assert.NotEmpty(list1);
            Assert.NotEqual(list0[0].addr, list1[0].addr);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void BgViewModel_LoadList_UpdatesReadStartAddressAndCount()
    {
        // Mirror Floor's contract: LoadList(filterIndex) must update
        // ReadStartAddress and ReadCount so the new display widgets reflect
        // the active filter slot.
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTerrainBGLookupTableViewModel();
            vm.LoadFilterEntries();

            var list = vm.LoadList(0);
            Assert.NotEmpty(list);
            Assert.NotEqual(0u, vm.ReadStartAddress);
            Assert.True(vm.ReadCount > 0, $"ReadCount should reflect map_terrain_type_count, got {vm.ReadCount}");
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    // -----------------------------------------------------------------
    // JumpToRef static entry — parse a `filter:row` reference text.
    // -----------------------------------------------------------------

    [Fact]
    public void View_JumpToRef_RejectsMalformedInput()
    {
        // Mirror WinForms `MapTerrainBGLookupTableForm.JumpToRef` early-return
        // semantics: when the regex pattern doesn't match (yields <= 2 parts),
        // the helper must return null without opening any window.
        var result = MapTerrainBGLookupTableView.JumpToRef("not a real reference");
        Assert.Null(result);
    }

    [Fact]
    public void View_JumpToRef_NullInput_DoesNotThrow()
    {
        var result = MapTerrainBGLookupTableView.JumpToRef(null!);
        Assert.Null(result);
    }

    /// <summary>
    /// Regression test for Copilot CLI inline review point on PR #491:
    /// the BG view must expose a `_navigationApplied` short-circuit flag so
    /// that `InitialLoad` (registered to `Opened` in the constructor) does
    /// NOT clobber a deep-link applied by `NavigateToFilterAndRow` before
    /// `Opened` fired. Verified by reflection — the production code path is
    /// otherwise hard to exercise without a real Avalonia head.
    /// </summary>
    [Fact]
    public void View_HasNavigationAppliedShortCircuitField()
    {
        var fieldInfo = typeof(MapTerrainBGLookupTableView).GetField(
            "_navigationApplied",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(fieldInfo);
        Assert.Equal(typeof(bool), fieldInfo!.FieldType);
    }

    /// <summary>
    /// Ensure CoreState.BaseDirectory points at the test bin dir so
    /// MapTerrainLookupCore.GetTerrainSetDic can locate
    /// config/data/battleterrain_set_*.txt. The Avalonia test project
    /// copies the repo's config/ into its bin output via the .csproj
    /// Content Include rule; we just need to tell CoreState where to find
    /// it. Idempotent — safe to call from every test (no side-effect
    /// leakage across collections because we only write when null/empty).
    /// </summary>
    static void EnsureCoreStateBaseDirectory()
    {
        if (!string.IsNullOrEmpty(CoreState.BaseDirectory))
            return;
        string? assemblyDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (assemblyDir != null)
            CoreState.BaseDirectory = assemblyDir;
    }

    /// <summary>
    /// Build a tiny synthetic FE8U ROM whose two BG-lookup pointer slots
    /// (filter 0 + filter 1) point to DIFFERENT in-ROM addresses, so the
    /// LoadList(int) overload can be observed to follow the filterIndex.
    /// </summary>
    static ROM MakeMinimalFe8uRom()
    {
        EnsureCoreStateBaseDirectory();
        var bytes = new byte[0x1100000];

        // FE8U lookup_table_battle_bg_00_pointer is at offset 0x57FF4 and
        // _01_pointer is at 0x57F48 (see ROMFE8U.cs). We plant GBA pointers
        // (with the 0x08000000 base) at those slots that resolve to two
        // distinct safe in-ROM addresses with usable per-entry data.
        uint slot0Target = 0x00800000u;
        uint slot1Target = 0x00900000u;
        BitConverter.GetBytes(slot0Target | 0x08000000u).CopyTo(bytes, 0x57FF4);
        BitConverter.GetBytes(slot1Target | 0x08000000u).CopyTo(bytes, 0x57F48);

        // Mark each slot's first byte differently so the test can verify
        // it followed the right pointer.
        bytes[slot0Target] = 0xA0;
        bytes[slot1Target] = 0xB1;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Walk parent directories from the test bin/ folder until we find the
    /// repo root (identified by FEBuilderGBA.sln).
    /// </summary>
    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
