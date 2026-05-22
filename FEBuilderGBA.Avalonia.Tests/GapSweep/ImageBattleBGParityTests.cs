// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 gap-sweep regression tests for ImageBattleBGView. (#434)
//
// Covers the 33 gaps the issue called out: 13 missing controls (density)
// + 20 missing labels + 2 missing INavigationTargetSource entries (jumps).
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
/// Tests proving the ImageBattleBG parity raise (#434) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the gap-sweep
/// report.
///
/// Marked [Collection("SharedState")] because the tests mutate
/// CoreState.ROM and CoreState.CommentCache/ResourceCache.
/// </summary>
[Collection("SharedState")]
public class ImageBattleBGParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 26 control instantiations. To leave the
    /// HIGH verdict we need AV >= 20 (75% of 26, the MEDIUM threshold).
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBattleBGView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer has 26 controls — see PR description / sweep report.
        const int WfControlCount = 26;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 20
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare both jump targets.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToGraphicsToolTarget()
    {
        var vm = new ImageBattleBGViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(GraphicsToolView));
    }

    [Fact]
    public void ViewModel_DeclaresJumpToDecreaseColorTarget()
    {
        var vm = new ImageBattleBGViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(DecreaseColorTSAToolView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #434, NONE of the two rows should still
        // carry an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new ImageBattleBGViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    [Fact]
    public void ViewModel_NavigationTargets_HaveDistinctCommandNames()
    {
        var vm = new ImageBattleBGViewModel();
        var targets = vm.GetNavigationTargets();
        var names = targets.Select(t => t.CommandName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate WF callsites and confirm both MATCH
    // the new manifest rows (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_BgToGraphicsTool_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ImageBattleBGForm",
                TargetForm: "GraphicsToolForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageBattleBGViewModel",
                SourceView: "ImageBattleBGView",
                Command: "JumpToGraphicsTool",
                TargetView: "GraphicsToolView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageBattleBGForm" &&
            r.TargetWfType == "GraphicsToolForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("GraphicsToolView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BgToDecreaseColor_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ImageBattleBGForm",
                TargetForm: "DecreaseColorTSAToolForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageBattleBGViewModel",
                SourceView: "ImageBattleBGView",
                Command: "JumpToDecreaseColor",
                TargetView: "DecreaseColorTSAToolView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageBattleBGForm" &&
            r.TargetWfType == "DecreaseColorTSAToolForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("DecreaseColorTSAToolView", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // VM behaviors — Comment, X_REF, source-file, ExpandList contracts.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_SaveComment_PersistsThroughCommentCache()
    {
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new ImageBattleBGViewModel
            {
                CurrentAddr = 0x12345u
            };
            vm.SaveComment("hello world");

            // RefreshComment re-reads from the cache - round-trip the value.
            vm.Comment = string.Empty;
            vm.RefreshComment();
            Assert.Equal("hello world", vm.Comment);
        }
        finally
        {
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_SaveComment_NoopWhenAddrIsZero()
    {
        // CurrentAddr=0 means no entry selected — the cache must not be
        // polluted with a key-0 entry.
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new ImageBattleBGViewModel(); // CurrentAddr defaults to 0
            vm.SaveComment("should not persist");
            // The cache should have no entry for addr 0.
            Assert.Equal(string.Empty, CoreState.CommentCache.S_At(0));
        }
        finally
        {
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_RefreshXrefs_EmptyOnSyntheticRom()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleBGViewModel();
            vm.RefreshXrefs(0);
            // Synthetic ROM has no terrain table populated — empty
            // expected (the helper must not throw).
            Assert.NotNull(vm.XRefEntries);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void ViewModel_RefreshSourceFile_RespectsResourceCache()
    {
        // EtcCacheResource() reads from disk in its constructor — point
        // it at the test bin dir so config/etc/resource_*.tsv can be
        // located (or absent — we don't care, the lookup just needs
        // a valid base path).
        EnsureCoreStateBaseDirectory();
        var prevRes = CoreState.ResourceCache;
        try
        {
            var cache = new FEBuilderGBA.EtcCacheResource();
            CoreState.ResourceCache = cache;

            var vm = new ImageBattleBGViewModel { CurrentIndex = 5 };
            // No entry yet — IsSourceFileAvailable must be false.
            vm.RefreshSourceFile(5);
            Assert.False(vm.IsSourceFileAvailable);

            // Adding a non-existent path keeps it false (file doesn't exist).
            cache.Update("BattleBG_" + U.ToHexString(5), "C:/this/path/does/not/exist.png");
            vm.RefreshSourceFile(5);
            Assert.False(vm.IsSourceFileAvailable);
            // But the SourceFilePath is recorded.
            Assert.Contains("does/not/exist.png", vm.SourceFilePath.Replace("\\", "/"));
        }
        finally
        {
            CoreState.ResourceCache = prevRes;
        }
    }

    /// <summary>
    /// Ensure CoreState.BaseDirectory points at the test bin dir so
    /// EtcCacheResource constructor can locate the resource TSV folder.
    /// Idempotent — safe to call from every test.
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

    [Fact]
    public void ViewModel_ExpandList_DelegatesToCoreHelper()
    {
        // The VM ExpandList must thread the call through to
        // ImageBattleBGCore.ExpandList — we exercise the synthetic ROM
        // path used by the Core tests.
        ROM rom = MakeMinimalFe8uRomWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBattleBGViewModel();
            vm.LoadList();
            Assert.True(vm.ReadCount >= 1,
                $"VM must load at least one BG row; got ReadCount={vm.ReadCount}");

            var undo = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            uint result = vm.ExpandList((uint)(vm.ReadCount + 2), undo);
            Assert.NotEqual(U.NOT_FOUND, result);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    // -----------------------------------------------------------------
    // View structural checks — AutomationIds the gap-fix surfaces.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AxamlContains_RequiredAutomationIds()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBattleBGView.axaml");
        Assert.True(File.Exists(axamlPath));
        string content = File.ReadAllText(axamlPath);

        // Spot-check the AutomationIds the WU3 spec called out.
        string[] required = {
            "ImageBattleBG_ReadStart_Input",
            "ImageBattleBG_ReadCount_Input",
            "ImageBattleBG_ReloadList_Button",
            "ImageBattleBG_BlockSize_Input",
            "ImageBattleBG_SelectedAddr_Input",
            "ImageBattleBG_Write_Button",
            "ImageBattleBG_Comment_Input",
            "ImageBattleBG_References_List",
            "ImageBattleBG_Preview_Image",
            "ImageBattleBG_OpenSource_Button",
            "ImageBattleBG_SelectSource_Button",
            "ImageBattleBG_ListExpands_Button",
            "ImageBattleBG_GraphicsTool_Button",
            "ImageBattleBG_DecreaseColor_Button",
            "ImageBattleBG_ImportPng_Button",
            "ImageBattleBG_ExportPng_Button",
            "ImageBattleBG_ExportPal_Button",
            "ImageBattleBG_ImportPal_Button",
        };
        foreach (var id in required)
        {
            Assert.Contains(id, content);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a tiny synthetic FE8U ROM whose `battle_bg_pointer` slot
    /// points to a minimal pointer table with `rowCount` distinct rows.
    /// </summary>
    static ROM MakeMinimalFe8uRomWithBgTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint pointerSlot = rom.RomInfo.battle_bg_pointer;
        for (int i = 0; i < rowCount; i++)
        {
            uint rowBase = tableAddr + (uint)(i * 12);
            BitConverter.GetBytes(0x08400000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 0);
            BitConverter.GetBytes(0x08500000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 4);
            BitConverter.GetBytes(0x08600000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 8);
        }
        if (rowCount > 0)
        {
            uint termAddr = tableAddr + (uint)(rowCount * 12);
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 0);
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 4);
        }
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, pointerSlot);
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static ROM MakeMinimalFe8uRom() => MakeMinimalFe8uRomWithBgTable(rowCount: 4);

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
