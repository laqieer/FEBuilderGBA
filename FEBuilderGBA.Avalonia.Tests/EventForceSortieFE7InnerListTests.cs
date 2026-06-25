// SPDX-License-Identifier: GPL-3.0-or-later
// #1439 — the Avalonia Force Sortie (FE7) editor never loaded the inner
// unit-list (the D0-dereferenced 4-byte sub-entry list), so Unit ID /
// Unknown 1-3 always read 0 and never saved. These headless tests drive the
// ported EventForceSortieFE7ViewModel against SYNTHETIC ROMs and prove:
//   * LoadSubList dereferences D0 and walks 4-byte sub-entries.
//   * The two WinForms terminators are honored: id == 0x00 AND byte3 == 0xD1.
//   * An invalid / non-pointer D0 yields an empty list.
//   * A pointer near EOF does NOT over-read the terminator byte at addr+3.
//   * ResetSubEntry clears SubAddr + fields so a stale sub-entry can't be
//     written after switching to an invalid/empty outer entry.
//   * The combined undo round-trip: mutate BOTH the outer D0 field and a
//     selected sub-entry in one UndoService scope, then RunUndo restores both
//     byte ranges byte-identical.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM / .Undo.

using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EventForceSortieFE7InnerListTests : IDisposable
{
    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public EventForceSortieFE7InnerListTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    static ROM MakeRom(int size = 0x40000)
    {
        var rom = new ROM();
        rom.LoadLow("force-sortie-fe7-1439.gba", new byte[size], "NAZO");
        return rom;
    }

    static void WriteSubEntry(ROM rom, uint addr, byte id, byte b1, byte b2, byte b3)
    {
        rom.Data[addr + 0] = id;
        rom.Data[addr + 1] = b1;
        rom.Data[addr + 2] = b2;
        rom.Data[addr + 3] = b3;
    }

    // Make the VM point its outer entry's D0 at the given ROM offset by setting
    // UnitListPointer directly (the property the view-bound NumericUpDown sets,
    // and the value LoadSubList dereferences).
    static EventForceSortieFE7ViewModel VmWithUnitList(ROM rom, uint listOffset)
    {
        CoreState.ROM = rom;
        var vm = new EventForceSortieFE7ViewModel();
        vm.UnitListPointer = listOffset + 0x08000000u;
        return vm;
    }

    // ----------------------------------------------------------------------
    // Walk + terminators
    // ----------------------------------------------------------------------

    [Fact]
    public void LoadSubList_WalksEntries_UntilIdZeroTerminator()
    {
        var rom = MakeRom();
        uint list = 0x1000;
        WriteSubEntry(rom, list + 0, 0x05, 0x11, 0x22, 0x33);
        WriteSubEntry(rom, list + 4, 0x06, 0x44, 0x55, 0x66);
        WriteSubEntry(rom, list + 8, 0x00, 0x00, 0x00, 0x00); // id == 0x00 -> stop

        var vm = VmWithUnitList(rom, list);
        var sub = vm.LoadSubList();

        Assert.Equal(2, sub.Count);
        Assert.Equal(list + 0, sub[0].addr);
        Assert.Equal(list + 4, sub[1].addr);
        Assert.Equal(0x05u, sub[0].tag);
        Assert.Equal(0x06u, sub[1].tag);
    }

    [Fact]
    public void LoadSubList_Stops_OnByte3IsD1Terminator()
    {
        var rom = MakeRom();
        uint list = 0x1000;
        WriteSubEntry(rom, list + 0, 0x05, 0x11, 0x22, 0x33);
        // Second entry has a non-zero id but byte3 == 0xD1 -> terminator,
        // so this row is NOT included (WF predicate continues only while
        // id != 0x00 && term != 0xD1).
        WriteSubEntry(rom, list + 4, 0x07, 0x44, 0x55, 0xD1);
        WriteSubEntry(rom, list + 8, 0x08, 0x00, 0x00, 0x00);

        var vm = VmWithUnitList(rom, list);
        var sub = vm.LoadSubList();

        Assert.Single(sub);
        Assert.Equal(list + 0, sub[0].addr);
    }

    [Fact]
    public void LoadSubList_Label_UsesOneBasedUnitNameResolver()
    {
        // WF UnitForm.GetUnitName(uid) is 1-based (it does uid--), and the row's
        // portrait icon loader (UnitPortraitByIdLoader) is also 1-based off the
        // leading hex id. The label must therefore resolve names via the 1-based
        // NameResolver.GetUnitNameByOneBasedId, NOT the 0-based GetUnitName.
        var rom = MakeRom();
        uint list = 0x1000;
        byte id = 0x05;
        WriteSubEntry(rom, list, id, 0x11, 0x22, 0x33);
        var vm = VmWithUnitList(rom, list);

        var sub = vm.LoadSubList();
        Assert.Single(sub);

        string expected = U.ToHexString(id) + " " + NameResolver.GetUnitNameByOneBasedId(id);
        Assert.Equal(expected, sub[0].name);
        // The leading hex id is preserved so UnitPortraitByIdLoader's U.atoh(name)
        // parses the same 1-based id for the portrait icon.
        Assert.StartsWith(U.ToHexString(id), sub[0].name);
    }

    [Fact]
    public void LoadSubList_InvalidPointer_ReturnsEmpty()
    {
        var rom = MakeRom();
        var vm = new EventForceSortieFE7ViewModel();
        CoreState.ROM = rom;

        // Not a GBA pointer (< 0x08000000).
        vm.UnitListPointer = 0x12345;
        Assert.Empty(vm.LoadSubList());

        // Zero pointer.
        vm.UnitListPointer = 0;
        Assert.Empty(vm.LoadSubList());
    }

    [Fact]
    public void LoadSubList_PointerNearEof_DoesNotOverReadByte3()
    {
        int size = 0x40000;
        var rom = MakeRom(size);
        // Point the list so only 2 bytes remain (addr+3 would be out of bounds).
        uint list = (uint)size - 2;
        var vm = VmWithUnitList(rom, list);

        // Must not throw and must stop immediately (the 4-byte read is out of
        // range, so the bounded guard breaks before touching addr+3).
        var sub = vm.LoadSubList();
        Assert.Empty(sub);
    }

    [Fact]
    public void LoadSubList_PointerExactlyOneEntryBeforeEof_ReadsThatEntryThenStops()
    {
        int size = 0x40000;
        var rom = MakeRom(size);
        // Exactly one 4-byte entry fits at the very end. It is a valid (non
        // terminator) entry; the next read is past EOF so the walk stops with
        // no over-read.
        uint list = (uint)size - 4;
        WriteSubEntry(rom, list, 0x09, 0x01, 0x02, 0x03);
        var vm = VmWithUnitList(rom, list);

        var sub = vm.LoadSubList();
        Assert.Single(sub);
        Assert.Equal(list, sub[0].addr);
    }

    // ----------------------------------------------------------------------
    // Sub-entry load + reset
    // ----------------------------------------------------------------------

    [Fact]
    public void LoadSubEntry_PopulatesUnitIdAndUnknowns()
    {
        var rom = MakeRom();
        uint list = 0x1000;
        WriteSubEntry(rom, list, 0x05, 0x11, 0x22, 0x33);
        var vm = VmWithUnitList(rom, list);

        vm.LoadSubEntry(list);

        Assert.Equal(list, vm.SubAddr);
        Assert.Equal(0x05u, vm.UnitId);
        Assert.Equal(0x11u, vm.Unknown1);
        Assert.Equal(0x22u, vm.Unknown2);
        Assert.Equal(0x33u, vm.Unknown3);
    }

    [Fact]
    public void ResetSubEntry_ClearsSubAddrAndFields_PreventsStaleWrite()
    {
        var rom = MakeRom();
        uint list = 0x1000;
        WriteSubEntry(rom, list, 0x05, 0x11, 0x22, 0x33);
        var vm = VmWithUnitList(rom, list);

        vm.LoadSubEntry(list);
        Assert.NotEqual(0u, vm.SubAddr);

        vm.ResetSubEntry();
        Assert.Equal(0u, vm.SubAddr);
        Assert.Equal(0u, vm.UnitId);
        Assert.Equal(0u, vm.Unknown1);
        Assert.Equal(0u, vm.Unknown2);
        Assert.Equal(0u, vm.Unknown3);

        // After reset WriteSubEntry must no-op (SubAddr==0 guard): mutate the
        // field, call Write, assert ROM bytes unchanged.
        rom.Data[list + 0] = 0x05;
        vm.UnitId = 0x77;
        vm.WriteSubEntry();
        Assert.Equal(0x05, rom.Data[list + 0]); // unchanged — no stale write
    }

    // ----------------------------------------------------------------------
    // Combined undo round-trip (outer D0 + inner sub-entry in one scope)
    // ----------------------------------------------------------------------

    [Fact]
    public void Write_BothOuterAndSubEntry_UnderOneScope_RevertedByUndo()
    {
        var rom = MakeRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint outerAddr = 0x800;  // outer entry holds the D0 pointer
        uint list = 0x1000;      // inner unit list
        WriteSubEntry(rom, list, 0x05, 0x11, 0x22, 0x33);
        // Outer D0 initially points at the list.
        uint listPtr = list + 0x08000000u;
        rom.Data[outerAddr + 0] = (byte)(listPtr & 0xFF);
        rom.Data[outerAddr + 1] = (byte)((listPtr >> 8) & 0xFF);
        rom.Data[outerAddr + 2] = (byte)((listPtr >> 16) & 0xFF);
        rom.Data[outerAddr + 3] = (byte)((listPtr >> 24) & 0xFF);

        var vm = new EventForceSortieFE7ViewModel();
        vm.LoadEntry(outerAddr);
        Assert.Equal(listPtr, vm.UnitListPointer);
        vm.LoadSubEntry(list);

        byte[] outerSnap = new byte[4];
        byte[] subSnap = new byte[4];
        Array.Copy(rom.Data, (int)outerAddr, outerSnap, 0, 4);
        Array.Copy(rom.Data, (int)list, subSnap, 0, 4);

        try
        {
            uint newPtr = (list + 0x100) + 0x08000000u; // a different valid pointer
            var svc = new UndoService();
            svc.Begin("test-fe7-sortie");
            vm.UnitListPointer = newPtr;
            vm.UnitId = 0x09;
            vm.Unknown1 = 0xAA;
            vm.Write();
            vm.WriteSubEntry();
            svc.Commit();

            try
            {
                Assert.Equal(newPtr, rom.u32(outerAddr));
                Assert.Equal(0x09, rom.Data[list + 0]);
                Assert.Equal(0xAA, rom.Data[list + 1]);
            }
            finally
            {
                CoreState.Undo.RunUndo();
            }

            // Undo must restore BOTH byte ranges byte-identical.
            for (int i = 0; i < 4; i++)
                Assert.Equal(outerSnap[i], rom.Data[(int)outerAddr + i]);
            for (int i = 0; i < 4; i++)
                Assert.Equal(subSnap[i], rom.Data[(int)list + i]);
        }
        finally
        {
            Array.Copy(outerSnap, 0, rom.Data, (int)outerAddr, 4);
            Array.Copy(subSnap, 0, rom.Data, (int)list, 4);
        }
    }
}
