using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="UnitActionPointerViewModel"/> (issue #1198 wave-2 #1173 —
/// port of WinForms <c>UnitActionPointerForm</c>). The table is the per-unit action/behavior
/// function-pointer table at <c>p32(RomInfo.unitaction_function_pointer)</c>, 4 bytes per entry
/// (a single GBA pointer). The editor exposes that pointer (P0) for read/write plus a resolved
/// action-name label.
/// </summary>
[Collection("SharedState")]
public class UnitActionPointerViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public UnitActionPointerViewModelTests(RomFixture fixture, ITestOutputHelper output)
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
    public void LoadList_ReturnsEntries_FourBytesApart()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + 4, list[i].addr);
        }
        // Count helper agrees with the list it builds.
        Assert.Equal(list.Count, vm.GetListCount());
    }

    [Fact]
    public void LoadEntry_ReadsFunctionPointer_AndActionId()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        // P0 is the dereferenced function pointer (0x08 prefix stripped).
        Assert.Equal(CoreState.ROM.p32(addr), vm.P0);
        // First row is action id 1 (WinForms non-rework ids start at 1).
        Assert.Equal(1u, vm.ActionId);
    }

    [Fact]
    public void WriteEntry_RoundTripsFunctionPointer()
    {
        if (Skip()) return;

        var vm = new UnitActionPointerViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.P0;

        try
        {
            vm.P0 = 0x1234;
            vm.WriteEntry();

            var vm2 = new UnitActionPointerViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x1234u, vm2.P0);
        }
        finally
        {
            vm.P0 = orig;
            vm.WriteEntry();
        }
    }
}
