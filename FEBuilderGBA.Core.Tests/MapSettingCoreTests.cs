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

        // ---- Version data size tests ----

        [Fact]
        public void FE6_MapSettingDataSize_Is68Or72()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(6, rom.RomInfo.version);
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

        [Fact]
        public void MakeMapIDList_FE7U_ValidatesWithCorrectClearConditionOffsets()
        {
            // FE7U has clear conditions at offsets 0x8C/0x8E (140/142),
            // not 0x88/0x8A (136/138) like FE7JP/FE8.
            // D0 must be a non-pointer so the full validation path is exercised.
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // D0: non-pointer value to force the full validation path
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4, B6: non-zero PLISTs
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: weather (must be < 0xE)
                rom.Data[baseAddr + 12] = 0x01;
                // Map name texts at 0x70/0x72 - valid text IDs
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7U clear conditions at 0x8C/0x8E (140/142) - valid text IDs
                WriteU16(rom.Data, (int)(baseAddr + 0x8C), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x8E), 1);

                // Set up text pointer table so textmax > 0
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500); // text entry 0
                WriteU32(rom.Data, 0x404, 0x08000600); // text entry 1

                var list = MapSettingCore.MakeMapIDList();
                Assert.NotEmpty(list);
                Assert.Equal(baseAddr, list[0].addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_FE7U_RejectsInvalidClearConditionAt0x8C()
        {
            // If FE7U clear condition at offset 0x8C (140) is invalid, the entry should be rejected
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // Non-pointer D0 (forces validation path)
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4: valid PLIST
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: valid weather
                rom.Data[baseAddr + 12] = 0x01;
                // Map name texts at 0x70/0x72 - valid
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7U clear conditions at 0x8C - INVALID (huge text ID)
                WriteU16(rom.Data, (int)(baseAddr + 0x8C), 0xFFFF);
                WriteU16(rom.Data, (int)(baseAddr + 0x8E), 1);

                // Set up text pointer table
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500);
                WriteU32(rom.Data, 0x404, 0x08000600);

                var list = MapSettingCore.MakeMapIDList();
                // Entry should be rejected because clear condition at offset 0x8C is invalid
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
