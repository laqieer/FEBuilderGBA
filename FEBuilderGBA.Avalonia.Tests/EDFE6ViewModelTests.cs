using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="EDFE6ViewModel"/> (issue #1198 —
/// port of WinForms <c>EDFE6Form</c>, the FE6 "Ending / after story" text editor).
///
/// The editor is FE6-ONLY: ed_3a is a 4×u16 text-ID table on FE6 but an ED/epilogue
/// table with a different schema on FE7/FE8. These tests therefore load a genuine FE6
/// ROM via <see cref="RomTestHelper.WithRom"/> (skipped gracefully when FE6 is absent)
/// and a separate test loads FE8U to prove the FE6 gate refuses to touch non-FE6 data.
/// </summary>
[Collection("SharedState")]
public class EDFE6ViewModelTests
{
    private readonly ITestOutputHelper _output;

    public EDFE6ViewModelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadList_OnFe6_ReturnsEntries_EightBytesApart()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EDFE6ViewModel();
            List<AddrResult> list = vm.LoadList();

            Assert.NotEmpty(list);
            Assert.True(list.Count <= (int)EDFE6ViewModel.MaxCount, "entry count must not exceed 0x42");

            for (int i = 1; i < list.Count; i++)
            {
                Assert.Equal(list[i - 1].addr + EDFE6ViewModel.EntrySize, list[i].addr);
            }
        });
    }

    [Fact]
    public void LoadEntry_OnFe6_PopulatesFourTextIdFields()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
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
        });
    }

    [Fact]
    public void WriteEntry_OnFe6_RoundTripsAllFourFields()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EDFE6ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            // Use entry 1 (Roy) — entry 0 is an empty placeholder.
            uint addr = list.Count > 1 ? list[1].addr : list[0].addr;
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
                // Restore the in-memory ROM (the on-disk file is never written).
                vm.Text0Id = orig0;
                vm.Text2Id = orig2;
                vm.Text4Id = orig4;
                vm.Text6Id = orig6;
                vm.WriteEntry();
            }
        });
    }

    [Fact]
    public void LoadEntry_InvalidSelection_ClearsFieldsAndStaysUnloaded()
    {
        RomTestHelper.WithRom("FE6", () =>
        {
            var vm = new EDFE6ViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);

            // First load a real entry so the fields are non-zero / loaded...
            vm.LoadEntry(list[0].addr);
            // ...then select an invalid (zero) address.
            vm.LoadEntry(0);

            Assert.False(vm.IsLoaded);
            Assert.Equal(0u, vm.Text0Id);
            Assert.Equal(0u, vm.Text2Id);
            Assert.Equal(0u, vm.Text4Id);
            Assert.Equal(0u, vm.Text6Id);
        });
    }

    [Fact]
    public void NonFe6Rom_ListEmpty_AndWriteRefused()
    {
        // The FE6 gate must prevent any list/read/write on a non-FE6 ROM, where
        // ed_3a holds a differently-structured ED/epilogue table.
        RomTestHelper.WithRom("FE8U", () =>
        {
            var vm = new EDFE6ViewModel();

            Assert.Empty(vm.LoadList());

            uint epilogueBase = CoreState.ROM.p32(CoreState.ROM.RomInfo.ed_3a_pointer);
            vm.LoadEntry(epilogueBase);
            Assert.False(vm.IsLoaded);          // invalid selection on non-FE6
            Assert.False(vm.WriteEntry());      // gate refuses mutation
        });
    }
}
