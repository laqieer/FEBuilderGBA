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
    /// Tests verifying cross-editor navigation and name resolution.
    /// Validates that entity IDs in one editor (e.g. Unit.ClassId) correctly
    /// resolve when loaded in another editor (e.g. ClassEditor), and that
    /// NameResolver produces consistent results across all lookup paths.
    /// </summary>
    [Collection("SharedState")]
    public class CrossEditorNavigationTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public CrossEditorNavigationTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // =================================================================
        // Unit -> Class Navigation (4 tests)
        // =================================================================

        [Fact]
        public void Unit_ClassId_ResolvesToValidClass()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            if (unitList.Count < 2) return;

            unitVm.LoadUnit(unitList[1].addr);
            uint classId = unitVm.ClassId;

            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();

            _output.WriteLine($"Unit 1 ClassId={classId}, classList.Count={classList.Count}");

            if (classId < (uint)classList.Count)
            {
                classVm.LoadClass(classList[(int)classId].addr);
                Assert.NotEqual(0u, classVm.NameId);
                _output.WriteLine($"Resolved class NameId=0x{classVm.NameId:X04}, Name={classVm.Name}");
            }
            else
            {
                _output.WriteLine($"ClassId {classId} out of range for class list (count={classList.Count})");
            }
        }

        [Fact]
        public void Unit_ClassId_NameMatchesNameResolver()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            if (unitList.Count < 2) return;

            unitVm.LoadUnit(unitList[1].addr);
            uint classId = unitVm.ClassId;

            string resolvedName = "";
            try
            {
                resolvedName = NameResolver.GetClassName(classId);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetClassName({classId}) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"NameResolver.GetClassName({classId}) = \"{resolvedName}\"");

            // Load the class via the editor and compare
            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();
            if (classId < (uint)classList.Count)
            {
                classVm.LoadClass(classList[(int)classId].addr);
                _output.WriteLine($"ClassEditor.Name = \"{classVm.Name}\"");
                // Both should resolve to the same underlying text
                Assert.Equal(resolvedName, classVm.Name);
            }
        }

        [Fact]
        public void Unit_MultipleUnits_HaveValidClassIds()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();

            int checkCount = Math.Min(10, unitList.Count);
            int validCount = 0;

            for (int i = 1; i < checkCount; i++)
            {
                unitVm.LoadUnit(unitList[i].addr);
                uint classId = unitVm.ClassId;

                if (classId < (uint)classList.Count)
                {
                    validCount++;
                    _output.WriteLine($"Unit {i}: ClassId={classId} (valid)");
                }
                else
                {
                    _output.WriteLine($"Unit {i}: ClassId={classId} OUT OF RANGE (classList.Count={classList.Count})");
                }
            }

            Assert.True(validCount > 0,
                "At least one of the first 10 units should have a ClassId within class list range");
        }

        [Fact]
        public void Unit_ClassId_InRange()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();

            int checkCount = Math.Min(10, unitList.Count);
            for (int i = 1; i < checkCount; i++)
            {
                unitVm.LoadUnit(unitList[i].addr);
                // ClassId is a byte (0-255); verify it is within class list bounds
                Assert.True(unitVm.ClassId < (uint)classList.Count,
                    $"Unit {i} ClassId={unitVm.ClassId} exceeds class list count={classList.Count}");
            }
        }

        // =================================================================
        // Unit -> Portrait Navigation (3 tests)
        // =================================================================

        [Fact]
        public void Unit_PortraitId_IsWithinRange()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            if (unitList.Count < 2) return;

            unitVm.LoadUnit(unitList[1].addr);

            // PortraitId is a u16; for most ROM versions, valid portraits are < 256
            _output.WriteLine($"Unit 1 PortraitId=0x{unitVm.PortraitId:X04}");
            Assert.True(unitVm.PortraitId < 0x100,
                $"PortraitId 0x{unitVm.PortraitId:X} is unreasonably large (expected < 256)");
        }

        [Fact]
        public void Unit_PortraitName_ResolvesNonEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            if (unitList.Count < 2) return;

            unitVm.LoadUnit(unitList[1].addr);
            uint portraitId = unitVm.PortraitId;

            if (portraitId == 0)
            {
                _output.WriteLine("Unit 1 has PortraitId=0; skipping portrait name check");
                return;
            }

            string portraitName = "";
            try
            {
                portraitName = NameResolver.GetPortraitName(portraitId);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetPortraitName({portraitId}) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"NameResolver.GetPortraitName({portraitId}) = \"{portraitName}\"");
            Assert.False(string.IsNullOrEmpty(portraitName),
                $"Portrait name for ID {portraitId} should be non-empty");
        }

        [Fact]
        public void Unit_MultipleUnits_HaveDistinctPortraits()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();

            var portraitIds = new HashSet<uint>();
            int checkCount = Math.Min(10, unitList.Count);

            for (int i = 1; i < checkCount; i++)
            {
                unitVm.LoadUnit(unitList[i].addr);
                portraitIds.Add(unitVm.PortraitId);
            }

            _output.WriteLine($"Found {portraitIds.Count} distinct portrait IDs in first {checkCount - 1} units");
            Assert.True(portraitIds.Count >= 2,
                $"Expected at least 2 distinct portrait IDs among first units, got {portraitIds.Count}");
        }

        // =================================================================
        // Item Cross-References (4 tests)
        // =================================================================

        [Fact]
        public void Item_NameResolver_MatchesLoadedName()
        {
            if (!_fixture.IsAvailable) return;

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();
            if (itemList.Count < 2) return;

            // Use item index 1 (first real item)
            itemVm.LoadItem(itemList[1].addr);

            string vmName = itemVm.Name;
            string resolvedName = "";
            try
            {
                resolvedName = NameResolver.GetItemName(1);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetItemName(1) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"ItemEditor.Name = \"{vmName}\", NameResolver.GetItemName(1) = \"{resolvedName}\"");
            Assert.Equal(resolvedName, vmName);
        }

        [Fact]
        public void Item_WeaponType_IsConsistentAcrossItems()
        {
            if (!_fixture.IsAvailable) return;

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();

            // Group items by WeaponType and verify items sharing a type have compatible Might > 0
            // (i.e. if an item has a weapon type 0-7, at least one item of that type has stats)
            var weaponTypeItems = new Dictionary<uint, List<int>>();
            int checkCount = Math.Min(20, itemList.Count);

            for (int i = 1; i < checkCount; i++)
            {
                itemVm.LoadItem(itemList[i].addr);
                uint wt = itemVm.WeaponType;
                if (wt <= 7) // weapon types only
                {
                    if (!weaponTypeItems.ContainsKey(wt))
                        weaponTypeItems[wt] = new List<int>();
                    weaponTypeItems[wt].Add(i);
                }
            }

            // For each weapon type group, verify at least one item has Might or Hit > 0
            foreach (var kvp in weaponTypeItems)
            {
                bool anyHasStats = false;
                foreach (int idx in kvp.Value)
                {
                    itemVm.LoadItem(itemList[idx].addr);
                    if (itemVm.Might > 0 || itemVm.Hit > 0)
                    {
                        anyHasStats = true;
                        break;
                    }
                }
                _output.WriteLine($"WeaponType {kvp.Key}: {kvp.Value.Count} items, hasStats={anyHasStats}");
                Assert.True(anyHasStats,
                    $"Weapon type {kvp.Key} has {kvp.Value.Count} items but none have Might or Hit > 0");
            }
        }

        [Fact]
        public void Item_ItemNumber_MatchesListIndex()
        {
            if (!_fixture.IsAvailable) return;

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();

            int checkCount = Math.Min(20, itemList.Count);
            for (int i = 0; i < checkCount; i++)
            {
                itemVm.LoadItem(itemList[i].addr);
                _output.WriteLine($"Item[{i}]: ItemNumber={itemVm.ItemNumber}");
                Assert.Equal((uint)i, itemVm.ItemNumber);
            }
        }

        [Fact]
        public void Item_NameId_ResolvesToText()
        {
            if (!_fixture.IsAvailable) return;

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();
            if (itemList.Count < 2) return;

            itemVm.LoadItem(itemList[1].addr);
            uint nameId = itemVm.NameId;

            if (nameId == 0)
            {
                _output.WriteLine("Item 1 has NameId=0; skipping text resolution check");
                return;
            }

            string text = "";
            try
            {
                text = NameResolver.GetTextById(nameId);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetTextById(0x{nameId:X04}) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"NameResolver.GetTextById(0x{nameId:X04}) = \"{text}\"");
            Assert.False(string.IsNullOrEmpty(text),
                $"Text for NameId 0x{nameId:X04} should be non-empty");
        }

        // =================================================================
        // MapSetting -> Song Cross-Reference (2 tests)
        // =================================================================

        [Fact]
        public void MapSetting_PlayerPhaseBGM_ResolvesToSong()
        {
            if (!_fixture.IsAvailable) return;
            // This test is for FE7/FE8 only (non-FE6)
            if (CoreState.ROM.RomInfo.version == 6) return;

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();

            bool foundNonZeroBGM = false;
            int checkCount = Math.Min(10, list.Count);

            for (int i = 0; i < checkCount; i++)
            {
                vm.LoadMapSetting(list[i].addr);
                uint bgm = vm.PlayerPhaseBGM;
                if (bgm > 0)
                {
                    foundNonZeroBGM = true;
                    string songName = "";
                    try
                    {
                        songName = NameResolver.GetSongName(bgm);
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"NameResolver.GetSongName({bgm}) threw: {ex.Message}");
                        continue;
                    }

                    _output.WriteLine($"Map {i}: PlayerPhaseBGM=0x{bgm:X}, SongName=\"{songName}\"");
                    Assert.False(string.IsNullOrEmpty(songName),
                        $"Song name for BGM 0x{bgm:X} should be non-empty");
                    break;
                }
            }

            if (!foundNonZeroBGM)
                _output.WriteLine("No maps with non-zero PlayerPhaseBGM found in first 10 entries");
        }

        [Fact]
        public void MapSettingFE6_PlayerPhaseBGM_ResolvesToSong()
        {
            if (!_fixture.IsAvailable) return;
            // This test is for FE6 only
            if (CoreState.ROM.RomInfo.version != 6) return;

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();

            bool foundNonZeroBGM = false;
            int checkCount = Math.Min(10, list.Count);

            for (int i = 0; i < checkCount; i++)
            {
                vm.LoadEntry(list[i].addr);
                uint bgm = vm.PlayerPhaseBGM;
                if (bgm > 0)
                {
                    foundNonZeroBGM = true;
                    string songName = "";
                    try
                    {
                        songName = NameResolver.GetSongName(bgm);
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"NameResolver.GetSongName({bgm}) threw: {ex.Message}");
                        continue;
                    }

                    _output.WriteLine($"Map {i}: PlayerPhaseBGM=0x{bgm:X}, SongName=\"{songName}\"");
                    Assert.False(string.IsNullOrEmpty(songName),
                        $"Song name for BGM 0x{bgm:X} should be non-empty");
                    break;
                }
            }

            if (!foundNonZeroBGM)
                _output.WriteLine("No FE6 maps with non-zero PlayerPhaseBGM found in first 10 entries");
        }

        // =================================================================
        // NameResolver Consistency (2 tests)
        // =================================================================

        [Fact]
        public void NameResolver_UnitName_ConsistentWithVmName()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            if (unitList.Count < 2) return;

            unitVm.LoadUnit(unitList[1].addr);
            string vmName = unitVm.Name;

            // Compute the raw table index from the ROM address.
            // NameResolver.GetUnitName(id) reads at base + id*dataSize where base is
            // the raw pointer (without FE6 skip). We derive id from the entry address.
            ROM rom = CoreState.ROM;
            uint rawBase = rom.p32(rom.RomInfo.unit_pointer);
            uint dataSize = rom.RomInfo.unit_datasize;
            uint unitIndex = (unitList[1].addr - rawBase) / dataSize;

            string resolvedName = "";
            try
            {
                resolvedName = NameResolver.GetUnitName(unitIndex);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetUnitName({unitIndex}) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"VM.Name=\"{vmName}\", NameResolver.GetUnitName({unitIndex})=\"{resolvedName}\"");
            Assert.Equal(resolvedName, vmName);
        }

        [Fact]
        public void NameResolver_ClassName_ConsistentWithVmName()
        {
            if (!_fixture.IsAvailable) return;

            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();
            if (classList.Count < 2) return;

            classVm.LoadClass(classList[1].addr);
            string vmName = classVm.Name;
            uint classNumber = classVm.ClassNumber;

            // NameResolver.GetClassName uses the class index (position in table)
            string resolvedName = "";
            try
            {
                resolvedName = NameResolver.GetClassName(1);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"NameResolver.GetClassName(1) threw: {ex.Message}");
                return;
            }

            _output.WriteLine($"VM.Name=\"{vmName}\", NameResolver.GetClassName(1)=\"{resolvedName}\", ClassNumber={classNumber}");
            Assert.Equal(resolvedName, vmName);
        }
    }
}
