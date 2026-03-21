using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class MapSettingFE7DispatchTests
    {
        private static ROM MakeRom()
        {
            var rom = new ROM();
            var data = new byte[0x200_0000];
            rom.LoadLow("fake.gba", data, "AE7E01");
            return rom;
        }

        [Fact]
        public void FE7JP_MapSettingDataSize_Is148()
        {
            var rom = MakeRom();
            var info = new ROMFE7JP(rom);
            Assert.Equal(148u, info.map_setting_datasize);
        }

        [Fact]
        public void FE7U_MapSettingDataSize_Is152()
        {
            var rom = MakeRom();
            var info = new ROMFE7U(rom);
            Assert.Equal(152u, info.map_setting_datasize);
        }

        [Fact]
        public void DispatchLogic_FE7U_SelectsLargerView()
        {
            var rom = MakeRom();
            var info = new ROMFE7U(rom);
            // >= 152 should select the FE7U view
            Assert.True(info.map_setting_datasize >= 152);
        }

        [Fact]
        public void DispatchLogic_FE7JP_SelectsSmallerView()
        {
            var rom = MakeRom();
            var info = new ROMFE7JP(rom);
            // < 152 should select the FE7JP view
            Assert.True(info.map_setting_datasize < 152);
        }
    }
}
