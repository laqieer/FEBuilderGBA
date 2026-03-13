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

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
