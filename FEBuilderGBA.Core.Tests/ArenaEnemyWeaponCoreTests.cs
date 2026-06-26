// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ArenaEnemyWeaponCore — the GUI-free Core seam that backs the
// Avalonia Arena Enemy Weapon editor's TWO lists (#1465):
//   - Basic  : arena_enemy_weapon_basic_pointer,  stride 1, 8 entries.
//   - Rank-up: arena_enemy_weapon_rankup_pointer, stride 1, 0x1A (26) entries.
//
// Synthetic FE8U ROM bytes let us exercise the read + label logic and the
// edit -> reload round-trip without a real ROM on disk.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ArenaEnemyWeaponCoreTests
{
    const uint BasicTableAddr = 0x00800000u;
    const uint RankupTableAddr = 0x00800100u;

    /// <summary>
    /// Build a synthetic FE8U ROM with both arena weapon tables parked at known
    /// free addresses, with the two ROM pointer slots repointed to them.
    /// </summary>
    static ROM MakeFe8uWithArenaTables(byte[] basicBytes, byte[] rankupBytes)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint basicSlot = rom.RomInfo.arena_enemy_weapon_basic_pointer;
        uint rankupSlot = rom.RomInfo.arena_enemy_weapon_rankup_pointer;
        Assert.True(basicSlot != 0, "ROMFE8U must define arena_enemy_weapon_basic_pointer");
        Assert.True(rankupSlot != 0, "ROMFE8U must define arena_enemy_weapon_rankup_pointer");

        Array.Copy(basicBytes, 0, bytes, BasicTableAddr, basicBytes.Length);
        Array.Copy(rankupBytes, 0, bytes, RankupTableAddr, rankupBytes.Length);
        BitConverter.GetBytes(BasicTableAddr | 0x08000000u).CopyTo(bytes, basicSlot);
        BitConverter.GetBytes(RankupTableAddr | 0x08000000u).CopyTo(bytes, rankupSlot);

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        // The list builders resolve item names via NameResolver.GetItemName,
        // which reads the ambient CoreState.ROM. Point it at the synthetic ROM
        // so the display-name resolution doesn't NPE on a null ambient ROM.
        CoreState.ROM = rom;
        return rom;
    }

    static byte[] Seq(int count)
    {
        var b = new byte[count];
        for (int i = 0; i < count; i++) b[i] = (byte)(i + 1);
        return b;
    }

    // -----------------------------------------------------------------
    // List builders — counts + per-row addr/value
    // -----------------------------------------------------------------

    [Fact]
    public void BuildBasicList_Returns8Entries()
    {
        var rom = MakeFe8uWithArenaTables(Seq(8), Seq(0x1A));
        var list = ArenaEnemyWeaponCore.BuildBasicList(rom);
        Assert.Equal(ArenaEnemyWeaponCore.BasicCount, list.Count);
        Assert.Equal(8, list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(BasicTableAddr + (uint)i, list[i].addr);
            Assert.Equal((uint)i, list[i].tag);
            Assert.Equal((uint)(i + 1), rom.u8(list[i].addr));
        }
    }

    [Fact]
    public void BuildRankupList_Returns26Entries()
    {
        var rom = MakeFe8uWithArenaTables(Seq(8), Seq(0x1A));
        var list = ArenaEnemyWeaponCore.BuildRankupList(rom);
        Assert.Equal(ArenaEnemyWeaponCore.RankupCount, list.Count);
        Assert.Equal(0x1A, list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(RankupTableAddr + (uint)i, list[i].addr);
            Assert.Equal((uint)i, list[i].tag);
            Assert.Equal((uint)(i + 1), rom.u8(list[i].addr));
        }
    }

    [Fact]
    public void BuildLists_NullRom_ReturnsEmpty()
    {
        Assert.Empty(ArenaEnemyWeaponCore.BuildBasicList(null));
        Assert.Empty(ArenaEnemyWeaponCore.BuildRankupList(null));
    }

    // -----------------------------------------------------------------
    // Edit -> reload round-trip (the rank-up acceptance gate)
    // -----------------------------------------------------------------

    [Fact]
    public void BuildRankupList_EditSlotByte_ReloadShowsNewValue()
    {
        var rom = MakeFe8uWithArenaTables(Seq(8), Seq(0x1A));

        var before = ArenaEnemyWeaponCore.BuildRankupList(rom);
        // Edit slot index 5 (a 中ランク slot) to a new weapon id.
        uint slotAddr = before[5].addr;
        Assert.NotEqual((uint)0x77, rom.u8(slotAddr));
        rom.write_u8(slotAddr, 0x77);

        var after = ArenaEnemyWeaponCore.BuildRankupList(rom);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(slotAddr, after[5].addr);
        Assert.Equal((uint)0x77, rom.u8(after[5].addr));
        // Other slots untouched.
        Assert.Equal((uint)5, rom.u8(after[4].addr));
    }

    [Fact]
    public void BuildBasicList_EditSlotByte_ReloadShowsNewValue()
    {
        var rom = MakeFe8uWithArenaTables(Seq(8), Seq(0x1A));

        var before = ArenaEnemyWeaponCore.BuildBasicList(rom);
        uint slotAddr = before[2].addr;
        rom.write_u8(slotAddr, 0x55);

        var after = ArenaEnemyWeaponCore.BuildBasicList(rom);
        Assert.Equal(slotAddr, after[2].addr);
        Assert.Equal((uint)0x55, rom.u8(after[2].addr));
    }

    // -----------------------------------------------------------------
    // Label / guidance / icon-type — boundary indices
    // -----------------------------------------------------------------

    [Fact]
    public void GetBasicTypeName_CoversAllEightSlots()
    {
        // icontype must equal the slot index for slots 0..7 (剣..闇魔法).
        for (int i = 0; i < 8; i++)
        {
            string label = ArenaEnemyWeaponCore.GetBasicTypeName(i, out string disp, out uint icon);
            Assert.False(string.IsNullOrEmpty(label));
            Assert.False(string.IsNullOrEmpty(disp));
            Assert.Equal((uint)i, icon);
        }
    }

    [Fact]
    public void GetRankupTypeName_SeparatorRows_HaveFFIcon()
    {
        // WF: 0x03/0x07/0x0B/0x0F/0x13/0x16/0x18 are separators (icon 0xFF),
        // 0x19 is the terminator (icon 0xFF).
        int[] separators = { 0x03, 0x07, 0x0B, 0x0F, 0x13, 0x16, 0x18, 0x19 };
        foreach (int idx in separators)
        {
            ArenaEnemyWeaponCore.GetRankupTypeName(idx, out _, out uint icon);
            Assert.Equal((uint)0xFF, icon);
        }
    }

    [Fact]
    public void GetRankupTypeName_WeaponRows_HaveWeaponIcon()
    {
        // First-of-each-weapon "0" rows map to the basic weapon icon type.
        Assert.Equal((uint)0, IconOf(0x00)); // 剣0
        Assert.Equal((uint)1, IconOf(0x04)); // 槍0
        Assert.Equal((uint)2, IconOf(0x08)); // 斧0
        Assert.Equal((uint)3, IconOf(0x0C)); // 弓0
        Assert.Equal((uint)5, IconOf(0x10)); // 理魔法0
        Assert.Equal((uint)6, IconOf(0x14)); // 光魔法0
        Assert.Equal((uint)7, IconOf(0x17)); // 闇魔法0
    }

    [Fact]
    public void GetRankupTypeName_AllSlots_HaveLabelAndGuidance()
    {
        for (int i = 0; i < ArenaEnemyWeaponCore.RankupCount; i++)
        {
            string label = ArenaEnemyWeaponCore.GetRankupTypeName(i, out string disp, out _);
            Assert.False(string.IsNullOrEmpty(label), $"missing label at slot {i:X}");
            Assert.False(string.IsNullOrEmpty(disp), $"missing guidance at slot {i:X}");
        }
    }

    static uint IconOf(int idx)
    {
        ArenaEnemyWeaponCore.GetRankupTypeName(idx, out _, out uint icon);
        return icon;
    }
}
