using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapSettingCoreTests
    {
        [Fact]
        public void MakeMapIDList_WithNoRom_ReturnsEmpty()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = MapSettingCore.MakeMapIDList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetMapAddr_WithNoRom_ReturnsNotFound()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                uint addr = MapSettingCore.GetMapAddr(0);
                Assert.Equal(U.NOT_FOUND, addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_WithPointerBackedMapEntry_ReturnsEntry()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000200);
                WriteU32(rom.Data, 0x200, 0x08000300); // WinForms treats a pointer here as valid.
                WriteU32(rom.Data, 0x204, 1);
                WriteU32(rom.Data, 0x208, 1);
                rom.Data[0x20C] = 0x00;

                var list = MapSettingCore.MakeMapIDList();

                Assert.Single(list);
                Assert.Equal((uint)0x200, list[0].addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ---- FE6-specific version data size tests ----

        [Fact]
        public void FE6_MapSettingDataSize_Is68Or72()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(6, rom.RomInfo.version);
            // FE6 data size is either 68 or 72 depending on the world map event table
            Assert.True(rom.RomInfo.map_setting_datasize == 68 || rom.RomInfo.map_setting_datasize == 72,
                $"FE6 map_setting_datasize should be 68 or 72 but was {rom.RomInfo.map_setting_datasize}");
        }

        [Fact]
        public void FE8U_MapSettingDataSize_Is148()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(8, rom.RomInfo.version);
            Assert.Equal(148u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE7JP_MapSettingDataSize_Is148()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7J01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(7, rom.RomInfo.version);
            Assert.Equal(148u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE7U_MapSettingDataSize_Is152()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(7, rom.RomInfo.version);
            Assert.Equal(152u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE6_IsMultibyte_True()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.True(rom.RomInfo.is_multibyte);
        }

        [Fact]
        public void FE7U_IsMultibyte_False()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
            Assert.False(rom.RomInfo.is_multibyte);
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
