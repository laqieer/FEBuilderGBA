using System.IO;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Verifies that each Avalonia ViewModel references the correct ROM info pointer,
    /// data size, and field offsets by inspecting the source code.
    /// </summary>
    public class ViewModelRomAddressTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        private string ReadViewModel(string name) =>
            File.ReadAllText(Path.Combine(AvaloniaDir, "ViewModels", name));

        // ---------------------------------------------------------------
        // UnitEditorViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void UnitEditorViewModel_ReadsFromUnitPointer()
        {
            var src = ReadViewModel("UnitEditorViewModel.cs");
            Assert.Contains("rom.RomInfo.unit_pointer", src);
            Assert.Contains("rom.RomInfo.unit_datasize", src);
            Assert.Contains("rom.RomInfo.unit_maxcount", src);
        }

        [Fact]
        public void UnitEditorViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("UnitEditorViewModel.cs");
            // NameId at offset 0 (W0)
            Assert.Contains("rom.u16(addr + 0)", src);
            // DescId at offset 2 (W2)
            Assert.Contains("rom.u16(addr + 2)", src);
            // UnitId at offset 4 (B4)
            Assert.Contains("rom.u8(addr + 4)", src);
            // ClassId at offset 5 (B5)
            Assert.Contains("rom.u8(addr + 5)", src);
            // PortraitId at offset 6 (W6)
            Assert.Contains("rom.u16(addr + 6)", src);
            // Level at offset 11 (B11)
            Assert.Contains("rom.u8(addr + 11)", src);
            // HP at offset 12 (signed byte, matching WinForms b12)
            Assert.Contains("(sbyte)rom.u8(addr + 12)", src);
            // Weapon levels at offsets 20-27
            Assert.Contains("rom.u8(addr + 20)", src);
            Assert.Contains("rom.u8(addr + 27)", src);
            // Growth rates at offsets 28-34
            Assert.Contains("rom.u8(addr + 28)", src);
            Assert.Contains("rom.u8(addr + 34)", src);
            // Ability flags at offsets 40-43
            Assert.Contains("rom.u8(addr + 40)", src);
            Assert.Contains("rom.u8(addr + 43)", src);
            // Support pointer at offset 44
            Assert.Contains("rom.u32(addr + 44)", src);
        }

        [Fact]
        public void UnitEditorViewModel_WritesMatchReads()
        {
            var src = ReadViewModel("UnitEditorViewModel.cs");
            // Verify write offsets match read offsets (corrected from old buggy offsets)
            Assert.Contains("rom.write_u16(addr + 0, NameId)", src);
            Assert.Contains("rom.write_u16(addr + 2, DescId)", src);
            Assert.Contains("rom.write_u8(addr + 4, UnitId)", src);
            Assert.Contains("rom.write_u8(addr + 5, ClassId)", src);
            Assert.Contains("rom.write_u16(addr + 6, PortraitId)", src);
            Assert.Contains("rom.write_u8(addr + 11, Level)", src);
            // Base stats use signed→unsigned byte cast: (uint)(byte)value
            Assert.Contains("rom.write_u8(addr + 12, (uint)(byte)HP)", src);
            Assert.Contains("rom.write_u8(addr + 13, (uint)(byte)Str)", src);
            Assert.Contains("rom.write_u8(addr + 20, WepSword)", src);
            Assert.Contains("rom.write_u8(addr + 28, GrowHP)", src);
            Assert.Contains("rom.write_u8(addr + 40, Ability1)", src);
            Assert.Contains("rom.write_u32(addr + 44, SupportPtr)", src);
        }

        [Fact]
        public void UnitEditorViewModel_ChecksVersionForLayout()
        {
            var src = ReadViewModel("UnitEditorViewModel.cs");
            Assert.Contains("rom.RomInfo.version", src);
        }

        // ---------------------------------------------------------------
        // ItemEditorViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void ItemEditorViewModel_ReadsFromItemPointer()
        {
            var src = ReadViewModel("ItemEditorViewModel.cs");
            Assert.Contains("rom.RomInfo.item_pointer", src);
            Assert.Contains("rom.RomInfo.item_datasize", src);
        }

        [Fact]
        public void ItemEditorViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("ItemEditorViewModel.cs");
            // Text IDs (decimal offsets matching WinForms convention)
            Assert.Contains("rom.u16(addr + 0)", src);    // W0 NameId
            Assert.Contains("rom.u16(addr + 2)", src);    // W2 DescId
            Assert.Contains("rom.u16(addr + 4)", src);    // W4 UseDescId
            // Item properties
            Assert.Contains("rom.u8(addr + 6)", src);     // B6 ItemNumber
            Assert.Contains("rom.u8(addr + 7)", src);     // B7 WeaponType
            Assert.Contains("rom.u8(addr + 20)", src);    // B20 Uses
            Assert.Contains("rom.u8(addr + 21)", src);    // B21 Might
            Assert.Contains("rom.u8(addr + 22)", src);    // B22 Hit
            Assert.Contains("rom.u8(addr + 23)", src);    // B23 Weight
            Assert.Contains("rom.u8(addr + 24)", src);    // B24 Crit
            Assert.Contains("rom.u8(addr + 25)", src);    // B25 Range
            Assert.Contains("rom.u16(addr + 26)", src);   // W26 Price
            Assert.Contains("rom.u8(addr + 28)", src);    // B28 WeaponRank
        }

        [Fact]
        public void ItemEditorViewModel_WritesMatchReads()
        {
            var src = ReadViewModel("ItemEditorViewModel.cs");
            Assert.Contains("rom.write_u16(addr + 0, NameId)", src);
            Assert.Contains("rom.write_u16(addr + 2, DescId)", src);
            Assert.Contains("rom.write_u16(addr + 4, UseDescId)", src);
            Assert.Contains("rom.write_u8(addr + 6, ItemNumber)", src);
            Assert.Contains("rom.write_u8(addr + 7, WeaponType)", src);
            Assert.Contains("rom.write_u8(addr + 20, Uses)", src);
            Assert.Contains("rom.write_u8(addr + 21, Might)", src);
            Assert.Contains("rom.write_u8(addr + 22, Hit)", src);
            Assert.Contains("rom.write_u8(addr + 23, Weight)", src);
            Assert.Contains("rom.write_u8(addr + 24, Crit)", src);
            Assert.Contains("rom.write_u8(addr + 25, Range)", src);
            Assert.Contains("rom.write_u16(addr + 26, Price)", src);
            Assert.Contains("rom.write_u8(addr + 28, WeaponRank)", src);
        }

        // ---------------------------------------------------------------
        // ClassEditorViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void ClassEditorViewModel_ReadsFromClassPointer()
        {
            var src = ReadViewModel("ClassEditorViewModel.cs");
            Assert.Contains("rom.RomInfo.class_pointer", src);
            Assert.Contains("rom.RomInfo.class_datasize", src);
        }

        [Fact]
        public void ClassEditorViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("ClassEditorViewModel.cs");
            // Header
            Assert.Contains("rom.u16(addr + 0)", src);    // NameId
            Assert.Contains("rom.u16(addr + 2)", src);    // DescId
            Assert.Contains("rom.u8(addr + 4)", src);     // ClassNumber
            // Base stats
            Assert.Contains("rom.u8(addr + 11)", src);    // BaseHp
            Assert.Contains("rom.u8(addr + 12)", src);    // BaseStr
            Assert.Contains("rom.u8(addr + 13)", src);    // BaseSkl
            Assert.Contains("rom.u8(addr + 14)", src);    // BaseSpd
            Assert.Contains("rom.u8(addr + 15)", src);    // BaseDef
            Assert.Contains("rom.u8(addr + 16)", src);    // BaseRes
            // Movement
            Assert.Contains("rom.u8(addr + 17)", src);    // Mov
            // Growth rates
            Assert.Contains("rom.u8(addr + 27)", src);    // GrowHp
            Assert.Contains("rom.u8(addr + 28)", src);    // GrowStr
            Assert.Contains("rom.u8(addr + 29)", src);    // GrowSkl
            Assert.Contains("rom.u8(addr + 30)", src);    // GrowSpd
            Assert.Contains("rom.u8(addr + 31)", src);    // GrowDef
            Assert.Contains("rom.u8(addr + 32)", src);    // GrowRes
            Assert.Contains("rom.u8(addr + 33)", src);    // GrowLck
        }

        [Fact]
        public void ClassEditorViewModel_WritesMatchReads()
        {
            var src = ReadViewModel("ClassEditorViewModel.cs");
            Assert.Contains("rom.write_u16(addr + 0, NameId)", src);
            Assert.Contains("rom.write_u16(addr + 2, DescId)", src);
            Assert.Contains("rom.write_u8(addr + 4, ClassNumber)", src);
            Assert.Contains("rom.write_u8(addr + 11, BaseHp)", src);
            Assert.Contains("rom.write_u8(addr + 12, BaseStr)", src);
            Assert.Contains("rom.write_u8(addr + 13, BaseSkl)", src);
            Assert.Contains("rom.write_u8(addr + 14, BaseSpd)", src);
            Assert.Contains("rom.write_u8(addr + 15, BaseDef)", src);
            Assert.Contains("rom.write_u8(addr + 16, BaseRes)", src);
            Assert.Contains("rom.write_u8(addr + 17, Mov)", src);
            Assert.Contains("rom.write_u8(addr + 27, GrowHp)", src);
            Assert.Contains("rom.write_u8(addr + 28, GrowStr)", src);
            Assert.Contains("rom.write_u8(addr + 29, GrowSkl)", src);
            Assert.Contains("rom.write_u8(addr + 30, GrowSpd)", src);
            Assert.Contains("rom.write_u8(addr + 31, GrowDef)", src);
            Assert.Contains("rom.write_u8(addr + 32, GrowRes)", src);
            Assert.Contains("rom.write_u8(addr + 33, GrowLck)", src);
        }

        // ---------------------------------------------------------------
        // ItemWeaponEffectViewerViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void ItemWeaponEffectViewModel_ReadsFromEffectPointer()
        {
            var src = ReadViewModel("ItemWeaponEffectViewerViewModel.cs");
            Assert.Contains("rom.RomInfo.item_effect_pointer", src);
        }

        [Fact]
        public void ItemWeaponEffectViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("ItemWeaponEffectViewerViewModel.cs");
            Assert.Contains("rom.u8(addr + 0)", src);     // ItemId
            Assert.Contains("rom.u8(addr + 1)", src);     // Unknown1
            Assert.Contains("rom.u8(addr + 2)", src);     // AnimType
            Assert.Contains("rom.u8(addr + 3)", src);     // Unknown3
            Assert.Contains("rom.u16(addr + 4)", src);    // EffectId
            Assert.Contains("rom.u16(addr + 6)", src);    // Unknown6
            Assert.Contains("rom.u32(addr + 8)", src);    // MapEffectPointer
            Assert.Contains("rom.u8(addr + 12)", src);    // DamageEffect
            Assert.Contains("rom.u8(addr + 13)", src);    // Motion
            Assert.Contains("rom.u8(addr + 14)", src);    // HitColor
            Assert.Contains("rom.u8(addr + 15)", src);    // Unknown15
        }

        [Fact]
        public void ItemWeaponEffectViewModel_Uses16ByteEntrySize()
        {
            var src = ReadViewModel("ItemWeaponEffectViewerViewModel.cs");
            // Each weapon effect entry is 16 bytes
            Assert.Contains("i * 16", src);
        }

        // ---------------------------------------------------------------
        // MapSettingViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void MapSettingViewModel_ReadsMapSettingDatasize()
        {
            var src = ReadViewModel("MapSettingViewModel.cs");
            Assert.Contains("rom.RomInfo.map_setting_datasize", src);
        }

        [Fact]
        public void MapSettingViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("MapSettingViewModel.cs");
            Assert.Contains("rom.u8(addr + 4)", src);     // TilesetPLIST
            Assert.Contains("rom.u8(addr + 5)", src);     // MapPLIST
            Assert.Contains("rom.u8(addr + 10)", src);    // PalettePLIST
            Assert.Contains("rom.u8(addr + 12)", src);    // Weather
            Assert.Contains("rom.u8(addr + 13)", src);    // ObjType
        }

        [Fact]
        public void MapSettingViewModel_HandlesVersionDifferences()
        {
            var src = ReadViewModel("MapSettingViewModel.cs");
            // FE6 has simpler struct, FE7/FE8 has chapter name at 0x70
            Assert.Contains("rom.RomInfo.version == 6", src);
            Assert.Contains("rom.u16(addr + 0)", src);    // FE6 ChapterNameId
            Assert.Contains("rom.u16(addr + 0x70)", src); // FE7/FE8 ChapterNameId
        }

        [Fact]
        public void MapSettingViewModel_UsesMapSettingCore()
        {
            var src = ReadViewModel("MapSettingViewModel.cs");
            Assert.Contains("MapSettingCore.MakeMapIDList()", src);
        }

        // ---------------------------------------------------------------
        // SongTableViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void SongTableViewModel_ReadsFromSoundTablePointer()
        {
            var src = ReadViewModel("SongTableViewModel.cs");
            Assert.Contains("rom.RomInfo.sound_table_pointer", src);
        }

        [Fact]
        public void SongTableViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("SongTableViewModel.cs");
            // Song table entries are 8 bytes each (pointer + padding)
            Assert.Contains("i * 8", src);
            // Header pointer at offset 0
            Assert.Contains("rom.u32(addr)", src);
            // Song header fields
            Assert.Contains("rom.u8(headerAddr + 0)", src);  // TrackCount
            Assert.Contains("rom.u8(headerAddr + 2)", src);  // Priority
            Assert.Contains("rom.u8(headerAddr + 3)", src);  // Reverb
        }

        [Fact]
        public void SongTableViewModel_ValidatesPointers()
        {
            var src = ReadViewModel("SongTableViewModel.cs");
            Assert.Contains("U.isPointer(headerPtr)", src);
            Assert.Contains("U.isSafetyOffset(headerAddr)", src);
        }

        // ---------------------------------------------------------------
        // PortraitViewerViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void PortraitViewModel_ReadsFromPortraitPointer()
        {
            var src = ReadViewModel("PortraitViewerViewModel.cs");
            Assert.Contains("rom.RomInfo.portrait_pointer", src);
            Assert.Contains("rom.RomInfo.portrait_datasize", src);
        }

        [Fact]
        public void PortraitViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("PortraitViewerViewModel.cs");
            // Portrait struct: image pointer at +0, map pointer at +4, palette pointer at +8
            Assert.Contains("rom.u32(addr + 0)", src);    // ImagePointer
            Assert.Contains("rom.u32(addr + 4)", src);    // MapPointer
            Assert.Contains("rom.u32(addr + 8)", src);    // PalettePointer
        }

        [Fact]
        public void PortraitViewModel_ValidatesEntryPointers()
        {
            var src = ReadViewModel("PortraitViewerViewModel.cs");
            Assert.Contains("U.isPointerOrNULL", src);
        }

        [Fact]
        public void PortraitViewModel_UsesImageUtilCoreForDecoding()
        {
            var src = ReadViewModel("PortraitViewerViewModel.cs");
            Assert.Contains("ImageUtilCore.GetPalette", src);
            Assert.Contains("ImageUtilCore.LoadROMTiles4bpp", src);
        }

        // ---------------------------------------------------------------
        // EventCondViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void EventCondViewModel_ReadsMapSettingDatasize()
        {
            var src = ReadViewModel("EventCondViewModel.cs");
            Assert.Contains("rom.RomInfo.map_setting_datasize", src);
        }

        [Fact]
        public void EventCondViewModel_UsesMapSettingCore()
        {
            var src = ReadViewModel("EventCondViewModel.cs");
            Assert.Contains("MapSettingCore.MakeMapIDList()", src);
        }

        [Fact]
        public void EventCondViewModel_ReadsRawBytesFromRom()
        {
            var src = ReadViewModel("EventCondViewModel.cs");
            // Reads individual bytes for hex display
            Assert.Contains("rom.u8(addr + i)", src);
        }

        // ---------------------------------------------------------------
        // ArenaClassViewerViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void ArenaClassViewModel_ReadsFromArenaPointer()
        {
            var src = ReadViewModel("ArenaClassViewerViewModel.cs");
            Assert.Contains("rom.RomInfo.arena_class_near_weapon_pointer", src);
        }

        [Fact]
        public void ArenaClassViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("ArenaClassViewerViewModel.cs");
            // Arena class entries are 1 byte each (class ID)
            Assert.Contains("i * 1", src);
            Assert.Contains("rom.u8(addr)", src);
        }

        [Fact]
        public void ArenaClassViewModel_TerminatesOnZeroClassId()
        {
            var src = ReadViewModel("ArenaClassViewerViewModel.cs");
            Assert.Contains("classId == 0x00", src);
        }

        // ---------------------------------------------------------------
        // WorldMapPointViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void WorldMapPointViewModel_ReadsFromWorldMapPointer()
        {
            var src = ReadViewModel("WorldMapPointViewModel.cs");
            Assert.Contains("rom.RomInfo.worldmap_point_pointer", src);
        }

        [Fact]
        public void WorldMapPointViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("WorldMapPointViewModel.cs");
            // World map point entries are 32 bytes each
            Assert.Contains("i * 32", src);
            Assert.Contains("rom.u16(addr + 0)", src);   // X
            Assert.Contains("rom.u16(addr + 2)", src);   // Y
            Assert.Contains("rom.u16(addr + 28)", src);  // NameTextId
        }

        [Fact]
        public void WorldMapPointViewModel_ValidatesPointerFields()
        {
            var src = ReadViewModel("WorldMapPointViewModel.cs");
            // Validates internal pointer fields at offsets +12, +16, +20
            Assert.Contains("rom.u32(addr + 12)", src);
            Assert.Contains("rom.u32(addr + 16)", src);
            Assert.Contains("rom.u32(addr + 20)", src);
            Assert.Contains("U.isPointerOrNULL", src);
        }

        // ---------------------------------------------------------------
        // SoundRoomViewerViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void SoundRoomViewModel_ReadsFromSoundRoomPointer()
        {
            var src = ReadViewModel("SoundRoomViewerViewModel.cs");
            Assert.Contains("rom.RomInfo.sound_room_pointer", src);
            Assert.Contains("rom.RomInfo.sound_room_datasize", src);
        }

        [Fact]
        public void SoundRoomViewModel_ReadsCorrectFieldOffsets()
        {
            var src = ReadViewModel("SoundRoomViewerViewModel.cs");
            Assert.Contains("rom.u16(addr + 0)", src);   // SongId
            Assert.Contains("rom.u32(addr + 4)", src);   // Raw4
            Assert.Contains("rom.u32(addr + 8)", src);   // Raw8
            Assert.Contains("rom.u16(addr + 12)", src);  // TextId
        }

        // ---------------------------------------------------------------
        // MoveCostEditorViewModel
        // ---------------------------------------------------------------

        [Fact]
        public void MoveCostViewModel_ReadsFromClassPointer()
        {
            var src = ReadViewModel("MoveCostEditorViewModel.cs");
            Assert.Contains("rom.RomInfo.class_pointer", src);
            Assert.Contains("rom.RomInfo.class_datasize", src);
        }

        [Fact]
        public void MoveCostViewModel_HandlesVersionSpecificOffset()
        {
            var src = ReadViewModel("MoveCostEditorViewModel.cs");
            // Move cost pointer offset varies by version
            Assert.Contains("rom.RomInfo.version == 6", src);
            Assert.Contains("moveCostPtrOffset = 52", src);  // FE6
            Assert.Contains("moveCostPtrOffset = 48", src);  // FE7/FE8
        }

        [Fact]
        public void MoveCostViewModel_ReadsTerrainCosts()
        {
            var src = ReadViewModel("MoveCostEditorViewModel.cs");
            // Reads individual terrain move cost bytes
            Assert.Contains("rom.u8((uint)(moveCostAddr + i))", src);
        }

        // ---------------------------------------------------------------
        // Cross-cutting: all ViewModels perform bounds checking
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("UnitEditorViewModel.cs")]
        [InlineData("ItemEditorViewModel.cs")]
        [InlineData("ClassEditorViewModel.cs")]
        [InlineData("MapSettingViewModel.cs")]
        [InlineData("PortraitViewerViewModel.cs")]
        [InlineData("MoveCostEditorViewModel.cs")]
        public void ViewModel_PerformsBoundsChecking(string fileName)
        {
            var src = ReadViewModel(fileName);
            Assert.Contains("rom.Data.Length", src);
        }

        [Theory]
        [InlineData("UnitEditorViewModel.cs")]
        [InlineData("ItemEditorViewModel.cs")]
        [InlineData("ClassEditorViewModel.cs")]
        [InlineData("ItemWeaponEffectViewerViewModel.cs")]
        [InlineData("SongTableViewModel.cs")]
        [InlineData("PortraitViewerViewModel.cs")]
        [InlineData("ArenaClassViewerViewModel.cs")]
        [InlineData("WorldMapPointViewModel.cs")]
        [InlineData("SoundRoomViewerViewModel.cs")]
        public void ViewModel_ChecksRomInfoNull(string fileName)
        {
            var src = ReadViewModel(fileName);
            Assert.Contains("rom?.RomInfo", src);
        }

        [Theory]
        [InlineData("UnitEditorViewModel.cs")]
        [InlineData("ItemEditorViewModel.cs")]
        [InlineData("ClassEditorViewModel.cs")]
        [InlineData("SongTableViewModel.cs")]
        [InlineData("PortraitViewerViewModel.cs")]
        [InlineData("ArenaClassViewerViewModel.cs")]
        [InlineData("WorldMapPointViewModel.cs")]
        [InlineData("SoundRoomViewerViewModel.cs")]
        public void ViewModel_ValidatesBaseAddress(string fileName)
        {
            var src = ReadViewModel(fileName);
            Assert.Contains("U.isSafetyOffset(baseAddr)", src);
        }
    }
}
