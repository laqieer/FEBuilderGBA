using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class MapSettingFE7DispatchTests
    {
        [Fact]
        public void FE7JP_MapSettingDataSize_Is148()
        {
            var rom = new ROM();
            rom.LoadLow("fake.gba", new byte[0x200_0000], "AE7J01");
            Assert.Equal(148u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE7U_MapSettingDataSize_Is152()
        {
            var rom = new ROM();
            rom.LoadLow("fake.gba", new byte[0x200_0000], "AE7E01");
            Assert.Equal(152u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void IsFE7ULayout_Returns_True_For_152()
        {
            Assert.True(MapSettingCore.IsFE7ULayout(152));
        }

        [Fact]
        public void IsFE7ULayout_Returns_True_For_LargerSizes()
        {
            Assert.True(MapSettingCore.IsFE7ULayout(160));
        }

        [Fact]
        public void IsFE7ULayout_Returns_False_For_148()
        {
            Assert.False(MapSettingCore.IsFE7ULayout(148));
        }

        [Fact]
        public void IsFE7ULayout_Returns_False_For_SmallerSizes()
        {
            Assert.False(MapSettingCore.IsFE7ULayout(100));
        }

        [Fact]
        public void FE7JP_ROM_DispatchesToJPView()
        {
            var rom = new ROM();
            rom.LoadLow("fake.gba", new byte[0x200_0000], "AE7J01");
            Assert.False(MapSettingCore.IsFE7ULayout(rom.RomInfo.map_setting_datasize));
        }

        [Fact]
        public void FE7U_ROM_DispatchesToUView()
        {
            var rom = new ROM();
            rom.LoadLow("fake.gba", new byte[0x200_0000], "AE7E01");
            Assert.True(MapSettingCore.IsFE7ULayout(rom.RomInfo.map_setting_datasize));
        }
    }
}
