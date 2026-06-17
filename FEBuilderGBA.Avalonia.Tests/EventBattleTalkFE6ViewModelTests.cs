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
}
