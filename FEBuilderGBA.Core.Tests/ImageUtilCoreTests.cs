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

        [Fact]
        public void DecodeHeaderTSA_WithNoImageService_ReturnsNull()
        {
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var result = ImageUtilCore.DecodeHeaderTSA(
                    new byte[64], new byte[] { 2, 2, 0, 0, 0, 0 }, new byte[32], 4, 4);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void DecodeHeaderTSA_InvalidHeader_FallsBackToLinearTSA()
        {
            // Header values > 32 should be treated as invalid
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                // With no image service, both paths return null; this tests that
                // invalid headers don't crash
                var result = ImageUtilCore.DecodeHeaderTSA(
                    new byte[64], new byte[] { 33, 33, 0, 0 }, new byte[32], 4, 4);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void DecodeHeaderTSA_SmallData_DoesNotCrash()
        {
            // TSA data too small for header should not crash
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var result = ImageUtilCore.DecodeHeaderTSA(
                    new byte[64], new byte[1], new byte[32], 4, 4);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }
    }
}
