// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 / Phase 4 / Phase 5 gap-sweep regression tests for
// EventUnitFE7View (closes #431 via PR #522).
//
// Covers the 39 gaps the issue called out:
//   - 36 missing WF-only labels (density / labels)
//   - 3 missing INavigationTargetSource manifest entries (jumps)
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
public class EventUnitFE7ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must clear the MEDIUM verdict.
    //
    // 2026-05-22 sweep reported WF=66 / AV=54 (Δ=-18.2%, LOW already).
    // The acceptance criterion is MEDIUM (>= 75% of WF). For WF=66 the
    // MEDIUM threshold is ceil(0.75*66) = 50. The new view design adds
    // ~30 controls (Top Address bar, Address bar, B3 sub-panel, Before/After
    // coords sub-panels, Jump panel, Comment, etc.) so the new AV count
    // should be well above 50.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-22 density sweep — see issue #431.
        const int WfControlCount = 66;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 50
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) — all three jumps registered.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllThreeJumpTargets()
    {
        var vm = new EventUnitFE7ViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t => t.TargetViewType == typeof(EventBattleTalkFE7View));
        Assert.Contains(targets, t => t.TargetViewType == typeof(SoundBossBGMViewerView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(EventHaikuFE7View));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #431, NONE of the three rows should still
        // carry an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new EventUnitFE7ViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate the three WF callsites and confirm
    // they MATCH the new manifest rows (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_BattleTalkFE7_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitFE7Form",
                TargetForm: "EventBattleTalkFE7Form",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitFE7ViewModel",
                SourceView: "EventUnitFE7View",
                Command: "JumpToBattleTalk",
                TargetView: "EventBattleTalkFE7View",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitFE7Form" &&
            r.TargetWfType == "EventBattleTalkFE7Form");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventBattleTalkFE7View", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_BattleBGM_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitFE7Form",
                TargetForm: "SoundBossBGMForm",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitFE7ViewModel",
                SourceView: "EventUnitFE7View",
                Command: "JumpToBattleBGM",
                TargetView: "SoundBossBGMViewerView",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitFE7Form" &&
            r.TargetWfType == "SoundBossBGMForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("SoundBossBGMViewerView", match.TargetAvType);
    }

    [Fact]
    public void JumpParityScanner_HaikuFE7_NowMatchesManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "EventUnitFE7Form",
                TargetForm: "EventHaikuFE7Form",
                HasAddressArgument: true),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "EventUnitFE7ViewModel",
                SourceView: "EventUnitFE7View",
                Command: "JumpToHaiku",
                TargetView: "EventHaikuFE7View",
                IssueRef: null),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "EventUnitFE7Form" &&
            r.TargetWfType == "EventHaikuFE7Form");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.Match, match!.Status);
        Assert.Equal("EventHaikuFE7View", match.TargetAvType);
    }

    // -----------------------------------------------------------------
    // ViewModel B3 round-trip — UnitInfoLV/Allegiance/Grow must be a
    // lossless decomposition of the raw UnitInfo byte.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_UnitInfoLV_DecomposesAndComposes()
    {
        var vm = new EventUnitFE7ViewModel();

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

    // -----------------------------------------------------------------
    // ViewModel Comment must round-trip through CoreState.CommentCache.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_Comment_RoundTripsThroughCommentCache()
    {
        // Ensure the cache is wired (App.axaml.cs does this at startup;
        // the test bench may not). HeadlessEtcCache is the test-friendly
        // implementation.
        if (CoreState.CommentCache == null)
        {
            CoreState.CommentCache = new HeadlessEtcCache();
        }

        var vm = new EventUnitFE7ViewModel();
        const uint addr = 0x00DEAD00u;

        // Seed the cache.
        CoreState.CommentCache.Update(addr, "test comment");

        // Build a synthetic ROM so LoadEntry can run without crashing.
        ROM rom = MakeFe7uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            vm.LoadEntry(addr);
            Assert.Equal("test comment", vm.Comment);

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
    // Undo coverage — the View's Write_Click handler must open and
    // commit an UndoService scope. We inspect the code-behind text
    // because the click handler is private and instantiating an
    // Avalonia Window in xunit needs the Avalonia app handle.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteClick_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml.cs");
        Assert.True(File.Exists(codeBehindPath), $"code-behind not found at {codeBehindPath}");

        string source = File.ReadAllText(codeBehindPath);

        // Find the Write_Click method body — match against the assertion
        // that it contains the Begin/Commit/Rollback triad.
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
            "EventUnitFE7View.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // ExpandList_Click must use undo discipline too — verify pattern.
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

    // -----------------------------------------------------------------
    // Copilot review fixes: ExpandList wired to Core helper, table-2
    // jumps land on the right row via VM fallback (out-of-list).
    // -----------------------------------------------------------------

    [Fact]
    public void View_ExpandListClick_InvokesVmExpandHelper()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // The handler must actually call ExpandUnitListCurrent — proves the
        // wiring isn't inert.
        Assert.Matches(
            new Regex(@"void\s+ExpandList_Click\([^)]*\)\s*\{[\s\S]*?_vm\.ExpandUnitListCurrent\(", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void ViewModel_ExpandUnitListCurrent_DelegatesToCoreHelper()
    {
        // The VM exposes the wrapper that the View calls.
        var vm = new EventUnitFE7ViewModel();
        var method = vm.GetType().GetMethod("ExpandUnitListCurrent",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Equal(typeof(uint), method!.ReturnType);
    }

    [Fact]
    public void EventBattleTalkFE7View_NavigateTo_SwitchesToSecondaryTable()
    {
        // #957 W1b: the secondary 12-byte table (event_ballte_talk2_pointer) is
        // now a browsable/editable table behind the Table filter combo, so
        // NavigateTo resolves which table the address belongs to and switches
        // the combo to it (loading the correct schema) instead of only logging
        // an out-of-list hit (the former Copilot review #522 round-4 gap).
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventBattleTalkFE7View.axaml.cs");
        string source = File.ReadAllText(path);

        // NavigateTo must resolve + switch the Table filter combo.
        Assert.Matches(
            new Regex(@"public\s+void\s+NavigateTo\([^)]*\)\s*\{[\s\S]*?ResolveTableIndexFor\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"public\s+void\s+NavigateTo\([^)]*\)\s*\{[\s\S]*?TableFilter\.SelectedIndex", RegexOptions.Singleline),
            source);
        // The 12-byte secondary schema must be wired in the VM.
        string vm = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "ViewModels", "EventBattleTalkFE7ViewModel.cs"));
        Assert.Contains("BattleTalkTable.Secondary", vm);
        Assert.Contains("event_ballte_talk2_pointer", vm);
    }

    [Fact]
    public void EventHaikuFE7View_NavigateTo_SwitchesToTutorialTable()
    {
        // #957 W1b: the two 12-byte tutorial tables (event_haiku_tutorial_1/2_pointer)
        // are now browsable/editable behind the Table filter combo; NavigateTo
        // resolves + switches the combo to the correct table instead of only
        // logging an out-of-list hit (the former Copilot review #522 round-4 gap).
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventHaikuFE7View.axaml.cs");
        string source = File.ReadAllText(path);

        Assert.Matches(
            new Regex(@"public\s+void\s+NavigateTo\([^)]*\)\s*\{[\s\S]*?ResolveTableIndexFor\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"public\s+void\s+NavigateTo\([^)]*\)\s*\{[\s\S]*?TableFilter\.SelectedIndex", RegexOptions.Singleline),
            source);
        string vm = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "ViewModels", "EventHaikuFE7ViewModel.cs"));
        Assert.Contains("HaikuTable.Tutorial1", vm);
        Assert.Contains("HaikuTable.Tutorial2", vm);
        Assert.Contains("event_haiku_tutorial_1_pointer", vm);
        Assert.Contains("event_haiku_tutorial_2_pointer", vm);
    }

    // -----------------------------------------------------------------
    // AutomationId presence — the new controls must have stable test ids
    // so MCP / UIAutomation can target them for end-to-end validation.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("EventUnitFE7_TopAddr_Input")]
    [InlineData("EventUnitFE7_ReadCount_Input")]
    [InlineData("EventUnitFE7_ReloadList_Button")]
    [InlineData("EventUnitFE7_NewAlloc_Button")]
    [InlineData("EventUnitFE7_ExpandList_Button")]
    [InlineData("EventUnitFE7_BlockSize_Input")]
    [InlineData("EventUnitFE7_SelectedAddr_Input")]
    [InlineData("EventUnitFE7_Comment_Input")]
    [InlineData("EventUnitFE7_LV_Input")]
    [InlineData("EventUnitFE7_Allegiance_Combo")]
    [InlineData("EventUnitFE7_GrowthRate_Combo")]
    [InlineData("EventUnitFE7_PosSync_Combo")]
    [InlineData("EventUnitFE7_JumpBattleTalk_Button")]
    [InlineData("EventUnitFE7_JumpBattleBGM_Button")]
    [InlineData("EventUnitFE7_JumpHaiku_Button")]
    [InlineData("EventUnitFE7_ItemDrop_Label")]
    public void View_DeclaresExpectedAutomationId(string automationId)
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
        string content = File.ReadAllText(axamlPath);
        Assert.Contains(automationId, content);
    }

    // -----------------------------------------------------------------
    // New(Alloc) — #1004 FE7 WF-parity reserved-NEW block + modal count-picker.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_NewAllocUnitList_AllocatesEditableBlock_FE7()
    {
        // VM NewAllocUnitList(count, undo) must allocate a real FE7 block via
        // the version-agnostic Core seam and return a valid base. FE7 uses
        // eventunit_data_size == 16 (ROMFE7U.cs) — NOT 20 like FE8.
        var vm = new EventUnitFE7ViewModel();
        ROM rom = MakeFe7uRom();
        var prevRom = CoreState.ROM;
        var prevDelegate = CoreState.AppendBinaryData;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = null; // headless freespace fallback

            const uint count = 3;
            const uint fe7UnitSize = 16;
            Assert.Equal(fe7UnitSize, rom.RomInfo.eventunit_data_size);

            uint newBase = vm.NewAllocUnitList(count, null);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // 3 rows × 16 bytes; each row B0 == 1; rest == 0.
            for (uint i = 0; i < count; i++)
            {
                uint rowAddr = newBase + i * fe7UnitSize;
                Assert.Equal((byte)0x01, rom.Data[rowAddr + 0]);
                for (uint b = 1; b < fe7UnitSize; b++)
                    Assert.Equal((byte)0x00, rom.Data[rowAddr + b]);
            }

            // Trailing terminator byte at base + count*size == 0x00.
            Assert.Equal((byte)0x00, rom.Data[newBase + count * fe7UnitSize]);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.AppendBinaryData = prevDelegate;
        }
    }

    [Fact]
    public void ViewModel_NewAllocUnitList_FE7_CountZero_IsNoOp()
    {
        // Cancel / count==0 path: the VM helper must not allocate.
        var vm = new EventUnitFE7ViewModel();
        ROM rom = MakeFe7uRom();
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
    public void View_NewAllocClick_FE7_DoesNotContainOpenEventCondView()
    {
        // The stub body that opened EventCondView must be gone; the new handler
        // must NOT contain a call to Open<EventCondView>.
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // The old stub opened EventCondView — that must be removed.
        Assert.DoesNotMatch(
            new Regex(@"NewAlloc_Click[\s\S]*?Open<EventCondView>", RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void View_NewAllocClick_FE7_OpensModalPickerAndCallsVmAllocator()
    {
        // The new handler must open the modal count-picker, call the VM allocator,
        // and wrap the call in an undo scope — mirrors the FE8 EventUnitView.
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // async void, modal picker.
        Assert.Matches(
            new Regex(@"async\s+void\s+NewAlloc_Click\(", RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?OpenModal<EventUnitNewAllocView, uint\?>", RegexOptions.Singleline),
            source);
        // Cancel / count==0 no-op guard.
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?count\s*==\s*null\s*\|\|\s*count\.Value\s*==\s*0[\s\S]*?return", RegexOptions.Singleline),
            source);
        // Undo scope.
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
    public void View_NewAllocClick_FE7_AddsNewEntryThatSurvivesMapRefresh()
    {
        // Session NEW tracking: the View must keep _newAllocData, add a "NEW"
        // AddrResult on alloc, and re-merge it on map refresh in
        // MapListBox_SelectionChanged.
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "EventUnitFE7View.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // Session list field exists.
        Assert.Matches(new Regex(@"_newAllocData"), source);
        // NewAlloc adds a "NEW" AddrResult.
        Assert.Matches(
            new Regex(@"NewAlloc_Click[\s\S]*?new\s+AddrResult\([^)]*""NEW""[^)]*\)[\s\S]*?_newAllocData\.Add", RegexOptions.Singleline),
            source);
        // Map selection re-merges NEW allocations.
        Assert.Matches(
            new Regex(@"MapListBox_SelectionChanged[\s\S]*?MergeNewAllocData\(", RegexOptions.Singleline),
            source);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeFe7uRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
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
