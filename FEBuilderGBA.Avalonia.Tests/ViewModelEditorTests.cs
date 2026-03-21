using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests verifying that core struct editor ViewModels correctly load data from real ROMs.
    /// All tests skip gracefully when ROMs are not available.
    ///
    /// Uses RomFixture (from WU0) to share ROM initialization across tests.
    /// </summary>
    [Collection("SharedState")]
    public class ViewModelEditorTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;

        public ViewModelEditorTests(RomFixture fixture)
        {
            _fixture = fixture;
        }

        // =====================================================================
        // UnitEditorViewModel
        // =====================================================================

        [Fact]
        public void UnitEditor_Constructor_DoesNotThrow()
        {
            var vm = new UnitEditorViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void UnitEditor_LoadUnitList_ReturnsNonEmptyList()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Unit list should have at least one entry");
            // FE8U has roughly 0x100 units; any version should have > 10
            Assert.True(list.Count > 10, $"Expected > 10 units, got {list.Count}");
        }

        [Fact]
        public void UnitEditor_LoadUnit_PopulatesProperties()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 1, "Need at least 2 units to test non-zero entry");

            // Load entry 1 (first real unit, e.g. Eirika in FE8U, Roy in FE6)
            vm.LoadUnit(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite, "CanWrite should be true after loading");
            // First real unit should have a valid NameId
            Assert.NotEqual(0u, vm.NameId);
        }

        [Fact]
        public void UnitEditor_LoadUnit_NameIsNotEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);

            // The resolved name should be non-empty for a valid unit
            Assert.False(string.IsNullOrEmpty(vm.Name), "Unit name should be resolved");
            Assert.NotEqual("???", vm.Name);
        }

        [Fact]
        public void UnitEditor_LoadUnit_BaseStatsAreReasonable()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            // Load a few units and check at least one has non-zero HP
            bool anyHasHP = false;
            for (int i = 1; i < System.Math.Min(10, list.Count); i++)
            {
                vm.LoadUnit(list[i].addr);
                if (vm.HP != 0)
                {
                    anyHasHP = true;
                    break;
                }
            }
            Assert.True(anyHasHP, "At least one of the first 10 units should have non-zero HP");
        }

        [Fact]
        public void UnitEditor_LoadUnit_GrowthRatesExist()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            // Check that at least one unit in the first 10 has growth rates
            bool anyHasGrowth = false;
            for (int i = 1; i < System.Math.Min(10, list.Count); i++)
            {
                vm.LoadUnit(list[i].addr);
                if (vm.GrowHP > 0 || vm.GrowStr > 0 || vm.GrowSpd > 0)
                {
                    anyHasGrowth = true;
                    break;
                }
            }
            Assert.True(anyHasGrowth, "At least one unit should have non-zero growth rates");
        }

        [Fact]
        public void UnitEditor_LoadUnit_LevelIsInRange()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);

            // Level should be 0-255 (byte), typically 1-20 for playable units
            Assert.True(vm.Level <= 255, $"Level {vm.Level} out of byte range");
        }

        [Fact]
        public void UnitEditor_LoadUnit_ClassIdIsValid()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);

            // First real unit should have a non-zero class
            Assert.NotEqual(0u, vm.ClassId);
            Assert.True(vm.ClassId <= 255, "ClassId should be in byte range");
        }

        [Fact]
        public void UnitEditor_GetDataReport_ReturnsNonEmptyDictionary()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);
            var report = vm.GetDataReport();

            Assert.NotNull(report);
            Assert.True(report.Count > 0, "Data report should have entries");
            Assert.True(report.ContainsKey("addr"), "Report should contain addr");
            Assert.True(report.ContainsKey("NameId"), "Report should contain NameId");
        }

        [Fact]
        public void UnitEditor_GetRawRomReport_MatchesDataReport()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);
            var dataReport = vm.GetDataReport();
            var rawReport = vm.GetRawRomReport();

            Assert.NotNull(rawReport);
            Assert.True(rawReport.Count > 0);

            // NameId from data report should match u16@0x00 from raw report
            Assert.Equal(dataReport["NameId"], rawReport["u16@0x00"]);
        }

        [Fact]
        public void UnitEditor_GetListCount_MatchesListLength()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            int count = vm.GetListCount();

            Assert.Equal(list.Count, count);
        }

        [Fact]
        public void UnitEditor_IsFE6_MatchesRomVersion()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 1) return;

            vm.LoadUnit(list[0].addr);

            bool expectedFE6 = CoreState.ROM.RomInfo.version == 6;
            Assert.Equal(expectedFE6, vm.IsFE6);
        }

        [Fact]
        public void UnitEditor_ValidateUnit_DoesNotThrow()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) return;

            vm.LoadUnit(list[1].addr);
            var warnings = vm.ValidateUnit();

            Assert.NotNull(warnings);
        }

        [Fact]
        public void UnitEditor_IsGrowthTrigger_RecognizesKnownProps()
        {
            Assert.True(UnitEditorViewModel.IsGrowthTrigger("HP"));
            Assert.True(UnitEditorViewModel.IsGrowthTrigger("GrowHP"));
            Assert.True(UnitEditorViewModel.IsGrowthTrigger("ClassId"));
            Assert.True(UnitEditorViewModel.IsGrowthTrigger("SimLevel"));
            Assert.False(UnitEditorViewModel.IsGrowthTrigger("Name"));
            Assert.False(UnitEditorViewModel.IsGrowthTrigger("CanWrite"));
        }

        // =====================================================================
        // ClassEditorViewModel
        // =====================================================================

        [Fact]
        public void ClassEditor_Constructor_DoesNotThrow()
        {
            var vm = new ClassEditorViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void ClassEditor_LoadClassList_ReturnsNonEmptyList()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Class list should have entries");
            // All FE games have dozens of classes
            Assert.True(list.Count > 20, $"Expected > 20 classes, got {list.Count}");
        }

        [Fact]
        public void ClassEditor_LoadClass_PopulatesProperties()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            Assert.True(list.Count > 1);

            // Load class 1 (e.g. Lord in FE8U)
            vm.LoadClass(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite);
            Assert.NotEqual(0u, vm.NameId);
        }

        [Fact]
        public void ClassEditor_LoadClass_IsDirtyFalseAfterLoad()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);

            // LoadClass calls MarkClean() at the end, so IsDirty should be false
            Assert.False(vm.IsDirty, "IsDirty should be false after LoadClass");
        }

        [Fact]
        public void ClassEditor_LoadClass_NameIsResolved()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);

            Assert.False(string.IsNullOrEmpty(vm.Name));
            Assert.NotEqual("???", vm.Name);
        }

        [Fact]
        public void ClassEditor_LoadClass_MovIsNonZero()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            // Check first few classes for non-zero movement
            bool anyHasMov = false;
            for (int i = 1; i < System.Math.Min(10, list.Count); i++)
            {
                vm.LoadClass(list[i].addr);
                if (vm.Mov > 0)
                {
                    anyHasMov = true;
                    break;
                }
            }
            Assert.True(anyHasMov, "At least one class should have non-zero movement");
        }

        [Fact]
        public void ClassEditor_LoadClass_BaseStatsAreReasonable()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);

            // Base HP should be reasonable for class 1
            Assert.True(vm.BaseHp <= 255, "BaseHp out of byte range");
            Assert.True(vm.BaseStr <= 255, "BaseStr out of byte range");
        }

        [Fact]
        public void ClassEditor_LoadClass_WeaponRanksExist()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();

            // Check that at least some class has weapon ranks
            bool anyHasWepRank = false;
            for (int i = 1; i < System.Math.Min(20, list.Count); i++)
            {
                vm.LoadClass(list[i].addr);
                if (vm.WepRankSword > 0 || vm.WepRankLance > 0 || vm.WepRankAxe > 0 ||
                    vm.WepRankBow > 0 || vm.WepRankStaff > 0 || vm.WepRankAnima > 0 ||
                    vm.WepRankLight > 0 || vm.WepRankDark > 0)
                {
                    anyHasWepRank = true;
                    break;
                }
            }
            Assert.True(anyHasWepRank, "At least one class should have non-zero weapon ranks");
        }

        [Fact]
        public void ClassEditor_GetDataReport_ContainsExpectedKeys()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);
            var report = vm.GetDataReport();

            Assert.NotNull(report);
            Assert.True(report.ContainsKey("addr"));
            Assert.True(report.ContainsKey("W0_NameId"));
            Assert.True(report.ContainsKey("B17_Mov"));
            Assert.True(report.ContainsKey("B40_Ability1"));
        }

        [Fact]
        public void ClassEditor_GetRawRomReport_IsNotEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);
            var report = vm.GetRawRomReport();

            Assert.NotNull(report);
            Assert.True(report.Count > 0);
        }

        [Fact]
        public void ClassEditor_GetListCount_MatchesListLength()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            Assert.Equal(vm.LoadClassList().Count, vm.GetListCount());
        }

        [Fact]
        public void ClassEditor_IsFE6_MatchesRomVersion()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            bool expectedFE6 = CoreState.ROM.RomInfo.version == 6;
            Assert.Equal(expectedFE6, vm.IsFE6);
        }

        [Fact]
        public void ClassEditor_ValidateClass_DoesNotThrow()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);
            var warnings = vm.ValidateClass();

            Assert.NotNull(warnings);
        }

        [Fact]
        public void ClassEditor_CalculateGrowth_ProducesOutput()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);
            vm.CalculateGrowth();

            Assert.False(string.IsNullOrEmpty(vm.GrowthSimText),
                "Growth simulator should produce non-empty text");
            Assert.Contains("LV", vm.GrowthSimText);
        }

        [Fact]
        public void ClassEditor_FE6Layout_HasDifferentStructSize()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return; // Only relevant for FE6

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) return;

            vm.LoadClass(list[1].addr);

            // FE6 should not have terrain pointers
            Assert.Equal(0u, vm.TerrainAvoidPtr);
            Assert.Equal(0u, vm.TerrainDefPtr);
            Assert.Equal(0u, vm.TerrainResPtr);
        }

        // =====================================================================
        // ItemEditorViewModel
        // =====================================================================

        [Fact]
        public void ItemEditor_Constructor_DoesNotThrow()
        {
            var vm = new ItemEditorViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void ItemEditor_LoadItemList_ReturnsNonEmptyList()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Item list should have entries");
            // All FE games have many items
            Assert.True(list.Count > 20, $"Expected > 20 items, got {list.Count}");
        }

        [Fact]
        public void ItemEditor_LoadItem_PopulatesProperties()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            Assert.True(list.Count > 1);

            // Load item 1 (e.g. Iron Sword in FE8U)
            vm.LoadItem(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite);
            Assert.NotEqual(0u, vm.NameId);
        }

        [Fact]
        public void ItemEditor_LoadItem_NameIsResolved()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);

            Assert.False(string.IsNullOrEmpty(vm.Name));
            Assert.NotEqual("???", vm.Name);
        }

        [Fact]
        public void ItemEditor_LoadItem_CombatStatsAreReasonable()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();

            // Check first weapon-type items for reasonable stats
            bool anyHasStats = false;
            for (int i = 1; i < System.Math.Min(20, list.Count); i++)
            {
                vm.LoadItem(list[i].addr);
                if (vm.Might > 0 || vm.Hit > 0 || vm.Uses > 0)
                {
                    anyHasStats = true;
                    break;
                }
            }
            Assert.True(anyHasStats, "At least one item should have combat stats");
        }

        [Fact]
        public void ItemEditor_LoadItem_UsesInByteRange()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            Assert.True(vm.Uses <= 255, "Uses should be in byte range");
            Assert.True(vm.Might <= 255, "Might should be in byte range");
            Assert.True(vm.Hit <= 255, "Hit should be in byte range");
        }

        [Fact]
        public void ItemEditor_LoadItem_WeaponTypeInRange()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            // Weapon type is a byte; values 0-12 are typical
            Assert.True(vm.WeaponType <= 255, "WeaponType should be in byte range");
        }

        [Fact]
        public void ItemEditor_RecalcComputed_SetsShopPrices()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();

            // Find an item with non-zero price
            for (int i = 1; i < System.Math.Min(20, list.Count); i++)
            {
                vm.LoadItem(list[i].addr);
                if (vm.Price > 0 && vm.Uses > 0)
                {
                    Assert.True(vm.ShopBuyPrice > 0, "ShopBuyPrice should be > 0 for priced item");
                    Assert.True(vm.ShopSellPrice > 0, "ShopSellPrice should be > 0");
                    Assert.Equal(vm.Uses * vm.Price, vm.ShopBuyPrice);
                    Assert.Equal((vm.Uses * vm.Price) / 2, vm.ShopSellPrice);
                    return;
                }
            }
            // If no priced item found in first 20, that's unusual but not a failure
        }

        [Fact]
        public void ItemEditor_GetDataReport_ContainsExpectedKeys()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            var report = vm.GetDataReport();

            Assert.NotNull(report);
            Assert.True(report.ContainsKey("addr"));
            Assert.True(report.ContainsKey("W0_NameId"));
            Assert.True(report.ContainsKey("B21_Might"));
            Assert.True(report.ContainsKey("W26_Price"));
        }

        [Fact]
        public void ItemEditor_GetRawRomReport_NameIdMatches()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            var data = vm.GetDataReport();
            var raw = vm.GetRawRomReport();

            Assert.Equal(data["W0_NameId"], raw["u16@0x00"]);
        }

        [Fact]
        public void ItemEditor_GetListCount_MatchesListLength()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            Assert.Equal(vm.LoadItemList().Count, vm.GetListCount());
        }

        [Fact]
        public void ItemEditor_ValidateItem_DoesNotThrow()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) return;

            vm.LoadItem(list[1].addr);
            var warnings = vm.ValidateItem();

            Assert.NotNull(warnings);
        }

        [Fact]
        public void ItemEditor_LoadItem_ItemNumberMatchesIndex()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 5) return;

            // Item at index 3 should have ItemNumber == 3 (self-referencing ID)
            vm.LoadItem(list[3].addr);
            Assert.Equal(3u, vm.ItemNumber);
        }

        // =====================================================================
        // MapSettingFE6ViewModel (FE6-specific map settings)
        // =====================================================================

        [Fact]
        public void MapSettingFE6_Constructor_DoesNotThrow()
        {
            var vm = new MapSettingFE6ViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void MapSettingFE6_LoadMapSettingList_ReturnsEntries()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();

            Assert.NotNull(list);
            // All FE games have map settings
            Assert.True(list.Count > 0, "Map setting list should have entries");
        }

        [Fact]
        public void MapSettingFE6_LoadEntry_PopulatesBasicFields()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return; // FE6 only

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) return;

            vm.LoadEntry(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            Assert.True(vm.DataSize > 0, "DataSize should be set after load");
        }

        // =====================================================================
        // MapSettingViewModel (FE7/FE8 map settings)
        // =====================================================================

        [Fact]
        public void MapSetting_Constructor_DoesNotThrow()
        {
            var vm = new MapSettingViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void MapSetting_LoadMapSettingList_ReturnsEntries()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Map setting list should have entries");
        }

        [Fact]
        public void MapSetting_LoadMapSetting_PopulatesFields()
        {
            if (!_fixture.IsAvailable) return;
            // MapSettingViewModel is for FE7/FE8 only
            if (CoreState.ROM.RomInfo.version == 6) return;

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) return;

            vm.LoadMapSetting(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.DataSize > 0);
        }

        [Fact]
        public void MapSetting_LoadMapSetting_BGMFieldsArePopulated()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version == 6) return;

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();

            // At least some map should have BGM set
            bool anyHasBGM = false;
            for (int i = 0; i < System.Math.Min(10, list.Count); i++)
            {
                vm.LoadMapSetting(list[i].addr);
                if (vm.PlayerPhaseBGM > 0)
                {
                    anyHasBGM = true;
                    break;
                }
            }
            Assert.True(anyHasBGM, "At least one map should have PlayerPhaseBGM set");
        }

        [Fact]
        public void MapSetting_FE6Guard_ClearsStateForFE6()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM.RomInfo.version != 6) return;

            // MapSettingViewModel should guard against FE6 misuse
            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 1) return;

            vm.LoadMapSetting(list[0].addr);

            // For FE6, it should reset/clear state instead of loading
            Assert.Equal(0u, vm.CurrentAddr);
        }

        // =====================================================================
        // ViewModelBase — dirty tracking
        // =====================================================================

        [Fact]
        public void ViewModelBase_IsDirty_FalseInitially()
        {
            var vm = new UnitEditorViewModel();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void ViewModelBase_MarkClean_ResetsDirty()
        {
            var vm = new ClassEditorViewModel();
            // Manually set a property to trigger dirty
            vm.NameId = 999;
            Assert.True(vm.IsDirty, "IsDirty should be true after property change");

            vm.MarkClean();
            Assert.False(vm.IsDirty, "IsDirty should be false after MarkClean");
        }

        [Fact]
        public void ViewModelBase_IsLoading_SuppressesDirty()
        {
            var vm = new ItemEditorViewModel();
            vm.IsLoading = true;
            vm.NameId = 42;
            Assert.False(vm.IsDirty, "IsDirty should remain false when IsLoading is true");

            vm.IsLoading = false;
            vm.NameId = 43;
            Assert.True(vm.IsDirty, "IsDirty should become true when IsLoading is false");
        }

        [Fact]
        public void ViewModelBase_PropertyChanged_Fires()
        {
            var vm = new UnitEditorViewModel();
            var changedProps = new List<string>();
            vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

            vm.HP = 42;

            Assert.Contains("HP", changedProps);
        }

        // =====================================================================
        // Cross-ViewModel consistency: list counts are stable
        // =====================================================================

        [Fact]
        public void AllEditors_ListsAreConsistentAcrossMultipleCalls()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var classVm = new ClassEditorViewModel();
            var itemVm = new ItemEditorViewModel();

            int unitCount1 = unitVm.GetListCount();
            int unitCount2 = unitVm.GetListCount();
            Assert.Equal(unitCount1, unitCount2);

            int classCount1 = classVm.GetListCount();
            int classCount2 = classVm.GetListCount();
            Assert.Equal(classCount1, classCount2);

            int itemCount1 = itemVm.GetListCount();
            int itemCount2 = itemVm.GetListCount();
            Assert.Equal(itemCount1, itemCount2);
        }

        [Fact]
        public void AllEditors_ListEntriesHaveValidAddresses()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            foreach (var entry in unitList)
            {
                Assert.True(entry.addr > 0, $"Unit entry has zero address");
                Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                    $"Unit entry address 0x{entry.addr:X} exceeds ROM size");
            }

            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();
            foreach (var entry in classList)
            {
                Assert.True(entry.addr > 0, $"Class entry has zero address");
                Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                    $"Class entry address 0x{entry.addr:X} exceeds ROM size");
            }

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();
            foreach (var entry in itemList)
            {
                Assert.True(entry.addr > 0, $"Item entry has zero address");
                Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                    $"Item entry address 0x{entry.addr:X} exceeds ROM size");
            }
        }

        [Fact]
        public void AllEditors_ListEntriesHaveNames()
        {
            if (!_fixture.IsAvailable) return;

            var unitVm = new UnitEditorViewModel();
            var unitList = unitVm.LoadUnitList();
            foreach (var entry in unitList)
            {
                Assert.False(string.IsNullOrEmpty(entry.name),
                    $"Unit entry at 0x{entry.addr:X} has empty name");
            }

            var classVm = new ClassEditorViewModel();
            var classList = classVm.LoadClassList();
            foreach (var entry in classList)
            {
                Assert.False(string.IsNullOrEmpty(entry.name),
                    $"Class entry at 0x{entry.addr:X} has empty name");
            }

            var itemVm = new ItemEditorViewModel();
            var itemList = itemVm.LoadItemList();
            foreach (var entry in itemList)
            {
                Assert.False(string.IsNullOrEmpty(entry.name),
                    $"Item entry at 0x{entry.addr:X} has empty name");
            }
        }

        // =====================================================================
        // Load multiple entries sequentially — state does not leak
        // =====================================================================

        [Fact]
        public void UnitEditor_LoadMultipleUnits_StateDoesNotLeak()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 3) return;

            vm.LoadUnit(list[1].addr);
            uint addr1 = vm.CurrentAddr;
            uint nameId1 = vm.NameId;

            vm.LoadUnit(list[2].addr);
            uint addr2 = vm.CurrentAddr;

            Assert.NotEqual(addr1, addr2);
            // After loading a different entry, the old address should not persist
            Assert.Equal(list[2].addr, vm.CurrentAddr);
        }

        [Fact]
        public void ClassEditor_LoadMultipleClasses_StateDoesNotLeak()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 3) return;

            vm.LoadClass(list[1].addr);
            uint addr1 = vm.CurrentAddr;

            vm.LoadClass(list[2].addr);
            Assert.Equal(list[2].addr, vm.CurrentAddr);
            Assert.NotEqual(addr1, vm.CurrentAddr);
        }

        [Fact]
        public void ItemEditor_LoadMultipleItems_StateDoesNotLeak()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 3) return;

            vm.LoadItem(list[1].addr);
            uint addr1 = vm.CurrentAddr;

            vm.LoadItem(list[2].addr);
            Assert.Equal(list[2].addr, vm.CurrentAddr);
            Assert.NotEqual(addr1, vm.CurrentAddr);
        }
    }
}
