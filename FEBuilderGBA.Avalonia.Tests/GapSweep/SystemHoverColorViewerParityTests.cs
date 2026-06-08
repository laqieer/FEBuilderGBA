// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for SystemHoverColorViewerView write path (#1007).
//
// Verifies the editor's write round-trip, guard conditions, color-list
// loading, and View structural requirements (AutomationIds, Write_Click
// wiring, UndoService scope).

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the SystemHoverColorViewerView write path (#1007) is
/// permanent.  Marked [Collection("SharedState")] because tests that
/// exercise CoreState.ROM / CoreState.Undo must serialize with all other
/// CoreState-mutating tests.
/// </summary>
[Collection("SharedState")]
public class SystemHoverColorViewerParityTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a minimal synthetic FE8U ROM (17 MB) and stamp a u16 at
    /// <paramref name="addr"/> with <paramref name="value"/>.
    /// </summary>
    static ROM MakeRomWithU16(uint addr, ushort value)
    {
        var bytes = new byte[0x1100000];
        BitConverter.GetBytes(value).CopyTo(bytes, (int)addr);
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    /// <summary>
    /// Minimal FE8U ROM with a populated
    /// <c>systemarea_move_gradation_palette_pointer</c> table.
    ///
    /// Layout:
    ///   bytes[pointerSlot .. +4] = GBA pointer to colorBase
    ///   bytes[colorBase .. +20]  = 10 u16 color entries
    /// </summary>
    static ROM MakeRomWithColorTable(out uint colorBase)
    {
        var bytes = new byte[0x1100000];
        var tmpRom = new ROM();
        tmpRom.LoadLow("tmp.gba", bytes, "BE8E01");

        // Pick a safe raw ROM address and convert to a GBA pointer.
        colorBase = 0x00200000u; // raw offset = 0x200000
        uint gbaPtr = colorBase | 0x08000000u;

        // Plant 10 u16 color entries: 0x0001, 0x0002, … 0x000A
        for (int i = 0; i < 10; i++)
        {
            uint off = colorBase + (uint)(i * 2);
            BitConverter.GetBytes((ushort)(i + 1)).CopyTo(bytes, (int)off);
        }

        // Write the GBA pointer into the pointer slot that
        // systemarea_move_gradation_palette_pointer points to.
        uint pointerSlot = tmpRom.RomInfo.systemarea_move_gradation_palette_pointer;
        BitConverter.GetBytes(gbaPtr).CopyTo(bytes, (int)pointerSlot);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------------
    // VM write round-trip (including undo)
    // -----------------------------------------------------------------------

    [Fact]
    public void VM_WriteRoundTrip_UpdatesRomAndRestoresOnUndo()
    {
        const uint ColorAddr = 0x00100000u;
        const ushort Original = 0x1234;
        const ushort NewValue = 0x5678;

        ROM rom = MakeRomWithU16(ColorAddr, Original);

        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new SystemHoverColorViewerViewModel();
            vm.LoadHoverColor(ColorAddr);

            // GBAColor should be set to the planted value after LoadHoverColor.
            Assert.Equal(Original, (ushort)vm.GBAColor);

            // Verify decoded R/G/B
            Assert.Equal((byte)((Original & 0x1F) * 8), vm.ColorR);
            Assert.Equal((byte)(((Original >> 5) & 0x1F) * 8), vm.ColorG);
            Assert.Equal((byte)(((Original >> 10) & 0x1F) * 8), vm.ColorB);

            // Open undo scope, modify GBAColor, write.
            var undoSvc = new UndoService();
            undoSvc.Begin("TestWrite");
            vm.GBAColor = NewValue;
            uint res = vm.Write();
            undoSvc.Commit();

            // Write must return the packed value.
            Assert.Equal((ushort)NewValue, (ushort)res);
            Assert.NotEqual(U.NOT_FOUND, res);

            // ROM must reflect the new value.
            Assert.Equal(NewValue, (ushort)rom.u16(ColorAddr));

            // ColorR/G/B must be updated.
            Assert.Equal((byte)((NewValue & 0x1F) * 8), vm.ColorR);
            Assert.Equal((byte)(((NewValue >> 5) & 0x1F) * 8), vm.ColorG);
            Assert.Equal((byte)(((NewValue >> 10) & 0x1F) * 8), vm.ColorB);

            // Undo must restore original value.
            CoreState.Undo.RunUndo();
            Assert.Equal(Original, (ushort)rom.u16(ColorAddr));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------------
    // VM live-sync: setting GBAColor keeps ColorR/G/B decoded in sync (#1044)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData((ushort)0x0000)]   // black
    [InlineData((ushort)0x7FFF)]   // white (R=G=B=31)
    [InlineData((ushort)0x001F)]   // pure red  (R=31)
    [InlineData((ushort)0x03E0)]   // pure green (G=31)
    [InlineData((ushort)0x7C00)]   // pure blue  (B=31)
    [InlineData((ushort)0x5678)]   // mixed
    public void VM_SetGBAColor_KeepsColorChannelsInSync(ushort value)
    {
        // No ROM / CurrentAddr needed: this proves the property setter re-decodes
        // ColorR/G/B live, so the View's bound NUDs update while editing — before
        // any Write. (Copilot bot review on PR #1044.)
        var vm = new SystemHoverColorViewerViewModel();
        vm.GBAColor = value;

        Assert.Equal((byte)((value & 0x1F) * 8), vm.ColorR);
        Assert.Equal((byte)(((value >> 5) & 0x1F) * 8), vm.ColorG);
        Assert.Equal((byte)(((value >> 10) & 0x1F) * 8), vm.ColorB);
    }

    // -----------------------------------------------------------------------
    // VM guard: Write returns NOT_FOUND when CanWrite==false or addr==0
    // -----------------------------------------------------------------------

    [Fact]
    public void VM_Write_GuardFailsWhenCanWriteFalse()
    {
        ROM rom = MakeRomWithU16(0x100000, 0xABCD);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // Fresh VM: CanWrite is false, CurrentAddr is 0.
            var vm = new SystemHoverColorViewerViewModel();
            Assert.False(vm.CanWrite);
            Assert.Equal(0u, vm.CurrentAddr);

            uint res = vm.Write();
            Assert.Equal(U.NOT_FOUND, res);

            // ROM must not be touched.
            Assert.Equal((ushort)0xABCD, (ushort)rom.u16(0x100000));
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void VM_Write_GuardFailsWhenCurrentAddrIsZero()
    {
        ROM rom = MakeRomWithU16(0x100000, 0x0001);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            var vm = new SystemHoverColorViewerViewModel();
            // Manually set CanWrite but leave CurrentAddr = 0.
            vm.CanWrite = true;
            vm.GBAColor = 0x7FFF;

            uint res = vm.Write();
            Assert.Equal(U.NOT_FOUND, res);
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    // -----------------------------------------------------------------------
    // VM list: LoadColorList returns 10 entries with correct addresses
    // -----------------------------------------------------------------------

    [Fact]
    public void VM_LoadColorList_Returns10EntriesWithCorrectAddresses()
    {
        ROM rom = MakeRomWithColorTable(out uint colorBase);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            var vm = new SystemHoverColorViewerViewModel();
            var list = vm.LoadColorList(0); // filter 0 = Move Range

            Assert.Equal(10, list.Count);
            for (int i = 0; i < 10; i++)
            {
                uint expectedAddr = colorBase + (uint)(i * 2);
                Assert.Equal(expectedAddr, list[i].addr);
                // tag carries the raw u16 color value planted above.
                Assert.Equal((uint)(i + 1), list[i].tag);
            }
        }
        finally
        {
            CoreState.ROM = prevRom;
        }
    }

    // -----------------------------------------------------------------------
    // View static checks: AXAML has the required AutomationIds and code-behind
    // has the required Write_Click wiring.
    // -----------------------------------------------------------------------

    [Fact]
    public void View_Axaml_ContainsRequiredAutomationIds()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SystemHoverColorViewerView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found: {axamlPath}");

        string content = File.ReadAllText(axamlPath);
        string[] required =
        {
            "SystemHoverColorViewer_GBAColor_Input",
            "SystemHoverColorViewer_ColorR_Input",
            "SystemHoverColorViewer_ColorG_Input",
            "SystemHoverColorViewer_ColorB_Input",
            "SystemHoverColorViewer_Write_Button",
        };
        foreach (string id in required)
            Assert.Contains(id, content);
    }

    [Fact]
    public void View_Axaml_WriteButton_HasCanWriteBinding_And_WriteClickHandler()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SystemHoverColorViewerView.axaml");
        string content = File.ReadAllText(axamlPath);

        Assert.Contains("Click=\"Write_Click\"", content);
        Assert.Contains("IsEnabled=\"{Binding CanWrite}\"", content);
    }

    [Fact]
    public void View_CodeBehind_WritesViaUndoServicePattern()
    {
        string repoRoot = FindRepoRoot();
        string csPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SystemHoverColorViewerView.axaml.cs");
        Assert.True(File.Exists(csPath), $"Code-behind not found: {csPath}");

        string content = File.ReadAllText(csPath);

        // Must have an UndoService field.
        Assert.Contains("_undoService", content);
        // Write_Click must exist.
        Assert.Contains("Write_Click", content);
        // UndoService scope: Begin + Commit + Rollback.
        Assert.Contains("_undoService.Begin(", content);
        Assert.Contains("Commit()", content);
        Assert.Contains("Rollback()", content);
        // Must call _vm.Write().
        Assert.Contains("_vm.Write(", content);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
