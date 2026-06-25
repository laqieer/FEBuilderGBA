// SPDX-License-Identifier: GPL-3.0-or-later
// #1431 — AOE Range editor was a non-functional Avalonia stub (addr-0 dummy
// entry, no grid, Write no-op). These headless tests drive the real ported
// AOERANGEViewModel against synthetic ROMs and prove:
//   * LoadEntry populates the 4 header fields AND the w*h grid cells.
//   * The center cell is highlighted (IsCenter) at cx + cy*w.
//   * ResizeGridPreserving keeps overlapping (x,y) values across a width change.
//   * The exact View OnWrite sequence (UndoService Begin → set → Write → Commit)
//     persists header + grid in place and round-trips through the ROM.
//   * A grow Write moves the record and repoints the parent slot; Rollback
//     restores byte identity.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM / .Undo.

using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class AOERANGEViewModelTests : IDisposable
{
    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public AOERANGEViewModelTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    static ROM MakeRom(int size = 0x200000)
    {
        var rom = new ROM();
        rom.LoadLow("aoe-1431.gba", new byte[size], "NAZO");
        return rom;
    }

    static void WriteRecord(ROM rom, uint addr, byte w, byte h, byte cx, byte cy, byte[] cells)
    {
        rom.Data[addr + 0] = w;
        rom.Data[addr + 1] = h;
        rom.Data[addr + 2] = cx;
        rom.Data[addr + 3] = cy;
        for (int i = 0; i < cells.Length; i++) rom.Data[addr + 4 + i] = cells[i];
    }

    static void WritePointer(ROM rom, uint slot, uint offset)
    {
        uint gba = offset + 0x08000000;
        rom.Data[slot + 0] = (byte)(gba & 0xFF);
        rom.Data[slot + 1] = (byte)((gba >> 8) & 0xFF);
        rom.Data[slot + 2] = (byte)((gba >> 16) & 0xFF);
        rom.Data[slot + 3] = (byte)((gba >> 24) & 0xFF);
    }

    // ----------------------------------------------------------------------
    // Load
    // ----------------------------------------------------------------------

    [Fact]
    public void LoadEntry_PopulatesHeaderAndGrid()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        uint addr = 0x1000;
        byte[] cells = { 0, 1, 1, 0, 1, 1 }; // 3x2
        WriteRecord(rom, addr, 3, 2, 1, 1, cells);

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(3u, vm.Width);
        Assert.Equal(2u, vm.Height);
        Assert.Equal(1u, vm.CenterX);
        Assert.Equal(1u, vm.CenterY);
        Assert.Equal(6, vm.Cells.Count);
        Assert.Equal(0u, vm.Cells[0].Value);
        Assert.Equal(1u, vm.Cells[1].Value);
        Assert.Equal(1u, vm.Cells[5].Value);
    }

    [Fact]
    public void LoadEntry_HighlightsCenterCell()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        WriteRecord(rom, 0x1000, 3, 2, 2, 1, new byte[] { 0, 0, 0, 0, 0, 1 });

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(0x1000);

        // center = cx + cy*w = 2 + 1*3 = 5.
        for (int i = 0; i < vm.Cells.Count; i++)
            Assert.Equal(i == 5, vm.Cells[i].IsCenter);
    }

    [Fact]
    public void LoadEntry_Unsafe_ClearsAndReportsStatus()
    {
        var rom = MakeRom(0x100);
        CoreState.ROM = rom;
        uint addr = 0xF8;
        rom.Data[addr] = 0xFF;
        rom.Data[addr + 1] = 0xFF; // huge w*h overruns the ROM.

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(addr);

        Assert.False(vm.IsLoaded);
        Assert.Empty(vm.Cells);
        Assert.Contains("No valid", vm.Status);
    }

    // ----------------------------------------------------------------------
    // Grid resize
    // ----------------------------------------------------------------------

    [Fact]
    public void ResizeGridPreserving_KeepsOverlapAcrossWidthChange()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        // 2x2 grid with distinct values.
        WriteRecord(rom, 0x1000, 2, 2, 0, 0, new byte[] { 1, 2, 3, 4 });

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(0x1000);

        // Grow to 3x3; values at (0,0),(1,0),(0,1),(1,1) must survive.
        vm.Width = 3;
        vm.Height = 3;
        vm.ResizeGridPreserving(2, 2);

        Assert.Equal(9, vm.Cells.Count);
        // (0,0)=1, (1,0)=2 stay in row 0.
        Assert.Equal(1u, vm.Cells[0].Value);
        Assert.Equal(2u, vm.Cells[1].Value);
        Assert.Equal(0u, vm.Cells[2].Value); // new column
        // (0,1)=3, (1,1)=4 move to row 1 (indices 3,4 under width 3).
        Assert.Equal(3u, vm.Cells[3].Value);
        Assert.Equal(4u, vm.Cells[4].Value);
        Assert.Equal(0u, vm.Cells[5].Value);
    }

    // ----------------------------------------------------------------------
    // Write (the exact View OnWrite sequence)
    // ----------------------------------------------------------------------

    [Fact]
    public void Write_InPlace_PersistsHeaderAndGrid_RoundTrips()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint addr = 0x1000;
        WriteRecord(rom, addr, 2, 2, 0, 0, new byte[] { 0, 0, 0, 0 });

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(addr);
        vm.MarkClean();

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();

        // EXACT View OnWrite sequence.
        undoService.Begin("Edit AOE Range");
        vm.CenterX = 1;
        vm.CenterY = 1;
        vm.Cells[0].Value = 9;
        vm.Cells[3].Value = 7;
        bool changed = vm.Write();
        undoService.Commit();
        vm.MarkClean();

        Assert.True(changed);
        Assert.Contains("in place", vm.Status); // wrote in place (no move).
        Assert.Equal(addr, vm.CurrentAddr);      // address unchanged on in-place.
        // Round-trip: re-read the record.
        var back = AoeRangeCore.ReadAoeRange(rom, addr);
        Assert.Equal(1u, back.CenterX);
        Assert.Equal(1u, back.CenterY);
        Assert.Equal(9, back.Cells[0]);
        Assert.Equal(7, back.Cells[3]);

        Assert.True(CoreState.Undo.IsModified);
        CoreState.Undo.RunUndo();
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Write_Grow_MovesAndRepointsParentSlot()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint slot = 0x400;   // >= 0x200, outside the header danger zone.
        uint addr = 0x1000;
        WriteRecord(rom, addr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });
        WritePointer(rom, slot, addr);
        for (int i = 0; i < 256; i++) rom.Data[0x100100 + i] = 0xFF;

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(addr);
        vm.ParentPointerSlot = slot;
        vm.MarkClean();

        var undoService = new UndoService();
        undoService.Begin("Edit AOE Range");
        vm.Width = 4;
        vm.Height = 4;
        vm.ResizeGridPreserving(2, 2);
        bool changed = vm.Write();
        undoService.Commit();
        vm.MarkClean();

        Assert.True(changed);
        Assert.NotEqual(addr, vm.CurrentAddr);
        Assert.Equal(vm.CurrentAddr, rom.p32(slot)); // parent slot repointed.
        var back = AoeRangeCore.ReadAoeRange(rom, vm.CurrentAddr);
        Assert.Equal(4u, back.Width);
        Assert.Equal(4u, back.Height);
    }

    [Fact]
    public void Write_Grow_Rollback_RestoresBytes()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        uint slot = 0x400;
        uint addr = 0x1000;
        WriteRecord(rom, addr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });
        WritePointer(rom, slot, addr);
        for (int i = 0; i < 256; i++) rom.Data[0x100100 + i] = 0xFF;

        var vm = new AOERANGEViewModel();
        vm.LoadEntry(addr);
        vm.ParentPointerSlot = slot;

        byte[] snap = (byte[])rom.Data.Clone();
        var undoService = new UndoService();
        undoService.Begin("Edit AOE Range");
        vm.Width = 4;
        vm.Height = 4;
        vm.ResizeGridPreserving(2, 2);
        vm.Write();
        undoService.Rollback();

        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void Write_NoRecordLoaded_RefusesNoMutation()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        byte[] snap = (byte[])rom.Data.Clone();

        var vm = new AOERANGEViewModel(); // nothing loaded.
        var undoService = new UndoService();
        undoService.Begin("Edit AOE Range");
        bool changed = vm.Write();
        if (changed) undoService.Commit(); else undoService.Rollback();

        Assert.False(changed);
        Assert.Equal(snap, rom.Data);
    }

    [Fact]
    public void ListParity_AOERANGE_IsNoListEditor_NotContextDependent()
    {
        Assert.True(ListParityHelper.IsNoListEditor("AOERANGEView"));
        Assert.False(ListParityHelper.IsContextDependentEditor("AOERANGEView"));
    }
}
