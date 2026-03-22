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
    /// WU4: List population tests verifying that editor ViewModels return
    /// non-empty, reasonably-sized lists when loading data from ROM.
    /// All tests skip gracefully when ROMs are not available.
    /// </summary>
    [Collection("SharedState")]
    public class ListPopulationTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _rom;
        private readonly ITestOutputHelper _output;

        public ListPopulationTests(RomFixture rom, ITestOutputHelper output)
        {
            _rom = rom;
            _output = output;
        }

        // =====================================================================
        // UnitEditorViewModel list tests
        // =====================================================================

        [Fact]
        public void UnitEditor_LoadUnitList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Unit list should not be empty");
            _output.WriteLine($"UnitEditor: {list.Count} units");
        }

        [Fact]
        public void UnitEditor_LoadUnitList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();

            // All FE games have at least ~50 units and fewer than 1000
            Assert.True(list.Count > 10, $"Too few units: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many units: {list.Count}");
            _output.WriteLine($"UnitEditor: {list.Count} units (reasonable range)");
        }

        [Fact]
        public void UnitEditor_LoadUnitList_AllEntriesHaveAddresses()
        {
            if (!_rom.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            Assert.True(list.Count > 0);

            foreach (var entry in list)
            {
                Assert.True(entry.addr > 0,
                    $"Unit entry #{entry.tag} has zero address");
            }
        }

        [Fact]
        public void UnitEditor_GetListCount_MatchesList()
        {
            if (!_rom.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            Assert.Equal(vm.LoadUnitList().Count, vm.GetListCount());
        }

        // =====================================================================
        // ClassEditorViewModel list tests
        // =====================================================================

        [Fact]
        public void ClassEditor_LoadClassList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Class list should not be empty");
            _output.WriteLine($"ClassEditor: {list.Count} classes");
        }

        [Fact]
        public void ClassEditor_LoadClassList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();

            Assert.True(list.Count > 20, $"Too few classes: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many classes: {list.Count}");
        }

        [Fact]
        public void ClassEditor_LoadClassList_AllEntriesHaveNames()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            Assert.True(list.Count > 0);

            foreach (var entry in list)
            {
                Assert.False(string.IsNullOrEmpty(entry.name),
                    $"Class entry #{entry.tag} has empty name");
            }
        }

        [Fact]
        public void ClassEditor_GetListCount_MatchesList()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            Assert.Equal(vm.LoadClassList().Count, vm.GetListCount());
        }

        // =====================================================================
        // ItemEditorViewModel list tests
        // =====================================================================

        [Fact]
        public void ItemEditor_LoadItemList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Item list should not be empty");
            _output.WriteLine($"ItemEditor: {list.Count} items");
        }

        [Fact]
        public void ItemEditor_LoadItemList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();

            Assert.True(list.Count > 20, $"Too few items: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many items: {list.Count}");
        }

        [Fact]
        public void ItemEditor_LoadItemList_AllEntriesHaveAddresses()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            Assert.True(list.Count > 0);

            foreach (var entry in list)
            {
                Assert.True(entry.addr > 0,
                    $"Item entry #{entry.tag} has zero address");
            }
        }

        [Fact]
        public void ItemEditor_GetListCount_MatchesList()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            Assert.Equal(vm.LoadItemList().Count, vm.GetListCount());
        }

        // =====================================================================
        // MapSettingCore list tests
        // =====================================================================

        [Fact]
        public void MapSettingCore_MakeMapIDList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var list = MapSettingCore.MakeMapIDList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Map ID list should not be empty");
            _output.WriteLine($"MapSettingCore: {list.Count} maps");
        }

        [Fact]
        public void MapSettingCore_MakeMapIDList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var list = MapSettingCore.MakeMapIDList();

            // All FE games have at least several maps and fewer than 500
            Assert.True(list.Count > 5, $"Too few maps: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many maps: {list.Count}");
        }

        [Fact]
        public void MapSettingCore_MakeMapIDList_AllEntriesHaveNames()
        {
            if (!_rom.IsAvailable) return;

            var list = MapSettingCore.MakeMapIDList();
            Assert.True(list.Count > 0);

            foreach (var entry in list)
            {
                Assert.False(string.IsNullOrEmpty(entry.name),
                    $"Map entry #{entry.tag} has empty name");
            }
        }

        [Fact]
        public void MapSettingCore_GetMapCount_MatchesMakeMapIDList()
        {
            if (!_rom.IsAvailable) return;

            int count = MapSettingCore.GetMapCount();
            var list = MapSettingCore.MakeMapIDList();
            Assert.Equal(list.Count, count);
        }

        [Fact]
        public void MapSettingCore_GetMapAddr_ReturnsValidAddresses()
        {
            if (!_rom.IsAvailable) return;

            var list = MapSettingCore.MakeMapIDList();
            if (list.Count < 2) return;

            // First valid map address should match the list entry
            uint addr = MapSettingCore.GetMapAddr(list[0].tag);
            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal(list[0].addr, addr);
        }

        // =====================================================================
        // PortraitViewerViewModel list tests
        // =====================================================================

        [Fact]
        public void PortraitViewer_LoadPortraitList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Portrait list should not be empty");
            _output.WriteLine($"PortraitViewer: {list.Count} portraits");
        }

        [Fact]
        public void PortraitViewer_LoadPortraitList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();

            Assert.True(list.Count > 10, $"Too few portraits: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many portraits: {list.Count}");
        }

        // =====================================================================
        // SongTableViewModel list tests
        // =====================================================================

        [Fact]
        public void SongTable_LoadSongList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new SongTableViewModel();
            var list = vm.LoadSongList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Song list should not be empty");
            _output.WriteLine($"SongTable: {list.Count} songs");
        }

        [Fact]
        public void SongTable_LoadSongList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new SongTableViewModel();
            var list = vm.LoadSongList();

            Assert.True(list.Count > 10, $"Too few songs: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many songs: {list.Count}");
        }

        // =====================================================================
        // SoundRoomViewerViewModel list tests
        // =====================================================================

        [Fact]
        public void SoundRoom_LoadSoundRoomList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new SoundRoomViewerViewModel();
            var list = vm.LoadSoundRoomList();

            Assert.NotNull(list);
            // Sound room might be empty for some versions, so just verify non-null
            _output.WriteLine($"SoundRoom: {list.Count} entries");
        }

        // =====================================================================
        // ImagePortraitViewModel list tests
        // =====================================================================

        [Fact]
        public void ImagePortrait_LoadList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImagePortraitViewModel();
            var list = vm.LoadList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Image portrait list should not be empty");
            _output.WriteLine($"ImagePortrait: {list.Count} portraits");
        }

        [Fact]
        public void ImagePortrait_LoadList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImagePortraitViewModel();
            var list = vm.LoadList();

            Assert.True(list.Count > 10, $"Too few image portraits: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many image portraits: {list.Count}");
        }

        // =====================================================================
        // ImageBGViewModel list tests
        // =====================================================================

        [Fact]
        public void ImageBG_LoadList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImageBGViewModel();
            var list = vm.LoadList();

            Assert.NotNull(list);
            // BG list should exist for all ROMs
            Assert.True(list.Count > 0, "BG list should not be empty");
            _output.WriteLine($"ImageBG: {list.Count} backgrounds");
        }

        // =====================================================================
        // BigCGViewerViewModel list tests
        // =====================================================================

        [Fact]
        public void BigCGViewer_LoadBigCGList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new BigCGViewerViewModel();
            var list = vm.LoadBigCGList();

            Assert.NotNull(list);
            _output.WriteLine($"BigCGViewer: {list.Count} CG entries");
        }

        // =====================================================================
        // ItemIconViewerViewModel list tests
        // =====================================================================

        [Fact]
        public void ItemIconViewer_LoadItemIconList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemIconViewerViewModel();
            var list = vm.LoadItemIconList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Item icon list should not be empty");
            _output.WriteLine($"ItemIconViewer: {list.Count} icons");
        }

        [Fact]
        public void ItemIconViewer_LoadItemIconList_CountIsReasonable()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemIconViewerViewModel();
            var list = vm.LoadItemIconList();

            Assert.True(list.Count > 10, $"Too few item icons: {list.Count}");
            Assert.True(list.Count < 10000, $"Too many item icons: {list.Count}");
        }

        // =====================================================================
        // ImageBattleAnimeViewModel list tests
        // =====================================================================

        [Fact]
        public void ImageBattleAnime_LoadList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImageBattleAnimeViewModel();
            var list = vm.LoadList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "Battle anime list should not be empty");
            _output.WriteLine($"ImageBattleAnime: {list.Count} animations");
        }

        // =====================================================================
        // CCBranchEditorViewModel list tests
        // =====================================================================

        [Fact]
        public void CCBranchEditor_LoadCCBranchList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new CCBranchEditorViewModel();
            var list = vm.LoadCCBranchList();

            Assert.NotNull(list);
            // CC branch exists for FE8, may be empty for FE6/FE7
            _output.WriteLine($"CCBranchEditor: {list.Count} entries");
        }

        // =====================================================================
        // MapSettingFE6ViewModel list tests
        // =====================================================================

        [Fact]
        public void MapSettingFE6_LoadMapSettingList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "MapSettingFE6 list should not be empty");
            _output.WriteLine($"MapSettingFE6: {list.Count} entries");
        }

        // =====================================================================
        // MapSettingViewModel list tests
        // =====================================================================

        [Fact]
        public void MapSetting_LoadMapSettingList_ReturnsItems()
        {
            if (!_rom.IsAvailable) return;

            var vm = new MapSettingViewModel();
            var list = vm.LoadMapSettingList();

            Assert.NotNull(list);
            Assert.True(list.Count > 0, "MapSetting list should not be empty");
            _output.WriteLine($"MapSetting: {list.Count} entries");
        }

        // =====================================================================
        // Cross-editor consistency: list stability
        // =====================================================================

        [Fact]
        public void AllEditors_ListsAreStableAcrossMultipleCalls()
        {
            if (!_rom.IsAvailable) return;

            // Call each LoadList twice and verify the count is stable
            var unit = new UnitEditorViewModel();
            Assert.Equal(unit.LoadUnitList().Count, unit.LoadUnitList().Count);

            var cls = new ClassEditorViewModel();
            Assert.Equal(cls.LoadClassList().Count, cls.LoadClassList().Count);

            var item = new ItemEditorViewModel();
            Assert.Equal(item.LoadItemList().Count, item.LoadItemList().Count);

            var portrait = new PortraitViewerViewModel();
            Assert.Equal(portrait.LoadPortraitList().Count, portrait.LoadPortraitList().Count);

            var song = new SongTableViewModel();
            Assert.Equal(song.LoadSongList().Count, song.LoadSongList().Count);
        }

        [Fact]
        public void AllEditors_ListAddressesAreWithinRomBounds()
        {
            if (!_rom.IsAvailable) return;

            uint romSize = (uint)CoreState.ROM.Data.Length;

            // Spot-check a few editors
            var unit = new UnitEditorViewModel();
            foreach (var e in unit.LoadUnitList())
                Assert.True(e.addr < romSize, $"Unit addr 0x{e.addr:X} >= ROM size");

            var cls = new ClassEditorViewModel();
            foreach (var e in cls.LoadClassList())
                Assert.True(e.addr < romSize, $"Class addr 0x{e.addr:X} >= ROM size");

            var item = new ItemEditorViewModel();
            foreach (var e in item.LoadItemList())
                Assert.True(e.addr < romSize, $"Item addr 0x{e.addr:X} >= ROM size");
        }
    }
}
