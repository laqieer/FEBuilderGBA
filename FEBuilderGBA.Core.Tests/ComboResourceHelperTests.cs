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

            uint[] expectedIds =
            {
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06,
                0x07, 0x09, 0x0B, 0x0C, 0x11, 0x12,
            };

            Assert.Equal(expectedIds, list.Select(x => x.id));
            Assert.All(list, entry =>
                Assert.StartsWith($"{U.ToHexString(entry.id)} ", entry.name));
        }

        [Fact]
        public void MakeWeaponTypeList_IncludesUnknownCurrentId()
        {
            var list = ComboResourceHelper.MakeWeaponTypeList(0x08);

            Assert.Equal(14, list.Count);
            Assert.Equal(0x08u, list[^1].id);
            Assert.StartsWith("08 ", list[^1].name);
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
