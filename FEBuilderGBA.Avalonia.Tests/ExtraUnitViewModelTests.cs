// SPDX-License-Identifier: GPL-3.0-or-later
// Parity / regression tests for ExtraUnitViewModel (FE8J Extra Unit editor, issue #1599).
//
// The Avalonia FE8J Extra Unit editor used to expose/write the read-only P0
// unit-data pointer and had no Flag editor at all, while the WinForms
// ExtraUnitForm's SOLE editable field is the per-entry FLAG byte at
// (i * 0x14 + 0x37E10). These tests drive the synthetic-ROM round-trip for the
// new editable Flag field and assert P0 is never mutated.
//
// Marked [Collection("SharedState")] because the ViewModel reads CoreState.ROM.
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ExtraUnitViewModelTests
{
    // ROM layout used by the FE8J Extra Unit editor.
    const uint BaseAddress = 0x37EE4;   // P0 pointer table base
    const uint EntrySize = 4;           // 4-byte GBA pointer per entry

    // Flag byte address for entry i (matches the VM's private GetFlagAddr).
    static uint FlagAddr(int i) => (uint)(i * 0x14 + 0x37E10);

    static ROM MakeFE8JRom()
    {
        var rom = new ROM();
        byte[] data = new byte[0x1000000];

        // ---- P0 pointer table: 2 valid entries, then a terminator. ----
        // Each entry points into a synthetic "unit data" region where byte 0 is
        // a (1-based) unit id. The stored u32 must be a safety pointer so the
        // list scanner's U.isSafetyPointer(rom.u32(addr)) passes.
        for (uint i = 0; i < 2; i++)
        {
            uint unitDataOffset = 0x200000u + i * 0x40u;   // ROM offset of the unit data
            uint storedPtr = 0x08000000u + unitDataOffset; // GBA pointer form
            U.write_p32(data, BaseAddress + i * EntrySize, storedPtr);
            // Unit id byte at the pointed-to region (1-based).
            data[unitDataOffset] = (byte)(i + 1);
        }
        // Terminate after 2 entries with an invalid pointer so CountEntries stops.
        U.write_p32(data, BaseAddress + 2 * EntrySize, 0xFFFFFFFFu);

        // ---- Flag bytes at the SEPARATE absolute address i*0x14 + 0x37E10. ----
        data[FlagAddr(0)] = 0xAB;
        data[FlagAddr(1)] = 0xCD;

        rom.LoadLow("synth-fe8j-extraunit.gba", data, "BE8J01");
        return rom;
    }

    [Fact]
    public void LoadEntry_LoadsFlagByte()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = MakeFE8JRom();
            CoreState.Undo = new Undo();

            var vm = new ExtraUnitViewModel();
            var list = vm.LoadList();
            Assert.True(list.Count >= 2, $"Expected at least 2 entries, got {list.Count}");

            vm.LoadEntry(list[0].addr);
            Assert.True(vm.IsLoaded);
            Assert.Equal(0xABu, vm.FlagId);

            vm.LoadEntry(list[1].addr);
            Assert.Equal(0xCDu, vm.FlagId);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void WriteEntry_WritesFlagByte_RoundTrips()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = MakeFE8JRom();
            CoreState.Undo = new Undo();

            var vm = new ExtraUnitViewModel();
            var list = vm.LoadList();
            Assert.True(list.Count >= 2);

            vm.LoadEntry(list[0].addr);
            vm.FlagId = 0x42;
            vm.WriteEntry();

            // The flag byte at entry 0's absolute address must now be 0x42.
            Assert.Equal(0x42u, CoreState.ROM.u8(FlagAddr(0)));

            // A fresh load re-reads the persisted value.
            var vm2 = new ExtraUnitViewModel();
            vm2.LoadList();
            vm2.LoadEntry(list[0].addr);
            Assert.Equal(0x42u, vm2.FlagId);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void WriteEntry_DoesNotMutateP0()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = MakeFE8JRom();
            CoreState.Undo = new Undo();

            var vm = new ExtraUnitViewModel();
            var list = vm.LoadList();
            Assert.True(list.Count >= 2);

            vm.LoadEntry(list[0].addr);

            // Record the raw P0 pointer slot and the flag byte before writing.
            uint p0Before = CoreState.ROM.u32(list[0].addr);
            uint flagBefore = CoreState.ROM.u8(FlagAddr(0));

            // Change the flag to a NEW value (different from the planted 0xAB).
            vm.FlagId = 0x77;
            vm.WriteEntry();

            // P0 pointer slot MUST be untouched.
            Assert.Equal(p0Before, CoreState.ROM.u32(list[0].addr));

            // The flag byte MUST have changed.
            uint flagAfter = CoreState.ROM.u8(FlagAddr(0));
            Assert.NotEqual(flagBefore, flagAfter);
            Assert.Equal(0x77u, flagAfter);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }
}
