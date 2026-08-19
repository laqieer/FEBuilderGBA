using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="EventBattleTalkFE6ViewModel"/> (issue #1188 —
/// port of WinForms <c>EventBattleTalkFE6Form</c>). FE6's battle-conversation struct is 12 bytes
/// (u8 attacker/defender, text at +4) — different from FE8's 16-byte layout — so these tests load
/// a genuine FE6 ROM via <see cref="RomTestHelper.WithRom"/>.
///
/// Issue #1438 adds the FE6-only secondary (boss generic-conversation) table at
/// <c>event_ballte_talk2_pointer</c>: 16-byte records, single triggering unit, event pointer at
/// +0x0C. These tests cover both the Main (12-byte) and Secondary (16-byte) tables.
/// </summary>
[Collection("SharedState")]
public class EventBattleTalkFE6ViewModelTests
{
    private readonly ITestOutputHelper _output;

    public EventBattleTalkFE6ViewModelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadList_OnFe6_ReturnsEntries_TwelveBytesApart()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            List<AddrResult> list = vm.LoadList();

            Assert.NotEmpty(list);
            Assert.Equal(list.Count, vm.GetListCount());
            for (int i = 1; i < list.Count; i++)
            {
                Assert.Equal(list[i - 1].addr + 12, list[i].addr);
            }
        });
    }

    [Fact]
    public void LoadEntry_OnFe6_ReadsStructFields()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(addr, vm.CurrentAddr);
            Assert.Equal(CoreState.ROM.u8(addr + 0), vm.AttackerUnit);
            Assert.Equal(CoreState.ROM.u8(addr + 1), vm.DefenderUnit);
            Assert.Equal(CoreState.ROM.u16(addr + 4), vm.Text);
            Assert.Equal(CoreState.ROM.u16(addr + 8), vm.AchievementFlag);
        });
    }

    [Fact]
    public void Write_OnFe6_RoundTripsAllFields()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            uint a0 = vm.AttackerUnit, d0 = vm.DefenderUnit, u2 = vm.Unknown02, u3 = vm.Unknown03,
                 t0 = vm.Text, u6 = vm.Unknown06, u7 = vm.Unknown07, f0 = vm.AchievementFlag,
                 ua = vm.Unknown0A, ub = vm.Unknown0B;

            try
            {
                vm.AttackerUnit = 0x11; vm.DefenderUnit = 0x22; vm.Unknown02 = 0x33; vm.Unknown03 = 0x44;
                vm.Text = 0x0567; vm.Unknown06 = 0x66; vm.Unknown07 = 0x77; vm.AchievementFlag = 0x0089;
                vm.Unknown0A = 0xAA; vm.Unknown0B = 0xBB;
                vm.Write();

                var vm2 = new EventBattleTalkFE6ViewModel();
                vm2.LoadEntry(addr);
                Assert.Equal(0x11u, vm2.AttackerUnit);
                Assert.Equal(0x22u, vm2.DefenderUnit);
                Assert.Equal(0x33u, vm2.Unknown02);
                Assert.Equal(0x44u, vm2.Unknown03);
                Assert.Equal(0x0567u, vm2.Text);
                Assert.Equal(0x66u, vm2.Unknown06);
                Assert.Equal(0x77u, vm2.Unknown07);
                Assert.Equal(0x0089u, vm2.AchievementFlag);
                Assert.Equal(0xAAu, vm2.Unknown0A);
                Assert.Equal(0xBBu, vm2.Unknown0B);
            }
            finally
            {
                vm.AttackerUnit = a0; vm.DefenderUnit = d0; vm.Unknown02 = u2; vm.Unknown03 = u3;
                vm.Text = t0; vm.Unknown06 = u6; vm.Unknown07 = u7; vm.AchievementFlag = f0;
                vm.Unknown0A = ua; vm.Unknown0B = ub;
                vm.Write();
            }
        });
    }

    // =====================================================================
    // Issue #1438 — Secondary (boss generic-conversation) 16-byte table.
    // =====================================================================

    [Fact]
    public void BlockSize_TogglesWithTable()
    {
        var vm = new EventBattleTalkFE6ViewModel();
        Assert.Equal(EventBattleTalkFE6ViewModel.BattleTalkTable.Main, vm.Table);
        Assert.False(vm.IsSecondaryTable);
        Assert.Equal(12u, vm.BlockSize);

        vm.Table = EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary;
        Assert.True(vm.IsSecondaryTable);
        Assert.Equal(16u, vm.BlockSize);

        vm.Table = EventBattleTalkFE6ViewModel.BattleTalkTable.Main;
        Assert.False(vm.IsSecondaryTable);
        Assert.Equal(12u, vm.BlockSize);
    }

    [Fact]
    public void ResolveBaseAddr_Secondary_UsesTalk2Pointer()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            ROM rom = CoreState.ROM;
            uint ptr2 = rom.RomInfo.event_ballte_talk2_pointer;
            // FE6 must expose the second battle-talk pointer.
            Assert.NotEqual(0u, ptr2);

            uint expected = rom.p32(ptr2);
            uint actual = EventBattleTalkFE6ViewModel.ResolveBaseAddr(
                rom, EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.Equal(expected, actual);
            Assert.True(U.isSafetyOffset(actual, rom));
        });
    }

    [Fact]
    public void LoadList_Secondary_ReturnsEntries_SixteenBytesApart()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            List<AddrResult> list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);

            Assert.True(vm.IsSecondaryTable);
            Assert.NotEmpty(list);
            for (int i = 1; i < list.Count; i++)
            {
                Assert.Equal(list[i - 1].addr + 16, list[i].addr);
            }
        });
    }

    [Fact]
    public void LoadEntry_Secondary_ReadsFlagAt08_AndEventPtrAt0C()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            Assert.True(vm.IsLoaded);
            Assert.True(vm.IsSecondaryTable);
            Assert.Equal(addr, vm.CurrentAddr);
            Assert.Equal(CoreState.ROM.u8(addr + 0), vm.AttackerUnit);
            Assert.Equal(CoreState.ROM.u16(addr + 4), vm.Text);
            Assert.Equal(CoreState.ROM.u16(addr + 8), vm.AchievementFlag);
            // Event pointer is a raw u32 at +0x0C (WinForms N_D12).
            Assert.Equal(CoreState.ROM.u32(addr + 12), vm.EventPointer);
        });
    }

    [Fact]
    public void Write_Secondary_RoundTripsEventPointer()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            uint a0 = vm.AttackerUnit, d0 = vm.DefenderUnit, u2 = vm.Unknown02, u3 = vm.Unknown03,
                 t0 = vm.Text, u6 = vm.Unknown06, u7 = vm.Unknown07, f0 = vm.AchievementFlag,
                 ua = vm.Unknown0A, ub = vm.Unknown0B, ep0 = vm.EventPointer;

            try
            {
                vm.AttackerUnit = 0x12; vm.DefenderUnit = 0x34; vm.Unknown02 = 0x56; vm.Unknown03 = 0x78;
                vm.Text = 0x09AB; vm.Unknown06 = 0xCD; vm.Unknown07 = 0xEF; vm.AchievementFlag = 0x0123;
                vm.Unknown0A = 0x45; vm.Unknown0B = 0x67;
                vm.EventPointer = 0x08ABCDEF; // raw GBA pointer value
                vm.Write();

                var vm2 = new EventBattleTalkFE6ViewModel();
                vm2.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
                vm2.LoadEntry(addr);
                Assert.True(vm2.IsSecondaryTable);
                Assert.Equal(0x12u, vm2.AttackerUnit);
                Assert.Equal(0x34u, vm2.DefenderUnit);
                Assert.Equal(0x56u, vm2.Unknown02);
                Assert.Equal(0x78u, vm2.Unknown03);
                Assert.Equal(0x09ABu, vm2.Text);
                Assert.Equal(0xCDu, vm2.Unknown06);
                Assert.Equal(0xEFu, vm2.Unknown07);
                Assert.Equal(0x0123u, vm2.AchievementFlag);
                Assert.Equal(0x45u, vm2.Unknown0A);
                Assert.Equal(0x67u, vm2.Unknown0B);
                Assert.Equal(0x08ABCDEFu, vm2.EventPointer);
            }
            finally
            {
                vm.AttackerUnit = a0; vm.DefenderUnit = d0; vm.Unknown02 = u2; vm.Unknown03 = u3;
                vm.Text = t0; vm.Unknown06 = u6; vm.Unknown07 = u7; vm.AchievementFlag = f0;
                vm.Unknown0A = ua; vm.Unknown0B = ub; vm.EventPointer = ep0;
                vm.Write();
            }
        });
    }

    [Fact]
    public void Write_Main_DoesNotTouchByteAt0C()
    {
        // Guardrail (Copilot plan review): the main 12-byte table must never
        // write the event pointer (offset +0x0C belongs to the NEXT main row).
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Main);
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            // Capture the bytes the next main row occupies (offset +0x0C..+0x0F).
            uint before0C = CoreState.ROM.u32(addr + 12);

            uint a0 = vm.AttackerUnit, d0 = vm.DefenderUnit, u2 = vm.Unknown02, u3 = vm.Unknown03,
                 t0 = vm.Text, u6 = vm.Unknown06, u7 = vm.Unknown07, f0 = vm.AchievementFlag,
                 ua = vm.Unknown0A, ub = vm.Unknown0B;
            try
            {
                vm.EventPointer = 0xDEADBEEF; // must be ignored on the main table
                vm.AchievementFlag = 0x00AB;
                vm.Write();
                Assert.Equal(before0C, CoreState.ROM.u32(addr + 12));
            }
            finally
            {
                vm.AttackerUnit = a0; vm.DefenderUnit = d0; vm.Unknown02 = u2; vm.Unknown03 = u3;
                vm.Text = t0; vm.Unknown06 = u6; vm.Unknown07 = u7; vm.AchievementFlag = f0;
                vm.Unknown0A = ua; vm.Unknown0B = ub;
                vm.Write();
            }
        });
    }


    // =====================================================================
    // Issue #2114 — FE6 triangle-attack direct quote table.
    // =====================================================================

    const int SyntheticFe6RomSize = 0x800000;
    const uint TriangleBase = 0x666528u;
    const uint MainBase = 0x700000u;
    const uint SecondaryBase = 0x710000u;

    static void WithSyntheticFE6(Action<ROM> body)
    {
        var prevRom = CoreState.ROM;
        try
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-fe6.gba", new byte[SyntheticFe6RomSize], "AFEJ01"));
            CoreState.ROM = rom;
            NameResolver.ClearCache();
            body(rom);
        }
        finally
        {
            CoreState.ROM = prevRom;
            NameResolver.ClearCache();
        }
    }

    static void SeedMainAndSecondary(ROM rom)
    {
        rom.write_p32(rom.RomInfo.event_ballte_talk_pointer, MainBase);
        rom.write_u16(MainBase + 0, 0x0201);
        rom.write_u16(MainBase + 12, 0x0403);
        rom.write_u16(MainBase + 24, 0x0000);

        rom.write_p32(rom.RomInfo.event_ballte_talk2_pointer, SecondaryBase);
        rom.write_u16(SecondaryBase + 0, 0x0605);
        rom.write_u32(SecondaryBase + 12, 0x08123456);
        rom.write_u16(SecondaryBase + 16, 0x0807);
        rom.write_u16(SecondaryBase + 32, 0x0000);
    }

    [Fact]
    public void TriangleAttack_BlockSizeAndBase_AreDirectFixedFE6Only()
    {
        WithSyntheticFE6(rom =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            Assert.Equal(0x666528u, rom.RomInfo.event_triangle_attack_quote_address);
            Assert.Equal(TriangleBase, EventBattleTalkFE6ViewModel.ResolveBaseAddr(
                rom, EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack));

            vm.Table = EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack;
            Assert.True(vm.IsTriangleAttackTable);
            Assert.False(vm.IsSecondaryTable);
            Assert.Equal(16u, vm.BlockSize);
        });
    }

    [Fact]
    public void LoadList_TriangleAttack_ReturnsExactlySixDirectRows_NoPointerOrTerminatorScan()
    {
        WithSyntheticFE6(rom =>
        {
            // Looks like a pointer if misread with p32(), and also begins with
            // u16==0 so a terminator scan would stop immediately.
            rom.write_u32(TriangleBase, 0x08720000);
            rom.write_u16(TriangleBase + 16, 0xFFFF);
            rom.Data[0x720000] = 0x22;

            var vm = new EventBattleTalkFE6ViewModel();
            List<AddrResult> list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack);

            Assert.True(vm.IsTriangleAttackTable);
            Assert.Equal(6, list.Count);
            Assert.Equal(6, vm.GetListCount());
            for (int i = 0; i < list.Count; i++)
                Assert.Equal(TriangleBase + (uint)(i * 16), list[i].addr);
        });
    }

    [Fact]
    public void LoadEntry_TriangleAttack_ReadsOnlyDirectWritableFields()
    {
        WithSyntheticFE6(rom =>
        {
            uint addr = TriangleBase + 2 * 16;
            rom.Data[addr + 0] = 0x12;
            rom.Data[addr + 1] = 0x07;
            rom.Data[addr + 2] = 0xA2;
            rom.Data[addr + 3] = 0xA3;
            rom.write_u16(addr + 4, 0x3456);
            rom.Data[addr + 6] = 0xA6;
            rom.Data[addr + 7] = 0xA7;
            rom.Data[addr + 8] = 0x5A;
            rom.write_u32(addr + 12, 0xDEADBEEF);

            var vm = new EventBattleTalkFE6ViewModel();
            vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack);
            vm.LoadEntry(addr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(addr, vm.CurrentAddr);
            Assert.Equal(0x12u, vm.AttackerUnit);
            Assert.Equal(0x07u, vm.DefenderUnit);
            Assert.Equal(0x3456u, vm.Text);
            Assert.Equal(0x5Au, vm.AchievementFlag);
            Assert.Equal(0u, vm.EventPointer);
            Assert.Contains("EventFlagByte", vm.GetDataReport().Keys);
            Assert.Contains("u8@0x08_EventFlagByte", vm.GetRawRomReport().Keys);
            Assert.DoesNotContain("u32@0x0C_EventPointer", vm.GetRawRomReport().Keys);
        });
    }

    [Fact]
    public void Write_TriangleAttack_PreservesReservedTailAndNeighborBytes()
    {
        WithSyntheticFE6(rom =>
        {
            uint addr = TriangleBase + 3 * 16;
            for (uint i = 0; i < 7 * 16; i++)
                rom.Data[TriangleBase + i] = (byte)(0x80 + (i & 0x3F));
            byte[] before = (byte[])rom.Data.Clone();

            var vm = new EventBattleTalkFE6ViewModel();
            vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack);
            vm.LoadEntry(addr);
            vm.AttackerUnit = 0x21;
            vm.DefenderUnit = 0x09;
            vm.Unknown02 = 0xEE;
            vm.Unknown03 = 0xEE;
            vm.Text = 0x4567;
            vm.Unknown06 = 0xEE;
            vm.Unknown07 = 0xEE;
            vm.AchievementFlag = 0xAB;
            vm.Unknown0A = 0xEE;
            vm.Unknown0B = 0xEE;
            vm.EventPointer = 0xDEADBEEF;
            vm.Write();

            Assert.Equal(0x21, rom.Data[addr + 0]);
            Assert.Equal(0x09, rom.Data[addr + 1]);
            Assert.Equal(0x4567u, rom.u16(addr + 4));
            Assert.Equal(0xAB, rom.Data[addr + 8]);

            foreach (uint off in new uint[] { 2, 3, 6, 7, 9, 10, 11, 12, 13, 14, 15 })
                Assert.Equal(before[addr + off], rom.Data[addr + off]);
            for (uint i = 0; i < 7 * 16; i++)
            {
                uint absolute = TriangleBase + i;
                if (absolute == addr + 0 || absolute == addr + 1 || absolute == addr + 4 ||
                    absolute == addr + 5 || absolute == addr + 8)
                    continue;
                Assert.Equal(before[absolute], rom.Data[absolute]);
            }
        });
    }

    [Fact]
    public void TriangleAttack_ShortRomFailsClosed()
    {
        WithSyntheticFE6(rom =>
        {
            rom.SwapNewROMDataDirect(new byte[(int)(TriangleBase + 5 * 16 + 15)]);
            var vm = new EventBattleTalkFE6ViewModel();

            Assert.Equal(0u, EventBattleTalkFE6ViewModel.ResolveBaseAddr(
                rom, EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack));
            Assert.Empty(vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.TriangleAttack));
            vm.LoadEntry(TriangleBase);
            Assert.False(vm.IsLoaded);
        });
    }

    [Fact]
    public void Synthetic_MainAndSecondary_RegressionsRemainPointerAndTerminatorBased()
    {
        WithSyntheticFE6(rom =>
        {
            SeedMainAndSecondary(rom);
            var vm = new EventBattleTalkFE6ViewModel();

            var main = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Main);
            Assert.Equal(2, main.Count);
            Assert.Equal(12u, main[1].addr - main[0].addr);
            uint mainNeighborBefore = rom.u32(MainBase + 12);
            vm.LoadEntry(MainBase);
            vm.EventPointer = 0xDEADBEEF;
            vm.AchievementFlag = 0x1234;
            vm.Write();
            Assert.Equal(mainNeighborBefore, rom.u32(MainBase + 12));

            var secondary = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.Equal(2, secondary.Count);
            Assert.Equal(16u, secondary[1].addr - secondary[0].addr);
            vm.LoadEntry(SecondaryBase);
            vm.EventPointer = 0x08ABCDEF;
            vm.Write();
            Assert.Equal(0x08ABCDEFu, rom.u32(SecondaryBase + 12));
        });
    }

    [Fact]
    public void ClearEntry_ResetsLoadedState()
    {
        // Guardrail (Copilot PR review): switching to an empty/missing table must
        // not leave a writable stale selection behind. ClearEntry zeroes the
        // loaded-entry state so the Write button (gated on IsLoaded) cannot commit
        // the previous table's row under the new schema.
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();
            var list = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.NotEmpty(list);
            vm.LoadEntry(list[0].addr);
            Assert.True(vm.IsLoaded);
            Assert.NotEqual(0u, vm.CurrentAddr);

            vm.ClearEntry();

            Assert.False(vm.IsLoaded);
            Assert.Equal(0u, vm.CurrentAddr);
            Assert.Equal(0u, vm.AttackerUnit);
            Assert.Equal(0u, vm.DefenderUnit);
            Assert.Equal(0u, vm.Text);
            Assert.Equal(0u, vm.AchievementFlag);
            Assert.Equal(0u, vm.EventPointer);
        });
    }

    [Fact]
    public void Reports_IncludeEventPointer_OnlyForSecondary()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EventBattleTalkFE6ViewModel();

            // Main table: no EventPointer key.
            var mainList = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Main);
            Assert.NotEmpty(mainList);
            vm.LoadEntry(mainList[0].addr);
            Assert.DoesNotContain("EventPointer", vm.GetDataReport().Keys);
            Assert.DoesNotContain("u32@0x0C_EventPointer", vm.GetRawRomReport().Keys);

            // Secondary table: EventPointer present.
            var secList = vm.LoadList(EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            Assert.NotEmpty(secList);
            vm.LoadEntry(secList[0].addr);
            Assert.Contains("EventPointer", vm.GetDataReport().Keys);
            Assert.Contains("u32@0x0C_EventPointer", vm.GetRawRomReport().Keys);
        });
    }
}
