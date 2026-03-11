using Xunit;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ComboResourceHelperTests
    {
        [Fact]
        public void MakeAffinityList_ReturnsExpectedEntries()
        {
            var list = ComboResourceHelper.MakeAffinityList();
            Assert.Equal(8, list.Count);
            Assert.Contains("Fire", list[1].name);
            Assert.Equal(0u, list[0].id);
        }

        [Fact]
        public void MakeWeaponTypeList_ReturnsExpectedEntries()
        {
            var list = ComboResourceHelper.MakeWeaponTypeList();
            Assert.Equal(9, list.Count);
            Assert.Contains("Sword", list[0].name);
            Assert.Contains("Staff", list[4].name);
        }

        [Fact]
        public void MakeUnitList_NoRom_ReturnsEmpty()
        {
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = ComboResourceHelper.MakeUnitList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom;
            }
        }

        [Fact]
        public void MakeClassList_NoRom_ReturnsEmpty()
        {
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = ComboResourceHelper.MakeClassList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom;
            }
        }

        [Fact]
        public void MakeItemList_NoRom_ReturnsEmpty()
        {
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = ComboResourceHelper.MakeItemList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom;
            }
        }

        [Fact]
        public void MakeSongList_NoRom_ReturnsEmpty()
        {
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = ComboResourceHelper.MakeSongList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom;
            }
        }
    }
}
