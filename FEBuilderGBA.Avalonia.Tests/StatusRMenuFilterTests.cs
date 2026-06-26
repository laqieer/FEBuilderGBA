// SPDX-License-Identifier: GPL-3.0-or-later
// #1459 — the Avalonia Status R-Menu editor only ever read
// status_rmenu_unit_pointer (table 0) with a weak linear +i*28 scan, so 5 of the
// up-to-6 RMenu tables were unreachable in the GUI. The WinForms ground truth
// (StatusRMenuForm) exposes all six tables via a version-gated FilterComboBox and
// a directional-pointer (ListFounder) traversal.
//
// These headless tests drive StatusRMenuViewModel against synthetic ROMs that
// plant directional graphs at the REAL status_rmenu*_pointer roots (so the test
// exercises the version-specific RomInfo) and prove:
//   * GetTableCount() == 6 on FE8, 5 on FE7/FE6 (version gate).
//   * SelectedTableIndex switches which table LoadStatusRMenuList() surfaces.
//   * The directional traversal reaches a non-contiguous child (the old linear
//     scan could not).
//   * The View wires the FilterComboBox population + reload.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM.

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class StatusRMenuFilterTests : IDisposable
{
    readonly ROM? _savedRom;

    public StatusRMenuFilterTests() => _savedRom = CoreState.ROM;
    public void Dispose() => CoreState.ROM = _savedRom;

    // ================================================================
    // Version-gated table count
    // ================================================================

    [Fact]
    public void GetTableCount_Is6_OnFE8()
    {
        CoreState.ROM = MakeBareRom("BE8E01"); // FE8U
        Assert.Equal(8, CoreState.ROM.RomInfo.version);
        var vm = new StatusRMenuViewModel();
        Assert.Equal(6, vm.GetTableCount());
    }

    [Fact]
    public void GetTableCount_Is5_OnFE7()
    {
        CoreState.ROM = MakeBareRom("AE7E01"); // FE7U
        Assert.Equal(7, CoreState.ROM.RomInfo.version);
        // FE7 has no FE8 status-screen table (rmenu6 == 0).
        Assert.Equal(0u, CoreState.ROM.RomInfo.status_rmenu6_pointer);
        var vm = new StatusRMenuViewModel();
        Assert.Equal(5, vm.GetTableCount());
    }

    [Fact]
    public void GetTableCount_Is5_OnFE6()
    {
        CoreState.ROM = MakeBareRom("AFEJ01"); // FE6
        Assert.Equal(6, CoreState.ROM.RomInfo.version);
        Assert.Equal(0u, CoreState.ROM.RomInfo.status_rmenu6_pointer);
        var vm = new StatusRMenuViewModel();
        Assert.Equal(5, vm.GetTableCount());
    }

    // ================================================================
    // SelectedTableIndex switches the surfaced table (the core bug)
    // ================================================================

    [Fact]
    public void SelectedTableIndex_SwitchesTable_FE8()
    {
        // Plant independent single-node graphs at the unit (table 0) and game
        // (table 1) roots, then prove the VM surfaces each by selection.
        ROM rom = MakeBareRom("BE8E01");
        uint unitRoot = rom.RomInfo.status_rmenu_unit_pointer;
        uint gameRoot = rom.RomInfo.status_rmenu_game_pointer;
        Assert.NotEqual(0u, unitRoot);
        Assert.NotEqual(0u, gameRoot);

        const uint NodeA = 0x00500000, NodeB = 0x00600000;
        rom.write_u32(unitRoot, U.toPointer(NodeA));
        rom.write_u32(gameRoot, U.toPointer(NodeB));
        WriteTerminalNode(rom, NodeA, 0x1111);
        WriteTerminalNode(rom, NodeB, 0x2222);
        CoreState.ROM = rom;

        var vm = new StatusRMenuViewModel { SelectedTableIndex = 0 };
        var t0 = vm.LoadStatusRMenuList();
        Assert.Single(t0);
        Assert.Equal(NodeA, t0[0].addr);

        vm.SelectedTableIndex = 1;
        var t1 = vm.LoadStatusRMenuList();
        Assert.Single(t1);
        Assert.Equal(NodeB, t1[0].addr);

        Assert.NotEqual(t0[0].addr, t1[0].addr);
    }

    [Fact]
    public void DirectionalTraversal_ReachesNonContiguousChild_FE8()
    {
        // NodeA's UP pointer → NodeB far away. The old linear +i*28 scan would
        // stop at NodeA+28 (zeroed) and never reach NodeB.
        ROM rom = MakeBareRom("BE8E01");
        uint unitRoot = rom.RomInfo.status_rmenu_unit_pointer;
        const uint NodeA = 0x00500000, NodeB = 0x00700000;
        rom.write_u32(unitRoot, U.toPointer(NodeA));
        WriteNode(rom, NodeA, up: NodeB, textId: 0x1111);
        WriteTerminalNode(rom, NodeB, 0x2222);
        CoreState.ROM = rom;

        var vm = new StatusRMenuViewModel { SelectedTableIndex = 0 };
        var list = vm.LoadStatusRMenuList();

        Assert.Equal(2, list.Count);
        Assert.Equal(NodeA, list[0].addr);
        Assert.Equal(NodeB, list[1].addr); // reachable ONLY via the directional pointer
    }

    [Fact]
    public void EveryNode_LandsOnStride28Geometry_FE8()
    {
        // Two-node chain at consecutive 28-byte slots — every AddrResult.addr
        // must be a valid 28-byte record start (list stride == loader record size).
        ROM rom = MakeBareRom("BE8E01");
        uint unitRoot = rom.RomInfo.status_rmenu_unit_pointer;
        const uint BASE = 0x00500000;
        uint NodeA = BASE, NodeB = BASE + 28;
        rom.write_u32(unitRoot, U.toPointer(NodeA));
        WriteNode(rom, NodeA, up: NodeB, textId: 0x1111);
        WriteTerminalNode(rom, NodeB, 0x2222);
        CoreState.ROM = rom;

        var vm = new StatusRMenuViewModel { SelectedTableIndex = 0 };
        var list = vm.LoadStatusRMenuList();
        Assert.Equal(2, list.Count);
        foreach (var r in list)
            Assert.Equal(0u, (r.addr - BASE) % 28u);
    }

    // ================================================================
    // View source-wiring guard
    // ================================================================

    [Fact]
    public void View_WiresFilterComboAndReload()
    {
        string repoRoot = FindRepoRoot();
        string axaml = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "StatusRMenuView.axaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "StatusRMenuView.axaml.cs"));

        // The combo exists in the view ...
        Assert.Contains("StatusRMenu_Filter_Combo", axaml);
        Assert.Contains("Name=\"FilterComboBox\"", axaml);
        // ... and the code-behind populates + reloads on change.
        Assert.Contains("FilterComboBox.SelectionChanged", code);
        Assert.Contains("PopulateFilterCombo", code);
        Assert.Contains("_vm.SelectedTableIndex", code);
    }

    // ================================================================
    // Helpers
    // ================================================================

    static ROM MakeBareRom(string header)
    {
        // ~17 MB so the FE8U status-screen table (rmenu6 = 0xA01CE0) and all the
        // planted node addresses are in-bounds.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow($"synth-{header}-1459.gba", bytes, header);
        return rom;
    }

    static void WriteNode(ROM rom, uint addr, uint up, ushort textId)
    {
        rom.write_u32(addr + 0, U.toPointer(up));
        rom.write_u32(addr + 4, 0);
        rom.write_u32(addr + 8, 0);
        rom.write_u32(addr + 12, 0);
        rom.write_u8(addr + 16, 0x10);
        rom.write_u8(addr + 17, 0x20);
        rom.write_u16(addr + 18, textId);
    }

    static void WriteTerminalNode(ROM rom, uint addr, ushort textId)
    {
        rom.write_u32(addr + 0, 0);
        rom.write_u32(addr + 4, 0);
        rom.write_u32(addr + 8, 0);
        rom.write_u32(addr + 12, 0);
        rom.write_u8(addr + 16, 0x10);
        rom.write_u8(addr + 17, 0x20);
        rom.write_u16(addr + 18, textId);
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return AppContext.BaseDirectory;
    }
}
