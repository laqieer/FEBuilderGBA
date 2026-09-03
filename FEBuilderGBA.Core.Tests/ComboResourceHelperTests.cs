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
        public void MakeWeaponTypeList_ReturnsCanonicalSparseEntries()
        {
            var list = ComboResourceHelper.MakeWeaponTypeList();

            var expected = new (uint id, string name)[]
            {
                (0x00, "00 Sword"),
                (0x01, "01 Lance"),
                (0x02, "02 Axe"),
                (0x03, "03 Bow"),
                (0x04, "04 Staff"),
                (0x05, "05 Anima"),
                (0x06, "06 Light"),
                (0x07, "07 Dark"),
                (0x09, "09 Item"),
                (0x0B, "0B Dragonstone/Monster Weapon"),
                (0x0C, "0C Ring"),
                (0x11, "11 Dragonstone"),
                (0x12, "12 Dancer's Ring"),
            };

            Assert.Equal(expected, list);
        }

        [Fact]
        public void MakeUnitList_NoRom_ReturnsEmpty()
        {
            ROM? oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var list = ComboResourceHelper.MakeUnitList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom!;
            }
        }

        [Fact]
        public void MakeClassList_NoRom_ReturnsEmpty()
        {
            ROM? oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var list = ComboResourceHelper.MakeClassList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom!;
            }
        }

        [Fact]
        public void MakeItemList_NoRom_ReturnsEmpty()
        {
            ROM? oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var list = ComboResourceHelper.MakeItemList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom!;
            }
        }

        [Fact]
        public void MakeSongList_NoRom_ReturnsEmpty()
        {
            ROM? oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var list = ComboResourceHelper.MakeSongList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = oldRom!;
            }
        }
    }
}
