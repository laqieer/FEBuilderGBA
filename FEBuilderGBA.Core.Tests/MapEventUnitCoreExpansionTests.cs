// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the new MapEventUnitCore helpers needed by the Avalonia
// EventUnitFE7View New Allocation + Expand List + jump-to-target features (#431).
//
// These tests construct synthetic FE7U ROM bytes so we exercise the
// pointer-table allocation + starter-byte initialization + repointing
// behaviors without needing a real ROM file on disk.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MapEventUnitCoreExpansionTests
{
    /// <summary>
    /// Build a minimal synthetic FE7U ROM with an event-unit pointer slot
    /// pointing at a small table at <paramref name="tableAddr"/>.
    /// Each entry is 16 bytes (FE7 eventunit_data_size). Entries 0..rowCount-1
    /// have UnitID=0x10+i, ClassID=0x20+i (distinguishing bytes) so tests
    /// can confirm row preservation. The terminator row (UnitID=0) sits
    /// after rowCount entries.
    /// </summary>
    static ROM MakeFe7uWithUnitTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        // Lay out rowCount rows at tableAddr.
        for (int i = 0; i < rowCount; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 16)));
            bytes[rowBase + 0] = (byte)(0x10 + i); // UnitID — distinguishing
            bytes[rowBase + 1] = (byte)(0x20 + i); // ClassID
        }
        // Terminator row (all zeros) sits immediately after the last row.

        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // ExpandUnitList: write starter bytes, copy old rows, repoint pointer
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandUnitList_NewRows_HaveB0EqualsOneAndB1EqualsOne()
    {
        ROM rom = MakeFe7uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Use a dedicated pointer slot (Copilot review #522 third pass —
            // the slot is a 4-byte pointer location, distinct from the
            // table base it points to).
            uint pointerSlot = 0x200; // must be >= 0x200 to satisfy U.isSafetyOffset
            uint tableAddr = 0x00800000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);
            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 10);

            Assert.NotEqual(U.NOT_FOUND, newBase);

            // New rows (4..9) must have B0=0x01, B1=0x01 per WF
            // AddressListExpandsEvent (EventUnitFE7Form.cs:485-490).
            for (uint i = 4; i < 10; i++)
            {
                uint rowAddr = newBase + i * 16;
                Assert.Equal((byte)0x01, rom.Data[rowAddr + 0]);
                Assert.Equal((byte)0x01, rom.Data[rowAddr + 1]);
                // Remaining 14 bytes must be zero (untouched).
                for (uint b = 2; b < 16; b++)
                {
                    Assert.Equal((byte)0x00, rom.Data[rowAddr + b]);
                }
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_OldRows_PreservedByteForByte()
    {
        ROM rom = MakeFe7uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Use a dedicated pointer slot (Copilot review #522 third pass).
            uint pointerSlot = 0x200; // must be >= 0x200 to satisfy U.isSafetyOffset
            uint tableAddr = 0x00800000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);

            // Snapshot original rows BEFORE expansion.
            byte[] origRows = new byte[4 * 16];
            Array.Copy(rom.Data, tableAddr, origRows, 0, 4 * 16);

            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 10);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // The first 4 rows at newBase must match origRows byte-for-byte.
            for (int i = 0; i < 4 * 16; i++)
            {
                Assert.Equal(origRows[i], rom.Data[newBase + i]);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_PointerRepointed_AfterReallocation()
    {
        ROM rom = MakeFe7uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;

            // We use a real RomInfo pointer slot — pick a known unused
            // address from FE7U's RomInfo for the pointer holding test
            // (e.g. eventunit_data_size is a size, not a slot — we just
            // verify the helper accepts a slot+oldBase and rewrites it).
            // For this test, allocate a 4-byte slot at 0x100 and put a
            // pointer there.
            uint pointerSlot = 0x200; // must be >= 0x200 to satisfy U.isSafetyOffset
            uint tableAddr = 0x00800000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);

            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 10);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            Assert.NotEqual(tableAddr, newBase);

            uint finalPointer = rom.p32(pointerSlot);
            Assert.Equal(newBase, finalPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_RejectsShrink()
    {
        ROM rom = MakeFe7uWithUnitTable(rowCount: 10);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint result = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: 0x100,
                oldBase: 0x00800000u,
                oldCount: 10,
                newCount: 4);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_RejectsZeroPointerSlot()
    {
        ROM rom = MakeFe7uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint result = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: 0,
                oldBase: 0x00800000u,
                oldCount: 4,
                newCount: 10);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_WritesZeroTerminator_AfterExpandedRows()
    {
        // Regression for Copilot CLI PR #522 second-pass review: when the
        // free region returned by FindFreeSpace is filled with 0xFF, the
        // post-table bytes would look like garbage rows to EnumerateUnits /
        // WF AddressList. The fix explicitly writes a zero terminator row
        // at newBase + newCount * blockSize so the table is well-bounded.
        //
        // We pre-fill the high half of the ROM with 0xFF, plant our table
        // at 0x00800000, run the expansion, and assert the byte at
        // newBase + newCount * blockSize == 0 (terminator).
        var bytes = new byte[0x1100000];
        // Fill the high half with 0xFF — simulates the WF "high-half is
        // empty" allocator path picking a 0xFF-filled free region. Start
        // past 0x00880000 so the original table at 0x00800000 stays clean.
        for (int i = 0x880000; i < bytes.Length; i++) bytes[i] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        uint origPointerSlot = 0x200; // must be >= 0x200 to satisfy U.isSafetyOffset
        uint tableAddr = 0x00800000u;
        for (int i = 0; i < 4; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 16)));
            rom.Data[rowBase + 0] = (byte)(0x10 + i);
            rom.Data[rowBase + 1] = (byte)(0x20 + i);
        }
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, origPointerSlot);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: origPointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 10);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // The terminator row (16 bytes of 0) must sit at newBase + 10*16.
            uint termAddr = newBase + 10u * 16u;
            for (int b = 0; b < 16; b++)
            {
                Assert.Equal((byte)0x00, rom.Data[termAddr + b]);
            }

            // CountEventUnitRows must return exactly 10 (terminator stops it).
            uint actualCount = MapEventUnitCore.CountEventUnitRows(rom, newBase);
            Assert.Equal(10u, actualCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // FindBattleTalkFE7UnitIdAddress — both tables, both columns
    // -----------------------------------------------------------------

    [Fact]
    public void FindBattleTalkFE7UnitIdAddress_Table1_FirstColumnMatch_ReturnsAddress()
    {
        ROM rom = MakeFe7uWithBattleTalkTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Table 1, row 1 has B0 = 0x05 (Eliwood placeholder unit id).
            uint hit = MapEventUnitCore.FindBattleTalkFE7UnitIdAddress(rom, 0x05);
            Assert.NotEqual(0u, hit);

            // The hit address must be inside table 1 (which starts at our
            // synthetic tableAddr1 = 0x00B00000) and the row's u8@0 must be 0x05.
            Assert.Equal((byte)0x05, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE7UnitIdAddress_Table1_SecondColumnMatch_ReturnsAddress()
    {
        ROM rom = MakeFe7uWithBattleTalkTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Table 1, row 2 has B0=0x06, B1=0x99 (second column unique).
            uint hit = MapEventUnitCore.FindBattleTalkFE7UnitIdAddress(rom, 0x99);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x99, rom.Data[hit + 1]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE7UnitIdAddress_Table2_Match_ReturnsAddress()
    {
        ROM rom = MakeFe7uWithBattleTalkTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Only table 2 contains B0=0xAA.
            uint hit = MapEventUnitCore.FindBattleTalkFE7UnitIdAddress(rom, 0xAA);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0xAA, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE7UnitIdAddress_NoMatch_ReturnsZero()
    {
        ROM rom = MakeFe7uWithBattleTalkTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Neither table contains B0/B1=0xCC.
            uint hit = MapEventUnitCore.FindBattleTalkFE7UnitIdAddress(rom, 0xCC);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe7uWithBattleTalkTables()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        // Get the RomInfo pointer slots (these are real ROMFE7U offsets).
        uint p1 = rom.RomInfo.event_ballte_talk_pointer;
        uint p2 = rom.RomInfo.event_ballte_talk2_pointer;
        Assert.True(p1 != 0, "ROMFE7U must define event_ballte_talk_pointer");
        Assert.True(p2 != 0, "ROMFE7U must define event_ballte_talk2_pointer");

        // Table 1: 16-byte blocks (FE7 battle-talk main, verified in
        // EventBattleTalkFE7Form.Init). Terminator: u16@0 == 0 || 0xFFFF.
        // Row 0: B0=0x01, B1=0x02
        // Row 1: B0=0x05, B1=0x06
        // Row 2: B0=0x06, B1=0x99
        // Row 3: terminator (u16 = 0).
        const int t1Block = 16;
        uint t1 = 0x00B00000u;
        bytes[t1 + 0 * t1Block + 0] = 0x01; bytes[t1 + 0 * t1Block + 1] = 0x02;
        bytes[t1 + 1 * t1Block + 0] = 0x05; bytes[t1 + 1 * t1Block + 1] = 0x06;
        bytes[t1 + 2 * t1Block + 0] = 0x06; bytes[t1 + 2 * t1Block + 1] = 0x99;
        BitConverter.GetBytes(t1 | 0x08000000u).CopyTo(bytes, checked((int)p1));

        // Table 2: 12-byte blocks (FE7 battle-talk-2 a.k.a. N1, verified in
        // EventBattleTalkFE7Form.N1_Init). Terminator: u8@0 == 0 || 0xFF.
        const int t2Block = 12;
        uint t2 = 0x00B10000u;
        bytes[t2 + 0 * t2Block + 0] = 0xAA; bytes[t2 + 0 * t2Block + 1] = 0xBB;
        BitConverter.GetBytes(t2 | 0x08000000u).CopyTo(bytes, checked((int)p2));

        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // FindHaikuFE7Address — wildcard 0x45 / unit-only fallback / both tables
    // -----------------------------------------------------------------

    [Fact]
    public void FindHaikuFE7Address_FullMatch_ReturnsRow()
    {
        ROM rom = MakeFe7uWithHaikuTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Table 1, row 1: B0=0x05, B1=0x03 (full match unit=5, map=3).
            uint hit = MapEventUnitCore.FindHaikuFE7Address(rom, unitId: 0x05, mapId: 0x03);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x05, rom.Data[hit + 0]);
            Assert.Equal((byte)0x03, rom.Data[hit + 1]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE7Address_Wildcard0x45_MatchesAnyMap()
    {
        ROM rom = MakeFe7uWithHaikuTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Table 1, row 2: B0=0x07, B1=0x45 (wildcard map).
            // Searching with unit=0x07, mapId=0xFF should still match row 2.
            uint hit = MapEventUnitCore.FindHaikuFE7Address(rom, unitId: 0x07, mapId: 0xFF);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x07, rom.Data[hit + 0]);
            Assert.Equal((byte)0x45, rom.Data[hit + 1]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE7Address_FallsBackToUnitOnly_WhenNoFullMatch()
    {
        ROM rom = MakeFe7uWithHaikuTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Table 1, row 3: B0=0x09, B1=0x02 (unit-only fallback path).
            // Search for unit=0x09 mapId=0xFF: full match (map=0x02) fails,
            // but row 3 is the only unit=0x09 hit, so it should return.
            uint hit = MapEventUnitCore.FindHaikuFE7Address(rom, unitId: 0x09, mapId: 0xFF);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x09, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE7Address_Table2_Match_ReturnsRow()
    {
        ROM rom = MakeFe7uWithHaikuTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Only table 2 (tutorial) contains unit 0xAA.
            uint hit = MapEventUnitCore.FindHaikuFE7Address(rom, unitId: 0xAA, mapId: 0x10);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0xAA, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE7Address_NoMatch_ReturnsZero()
    {
        ROM rom = MakeFe7uWithHaikuTables();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindHaikuFE7Address(rom, unitId: 0xCC, mapId: 0x10);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe7uWithHaikuTables()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        // ROMFE7U: event_haiku_pointer (16-byte blocks),
        // event_haiku_tutorial_1_pointer (12-byte blocks).
        // Verified in EventHaikuFE7Form.Init (block size 16) and
        // EventHaikuFE7Form.N1_Init (block size 12).
        uint p1 = rom.RomInfo.event_haiku_pointer;
        uint p2 = rom.RomInfo.event_haiku_tutorial_1_pointer;
        Assert.True(p1 != 0, "ROMFE7U must define event_haiku_pointer");

        // Main haiku table uses 16-byte blocks per WF Init.
        const int blockSize = 16;
        uint t1 = 0x00B00000u;
        // Row 0: B0=0x01, B1=0x02
        bytes[t1 + 0 * blockSize + 0] = 0x01; bytes[t1 + 0 * blockSize + 1] = 0x02;
        // Row 1: B0=0x05, B1=0x03 (full match candidate)
        bytes[t1 + 1 * blockSize + 0] = 0x05; bytes[t1 + 1 * blockSize + 1] = 0x03;
        // Row 2: B0=0x07, B1=0x45 (wildcard map)
        bytes[t1 + 2 * blockSize + 0] = 0x07; bytes[t1 + 2 * blockSize + 1] = 0x45;
        // Row 3: B0=0x09, B1=0x02 (unit-only fallback candidate)
        bytes[t1 + 3 * blockSize + 0] = 0x09; bytes[t1 + 3 * blockSize + 1] = 0x02;
        // Row 4: terminator (B0=0)
        BitConverter.GetBytes(t1 | 0x08000000u).CopyTo(bytes, checked((int)p1));

        if (p2 != 0)
        {
            // N1 (tutorial) table uses 12-byte blocks.
            const int n1Block = 12;
            uint t2 = 0x00B10000u;
            // Row 0: B0=0xAA, B1=0x10
            bytes[t2 + 0 * n1Block + 0] = 0xAA; bytes[t2 + 0 * n1Block + 1] = 0x10;
            BitConverter.GetBytes(t2 | 0x08000000u).CopyTo(bytes, checked((int)p2));
        }

        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // FindBossBGMFE7UnitIdAddress — SoundBossBGM jump support
    // -----------------------------------------------------------------

    [Fact]
    public void FindBossBGMFE7UnitIdAddress_Match_ReturnsAddress()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        uint slot = rom.RomInfo.sound_boss_bgm_pointer;
        Assert.True(slot != 0, "ROMFE7U must define sound_boss_bgm_pointer");

        uint table = 0x00B00000u;
        // Block size for boss BGM is 8 bytes (BossBGM struct).
        const int blockSize = 8;
        bytes[table + 0 * blockSize + 0] = 0x01;
        bytes[table + 1 * blockSize + 0] = 0x07;
        bytes[table + 2 * blockSize + 0] = 0x12;
        BitConverter.GetBytes(table | 0x08000000u).CopyTo(bytes, checked((int)slot));
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindBossBGMFE7UnitIdAddress(rom, 0x07);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x07, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBossBGMFE7UnitIdAddress_NoMatch_ReturnsZero()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindBossBGMFE7UnitIdAddress(rom, 0xCC);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }
}
