using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="UnitIncreaseHeightViewModel"/> (issue #1174 —
/// port of WinForms <c>UnitIncreaseHeightForm</c>). The "tall portrait" status-screen height table
/// is native to FE8 (the switch2 SUB/CMP pattern matches in vanilla), so the FE8U fixture has data.
/// Each 4-byte row holds the height marker (unit_increase_height_no / _yes).
/// </summary>
[Collection("SharedState")]
public class UnitIncreaseHeightViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public UnitIncreaseHeightViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries_FourBytesApart()
    {
        if (Skip()) return;

        var vm = new UnitIncreaseHeightViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(list.Count, vm.GetListCount());
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + 4, list[i].addr);
        }
    }

    [Fact]
    public void LoadEntry_ReadsHeightValue()
    {
        if (Skip()) return;

        var vm = new UnitIncreaseHeightViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u32(addr), vm.HeightValue);
    }

    [Fact]
    public void WriteEntry_RoundTripsHeightValue()
    {
        if (Skip()) return;

        var vm = new UnitIncreaseHeightViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.HeightValue;

        // Toggle between the two valid marker values.
        uint yes = CoreState.ROM.RomInfo.unit_increase_height_yes;
        uint no = CoreState.ROM.RomInfo.unit_increase_height_no;
        uint target = orig == yes ? no : yes;

        try
        {
            vm.HeightValue = target;
            vm.WriteEntry();

            var vm2 = new UnitIncreaseHeightViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(target, vm2.HeightValue);
        }
        finally
        {
            vm.HeightValue = orig;
            vm.WriteEntry();
        }
    }
}
