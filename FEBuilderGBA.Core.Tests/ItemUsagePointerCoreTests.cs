// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ItemUsagePointerCore — the Core-side extraction of
// ItemUsagePointerForm switch2 dispatch + PatchUtil.Switch2Expands. (#440)
//
// These tests construct synthetic FE8U ROM bytes so we can exercise the
// switch2 metadata reads + Switch2Expands ROM mutation without needing
// a real ROM file on disk.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ItemUsagePointerCoreTests
{
    /// <summary>
    /// Build a tiny synthetic FE8U ROM with a valid Switch2 ASM signature
    /// at the item_usability_array_switch2_address and a small pointer
    /// table at the usability_array. The Switch2 metadata is hand-laid
    /// to encode (start=0, count=4) so MakeRows returns 5 entries by the
    /// `count + 1` convention.
    /// </summary>
    static ROM MakeFe8uWithSwitch2(byte[] funcPtrTableBytes, byte start = 0, byte countMinusOne = 4)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        uint ptrSlot = rom.RomInfo.item_usability_array_pointer;
        Assert.True(switchAddr != 0, "ROMFE8U must define item_usability_array_switch2_address");
        Assert.True(ptrSlot != 0, "ROMFE8U must define item_usability_array_pointer");

        // Plant Switch2 ASM signature at switchAddr:
        //  +0  start       (u8)
        //  +1  SUB opcode  (0x38..0x3D — pick 0x38)
        //  +2  count - 1   (u8)
        //  +3  CMP opcode  (0x28..0x2D — pick 0x28)
        bytes[switchAddr + 0] = start;
        bytes[switchAddr + 1] = 0x38;
        bytes[switchAddr + 2] = countMinusOne;
        bytes[switchAddr + 3] = 0x28;
        // Re-load so ROM caches the data
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // Park the pointer-table at a known free address, point the slot to it.
        uint tableAddr = 0x00800000u;
        Array.Copy(funcPtrTableBytes, 0, bytes, tableAddr, funcPtrTableBytes.Length);
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, ptrSlot);

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // IsSwitch2Enable — byte-pattern detection
    // -----------------------------------------------------------------

    [Fact]
    public void IsSwitch2Enable_VanillaRom_ReturnsFalse()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("vanilla.gba", bytes, "BE8E01");
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        // Vanilla bytes are 0 — SUB opcode 0x00 is outside the 0x38..0x3D range.
        Assert.False(ItemUsagePointerCore.IsSwitch2Enable(rom, switchAddr));
    }

    [Fact]
    public void IsSwitch2Enable_WithSignature_ReturnsTrue()
    {
        var rom = MakeFe8uWithSwitch2(new byte[20], start: 0, countMinusOne: 4);
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        Assert.True(ItemUsagePointerCore.IsSwitch2Enable(rom, switchAddr));
    }

    [Fact]
    public void IsSwitch2Enable_NullRom_ReturnsFalse()
    {
        Assert.False(ItemUsagePointerCore.IsSwitch2Enable(null!, 0x12345));
    }

    [Fact]
    public void IsSwitch2Enable_ZeroAddress_ReturnsFalse()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("vanilla.gba", bytes, "BE8E01");
        Assert.False(ItemUsagePointerCore.IsSwitch2Enable(rom, 0u));
    }

    // -----------------------------------------------------------------
    // ReadSwitch2 — start + (count + 1) read
    // -----------------------------------------------------------------

    [Fact]
    public void ReadSwitch2_WithSignature_ReturnsStartAndCountPlusOne()
    {
        var rom = MakeFe8uWithSwitch2(new byte[20], start: 0x10, countMinusOne: 0x05);
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        var result = ItemUsagePointerCore.ReadSwitch2(rom, switchAddr);
        Assert.NotNull(result);
        Assert.Equal((uint)0x10, result!.Value.Start);
        Assert.Equal((uint)0x06, result.Value.TotalCount); // 5 + 1
    }

    [Fact]
    public void ReadSwitch2_NoSignature_ReturnsNull()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("vanilla.gba", bytes, "BE8E01");
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        var result = ItemUsagePointerCore.ReadSwitch2(rom, switchAddr);
        Assert.Null(result);
    }

    // -----------------------------------------------------------------
    // MakeRows — Switch2-count semantics (#440 acceptance, point 3)
    // -----------------------------------------------------------------

    /// <summary>
    /// Critical regression test: when a NULL pointer (0) is parked in the
    /// middle of the function-pointer table, MakeRows must NOT truncate
    /// the list at that point. It must walk all `count + 1` entries.
    /// </summary>
    [Fact]
    public void MakeRows_NullPointerMidList_DoesNotTruncate()
    {
        // 5 entries (count=4 + 1):
        //   row 0: valid pointer 0x08123456
        //   row 1: valid pointer 0x08123466
        //   row 2: NULL (0x00000000) — the regression bug would stop here
        //   row 3: valid pointer 0x08123476
        //   row 4: valid pointer 0x08123486
        byte[] table = new byte[5 * 4];
        BitConverter.GetBytes(0x08123456u).CopyTo(table, 0);
        BitConverter.GetBytes(0x08123466u).CopyTo(table, 4);
        BitConverter.GetBytes(0x00000000u).CopyTo(table, 8);   // NULL gap
        BitConverter.GetBytes(0x08123476u).CopyTo(table, 12);
        BitConverter.GetBytes(0x08123486u).CopyTo(table, 16);

        var rom = MakeFe8uWithSwitch2(table, start: 0, countMinusOne: 4);
        var rows = ItemUsagePointerCore.MakeRows(rom, ItemUsagePointerCore.FilterKind.Usability);

        // EXACTLY 5 rows — NULL gap does not truncate.
        Assert.Equal(5, rows.Count);
        // The NULL row stays in the list as "Func=0x00000000".
        Assert.Contains("Func=0x00000000", rows[2].name);
        // Trailing valid pointer is preserved.
        Assert.Contains("Func=0x08123476", rows[3].name);
        Assert.Contains("Func=0x08123486", rows[4].name);
    }

    [Fact]
    public void MakeRows_RespectsStartItemId()
    {
        // start=0x10 means row 0 -> itemId 0x10, row 1 -> 0x11, etc.
        byte[] table = new byte[3 * 4];
        BitConverter.GetBytes(0x08100000u).CopyTo(table, 0);
        BitConverter.GetBytes(0x08100100u).CopyTo(table, 4);
        BitConverter.GetBytes(0x08100200u).CopyTo(table, 8);

        var rom = MakeFe8uWithSwitch2(table, start: 0x10, countMinusOne: 2);
        var rows = ItemUsagePointerCore.MakeRows(rom, ItemUsagePointerCore.FilterKind.Usability);

        Assert.Equal(3, rows.Count);
        // Item ID column should reflect start offset.
        // U.ToHexString format: "X02" for values <= 0xFF (no "0x" prefix).
        Assert.StartsWith("10", rows[0].name);
        Assert.StartsWith("11", rows[1].name);
        Assert.StartsWith("12", rows[2].name);
    }

    [Fact]
    public void MakeRows_NoSwitch2_ReturnsEmpty()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("vanilla.gba", bytes, "BE8E01");
        var rows = ItemUsagePointerCore.MakeRows(rom, ItemUsagePointerCore.FilterKind.Usability);
        Assert.Empty(rows);
    }

    [Fact]
    public void MakeRows_NullRom_ReturnsEmpty()
    {
        var rows = ItemUsagePointerCore.MakeRows(null!, ItemUsagePointerCore.FilterKind.Usability);
        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------
    // GetAllFilters — 10 metadata rows in display order
    // -----------------------------------------------------------------

    [Fact]
    public void GetAllFilters_ReturnsExactlyTenFiltersInDisplayOrder()
    {
        var filters = ItemUsagePointerCore.GetAllFilters();
        Assert.Equal(10, filters.Count);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Usability,    filters[0].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Effect,       filters[1].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Promotion1,   filters[2].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Promotion2,   filters[3].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Staff1,       filters[4].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.Staff2,       filters[5].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.StatBooster1, filters[6].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.StatBooster2, filters[7].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.ErrorMessage, filters[8].Kind);
        Assert.Equal(ItemUsagePointerCore.FilterKind.NameArticle,  filters[9].Kind);
    }

    [Fact]
    public void GetPointerSlot_Fe8uExpectedKinds_AllResolveToNonZero()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("vanilla.gba", bytes, "BE8E01");

        // ROMFE8U defines: Usability, Effect, Promotion1, Staff1, Staff2,
        // StatBooster1, StatBooster2, ErrorMessage, NameArticle.
        // Promotion2 is FE7-only — explicitly zero in FE8U/FE8J.
        var fe8uKinds = new[]
        {
            ItemUsagePointerCore.FilterKind.Usability,
            ItemUsagePointerCore.FilterKind.Effect,
            ItemUsagePointerCore.FilterKind.Promotion1,
            ItemUsagePointerCore.FilterKind.Staff1,
            ItemUsagePointerCore.FilterKind.Staff2,
            ItemUsagePointerCore.FilterKind.StatBooster1,
            ItemUsagePointerCore.FilterKind.StatBooster2,
            ItemUsagePointerCore.FilterKind.ErrorMessage,
            ItemUsagePointerCore.FilterKind.NameArticle,
        };
        foreach (var kind in fe8uKinds)
        {
            uint p = ItemUsagePointerCore.GetPointerSlot(rom, kind);
            Assert.True(p != 0, $"FilterKind.{kind} has no pointer slot in ROMFE8U");
        }

        // Promotion2 is intentionally absent for FE8U (FE7-only mechanic).
        Assert.Equal(0u,
            ItemUsagePointerCore.GetPointerSlot(rom, ItemUsagePointerCore.FilterKind.Promotion2));
    }

    [Fact]
    public void GetPointerSlot_Fe7uPromotion2_ResolvesToNonZero()
    {
        // ROM.LoadLow requires >= 0x1000000 bytes for the FE7U code path.
        var bytes = new byte[0x1000000];
        var rom = new ROM();
        rom.LoadLow("vanilla-fe7u.gba", bytes, "AE7E01");
        Assert.NotNull(rom.RomInfo);
        // Sanity: ROMFE7U defines item_promotion2_array_pointer.
        uint p = ItemUsagePointerCore.GetPointerSlot(rom, ItemUsagePointerCore.FilterKind.Promotion2);
        Assert.True(p != 0, "FE7U should define Promotion2 pointer slot");
    }

    // -----------------------------------------------------------------
    // Switch2Expands — ROM-mutation round-trip
    // -----------------------------------------------------------------

    [Fact]
    public void Switch2Expands_ExpandsTableAndUpdatesSwitchMetadata()
    {
        byte[] table = new byte[3 * 4];
        BitConverter.GetBytes(0x08100000u).CopyTo(table, 0);
        BitConverter.GetBytes(0x08100100u).CopyTo(table, 4);
        BitConverter.GetBytes(0x08100200u).CopyTo(table, 8);

        var rom = MakeFe8uWithSwitch2(table, start: 0, countMinusOne: 2);
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        uint ptrSlot = rom.RomInfo.item_usability_array_pointer;

        // Save the previous wiring so the test cleans up.
        var prevAppender = CoreState.AppendBinaryData;
        var prevRom = CoreState.ROM;
        var prevServices = CoreState.Services;
        try
        {
            // CoreState.ROM must be set BEFORE creating UndoData, because
            // Undo.NewUndoDataLow reads CoreState.ROM.Data.Length.
            CoreState.ROM = rom;
            CoreState.Services = null; // Skip user confirmation.

            // Use a simple appender that puts the new table at a free
            // region of the synthetic ROM bytes.
            uint nextFree = 0x00900000u;
            CoreState.AppendBinaryData = (data, undo) =>
            {
                uint dst = nextFree;
                for (int i = 0; i < data.Length; i++)
                    rom.write_u8(dst + (uint)i, data[i], undo);
                nextFree += (uint)(((data.Length + 3) / 4) * 4);
                return dst;
            };

            // Properly initialize UndoData — write_u*(undo) requires .list.
            var undoBuf = new Undo();
            var undo = undoBuf.NewUndoData("test", "Switch2Expands");
            uint newAddr = ItemUsagePointerCore.Switch2Expands(
                rom,
                ptrSlot,
                switchAddr,
                newCount: 8u,
                defaultJumpAddr: 0x08FFFFFEu,
                undodata: undo);

            Assert.NotEqual(U.NOT_FOUND, newAddr);

            // Switch2 metadata updated: start=0, count-1 = 7.
            Assert.Equal((uint)0, rom.u8(switchAddr + 0));
            Assert.Equal((uint)7, rom.u8(switchAddr + 2));

            // The pointer slot was updated to the new table address.
            uint newGbaPtr = rom.u32(ptrSlot);
            Assert.Equal(newAddr | 0x08000000u, newGbaPtr);
        }
        finally
        {
            CoreState.AppendBinaryData = prevAppender;
            CoreState.ROM = prevRom;
            CoreState.Services = prevServices;
        }
    }

    [Fact]
    public void Switch2Expands_AlreadyLargeEnough_ReturnsNotFound()
    {
        byte[] table = new byte[5 * 4];
        var rom = MakeFe8uWithSwitch2(table, start: 0, countMinusOne: 4);
        uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
        uint ptrSlot = rom.RomInfo.item_usability_array_pointer;

        var prevAppender = CoreState.AppendBinaryData;
        var prevRom = CoreState.ROM;
        var prevServices = CoreState.Services;
        try
        {
            CoreState.ROM = rom;
            CoreState.Services = null;
            CoreState.AppendBinaryData = (data, undo) => 0x900000u;

            var undoBuf2 = new Undo();
            var undo2 = undoBuf2.NewUndoData("test", "Switch2Expands_TooSmall");
            uint result = ItemUsagePointerCore.Switch2Expands(
                rom,
                ptrSlot,
                switchAddr,
                newCount: 3u, // Less than current 0 + (4 + 1) = 5
                defaultJumpAddr: 0x08FFFFFEu,
                undodata: undo2);

            Assert.Equal(U.NOT_FOUND, result);
        }
        finally
        {
            CoreState.AppendBinaryData = prevAppender;
            CoreState.ROM = prevRom;
            CoreState.Services = prevServices;
        }
    }

    /// <summary>
    /// MakeRows can be called when CoreState.ROM is not set (e.g. headless
    /// scanner) — it must not throw NRE. Pass the ROM via the rom argument.
    /// </summary>
    [Fact]
    public void MakeRows_WithoutCoreStateRom_DoesNotThrow()
    {
        byte[] table = new byte[3 * 4];
        BitConverter.GetBytes(0x08100000u).CopyTo(table, 0);
        var rom = MakeFe8uWithSwitch2(table, start: 0, countMinusOne: 2);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null; // Simulate headless caller.
            var rows = ItemUsagePointerCore.MakeRows(rom, ItemUsagePointerCore.FilterKind.Usability);
            Assert.NotEmpty(rows);
        }
        finally { CoreState.ROM = prevRom; }
    }
}
