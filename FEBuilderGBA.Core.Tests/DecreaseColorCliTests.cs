using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DecreaseColorCliTests
    {
        [Fact]
        public void Quantize_WithSyntheticRGBA_ReturnsResult()
        {
            // Create a 4x4 image with 3 colors + transparency
            int w = 4, h = 4;
            byte[] rgba = new byte[w * h * 4];

            // Row 0: Red
            for (int x = 0; x < 4; x++)
            {
                rgba[(0 * w + x) * 4 + 0] = 255; // R
                rgba[(0 * w + x) * 4 + 3] = 255; // A
            }
            // Row 1: Green
            for (int x = 0; x < 4; x++)
            {
                rgba[(1 * w + x) * 4 + 1] = 255; // G
                rgba[(1 * w + x) * 4 + 3] = 255; // A
            }
            // Row 2: Blue
            for (int x = 0; x < 4; x++)
            {
                rgba[(2 * w + x) * 4 + 2] = 255; // B
                rgba[(2 * w + x) * 4 + 3] = 255; // A
            }
            // Row 3: Transparent (alpha=0)

            var result = DecreaseColorCore.Quantize(rgba, w, h, 16);
            Assert.NotNull(result);
            Assert.Equal(w, result.Width);
            Assert.Equal(h, result.Height);
            Assert.Equal(w * h, result.IndexData.Length);
            Assert.True(result.ColorCount >= 2); // At least transparent + 1 color
            Assert.True(result.ColorCount <= 16);
        }

        [Fact]
        public void Quantize_TransparentPixels_GetIndex0()
        {
            int w = 2, h = 2;
            byte[] rgba = new byte[w * h * 4]; // all zeros = transparent

            var result = DecreaseColorCore.Quantize(rgba, w, h, 16);
            Assert.NotNull(result);
            foreach (byte idx in result.IndexData)
                Assert.Equal(0, idx); // All transparent
        }

        [Fact]
        public void Quantize_NullInput_ReturnsNull()
        {
            var result = DecreaseColorCore.Quantize(null, 4, 4, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Quantize_TooSmallBuffer_ReturnsNull()
        {
            var result = DecreaseColorCore.Quantize(new byte[4], 4, 4, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Quantize_SingleColor_ReturnsValidPalette()
        {
            int w = 8, h = 8;
            byte[] rgba = new byte[w * h * 4];
            // Fill with a single color (orange)
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = 255; // R
                rgba[i * 4 + 1] = 128; // G
                rgba[i * 4 + 2] = 0;   // B
                rgba[i * 4 + 3] = 255; // A
            }

            var result = DecreaseColorCore.Quantize(rgba, w, h, 16);
            Assert.NotNull(result);
            Assert.Equal(2, result.ColorCount); // transparent + 1 color
            // All pixels should map to index 1
            foreach (byte idx in result.IndexData)
                Assert.Equal(1, idx);
        }

        [Fact]
        public void Quantize_GBAPalette_HasCorrectSize()
        {
            int w = 4, h = 4;
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = (byte)(i * 16);
                rgba[i * 4 + 1] = 128;
                rgba[i * 4 + 2] = 64;
                rgba[i * 4 + 3] = 255;
            }

            var result = DecreaseColorCore.Quantize(rgba, w, h, 16);
            Assert.NotNull(result);
            // GBA palette = 2 bytes per color
            Assert.Equal(result.ColorCount * 2, result.GBAPalette.Length);
        }
    }
}
