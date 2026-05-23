// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBGCore — the Core-side extraction of ImageBGForm's
// list-expansion / reserve-slot detection / BG256 LoadList rule
// helpers (#429).
//
// Mirrors the structure of `ImageBattleBGCoreTests` (PR #513).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageBGCoreTests
{
    static Undo.UndoData NewUndo(string name = "test") => new Undo.UndoData
    {
        time = DateTime.Now,
        name = name,
        list = new System.Collections.Generic.List<Undo.UndoPostion>(),
        filesize = 0,
    };

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with a small BG pointer table
    /// at a known free address, and the `bg_pointer` slot pointing
    /// to it. Each entry is 12 bytes: u32 image / u32 tsa / u32 palette.
    /// </summary>
    static ROM MakeFe8uWithBgTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];

        // FE8U `bg_pointer` is resolved by `U.FindROMPointer` against a list
        // of candidate slots: 0x00E894, 0x00ECF4, 0x00EDF8, 0x0010E44.
        // Pre-write the table + rows + pointer-to-table BEFORE LoadLow so
        // FindROMPointer picks the primary slot on the first scan and the
        // resulting RomInfo.bg_pointer matches our data.
        uint primaryPointerSlot = 0x00E894u;

        for (int i = 0; i < rowCount; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 12)));
            uint imgPtr = 0x08400000u | ((uint)i << 12);
            uint tsaPtr = 0x08500000u | ((uint)i << 12);
            uint palPtr = 0x08600000u | ((uint)i << 12);
            BitConverter.GetBytes(imgPtr).CopyTo(bytes, rowBase + 0);
            BitConverter.GetBytes(tsaPtr).CopyTo(bytes, rowBase + 4);
            BitConverter.GetBytes(palPtr).CopyTo(bytes, rowBase + 8);
        }

        if (rowCount > 0)
        {
            int termAddr = checked((int)(tableAddr + (uint)(rowCount * 12)));
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 0);
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 4);
        }

        // Plant the pointer-to-table at the FE8U primary bg_pointer slot.
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, (int)primaryPointerSlot);

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint pointerSlot = rom.RomInfo.bg_pointer;
        Assert.True(pointerSlot != 0, "ROMFE8U must define bg_pointer");
        return rom;
    }

    // -----------------------------------------------------------------
    // ExpandList — pointer repoint + row preservation + row[0] fill
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_RepointsBgPointer_ToNewBase()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origPointer = rom.p32(rom.RomInfo.bg_pointer);

            var undo = NewUndo();
            uint newBase = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            Assert.NotEqual(origPointer, newBase);

            uint finalPointer = rom.p32(rom.RomInfo.bg_pointer);
            Assert.Equal(newBase, finalPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_PreservesOldRows_ByteForByte()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origBase = rom.p32(rom.RomInfo.bg_pointer);

            byte[] origRows = new byte[4 * 12];
            Array.Copy(rom.Data, origBase, origRows, 0, 4 * 12);

            var undo = NewUndo();
            uint newBase = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            for (int i = 0; i < 4 * 12; i++)
            {
                Assert.Equal(origRows[i], rom.Data[newBase + i]);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_NewRows_AreClonesOfRow0()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origBase = rom.p32(rom.RomInfo.bg_pointer);

            byte[] row0 = new byte[12];
            Array.Copy(rom.Data, origBase, row0, 0, 12);

            var undo = NewUndo();
            uint newBase = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            for (int rowIdx = 4; rowIdx < 10; rowIdx++)
            {
                for (int byteIdx = 0; byteIdx < 12; byteIdx++)
                {
                    Assert.Equal(
                        row0[byteIdx],
                        rom.Data[newBase + rowIdx * 12 + byteIdx]);
                }
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsNewCountAbove255()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            uint result = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 256, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsShrinks()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 10);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            uint result = ImageBGCore.ExpandList(rom, oldCount: 10, newCount: 5, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsZeroOldCount()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            uint result = ImageBGCore.ExpandList(rom, oldCount: 0, newCount: 10, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsNullRom()
    {
        var undo = NewUndo();
        uint result = ImageBGCore.ExpandList(null!, oldCount: 4, newCount: 10, undo);
        Assert.Equal(U.NOT_FOUND, result);
    }

    [Fact]
    public void ExpandList_RejectsNullUndo_OnUndoOverload()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint result = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo: null!);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_AcceptsNewCount255_AtLimit()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            uint result = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 255, undo);
            Assert.NotEqual(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_AmbientScope_RecordsUndoEntries()
    {
        // Parameterless overload assumes an ambient undo scope is already open.
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            using (ROM.BeginUndoScope(undo))
            {
                uint result = ImageBGCore.ExpandList(rom, oldCount: 4, newCount: 10);
                Assert.NotEqual(U.NOT_FOUND, result);
            }
            // Undo list should contain at least the pointer-repoint entry + per-row writes.
            Assert.True(undo.list.Count > 0);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // IsReserveBgId
    // -----------------------------------------------------------------

    [Fact]
    public void IsReserveBgId_RecognisesFe8uBlack()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 1);
        // FE8U bg_reserve_black_bgid = 0x35.
        Assert.True(ImageBGCore.IsReserveBgId(rom, 0x35));
    }

    [Fact]
    public void IsReserveBgId_RecognisesFe8uRandom()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 1);
        // FE8U bg_reserve_random_bgid = 0x37.
        Assert.True(ImageBGCore.IsReserveBgId(rom, 0x37));
    }

    [Fact]
    public void IsReserveBgId_RejectsOtherIds()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 1);
        Assert.False(ImageBGCore.IsReserveBgId(rom, 0x00));
        Assert.False(ImageBGCore.IsReserveBgId(rom, 0x10));
        Assert.False(ImageBGCore.IsReserveBgId(rom, 0xFF));
    }

    [Fact]
    public void IsReserveBgId_NullRom_ReturnsFalse()
    {
        Assert.False(ImageBGCore.IsReserveBgId(null!, 0x35));
    }

    // -----------------------------------------------------------------
    // Is255BG — requires the BG256Color patch to be installed
    // -----------------------------------------------------------------

    [Fact]
    public void Is255BG_NotPatched_ReturnsFalse()
    {
        // Bare synthetic ROM does not have the BG256Color patch bytes.
        ROM rom = MakeFe8uWithBgTable(rowCount: 1);
        Assert.False(ImageBGCore.Is255BG(rom, 0x08400000u, 0));
    }

    [Fact]
    public void Is255BG_NullRom_ReturnsFalse()
    {
        Assert.False(ImageBGCore.Is255BG(null!, 0x08400000u, 0));
    }

    [Fact]
    public void Is255BG_Patched_NonZeroImageAndZeroP4_ReturnsTrue()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("fake-bg256.gba", bytes, "BE8E01");
        // Plant the BG256Color signature bytes at FE8U addr 0xE2DA.
        bytes[0xE2DA] = 0xC0; bytes[0xE2DB] = 0x46;
        bytes[0xE2DC] = 0xC0; bytes[0xE2DD] = 0x46;
        rom.LoadLow("fake-bg256.gba", bytes, "BE8E01");
        Assert.True(ImageBGCore.Is255BG(rom, 0x08400000u, 0));
        Assert.False(ImageBGCore.Is255BG(rom, 0u, 0)); // zero image
        Assert.False(ImageBGCore.Is255BG(rom, 0x08400000u, 1)); // P4 != 0
    }

    // -----------------------------------------------------------------
    // IsValidEntry — the WF LoadList scan rule
    // -----------------------------------------------------------------

    [Fact]
    public void IsValidEntry_Unpatched_RequiresBothPointers()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 1);
        // Both pointers — valid
        Assert.True(ImageBGCore.IsValidEntry(rom, 0x08400000u, 0x08500000u));
        // Image=null, TSA=ptr — also valid (isPointerOrNULL accepts NULL)
        Assert.True(ImageBGCore.IsValidEntry(rom, 0, 0x08500000u));
        // Image=garbage — invalid
        Assert.False(ImageBGCore.IsValidEntry(rom, 0x12345678u, 0x08500000u));
        // TSA=garbage — invalid
        Assert.False(ImageBGCore.IsValidEntry(rom, 0x08400000u, 0x12345678u));
        // P4=1 (flag) — invalid when unpatched (not a pointer)
        Assert.False(ImageBGCore.IsValidEntry(rom, 0x08400000u, 1));
    }

    [Fact]
    public void IsValidEntry_Patched_AcceptsP4Zero()
    {
        // Plant BG256 signature for FE8U.
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("bg256-fe8u.gba", bytes, "BE8E01");
        bytes[0xE2DA] = 0xC0; bytes[0xE2DB] = 0x46;
        bytes[0xE2DC] = 0xC0; bytes[0xE2DD] = 0x46;
        rom.LoadLow("bg256-fe8u.gba", bytes, "BE8E01");

        // Under BG256: P4=0 with valid image-or-null → valid
        Assert.True(ImageBGCore.IsValidEntry(rom, 0x08400000u, 0));
        // P4=1 same rule
        Assert.True(ImageBGCore.IsValidEntry(rom, 0x08400000u, 1));
        // P4=2 falls back to the pointer-or-null check on both
        Assert.False(ImageBGCore.IsValidEntry(rom, 0x08400000u, 2));
        // P4=ptr — valid (both pointers)
        Assert.True(ImageBGCore.IsValidEntry(rom, 0x08400000u, 0x08500000u));
    }
}
