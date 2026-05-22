// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/4 gap-sweep regression tests for WorldMapEventPointerView. (#432)
//
// Covers the 39->>=30 control raise (density) and the 5 real cross-editor
// jump callsites (jumps). The scanner-expanded WorldMapPathEditorView row
// (6th in the report) remains intentionally unmanifested — see
// WorldMapEventPointerViewModel.NavigationTargets.cs header for rationale.
//
// Marked [Collection("SharedState")] because several tests mutate
// CoreState.ROM. Without serialization, xUnit's parallel runner can
// observe a sibling test's ROM swap mid-call.
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
/// Tests proving the WorldMapEventPointer parity raise (#432) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
/// </summary>
[Collection("SharedState")]
public class WorldMapEventPointerParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 39 controls (per density sweep 2026-05-24).
    /// To leave HIGH we need AV >= ceil(WF * 0.75) = 30 controls.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "WorldMapEventPointerView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-24 density sweep — see issue #432.
        const int WfControlCount = 39;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 30
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all 5 real callsites.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllFiveJumpTargets()
    {
        var vm = new WorldMapEventPointerViewModel();
        var targets = vm.GetNavigationTargets();

        // WF JumpForm<EventScriptForm>() appears in 3 separate click handlers:
        // JUMP_OPNING_EVENT, JUMP_ENDING1_EVENT, JUMP_ENDING2_EVENT. Each is
        // its own manifest entry so the scanner can distinguish them.
        int eventScriptCount = targets.Count(t => t.TargetViewType == typeof(EventScriptView));
        Assert.Equal(3, eventScriptCount);
        Assert.Contains(targets, t => t.TargetViewType == typeof(WorldMapPathView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(WorldMapPointView));
        Assert.Equal(5, targets.Count);
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #432, NONE of the 5 rows should carry an
        // IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new WorldMapEventPointerViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    [Fact]
    public void ViewModel_DoesNotManifestWorldMapPathEditorView()
    {
        // The 6th scanner row (WorldMapPathForm -> WorldMapPathEditorView) is
        // a PairMatcher artifact — declaring it as a navigation target would
        // add UX beyond WF parity (Copilot CLI plan review point 2). Lock the
        // decision in so future refactors don't accidentally re-introduce it.
        var vm = new WorldMapEventPointerViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.DoesNotContain(targets, t => t.TargetViewType == typeof(WorldMapPathEditorView));
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate the 5 WF callsites and confirm
    // they MATCH the manifest rows. The 6th scanner-expanded row
    // (WorldMapPathEditorView) stays as MissingAvManifest.
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_FiveWfCallsites_MatchManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "WorldMapEventPointerForm",
                TargetForm: "EventScriptForm",
                HasAddressArgument: false),
            new WfJumpCallsite(
                SourceForm: "WorldMapEventPointerForm",
                TargetForm: "EventScriptForm",
                HasAddressArgument: false),
            new WfJumpCallsite(
                SourceForm: "WorldMapEventPointerForm",
                TargetForm: "EventScriptForm",
                HasAddressArgument: false),
            new WfJumpCallsite(
                SourceForm: "WorldMapEventPointerForm",
                TargetForm: "WorldMapPathForm",
                HasAddressArgument: false),
            new WfJumpCallsite(
                SourceForm: "WorldMapEventPointerForm",
                TargetForm: "WorldMapPointForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "WorldMapEventPointerViewModel",
                SourceView: "WorldMapEventPointerView",
                Command: "JumpToOpeningEvent",
                TargetView: "EventScriptView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "WorldMapEventPointerViewModel",
                SourceView: "WorldMapEventPointerView",
                Command: "JumpToEnding1Event",
                TargetView: "EventScriptView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "WorldMapEventPointerViewModel",
                SourceView: "WorldMapEventPointerView",
                Command: "JumpToEnding2Event",
                TargetView: "EventScriptView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "WorldMapEventPointerViewModel",
                SourceView: "WorldMapEventPointerView",
                Command: "JumpToWorldMapPath",
                TargetView: "WorldMapPathView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "WorldMapEventPointerViewModel",
                SourceView: "WorldMapEventPointerView",
                Command: "JumpToWorldMapPoint",
                TargetView: "WorldMapPointView",
                IssueRef: null),
        };

        // Pass repoRoot so the PairMatcher layer runs and discovers the
        // EventScriptForm -> EventScriptView pair (which lives outside
        // ListParityHelper's authoritative seed because EventScript isn't a
        // list-backed editor). Without this, EventScript callsites stay as
        // MissingAvManifest in the unit-test environment even though the
        // production sweep correctly matches them.
        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests, FindRepoRoot());

        // EventScript rows: with PairMatcher active, EventScriptForm pairs to
        // multiple AV views (EventScriptView, EventScriptMainView, etc). The
        // scanner emits one Match row per (WF row, manifest entry) pair, so
        // 3 callsites x 3 manifest entries pointing at EventScriptView = 9
        // Match rows. The semantic check: at least one Match row exists per
        // command name, ensuring each of the 3 click handlers has a real
        // target.
        var eventScriptMatches = rows.Where(r =>
            r.SourceForm == "WorldMapEventPointerForm" &&
            r.TargetWfType == "EventScriptForm" &&
            r.TargetAvType == "EventScriptView" &&
            r.Status == JumpRowStatus.Match).ToList();
        Assert.True(eventScriptMatches.Count >= 3,
            $"Expected >= 3 EventScript Match rows, got {eventScriptMatches.Count}");
        // All three manifest command names appear at least once in the matches.
        var commands = eventScriptMatches.Select(r => r.Command).Distinct().ToList();
        Assert.Contains("JumpToOpeningEvent", commands);
        Assert.Contains("JumpToEnding1Event", commands);
        Assert.Contains("JumpToEnding2Event", commands);

        // WorldMapPath: scanner expands `WorldMapPathForm` to TWO AV views
        // (WorldMapPathView from ListParityHelper, WorldMapPathEditorView from
        // PairMatcher). Our manifest declares only WorldMapPathView, so:
        //   - the WorldMapPathView row MATCHES
        //   - the WorldMapPathEditorView row remains MissingAvManifest
        var pathViewMatch = rows.FirstOrDefault(r =>
            r.SourceForm == "WorldMapEventPointerForm" &&
            r.TargetWfType == "WorldMapPathForm" &&
            r.TargetAvType == "WorldMapPathView");
        Assert.NotNull(pathViewMatch);
        Assert.Equal(JumpRowStatus.Match, pathViewMatch!.Status);

        var pathEditorRow = rows.FirstOrDefault(r =>
            r.SourceForm == "WorldMapEventPointerForm" &&
            r.TargetWfType == "WorldMapPathForm" &&
            r.TargetAvType == "WorldMapPathEditorView");
        Assert.NotNull(pathEditorRow);
        Assert.Equal(JumpRowStatus.MissingAvManifest, pathEditorRow!.Status);

        // WorldMapPoint: scanner expands to one or more AV views. The
        // WorldMapPointView row should resolve to Match.
        var pointMatch = rows.FirstOrDefault(r =>
            r.SourceForm == "WorldMapEventPointerForm" &&
            r.TargetWfType == "WorldMapPointForm" &&
            r.TargetAvType == "WorldMapPointView");
        Assert.NotNull(pointMatch);
        Assert.Equal(JumpRowStatus.Match, pointMatch!.Status);
    }

    // -----------------------------------------------------------------
    // View: real click handlers must be wired to WindowManager. Manifest
    // match alone doesn't prove the UI works (Copilot CLI plan review
    // point 4 echo from #440 — codified here).
    // -----------------------------------------------------------------

    [Fact]
    public void View_NavigationHandlers_AreWiredToWindowManager()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "WorldMapEventPointerView.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");

        string source = File.ReadAllText(viewCsPath);

        AssertHandlerWiring(source,
            handlerName: "JumpToOpening_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<EventScriptView>");

        AssertHandlerWiring(source,
            handlerName: "JumpToEnding1_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<EventScriptView>");

        AssertHandlerWiring(source,
            handlerName: "JumpToEnding2_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<EventScriptView>");

        AssertHandlerWiring(source,
            handlerName: "JumpToWorldMapPath_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<WorldMapPathView>");

        AssertHandlerWiring(source,
            handlerName: "JumpToWorldMapPoint_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<WorldMapPointView>");

        // NewAlloc handlers (mirrors WF L_0_NEWALLOC_EVENT / N_L_0_NEWALLOC_EVENT).
        // Avalonia delegates to opening the EventScriptView so the user can
        // pick / author a new event — the full AllocEvent state machine is
        // intentionally not re-implemented (out of scope for #432).
        AssertHandlerWiring(source,
            handlerName: "BeforeNewAlloc_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<EventScriptView>");

        AssertHandlerWiring(source,
            handlerName: "AfterNewAlloc_Click",
            requiredCallPattern: @"WindowManager\.Instance\.(Navigate|Open)<EventScriptView>");
    }

    // -----------------------------------------------------------------
    // ViewModel list construction — preserve WF semantics. WF's Init() and
    // N_Init() predicates return `true` for `i == 0` BEFORE calling
    // U.isPointer(...). The Avalonia VM must keep that contract — without
    // it, a NULL slot at row 0 would mis-truncate the list. (Copilot CLI
    // plan review point 3.)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadBeforeList_AlwaysIncludesRow0_EvenWhenRow0IsNull()
    {
        ROM rom = MakeFe8uWithBeforeListShape(slot0: 0u, slot1: 0u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();
            var rows = vm.LoadBeforeList();
            // Row 0 is unconditionally included even though slot0 == 0;
            // matches WF predicate (i == 0 returns true regardless of
            // pointer validity).
            Assert.True(rows.Count >= 1, $"Expected >=1 row when slot0 is NULL, got {rows.Count}");
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadBeforeList_StopsWhenRow1IsNonPointer()
    {
        // Row 0 valid pointer, row 1 NULL: list contains exactly 1 row.
        ROM rom = MakeFe8uWithBeforeListShape(slot0: 0x08123456u, slot1: 0u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();
            var rows = vm.LoadBeforeList();
            Assert.Single(rows);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadBeforeList_TruncatesAtFirstNonPointerAfterRow0()
    {
        // Slots 0..2 valid, slot 3 NULL: 3 rows.
        ROM rom = MakeFe8uWithBeforeListSlots(
            new uint[] { 0x08123456u, 0x08123466u, 0x08123476u, 0u, 0x08123486u });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();
            var rows = vm.LoadBeforeList();
            Assert.Equal(3, rows.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadAfterList_AlsoUsesRow0UnconditionalRule()
    {
        // Symmetric assertion for the After (stage-select) table.
        ROM rom = MakeFe8uWithAfterListShape(slot0: 0u, slot1: 0u);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();
            var rows = vm.LoadAfterList();
            Assert.True(rows.Count >= 1, $"Expected >=1 row when slot0 is NULL, got {rows.Count}");
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Per-row event pointer round-trip (Before + After tables). Copilot
    // CLI re-review of PR #511 flagged that per-row pointers must use
    // `rom.p32` / `rom.write_p32` semantics so the 0x08000000 mask is
    // applied symmetrically — otherwise high pointers (e.g. 0x08A39768)
    // get capped/stripped during write. These tests pin the contract.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_BeforeRow_RoundTrips_With_PointerForm()
    {
        // Plant a row 0 with a high-bit pointer to verify the mask survives
        // a load/write cycle. Without p32/write_p32 the high byte (0x08)
        // would be lost or doubled.
        uint slot0 = 0x08A39768u;
        ROM rom = MakeFe8uWithBeforeListSlots(new[] { slot0 });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();

            uint baseAddr = rom.p32(rom.RomInfo.worldmap_event_on_stageclear_pointer);
            vm.LoadBeforeEntry(baseAddr);

            // VM stores values in OFFSET form (mask stripped via p32).
            Assert.Equal(0x00A39768u, vm.BeforeEventPointer);

            // Modify and write back. write_p32 re-applies the mask.
            vm.BeforeEventPointer = 0x00BBCCDDu;
            vm.WriteBefore();
            Assert.Equal(0x08BBCCDDu, rom.u32(baseAddr));

            // Reload and verify the round-trip is symmetric.
            vm.LoadBeforeEntry(baseAddr);
            Assert.Equal(0x00BBCCDDu, vm.BeforeEventPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_AfterRow_RoundTrips_With_PointerForm()
    {
        uint slot0 = 0x08A39D00u;
        ROM rom = MakeFe8uWithAfterListSlots(new[] { slot0 });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new WorldMapEventPointerViewModel();

            uint baseAddr = rom.p32(rom.RomInfo.worldmap_event_on_stageselect_pointer);
            vm.LoadAfterEntry(baseAddr);

            Assert.Equal(0x00A39D00u, vm.AfterEventPointer);

            vm.AfterEventPointer = 0x00ABCDEFu;
            vm.WriteAfter();
            Assert.Equal(0x08ABCDEFu, rom.u32(baseAddr));

            vm.LoadAfterEntry(baseAddr);
            Assert.Equal(0x00ABCDEFu, vm.AfterEventPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel — global events round-trip (Opening / Ending1 / Ending2).
    // The 3 global event pointers live in fixed RomInfo slots, NOT in the
    // dynamic Before/After tables. The Write path must update all three.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteGlobalEvents_RoundTrips()
    {
        ROM rom = MakeFe8uWithGlobalEventSlots();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // VM stores values in OFFSET form (matches WF
            // `OPNING_EVENT.Value = Program.ROM.p32(...)` which always
            // returns the toOffset()-stripped form). write_p32 re-applies
            // the 0x08000000 mask via U.toPointer; p32 strips it back when
            // reading. Use offset-form sentinels to assert the round-trip
            // is symmetric.
            var vm = new WorldMapEventPointerViewModel();
            vm.OpeningEvent = 0x00111111u;
            vm.Ending1Event = 0x00222222u;
            vm.Ending2Event = 0x00333333u;
            vm.WriteGlobalEvents();

            uint openingSlot = rom.RomInfo.oping_event_pointer;
            uint ending1Slot = rom.RomInfo.ending1_event_pointer;
            uint ending2Slot = rom.RomInfo.ending2_event_pointer;

            // After write the raw bytes carry the pointer form (mask on).
            Assert.Equal(0x08111111u, rom.u32(openingSlot));
            Assert.Equal(0x08222222u, rom.u32(ending1Slot));
            Assert.Equal(0x08333333u, rom.u32(ending2Slot));

            // p32 strips the mask back to offset form — symmetric round-trip.
            Assert.Equal(0x00111111u, rom.p32(openingSlot));
            Assert.Equal(0x00222222u, rom.p32(ending1Slot));
            Assert.Equal(0x00333333u, rom.p32(ending2Slot));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_UsesCorrectFE8Pointers_NotMapWorldmapEventPointer()
    {
        // Regression guard for the original bug: pre-#432, the view read
        // `map_worldmapevent_pointer` (an FE6-only slot that is 0 on FE8).
        // Lock in that the VM source no longer references that pointer in
        // CODE lines — comments are allowed to mention it historically
        // (e.g. "pre-#432 this VM read `map_worldmapevent_pointer`...").
        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "WorldMapEventPointerViewModel.cs");
        string source = File.ReadAllText(vmPath);
        // Strip C# line comments before scanning.
        string codeOnly = System.Text.RegularExpressions.Regex.Replace(
            source, @"//[^\n]*", "");
        Assert.DoesNotContain("map_worldmapevent_pointer", codeOnly);
        Assert.Contains("worldmap_event_on_stageclear_pointer", source);
        Assert.Contains("worldmap_event_on_stageselect_pointer", source);
    }

    // -----------------------------------------------------------------
    // Existing list-parity helper still maps the editor (regression guard).
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_WorldMapEventPointerView_StillRegistered()
    {
        var map = ListParityHelper.GetMapping("WorldMapEventPointerView");
        Assert.NotNull(map);
        Assert.Equal("WorldMapEventPointerForm", map!.Value.FormType);
    }

    // ---------------------------- Helpers ----------------------------

    static void AssertHandlerWiring(string source, string handlerName, string requiredCallPattern)
    {
        int sigIdx = source.IndexOf(handlerName + "(", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0,
            $"Click handler '{handlerName}' not found in WorldMapEventPointerView.axaml.cs");
        int braceOpenIdx = source.IndexOf('{', sigIdx);
        Assert.True(braceOpenIdx > sigIdx, $"Handler '{handlerName}' has no body");
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0, $"Handler '{handlerName}' body is malformed (no matching `}}`)");
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
        Assert.Matches(requiredCallPattern, body);
    }

    /// <summary>
    /// Build a synthetic FE8U ROM whose Before (stage-clear) pointer table
    /// has only two slots populated as requested. Used by tests that want
    /// to assert how the row-0 / row-1 termination interacts with NULL
    /// entries — see ViewModel_LoadBeforeList_* facts.
    /// </summary>
    static ROM MakeFe8uWithBeforeListShape(uint slot0, uint slot1)
        => MakeFe8uWithBeforeListSlots(new[] { slot0, slot1 });

    static ROM MakeFe8uWithAfterListShape(uint slot0, uint slot1)
        => MakeFe8uWithAfterListSlots(new[] { slot0, slot1 });

    static ROM MakeFe8uWithBeforeListSlots(uint[] slots)
    {
        return MakeFe8uWithListSlots(
            stageClearSlots: slots,
            stageSelectSlots: null);
    }

    static ROM MakeFe8uWithAfterListSlots(uint[] slots)
    {
        return MakeFe8uWithListSlots(
            stageClearSlots: null,
            stageSelectSlots: slots);
    }

    static ROM MakeFe8uWithListSlots(uint[]? stageClearSlots, uint[]? stageSelectSlots)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint baseClear = 0x00800000u;
        uint baseSelect = 0x00900000u;

        if (stageClearSlots != null)
        {
            for (int i = 0; i < stageClearSlots.Length; i++)
            {
                BitConverter.GetBytes(stageClearSlots[i]).CopyTo(bytes, baseClear + (uint)(i * 4));
            }
            // Plant a sentinel NULL after the slots so the loader has
            // somewhere to stop even if the test array doesn't terminate.
            BitConverter.GetBytes(0u).CopyTo(bytes, baseClear + (uint)(stageClearSlots.Length * 4));
            BitConverter.GetBytes(baseClear | 0x08000000u).CopyTo(bytes,
                rom.RomInfo.worldmap_event_on_stageclear_pointer);
        }

        if (stageSelectSlots != null)
        {
            for (int i = 0; i < stageSelectSlots.Length; i++)
            {
                BitConverter.GetBytes(stageSelectSlots[i]).CopyTo(bytes, baseSelect + (uint)(i * 4));
            }
            BitConverter.GetBytes(0u).CopyTo(bytes, baseSelect + (uint)(stageSelectSlots.Length * 4));
            BitConverter.GetBytes(baseSelect | 0x08000000u).CopyTo(bytes,
                rom.RomInfo.worldmap_event_on_stageselect_pointer);
        }

        // Re-load so RomInfo (already populated by the first LoadLow) sees
        // the planted pointer slots; LoadLow re-parses the byte array
        // header but keeps the FE8U-detected RomInfo intact.
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static ROM MakeFe8uWithGlobalEventSlots()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        // RomInfo offsets for the three global event pointers exist in the
        // FE8U header range (< 0x10000), so the byte array already covers
        // them. Nothing to plant — the Write_Click path drives the bytes.
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
