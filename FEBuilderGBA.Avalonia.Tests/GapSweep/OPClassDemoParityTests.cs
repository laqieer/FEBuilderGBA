// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1+2+5+6 gap-sweep regression tests for OPClassDemoViewerView. (#419)
//
// Closes the 32 control gap + 27 WF-only labels surfaced by the gap-sweep
// methodology on OPClassDemoForm (HIGH density 31/63, -50.8 %). After this
// PR the view rebuilds to a three-pane master-detail editor (main list /
// detail panel with previews / two sub-list panels) + a top read-config
// bar + an address-write bar, with patch-aware affordances for
// OPClassReelSort (ListExpand button) and OPClassReelAnimationIDOver255
// (BattleAnime+1 label).
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the OPClassDemo parity raise (#419) is permanent.
/// Density target: AV ≥ ceil(WF * 0.75) = 48.
///
/// Marked [Collection("SharedState")] because the VM tests mutate
/// CoreState.ROM to plant synthetic ROMs.
/// </summary>
[Collection("SharedState")]
public class OPClassDemoParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer reports 63 controls; the MEDIUM threshold is
    /// `ceil(63 * 0.75) = 48`. Falling below this re-enters HIGH territory.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "OPClassDemoViewerView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 63;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 48
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be ≥ {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Phase 5 — control surface assertions (static AXAML inspection).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasFilterAndReloadBar()
    {
        string axaml = ReadAxaml();
        // #649: ReadStart / ReadCount migrated to EditorTopBar's read-only
        // TextBlock slots — `_Input` renamed to `_Label` to reflect the
        // actual UIAutomation control type (Text, not Spinner). The reload
        // button now fires via the unified EditorTopBar's ReloadRequested
        // routed event (OnTopBarReloadRequested in the code-behind).
        Assert.Contains("StartAddressAutomationId=\"OPClassDemoViewer_ReadStartAddress_Label\"", axaml);
        Assert.Contains("ReadCountAutomationId=\"OPClassDemoViewer_ReadCount_Label\"", axaml);
        Assert.Contains("ReloadAutomationId=\"OPClassDemoViewer_ReloadList_Button\"", axaml);
        Assert.Contains("OnTopBarReloadRequested", axaml);
    }

    [Fact]
    public void View_HasAddressWriteBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_Addr_Label\"", axaml);
    }

    [Fact]
    public void View_HasN1JpFontSublist()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N1_List\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N1_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N1_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N1_Write_Button\"", axaml);
        Assert.Contains("Click=\"N1_Write_Click\"", axaml);
    }

    [Fact]
    public void View_HasN2AnimeSublist()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N2_List\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N2_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N2_B1_Input\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N2_Cmd_Combo\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_N2_Write_Button\"", axaml);
        Assert.Contains("Click=\"N2_Write_Click\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_ListExpand_Button\"", axaml);
        Assert.Contains("Click=\"ListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasPatchNoticeAndPlus1Label()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_PatchNotice_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_BattleAnimePlus1_Label\"", axaml);
    }

    [Fact]
    public void View_HasInlinePreviewLabels()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_PaletteId_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_ClassName_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_BattleAnime_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_TerrainLeft_Label\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_TerrainRight_Label\"", axaml);
    }

    [Fact]
    public void View_HasComboBoxesForAllyAndMagic()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"OPClassDemoViewer_AllyEnemyColor_Combo\"", axaml);
        Assert.Contains("AutomationId=\"OPClassDemoViewer_MagicEffect_Combo\"", axaml);
    }

    // -----------------------------------------------------------------
    // Roslyn AST walk — assert all four handlers use _undoService.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N1WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N2WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_ListExpandHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+ListExpand_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+ListExpand_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+ListExpand_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_ListExpandHandler_CallsDataExpansionCore()
    {
        string source = ReadCodeBehind();
        // The handler must delegate to _vm.ExpandList() which itself calls
        // DataExpansionCore.ExpandTable; the regression here is that the
        // view actually invokes the VM helper (not a stale stub).
        Assert.Matches(new Regex(@"void\s+ListExpand_Click[\s\S]*?_vm\.ExpandList\(", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_LoadN1Sublist_ResetsSelectedAddressOnEntryChange()
    {
        // Copilot CLI re-review on PR #544 #4 — Load*Sublist must reset
        // _n*SelectedAddr before replacing the list so a stale selection
        // from the previous row cannot land in a Write_Click against the
        // wrong address. The handler regex looks for the explicit reset
        // assignment at the top of each Load*Sublist body.
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+LoadN1Sublist[\s\S]*?_n1SelectedAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_LoadN2Sublist_ResetsSelectedAddressOnEntryChange()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+LoadN2Sublist[\s\S]*?_n2SelectedAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    // -----------------------------------------------------------------
    // ViewModel sublist walks (synthetic FE8U ROM)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadN1FontList_StopsAt0xFF()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Plant a glyph stream at 0x00820000: 5 bytes then 0xFF then junk.
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            for (int i = 0; i < 5; i++) bytes[baseAddr + i] = (byte)(0x10 + i);
            bytes[baseAddr + 5] = 0xFF;
            bytes[baseAddr + 6] = 0x42; // junk past terminator
            // Plant a GBA pointer at slot 0x00830000 → 0x00820000.
            uint slot = 0x00830000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new OPClassDemoViewerViewModel();
            var rows = vm.LoadN1FontList(slot);
            Assert.Equal(5, rows.Count);
            Assert.Equal(0x10u, rows[0].GlyphId);
            Assert.Equal(0x14u, rows[4].GlyphId);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN1FontList_RespectsMax16Cap()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            // Plant 32 non-FF bytes — walker must stop at index 16.
            for (int i = 0; i < 32; i++) bytes[baseAddr + i] = (byte)(0x20 + i);
            uint slot = 0x00830000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new OPClassDemoViewerViewModel();
            var rows = vm.LoadN1FontList(slot);
            Assert.Equal(16, rows.Count);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN1FontList_NullRom_ReturnsEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new OPClassDemoViewerViewModel();
            var rows = vm.LoadN1FontList(0x00830000u);
            Assert.Empty(rows);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN2CommandList_StopsAt0x00()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            byte[] bytes = rom.Data;
            uint baseAddr = 0x00840000u;
            // Plant 3 (Cmd, Arg) rows then Cmd=0x00 terminator.
            for (int i = 0; i < 3; i++)
            {
                bytes[baseAddr + i * 2] = (byte)(0x01 + i);     // Cmd
                bytes[baseAddr + i * 2 + 1] = (byte)(0x10 * i); // Arg
            }
            bytes[baseAddr + 3 * 2] = 0x00; // terminator
            bytes[baseAddr + 3 * 2 + 1] = 0x42; // junk
            uint slot = 0x00850000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new OPClassDemoViewerViewModel();
            var rows = vm.LoadN2CommandList(slot);
            Assert.Equal(3, rows.Count);
            Assert.Equal(0x01u, rows[0].Command);
            Assert.Equal(0x00u, rows[0].Argument);
            Assert.Equal(0x03u, rows[2].Command);
            Assert.Equal(0x20u, rows[2].Argument);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Pointer-aware write semantics (P0 / P8 / P24) — issue raised by
    // Copilot CLI re-review on PR #544. The VM must read JapaneseNamePointer /
    // AnimePointer as offsets (via rom.p32) and write them back with the
    // 0x08000000 high bit applied (via rom.write_p32).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteOPClassDemo_StoresJapaneseNamePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            // Plant a valid p0 at the entry so LoadOPClassDemo can be
            // called later without tripping pointer-safety checks
            // (not strictly needed here — we test the write path).
            uint entryAddr = 0x00800000u;
            vm.CurrentAddr = entryAddr;
            vm.JapaneseNamePointer = 0x00200000u; // ROM offset, NO high bit.
            vm.WriteOPClassDemo();

            // Read raw u32: should have GBA 0x08000000 high bit.
            uint raw = rom.u32(entryAddr + 8);
            uint decoded = rom.p32(entryAddr + 8);
            Assert.Equal(0x00200000u | 0x08000000u, raw);
            Assert.Equal(0x00200000u, decoded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteOPClassDemo_StoresAnimePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            uint entryAddr = 0x00800100u;
            vm.CurrentAddr = entryAddr;
            vm.AnimePointer = 0x00300000u;
            vm.WriteOPClassDemo();

            uint raw = rom.u32(entryAddr + 24);
            uint decoded = rom.p32(entryAddr + 24);
            Assert.Equal(0x00300000u | 0x08000000u, raw);
            Assert.Equal(0x00300000u, decoded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteOPClassDemo_StoresEnglishNamePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            uint entryAddr = 0x00800200u;
            vm.CurrentAddr = entryAddr;
            vm.EnglishNamePointer = 0x00400000u;
            vm.WriteOPClassDemo();

            uint raw = rom.u32(entryAddr + 0);
            uint decoded = rom.p32(entryAddr + 0);
            Assert.Equal(0x00400000u | 0x08000000u, raw);
            Assert.Equal(0x00400000u, decoded);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadOPClassDemo_ReadsPointersAsOffsets()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Plant raw GBA pointer bytes at the test address.
            uint entryAddr = 0x00800300u;
            uint gbaPointer = 0x08500000u;
            BitConverter.GetBytes(gbaPointer).CopyTo(rom.Data, entryAddr + 0);  // P0
            BitConverter.GetBytes(gbaPointer + 0x1000).CopyTo(rom.Data, entryAddr + 8);  // P8
            BitConverter.GetBytes(gbaPointer + 0x2000).CopyTo(rom.Data, entryAddr + 24); // P24

            var vm = new OPClassDemoViewerViewModel();
            vm.LoadOPClassDemo(entryAddr);

            // p32 strips the 0x08000000 high bit, so the ViewModel
            // observable property must report the OFFSET only.
            Assert.Equal(0x00500000u, vm.EnglishNamePointer);
            Assert.Equal(0x00501000u, vm.JapaneseNamePointer);
            Assert.Equal(0x00502000u, vm.AnimePointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Patch presence flags
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_PatchPresence_OPClassReelAnimationIDOver255_FE8J_PatchPresent()
    {
        ROM rom = MakeMinimalFe8jRom(plantOver255: true);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            Assert.True(vm.IsOver255PatchActive);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_PatchPresence_OPClassReelAnimationIDOver255_FE8U_AlwaysFalse()
    {
        ROM rom = MakeMinimalFe8uRom();
        // Synthetic write of FE8J signature at FE8J offset — must NOT match on FE8U.
        rom.Data[0xB86B0] = 0x59;
        rom.Data[0xB86B0 + 1] = 0x8A;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            Assert.False(vm.IsOver255PatchActive);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_PatchPresence_OPClassReelSort_FE8J_PatchPresent()
    {
        ROM rom = MakeMinimalFe8jRom(plantReelSort: true);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            Assert.True(vm.IsReelSortPatchActive);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_PatchPresence_OPClassReelSort_FE8U_PatchPresent()
    {
        // OPClassReelSort table has both FE8J AND FE8U entries.
        ROM rom = MakeMinimalFe8uRom();
        rom.Data[0xB40EC] = 0x04;
        rom.Data[0xB40EC + 1] = 0x4B;
        rom.Data[0xB40EC + 2] = 0x1B;
        rom.Data[0xB40EC + 3] = 0x68;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new OPClassDemoViewerViewModel();
            Assert.True(vm.IsReelSortPatchActive);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>Minimal FE8U ROM (BE8E01 + 0x1100000 bytes).</summary>
    static ROM MakeMinimalFe8uRom()
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    /// <summary>Minimal FE8J ROM optionally seeded with the Over255 / ReelSort signatures.</summary>
    static ROM MakeMinimalFe8jRom(bool plantOver255 = false, bool plantReelSort = false)
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        if (plantOver255)
        {
            data[0xB86B0] = 0x59;
            data[0xB86B0 + 1] = 0x8A;
        }
        if (plantReelSort)
        {
            data[0xB8C80] = 0x04;
            data[0xB8C80 + 1] = 0x4B;
            data[0xB8C80 + 2] = 0x1B;
            data[0xB8C80 + 3] = 0x68;
        }
        rom.LoadLow("synthetic-fe8j.gba", data, "BE8J01");
        return rom;
    }

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "OPClassDemoViewerView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "OPClassDemoViewerView.axaml.cs"));

    /// <summary>Walk parents from test bin dir until we find FEBuilderGBA.sln.</summary>
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
