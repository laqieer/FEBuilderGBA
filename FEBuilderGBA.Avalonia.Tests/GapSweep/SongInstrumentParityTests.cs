// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for SongInstrumentView. (#387)
//
// Closes the 319 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SongInstrumentForm` (HIGH density 54/323 == -83.3 %, 50 WF-only labels,
// 14 tab pages). Mirrors the parity-test pattern from PR #558 (SongTrack).
//
// SongInstrument-specific shape: 14 explicit instrument tabs (UNIONTAB_N00..
// UNIONTAB_N80) declared as static AXAML elements so
// `ControlDensityScanner.CountAvControlsInDocument` actually counts them
// (controls inside DataTemplate / ItemsControl.ItemTemplate are skipped per
// Copilot CLI plan review v1, concern #1).
//
// Manifest contains zero wired jumps with `IssueRef: null`. The single WF
// `SongInstrumentImportWaveForm` dispatch flow stays deliberately ABSENT from
// the manifest — `JumpParityScanner.ComputeJumpRows` reports it as
// `MissingAvManifest`, which is the truthful state (Copilot CLI plan review
// v1, concern #3).
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
/// Tests proving the SongInstrument parity raise (#387) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SongInstrumentParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 323 control instantiations (per 2026-05-27
    /// density sweep). To leave the HIGH verdict we need
    /// AV >= ceil(323 * 0.75) = 243.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 323;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 243
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
        Assert.Contains("AutomationId=\"SongInstrument_ReadStartAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_ReadCount_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_ReloadList_Button\"", axaml);
    }

    [Fact]
    public void View_HasMasterAddressBar()
    {
        // WF AddressPanel: Address / BlockSize / SelectAddress / WriteButton.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongInstrument_Address_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_SelectedAddress_Label\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_Write_Button\"", axaml);
    }

    [Fact]
    public void View_HasCommonHeaderBar()
    {
        // WF panel2: L_0_COMBO (type), Info link, Inst_Export / Inst_Import,
        // X_MoreInfo text.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongInstrument_TypeCombo_Input\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_Info_Label\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_InstExport_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_InstImport_Button\"", axaml);
        Assert.Contains("AutomationId=\"SongInstrument_MoreInfo_Input\"", axaml);
    }

    [Fact]
    public void View_HasFingerprintFooter()
    {
        // WF panel4: FINGERPRINT text box.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SongInstrument_Fingerprint_Input\"", axaml);
    }

    /// <summary>
    /// All 14 instrument tabs must be explicit static AXAML controls
    /// (not behind DataTemplate / ItemTemplate). This is the cornerstone of
    /// the density gap fix — Copilot CLI plan review v1, concern #1.
    /// </summary>
    [Theory]
    [InlineData(0x00, "N00")] [InlineData(0x01, "N01")] [InlineData(0x02, "N02")]
    [InlineData(0x03, "N03")] [InlineData(0x04, "N04")] [InlineData(0x08, "N08")]
    [InlineData(0x09, "N09")] [InlineData(0x0A, "N0A")] [InlineData(0x0B, "N0B")]
    [InlineData(0x0C, "N0C")] [InlineData(0x10, "N10")] [InlineData(0x18, "N18")]
    [InlineData(0x40, "N40")] [InlineData(0x80, "N80")]
    public void View_HasInstrumentTab(byte headerByte, string tabName)
    {
        string axaml = ReadAxaml();
        // Each tab has a stable AutomationId SongInstrument_UNIONTAB_Nxx_Tab
        Assert.Contains($"AutomationId=\"SongInstrument_UNIONTAB_{tabName}_Tab\"", axaml);
        _ = headerByte; // header byte is asserted via tab selection test below
    }

    [Fact]
    public void View_AllTabControls_NotInsideTemplate()
    {
        // Regression guard: if a refactor moves the 14 tab controls into a
        // DataTemplate / ItemsControl, ControlDensityScanner will silently
        // drop them. This test reads the AXAML through the same XLinq pass
        // the scanner uses and asserts the 14 tab AutomationIds appear
        // outside any template container.
        var doc = XDocument.Load(AxamlPath());

        var templateContainers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Design.DataContext", "Style", "Styles",
            "DataTemplate", "ControlTemplate", "ItemTemplate",
            "ItemsPanelTemplate", "HierarchicalDataTemplate",
        };

        var ids = new List<string>();
        foreach (var el in doc.Descendants())
        {
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
                var ln = attr.Name.LocalName;
                if (ln == "AutomationId" || ln.EndsWith(".AutomationId", StringComparison.Ordinal))
                    ids.Add(attr.Value);
            }
        }

        string[] tabNames = { "N00", "N01", "N02", "N03", "N04", "N08", "N09",
                              "N0A", "N0B", "N0C", "N10", "N18", "N40", "N80" };
        foreach (var t in tabNames)
        {
            Assert.Contains($"SongInstrument_UNIONTAB_{t}_Tab", ids);
        }
    }

    // -----------------------------------------------------------------
    // Per-tab field surface: each tab must expose the WF-equivalent bytes.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("N00", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N08", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N10", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N18", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N01", new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11" })]
    [InlineData("N02", new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11" })]
    [InlineData("N09", new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11" })]
    [InlineData("N0A", new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11" })]
    [InlineData("N03", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N0B", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N04", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N0C", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    [InlineData("N40", new[] { "B1", "B2", "B3", "P4", "P8" })]
    [InlineData("N80", new[] { "B1", "B2", "B3", "P4", "B8", "B9", "B10", "B11" })]
    public void View_TabExposesAllWfBytes(string tabName, string[] expectedFields)
    {
        string axaml = ReadAxaml();
        foreach (var f in expectedFields)
        {
            Assert.Contains(
                $"AutomationId=\"SongInstrument_{tabName}_{f}_Input\"",
                axaml);
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
    // Tab selection - exact byte map (concern v2 #1).
    // -----------------------------------------------------------------

    /// <summary>
    /// The map from header byte to expected tab AutomationId must cover all
    /// 14 WF UNIONTAB_Nxx values. Category-based selection collapses 14 tabs
    /// into 6 categories and cannot distinguish e.g. N00 vs N08 — Copilot
    /// CLI plan review v2, concern #1.
    /// </summary>
    [Theory]
    [InlineData((byte)0x00, "SongInstrument_UNIONTAB_N00_Tab")]
    [InlineData((byte)0x01, "SongInstrument_UNIONTAB_N01_Tab")]
    [InlineData((byte)0x02, "SongInstrument_UNIONTAB_N02_Tab")]
    [InlineData((byte)0x03, "SongInstrument_UNIONTAB_N03_Tab")]
    [InlineData((byte)0x04, "SongInstrument_UNIONTAB_N04_Tab")]
    [InlineData((byte)0x08, "SongInstrument_UNIONTAB_N08_Tab")]
    [InlineData((byte)0x09, "SongInstrument_UNIONTAB_N09_Tab")]
    [InlineData((byte)0x0A, "SongInstrument_UNIONTAB_N0A_Tab")]
    [InlineData((byte)0x0B, "SongInstrument_UNIONTAB_N0B_Tab")]
    [InlineData((byte)0x0C, "SongInstrument_UNIONTAB_N0C_Tab")]
    [InlineData((byte)0x10, "SongInstrument_UNIONTAB_N10_Tab")]
    [InlineData((byte)0x18, "SongInstrument_UNIONTAB_N18_Tab")]
    [InlineData((byte)0x40, "SongInstrument_UNIONTAB_N40_Tab")]
    [InlineData((byte)0x80, "SongInstrument_UNIONTAB_N80_Tab")]
    public void ViewModel_GetExpectedTabId_ExactByteMap(byte headerByte, string expectedTabId)
    {
        string tabId = SongInstrumentViewModel.GetExpectedTabId(headerByte);
        Assert.Equal(expectedTabId, tabId);
    }

    [Fact]
    public void ViewModel_GetExpectedTabId_UnknownByte_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SongInstrumentViewModel.GetExpectedTabId(0xFF));
        Assert.Equal(string.Empty, SongInstrumentViewModel.GetExpectedTabId(0x42));
    }

    // -----------------------------------------------------------------
    // Per-tab byte round-trip (concern v2 #2).
    // Each tab seeds distinct non-zero values into every WF-exposed byte
    // and confirms all bytes survive Write -> reload.
    // -----------------------------------------------------------------

    /// <summary>
    /// DirectSound family tabs (N00/N08/N10/N18): WF exposes B0..B3, P4
    /// (u32), and B8..B11. All 12 bytes must round-trip.
    /// </summary>
    [Theory]
    [InlineData((byte)0x00)] [InlineData((byte)0x08)]
    [InlineData((byte)0x10)] [InlineData((byte)0x18)]
    public void ViewModel_Write_DirectSound_RoundTrip_AllBytes(byte headerByte)
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            // Seed every byte the tab exposes with a distinct non-zero value.
            vm.CurrentAddr = addr;
            vm.HeaderByte = headerByte;
            vm.Category = SongInstrumentViewModel.ClassifyType(headerByte);
            vm.B1 = 0x11; vm.B2 = 0x22; vm.B3 = 0x33;
            vm.WavePtr = 0x08200400u;
            vm.Attack = 0x88; vm.Decay = 0x99;
            vm.Sustain = 0xAA; vm.Release = 0xBB;
            vm.Write();

            // Reload and assert every byte survived.
            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal(headerByte, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.B1);
            Assert.Equal(0x22, vm2.B2);
            Assert.Equal(0x33, vm2.B3);
            Assert.Equal(0x08200400u, vm2.WavePtr);
            Assert.Equal(0x88, vm2.Attack);
            Assert.Equal(0x99, vm2.Decay);
            Assert.Equal(0xAA, vm2.Sustain);
            Assert.Equal(0xBB, vm2.Release);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// SquareWave family tabs (N01/N02/N09/N0A): WF exposes B0..B11 raw
    /// (including B4=squarepattern). All 12 bytes must round-trip.
    /// </summary>
    [Theory]
    [InlineData((byte)0x01)] [InlineData((byte)0x02)]
    [InlineData((byte)0x09)] [InlineData((byte)0x0A)]
    public void ViewModel_Write_SquareWave_RoundTrip_AllBytes(byte headerByte)
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            vm.CurrentAddr = addr;
            vm.HeaderByte = headerByte;
            vm.Category = SongInstrumentViewModel.ClassifyType(headerByte);
            vm.Sweep = 0x11;     // B1
            vm.DutyLen = 0x22;   // B2
            vm.EnvStep = 0x33;   // B3
            vm.B4 = 0x44;        // B4 (squarepattern — concern v2 #2)
            vm.B5 = 0x55;
            vm.B6 = 0x66;
            vm.B7 = 0x77;
            vm.Attack = 0x88; vm.Decay = 0x99;
            vm.Sustain = 0xAA; vm.Release = 0xBB;
            vm.Write();

            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal(headerByte, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.Sweep);
            Assert.Equal(0x22, vm2.DutyLen);
            Assert.Equal(0x33, vm2.EnvStep);
            Assert.Equal(0x44, vm2.B4);
            Assert.Equal(0x55, vm2.B5);
            Assert.Equal(0x66, vm2.B6);
            Assert.Equal(0x77, vm2.B7);
            Assert.Equal(0x88, vm2.Attack);
            Assert.Equal(0x99, vm2.Decay);
            Assert.Equal(0xAA, vm2.Sustain);
            Assert.Equal(0xBB, vm2.Release);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// WaveMemory family tabs (N03/N0B): same shape as DirectSound but no
    /// ADSR-pad bytes used (WF still exposes B8..B11).
    /// </summary>
    [Theory]
    [InlineData((byte)0x03)] [InlineData((byte)0x0B)]
    public void ViewModel_Write_WaveMemory_RoundTrip_AllBytes(byte headerByte)
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            vm.CurrentAddr = addr;
            vm.HeaderByte = headerByte;
            vm.Category = SongInstrumentViewModel.ClassifyType(headerByte);
            vm.B1 = 0x11; vm.B2 = 0x22; vm.B3 = 0x33;
            vm.WavePtr = 0x08200500u;
            vm.Attack = 0x88; vm.Decay = 0x99;
            vm.Sustain = 0xAA; vm.Release = 0xBB;
            vm.Write();

            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal(headerByte, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.B1);
            Assert.Equal(0x22, vm2.B2);
            Assert.Equal(0x33, vm2.B3);
            Assert.Equal(0x08200500u, vm2.WavePtr);
            Assert.Equal(0x88, vm2.Attack);
            Assert.Equal(0x99, vm2.Decay);
            Assert.Equal(0xAA, vm2.Sustain);
            Assert.Equal(0xBB, vm2.Release);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Noise family tabs (N04/N0C): WF exposes B4 as Period.
    /// </summary>
    [Theory]
    [InlineData((byte)0x04)] [InlineData((byte)0x0C)]
    public void ViewModel_Write_Noise_RoundTrip_AllBytes(byte headerByte)
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            vm.CurrentAddr = addr;
            vm.HeaderByte = headerByte;
            vm.Category = SongInstrumentViewModel.ClassifyType(headerByte);
            vm.B1 = 0x11; vm.B2 = 0x22; vm.B3 = 0x33;
            vm.Period = 0x44;  // B4 = noisepattern
            vm.Attack = 0x88; vm.Decay = 0x99;
            vm.Sustain = 0xAA; vm.Release = 0xBB;
            vm.Write();

            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal(headerByte, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.B1);
            Assert.Equal(0x22, vm2.B2);
            Assert.Equal(0x33, vm2.B3);
            Assert.Equal(0x44, vm2.Period);
            Assert.Equal(0x88, vm2.Attack);
            Assert.Equal(0x99, vm2.Decay);
            Assert.Equal(0xAA, vm2.Sustain);
            Assert.Equal(0xBB, vm2.Release);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// MultiSample tab (N40): WF exposes B0..B3, P4 (u32, KeyMapPtr),
    /// P8 (u32, SubInstrPtr). No B8..B11.
    /// </summary>
    [Fact]
    public void ViewModel_Write_MultiSample_RoundTrip_AllBytes()
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            vm.CurrentAddr = addr;
            vm.HeaderByte = 0x40;
            vm.Category = SongInstrumentViewModel.ClassifyType(0x40);
            vm.B1 = 0x11; vm.B2 = 0x22; vm.B3 = 0x33;
            vm.KeyMapPtr = 0x08200600u;
            vm.SubInstrPtr = 0x08200700u;
            vm.Write();

            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal((byte)0x40, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.B1);
            Assert.Equal(0x22, vm2.B2);
            Assert.Equal(0x33, vm2.B3);
            Assert.Equal(0x08200600u, vm2.KeyMapPtr);
            Assert.Equal(0x08200700u, vm2.SubInstrPtr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Drum tab (N80): WF exposes B0..B3, P4 (u32, SubInstrPtr),
    /// AND B8..B11 as raw user-editable bytes. This is the regression
    /// guard for Copilot CLI plan review v2 concern #2 — the prior VM
    /// wrote zeros to addr+8..11 and dropped user edits.
    /// </summary>
    [Fact]
    public void ViewModel_Write_Drum_RoundTrip_AllBytes_IncludingB8B9B10B11()
    {
        var rom = MakeMinimalRom(out uint addr);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new SongInstrumentViewModel();

            vm.CurrentAddr = addr;
            vm.HeaderByte = 0x80;
            vm.Category = SongInstrumentViewModel.ClassifyType(0x80);
            vm.B1 = 0x11; vm.B2 = 0x22; vm.B3 = 0x33;
            vm.SubInstrPtr = 0x08200800u;
            // B8..B11 — previously dropped. Must round-trip per concern v2 #2.
            vm.B8 = 0x88; vm.B9 = 0x99;
            vm.B10 = 0xAA; vm.B11 = 0xBB;
            vm.Write();

            var vm2 = new SongInstrumentViewModel();
            vm2.LoadEntry(addr);

            Assert.Equal((byte)0x80, vm2.HeaderByte);
            Assert.Equal(0x11, vm2.B1);
            Assert.Equal(0x22, vm2.B2);
            Assert.Equal(0x33, vm2.B3);
            Assert.Equal(0x08200800u, vm2.SubInstrPtr);
            Assert.Equal(0x88, vm2.B8);
            Assert.Equal(0x99, vm2.B9);
            Assert.Equal(0xAA, vm2.B10);
            Assert.Equal(0xBB, vm2.B11);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) - empty (concern v1 #3).
    // -----------------------------------------------------------------

    [Fact]
    public void NavigationManifest_IsEmpty_ZeroWiredJumps()
    {
        // The only WF jump callsite (`SongInstrumentImportWaveForm`) is
        // deferred — the import-wave flow remains WinForms-coupled. The
        // manifest is empty and the JumpParityScanner reports the single WF
        // callsite as `MissingAvManifest` (the truthful state). When a
        // future PR wires the WindowManager.Navigate path, one row is added
        // to the manifest in lockstep.
        var vm = new SongInstrumentViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Empty(targets);
    }

    /// <summary>
    /// The deferred WF import-wave flow must NOT appear in the View
    /// code-behind as wired navigation. If a future refactor adds it, the
    /// manifest must be updated in lockstep.
    /// </summary>
    [Fact]
    public void View_ImportFlows_AreDeferred_NoWiringYet()
    {
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain("Navigate<SongInstrumentImportWaveView>", source);
        Assert.DoesNotContain("Open<SongInstrumentImportWaveView>", source);
    }

    /// <summary>
    /// Feed the 1 WF callsite + 0 AV manifest rows through
    /// JumpParityScanner.ComputeJumpRows and assert exactly 0 Match + 1
    /// MissingAvManifest row. Proves the truthful-state convention.
    /// </summary>
    [Fact]
    public void JumpParityScanner_ZeroMatch_OneMissingManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite("SongInstrumentForm", "SongInstrumentImportWaveForm", HasAddressArgument: false),
        };

        var avManifests = new AvManifestEntry[0];

        string repoRoot = FindRepoRoot();
        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests, repoRoot);

        var siRows = rows.Where(r => r.SourceForm == "SongInstrumentForm").ToList();
        Assert.Single(siRows);
        Assert.Equal("SongInstrumentImportWaveForm", siRows[0].TargetWfType);
        Assert.Equal(JumpRowStatus.MissingAvManifest, siRows[0].Status);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SongInstrumentView.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SongInstrumentView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with 12 zero bytes at 0x100000 for
    /// instrument writes.
    /// </summary>
    static ROM MakeMinimalRom(out uint instrAddr)
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x800000], "BE8E01");
        instrAddr = 0x100000;
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
            throw new InvalidOperationException(
                "Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
