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
