using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="EventFinalSerifFE7ViewModel"/> (issue #1189 —
/// port of WinForms <c>EventFinalSerifFE7Form</c>). The final-chapter per-character dialogue table
/// (<c>event_final_serif</c>, FE7-only) holds 8-byte records: Unit id at +0, Text id at +4. Loaded
/// on a real FE7 ROM (the FE8U fixture has no such table).
/// </summary>
[Collection("SharedState")]
public class EventFinalSerifFE7ViewModelTests
{
    private readonly ITestOutputHelper _output;

    public EventFinalSerifFE7ViewModelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadList_OnFe7_ReturnsEntries_EightBytesApart()
    {
        RomTestHelper.WithRom("FE7U", () =>
        {
            var vm = new EventFinalSerifFE7ViewModel();
            List<AddrResult> list = vm.LoadList();

            Assert.NotEmpty(list);
            Assert.Equal(list.Count, vm.GetListCount());
            for (int i = 1; i < list.Count; i++)
            {
                Assert.Equal(list[i - 1].addr + 8, list[i].addr);
            }
        });
    }

    [Fact]
    public void LoadEntry_OnFe7_ReadsUnitAndText()
    {
        RomTestHelper.WithRom("FE7U", () =>
        {
            var vm = new EventFinalSerifFE7ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(addr, vm.CurrentAddr);
            Assert.Equal(CoreState.ROM.u32(addr + 0), vm.Unit);
            Assert.Equal(CoreState.ROM.u32(addr + 4), vm.TextID);
        });
    }

    [Fact]
    public void Write_OnFe7_RoundTripsUnitAndText()
    {
        RomTestHelper.WithRom("FE7U", () =>
        {
            var vm = new EventFinalSerifFE7ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            uint addr = list[0].addr;
            vm.LoadEntry(addr);
            uint origUnit = vm.Unit, origText = vm.TextID;

            try
            {
                vm.Unit = 0x12;
                vm.TextID = 0x0567;
                vm.Write();

                var vm2 = new EventFinalSerifFE7ViewModel();
                vm2.LoadEntry(addr);
                Assert.Equal(0x12u, vm2.Unit);
                Assert.Equal(0x0567u, vm2.TextID);
            }
            finally
            {
                vm.Unit = origUnit;
                vm.TextID = origText;
                vm.Write();
            }
        });
    }
}
