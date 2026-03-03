using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class MapConvertCoreTests
    {
        [Fact]
        public void ConvertImage_ValidInput_ReturnsResult()
        {
            // Create an 8x8 solid red image
            int w = 8, h = 8;
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = 255; // R
                rgba[i * 4 + 1] = 0;   // G
                rgba[i * 4 + 2] = 0;   // B
                rgba[i * 4 + 3] = 255; // A
            }

            var result = MapConvertCore.ConvertImage(rgba, w, h);
            Assert.NotNull(result);
            Assert.Equal(1, result.WidthTiles);
            Assert.Equal(1, result.HeightTiles);
            Assert.True(result.TileCount >= 1);
            Assert.NotNull(result.TileData);
            Assert.NotNull(result.TSAData);
            Assert.NotNull(result.PaletteData);
        }

        [Fact]
        public void ConvertImage_16x16_ReturnsFourTileSlots()
        {
            int w = 16, h = 16;
            byte[] rgba = new byte[w * h * 4];
            // Fill with opaque red
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = 200;
                rgba[i * 4 + 3] = 255;
            }

            var result = MapConvertCore.ConvertImage(rgba, w, h);
            Assert.NotNull(result);
            Assert.Equal(2, result.WidthTiles);
            Assert.Equal(2, result.HeightTiles);
            // TSA data should have 4 entries (2x2 tiles), each 2 bytes
            Assert.Equal(8, result.TSAData.Length);
        }

        [Fact]
        public void ConvertImage_IdenticalTiles_Deduplicated()
        {
            // Create 16x8 image where both halves are identical
            int w = 16, h = 8;
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    rgba[i * 4 + 0] = 128; // Same color everywhere
                    rgba[i * 4 + 1] = 64;
                    rgba[i * 4 + 2] = 32;
                    rgba[i * 4 + 3] = 255;
                }
            }

            var result = MapConvertCore.ConvertImage(rgba, w, h);
            Assert.NotNull(result);
            // Both tiles should be identical, so only 1 unique tile
            Assert.Equal(1, result.TileCount);
        }

        [Fact]
        public void ConvertImage_NullInput_ReturnsNull()
        {
            Assert.Null(MapConvertCore.ConvertImage(null, 8, 8));
        }

        [Fact]
        public void ConvertImage_NonMultipleOf8_ReturnsNull()
        {
            byte[] rgba = new byte[7 * 8 * 4];
            Assert.Null(MapConvertCore.ConvertImage(rgba, 7, 8));
        }

        [Fact]
        public void ConvertImage_TooSmallBuffer_ReturnsNull()
        {
            Assert.Null(MapConvertCore.ConvertImage(new byte[4], 8, 8));
        }

        [Fact]
        public void ConvertImage_TileDataIs4bpp()
        {
            int w = 8, h = 8;
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = 255;
                rgba[i * 4 + 3] = 255;
            }

            var result = MapConvertCore.ConvertImage(rgba, w, h);
            Assert.NotNull(result);
            // 4bpp: 32 bytes per 8x8 tile
            Assert.Equal(result.TileCount * 32, result.TileData.Length);
        }
    }
}
