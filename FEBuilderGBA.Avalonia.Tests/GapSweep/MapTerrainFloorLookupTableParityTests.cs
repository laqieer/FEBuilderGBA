// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 gap-sweep regression tests for MapTerrainFloorLookupTableView. (#442)
//
// Covers the 13 gaps the issue called out: 10 missing controls (density)
// + 3 missing INavigationTargetSource entries (jumps). Tests are kept
// headless — no real ROM file required for the density / manifest / scanner
// assertions. The cross-Layer-1b scanner test exercises the new
// ListParityHelper.GetExtraCrossViewMappings hook.
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
/// Tests proving the Floor LookupTable parity raise (#442) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
///
/// Marked [Collection("SharedState")] (#441) because the tests mutate
/// CoreState.ROM and CoreState.BaseDirectory — without serialization,
/// xUnit's per-class parallel runner can race a sibling test's ROM swap
/// between two LoadList calls in the same test, producing "Expected: 65,
/// Actual: 0" mismatches.
/// </summary>
[Collection("SharedState")]
public class MapTerrainFloorLookupTableParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 16 control instantiations. To leave the
    /// HIGH verdict we need AV ≥ 12 (75% of 16, the MEDIUM threshold).
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        // Pull the AXAML file from the live worktree — same source the
        // ControlDensityScanner reads.
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapTerrainFloorLookupTableView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer has 16 controls — see PR description / sweep report.
        const int WfControlCount = 16;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 12
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be ≥ {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all three callsites.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllThreeJumpTargets()
    {
        var vm = new MapTerrainFloorLookupTableViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t => t.TargetViewType == typeof(MapTerrainBGLookupTableView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(PatchManagerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(MapTerrainFloorLookupTableView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #442, NONE of the three rows should still
        // carry an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new MapTerrainFloorLookupTableViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Scanner cross-ref (Layer 1b) — PatchForm ↔ PatchManagerView mapping.
    // Without this hook, the PatchForm callsite is unresolved on the AV side.
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_KnownExtraCrossViewMappings_HasPatchPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("PatchManagerView"),
            "PatchManagerView ↔ PatchForm must be declared in ListParityHelper.GetExtraCrossViewMappings");
        Assert.Equal("PatchForm", extras["PatchManagerView"]);
    }

    [Fact]
    public void JumpParityScanner_WfFormToAvViewsMap_IncludesPatchManagerViewForPatchForm()
    {
        // No repo root → scanner skips PairMatcher layer but the extra-mappings
        // (Layer 1b) still apply. This isolates the test from the file system.
        var map = JumpParityScanner.BuildWfFormToAvViewsMap();
        Assert.True(map.ContainsKey("PatchForm"),
            "PatchForm must be present in WF→AV map after extra-mapping layer kicks in");
        Assert.Contains("PatchManagerView", map["PatchForm"]);
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate a WF callsite for PatchForm and confirm
    // it MATCHes the new manifest row (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_FloorToPatchForm_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "MapTerrainFloorLookupTableForm",
                TargetForm: "PatchForm",
                HasAddressArgument: false),
        };

        // Build the AV-manifest list manually so the test stays headless and
        // independent of which other VMs implement INavigationTargetSource —
        // we only care about the specific row we just added.
        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "MapTerrainFloorLookupTableViewModel",
                SourceView: "MapTerrainFloorLookupTableView",
                Command: "JumpToPatchExtendsBattleBG",
                TargetView: "PatchManagerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "MapTerrainFloorLookupTableForm" &&
            r.TargetWfType == "PatchForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("PatchManagerView", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // Filter+row preservation: NavigateToFilterAndRow must use the right
    // base pointer when filterIndex > 0 (Copilot CLI review point 2).
    // -----------------------------------------------------------------

    [Fact]
    public void BgViewModel_LoadList_NonZeroFilter_SwapsBasePointer()
    {
        // Synthesize a tiny FE8U ROM where bg-pointer 0 and bg-pointer 1
        // differ; the LoadList(filterIndex) overload must follow filter 1.
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTerrainBGLookupTableViewModel();

            var list0 = vm.LoadList(0);
            var list1 = vm.LoadList(1);
            // The base pointers for vanilla filter 0 vs filter 1 resolve to
            // different ROM addresses on a properly-laid-out FE8U; the same
            // synthetic ROM here just makes sure the overload is wired —
            // both lists are non-empty and start at different addresses.
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
    public void BgViewModel_LoadList_NoFilterArg_DefaultsToZero()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapTerrainBGLookupTableViewModel();

            var defaultList = vm.LoadList();
            var zeroList = vm.LoadList(0);
            Assert.Equal(defaultList.Count, zeroList.Count);
            if (defaultList.Count > 0)
            {
                Assert.Equal(defaultList[0].addr, zeroList[0].addr);
            }
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    /// <summary>
    /// Ensure CoreState.BaseDirectory points at the test bin dir so
    /// MapTerrainLookupCore.GetTerrainSetDic can locate
    /// config/data/battleterrain_set_*.txt. The Avalonia test project copies
    /// the repo's config/ into its bin output via Content Include; we just
    /// need to tell CoreState where to find it. Idempotent — safe across
    /// test collections (only writes when null/empty). #441 widened the BG
    /// VM's LoadList path to chain through LoadFilterEntries, which calls
    /// GetTerrainSetDic — so the BG-VM tests (previously path-free) now also
    /// need this guard.
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
        // it followed the right pointer when needed.
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
    /// repo root (identified by FEBuilderGBA.sln). Mirrors the pattern used
    /// in JumpParityScannerTests and friends.
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
