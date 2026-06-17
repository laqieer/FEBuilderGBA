using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="EventBattleTalkViewModel"/> (issue #1187 —
/// port of WinForms <c>EventBattleTalkForm</c>, the FE8 battle-conversation editor). The table at
/// <c>p32(event_ballte_talk_pointer)</c> holds 16-byte records (attacker/defender units, map,
/// achievement flag, text id, event pointer).
/// </summary>
[Collection("SharedState")]
public class EventBattleTalkViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EventBattleTalkViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable || CoreState.ROM?.RomInfo == null
            || CoreState.ROM.RomInfo.event_ballte_talk_pointer == 0)
        {
            _output.WriteLine("ROM/battle-talk pointer not available — skipping.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries_SixteenBytesApart()
    {
        if (Skip()) return;

        var vm = new EventBattleTalkViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(list.Count, vm.GetListCount());
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + 16, list[i].addr);
        }
    }

    [Fact]
    public void LoadEntry_ReadsStructFields()
    {
        if (Skip()) return;

        var vm = new EventBattleTalkViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u16(addr + 0), vm.AttackerUnit);
        Assert.Equal(CoreState.ROM.u16(addr + 2), vm.DefenderUnit);
        Assert.Equal(CoreState.ROM.u8(addr + 4), vm.Map);
        Assert.Equal(CoreState.ROM.u16(addr + 8), vm.Text);
        Assert.Equal(CoreState.ROM.u32(addr + 12), vm.EventPointer);
    }

    [Fact]
    public void Write_RoundTripsAllFields()
    {
        if (Skip()) return;

        var vm = new EventBattleTalkViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        uint a0 = vm.AttackerUnit, d0 = vm.DefenderUnit, m0 = vm.Map, u5 = vm.Unknown05,
             f0 = vm.AchievementFlag, t0 = vm.Text, ua = vm.Unknown0A, ub = vm.Unknown0B, ep = vm.EventPointer;

        try
        {
            vm.AttackerUnit = 0x11; vm.DefenderUnit = 0x22; vm.Map = 0x33; vm.Unknown05 = 0x44;
            vm.AchievementFlag = 0x55; vm.Text = 0x66; vm.Unknown0A = 0x77; vm.Unknown0B = 0x88;
            vm.EventPointer = 0x08123456;
            vm.Write();

            var vm2 = new EventBattleTalkViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x11u, vm2.AttackerUnit);
            Assert.Equal(0x22u, vm2.DefenderUnit);
            Assert.Equal(0x33u, vm2.Map);
            Assert.Equal(0x44u, vm2.Unknown05);
            Assert.Equal(0x55u, vm2.AchievementFlag);
            Assert.Equal(0x66u, vm2.Text);
            Assert.Equal(0x77u, vm2.Unknown0A);
            Assert.Equal(0x88u, vm2.Unknown0B);
            Assert.Equal(0x08123456u, vm2.EventPointer);
        }
        finally
        {
            vm.AttackerUnit = a0; vm.DefenderUnit = d0; vm.Map = m0; vm.Unknown05 = u5;
            vm.AchievementFlag = f0; vm.Text = t0; vm.Unknown0A = ua; vm.Unknown0B = ub; vm.EventPointer = ep;
            vm.Write();
        }
    }
}
