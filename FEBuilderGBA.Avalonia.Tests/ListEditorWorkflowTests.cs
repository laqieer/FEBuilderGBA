using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests verifying list editor iteration and boundary behavior for
    /// UnitEditor, ClassEditor, ItemEditor, and MapSettingFE6 ViewModels.
    /// All tests are read-only and skip gracefully when ROMs are unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class ListEditorWorkflowTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ListEditorWorkflowTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // =================================================================
        // Full List Iteration (4 tests)
        // =================================================================

        [Fact]
        public void UnitEditor_IterateAllUnits_NoCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 0, "Unit list should not be empty");
            _output.WriteLine($"Iterating {list.Count} units (ROM version: {_fixture.Version})");

            for (int i = 0; i < list.Count; i++)
            {
                vm.LoadUnit(list[i].addr);
                Assert.NotEqual(0u, vm.CurrentAddr);
            }

            _output.WriteLine($"Successfully iterated all {list.Count} units");
        }

        [Fact]
        public void ClassEditor_IterateAllClasses_NoCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            Assert.True(list.Count > 0, "Class list should not be empty");
            _output.WriteLine($"Iterating {list.Count} classes (ROM version: {_fixture.Version})");

            for (int i = 0; i < list.Count; i++)
            {
                vm.LoadClass(list[i].addr);
                Assert.NotEqual(0u, vm.CurrentAddr);
            }

            _output.WriteLine($"Successfully iterated all {list.Count} classes");
        }

        [Fact]
        public void ItemEditor_IterateAllItems_NoCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            Assert.True(list.Count > 0, "Item list should not be empty");
            _output.WriteLine($"Iterating {list.Count} items (ROM version: {_fixture.Version})");

            for (int i = 0; i < list.Count; i++)
            {
                vm.LoadItem(list[i].addr);
                Assert.NotEqual(0u, vm.CurrentAddr);
            }

            _output.WriteLine($"Successfully iterated all {list.Count} items");
        }

        [Fact]
        public void MapSettingFE6_IterateAllEntries_NoCrash()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return; // FE6 only

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            Assert.True(list.Count > 0, "Map setting list should not be empty for FE6");
            _output.WriteLine($"Iterating {list.Count} FE6 map settings");

            for (int i = 0; i < list.Count; i++)
            {
                vm.LoadEntry(list[i].addr);
                Assert.NotEqual(0u, vm.CurrentAddr);
            }

            _output.WriteLine($"Successfully iterated all {list.Count} FE6 map settings");
        }

        // =================================================================
        // Sequential State Isolation (4 tests)
        // =================================================================

        [Fact]
        public void UnitEditor_SequentialLoads_AddressChanges()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 3) return;

            vm.LoadUnit(list[1].addr);
            uint addr1 = vm.CurrentAddr;

            vm.LoadUnit(list[2].addr);
            uint addr2 = vm.CurrentAddr;

            Assert.NotEqual(addr1, addr2);
            Assert.Equal(list[1].addr, addr1);
            Assert.Equal(list[2].addr, addr2);
            _output.WriteLine($"Entry[1] addr=0x{addr1:X}, Entry[2] addr=0x{addr2:X}");
        }

        [Fact]
        public void UnitEditor_SequentialLoads_IsDirtyFalse()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 3) return;

            vm.LoadUnit(list[1].addr);
            Assert.False(vm.IsDirty, "IsDirty should be false after loading entry[1]");

            vm.LoadUnit(list[2].addr);
            Assert.False(vm.IsDirty, "IsDirty should be false after loading entry[2]");

            vm.LoadUnit(list[0].addr);
            Assert.False(vm.IsDirty, "IsDirty should be false after loading entry[0]");
        }

        [Fact]
        public void ClassEditor_SequentialLoads_NameChanges()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 6) return;

            vm.LoadClass(list[1].addr);
            string name1 = vm.Name;

            vm.LoadClass(list[5].addr);
            string name5 = vm.Name;

            // Classes 1 and 5 should have different names in any FE ROM
            Assert.NotEqual(name1, name5);
            _output.WriteLine($"Class[1] name='{name1}', Class[5] name='{name5}'");
        }

        [Fact]
        public void ItemEditor_SequentialLoads_PropertiesUpdate()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 3) return;

            vm.LoadItem(list[1].addr);
            uint nameId1 = vm.NameId;
            uint addr1 = vm.CurrentAddr;

            vm.LoadItem(list[2].addr);
            uint nameId2 = vm.NameId;
            uint addr2 = vm.CurrentAddr;

            // At minimum, the address must differ
            Assert.NotEqual(addr1, addr2);
            // For distinct items, at least one field should differ (nameId, might, etc.)
            bool anyDiffers = (nameId1 != nameId2) ||
                              (vm.Might != 0) || (vm.Uses != 0);
            Assert.True(anyDiffers, "At least one property should differ between items 1 and 2");
        }

        // =================================================================
        // Boundary Tests (4 tests)
        // =================================================================

        [Fact]
        public void UnitEditor_LoadFirstEntry_DoesNotCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 0, "Unit list should not be empty");

            var ex = Record.Exception(() => vm.LoadUnit(list[0].addr));
            Assert.Null(ex);
            Assert.NotEqual(0u, vm.CurrentAddr);
        }

        [Fact]
        public void UnitEditor_LoadLastEntry_DoesNotCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 0, "Unit list should not be empty");

            int lastIdx = list.Count - 1;
            var ex = Record.Exception(() => vm.LoadUnit(list[lastIdx].addr));
            Assert.Null(ex);
            Assert.NotEqual(0u, vm.CurrentAddr);
            _output.WriteLine($"Last unit index={lastIdx}, addr=0x{list[lastIdx].addr:X}");
        }

        [Fact]
        public void ClassEditor_LoadFirstEntry_DoesNotCrash()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            Assert.True(list.Count > 0, "Class list should not be empty");

            var ex = Record.Exception(() => vm.LoadClass(list[0].addr));
            Assert.Null(ex);
            Assert.NotEqual(0u, vm.CurrentAddr);
        }

        [Fact]
        public void ItemEditor_LoadLastEntry_HasValidNameId()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            Assert.True(list.Count > 0, "Item list should not be empty");

            int lastIdx = list.Count - 1;
            vm.LoadItem(list[lastIdx].addr);

            // The last item entry should still have a non-null name
            Assert.NotNull(vm.Name);
            _output.WriteLine($"Last item index={lastIdx}, name='{vm.Name}', NameId=0x{vm.NameId:X}");
        }

        // =================================================================
        // Data Report Consistency (4 tests)
        // =================================================================

        [Fact]
        public void UnitEditor_DataReport_ConsistentAcrossLoads()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            // Load the same unit twice and compare reports
            vm.LoadUnit(list[1].addr);
            var report1 = vm.GetDataReport();

            vm.LoadUnit(list[1].addr);
            var report2 = vm.GetDataReport();

            Assert.Equal(report1.Count, report2.Count);
            foreach (var key in report1.Keys)
            {
                Assert.True(report2.ContainsKey(key), $"Key '{key}' missing in second report");
                Assert.Equal(report1[key], report2[key]);
            }
        }

        [Fact]
        public void ClassEditor_DataReport_ContainsAllExpectedKeys()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);
            var report = vm.GetDataReport();

            Assert.True(report.ContainsKey("addr"), "Report should contain 'addr'");
            Assert.True(report.ContainsKey("W0_NameId"), "Report should contain 'W0_NameId'");
            Assert.True(report.ContainsKey("B17_Mov"), "Report should contain 'B17_Mov'");
            Assert.True(report.ContainsKey("B40_Ability1"), "Report should contain 'B40_Ability1'");
            _output.WriteLine($"Class data report has {report.Count} keys");
        }

        [Fact]
        public void ItemEditor_RawRomReport_MatchesDataReport()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            var dataReport = vm.GetDataReport();
            var rawReport = vm.GetRawRomReport();

            Assert.NotNull(dataReport);
            Assert.NotNull(rawReport);
            Assert.True(rawReport.Count > 0, "Raw ROM report should have entries");

            // u16@0x00 from raw should match W0_NameId from data report
            Assert.Equal(dataReport["W0_NameId"], rawReport["u16@0x00"]);
        }

        [Fact]
        public void UnitEditor_DataReport_DiffersForDifferentEntries()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 3) return;

            vm.LoadUnit(list[1].addr);
            var report1 = vm.GetDataReport();

            vm.LoadUnit(list[2].addr);
            var report2 = vm.GetDataReport();

            // The addr key must always differ for different entries
            Assert.NotEqual(report1["addr"], report2["addr"]);

            // At least one non-addr key should also differ
            bool anyValueDiffers = report1.Keys
                .Where(k => k != "addr")
                .Any(k => report2.ContainsKey(k) && report1[k] != report2[k]);
            Assert.True(anyValueDiffers,
                "At least one data field should differ between entries 1 and 2");
        }

        // =================================================================
        // Guard and Safety Tests (4 tests)
        // =================================================================

        [Fact]
        public void UnitEditor_ListCount_MatchesListLength()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            int count = vm.GetListCount();

            Assert.Equal(list.Count, count);
            _output.WriteLine($"UnitEditor list count: {count}");
        }

        [Fact]
        public void ClassEditor_ListCount_MatchesListLength()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            int count = vm.GetListCount();

            Assert.Equal(list.Count, count);
            _output.WriteLine($"ClassEditor list count: {count}");
        }

        [Fact]
        public void ItemEditor_AllAddresses_WithinRomBounds()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            Assert.True(list.Count > 0, "Item list should not be empty");

            uint romLength = (uint)CoreState.ROM.Data.Length;
            _output.WriteLine($"ROM size: 0x{romLength:X}, checking {list.Count} item addresses");

            foreach (var entry in list)
            {
                Assert.True(entry.addr < romLength,
                    $"Item address 0x{entry.addr:X} exceeds ROM size 0x{romLength:X}");
            }
        }

        [Fact]
        public void UnitEditor_AllEntries_HaveNonEmptyNames()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 0, "Unit list should not be empty");

            int emptyCount = 0;
            foreach (var entry in list)
            {
                Assert.NotNull(entry.name);
                if (!string.IsNullOrEmpty(entry.name))
                    continue;
                emptyCount++;
            }

            // Every entry.name from LoadUnitList() should be non-null and non-empty
            Assert.Equal(0, emptyCount);
            _output.WriteLine($"All {list.Count} unit entries have non-empty names");
        }
    }
}
