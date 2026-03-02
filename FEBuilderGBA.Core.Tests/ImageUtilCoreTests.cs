using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class ImageUtilCoreTests
    {
        [Fact]
        public void GetPalette_WithNoRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var palette = ImageUtilCore.GetPalette(0, 16);
                Assert.Null(palette);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void LoadROMTiles4bpp_WithNoImageService_ReturnsNull()
        {
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var image = ImageUtilCore.LoadROMTiles4bpp(0, new byte[32], 1, 1);
                Assert.Null(image);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }
    }
}
