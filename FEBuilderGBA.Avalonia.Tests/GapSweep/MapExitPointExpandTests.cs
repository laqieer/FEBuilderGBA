// SPDX-License-Identifier: GPL-3.0-or-later
// #773 — MapExitPoint "Data Expansion" (Expand List) wiring tests.
//
// Proves the Avalonia Map Exit Point editor's previously-inert ExpandList
// button now grows the per-map exit-point block by one blank row via
// DataExpansionCore.ExpandTableTo:
//   - ExpandExitList relocates the block, repoints the per-map slot, appends a
//     blank (zero-filled) row, and preserves the B0/0xFFFFFFFF terminator;
//   - the operation is undoable (ROM bytes + slot pointer restored on RunUndo);
//   - a blank/no-list map is refused (Success==false, ROM unchanged);
//   - a corrupt/unterminated block is refused (Success==false, ROM unchanged);
//   - a headless [AvaloniaFact] click of the Expand button grows the displayed
//     sub-list by exactly one row.
//
// NOTE on ROM source: the repo does NOT commit real .gba ROMs (the roms/
// folder is empty locally and in CI — TestRomLocator.FindRom returns null).
// A real-ROM-only test would therefore be SKIPPED everywhere, giving zero
// coverage. So — exactly like the existing MapExitPointParityTests and
// MapExitPointCoreTests — these tests build a deterministic synthetic FE8U ROM
// (header signature BE8E01) and plant a per-map exit-point block plus an
// explicit 0xFF free-space region for the relocate. This exercises the full
// ExpandTableTo path (find-free-space → copy → zero-fill → terminator →
// repoint) without depending on an external ROM.
//
// Marked [Collection("SharedState")] because the tests mutate CoreState.ROM /
// CoreState.Undo to plant the synthetic ROM (matches the sibling MapExitPoint
// suites). CoreState.ROM is re-asserted at the top of each ROM-dependent body
// to avoid shared-state flakes when the suite interleaves with other classes.
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class MapExitPointExpandTests
{
    // The per-map block of map 0 (the first enemy slot) lives here.
    const uint PerMapBase = 0x00810000u;
    // The first enemy pointer-slot in the per-map pointer table.
    const uint EnemySlot0 = 0x00800000u;

    /// <summary>
    /// Build a synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item><c>map_exit_point_pointer</c> → 0x00800000 (the per-map slot table).</item>
    ///   <item>Enemy slot 0 (0x00800000) → 0x00810000 (per-map block 0).</item>
    ///   <item>Block 0 = <paramref name="rows"/> rows (X=0x10+r, Y=0x20+r,
    ///         Escape=0x03, Flag=0) then a B0=0xFF terminator.</item>
    ///   <item>A 0x40000-byte 0xFF free-space region at 0x00C00000 so
    ///         <c>DataExpansionCore.FindFreeSpace</c> relocates there without a
    ///         ROM resize (the buffer is otherwise zero-filled).</item>
    /// </list>
    /// </summary>
    static ROM MakeFe8uRom(int rows = 2)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint pointerSlot = rom.RomInfo.map_exit_point_pointer;
        uint tableBase = 0x00800000u;
        BitConverter.GetBytes(tableBase | 0x08000000u).CopyTo(bytes, pointerSlot);

        // Enemy slot 0 → per-map block 0.
        BitConverter.GetBytes(PerMapBase | 0x08000000u).CopyTo(bytes, tableBase);

        // Plant `rows` rows then a B0=0xFF terminator.
        for (int r = 0; r < rows; r++)
        {
            int rowBase = (int)(PerMapBase + r * 4);
            bytes[rowBase + 0] = (byte)(0x10 + r);
            bytes[rowBase + 1] = (byte)(0x20 + r);
            bytes[rowBase + 2] = 0x03;
            bytes[rowBase + 3] = 0x00;
        }
        bytes[(int)(PerMapBase + rows * 4)] = 0xFF; // terminator (explicit cast, matches rowBase above)

        // Explicit 0xFF free-space region so FindFreeSpace lands here.
        for (uint i = 0; i < 0x40000u; i++)
        {
            bytes[0x00C00000u + i] = 0xFF;
        }

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // VM — successful expand round-trip
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandExitList_GrowsBlockByOneRow_RepointsSlot_PreservesTerminator()
    {
        ROM rom = MakeFe8uRom(rows: 2);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            vm.LoadExitListForMap(EnemySlot0);
            Assert.False(vm.IsBlank);
            Assert.Equal(PerMapBase, vm.CurrentExitPointAddr);

            // currentCount = rows before the terminator (2).
            Assert.True(MapExitPointCore.TryCountExitRows(rom, PerMapBase, out uint currentCount));
            Assert.Equal(2u, currentCount);

            DataExpansionCore.ExpandResult r;
            var undodata = CoreState.Undo.NewUndoData("MapExit ExpandList test");
            using (ROM.BeginUndoScope(undodata))
            {
                r = vm.ExpandExitList(undodata);
            }
            CoreState.Undo.Push(undodata);

            Assert.True(r.Success, r.Error);
            Assert.Equal(currentCount + 1, r.NewCount); // 3
            uint newBase = r.NewBaseAddress;

            // The per-map slot now points at the relocated block.
            Assert.Equal(newBase, rom.p32(EnemySlot0));
            Assert.NotEqual(PerMapBase, newBase);

            // The original 2 rows copied verbatim.
            Assert.Equal(0x10, rom.Data[newBase + 0]);
            Assert.Equal(0x20, rom.Data[newBase + 1]);
            Assert.Equal(0x11, rom.Data[newBase + 4]);
            Assert.Equal(0x21, rom.Data[newBase + 5]);

            // The appended (3rd) row is zero-filled.
            Assert.Equal(0x00, rom.Data[newBase + 8]);
            Assert.Equal(0x00, rom.Data[newBase + 9]);
            Assert.Equal(0x00, rom.Data[newBase + 10]);
            Assert.Equal(0x00, rom.Data[newBase + 11]);

            // The 0xFFFFFFFF terminator sits at newBase + NewCount*4. Its first
            // byte (B0) is 0xFF, so the walk still stops there.
            uint termAddr = newBase + r.NewCount * 4u;
            Assert.Equal(0xFFu, rom.u8(termAddr));
            Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandExitList_IsUndoable_RestoresSlotAndBytes()
    {
        ROM rom = MakeFe8uRom(rows: 2);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            vm.LoadExitListForMap(EnemySlot0);

            uint origSlotValue = rom.p32(EnemySlot0);
            byte[] origBlock = rom.getBinaryData(PerMapBase, 0x20);

            var undodata = CoreState.Undo.NewUndoData("MapExit ExpandList undo test");
            DataExpansionCore.ExpandResult r;
            using (ROM.BeginUndoScope(undodata))
            {
                r = vm.ExpandExitList(undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.True(r.Success, r.Error);
            Assert.NotEqual(origSlotValue, rom.p32(EnemySlot0)); // repointed away

            // Undo: slot pointer + the original block bytes return to baseline.
            CoreState.Undo.RunUndo();
            Assert.Equal(origSlotValue, rom.p32(EnemySlot0));
            Assert.Equal(origBlock, rom.getBinaryData(PerMapBase, 0x20));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // VM — refusal paths leave the ROM unchanged
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandExitList_BlankMap_ReturnsFalse_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(rows: 2);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // Point enemy slot 0 at the blank marker → "no exits" state.
            uint blank = rom.RomInfo.map_exit_point_blank;
            BitConverter.GetBytes(blank | 0x08000000u).CopyTo(rom.Data, EnemySlot0);

            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            vm.LoadExitListForMap(EnemySlot0);
            Assert.True(vm.IsBlank);

            byte[] before = rom.getBinaryData(0x00800000u, 0x20000);
            var undodata = CoreState.Undo.NewUndoData("blank");
            DataExpansionCore.ExpandResult r;
            using (ROM.BeginUndoScope(undodata))
            {
                r = vm.ExpandExitList(undodata);
            }

            Assert.False(r.Success);
            Assert.False(string.IsNullOrEmpty(r.Error));
            Assert.Equal(before, rom.getBinaryData(0x00800000u, 0x20000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandExitList_CorruptUnterminatedBlock_ReturnsFalse_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(rows: 2);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // Overwrite the block (incl. its terminator) with non-0xFF bytes so
            // TryCountExitRows finds no terminator within the 0x100 row cap.
            for (uint i = 0; i < 0x100u * 4u; i++)
            {
                rom.Data[PerMapBase + i] = 0x01;
            }

            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            vm.LoadExitListForMap(EnemySlot0);
            // Not blank (slot still points at PerMapBase) — but unterminated.
            Assert.False(vm.IsBlank);
            Assert.Equal(PerMapBase, vm.CurrentExitPointAddr);

            byte[] before = rom.getBinaryData(0x00800000u, 0x20000);
            var undodata = CoreState.Undo.NewUndoData("corrupt");
            DataExpansionCore.ExpandResult r;
            using (ROM.BeginUndoScope(undodata))
            {
                r = vm.ExpandExitList(undodata);
            }

            Assert.False(r.Success);
            Assert.Contains("terminator", r.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, rom.getBinaryData(0x00800000u, 0x20000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Headless View — clicking Expand grows the displayed sub-list by one
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void View_ClickExpand_GrowsSubListByOneRow()
    {
        ROM rom = MakeFe8uRom(rows: 2);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var view = new MapExitPointView();
            var exitList = view.FindControl<AddressListControl>("ExitList");
            Assert.NotNull(exitList);

            // Drive the VM through the same path a map-list selection would.
            // (Re-assert CoreState.ROM defensively — constructing the View runs
            // its Opened handler which reloads the map list.)
            CoreState.ROM = rom;
            Invoke(view, "OnMapSelected", EnemySlot0);

            int rowsBefore = exitList!.ItemCount;
            Assert.Equal(2, rowsBefore); // 2 planted rows

            // Click "Data Expansion".
            Invoke(view, "ExpandList_Click");

            int rowsAfter = exitList.ItemCount;
            Assert.Equal(rowsBefore + 1, rowsAfter); // 3 rows now
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Reflection helpers (the View click handlers are private).
    // -----------------------------------------------------------------

    static void Invoke(MapExitPointView view, string method)
    {
        var m = typeof(MapExitPointView).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(view, new object?[] { null, new RoutedEventArgs() });
    }

    static void Invoke(MapExitPointView view, string method, uint addr)
    {
        var m = typeof(MapExitPointView).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(view, new object?[] { addr });
    }
}
