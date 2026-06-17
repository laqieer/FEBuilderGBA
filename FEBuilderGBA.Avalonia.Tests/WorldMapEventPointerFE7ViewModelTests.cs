using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="WorldMapEventPointerFE7ViewModel"/> (issue #1182 —
/// port of WinForms <c>WorldMapEventPointerFE7Form</c>). The stage-select event table
/// (<c>p32(worldmap_event_on_stageselect_pointer)</c>, 4-byte event pointers) and the two global
/// ending-event pointers share the same schema on FE7 and FE8, so the FE8U fixture exercises the
/// read/write mechanics; the GUI screenshot uses FE7U.
/// </summary>
[Collection("SharedState")]
public class WorldMapEventPointerFE7ViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WorldMapEventPointerFE7ViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable || CoreState.ROM?.RomInfo == null
            || CoreState.ROM.RomInfo.worldmap_event_on_stageselect_pointer == 0)
        {
            _output.WriteLine("ROM/world-map-event pointer not available — skipping.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries_FourBytesApart()
    {
        if (Skip()) return;

        var vm = new WorldMapEventPointerFE7ViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(list.Count, vm.GetListCount());
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + WorldMapEventPointerFE7ViewModel.EntrySize, list[i].addr);
        }
    }

    [Fact]
    public void LoadEntry_ReadsEventPointer()
    {
        if (Skip()) return;

        var vm = new WorldMapEventPointerFE7ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.p32(addr), vm.EventPointer);
    }

    [Fact]
    public void LoadGlobalEvents_ReadsEndingPointers()
    {
        if (Skip()) return;

        var vm = new WorldMapEventPointerFE7ViewModel();
        vm.LoadGlobalEvents();

        Assert.Equal(CoreState.ROM.p32(CoreState.ROM.RomInfo.ending1_event_pointer), vm.Ending1Event);
        Assert.Equal(CoreState.ROM.p32(CoreState.ROM.RomInfo.ending2_event_pointer), vm.Ending2Event);
    }

    [Fact]
    public void WriteEntry_RoundTripsEventPointer()
    {
        if (Skip()) return;

        var vm = new WorldMapEventPointerFE7ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        // Use a non-row-0 entry (row 0 is the always-included header slot).
        uint addr = list.Count > 1 ? list[1].addr : list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.EventPointer;

        try
        {
            vm.EventPointer = 0x123456;
            Assert.True(vm.WriteEntry());

            var vm2 = new WorldMapEventPointerFE7ViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x123456u, vm2.EventPointer);
        }
        finally
        {
            vm.EventPointer = orig;
            vm.WriteEntry();
        }
    }

    [Fact]
    public void WriteGlobalEvents_RoundTripsEndings()
    {
        if (Skip()) return;

        var vm = new WorldMapEventPointerFE7ViewModel();
        vm.LoadGlobalEvents();
        uint orig1 = vm.Ending1Event, orig2 = vm.Ending2Event;

        try
        {
            vm.Ending1Event = 0x111111;
            vm.Ending2Event = 0x222222;
            Assert.True(vm.WriteGlobalEvents());

            var vm2 = new WorldMapEventPointerFE7ViewModel();
            vm2.LoadGlobalEvents();
            Assert.Equal(0x111111u, vm2.Ending1Event);
            Assert.Equal(0x222222u, vm2.Ending2Event);
        }
        finally
        {
            vm.Ending1Event = orig1;
            vm.Ending2Event = orig2;
            vm.WriteGlobalEvents();
        }
    }
}
