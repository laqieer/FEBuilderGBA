// SPDX-License-Identifier: GPL-3.0-or-later
// #1442 — Talk Group (FE7) editor was a single-auto-discovered-entry stub. WinForms
// EventTalkGroupFE7Form is a 14-entry (i=0..0xD) stride-4 list editor with repoint
// (JumpToAddr/ReInit) and NewAlloc (byte[4*0xE]). These headless tests prove the
// ported ViewModel emits the full 14-row list at base+i*4, repoints onto an
// arbitrary block base (raw offset OR 0x08...... pointer), and NewAlloc allocates
// the correct 56-byte block.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM / .Undo.

using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EventTalkGroupFE7ListTests : IDisposable
{
    const uint BlockA = 0x00900000u; // first synthetic talk-group block base
    const uint BlockB = 0x00910000u; // second block base (repoint target)

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public EventTalkGroupFE7ListTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    [Fact]
    public void LoadList_Emits14StrideFourEntries_FE7()
    {
        ROM rom = MakeRom();
        CoreState.ROM = rom;

        var vm = new EventTalkGroupFE7ViewModel();
        vm.SetBaseAddr(BlockA);
        var list = vm.LoadList();

        // 14 entries (i=0..0xD), stride 4.
        Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(BlockA + (uint)(i * EventTalkGroupFE7ViewModel.EntryStride), list[i].addr);
            Assert.Equal((uint)i, list[i].tag);
        }
        // GetListCount mirrors the list length.
        Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, vm.GetListCount());
    }

    [Fact]
    public void LoadList_LabelReadsTextIdAsU16_FE7()
    {
        ROM rom = MakeRom();
        // Entry 3 has a distinctive low u16; the upper u16 must be ignored by the label.
        PlantU16(rom, BlockA + 3 * 4 + 0, 0xABCD);
        PlantU16(rom, BlockA + 3 * 4 + 2, 0x1234);
        CoreState.ROM = rom;

        var vm = new EventTalkGroupFE7ViewModel();
        vm.SetBaseAddr(BlockA);
        var list = vm.LoadList();

        // Label encodes the u16 text id (0xABCD), not the full u32.
        Assert.Contains("0xABCD", list[3].name);
        Assert.DoesNotContain("0x1234ABCD", list[3].name);
    }

    [Fact]
    public void SetBaseAddr_AcceptsRawOffsetOrPointer_FE7()
    {
        ROM rom = MakeRom();
        CoreState.ROM = rom;

        var vm = new EventTalkGroupFE7ViewModel();

        // Raw offset normalizes to itself.
        vm.SetBaseAddr(BlockB);
        Assert.Equal(BlockB, vm.BaseAddr);

        // 0x08...... GBA pointer normalizes to the same offset (parity with ReInit).
        vm.SetBaseAddr(U.toPointer(BlockB));
        Assert.Equal(BlockB, vm.BaseAddr);

        // Zero clears (re-auto-discover).
        vm.SetBaseAddr(0);
        Assert.Equal(0u, vm.BaseAddr);
    }

    [Fact]
    public void Repoint_SwitchesListToDifferentBlock_FE7()
    {
        ROM rom = MakeRom();
        CoreState.ROM = rom;

        var vm = new EventTalkGroupFE7ViewModel();

        vm.SetBaseAddr(BlockA);
        var listA = vm.LoadList();
        Assert.Equal(BlockA, listA[0].addr);

        vm.SetBaseAddr(BlockB);
        var listB = vm.LoadList();
        Assert.Equal(BlockB, listB[0].addr);
        Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, listB.Count);
    }

    [Fact]
    public void NewAlloc_AppendsFiftySixByteBlockAndRepoints_FE7()
    {
        var savedAppend = CoreState.AppendBinaryData;
        try
        {
            CoreState.AppendBinaryData = null; // headless free-space fallback

            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new EventTalkGroupFE7ViewModel();
            int before = rom.Data.Length;

            uint addr;
            using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("t")))
            {
                addr = vm.NewAlloc();
            }

            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal(addr, vm.BaseAddr);
            // 14 × 4 = 56-byte block, zero-filled.
            Assert.Equal(56, EventTalkGroupFE7ViewModel.NewAllocSize);
            for (uint i = 0; i < EventTalkGroupFE7ViewModel.NewAllocSize; i++)
            {
                Assert.Equal(0u, rom.u8(addr + i));
            }
            // The new block lists 14 entries.
            Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, vm.LoadList().Count);
        }
        finally
        {
            CoreState.AppendBinaryData = savedAppend;
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    static ROM MakeRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synth-AE7E01-1442.gba", bytes, "AE7E01");
        return rom;
    }

    static void PlantU16(ROM rom, uint addr, ushort value)
    {
        rom.Data[(int)addr + 0] = (byte)(value & 0xFF);
        rom.Data[(int)addr + 1] = (byte)((value >> 8) & 0xFF);
    }
}
