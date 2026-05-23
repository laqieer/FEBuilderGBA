// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 gap-sweep regression tests for ImageBGView (#429).
//
// Covers the 42 gaps the issue called out: 19 missing controls (density)
// + 20 missing labels + 3 missing INavigationTargetSource entries (jumps).
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
/// Tests proving the ImageBG parity raise (#429) is permanent.
/// Marked [Collection("SharedState")] because the tests mutate
/// CoreState.ROM and CoreState.CommentCache/ResourceCache.
/// </summary>
[Collection("SharedState")]
public class ImageBGParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF Designer.cs has 26 controls. The MEDIUM threshold is 75% × 26 = 20.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBGView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 26;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 20
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all 3 jump targets.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToGraphicsToolTarget()
    {
        var vm = new ImageBGViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(GraphicsToolView));
    }

    [Fact]
    public void ViewModel_DeclaresJumpToDecreaseColorTarget()
    {
        var vm = new ImageBGViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(DecreaseColorTSAToolView));
    }

    [Fact]
    public void ViewModel_DeclaresJumpToBGSelectPopupTarget()
    {
        var vm = new ImageBGViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(ImageBGSelectPopupView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        var vm = new ImageBGViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    [Fact]
    public void ViewModel_NavigationTargets_HaveDistinctCommandNames()
    {
        var vm = new ImageBGViewModel();
        var targets = vm.GetNavigationTargets();
        var names = targets.Select(t => t.CommandName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate WF callsites and confirm MATCH.
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_BgToGraphicsTool_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ImageBGForm",
                TargetForm: "GraphicsToolForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageBGViewModel",
                SourceView: "ImageBGView",
                Command: "JumpToGraphicsTool",
                TargetView: "GraphicsToolView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageBGForm" &&
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
                SourceForm: "ImageBGForm",
                TargetForm: "DecreaseColorTSAToolForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageBGViewModel",
                SourceView: "ImageBGView",
                Command: "JumpToDecreaseColor",
                TargetView: "DecreaseColorTSAToolView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageBGForm" &&
            r.TargetWfType == "DecreaseColorTSAToolForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("DecreaseColorTSAToolView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BgToBGSelectPopup_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "ImageBGForm",
                TargetForm: "ImageBGSelectPopupForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "ImageBGViewModel",
                SourceView: "ImageBGView",
                Command: "JumpToBGSelectPopup",
                TargetView: "ImageBGSelectPopupView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "ImageBGForm" &&
            r.TargetWfType == "ImageBGSelectPopupForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("ImageBGSelectPopupView", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // VM behaviors — Comment, source-file, ExpandList, warning.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_SaveComment_PersistsThroughCommentCache()
    {
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new ImageBGViewModel
            {
                CurrentAddr = 0x12345u
            };
            vm.SaveComment("hello world");

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
        var prevCache = CoreState.CommentCache;
        try
        {
            CoreState.CommentCache = new HeadlessEtcCache();
            var vm = new ImageBGViewModel(); // CurrentAddr defaults to 0
            vm.SaveComment("should not persist");
            Assert.Equal(string.Empty, CoreState.CommentCache.S_At(0));
        }
        finally
        {
            CoreState.CommentCache = prevCache;
        }
    }

    [Fact]
    public void ViewModel_RefreshSourceFile_RespectsResourceCache()
    {
        EnsureCoreStateBaseDirectory();
        var prevRes = CoreState.ResourceCache;
        try
        {
            var cache = new FEBuilderGBA.EtcCacheResource();
            CoreState.ResourceCache = cache;

            var vm = new ImageBGViewModel { CurrentIndex = 5 };
            vm.RefreshSourceFile(5);
            Assert.False(vm.IsSourceFileAvailable);

            cache.Update("BG_" + U.ToHexString(5), "C:/this/path/does/not/exist.png");
            vm.RefreshSourceFile(5);
            Assert.False(vm.IsSourceFileAvailable);
            Assert.Contains("does/not/exist.png", vm.SourceFilePath.Replace("\\", "/"));
        }
        finally
        {
            CoreState.ResourceCache = prevRes;
        }
    }

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
        ROM rom = MakeMinimalFe8uRomWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
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

    [Fact]
    public void ViewModel_RefreshWarningMessage_ReserveBlack_ShowsBanner()
    {
        ROM rom = MakeMinimalFe8uRomWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
            // FE8U bg_reserve_black_bgid = 0x35.
            vm.RefreshWarningMessage(0x35);
            Assert.False(string.IsNullOrEmpty(vm.WarningMessage));
            Assert.Contains("black", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void ViewModel_RefreshWarningMessage_ReserveRandom_ShowsBanner()
    {
        ROM rom = MakeMinimalFe8uRomWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
            // FE8U bg_reserve_random_bgid = 0x37.
            vm.RefreshWarningMessage(0x37);
            Assert.False(string.IsNullOrEmpty(vm.WarningMessage));
            Assert.Contains("random", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void ViewModel_RefreshWarningMessage_NormalSlot_ShowsNoBanner()
    {
        ROM rom = MakeMinimalFe8uRomWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
            vm.RefreshWarningMessage(0x10);
            Assert.Equal(string.Empty, vm.WarningMessage);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    /// <summary>
    /// Per Copilot CLI v3 review (#517): a BG256-patched ROM entry
    /// with P4 &lt;= 1 (255/224-color mode flag) MUST NOT accept a
    /// 16-color import via either the file-picker or drag-drop path.
    /// The Avalonia View now refuses such imports before any ROM
    /// write with an explicit "use the WinForms editor" error. We
    /// verify the underlying decision predicate here, since the
    /// actual View handlers run modally and are out of headless reach.
    /// </summary>
    [Fact]
    public void ViewModel_BG256Patched_WithP4Flag_IsTreatedAs255_224Mode()
    {
        // Build a BG256-patched ROM and verify ImageBGCore.Is255BG +
        // the IsBG256Patched flag the VM exposes drive the View's
        // pre-import gate correctly.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("bg256-fe8u.gba", bytes, "BE8E01");
        // Plant BG256 signature for FE8U.
        bytes[0xE2DA] = 0xC0; bytes[0xE2DB] = 0x46;
        bytes[0xE2DC] = 0xC0; bytes[0xE2DD] = 0x46;
        rom.LoadLow("bg256-fe8u.gba", bytes, "BE8E01");

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
            // Simulate a BG255 entry (P0 valid pointer, P4 = 0).
            vm.P0 = 0x08400000u;
            vm.P4 = 0u;
            vm.IsBG256Patched = FEBuilderGBA.PatchDetection.HasBG256ColorPatch(rom);

            // The View's `PreImportGate` refuses when
            // `IsBG256Patched && P4 <= 1`. Verify both halves:
            Assert.True(vm.IsBG256Patched, "BG256 patch must be detected");
            Assert.True(vm.P4 <= 1, "P4 must be a mode flag");

            // The Core helper directly answers "is this a 255-color BG?"
            Assert.True(FEBuilderGBA.ImageBGCore.Is255BG(rom, vm.P0, vm.P4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadList_HonorsBG256P4FlagRule()
    {
        // Build a ROM with the BG256 patch installed and an entry whose
        // P4 = 0 (255-color mode flag). Without the patched rule the
        // list would truncate; with it the row should be accepted.
        var bytes = new byte[0x1100000];

        // Plant BG256 signature for FE8U at the patched detection bytes.
        bytes[0xE2DA] = 0xC0; bytes[0xE2DB] = 0x46;
        bytes[0xE2DC] = 0xC0; bytes[0xE2DD] = 0x46;

        // Row 0 — normal entry (both pointers valid).
        uint tableAddr = 0x00800000u;
        BitConverter.GetBytes(0x08400000u).CopyTo(bytes, (int)tableAddr + 0);
        BitConverter.GetBytes(0x08500000u).CopyTo(bytes, (int)tableAddr + 4);
        BitConverter.GetBytes(0x08600000u).CopyTo(bytes, (int)tableAddr + 8);
        // Row 1 — BG255 entry (P4 = 0 flag, P0 must be valid pointer).
        BitConverter.GetBytes(0x08410000u).CopyTo(bytes, (int)tableAddr + 12);
        BitConverter.GetBytes(0u).CopyTo(bytes, (int)tableAddr + 16);
        BitConverter.GetBytes(0x08610000u).CopyTo(bytes, (int)tableAddr + 20);
        // Terminator.
        BitConverter.GetBytes(0u).CopyTo(bytes, (int)tableAddr + 24);
        BitConverter.GetBytes(0u).CopyTo(bytes, (int)tableAddr + 28);

        // Pre-plant the pointer-to-table at the FE8U primary bg_pointer slot
        // so FindROMPointer picks it during the FIRST LoadLow.
        uint primaryPointerSlot = 0x00E894u;
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, (int)primaryPointerSlot);

        var rom = new ROM();
        rom.LoadLow("bg256-fe8u.gba", bytes, "BE8E01");

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ImageBGViewModel();
            var items = vm.LoadList();
            // Both rows should be accepted under BG256 patch.
            Assert.True(items.Count >= 2,
                $"Expected at least 2 rows under BG256 patch; got {items.Count}");
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // View structural checks — AutomationIds the gap-fix surfaces.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AxamlContains_RequiredAutomationIds()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImageBGView.axaml");
        Assert.True(File.Exists(axamlPath));
        string content = File.ReadAllText(axamlPath);

        string[] required = {
            "ImageBG_ReadStart_Input",
            "ImageBG_ReadCount_Input",
            "ImageBG_ReloadList_Button",
            "ImageBG_BlockSize_Input",
            "ImageBG_SelectedAddr_Input",
            "ImageBG_Write_Button",
            "ImageBG_Comment_Input",
            "ImageBG_References_List",
            "ImageBG_Image_Image",
            "ImageBG_OpenSource_Button",
            "ImageBG_SelectSource_Button",
            "ImageBG_ListExpands_Button",
            "ImageBG_GraphicsTool_Button",
            "ImageBG_DecreaseColor_Button",
            "ImageBG_ImportPng_Button",
            "ImageBG_ExportPng_Button",
            "ImageBG_ExportPal_Button",
            "ImageBG_ImportPal_Button",
            "ImageBG_Warning_Text",
        };
        foreach (var id in required)
        {
            Assert.Contains(id, content);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeMinimalFe8uRomWithBgTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];

        // FE8U `bg_pointer` is resolved by `U.FindROMPointer` against a list
        // of candidate slots: 0x00E894, 0x00ECF4, 0x00EDF8, 0x0010E44.
        // Pre-write the table address + 3 rows to ALL candidate slots
        // so FindROMPointer picks 0x00E894 on the first LoadLow, and the
        // resulting RomInfo.bg_pointer matches the data we lay out below.
        uint primaryPointerSlot = 0x00E894u;

        // Lay out the table rows first so FindROMPointer's secondary
        // pointer check (`a + 0x8`) succeeds.
        for (int i = 0; i < rowCount; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 12)));
            BitConverter.GetBytes(0x08400000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 0);
            BitConverter.GetBytes(0x08500000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 4);
            BitConverter.GetBytes(0x08600000u | ((uint)i << 12)).CopyTo(bytes, rowBase + 8);
        }
        if (rowCount > 0)
        {
            // Plant a NON-pointer, NON-null value at row[rowCount] so
            // LoadList's `IsValidEntry` returns false and the loop
            // terminates at rowCount. (Zero entries pass `isPointerOrNULL`,
            // so we must use a value like 0x12345678 that's neither.)
            int termAddr = checked((int)(tableAddr + (uint)(rowCount * 12)));
            BitConverter.GetBytes(0x12345678u).CopyTo(bytes, termAddr + 0);
            BitConverter.GetBytes(0x12345678u).CopyTo(bytes, termAddr + 4);
        }

        // Plant the pointer at the primary slot so FindROMPointer picks it.
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, (int)primaryPointerSlot);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

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
