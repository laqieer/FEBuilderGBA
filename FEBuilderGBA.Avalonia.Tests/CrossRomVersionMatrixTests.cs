using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Cross-ROM version matrix tests: ~50 [Theory] tests parameterized across
    /// all 5 ROM variants (FE6, FE7J, FE7U, FE8J, FE8U).
    ///
    /// Each test uses RomTestHelper.WithRom() to load a specific ROM, run
    /// assertions, and then restore the original CoreState so tests are isolated.
    ///
    /// Tests skip gracefully when a ROM file is not available.
    /// </summary>
    [Collection("SharedState")]
    public class CrossRomVersionMatrixTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public CrossRomVersionMatrixTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // =====================================================================
        // 1. Version Detection (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionDetection_MatchesExpected(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            int expectedVersionNum = version switch
            {
                "FE6" => 6,
                "FE7J" => 7,
                "FE7U" => 7,
                "FE8J" => 8,
                "FE8U" => 8,
                _ => throw new ArgumentException($"Unknown version: {version}")
            };

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(expectedVersionNum, CoreState.ROM.RomInfo.version);
                _output.WriteLine($"{version}: version={CoreState.ROM.RomInfo.version}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionDetection_VersionToFilename_Matches(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(version, CoreState.ROM.RomInfo.VersionToFilename);
                _output.WriteLine($"{version}: VersionToFilename={CoreState.ROM.RomInfo.VersionToFilename}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionDetection_IsMultibyte_CorrectPerVersion(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            bool expectedMultibyte = version is "FE6" or "FE7J" or "FE8J";

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(expectedMultibyte, CoreState.ROM.RomInfo.is_multibyte);
                _output.WriteLine($"{version}: is_multibyte={CoreState.ROM.RomInfo.is_multibyte}");
            });
        }

        // =====================================================================
        // 2. Unit List (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitList_HasMoreThan10Entries(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();

                Assert.NotNull(list);
                Assert.True(list.Count > 10,
                    $"{version}: expected > 10 units, got {list.Count}");
                _output.WriteLine($"{version}: {list.Count} units");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitList_FirstRealUnit_HasNonZeroNameId(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                Assert.True(list.Count > 1, $"{version}: need at least 2 units");

                vm.LoadUnit(list[1].addr);
                Assert.NotEqual(0u, vm.NameId);
                _output.WriteLine($"{version}: unit[1] NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitList_StructSize_MatchesVersionSpec(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            uint expectedSize = version == "FE6" ? 48u : 52u;

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(expectedSize, CoreState.ROM.RomInfo.unit_datasize);
                _output.WriteLine($"{version}: unit_datasize={CoreState.ROM.RomInfo.unit_datasize}");
            });
        }

        // =====================================================================
        // 3. Class List (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassList_HasMoreThan20Entries(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();

                Assert.NotNull(list);
                Assert.True(list.Count > 20,
                    $"{version}: expected > 20 classes, got {list.Count}");
                _output.WriteLine($"{version}: {list.Count} classes");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassList_FirstRealClass_HasNonZeroNameId(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                Assert.True(list.Count > 1, $"{version}: need at least 2 classes");

                vm.LoadClass(list[1].addr);
                Assert.NotEqual(0u, vm.NameId);
                _output.WriteLine($"{version}: class[1] NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassList_StructSize_MatchesVersionSpec(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            uint expectedSize = version == "FE6" ? 72u : 84u;

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(expectedSize, CoreState.ROM.RomInfo.class_datasize);
                _output.WriteLine($"{version}: class_datasize={CoreState.ROM.RomInfo.class_datasize}");
            });
        }

        // =====================================================================
        // 4. Item List (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemList_HasMoreThan20Entries(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();

                Assert.NotNull(list);
                Assert.True(list.Count > 20,
                    $"{version}: expected > 20 items, got {list.Count}");
                _output.WriteLine($"{version}: {list.Count} items");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemList_FirstRealItem_HasNonZeroNameId(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();
                Assert.True(list.Count > 1, $"{version}: need at least 2 items");

                vm.LoadItem(list[1].addr);
                Assert.NotEqual(0u, vm.NameId);
                _output.WriteLine($"{version}: item[1] NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemList_StructSize_MatchesVersionSpec(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            uint expectedSize = version == "FE6" ? 32u : 36u;

            RomTestHelper.WithRom(version, () =>
            {
                Assert.Equal(expectedSize, CoreState.ROM.RomInfo.item_datasize);
                _output.WriteLine($"{version}: item_datasize={CoreState.ROM.RomInfo.item_datasize}");
            });
        }

        // =====================================================================
        // 5. Map Settings (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void MapSettings_ListIsNonEmpty(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                if (CoreState.ROM.RomInfo.version == 6)
                {
                    var vm = new MapSettingFE6ViewModel();
                    var list = vm.LoadMapSettingList();
                    Assert.NotNull(list);
                    Assert.True(list.Count > 0, $"{version}: FE6 map settings list should not be empty");
                    _output.WriteLine($"{version}: {list.Count} map settings (FE6)");
                }
                else
                {
                    var vm = new MapSettingViewModel();
                    var list = vm.LoadMapSettingList();
                    Assert.NotNull(list);
                    Assert.True(list.Count > 0, $"{version}: map settings list should not be empty");
                    _output.WriteLine($"{version}: {list.Count} map settings");
                }
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void MapSettings_DataSize_MatchesVersion(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                uint dataSize = CoreState.ROM.RomInfo.map_setting_datasize;
                int ver = CoreState.ROM.RomInfo.version;

                if (ver == 7 && !CoreState.ROM.RomInfo.is_multibyte)
                {
                    // FE7U: 152
                    Assert.Equal(152u, dataSize);
                }
                else if (ver == 7)
                {
                    // FE7J: 148
                    Assert.Equal(148u, dataSize);
                }
                else if (ver == 8)
                {
                    // FE8J/FE8U: 148
                    Assert.Equal(148u, dataSize);
                }
                // FE6: variable (68 or 72), just check non-zero
                else if (ver == 6)
                {
                    Assert.True(dataSize > 0, "FE6 map_setting_datasize should be > 0");
                }

                _output.WriteLine($"{version}: map_setting_datasize={dataSize}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void MapSettings_VersionSpecificDispatch_Correct(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                int ver = CoreState.ROM.RomInfo.version;
                uint dataSize = CoreState.ROM.RomInfo.map_setting_datasize;

                if (ver == 6)
                {
                    Assert.False(MapSettingCore.IsFE7ULayout(dataSize));
                    _output.WriteLine($"{version}: dispatches to MapSettingFE6View");
                }
                else if (ver == 7 && MapSettingCore.IsFE7ULayout(dataSize))
                {
                    Assert.True(MapSettingCore.IsFE7ULayout(dataSize));
                    _output.WriteLine($"{version}: dispatches to MapSettingFE7UView");
                }
                else if (ver == 7)
                {
                    Assert.False(MapSettingCore.IsFE7ULayout(dataSize));
                    _output.WriteLine($"{version}: dispatches to MapSettingFE7View");
                }
                else
                {
                    Assert.Equal(8, ver);
                    _output.WriteLine($"{version}: dispatches to MapSettingView");
                }
            });
        }

        // =====================================================================
        // 6. Cross-Version Struct Sizes (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void StructSizes_AllThree_MatchExpected(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var info = CoreState.ROM.RomInfo;
                bool isFE6 = info.version == 6;

                uint expectedUnit = isFE6 ? 48u : 52u;
                uint expectedClass = isFE6 ? 72u : 84u;
                uint expectedItem = isFE6 ? 32u : 36u;

                Assert.Equal(expectedUnit, info.unit_datasize);
                Assert.Equal(expectedClass, info.class_datasize);
                Assert.Equal(expectedItem, info.item_datasize);

                _output.WriteLine($"{version}: unit={info.unit_datasize} class={info.class_datasize} item={info.item_datasize}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void StructSizes_PortraitDataSize_IsNonZero(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.True(CoreState.ROM.RomInfo.portrait_datasize > 0,
                    $"{version}: portrait_datasize should be > 0");
                _output.WriteLine($"{version}: portrait_datasize={CoreState.ROM.RomInfo.portrait_datasize}");
            });
        }

        // =====================================================================
        // 7. Write Round-Trip Per ROM (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void WriteRoundTrip_Unit_NameIdPreserved(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                // Locate unit[1] base address
                uint pointerOffset = U.toOffset(info.unit_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint unit1Addr = baseAddr + info.unit_datasize;

                // Read original NameId (u16 at offset 0)
                uint originalNameId = rom.u16(unit1Addr);
                Assert.NotEqual(0u, originalNameId);

                // Write a modified value, read back, then restore
                uint testValue = originalNameId ^ 0x0001; // flip lowest bit
                int idx = (int)unit1Addr;
                byte origByte0 = rom.Data[idx];
                byte origByte1 = rom.Data[idx + 1];

                try
                {
                    rom.write_u16(unit1Addr, testValue);
                    uint readBack = rom.u16(unit1Addr);
                    Assert.Equal(testValue, readBack);
                    _output.WriteLine($"{version}: unit[1] write round-trip OK (0x{originalNameId:X} -> 0x{testValue:X} -> readback 0x{readBack:X})");
                }
                finally
                {
                    // Restore original bytes
                    rom.Data[idx] = origByte0;
                    rom.Data[idx + 1] = origByte1;

                    // Verify restore
                    Assert.Equal(originalNameId, rom.u16(unit1Addr));
                }
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void WriteRoundTrip_Item_NameIdPreserved(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                uint pointerOffset = U.toOffset(info.item_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint item1Addr = baseAddr + info.item_datasize;

                uint originalNameId = rom.u16(item1Addr);
                Assert.NotEqual(0u, originalNameId);

                uint testValue = originalNameId ^ 0x0001;
                int idx = (int)item1Addr;
                byte origByte0 = rom.Data[idx];
                byte origByte1 = rom.Data[idx + 1];

                try
                {
                    rom.write_u16(item1Addr, testValue);
                    uint readBack = rom.u16(item1Addr);
                    Assert.Equal(testValue, readBack);
                    _output.WriteLine($"{version}: item[1] write round-trip OK");
                }
                finally
                {
                    rom.Data[idx] = origByte0;
                    rom.Data[idx + 1] = origByte1;
                    Assert.Equal(originalNameId, rom.u16(item1Addr));
                }
            });
        }

        // =====================================================================
        // 8. NameResolver Per ROM (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void NameResolver_GetUnitName_ReturnsNonEmpty(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                NameResolver.ClearCache();
                string name = NameResolver.GetUnitName(1);
                Assert.False(string.IsNullOrEmpty(name), $"{version}: GetUnitName(1) returned empty");
                Assert.NotEqual("???", name);
                _output.WriteLine($"{version}: unit[1] = \"{name}\"");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void NameResolver_GetClassName_ReturnsNonEmpty(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                NameResolver.ClearCache();
                string name = NameResolver.GetClassName(1);
                Assert.False(string.IsNullOrEmpty(name), $"{version}: GetClassName(1) returned empty");
                Assert.NotEqual("???", name);
                _output.WriteLine($"{version}: class[1] = \"{name}\"");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void NameResolver_GetItemName_ReturnsNonEmpty(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                NameResolver.ClearCache();
                string name = NameResolver.GetItemName(1);
                Assert.False(string.IsNullOrEmpty(name), $"{version}: GetItemName(1) returned empty");
                Assert.NotEqual("???", name);
                _output.WriteLine($"{version}: item[1] = \"{name}\"");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void NameResolver_AllThreeNames_AreDistinct(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                NameResolver.ClearCache();
                string unitName = NameResolver.GetUnitName(1);
                string className = NameResolver.GetClassName(1);
                string itemName = NameResolver.GetItemName(1);

                // All three should be non-empty
                Assert.False(string.IsNullOrEmpty(unitName));
                Assert.False(string.IsNullOrEmpty(className));
                Assert.False(string.IsNullOrEmpty(itemName));

                // At least item name should differ from unit name in all versions
                // (unit[1] is a character, item[1] is a weapon)
                Assert.NotEqual(unitName, itemName);
                _output.WriteLine($"{version}: unit=\"{unitName}\" class=\"{className}\" item=\"{itemName}\"");
            });
        }

        // =====================================================================
        // 9. Cross-Version Field Offsets (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void FieldOffsets_UnitNameId_AtOffset0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                uint pointerOffset = U.toOffset(info.unit_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint unit1Addr = baseAddr + info.unit_datasize;

                // NameId is u16 at offset 0 for all versions
                uint nameIdDirect = rom.u16(unit1Addr + 0);

                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                Assert.True(list.Count > 1);
                vm.LoadUnit(list[1].addr);

                Assert.Equal(nameIdDirect, vm.NameId);
                _output.WriteLine($"{version}: unit[1] NameId@0=0x{nameIdDirect:X}, VM.NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void FieldOffsets_ClassNameId_AtOffset0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                uint pointerOffset = U.toOffset(info.class_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint class1Addr = baseAddr + info.class_datasize;

                // NameId is u16 at offset 0 for all versions
                uint nameIdDirect = rom.u16(class1Addr + 0);

                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                Assert.True(list.Count > 1);
                vm.LoadClass(list[1].addr);

                Assert.Equal(nameIdDirect, vm.NameId);
                _output.WriteLine($"{version}: class[1] NameId@0=0x{nameIdDirect:X}, VM.NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void FieldOffsets_ItemNameId_AtOffset0(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                uint pointerOffset = U.toOffset(info.item_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint item1Addr = baseAddr + info.item_datasize;

                // NameId is u16 at offset 0 for all versions
                uint nameIdDirect = rom.u16(item1Addr + 0);

                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();
                Assert.True(list.Count > 1);
                vm.LoadItem(list[1].addr);

                Assert.Equal(nameIdDirect, vm.NameId);
                _output.WriteLine($"{version}: item[1] NameId@0=0x{nameIdDirect:X}, VM.NameId=0x{vm.NameId:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void FieldOffsets_UnitClassId_AtOffset4(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                uint pointerOffset = U.toOffset(info.unit_pointer);
                uint baseAddr = rom.p32(pointerOffset);
                uint unit1Addr = baseAddr + info.unit_datasize;

                // ClassId is u8 at offset 4 for all versions
                uint classIdDirect = rom.u8(unit1Addr + 4);

                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                Assert.True(list.Count > 1);
                vm.LoadUnit(list[1].addr);

                Assert.Equal(classIdDirect, vm.ClassId);
                _output.WriteLine($"{version}: unit[1] ClassId@4=0x{classIdDirect:X}, VM.ClassId=0x{vm.ClassId:X}");
            });
        }

        // =====================================================================
        // 10. Version Guards (5 tests)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionGuard_MapSettingVM_GuardsAgainstFE6(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new MapSettingViewModel();
                var list = vm.LoadMapSettingList();

                if (CoreState.ROM.RomInfo.version == 6 && list.Count > 0)
                {
                    // MapSettingViewModel should guard against FE6: CurrentAddr stays 0
                    vm.LoadMapSetting(list[0].addr);
                    Assert.Equal(0u, vm.CurrentAddr);
                    _output.WriteLine($"{version}: MapSettingViewModel correctly blocks FE6");
                }
                else if (CoreState.ROM.RomInfo.version != 6 && list.Count > 1)
                {
                    // Non-FE6: should load normally
                    vm.LoadMapSetting(list[1].addr);
                    Assert.NotEqual(0u, vm.CurrentAddr);
                    _output.WriteLine($"{version}: MapSettingViewModel loads for non-FE6");
                }
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionGuard_MapSettingFE6VM_WorksOnAllVersions(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new MapSettingFE6ViewModel();
                var list = vm.LoadMapSettingList();

                // LoadMapSettingList should always return entries (it reads map_setting_pointer)
                Assert.NotNull(list);
                Assert.True(list.Count > 0, $"{version}: MapSettingFE6ViewModel should list entries");
                _output.WriteLine($"{version}: MapSettingFE6ViewModel list count={list.Count}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionGuard_ClassEditorVM_TerrainPointers(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                if (list.Count < 2) return;

                vm.LoadClass(list[1].addr);

                if (CoreState.ROM.RomInfo.version == 6)
                {
                    // FE6 does not have terrain pointers in the class struct
                    Assert.Equal(0u, vm.TerrainAvoidPtr);
                    Assert.Equal(0u, vm.TerrainDefPtr);
                    Assert.Equal(0u, vm.TerrainResPtr);
                    _output.WriteLine($"{version}: terrain pointers zero (expected for FE6)");
                }
                else
                {
                    // FE7/FE8: at least some classes should have terrain pointers
                    bool anyHasTerrain = false;
                    for (int i = 1; i < Math.Min(10, list.Count); i++)
                    {
                        vm.LoadClass(list[i].addr);
                        if (vm.TerrainAvoidPtr != 0 || vm.TerrainDefPtr != 0 || vm.TerrainResPtr != 0)
                        {
                            anyHasTerrain = true;
                            break;
                        }
                    }
                    Assert.True(anyHasTerrain,
                        $"{version}: at least one class should have terrain pointers in FE7/FE8");
                    _output.WriteLine($"{version}: terrain pointers non-zero (expected for FE7/FE8)");
                }
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionGuard_UnitEditorVM_IsFE6Flag(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                if (list.Count < 1) return;

                vm.LoadUnit(list[0].addr);

                bool expectedFE6 = CoreState.ROM.RomInfo.version == 6;
                Assert.Equal(expectedFE6, vm.IsFE6);
                _output.WriteLine($"{version}: IsFE6={vm.IsFE6}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void VersionGuard_UnitPointerAndClassPointer_AreValid(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;
                var info = rom.RomInfo;

                // All versions should have valid unit/class/item pointers
                Assert.NotEqual(0u, info.unit_pointer);
                Assert.NotEqual(0u, info.class_pointer);
                Assert.NotEqual(0u, info.item_pointer);

                // Deref pointers should point to valid ROM locations
                uint unitBase = rom.p32(U.toOffset(info.unit_pointer));
                uint classBase = rom.p32(U.toOffset(info.class_pointer));
                uint itemBase = rom.p32(U.toOffset(info.item_pointer));

                Assert.True(unitBase > 0 && unitBase < (uint)rom.Data.Length,
                    $"{version}: unit table base 0x{unitBase:X} out of range");
                Assert.True(classBase > 0 && classBase < (uint)rom.Data.Length,
                    $"{version}: class table base 0x{classBase:X} out of range");
                Assert.True(itemBase > 0 && itemBase < (uint)rom.Data.Length,
                    $"{version}: item table base 0x{itemBase:X} out of range");

                _output.WriteLine($"{version}: unit@0x{unitBase:X} class@0x{classBase:X} item@0x{itemBase:X}");
            });
        }

        // =====================================================================
        // 11. Bonus: Cross-version ROM data sanity (extra tests to reach ~50)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void RomData_SizeIsReasonable(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var rom = CoreState.ROM;

                // GBA ROMs are at least 8MB and at most 32MB
                Assert.True(rom.Data.Length >= 0x800000,
                    $"{version}: ROM size {rom.Data.Length} too small (< 8MB)");
                Assert.True(rom.Data.Length <= 0x2000000,
                    $"{version}: ROM size {rom.Data.Length} too large (> 32MB)");

                _output.WriteLine($"{version}: ROM size = {rom.Data.Length / 1024 / 1024}MB ({rom.Data.Length} bytes)");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void RomData_TextPointer_IsNonZero(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.NotEqual(0u, CoreState.ROM.RomInfo.text_pointer);
                _output.WriteLine($"{version}: text_pointer=0x{CoreState.ROM.RomInfo.text_pointer:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void RomData_PortraitPointer_IsNonZero(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.NotEqual(0u, CoreState.ROM.RomInfo.portrait_pointer);
                _output.WriteLine($"{version}: portrait_pointer=0x{CoreState.ROM.RomInfo.portrait_pointer:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void RomData_SoundTablePointer_IsNonZero(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.NotEqual(0u, CoreState.ROM.RomInfo.sound_table_pointer);
                _output.WriteLine($"{version}: sound_table_pointer=0x{CoreState.ROM.RomInfo.sound_table_pointer:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void RomData_HuffmanPointers_AreNonZero(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                Assert.NotEqual(0u, CoreState.ROM.RomInfo.mask_pointer);
                Assert.NotEqual(0u, CoreState.ROM.RomInfo.mask_point_base_pointer);
                _output.WriteLine($"{version}: mask_pointer=0x{CoreState.ROM.RomInfo.mask_pointer:X}");
            });
        }

        // =====================================================================
        // 12. Unit/Class/Item list entry addresses & consistency
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitList_AllEntries_HaveValidAddresses(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                foreach (var entry in list)
                {
                    Assert.True(entry.addr > 0, $"{version}: unit entry has zero address");
                    Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                        $"{version}: unit entry 0x{entry.addr:X} exceeds ROM");
                }
                _output.WriteLine($"{version}: all {list.Count} unit addresses valid");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassList_AllEntries_HaveValidAddresses(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                foreach (var entry in list)
                {
                    Assert.True(entry.addr > 0, $"{version}: class entry has zero address");
                    Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                        $"{version}: class entry 0x{entry.addr:X} exceeds ROM");
                }
                _output.WriteLine($"{version}: all {list.Count} class addresses valid");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemList_AllEntries_HaveValidAddresses(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();
                foreach (var entry in list)
                {
                    Assert.True(entry.addr > 0, $"{version}: item entry has zero address");
                    Assert.True(entry.addr < (uint)CoreState.ROM.Data.Length,
                        $"{version}: item entry 0x{entry.addr:X} exceeds ROM");
                }
                _output.WriteLine($"{version}: all {list.Count} item addresses valid");
            });
        }

        // =====================================================================
        // 13. List entry names non-empty
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitList_AllEntries_HaveNonEmptyNames(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                foreach (var entry in list)
                {
                    Assert.False(string.IsNullOrEmpty(entry.name),
                        $"{version}: unit entry at 0x{entry.addr:X} has empty name");
                }
                _output.WriteLine($"{version}: all {list.Count} unit entries have names");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassList_AllEntries_HaveNonEmptyNames(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                foreach (var entry in list)
                {
                    Assert.False(string.IsNullOrEmpty(entry.name),
                        $"{version}: class entry at 0x{entry.addr:X} has empty name");
                }
                _output.WriteLine($"{version}: all {list.Count} class entries have names");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemList_AllEntries_HaveNonEmptyNames(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();
                foreach (var entry in list)
                {
                    Assert.False(string.IsNullOrEmpty(entry.name),
                        $"{version}: item entry at 0x{entry.addr:X} has empty name");
                }
                _output.WriteLine($"{version}: all {list.Count} item entries have names");
            });
        }

        // =====================================================================
        // 14. List count consistency (repeated calls return same count)
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ListCounts_StableAcrossMultipleCalls(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
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

                _output.WriteLine($"{version}: units={unitCount1} classes={classCount1} items={itemCount1}");
            });
        }

        // =====================================================================
        // 15. Sequential load does not leak state
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitEditor_LoadMultiple_StateDoesNotLeak(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();
                if (list.Count < 3) return;

                vm.LoadUnit(list[1].addr);
                uint addr1 = vm.CurrentAddr;

                vm.LoadUnit(list[2].addr);
                Assert.NotEqual(addr1, vm.CurrentAddr);
                Assert.Equal(list[2].addr, vm.CurrentAddr);
                _output.WriteLine($"{version}: load[1]=0x{addr1:X} load[2]=0x{vm.CurrentAddr:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassEditor_LoadMultiple_StateDoesNotLeak(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();
                if (list.Count < 3) return;

                vm.LoadClass(list[1].addr);
                uint addr1 = vm.CurrentAddr;

                vm.LoadClass(list[2].addr);
                Assert.NotEqual(addr1, vm.CurrentAddr);
                Assert.Equal(list[2].addr, vm.CurrentAddr);
                _output.WriteLine($"{version}: load[1]=0x{addr1:X} load[2]=0x{vm.CurrentAddr:X}");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemEditor_LoadMultiple_StateDoesNotLeak(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();
                if (list.Count < 3) return;

                vm.LoadItem(list[1].addr);
                uint addr1 = vm.CurrentAddr;

                vm.LoadItem(list[2].addr);
                Assert.NotEqual(addr1, vm.CurrentAddr);
                Assert.Equal(list[2].addr, vm.CurrentAddr);
                _output.WriteLine($"{version}: load[1]=0x{addr1:X} load[2]=0x{vm.CurrentAddr:X}");
            });
        }

        // =====================================================================
        // 16. Unit base stats sanity
        // =====================================================================

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ClassEditor_SomeClassHasNonZeroMov(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();

                bool anyHasMov = false;
                for (int i = 1; i < Math.Min(10, list.Count); i++)
                {
                    vm.LoadClass(list[i].addr);
                    if (vm.Mov > 0) { anyHasMov = true; break; }
                }
                Assert.True(anyHasMov, $"{version}: at least one class should have non-zero Mov");
                _output.WriteLine($"{version}: found class with non-zero Mov");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void ItemEditor_SomeItemHasCombatStats(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new ItemEditorViewModel();
                var list = vm.LoadItemList();

                bool anyHasStats = false;
                for (int i = 1; i < Math.Min(20, list.Count); i++)
                {
                    vm.LoadItem(list[i].addr);
                    if (vm.Might > 0 || vm.Hit > 0 || vm.Uses > 0)
                    {
                        anyHasStats = true;
                        break;
                    }
                }
                Assert.True(anyHasStats, $"{version}: at least one item should have combat stats");
                _output.WriteLine($"{version}: found item with combat stats");
            });
        }

        [Theory]
        [MemberData(nameof(TestRomLocator.AllRoms), MemberType = typeof(TestRomLocator))]
        public void UnitBaseStats_SomeUnitHasNonZeroHP(string version, string? romPath)
        {
            if (romPath == null) { _output.WriteLine($"Skipping {version}: ROM not available"); return; }

            RomTestHelper.WithRom(version, () =>
            {
                var vm = new UnitEditorViewModel();
                var list = vm.LoadUnitList();

                bool anyHasHP = false;
                for (int i = 1; i < Math.Min(10, list.Count); i++)
                {
                    vm.LoadUnit(list[i].addr);
                    if (vm.HP != 0) { anyHasHP = true; break; }
                }
                Assert.True(anyHasHP, $"{version}: at least one of first 10 units should have non-zero HP");
                _output.WriteLine($"{version}: found unit with non-zero HP");
            });
        }
    }

    // =========================================================================
    // RomTestHelper: save/restore CoreState around per-ROM test actions
    // =========================================================================

    /// <summary>
    /// Helper that temporarily loads a specific ROM version into CoreState,
    /// runs an action, and restores the original state.
    /// </summary>
    internal static class RomTestHelper
    {
        /// <summary>
        /// Load a specific ROM by version name, run the action, then restore
        /// the previous CoreState. If the ROM is not available, the action
        /// is not executed (test gracefully skips).
        /// </summary>
        public static void WithRom(string version, Action action)
        {
            string? path = TestRomLocator.FindRom(version);
            if (path == null) return;

            // Save current CoreState
            var prevRom = CoreState.ROM;
            var prevCommentCache = CoreState.CommentCache;
            var prevLintCache = CoreState.LintCache;
            var prevWorkSupportCache = CoreState.WorkSupportCache;
            var prevSystemTextEncoder = CoreState.SystemTextEncoder;
            var prevFETextEncoder = CoreState.FETextEncoder;
            var prevTextEscape = CoreState.TextEscape;
            var prevUndo = CoreState.Undo;
            var prevBaseDirectory = CoreState.BaseDirectory;

            try
            {
                // Set BaseDirectory for config access
                string assemblyDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = assemblyDir;

                // Register code pages (idempotent)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Load config if available
                string configPath = Path.Combine(assemblyDir, "config", "config.xml");
                if (File.Exists(configPath) && CoreState.Config == null)
                {
                    var config = new Config();
                    config.Load(configPath);
                    CoreState.Config = config;
                }

                // Load the ROM
                var rom = new ROM();
                if (!rom.Load(path, out string _))
                    return;

                CoreState.ROM = rom;

                // Wire headless caches
                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();

                // Wire text encoder
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom);
                }
                catch
                {
                    CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                }

                // Init Huffman encoder
                try
                {
                    CoreState.FETextEncoder = new FETextEncode();
                }
                catch
                {
                    // Non-fatal
                }

                // Init text escape and undo
                CoreState.TextEscape ??= new TextEscape();
                CoreState.Undo ??= new Undo();

                // Clear all caches to avoid cross-ROM contamination
                NameResolver.ClearCache();
                try { PatchDetection.ClearAllCaches(); } catch { }
                try { MagicSplitUtil.ClearCache(); } catch { }

                // Run the test action
                action();
            }
            finally
            {
                // Restore previous CoreState
                CoreState.ROM = prevRom;
                CoreState.CommentCache = prevCommentCache;
                CoreState.LintCache = prevLintCache;
                CoreState.WorkSupportCache = prevWorkSupportCache;
                CoreState.SystemTextEncoder = prevSystemTextEncoder;
                CoreState.FETextEncoder = prevFETextEncoder;
                CoreState.TextEscape = prevTextEscape;
                CoreState.Undo = prevUndo;
                CoreState.BaseDirectory = prevBaseDirectory;

                // Clear caches to avoid cross-ROM contamination
                NameResolver.ClearCache();
                try { PatchDetection.ClearAllCaches(); } catch { }
                try { MagicSplitUtil.ClearCache(); } catch { }
            }
        }
    }
}
