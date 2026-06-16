using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="MapMiniMapTerrainImageViewModel"/> (issue #1180 —
/// port of WinForms <c>MapMiniMapTerrainImageForm</c>). The table at
/// <c>p32(RomInfo.map_minimap_tile_array_pointer)</c> holds one 4-byte tile-array pointer per
/// terrain type (count = <c>map_terrain_type_count</c>), with a named-preset combo from the
/// <c>map_minimap_tile_array_</c> resource.
/// </summary>
[Collection("SharedState")]
public class MapMiniMapTerrainImageViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MapMiniMapTerrainImageViewModelTests(RomFixture fixture, ITestOutputHelper output)
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
    public void LoadList_ReturnsEntries_FourBytesApart_CountMatchesTerrainTypes()
    {
        if (Skip()) return;

        var vm = new MapMiniMapTerrainImageViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal((int)CoreState.ROM.RomInfo.map_terrain_type_count, list.Count);
        Assert.Equal(vm.GetListCount(), list.Count);
        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + MapMiniMapTerrainImageViewModel.EntrySize, list[i].addr);
        }
    }

    [Fact]
    public void LoadEntry_ReadsPointer()
    {
        if (Skip()) return;

        var vm = new MapMiniMapTerrainImageViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u32(addr), vm.P0);
    }

    [Fact]
    public void OptionCombo_LoadsNamedPresets_AndRoundTripsMapping()
    {
        if (Skip()) return;

        var vm = new MapMiniMapTerrainImageViewModel();
        var labels = vm.OptionLabels;

        Assert.NotEmpty(labels);

        // The value→index→value mapping must be consistent.
        uint v0 = vm.GetOptionValue(0);
        Assert.Equal(0, vm.GetOptionIndex(v0));

        // Setting P0 to a known preset resolves its name; an unknown value resolves to empty.
        vm.P0 = v0;
        Assert.False(string.IsNullOrEmpty(vm.TileArrayName));
        vm.P0 = 0xDEADBEEF;
        Assert.Equal("", vm.TileArrayName);
    }

    [Fact]
    public void Write_RoundTripsPointer()
    {
        if (Skip()) return;

        var vm = new MapMiniMapTerrainImageViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.P0;

        try
        {
            vm.P0 = 0x08123456;
            vm.Write();

            var vm2 = new MapMiniMapTerrainImageViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x08123456u, vm2.P0);
        }
        finally
        {
            vm.P0 = orig;
            vm.Write();
        }
    }
}
