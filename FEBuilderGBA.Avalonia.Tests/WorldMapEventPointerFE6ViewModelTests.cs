using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="WorldMapEventPointerFE6ViewModel"/> (issue #1181 —
/// port of WinForms <c>WorldMapEventPointerFE6Form</c>). Unlike the FE7 sibling these tests need
/// an FE6 ROM specifically: the FE6 list is built off the MAP SETTINGS table and resolves each
/// row's address through the FE6-only world-map-event PLIST, so the data only exists when
/// <c>RomInfo.version == 6</c>. <see cref="RomTestHelper.WithRom"/> loads FE6 (and no-ops when the
/// ROM is unavailable, e.g. local runs without a roms/ dir), restoring CoreState afterwards.
/// </summary>
[Collection("SharedState")]
public class WorldMapEventPointerFE6ViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WorldMapEventPointerFE6ViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void LoadList_OnFE6_ResolvesRowsIntoMapPointerTable()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            ROM rom = CoreState.ROM;
            Assert.Equal(6, rom.RomInfo.version);

            var vm = new WorldMapEventPointerFE6ViewModel();
            List<AddrResult> list = vm.LoadList();

            // GetListCount is the same code path — keep them in lockstep.
            Assert.Equal(list.Count, vm.GetListCount());

            uint mapBase = rom.p32(rom.RomInfo.map_map_pointer_pointer);
            foreach (AddrResult r in list)
            {
                // Every row address must be the map-pointer-table slot the FE6
                // world-map-event PLIST indexes: p32(map_map_pointer_pointer) +
                // plist*4 — so it is >= base, 4-byte aligned from base, and safe.
                Assert.True(r.addr >= mapBase);
                Assert.Equal(0u, (r.addr - mapBase) % 4);
                Assert.True(U.isSafetyOffset(r.addr, rom));
                Assert.False(string.IsNullOrEmpty(r.name));
            }
            _output.WriteLine($"FE6 world-map-event rows: {list.Count}");
        });
    }

    [Fact]
    public void LoadEntry_SetsCurrentAddrAndIsLoaded()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new WorldMapEventPointerFE6ViewModel();
            var list = vm.LoadList();
            if (list.Count == 0)
            {
                _output.WriteLine("FE6 ROM has no world-map-event maps — skipping entry check.");
                return;
            }

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(addr, vm.CurrentAddr);
        });
    }

    [Fact]
    public void LoadList_OnNonFE6Rom_DoesNotThrow()
    {
        // The VM must never throw on a non-FE6 ROM (the menu entry can be opened
        // on any ROM). It should simply return a list (typically empty, since the
        // FE6-only world-map-event PLIST is meaningful only on FE6).
        if (!_fixture.IsAvailable || CoreState.ROM?.RomInfo == null)
        {
            _output.WriteLine("ROM not available — skipping.");
            return;
        }

        // The fixture ROM must actually be non-FE6 for this to exercise the
        // intended path; if the only available ROM is FE6, skip rather than
        // mislabel an FE6 run as a non-FE6 assertion.
        if (CoreState.ROM.RomInfo.version == 6)
        {
            _output.WriteLine("Fixture ROM is FE6 — non-FE6 path not exercised, skipping.");
            return;
        }

        var vm = new WorldMapEventPointerFE6ViewModel();
        var list = vm.LoadList();
        Assert.NotNull(list);
        Assert.Equal(list.Count, vm.GetListCount());
    }
}
