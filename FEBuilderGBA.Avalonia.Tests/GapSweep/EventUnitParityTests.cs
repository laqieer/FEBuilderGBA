// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 / Phase 4 / Phase 5 gap-sweep regression tests for
// EventUnitView (closes #420 via this PR).
//
// Covers the 56 gaps the issue called out:
//   - 50 missing WF-only labels (density / labels)
//   - 6 missing INavigationTargetSource manifest entries (jumps)
//
// The tests assert STABLE, HEADLESS contracts (XAML doc, VM reflection,
// manifest entries) so CI stays cross-platform.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class EventUnitParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must clear the MEDIUM verdict.
    //
    // 2026-05-22 sweep reported WF=95 / AV=54. The MEDIUM threshold is
    // ceil(0.75 * 95) = 72. After the new view design adds the Top
    // read-config bar, B3+W4 sub-panels, After-coords display, Address
    // bar, Comment, jump panel + dialog launcher, and Random Monster
    // button, the new AV count should be well above 72.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-22 density sweep — see issue #420.
        const int WfControlCount = 95;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 72
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) — all six jumps registered.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllSixJumpTargets()
    {
        var vm = new EventUnitViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Equal(6, targets.Count);
        Assert.Contains(targets, t => t.TargetViewType == typeof(EventBattleTalkView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(SoundBossBGMViewerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(EventHaikuView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(EventUnitNewAllocView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(EventUnitItemDropView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(MonsterProbabilityViewerView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #420, NONE of the six rows should still
        // carry an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new EventUnitViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate the six WF callsites and confirm
    // they MATCH the new manifest rows (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_BattleTalk_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "EventBattleTalkForm",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToBattleTalk",
                TargetView: "EventBattleTalkView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "EventBattleTalkForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventBattleTalkView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BattleBGM_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "SoundBossBGMForm",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToBattleBGM",
                TargetView: "SoundBossBGMViewerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "SoundBossBGMForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("SoundBossBGMViewerView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_Haiku_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "EventHaikuForm",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToHaiku",
                TargetView: "EventHaikuView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "EventHaikuForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventHaikuView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_NewAlloc_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "EventUnitNewAllocForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToNewAlloc",
                TargetView: "EventUnitNewAllocView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "EventUnitNewAllocForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventUnitNewAllocView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_ItemDrop_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "EventUnitItemDropForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToItemDrop",
                TargetView: "EventUnitItemDropView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "EventUnitItemDropForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventUnitItemDropView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_MonsterProbability_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitForm",
                TargetForm: "MonsterProbabilityForm",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitViewModel",
                SourceView: "EventUnitView",
                Command: "JumpToMonsterProbability",
                TargetView: "MonsterProbabilityViewerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitForm" &&
            r.TargetWfType == "MonsterProbabilityForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("MonsterProbabilityViewerView", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // ViewModel B3 round-trip — UnitInfoLV/Allegiance/Grow must be a
    // lossless decomposition of the raw UnitInfo byte.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_UnitInfoLV_DecomposesAndComposes()
    {
        var vm = new EventUnitViewModel();
        // Sample bit pattern: lv=20, assign=2 (Enemy), grow=1.
        // 1 | (2<<1) | (20<<3) = 1 | 4 | 160 = 0xA5.
        vm.UnitInfo = 0xA5;
        Assert.Equal(20u, vm.UnitInfoLV);
        Assert.Equal(2u, vm.UnitInfoAllegiance);
        Assert.Equal(1u, vm.UnitInfoGrow);

        // Setting LV alone preserves the other fields.
        vm.UnitInfoLV = 5;
        // 1 | (2<<1) | (5<<3) = 1 | 4 | 40 = 0x2D
        Assert.Equal(0x2Du, vm.UnitInfo);
        Assert.Equal(2u, vm.UnitInfoAllegiance);
        Assert.Equal(1u, vm.UnitInfoGrow);
    }

    [Fact]
    public void ViewModel_UnitInfoAllegiance_AllValuesRoundTrip()
    {
        var vm = new EventUnitViewModel();
        for (uint allegiance = 0; allegiance < 4; allegiance++)
        {
            vm.UnitInfo = 0;
            vm.UnitInfoAllegiance = allegiance;
            Assert.Equal(allegiance, vm.UnitInfoAllegiance);
        }
    }

    // -----------------------------------------------------------------
    // ViewModel W4 round-trip — BeforeX/BeforeY/UnitPosExt must be a
    // lossless decomposition of UnitGrowth.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_W4_DecomposesAndComposes()
    {
        var vm = new EventUnitViewModel();
        // Sample W4: X=5, Y=10, Ext=2 (Item Drop).
        // 5 | (10 << 6) | (2 << 12) = 5 | 640 | 8192 = 0x2285.
        vm.UnitGrowth = 0x2285;
        Assert.Equal(5u, vm.BeforeX);
        Assert.Equal(10u, vm.BeforeY);
        Assert.Equal(2u, vm.UnitPosExt);
        Assert.True(vm.ItemDropFlag);

        // Setting BeforeX alone preserves the others.
        vm.BeforeX = 7;
        Assert.Equal(7u, vm.BeforeX);
        Assert.Equal(10u, vm.BeforeY);
        Assert.Equal(2u, vm.UnitPosExt);
        Assert.True(vm.ItemDropFlag);
    }

    [Fact]
    public void ViewModel_ItemDropFlag_TogglesW4ExtBit2()
    {
        var vm = new EventUnitViewModel();
        // Start with ext=0 (no drop).
        vm.UnitGrowth = 0;
        Assert.False(vm.ItemDropFlag);

        vm.ItemDropFlag = true;
        Assert.True(vm.ItemDropFlag);
        Assert.Equal(2u, vm.UnitPosExt);
        Assert.Equal(2u << 12, vm.UnitGrowth);

        vm.ItemDropFlag = false;
        Assert.False(vm.ItemDropFlag);
        Assert.Equal(0u, vm.UnitPosExt);
    }

    [Fact]
    public void ViewModel_ItemDropDisplay_ChangesWithW4ExtBit()
    {
        var vm = new EventUnitViewModel();
        // No drop bit -> R._("Item Drop: doesn't drop")
        vm.UnitGrowth = 0;
        string noDropDisplay = vm.ItemDropDisplay;

        // Set drop bit -> R._("Item Drop: drops")
        vm.UnitGrowth = (2u << 12);
        string dropDisplay = vm.ItemDropDisplay;

        // Both must match the localized R._-resolved strings (so the test
        // stays green under ja/zh translations too — Copilot bot review
        // round 2 #3).
        Assert.Equal(R._("Item Drop: doesn't drop"), noDropDisplay);
        Assert.Equal(R._("Item Drop: drops"), dropDisplay);
        // The two states MUST produce different strings (regression guard).
        Assert.NotEqual(noDropDisplay, dropDisplay);
    }

    // -----------------------------------------------------------------
    // ViewModel Comment must round-trip through CoreState.CommentCache.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_Comment_RoundTripsThroughCommentCache()
    {
        if (CoreState.CommentCache == null)
        {
            CoreState.CommentCache = new HeadlessEtcCache();
        }

        var vm = new EventUnitViewModel();
        const uint addr = 0x00BEEF00u;

        // Seed the cache.
        CoreState.CommentCache.Update(addr, "eventunit test comment");

        // Build a synthetic ROM so LoadEntry can run without crashing.
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            vm.LoadEntry(addr);
            Assert.Equal("eventunit test comment", vm.Comment);

            // Round-trip: mutate then write, then re-load.
            vm.Comment = "new value";
            vm.WriteEntry();
            vm.Comment = ""; // simulate fresh load
            vm.LoadEntry(addr);
            Assert.Equal("new value", vm.Comment);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Undo coverage — the View's Write_Click + ExpandList_Click + W4
    // ItemDrop checkbox must open and commit UndoService scopes.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteClick_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        Assert.True(File.Exists(codeBehindPath), $"code-behind not found at {codeBehindPath}");

        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Begin\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Commit\(\)", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+Write_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Rollback\(\)", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void View_ExpandListClick_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(
            new Regex(@"void\s+ExpandList_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Begin\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+ExpandList_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Commit\(\)", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"void\s+ExpandList_Click\([^)]*\)\s*\{[\s\S]*?_undoService\.Rollback\(\)", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void View_ExpandListClick_InvokesVmExpandHelper()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(
            new Regex(@"void\s+ExpandList_Click\([^)]*\)\s*\{[\s\S]*?_vm\.ExpandUnitListCurrent\(", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void ViewModel_ExpandUnitListCurrent_UsesFe8StarterByte()
    {
        // The VM's ExpandUnitListCurrent must invoke
        // MapEventUnitCore.ExpandUnitList with starterB1=0x02 (FE8
        // semantics) per WF EventUnitForm.AddressListExpandsEvent.
        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "EventUnitViewModel.cs");
        string source = File.ReadAllText(vmPath);

        // Look for the call: ExpandUnitList(...starterB1: 0x02)
        Assert.Matches(
            new Regex(@"MapEventUnitCore\.ExpandUnitList\([\s\S]*?starterB1:\s*0x02", RegexOptions.Singleline),
            source);
    }

    // -----------------------------------------------------------------
    // AutomationId presence — the new controls must have stable test ids
    // so MCP / UIAutomation can target them for end-to-end validation.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("EventUnit_TopAddr_Input")]
    [InlineData("EventUnit_ReadCount_Input")]
    [InlineData("EventUnit_ReloadList_Button")]
    [InlineData("EventUnit_NewAlloc_Button")]
    [InlineData("EventUnit_ExpandList_Button")]
    [InlineData("EventUnit_BlockSize_Input")]
    [InlineData("EventUnit_SelectedAddr_Input")]
    [InlineData("EventUnit_Comment_Input")]
    [InlineData("EventUnit_LV_Input")]
    [InlineData("EventUnit_Allegiance_Combo")]
    [InlineData("EventUnit_GrowthRate_Combo")]
    [InlineData("EventUnit_BeforeX_Input")]
    [InlineData("EventUnit_BeforeY_Input")]
    [InlineData("EventUnit_ItemDropFlag_Check")]
    [InlineData("EventUnit_JumpBattleTalk_Button")]
    [InlineData("EventUnit_JumpBattleBGM_Button")]
    [InlineData("EventUnit_JumpHaiku_Button")]
    [InlineData("EventUnit_ItemDropDialog_Button")]
    [InlineData("EventUnit_JumpMonsterProb_Button")]
    [InlineData("EventUnit_ItemDrop_Label")]
    public void View_DeclaresExpectedAutomationId(string automationId)
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
        string content = File.ReadAllText(axamlPath);
        Assert.Contains(automationId, content);
    }

    // -----------------------------------------------------------------
    // New(Alloc) — #776 WF-parity reserved-NEW block + modal count-picker.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_NewAllocUnitList_AllocatesEditableBlock()
    {
        // VM NewAllocUnitList(count, undo) must allocate a real block via the
        // shared Core seam (freespace fallback in headless tests) and return a
        // valid base whose first row is a valid (B0=1) starter row.
        var vm = new EventUnitViewModel();
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null; // headless freespace fallback

            uint newBase = vm.NewAllocUnitList(3, null);
            Assert.NotEqual(U.NOT_FOUND, newBase);
            // 3 rows, each B0 == 1; trailing terminator byte == 0.
            Assert.Equal((byte)0x01, rom.Data[newBase + 0]);
            Assert.Equal((byte)0x01, rom.Data[newBase + 20]);
            Assert.Equal((byte)0x01, rom.Data[newBase + 40]);
            Assert.Equal((byte)0x00, rom.Data[newBase + 60]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void ViewModel_NewAllocUnitList_CountZero_IsNoOp()
    {
        // Cancel / count==0 path: the VM helper must not allocate.
        var vm = new EventUnitViewModel();
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null;

            uint searchStart = (uint)(rom.Data.Length / 2);
            byte[] before = rom.getBinaryData(searchStart, 256);

            uint result = vm.NewAllocUnitList(0, null);
            Assert.Equal(U.NOT_FOUND, result);

            byte[] after = rom.getBinaryData(searchStart, 256);
            Assert.Equal(before, after);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void View_NewAllocClick_OpensModalPicker_AndGuardsCancelCountZero()
    {
        // The View's NewAlloc_Click must open the modal count-picker via
        // OpenModal<EventUnitNewAllocView, uint?> and early-return on Cancel (null) or count==0.
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // Opens the modal picker and awaits a uint? count.
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?OpenModal<EventUnitNewAllocView, uint\?>", RegexOptions.Singleline),
            source);
        // Cancel / count==0 no-op guard.
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?count\s*==\s*null\s*\|\|\s*count\.Value\s*==\s*0[\s\S]*?return", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void View_NewAllocClick_WrapsInUndoScope_AndCallsVmAllocator()
    {
        // NewAlloc_Click must open an undo scope, call the VM allocator,
        // rollback on U.NOT_FOUND, and commit on success (mirrors WF + the
        // ExpandList_Click discipline).
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?_undoService\.Begin\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?_vm\.NewAllocUnitList\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?U\.NOT_FOUND[\s\S]*?_undoService\.Rollback\(\)", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?_undoService\.Commit\(\)", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void View_NewAllocClick_AddsNewEntryThatSurvivesMapRefresh()
    {
        // In-RAM NEW tracking (WF NewAllocData parity): the View must keep a
        // session list (_newAllocData), add a "NEW" AddrResult to it on alloc,
        // and re-merge it on map/group refresh in MapListBox_SelectionChanged
        // (the AppendNoWriteNewData survival contract).
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // A session list field exists.
        Assert.Matches(new Regex(@"_newAllocData"), source);
        // NewAlloc adds a "NEW" AddrResult to the session list.
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?new\s+AddrResult\([^)]*""NEW""[^)]*\)[\s\S]*?_newAllocData\.Add", RegexOptions.Singleline),
            source);
        // Map selection re-merges the session NEW allocations.
        Assert.Matches(
            new Regex(@"MapListBox_SelectionChanged[\s\S]*?MergeNewAllocData\(", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void NewAllocView_IsModalCountPicker_WithFiftyCap()
    {
        // The restructured EventUnitNewAllocView must be a modal count-picker
        // (NumericUpDown Min=1/Max=50/Value=1 + OK/Cancel), NOT the old
        // address-list viewer.
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitNewAllocView.axaml");
        string content = File.ReadAllText(axamlPath);

        Assert.Contains("EventUnitNewAlloc_Count_Input", content);
        Assert.Contains("Maximum=\"50\"", content);
        Assert.Contains("Minimum=\"1\"", content);
        Assert.Contains("EventUnitNewAlloc_OK_Button", content);
        Assert.Contains("EventUnitNewAlloc_Cancel_Button", content);
        // The old display-only address list must be gone.
        Assert.DoesNotContain("AddressListControl", content);
    }

    [Fact]
    public void NewAllocView_CodeBehind_ClosesWithCountOrNull()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitNewAllocView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // OK returns the chosen count; Cancel returns null through DialogResult.
        Assert.Matches(
            new Regex(@"OK_Click[\s\S]*?DialogResult\s*=\s*\(uint\?\)count;\s*RequestClose\(\)", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"Cancel_Click[\s\S]*?DialogResult\s*=\s*\(uint\?\)null;\s*RequestClose\(\)", RegexOptions.Singleline),
            source);
    }

    // -----------------------------------------------------------------
    // #1018 — FE8 random-monster jump pre-selects the class_id row.
    //
    // WF: InputFormRef.JumpForm<MonsterProbabilityForm>(B1, "AddressList", B1)
    // selects the AddressList row whose INDEX == class_id (B1). The Avalonia
    // resolver maps that row index to the matching Monster Probability entry's
    // address over LoadMonsterProbabilityList's 12-byte stride.
    // -----------------------------------------------------------------

    [Fact]
    public void ResolveAddressByClassIndex_InRange_ReturnsBasePlusStride()
    {
        var vm = new MonsterProbabilityViewerViewModel();
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            const uint baseAddr = 0x00100000u; // safe offset, clear of the pointer slot
            const int count = 5;
            // Plant the GBA pointer to the table base. Read the pointer-slot
            // offset from RomInfo (not a hard-coded constant) so the test stays
            // correct if ROMFE8U.monster_probability_pointer ever changes.
            uint ptrSlot = rom.RomInfo.monster_probability_pointer;
            rom.write_p32(ptrSlot, baseAddr);
            // Plant 5 non-terminated 12-byte entries (first byte != 0xFF so the
            // u8==0xFF terminator in LoadMonsterProbabilityList does not stop early).
            for (uint i = 0; i < count; i++)
            {
                uint entry = baseAddr + i * 12u;
                rom.write_u8(entry, i + 1u); // B0 (class id slot 1), never 0xFF
                // remaining 11 bytes stay 0 — fine, only the terminator byte matters
            }

            // Each in-range class_id index k resolves to base + k*12.
            for (uint k = 0; k < count; k++)
            {
                Assert.Equal(baseAddr + k * 12u, vm.ResolveAddressByClassIndex(k));
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ResolveAddressByClassIndex_OutOfRange_ReturnsNotFound()
    {
        var vm = new MonsterProbabilityViewerViewModel();
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            const uint baseAddr = 0x00100000u;
            const int count = 3;
            // Read the pointer-slot offset from RomInfo (not a hard-coded
            // constant) so the test follows ROMFE8U if the offset ever changes.
            uint ptrSlot = rom.RomInfo.monster_probability_pointer;
            rom.write_p32(ptrSlot, baseAddr);
            for (uint i = 0; i < count; i++)
            {
                rom.write_u8(baseAddr + i * 12u, i + 1u);
            }
            // Terminate the table right after entry #3 so LoadMonsterProbabilityList
            // yields exactly 3 rows.
            rom.write_u8(baseAddr + (uint)count * 12u, 0xFF);

            // k == count and k > count are out of range.
            Assert.Equal(U.NOT_FOUND, vm.ResolveAddressByClassIndex((uint)count));
            Assert.Equal(U.NOT_FOUND, vm.ResolveAddressByClassIndex((uint)count + 7u));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ResolveAddressByClassIndex_NoTable_ReturnsNotFound()
    {
        // A fresh FE8U ROM has an all-zero pointer slot, so the table base
        // resolves to 0 (fails isSafetyOffset) and the list is empty — any
        // class_id index must return NOT_FOUND with no exception.
        var vm = new MonsterProbabilityViewerViewModel();
        ROM rom = MakeFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            Assert.Equal(U.NOT_FOUND, vm.ResolveAddressByClassIndex(0u));
            Assert.Equal(U.NOT_FOUND, vm.ResolveAddressByClassIndex(42u));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void View_JumpMonsterProb_ResolvesClassIndexAndNavigates()
    {
        // The View's JumpMonsterProb_Click must read B1 from the LIVE control
        // (ClassIDBox.Value), resolve the class_id row index via
        // ResolveAddressByClassIndex, and Navigate to the
        // MonsterProbabilityViewerView; the stale "does not currently expose
        // row-index navigation" limitation comment must be gone (#1018).
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // Extract just the handler body so the "no stale VM read" assertion is
        // scoped to this handler (other handlers legitimately read _vm.ClassID).
        var handlerMatch = Regex.Match(
            source,
            @"void\s+JumpMonsterProb_Click\([^)]*\)\s*\{(?<body>[\s\S]*?)\n        \}",
            RegexOptions.Singleline);
        Assert.True(handlerMatch.Success, "JumpMonsterProb_Click handler not found");
        string body = handlerMatch.Groups["body"].Value;

        // FIX 1: reads the LIVE displayed control (ClassIDBox.Value), like WF
        // (this.B1.Value) and the sibling jump handlers (UnitIDBox.Value) — not
        // the stale-on-edit _vm.ClassID that only syncs on load / Write.
        Assert.True(
            body.Contains("ClassIDBox.Value"),
            "JumpMonsterProb_Click must read the live ClassIDBox.Value (B1)");
        Assert.False(
            body.Contains("_vm.ClassID"),
            "JumpMonsterProb_Click must NOT read the stale _vm.ClassID; use the live ClassIDBox.Value");
        Assert.True(
            body.Contains("ResolveAddressByClassIndex("),
            "JumpMonsterProb_Click must resolve the class_id row index");
        Assert.True(
            body.Contains("Navigate<MonsterProbabilityViewerView>("),
            "JumpMonsterProb_Click must Navigate to the MonsterProbabilityViewerView");
        Assert.False(
            source.Contains("does not currently expose row-index navigation"),
            "stale 'does not currently expose row-index navigation' comment must be removed");
    }

    [Fact]
    public void MonsterProbabilityView_NavigateTo_StashesPendingAddressBeforeListLoads()
    {
        // The viewer must defer a NavigateTo that arrives before the list loads
        // (WindowManager.Navigate opens then immediately NavigateTo, but Avalonia
        // raises Opened asynchronously) and replay it in LoadList — otherwise
        // SelectAddress no-ops against the empty list (#1018 timing fix).
        string repoRoot = FindRepoRoot();
        string viewPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MonsterProbabilityViewerView.axaml.cs");
        string source = File.ReadAllText(viewPath);

        // NavigateTo stashes the address into _pendingNavigateAddr when the list
        // has not loaded yet.
        Assert.Matches(
            new Regex(@"void\s+NavigateTo\(uint\s+\w+\)\s*\{[\s\S]*?_listLoaded[\s\S]*?_pendingNavigateAddr\s*=", RegexOptions.Singleline),
            source);
        // LoadList replays the stashed address via SelectAddress.
        Assert.Matches(
            new Regex(@"_pendingNavigateAddr\s+is\s+uint[\s\S]*?SelectAddress\(", RegexOptions.Singleline),
            source);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeFe8uRom()
    {
        var bytes = new byte[0x1100000];
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
        return dir ?? throw new InvalidOperationException("Couldn't locate FEBuilderGBA.sln");
    }
}
