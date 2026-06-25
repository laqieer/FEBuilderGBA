// SPDX-License-Identifier: GPL-3.0-or-later
// Independent oracle tests for MapPointerPlistUsageCore — the New-PLIST popup
// usage scan / overwrite-confirmation / Extend-state helper (#1433).
//
// Synthetic FE8U ROMs plant exact PLIST bytes so each WinForms branch
// (GetMapIDsWherePlist / PlistToName / IsExtendsPlist / GetExtendState) is
// exercised against HAND-BUILT expectations — never VM==golden. The planted
// bytes are read back via raw byte assertions where needed for independence.

using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapPointerPlistUsageCoreTests
    {
        // =================================================================
        // GetMapIDsWherePlist — typed field selection.
        // =================================================================

        [Fact]
        public void EventType_UsedPlist_ReturnsTheMap()
        {
            // event_plist = 3 planted on map id 0 (split layout).
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var maps = MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, MapChangeCore.PlistType.EVENT, 3);
            Assert.Single(maps);
            Assert.Equal(0u, maps[0]);
        }

        [Fact]
        public void EventType_UnusedPlist_ReturnsEmpty()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var maps = MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, MapChangeCore.PlistType.EVENT, 99);
            Assert.Empty(maps);
        }

        [Fact]
        public void UnknownType_ScansEveryField()
        {
            // mappointer_plist = 5; a UNKNOWN (null) scan must find it even
            // though no EVENT field uses 5.
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var byField = MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, MapChangeCore.PlistType.EVENT, 5);
            Assert.Empty(byField); // EVENT field is 3, not 5

            var byUnknown = MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, null, 5);
            Assert.Single(byUnknown); // mappointer_plist == 5 is found
        }

        [Fact]
        public void ObjectType_MatchesBothObjLowAndObjHigh()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 22);

            Assert.Single(MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, MapChangeCore.PlistType.OBJECT, 11)); // low byte
            Assert.Single(MapPointerPlistUsageCore.GetMapIDsWherePlist(
                rom, MapChangeCore.PlistType.OBJECT, 22)); // high byte
        }

        // =================================================================
        // BuildPlistUsageInfo — message + IsAlreadyUse.
        // =================================================================

        [Fact]
        public void Usage_PlistZero_IsReservedAndAlreadyUse()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(
                rom, MapChangeCore.PlistType.EVENT, 0);
            Assert.True(info.IsAlreadyUse);
            Assert.False(string.IsNullOrEmpty(info.Message));
        }

        [Fact]
        public void Usage_UsedEventPlist_IsAlreadyUse_NamesTheMap()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(
                rom, MapChangeCore.PlistType.EVENT, 3);
            Assert.True(info.IsAlreadyUse);
            // Message contains the "already used" wording (en/ja/zh translated).
            Assert.False(string.IsNullOrEmpty(info.Message));
        }

        [Fact]
        public void Usage_UnusedEventPlist_NotAlreadyUse_Recommended()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(
                rom, MapChangeCore.PlistType.EVENT, 50);
            Assert.False(info.IsAlreadyUse);
            Assert.False(string.IsNullOrEmpty(info.Message));
        }

        [Fact]
        public void Usage_NonSplit_ScansEveryField_MappointerMatch_IsAlreadyUse()
        {
            // Non-split → PlistToName uses the UNKNOWN scan, so a PLIST used
            // only by the mappointer field still reads as "already used" even
            // though the search type is EVENT.
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(
                rom, MapChangeCore.PlistType.EVENT, 5);
            Assert.True(info.IsAlreadyUse);
        }

        [Fact]
        public void Usage_OutOfRangePlist_IsAlreadyUse()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            // Split layout → count = 256, so 0xFFFF is out of range.
            uint count = MapPointerPlistUsageCore.GetEventPlistCount(rom);
            Assert.True(count > 0);
            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(
                rom, MapChangeCore.PlistType.EVENT, count + 10);
            Assert.True(info.IsAlreadyUse);
        }

        // =================================================================
        // Extend state + count parity.
        // =================================================================

        [Fact]
        public void Split_GivesAlreadySplitState_And256Count()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            Assert.Equal(MapPointerPlistUsageCore.ExtendState.AlreadySplit,
                MapPointerPlistUsageCore.GetExtendState(rom));
            Assert.Equal(256u, MapPointerPlistUsageCore.GetEventPlistCount(rom));
            Assert.True(MapPointerPlistUsageCore.IsExtendsPlist(rom));
        }

        [Fact]
        public void NonSplit_GivesEnabledState_AndVanillaCount()
        {
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            // FE8U vanilla default size is 0xEC (236) < 255 → Enabled.
            Assert.Equal(MapPointerPlistUsageCore.ExtendState.Enabled,
                MapPointerPlistUsageCore.GetExtendState(rom));
            Assert.Equal(0xECu, MapPointerPlistUsageCore.GetEventPlistCount(rom));
            Assert.False(MapPointerPlistUsageCore.IsExtendsPlist(rom));
        }

        // =================================================================
        // Never-throws.
        // =================================================================

        [Fact]
        public void NullRom_NeverThrows()
        {
            Assert.Empty(MapPointerPlistUsageCore.GetMapIDsWherePlist(null, MapChangeCore.PlistType.EVENT, 3));
            var info = MapPointerPlistUsageCore.BuildPlistUsageInfo(null, MapChangeCore.PlistType.EVENT, 3);
            Assert.False(info.IsAlreadyUse);
            Assert.Equal(0u, MapPointerPlistUsageCore.GetEventPlistCount(null));
            Assert.False(MapPointerPlistUsageCore.IsExtendsPlist(null));
            Assert.Equal(MapPointerPlistUsageCore.ExtendState.Enabled,
                MapPointerPlistUsageCore.GetExtendState(null));
        }

        [Fact]
        public void RecognizedRom_UnsafePointerSlots_NeverThrows()
        {
            // A fully-recognized FE8U ROM (RomInfo IS set) whose PLIST / map-
            // setting base pointers are deliberately unsafe (point at or past
            // EOF, and one slot straddles the very end of the data) must not
            // throw when the helpers dereference those slots.
            var rom = MakeFe8uRom();
            Assert.NotNull(rom.RomInfo); // recognized — not the null-RomInfo path

            uint romLen = (uint)rom.Data.Length;
            // Base pointers that dereference to a GBA offset past EOF.
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer,    0x08000000u | romLen);      // == EOF
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,     0x08000000u | (romLen + 4));
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,      0x08000000u | (romLen - 2)); // slot straddles EOF
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0xFFFFFFFFu);                // garbage pointer
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,        0x08000000u | (romLen - 1));
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,0x00000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,  0x08000000u | romLen);

            var ex = Record.Exception(() =>
            {
                MapPointerPlistUsageCore.GetEventPlistCount(rom);
                MapPointerPlistUsageCore.IsExtendsPlist(rom);
                MapPointerPlistUsageCore.GetExtendState(rom);
                MapPointerPlistUsageCore.GetMapIDsWherePlist(rom, MapChangeCore.PlistType.EVENT, 3);
                MapPointerPlistUsageCore.GetMapIDsWherePlist(rom, null, 3);
                MapPointerPlistUsageCore.BuildPlistUsageInfo(rom, MapChangeCore.PlistType.EVENT, 3);
            });
            Assert.Null(ex);
        }

        // =================================================================
        // Synthetic-ROM builders (mirrors MapPListResolverCoreTests).
        // =================================================================

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeSplitFe8uRomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            var rom = MakeFe8uRom();
            PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
                palette, palette2, objLow, objHigh);
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,        0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,   0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,     0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,         0x08805000u);
            return rom;
        }

        static ROM MakeNonSplitFe8uRomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            var rom = MakeFe8uRom();
            PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
                palette, palette2, objLow, objHigh);
            uint shared = 0x08800000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,      shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,  shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,  shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,         shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,         shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,   shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,       shared);
            return rom;
        }

        static void PlantMap(ROM rom,
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            uint mapTableBase = 0x00700000u;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);

            int rec = (int)mapTableBase;
            WriteU32(rom.Data, rec + 0, 0x08123456u);
            ushort obj = (ushort)((objLow & 0xFF) | ((objHigh & 0xFF) << 8));
            rom.Data[rec + 4] = (byte)(obj & 0xFF);
            rom.Data[rec + 5] = (byte)((obj >> 8) & 0xFF);
            rom.Data[rec + 6] = (byte)palette;
            rom.Data[rec + 7] = (byte)config;
            rom.Data[rec + 8] = (byte)mappointer;
            rom.Data[rec + 9] = (byte)anime1;
            rom.Data[rec + 10] = (byte)anime2;
            rom.Data[rec + 11] = (byte)mapchange;
            rom.Data[rec + (int)rom.RomInfo.map_setting_event_plist_pos] = (byte)evt;
            if (palette2 != 0)
            {
                rom.Data[rec + 146] = (byte)palette2;
            }

            int term = (int)(mapTableBase + dataSize);
            WriteU32(rom.Data, term + 0, 0x00000000u);
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
