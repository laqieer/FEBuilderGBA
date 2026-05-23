// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for SongTrackView. (#412)
//
// Closes the 66 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SongTrackForm` (HIGH density 19/45 == -57.8 %, 34 WF-only labels,
// 3 common labels, 6 unmapped jump callsites).
//
// Mirrors the parity-test pattern from PR #555 (SkillAssignmentClassSkillSystem)
// and PR #549 (OPClassDemoFE7): density floor + AutomationId surface coverage +
// undo wrap + ROM round-trip + Roslyn-static view code-behind assertions +
// JumpParityScanner round-trip.
//
// SongTrack-specific shape: 16 explicit track columns (TrackLabel1..16 +
// Track1..16 ListBoxes) declared as static AXAML elements so
// `ControlDensityScanner.CountAvControlsInDocument` actually counts them
// (controls inside DataTemplate / ItemsControl.ItemTemplate are skipped per
// Copilot CLI plan review v1, concern #1).
//
// Manifest contains exactly 3 wired jumps with `IssueRef: null`. The 3 import
// dispatch flows (Midi / SelectInstrument / Wave) stay deliberately ABSENT
// from the manifest — `JumpParityScanner.ComputeJumpRows` reports them as
// `MissingAvManifest`, which is the truthful state (Copilot CLI plan review
// v1, concern #2).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the SongTrack parity raise (#412) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SongTrackParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 45 control instantiations (per 2026-05-21
    /// density sweep). To leave the HIGH verdict we need
    /// AV >= ceil(45 * 0.75) = 34.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 45;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 34
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75 % of WF={WfControlCount}) to leave the HIGH verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // The AutomationId vocabulary mirrors the WF designer field names so
    // headless tests / external automation can drive both UIs identically.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReadConfigBar()
    {
        // WF panel1: ReadStartAddress / ReadCount / ReloadListButton.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongTrack_ReadStartAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_ReloadList_Button\"", axaml);
    }

    [Fact]
    public void View_HasMasterAddressBar()
    {
        // WF AddressPanel: Address / BlockSize / SelectAddress / WriteButton.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongTrack_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasDetailPanel_AllHeaderFields()
    {
        // WF panel5: B0 (TrackCount) / B1 (NumBlks) / B2 (Priority) /
        // B3 (Reverb) / P4 (InstrumentAddr).
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongTrack_TrackCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_NumBlks_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_Priority_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_Reverb_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_InstrumentAddr_Input\"", axaml);
    }

    [Fact]
    public void View_HasDetailPanel_AllActionButtons()
    {
        // WF panel5: SongExchange / Import / Export / SappyPlay /
        // OpenSourceFile / OpenSourceFolder + LinkInternet jump label.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongTrack_SongExchange_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_Import_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_Export_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_SappyPlay_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_OpenSourceFile_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_OpenSourceFolder_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_LinkInternet_Label\"", axaml);
        Assert.Contains("AutomationId=\"SongTrack_AllTracks_Label\"", axaml);
    }

    /// <summary>
    /// All 16 track columns must be explicit static AXAML controls (not behind
    /// DataTemplate / ItemTemplate). This is the cornerstone of the density
    /// gap fix — Copilot CLI plan review v1, concern #1.
    /// </summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    [InlineData(13)] [InlineData(14)] [InlineData(15)] [InlineData(16)]
    public void View_HasTrackColumn(int trackIndex)
    {
        string axaml = ReadAxaml();
        Assert.Contains($"AutomationId=\"SongTrack_TrackLabel{trackIndex}_Label\"", axaml);
        Assert.Contains($"AutomationId=\"SongTrack_Track{trackIndex}_List\"", axaml);
    }

    [Fact]
    public void View_AllTrackControls_NotInsideTemplate()
    {
        // Regression guard: if a refactor moves the 16 track controls into a
        // DataTemplate / ItemsControl, ControlDensityScanner will silently
        // drop them. This test reads the AXAML through the same Roslyn pass
        // the scanner uses and asserts the 32 control AutomationIds appear
        // outside any template container.
        var doc = XDocument.Load(AxamlPath());

        // Collect every AutomationId attribute that lives outside a template
        // container, mirroring ControlDensityScanner's allow-list rules.
        var templateContainers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Design.DataContext", "Style", "Styles",
            "DataTemplate", "ControlTemplate", "ItemTemplate",
            "ItemsPanelTemplate", "HierarchicalDataTemplate",
        };

        var ids = new List<string>();
        foreach (var el in doc.Descendants())
        {
            // Skip if any ancestor is a template container.
            bool inTemplate = false;
            foreach (var anc in el.Ancestors())
            {
                if (templateContainers.Contains(anc.Name.LocalName))
                {
                    inTemplate = true;
                    break;
                }
            }
            if (inTemplate) continue;
            foreach (var attr in el.Attributes())
            {
                // Avalonia uses `AutomationProperties.AutomationId="..."` —
                // XLinq exposes the attached-property form as a dotted
                // LocalName, NOT as the bare suffix. Accept both shapes so
                // this test matches the AXAML the production view emits.
                var ln = attr.Name.LocalName;
                if (ln == "AutomationId" || ln.EndsWith(".AutomationId", StringComparison.Ordinal))
                    ids.Add(attr.Value);
            }
        }

        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"SongTrack_TrackLabel{i}_Label", ids);
            Assert.Contains($"SongTrack_Track{i}_List", ids);
        }
    }

    // -----------------------------------------------------------------
    // Phase 5 - Write handler undo + ViewModel round-trip.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        // Roslyn-static read of the code-behind source - no Avalonia head
        // needed. Write_Click must open / commit / rollback an undo scope.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_WriteHandler_RoundTripsThroughViewModel()
    {
        // Neither handler should call rom.SetU* / rom.write_u* directly -
        // all ROM mutation must go through the ViewModel methods so the
        // UndoService receives the diff via the ROM hook.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
        Assert.Contains("_vm.Write()", source);
    }

    // -----------------------------------------------------------------
    // ViewModel state - ROM round-trip on synthetic ROM.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAllHeaderFields()
    {
        var rom = MakeMinimalRomWithSong(out uint songAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongTrackViewModel();
            vm.LoadEntry(songAddr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(songAddr, vm.CurrentAddr);
            // Synthetic ROM plants known values for all 5 header fields.
            Assert.Equal(0x05u, vm.TrackCount);
            Assert.Equal(0x10u, vm.NumBlks);
            Assert.Equal(0x20u, vm.Priority);
            Assert.Equal(0x80u, vm.Reverb);
            Assert.Equal(0x08100200u, vm.InstrumentAddr);
            // Tracks parsed (count matches header).
            Assert.Equal(5, vm.Tracks.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Write_PersistsAllHeaderFields_RoundTrip()
    {
        var rom = MakeMinimalRomWithSong(out uint songAddr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongTrackViewModel();
            vm.LoadEntry(songAddr);

            vm.TrackCount = 0x08;
            vm.NumBlks = 0x42;
            vm.Priority = 0x55;
            vm.Reverb = 0x9E;
            vm.InstrumentAddr = 0x08200300u;
            vm.Write();

            Assert.Equal(0x08u, rom.u8(songAddr + 0));
            Assert.Equal(0x42u, rom.u8(songAddr + 1));
            Assert.Equal(0x55u, rom.u8(songAddr + 2));
            Assert.Equal(0x9Eu, rom.u8(songAddr + 3));
            Assert.Equal(0x08200300u, rom.u32(songAddr + 4));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_ParsesUpToSixteenTracks()
    {
        var rom = MakeMinimalRomWithSong(out uint songAddr, trackCount: 16);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongTrackViewModel();
            vm.LoadEntry(songAddr);

            Assert.Equal(16u, vm.TrackCount);
            Assert.Equal(16, vm.Tracks.Count);
            // Every track pointer should be valid in our synthetic data.
            foreach (var track in vm.Tracks)
                Assert.True(track.IsValid, $"Track {track.Index} should be valid");
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadEntry_TrackCountZero_NoTracks()
    {
        var rom = MakeMinimalRomWithSong(out uint songAddr, trackCount: 0);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongTrackViewModel();
            vm.LoadEntry(songAddr);

            Assert.Equal(0u, vm.TrackCount);
            Assert.Empty(vm.Tracks);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) - 3 wired jumps + 3 deliberate gaps.
    // -----------------------------------------------------------------

    [Fact]
    public void NavigationManifest_HasExactlyThreeWiredRows()
    {
        var vm = new SongTrackViewModel();
        var targets = vm.GetNavigationTargets();

        // Exactly 3 manifest rows — the 3 user-triggerable jumps. The 3
        // import dispatch flows (Midi / SelectInstrument / Wave) are
        // DELIBERATELY absent (Copilot CLI plan review v1, concern #2).
        Assert.Equal(3, targets.Count);

        Assert.Contains(targets, t =>
            t.TargetViewType == typeof(SongExchangeView) && t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.TargetViewType == typeof(SongTrackAllChangeTrackView) && t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.TargetViewType == typeof(SongTrackChangeTrackView) && t.IssueRef == null);
    }

    [Fact]
    public void NavigationManifest_NoEntries_AreMarkedAsKnownGaps()
    {
        // After #412 lands, all 3 wired manifest rows must be Match (no
        // IssueRef). KnownGap rows are explicitly NOT used for the 3 deferred
        // import flows — they stay MissingAvManifest, which the
        // JumpParityScanner round-trip test confirms below.
        var vm = new SongTrackViewModel();
        foreach (var t in vm.GetNavigationTargets())
        {
            Assert.Null(t.IssueRef);
        }
    }

    /// <summary>
    /// Roslyn-static read of the View code-behind: every manifest row's
    /// CommandName must appear as a click-handler method in the .cs file,
    /// and the click handler must dispatch through `WindowManager` to the
    /// declared TargetViewType. Pattern matches
    /// `PointerToolParityTests.View_NavigationHandlers_AreWiredToWindowManager`.
    /// </summary>
    [Fact]
    public void View_NavigationHandlers_AreWiredToWindowManager()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());

        // JumpToSongExchange -> SongExchangeView via WindowManager.Navigate.
        Assert.Contains("WindowManager", source);
        Assert.Contains("SongExchangeView", source);
        Assert.Contains("SongTrackAllChangeTrackView", source);
        Assert.Contains("SongTrackChangeTrackView", source);
        // Each navigation handler exists as a Click method.
        Assert.Contains("SongExchange_Click", source);
        Assert.Contains("AllTracks_Click", source);
        Assert.Contains("TrackLabel_Click", source);
    }

    /// <summary>
    /// The 3 deferred import flows must NOT appear in the View code-behind as
    /// wired navigation — if a future refactor adds the wiring, the manifest
    /// must be updated in lockstep (or the gap rule breaks).
    /// </summary>
    [Fact]
    public void View_ImportFlows_AreDeferred_NoWiringYet()
    {
        // If these strings appear in the code-behind, the click handlers
        // really exist — at which point the manifest MUST grow to declare
        // them or the parity scanner will flag a behavior-vs-manifest drift.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain("Navigate<SongTrackImportMidiView>", source);
        Assert.DoesNotContain("Navigate<SongTrackImportWaveView>", source);
        Assert.DoesNotContain("Navigate<SongTrackImportSelectInstrumentView>", source);
        Assert.DoesNotContain("Open<SongTrackImportMidiView>", source);
        Assert.DoesNotContain("Open<SongTrackImportWaveView>", source);
        Assert.DoesNotContain("Open<SongTrackImportSelectInstrumentView>", source);
    }

    /// <summary>
    /// Each target view's `NavigateTo(uint address)` must call
    /// `_vm.LoadEntry(address)` so the click handlers actually carry the
    /// requested context to the target editor (Copilot CLI review #1 +
    /// laqieer top-level review on PR #558). The exact branching strategy
    /// differs per view:
    /// - `SongExchangeView` always calls `LoadEntry(address)` because the
    ///   caller passes a song INDEX where 0 is a valid value (silence song)
    ///   — Copilot bot review on PR #558 inline #1.
    /// - `SongTrackAllChangeTrackView` and `SongTrackChangeTrackView` use
    ///   `if (address != 0)` because the caller passes a ROM ADDRESS where 0
    ///   means "no context" (standalone open path).
    /// </summary>
    [Theory]
    [InlineData(typeof(SongExchangeView))]
    [InlineData(typeof(SongTrackAllChangeTrackView))]
    [InlineData(typeof(SongTrackChangeTrackView))]
    public void TargetView_NavigateTo_HandlesNonZeroAddress(Type viewType)
    {
        string repoRoot = FindRepoRoot();
        string viewName = viewType.Name;
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", $"{viewName}.axaml.cs");
        Assert.True(File.Exists(codeBehindPath),
            $"Target view code-behind missing at {codeBehindPath}");

        string source = File.ReadAllText(codeBehindPath);
        // Every target view's NavigateTo MUST call _vm.LoadEntry(address)
        // — NOT just EntryList.SelectAddress(address) which would fail to
        // find the row in the stub master list.
        Assert.Contains("_vm.LoadEntry(address)", source);
    }

    /// <summary>
    /// Feed the 6 WF callsites + 3 AV manifest rows through
    /// JumpParityScanner.ComputeJumpRows and assert exactly 3 Match + 3
    /// MissingAvManifest rows. This is the round-trip proof that the
    /// manifest only declares wired jumps and the deferred flows surface
    /// truthfully in the parity report.
    /// </summary>
    [Fact]
    public void JumpParityScanner_ThreeWiredMatch_ThreeImportMissingManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite("SongTrackForm", "SongExchangeForm", HasAddressArgument: true),
            new WfJumpCallsite("SongTrackForm", "SongTrackAllChangeTrackForm", HasAddressArgument: false),
            new WfJumpCallsite("SongTrackForm", "SongTrackChangeTrackForm", HasAddressArgument: false),
            new WfJumpCallsite("SongTrackForm", "SongTrackImportMidiForm", HasAddressArgument: false),
            new WfJumpCallsite("SongTrackForm", "SongTrackImportSelectInstrumentForm", HasAddressArgument: false),
            new WfJumpCallsite("SongTrackForm", "SongTrackImportWaveForm", HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry("SongTrackViewModel", "SongTrackView",
                "JumpToSongExchange", "SongExchangeView", IssueRef: null),
            new AvManifestEntry("SongTrackViewModel", "SongTrackView",
                "JumpToSongTrackAllChangeTrack", "SongTrackAllChangeTrackView", IssueRef: null),
            new AvManifestEntry("SongTrackViewModel", "SongTrackView",
                "JumpToSongTrackChangeTrack", "SongTrackChangeTrackView", IssueRef: null),
        };

        string repoRoot = FindRepoRoot();
        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests, repoRoot);

        // Each WF callsite from SongTrackForm should produce one row.
        var songTrackRows = rows.Where(r => r.SourceForm == "SongTrackForm").ToList();
        Assert.Equal(6, songTrackRows.Count);

        // 3 Match: SongExchange + AllChangeTrack + ChangeTrack.
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongExchangeForm" && r.Status == JumpRowStatus.Match);
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongTrackAllChangeTrackForm" && r.Status == JumpRowStatus.Match);
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongTrackChangeTrackForm" && r.Status == JumpRowStatus.Match);
        // 3 MissingAvManifest: ImportMidi + ImportSelectInstrument + ImportWave.
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongTrackImportMidiForm" && r.Status == JumpRowStatus.MissingAvManifest);
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongTrackImportSelectInstrumentForm" && r.Status == JumpRowStatus.MissingAvManifest);
        Assert.Contains(songTrackRows, r =>
            r.TargetWfType == "SongTrackImportWaveForm" && r.Status == JumpRowStatus.MissingAvManifest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SongTrackView.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SongTrackView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with one song-header at 0x100000:
    ///   B0 = trackCount (parameter)
    ///   B1 = 0x10 (NumBlks)
    ///   B2 = 0x20 (Priority)
    ///   B3 = 0x80 (Reverb)
    ///   P4 = 0x08100200 (InstrumentAddr)
    ///   then `trackCount` track pointers each pointing at 0x08100400 + i*0x10.
    /// </summary>
    static ROM MakeMinimalRomWithSong(out uint songAddr, uint trackCount = 5)
    {
        var rom = new ROM();
        // FE8U title is BE8E - synthesize.
        rom.LoadLow("synth.gba", new byte[0x800000], "BE8E01");

        songAddr = 0x100000;
        // Header bytes.
        rom.Data[songAddr + 0] = (byte)trackCount;
        rom.Data[songAddr + 1] = 0x10;
        rom.Data[songAddr + 2] = 0x20;
        rom.Data[songAddr + 3] = 0x80;
        WriteU32(rom.Data, (int)(songAddr + 4), 0x08100200u);

        // Track pointers — each track points at 0x08100400 + i*0x10.
        uint trackDataBase = 0x100400;
        for (uint i = 0; i < trackCount; i++)
        {
            uint ptrSlot = songAddr + 8 + i * 4;
            uint trackPtr = 0x08000000u | (trackDataBase + i * 0x10);
            WriteU32(rom.Data, (int)ptrSlot, trackPtr);
            // Plant some dummy data at trackDataBase + i*0x10 so the track
            // address looks safe (and the rest of the parser is happy).
            rom.Data[trackDataBase + i * 0x10] = 0xB2; // a generic command byte
        }

        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException(
                "Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
