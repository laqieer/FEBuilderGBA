using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="EDFE6ViewModel"/> (issue #1198 —
/// port of WinForms <c>EDFE6Form</c>, the FE6 "Ending / after story" text editor).
/// The RomFixture loads an FE8U ROM; <c>ed_3a_pointer</c> is valid on FE8U too,
/// so the list + read/write round-trip exercises real ROM data.
/// </summary>
[Collection("SharedState")]
public class EDFE6ViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EDFE6ViewModelTests(RomFixture fixture, ITestOutputHelper output)
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
    public void LoadList_ReturnsEntries_EightBytesApart()
    {
        if (Skip()) return;

        var vm = new EDFE6ViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.True(list.Count <= (int)EDFE6ViewModel.MaxCount, "entry count must not exceed 0x42");

        for (int i = 1; i < list.Count; i++)
        {
            Assert.Equal(list[i - 1].addr + EDFE6ViewModel.EntrySize, list[i].addr);
        }
    }

    [Fact]
    public void LoadEntry_PopulatesFourTextIdFields()
    {
        if (Skip()) return;

        var vm = new EDFE6ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u16(addr + 0), vm.Text0Id);
        Assert.Equal(CoreState.ROM.u16(addr + 2), vm.Text2Id);
        Assert.Equal(CoreState.ROM.u16(addr + 4), vm.Text4Id);
        Assert.Equal(CoreState.ROM.u16(addr + 6), vm.Text6Id);
    }

    [Fact]
    public void WriteEntry_RoundTripsAllFourFields()
    {
        if (Skip()) return;

        var vm = new EDFE6ViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        uint orig0 = vm.Text0Id, orig2 = vm.Text2Id, orig4 = vm.Text4Id, orig6 = vm.Text6Id;
        try
        {
            vm.Text0Id = 0x0123;
            vm.Text2Id = 0x0045;
            vm.Text4Id = 0x0067;
            vm.Text6Id = 0x0089;
            Assert.True(vm.WriteEntry());

            // Re-read with a fresh VM to prove the bytes actually landed in ROM.
            var vm2 = new EDFE6ViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x0123u, vm2.Text0Id);
            Assert.Equal(0x0045u, vm2.Text2Id);
            Assert.Equal(0x0067u, vm2.Text4Id);
            Assert.Equal(0x0089u, vm2.Text6Id);
        }
        finally
        {
            // Restore the shared fixture ROM byte-for-byte.
            vm.Text0Id = orig0;
            vm.Text2Id = orig2;
            vm.Text4Id = orig4;
            vm.Text6Id = orig6;
            vm.WriteEntry();
        }
    }

    [Fact]
    public void WriteEntry_WithoutSelection_DoesNotMutate()
    {
        // No ROM dependency: a VM that never loaded an entry has CurrentAddr == 0.
        var vm = new EDFE6ViewModel();
        Assert.False(vm.WriteEntry());
    }
}
