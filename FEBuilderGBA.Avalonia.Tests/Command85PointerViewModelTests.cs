using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="Command85PointerViewModel"/> (issue #1186 —
/// port of WinForms <c>Command85PointerForm</c>). The table at
/// <c>p32(RomInfo.command_85_pointer_table_pointer)</c> holds 4-byte function pointers, one per
/// battle-animation command code starting at <c>0x19</c>.
/// </summary>
[Collection("SharedState")]
public class Command85PointerViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Command85PointerViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries_FourBytesApart_FirstIdIs0x19()
    {
        if (Skip()) return;

        var vm = new Command85PointerViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(Command85PointerViewModel.FirstCommandId, list[0].tag);
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + Command85PointerViewModel.EntrySize, list[i].addr);
        }
        Assert.Equal(list.Count, vm.GetListCount());
    }

    [Fact]
    public void LoadEntry_ReadsPointer_AndCommandId()
    {
        if (Skip()) return;

        var vm = new Command85PointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u32(addr), vm.PointerValue);
        Assert.Equal(Command85PointerViewModel.FirstCommandId, vm.CommandId);
    }

    [Fact]
    public void Write_RoundTripsPointer()
    {
        if (Skip()) return;

        var vm = new Command85PointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.PointerValue;

        try
        {
            vm.PointerValue = 0x08123456;
            vm.Write();

            var vm2 = new Command85PointerViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x08123456u, vm2.PointerValue);
        }
        finally
        {
            vm.PointerValue = orig;
            vm.Write();
        }
    }
}
