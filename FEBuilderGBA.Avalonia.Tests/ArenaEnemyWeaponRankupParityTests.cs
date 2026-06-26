// SPDX-License-Identifier: GPL-3.0-or-later
// #1465 — the Avalonia Arena Enemy Weapon editor exposed only the basic 8-entry
// weapon list; the WinForms ArenaEnemyWeaponForm also edits the 26-entry rank-up
// weapon list. These headless tests drive the ported VM against a synthetic FE8U
// ROM and prove:
//   * LoadArenaEnemyWeaponRankupList returns 26 entries (WF N_Init i<0x1A).
//   * The basic list still returns 8 entries (WF Init i<8).
//   * Both actual Avalonia AddressListControls (basic + rank-up) match the
//     Core builders BuildBasicList / BuildRankupList (two-list parity — the
//     exact regression #1465 reports).
//   * A rank-up slot edit -> Write -> reload round-trips through the ROM.
//   * Per-slot type labels/guidance resolve for both lists.
//   * The AXAML actually declares the second AddressListControl + rank-up
//     automation ids.
//
// [Collection("SharedState")] because the tests mutate CoreState.ROM.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ArenaEnemyWeaponRankupParityTests : IDisposable
{
    const uint BasicTableAddr = 0x00800000u;
    const uint RankupTableAddr = 0x00800100u;

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public ArenaEnemyWeaponRankupParityTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    static ROM MakeFe8uWithArenaTables()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("arena-1465.gba", bytes, "BE8E01");

        uint basicSlot = rom.RomInfo.arena_enemy_weapon_basic_pointer;
        uint rankupSlot = rom.RomInfo.arena_enemy_weapon_rankup_pointer;
        Assert.True(basicSlot != 0);
        Assert.True(rankupSlot != 0);

        for (uint i = 0; i < 8; i++) bytes[BasicTableAddr + i] = (byte)(i + 1);
        for (uint i = 0; i < 0x1A; i++) bytes[RankupTableAddr + i] = (byte)(i + 1);
        BitConverter.GetBytes(BasicTableAddr | 0x08000000u).CopyTo(bytes, basicSlot);
        BitConverter.GetBytes(RankupTableAddr | 0x08000000u).CopyTo(bytes, rankupSlot);

        rom.LoadLow("arena-1465.gba", bytes, "BE8E01");
        CoreState.ROM = rom;
        return rom;
    }

    // ----------------------------------------------------------------------
    // Counts
    // ----------------------------------------------------------------------

    [Fact]
    public void RankupList_Returns26Entries()
    {
        MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();
        Assert.Equal(0x1A, vm.LoadArenaEnemyWeaponRankupList().Count);
        Assert.Equal(0x1A, vm.GetRankupListCount());
    }

    [Fact]
    public void BasicList_StillReturns8Entries()
    {
        MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();
        Assert.Equal(8, vm.LoadArenaEnemyWeaponList().Count);
        Assert.Equal(8, vm.GetListCount());
    }

    // ----------------------------------------------------------------------
    // Two-list parity: VM lists match the Core builders by addr + name
    // ----------------------------------------------------------------------

    [Fact]
    public void BothLists_MatchCoreBuilders()
    {
        var rom = MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();

        var refBasic = ArenaEnemyWeaponCore.BuildBasicList(rom);
        var vmBasic = vm.LoadArenaEnemyWeaponList();
        Assert.Equal(refBasic.Count, vmBasic.Count);
        for (int i = 0; i < refBasic.Count; i++)
        {
            Assert.Equal(refBasic[i].addr, vmBasic[i].addr);
            Assert.Equal(refBasic[i].name, vmBasic[i].name);
        }

        var refRankup = ArenaEnemyWeaponCore.BuildRankupList(rom);
        var vmRankup = vm.LoadArenaEnemyWeaponRankupList();
        Assert.Equal(refRankup.Count, vmRankup.Count);
        for (int i = 0; i < refRankup.Count; i++)
        {
            Assert.Equal(refRankup[i].addr, vmRankup[i].addr);
            Assert.Equal(refRankup[i].name, vmRankup[i].name);
        }
    }

    // ----------------------------------------------------------------------
    // Edit -> Write -> reload round-trip (rank-up list)
    // ----------------------------------------------------------------------

    [Fact]
    public void RankupWrite_RoundTripsThroughRom()
    {
        var rom = MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();

        var list = vm.LoadArenaEnemyWeaponRankupList();
        uint slotAddr = list[9].addr; // a 中ランク slot
        vm.LoadArenaEnemyWeaponRankup(slotAddr);
        Assert.True(vm.RankupCanWrite);
        Assert.NotEqual((uint)0x42, vm.RankupWeaponId);

        vm.RankupWeaponId = 0x42;
        vm.WriteArenaEnemyWeaponRankup();

        Assert.Equal((uint)0x42, rom.u8(slotAddr));

        // Reload sees the new value; neighbours untouched.
        var after = vm.LoadArenaEnemyWeaponRankupList();
        Assert.Equal((uint)0x42, rom.u8(after[9].addr));
        Assert.Equal((uint)9, rom.u8(after[8].addr));
    }

    [Fact]
    public void BasicWrite_RoundTripsThroughRom()
    {
        var rom = MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();

        var list = vm.LoadArenaEnemyWeaponList();
        uint slotAddr = list[3].addr;
        vm.LoadArenaEnemyWeapon(slotAddr);
        vm.WeaponId = 0x31;
        vm.WriteArenaEnemyWeapon();

        Assert.Equal((uint)0x31, rom.u8(slotAddr));
    }

    // ----------------------------------------------------------------------
    // Type-info exposure
    // ----------------------------------------------------------------------

    [Fact]
    public void TypeInfo_ResolvesForBothLists()
    {
        MakeFe8uWithArenaTables();
        var vm = new ArenaEnemyWeaponViewerViewModel();

        var basic = vm.GetBasicTypeInfo(0);
        Assert.False(string.IsNullOrEmpty(basic.Label));
        Assert.False(string.IsNullOrEmpty(basic.Guidance));

        var rankup = vm.GetRankupTypeInfo(0x19); // terminator
        Assert.False(string.IsNullOrEmpty(rankup.Label));
        Assert.Equal((uint)0xFF, rankup.IconType);
    }

    // ----------------------------------------------------------------------
    // AXAML structure — two AddressListControls + rank-up automation ids
    // ----------------------------------------------------------------------

    [Fact]
    public void Axaml_DeclaresSecondListAndRankupControls()
    {
        string axaml = AxamlPath();
        Assert.True(File.Exists(axaml), $"AXAML not found at {axaml}");
        string text = File.ReadAllText(axaml);

        // Two AddressListControls (basic + rank-up).
        int listCount = CountOccurrences(text, "AddressListControl");
        Assert.True(listCount >= 2, $"expected >=2 AddressListControl, found {listCount}");

        Assert.Contains("ArenaEnemyWeaponViewer_Entry_List", text);
        Assert.Contains("ArenaEnemyWeaponViewer_Rankup_Entry_List", text);
        Assert.Contains("ArenaEnemyWeaponViewer_Rankup_WeaponId_Input", text);
        Assert.Contains("ArenaEnemyWeaponViewer_Rankup_Write_Button", text);
        Assert.Contains("ArenaEnemyWeaponViewer_Rankup_TypeLabel", text);
        Assert.Contains("ArenaEnemyWeaponViewer_Rankup_Info", text);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    static string AxamlPath()
    {
        // Walk up from the test bin dir to the repo root, then into the view.
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "FEBuilderGBA.Avalonia", "Views", "ArenaEnemyWeaponViewerView.axaml");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(AppContext.BaseDirectory, "ArenaEnemyWeaponViewerView.axaml");
    }
}
