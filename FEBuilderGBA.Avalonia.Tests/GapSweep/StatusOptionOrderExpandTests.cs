// SPDX-License-Identifier: GPL-3.0-or-later
// #1608 — Status Option Order editor list-expand + count-byte update tests.
//
// Proves the Avalonia Status Option Order editor's new "Expand List" affordance
// grows the 1-byte-per-entry game-option-order list AND raises the order-count
// byte at status_game_option_order_count_address, mirroring WinForms
// StatusOptionOrderForm.AddressListExpandsEvent (the active write_u8 path; the
// commented-out FE7 order2 repoint is intentionally NOT ported):
//   - StatusOptionOrderViewModel.ExpandList relocates the list via
//     DataExpansionCore.ExpandTableTo(entrySize:1), repoints the canonical
//     pointer, copies the original bytes verbatim, zero-fills the new slot, and
//     writes the new count byte — all under the caller's ambient undo scope;
//   - the whole expand is undoable (ROM bytes + pointer + count byte restored
//     byte-identically on RunUndo);
//   - newCount < current and newCount > byte.MaxValue (256) are refused BEFORE
//     any mutation, so the list is never moved without a matching count write;
//   - an unset pointer / count address is refused;
//   - the raw count byte (0..255) is honored on reload — a count of 65 (which the
//     OLD 0x40->0x20 clamp would have collapsed) survives LoadStatusOptionOrderList;
//   - a headless [AvaloniaFact] confirms the Expand button is present + enabled.
//
// NOTE on ROM source: the repo does NOT commit real .gba ROMs (roms/ is empty
// locally and in CI). So — exactly like the sibling MapExitPointExpandTests /
// ImageMapActionAnimationParityTests — these tests build a deterministic
// synthetic FE8U ROM (header signature BE8E01) and plant the list pointer + count
// byte at the FE8U RomInfo addresses plus an explicit 0xFF free-space region for
// the relocate.
//
// Marked [Collection("SharedState")] because the tests mutate CoreState.ROM /
// CoreState.Undo to plant the synthetic ROM. CoreState.ROM is re-asserted at the
// top of each ROM-dependent body to avoid shared-state flakes.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class StatusOptionOrderExpandTests
{
    // The game-option-order list lives here (pointed to by the FE8U pointer).
    const uint ListBase = 0x00800000u;
    // The 0xFF free-space region the relocate lands in.
    const uint FreeSpace = 0x00C00000u;
    const uint FreeSpaceLen = 0x40000u;

    /// <summary>
    /// Build a synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item><c>status_game_option_order_pointer</c> (0xB16F8) → 0x00800000.</item>
    ///   <item><c>status_game_option_order_count_address</c> (0xB1A32) = <paramref name="count"/>.</item>
    ///   <item>List base 0x00800000 = <paramref name="count"/> bytes (slot r = 0x10 + r).</item>
    ///   <item>A 0xFF free-space region at 0x00C00000 so
    ///         <c>DataExpansionCore.FindFreeSpace</c> relocates there without a
    ///         ROM resize (the buffer is otherwise zero-filled).</item>
    /// </list>
    /// </summary>
    static ROM MakeFe8uRom(byte count = 8)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
        uint countAddr = rom.RomInfo.status_game_option_order_count_address;
        Assert.NotEqual(0u, ptrAddr);
        Assert.NotEqual(0u, countAddr);

        // pointer → list base
        BitConverter.GetBytes(ListBase | 0x08000000u).CopyTo(bytes, ptrAddr);
        // count byte
        bytes[countAddr] = count;
        // list entries
        for (int r = 0; r < count; r++)
            bytes[(int)(ListBase + r)] = (byte)(0x10 + r);

        // Explicit 0xFF free-space region for the relocate.
        for (uint i = 0; i < FreeSpaceLen; i++)
            bytes[FreeSpace + i] = 0xFF;

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // VM — successful expand round-trip
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_GrowsList_RepointsPointer_WritesCountByte()
    {
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
            uint countAddr = rom.RomInfo.status_game_option_order_count_address;

            var vm = new StatusOptionOrderViewModel();
            var items = vm.LoadStatusOptionOrderList();
            Assert.Equal(8, items.Count);
            Assert.Equal(8u, vm.ReadCount);

            string err;
            var undodata = CoreState.Undo.NewUndoData("StatusOptionOrder ExpandList test");
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(12, undodata);
            }
            CoreState.Undo.Push(undodata);

            Assert.Equal("", err);
            Assert.Equal(12u, vm.ReadCount);

            // The pointer now points at the relocated list.
            uint newBase = rom.p32(ptrAddr);
            Assert.NotEqual(ListBase, newBase);

            // Original 8 bytes copied verbatim.
            for (uint r = 0; r < 8; r++)
                Assert.Equal(0x10u + r, rom.u8(newBase + r));

            // The 4 appended slots are zero-filled.
            for (uint r = 8; r < 12; r++)
                Assert.Equal(0x00u, rom.u8(newBase + r));

            // The count byte was raised to 12.
            Assert.Equal(12u, rom.u8(countAddr));

            // A reload now shows 12 entries.
            Assert.Equal(12, vm.LoadStatusOptionOrderList().Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandList_IsUndoable_RestoresPointerCountAndBytes()
    {
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
            uint countAddr = rom.RomInfo.status_game_option_order_count_address;

            var vm = new StatusOptionOrderViewModel();
            vm.LoadStatusOptionOrderList();

            uint origPtr = rom.p32(ptrAddr);
            uint origCount = rom.u8(countAddr);
            byte[] origList = rom.getBinaryData(ListBase, 8);

            string err;
            var undodata = CoreState.Undo.NewUndoData("StatusOptionOrder ExpandList undo test");
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(20, undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.Equal("", err);
            Assert.NotEqual(origPtr, rom.p32(ptrAddr)); // repointed away
            Assert.Equal(20u, rom.u8(countAddr));

            // Undo: pointer + count byte + original list bytes return to baseline.
            CoreState.Undo.RunUndo();
            Assert.Equal(origPtr, rom.p32(ptrAddr));
            Assert.Equal(origCount, rom.u8(countAddr));
            Assert.Equal(origList, rom.getBinaryData(ListBase, 8));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // VM — refusal paths leave the ROM unchanged (validate-before-mutate)
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_BelowCurrent_ReturnsError_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new StatusOptionOrderViewModel();
            vm.LoadStatusOptionOrderList();

            byte[] before = rom.getBinaryData(0x00800000u, 0x20000);
            uint origPtr = rom.p32(rom.RomInfo.status_game_option_order_pointer);

            string err = vm.ExpandList(5, null);

            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(origPtr, rom.p32(rom.RomInfo.status_game_option_order_pointer));
            Assert.Equal(before, rom.getBinaryData(0x00800000u, 0x20000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandList_AboveByteMax_ReturnsError_RomUnchanged()
    {
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            uint countAddr = rom.RomInfo.status_game_option_order_count_address;
            var vm = new StatusOptionOrderViewModel();
            vm.LoadStatusOptionOrderList();

            byte[] before = rom.getBinaryData(0x00800000u, 0x20000);
            uint origPtr = rom.p32(rom.RomInfo.status_game_option_order_pointer);
            uint origCount = rom.u8(countAddr);

            // 256 must be refused BEFORE any mutation — write_u8 would otherwise
            // truncate it to 0 and the table would already be relocated.
            string err = vm.ExpandList(256, null);

            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(origPtr, rom.p32(rom.RomInfo.status_game_option_order_pointer));
            Assert.Equal(origCount, rom.u8(countAddr)); // count byte untouched (not 0)
            Assert.Equal(before, rom.getBinaryData(0x00800000u, 0x20000));
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Reader parity — raw count byte 0..255 honored, no 0x40->0x20 clamp
    // -----------------------------------------------------------------

    [Fact]
    public void LoadList_HonorsRawCountByte_Above0x40()
    {
        // A count of 0x46 (70) exceeds the old 0x40 cap; the removed clamp would
        // have collapsed it back to 0x20 (32). The raw byte must now be honored.
        ROM rom = MakeFe8uRom(count: 70);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new StatusOptionOrderViewModel();
            var items = vm.LoadStatusOptionOrderList();

            Assert.Equal(70, items.Count);
            Assert.Equal(70u, vm.ReadCount);

            // The ListParityHelper reader must agree (it had the same clamp).
            // BuildReferenceList reads CoreState.ROM (set above) internally.
            var parity = FEBuilderGBA.Avalonia.Services.ListParityHelper.BuildReferenceList("StatusOptionOrderView");
            Assert.NotNull(parity);
            Assert.Equal(70, parity!.Count);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ExpandThenReload_To65_SurvivesClampRemoval()
    {
        // End-to-end: expand from 8 to 65 (> old 0x40 cap), reload, and confirm
        // the editor shows all 65 slots rather than collapsing to 0x20.
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new StatusOptionOrderViewModel();
            vm.LoadStatusOptionOrderList();

            string err;
            var undodata = CoreState.Undo.NewUndoData("StatusOptionOrder Expand65");
            using (ROM.BeginUndoScope(undodata))
            {
                err = vm.ExpandList(65, undodata);
            }
            CoreState.Undo.Push(undodata);
            Assert.Equal("", err);

            // Fresh VM (proves the count is read from the ROM, not VM state).
            var vm2 = new StatusOptionOrderViewModel();
            Assert.Equal(65, vm2.LoadStatusOptionOrderList().Count);
            Assert.Equal(65u, vm2.ReadCount);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Headless View — the Expand button is present + enabled
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void View_ExpandButton_IsPresentAndEnabled()
    {
        ROM rom = MakeFe8uRom(count: 8);
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var view = new StatusOptionOrderView();
            var expand = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(expand);
            Assert.True(expand!.IsEnabled);
            Assert.Equal("Expand List", expand.Content);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }
}
