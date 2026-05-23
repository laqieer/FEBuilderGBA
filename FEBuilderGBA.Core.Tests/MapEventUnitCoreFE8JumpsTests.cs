// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the new MapEventUnitCore FE8-specific jump-target search helpers
// and the version-aware ExpandUnitList overload, both required by the
// Avalonia EventUnitView gap-sweep (closes #420).
//
// FE8 differs from FE7 (PR #522) in three ways the existing helpers do not
// model:
//   1. BattleTalk: 16-byte blocks, u16@0/u16@2 unit-id match, u16@4 map filter.
//   2. Haiku: 12-byte blocks, u16@0 unit-id match, u8@3 map filter (0xFF = wildcard).
//   3. BossBGM: 8-byte blocks, u8@0 unit-id match (same as FE7).
//   4. List expansion: starter row B0=0x01, B1=0x02 (vs FE7's 0x01/0x01);
//      after-coord fields B7/D8 explicitly cleared to zero.
//
// W4 (UnitPos) round-trip: tests for the new ParseFe8UnitPos{X,Y,Ext} helpers
// in FEBuilderGBA.Core/U.cs that mirror the existing MakeFe8UnitPos packer.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MapEventUnitCoreFE8JumpsTests
{
    // -----------------------------------------------------------------
    // ParseFe8UnitPos round-trip — W4 decomposition helpers.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(0u, 0u, 0u)]
    [InlineData(1u, 2u, 3u)]
    [InlineData(15u, 22u, 5u)]
    [InlineData(63u, 63u, 7u)]   // max values for each field
    [InlineData(5u, 10u, 2u)]    // ItemDrop bit set
    public void ParseFe8UnitPos_RoundTrip_RecoversComponents(uint x, uint y, uint ext)
    {
        uint packed = U.MakeFe8UnitPos(x, y, ext);
        Assert.Equal(x, U.ParseFe8UnitPosX(packed));
        Assert.Equal(y, U.ParseFe8UnitPosY(packed));
        Assert.Equal(ext, U.ParseFe8UnitPosExt(packed));
    }

    [Fact]
    public void ParseFe8UnitPos_MaskingIgnoresHighBits()
    {
        // Packed with full 16 bits — the parsers must mask to the
        // canonical W4 layout: x (6 bits) | y << 6 (6 bits) | ext << 12 (3 bits).
        uint packed = 0xFFFFu;
        Assert.Equal(63u, U.ParseFe8UnitPosX(packed));
        Assert.Equal(63u, U.ParseFe8UnitPosY(packed));
        Assert.Equal(7u, U.ParseFe8UnitPosExt(packed));
    }

    // -----------------------------------------------------------------
    // FindBattleTalkFE8UnitIdAddress
    // -----------------------------------------------------------------

    [Fact]
    public void FindBattleTalkFE8UnitIdAddress_Column0Match_ReturnsRow()
    {
        ROM rom = MakeFe8uWithBattleTalkTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 0: u16@0 = 0x0001, u16@2 = 0x0002, u16@4 = 0x0010
            uint hit = MapEventUnitCore.FindBattleTalkFE8UnitIdAddress(rom, unitId: 0x0001, mapId: 0x0010);
            Assert.NotEqual(0u, hit);
            Assert.Equal((ushort)0x0001, rom.u16(hit + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE8UnitIdAddress_Column2Match_ReturnsRow()
    {
        ROM rom = MakeFe8uWithBattleTalkTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 1: u16@0 = 0x0099, u16@2 = 0x00BB, u16@4 = 0x0020
            // Searching for unitId=0xBB should match via column-2 (u16@2).
            uint hit = MapEventUnitCore.FindBattleTalkFE8UnitIdAddress(rom, unitId: 0x00BB, mapId: 0x0020);
            Assert.NotEqual(0u, hit);
            Assert.Equal((ushort)0x00BB, rom.u16(hit + 2));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE8UnitIdAddress_MapMismatch_FallsBackToUnitOnly()
    {
        ROM rom = MakeFe8uWithBattleTalkTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 1 has u16@4 = 0x0020. Searching with mapId=0x0099 (different)
            // should fall back to the unit-only match.
            uint hit = MapEventUnitCore.FindBattleTalkFE8UnitIdAddress(rom, unitId: 0x00BB, mapId: 0x0099);
            Assert.NotEqual(0u, hit);
            // Verify we landed on row 1 (the only row with 0xBB).
            Assert.Equal((ushort)0x00BB, rom.u16(hit + 2));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBattleTalkFE8UnitIdAddress_NoMatch_ReturnsZero()
    {
        ROM rom = MakeFe8uWithBattleTalkTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindBattleTalkFE8UnitIdAddress(rom, unitId: 0xCCCC, mapId: 0x0000);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe8uWithBattleTalkTable()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint p = rom.RomInfo.event_ballte_talk_pointer;
        Assert.True(p != 0, "ROMFE8U must define event_ballte_talk_pointer");

        // FE8 BattleTalk: 16-byte blocks, terminator u16==0xFFFF.
        // Row 0: u16@0=0x0001, u16@2=0x0002, u16@4=0x0010
        // Row 1: u16@0=0x0099, u16@2=0x00BB, u16@4=0x0020
        // Row 2: terminator (u16@0 == 0xFFFF)
        const int block = 16;
        uint t = 0x00B00000u;
        bytes[t + 0 * block + 0] = 0x01; bytes[t + 0 * block + 1] = 0x00;
        bytes[t + 0 * block + 2] = 0x02; bytes[t + 0 * block + 3] = 0x00;
        bytes[t + 0 * block + 4] = 0x10; bytes[t + 0 * block + 5] = 0x00;

        bytes[t + 1 * block + 0] = 0x99; bytes[t + 1 * block + 1] = 0x00;
        bytes[t + 1 * block + 2] = 0xBB; bytes[t + 1 * block + 3] = 0x00;
        bytes[t + 1 * block + 4] = 0x20; bytes[t + 1 * block + 5] = 0x00;

        // Terminator
        bytes[t + 2 * block + 0] = 0xFF; bytes[t + 2 * block + 1] = 0xFF;

        BitConverter.GetBytes(t | 0x08000000u).CopyTo(bytes, checked((int)p));

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // FindHaikuFE8Address
    // -----------------------------------------------------------------

    [Fact]
    public void FindHaikuFE8Address_FullMatch_ReturnsRow()
    {
        ROM rom = MakeFe8uWithHaikuTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 0: u16@0=0x0010, u8@3=0x05 (full match unit=0x10, map=5)
            uint hit = MapEventUnitCore.FindHaikuFE8Address(rom, unitId: 0x0010, mapId: 0x05);
            Assert.NotEqual(0u, hit);
            Assert.Equal((ushort)0x0010, rom.u16(hit + 0));
            Assert.Equal((byte)0x05, rom.Data[hit + 3]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE8Address_WildcardMap0xFF_MatchesAnyMap()
    {
        ROM rom = MakeFe8uWithHaikuTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 1: u16@0=0x0020, u8@3=0xFF (wildcard map).
            // Searching with any mapId should match.
            uint hit = MapEventUnitCore.FindHaikuFE8Address(rom, unitId: 0x0020, mapId: 0x77);
            Assert.NotEqual(0u, hit);
            Assert.Equal((ushort)0x0020, rom.u16(hit + 0));
            Assert.Equal((byte)0xFF, rom.Data[hit + 3]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE8Address_UnitMatch_MapMismatch_FallsBackToFirstUnitOnly()
    {
        ROM rom = MakeFe8uWithHaikuTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 2: u16@0=0x0030, u8@3=0x05. With mapId=0x99 (mismatch),
            // we should still get this row as the unit-only fallback.
            uint hit = MapEventUnitCore.FindHaikuFE8Address(rom, unitId: 0x0030, mapId: 0x99);
            Assert.NotEqual(0u, hit);
            Assert.Equal((ushort)0x0030, rom.u16(hit + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindHaikuFE8Address_NoMatch_ReturnsZero()
    {
        ROM rom = MakeFe8uWithHaikuTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindHaikuFE8Address(rom, unitId: 0xCCCC, mapId: 0x00);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe8uWithHaikuTable()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint p = rom.RomInfo.event_haiku_pointer;
        Assert.True(p != 0, "ROMFE8U must define event_haiku_pointer");

        // FE8 Haiku: 12-byte blocks, terminator u16==0xFFFF.
        // Row 0: u16@0=0x0010, u8@3=0x05 (full match candidate)
        // Row 1: u16@0=0x0020, u8@3=0xFF (wildcard map)
        // Row 2: u16@0=0x0030, u8@3=0x05 (unit-only fallback target)
        // Row 3: terminator (u16==0xFFFF)
        const int block = 12;
        uint t = 0x00C00000u;
        bytes[t + 0 * block + 0] = 0x10; bytes[t + 0 * block + 1] = 0x00;
        bytes[t + 0 * block + 3] = 0x05;

        bytes[t + 1 * block + 0] = 0x20; bytes[t + 1 * block + 1] = 0x00;
        bytes[t + 1 * block + 3] = 0xFF;

        bytes[t + 2 * block + 0] = 0x30; bytes[t + 2 * block + 1] = 0x00;
        bytes[t + 2 * block + 3] = 0x05;

        // Terminator
        bytes[t + 3 * block + 0] = 0xFF; bytes[t + 3 * block + 1] = 0xFF;

        BitConverter.GetBytes(t | 0x08000000u).CopyTo(bytes, checked((int)p));

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // FindBossBGMFE8UnitIdAddress
    // -----------------------------------------------------------------

    [Fact]
    public void FindBossBGMFE8UnitIdAddress_Match_ReturnsRow()
    {
        ROM rom = MakeFe8uWithBossBGMTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Row 1 has u8@0 = 0x22.
            uint hit = MapEventUnitCore.FindBossBGMFE8UnitIdAddress(rom, unitId: 0x22);
            Assert.NotEqual(0u, hit);
            Assert.Equal((byte)0x22, rom.Data[hit + 0]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBossBGMFE8UnitIdAddress_NoMatch_ReturnsZero()
    {
        ROM rom = MakeFe8uWithBossBGMTable();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindBossBGMFE8UnitIdAddress(rom, unitId: 0xDE);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindBossBGMFE8UnitIdAddress_TerminatorStops_FFFF()
    {
        // Ensure the iteration stops at the u16==0xFFFF terminator
        // (data past the terminator is NOT searched).
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint p = rom.RomInfo.sound_boss_bgm_pointer;
        Assert.True(p != 0, "ROMFE8U must define sound_boss_bgm_pointer");

        const int block = 8;
        uint t = 0x00D00000u;
        // Row 0: terminator immediately.
        bytes[t + 0] = 0xFF; bytes[t + 1] = 0xFF;
        // Row 1 (past terminator) has u8@0=0x55 — must NOT be found.
        bytes[t + block + 0] = 0x55;

        BitConverter.GetBytes(t | 0x08000000u).CopyTo(bytes, checked((int)p));
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint hit = MapEventUnitCore.FindBossBGMFE8UnitIdAddress(rom, unitId: 0x55);
            Assert.Equal(0u, hit);
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe8uWithBossBGMTable()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint p = rom.RomInfo.sound_boss_bgm_pointer;
        Assert.True(p != 0, "ROMFE8U must define sound_boss_bgm_pointer");

        // FE8 BossBGM: 8-byte blocks, terminator u16==0xFFFF.
        // Row 0: u8@0=0x11
        // Row 1: u8@0=0x22
        // Row 2: terminator
        const int block = 8;
        uint t = 0x00D00000u;
        bytes[t + 0 * block + 0] = 0x11;
        bytes[t + 1 * block + 0] = 0x22;
        // Terminator
        bytes[t + 2 * block + 0] = 0xFF;
        bytes[t + 2 * block + 1] = 0xFF;

        BitConverter.GetBytes(t | 0x08000000u).CopyTo(bytes, checked((int)p));

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // ExpandUnitList — FE8 starter variant (B0=0x01, B1=0x02).
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandUnitList_FE8_StarterB1Equals0x02()
    {
        ROM rom = MakeFe8uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint pointerSlot = 0x200;
            uint tableAddr = 0x00900000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);

            // Pass starterB1=0x02 explicitly for FE8 semantics.
            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 7,
                starterB1: 0x02);

            Assert.NotEqual(U.NOT_FOUND, newBase);

            // New rows 4..6 must have B0=0x01, B1=0x02.
            for (uint i = 4; i < 7; i++)
            {
                uint rowAddr = newBase + i * 20; // FE8 block size = 20
                Assert.Equal((byte)0x01, rom.Data[rowAddr + 0]);
                Assert.Equal((byte)0x02, rom.Data[rowAddr + 1]);
                // B7 (after-coord count) and D8 (after-coord pointer) must be zero.
                Assert.Equal((byte)0x00, rom.Data[rowAddr + 7]);
                for (uint b = 8; b < 12; b++)
                {
                    Assert.Equal((byte)0x00, rom.Data[rowAddr + b]);
                }
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_FE8_DefaultStarterB1_Is0x01_BackCompat()
    {
        // When starterB1 is omitted, the default is 0x01 (FE7 behavior).
        // This guards backwards compatibility for existing FE7 callsites.
        ROM rom = MakeFe7uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint pointerSlot = 0x200;
            uint tableAddr = 0x00800000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);

            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 7);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            // FE7 block size = 16, starter B1=0x01 by default.
            uint rowAddr = newBase + 4u * 16u;
            Assert.Equal((byte)0x01, rom.Data[rowAddr + 1]);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandUnitList_FE8_OldRowsPreserved()
    {
        ROM rom = MakeFe8uWithUnitTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint pointerSlot = 0x200;
            uint tableAddr = 0x00900000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, pointerSlot);

            // Snapshot original rows BEFORE expansion.
            byte[] origRows = new byte[4 * 20];
            Array.Copy(rom.Data, tableAddr, origRows, 0, 4 * 20);

            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom,
                eventPointerSlot: pointerSlot,
                oldBase: tableAddr,
                oldCount: 4,
                newCount: 7,
                starterB1: 0x02);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            for (int i = 0; i < 4 * 20; i++)
            {
                Assert.Equal(origRows[i], rom.Data[newBase + i]);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    static ROM MakeFe8uWithUnitTable(int rowCount, uint tableAddr = 0x00900000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // FE8 block size = 20. Lay out rowCount rows.
        for (int i = 0; i < rowCount; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 20)));
            bytes[rowBase + 0] = (byte)(0x10 + i);
            bytes[rowBase + 1] = (byte)(0x20 + i);
        }
        // Terminator row is all zeros after rowCount rows.

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static ROM MakeFe7uWithUnitTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");

        for (int i = 0; i < rowCount; i++)
        {
            int rowBase = checked((int)(tableAddr + (uint)(i * 16)));
            bytes[rowBase + 0] = (byte)(0x10 + i);
            bytes[rowBase + 1] = (byte)(0x20 + i);
        }

        rom.LoadLow("synthetic-fe7u.gba", bytes, "AE7E01");
        return rom;
    }
}
