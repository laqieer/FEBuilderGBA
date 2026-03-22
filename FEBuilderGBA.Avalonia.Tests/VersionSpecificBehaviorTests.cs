using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests verifying ROM version-specific behavior across FE6, FE7J/U, and FE8J/U.
    /// Tests are flexible: they adapt assertions based on the detected ROM version
    /// and skip gracefully when the required version is not available.
    /// </summary>
    [Collection("SharedState")]
    public class VersionSpecificBehaviorTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public VersionSpecificBehaviorTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // =================================================================
        // FE6 Guard Tests (1-4)
        // =================================================================

        [Fact]
        public void MapSettingViewModel_FE6Guard_BlocksLoad()
        {
            // On FE6 ROM, LoadMapSetting should set CurrentAddr = 0 (guard blocks load)
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return;

            _output.WriteLine($"ROM version: FE6 (version={CoreState.ROM.RomInfo.version})");

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 1) return;

            vm.LoadMapSetting(list[0].addr);

            Assert.Equal(0u, vm.CurrentAddr);
        }

        [Fact]
        public void MapSettingViewModel_FE6Guard_AllowsNonFE6()
        {
            // On FE7/FE8 ROM, LoadMapSetting should populate fields normally
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version == 6) return;

            _output.WriteLine($"ROM version: {_fixture.Version} (version={CoreState.ROM.RomInfo.version})");

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) return;

            vm.LoadMapSetting(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.DataSize > 0, "DataSize should be populated for non-FE6 ROM");
        }

        [Fact]
        public void MapSettingFE6ViewModel_LoadEntry_WorksOnFE6()
        {
            // On FE6, the FE6-specific ViewModel should load entries correctly
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return;

            _output.WriteLine($"ROM version: FE6 — testing MapSettingFE6ViewModel.LoadEntry");

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) return;

            vm.LoadEntry(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.IsLoaded, "IsLoaded should be true after LoadEntry on FE6");
            Assert.True(vm.DataSize > 0, "DataSize should be set after load");
        }

        [Fact]
        public void UnitEditorViewModel_IsFE6_MatchesVersion()
        {
            if (!_fixture.IsAvailable) return;

            _output.WriteLine($"ROM version: {_fixture.Version} (version={CoreState.ROM.RomInfo.version})");

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 1) return;

            vm.LoadUnit(list[0].addr);

            bool expectedFE6 = CoreState.ROM.RomInfo.version == 6;
            Assert.Equal(expectedFE6, vm.IsFE6);
        }

        // =================================================================
        // Struct Size Tests (5-8)
        // =================================================================

        [Fact]
        public void UnitDataSize_MatchesVersion()
        {
            if (!_fixture.IsAvailable) return;

            var ri = CoreState.ROM.RomInfo;
            _output.WriteLine($"ROM version: {_fixture.Version}, unit_datasize={ri.unit_datasize}");

            Assert.True(ri.unit_datasize > 0, "unit_datasize should be positive");

            if (ri.version == 6)
            {
                // FE6: unit_datasize = 48
                Assert.Equal(48u, ri.unit_datasize);
            }
            else
            {
                // FE7/FE8: unit_datasize = 52
                Assert.Equal(52u, ri.unit_datasize);
            }
        }

        [Fact]
        public void ClassDataSize_IsConsistent()
        {
            if (!_fixture.IsAvailable) return;

            var ri = CoreState.ROM.RomInfo;
            _output.WriteLine($"ROM version: {_fixture.Version}, class_datasize={ri.class_datasize}");

            Assert.True(ri.class_datasize > 0, "class_datasize should be positive");

            if (ri.version == 6)
            {
                // FE6: class_datasize = 72
                Assert.Equal(72u, ri.class_datasize);
            }
            else
            {
                // FE7/FE8: class_datasize = 84
                Assert.Equal(84u, ri.class_datasize);
            }
        }

        [Fact]
        public void ItemDataSize_MatchesVersion()
        {
            if (!_fixture.IsAvailable) return;

            var ri = CoreState.ROM.RomInfo;
            _output.WriteLine($"ROM version: {_fixture.Version}, item_datasize={ri.item_datasize}");

            Assert.True(ri.item_datasize > 0, "item_datasize should be positive");

            if (ri.version == 6)
            {
                // FE6: item_datasize = 32
                Assert.Equal(32u, ri.item_datasize);
            }
            else
            {
                // FE7/FE8: item_datasize = 36
                Assert.Equal(36u, ri.item_datasize);
            }
        }

        [Fact]
        public void MapSettingDataSize_MatchesVersion()
        {
            if (!_fixture.IsAvailable) return;

            var ri = CoreState.ROM.RomInfo;
            _output.WriteLine($"ROM version: {_fixture.Version}, map_setting_datasize={ri.map_setting_datasize}");

            Assert.True(ri.map_setting_datasize > 0, "map_setting_datasize should be positive");

            if (ri.version == 6)
            {
                // FE6: 68 or 72 depending on ROM variant
                Assert.True(ri.map_setting_datasize >= 68 && ri.map_setting_datasize <= 72,
                    $"FE6 map_setting_datasize should be 68-72, got {ri.map_setting_datasize}");
            }
            else
            {
                // FE7/FE8: 148 or 152
                Assert.True(ri.map_setting_datasize >= 148,
                    $"FE7/FE8 map_setting_datasize should be >= 148, got {ri.map_setting_datasize}");
            }
        }

        // =================================================================
        // Field Offset Tests (9-12)
        // =================================================================

        [Fact]
        public void UnitEditor_NameId_AtOffset0()
        {
            // Verify NameId comes from ROM u16 at offset 0 of the unit entry
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            uint addr = list[1].addr;
            vm.LoadUnit(addr);

            uint romVal = CoreState.ROM.u16(addr + 0);
            _output.WriteLine($"Unit addr=0x{addr:X}, NameId={vm.NameId}, ROM u16@0=0x{romVal:X}");

            Assert.Equal(romVal, vm.NameId);
        }

        [Fact]
        public void ClassEditor_NameId_AtOffset0()
        {
            // Verify class NameId comes from ROM u16 at offset 0
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            uint addr = list[1].addr;
            vm.LoadClass(addr);

            uint romVal = CoreState.ROM.u16(addr + 0);
            _output.WriteLine($"Class addr=0x{addr:X}, NameId={vm.NameId}, ROM u16@0=0x{romVal:X}");

            Assert.Equal(romVal, vm.NameId);
        }

        [Fact]
        public void ItemEditor_NameId_AtOffset0()
        {
            // Verify item NameId comes from ROM u16 at offset 0
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            uint addr = list[1].addr;
            vm.LoadItem(addr);

            uint romVal = CoreState.ROM.u16(addr + 0);
            _output.WriteLine($"Item addr=0x{addr:X}, NameId={vm.NameId}, ROM u16@0=0x{romVal:X}");

            Assert.Equal(romVal, vm.NameId);
        }

        [Fact]
        public void UnitEditor_ClassId_AtOffset5()
        {
            // Verify ClassId comes from ROM u8 at offset 5 of the unit entry
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            uint addr = list[1].addr;
            vm.LoadUnit(addr);

            uint romVal = CoreState.ROM.u8(addr + 5);
            _output.WriteLine($"Unit addr=0x{addr:X}, ClassId={vm.ClassId}, ROM u8@5=0x{romVal:X}");

            Assert.Equal(romVal, vm.ClassId);
        }

        // =================================================================
        // Extended Fields / Version Differences (13-16)
        // =================================================================

        [Fact]
        public void ClassEditor_TerrainPtrs_ZeroForFE6()
        {
            // FE6 classes have no terrain avoid/def/res pointers
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return;

            _output.WriteLine("FE6: verifying terrain pointers are zero");

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);

            Assert.Equal(0u, vm.TerrainAvoidPtr);
            Assert.Equal(0u, vm.TerrainDefPtr);
            Assert.Equal(0u, vm.TerrainResPtr);
        }

        [Fact]
        public void ClassEditor_TerrainPtrs_PopulatedForNonFE6()
        {
            // FE7/FE8 classes should have terrain pointers populated (at least some non-zero)
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version == 6) return;

            _output.WriteLine($"{_fixture.Version}: verifying terrain pointers are populated");

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();

            // Check first several classes for at least one with non-zero terrain pointers
            bool anyHasTerrainPtr = false;
            for (int i = 1; i < Math.Min(20, list.Count); i++)
            {
                vm.LoadClass(list[i].addr);
                if (vm.TerrainAvoidPtr != 0 || vm.TerrainDefPtr != 0 || vm.TerrainResPtr != 0)
                {
                    anyHasTerrainPtr = true;
                    _output.WriteLine($"  Class {i} at 0x{list[i].addr:X}: " +
                        $"Avoid=0x{vm.TerrainAvoidPtr:X}, Def=0x{vm.TerrainDefPtr:X}, Res=0x{vm.TerrainResPtr:X}");
                    break;
                }
            }
            Assert.True(anyHasTerrainPtr,
                "At least one FE7/8 class should have non-zero terrain pointers");
        }

        [Fact]
        public void UnitEditor_FE7FE8_HasTalkGroup()
        {
            // FE7/8 units have a TalkGroup field at offset 48; FE6 units do not
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version == 6) return;

            _output.WriteLine($"{_fixture.Version}: verifying TalkGroup field at offset 48");

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();

            // Scan first several units — at least one should have TalkGroup readable
            // (may be zero for some units, but field must exist at offset 48)
            Assert.True(list.Count > 1, "Need at least 2 units");

            vm.LoadUnit(list[1].addr);

            // IsFE6 should be false for FE7/8 ROM
            Assert.False(vm.IsFE6, "IsFE6 should be false for FE7/8 ROM");

            // Verify the field reads from the correct ROM offset
            if (list.Count > 1)
            {
                vm.LoadUnit(list[1].addr);
                uint romVal = CoreState.ROM.u8(list[1].addr + 48);
                Assert.Equal(romVal, vm.TalkGroup);
            }
        }

        [Fact]
        public void MapSettingFE6_HasWorldMapFields()
        {
            // FE6 map settings have WorldMapX/Y fields (when dataSize > 63)
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return;

            _output.WriteLine("FE6: verifying WorldMap fields in MapSettingFE6ViewModel");

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) return;

            vm.LoadEntry(list[1].addr);

            // The WorldMapX/Y fields should be readable (may be zero for some maps)
            _output.WriteLine($"  WorldMapX={vm.WorldMapX}, WorldMapY={vm.WorldMapY}");
            _output.WriteLine($"  WorldMapPointX={vm.WorldMapPointX}, WorldMapPointY={vm.WorldMapPointY}");
            _output.WriteLine($"  DataSize={vm.DataSize}");

            // If data size supports world map fields (>63), they should have been read
            if (vm.DataSize > 63)
            {
                // Fields were read — values are valid bytes (0-255)
                Assert.True(vm.WorldMapX <= 255, "WorldMapX should be in byte range");
                Assert.True(vm.WorldMapY <= 255, "WorldMapY should be in byte range");
            }
        }

        // =================================================================
        // FE8 Specific / Version Detection / NameResolver (17-20)
        // =================================================================

        [Fact]
        public void RomInfo_Version_IsDetectedCorrectly()
        {
            if (!_fixture.IsAvailable) return;

            var ri = CoreState.ROM.RomInfo;
            int version = ri.version;
            _output.WriteLine($"Detected version int: {version}, fixture version: {_fixture.Version}");

            // Version should be one of the known values
            Assert.True(version == 6 || version == 7 || version == 8,
                $"version should be 6, 7, or 8 — got {version}");

            // Cross-check with fixture version string
            switch (_fixture.Version)
            {
                case "FE6":
                    Assert.Equal(6, version);
                    break;
                case "FE7J":
                case "FE7U":
                    Assert.Equal(7, version);
                    break;
                case "FE8J":
                case "FE8U":
                    Assert.Equal(8, version);
                    break;
            }
        }

        [Fact]
        public void NameResolver_GetUnitName_ReturnsNonEmpty()
        {
            if (!_fixture.IsAvailable) return;

            _output.WriteLine($"ROM version: {_fixture.Version}");

            // Unit ID 1 is always a named character (Roy in FE6, Eliwood/Lyn in FE7, Eirika in FE8)
            string name = NameResolver.GetUnitName(1);
            _output.WriteLine($"Unit 1 name: '{name}'");

            Assert.False(string.IsNullOrEmpty(name), "Unit 1 name should not be empty");
            Assert.NotEqual("???", name);
        }

        [Fact]
        public void NameResolver_GetClassName_ReturnsNonEmpty()
        {
            if (!_fixture.IsAvailable) return;

            _output.WriteLine($"ROM version: {_fixture.Version}");

            // Class ID 1 is typically a Lord or main class
            string name = NameResolver.GetClassName(1);
            _output.WriteLine($"Class 1 name: '{name}'");

            Assert.False(string.IsNullOrEmpty(name), "Class 1 name should not be empty");
            Assert.NotEqual("???", name);
        }

        [Fact]
        public void NameResolver_GetItemName_ReturnsNonEmpty()
        {
            if (!_fixture.IsAvailable) return;

            _output.WriteLine($"ROM version: {_fixture.Version}");

            // Item ID 1 is typically Iron Sword or a basic weapon
            string name = NameResolver.GetItemName(1);
            _output.WriteLine($"Item 1 name: '{name}'");

            Assert.False(string.IsNullOrEmpty(name), "Item 1 name should not be empty");
            Assert.NotEqual("???", name);
        }
    }
}
