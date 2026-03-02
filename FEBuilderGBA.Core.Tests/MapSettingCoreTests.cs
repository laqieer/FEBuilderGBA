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
    }
}
