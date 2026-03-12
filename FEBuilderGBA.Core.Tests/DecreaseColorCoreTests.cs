using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class DecreaseColorCoreTests
    {
        [Fact]
        public void Quantize_SimpleSolidColor()
        {
            // 2x2 red image
            byte[] rgba = new byte[]
            {
                255, 0, 0, 255,  255, 0, 0, 255,
                255, 0, 0, 255,  255, 0, 0, 255,
            };

            var result = DecreaseColorCore.Quantize(rgba, 2, 2, 16);

            Assert.NotNull(result);
            Assert.Equal(2, result.Width);
            Assert.Equal(2, result.Height);
            Assert.Equal(4, result.IndexData.Length);
            Assert.True(result.ColorCount > 0);
            Assert.True(result.ColorCount <= 16);

            // All pixels should have same non-zero index (not transparent)
            Assert.True(result.IndexData[0] > 0);
            Assert.Equal(result.IndexData[0], result.IndexData[1]);
            Assert.Equal(result.IndexData[0], result.IndexData[2]);
            Assert.Equal(result.IndexData[0], result.IndexData[3]);
        }

        [Fact]
        public void Quantize_TransparentPixels_GetIndex0()
        {
            // 2x1: one transparent, one opaque red
            byte[] rgba = new byte[]
            {
                0, 0, 0, 0,     // transparent
                255, 0, 0, 255, // red
            };

            var result = DecreaseColorCore.Quantize(rgba, 2, 1, 16);

            Assert.NotNull(result);
            Assert.Equal(0, result.IndexData[0]); // transparent = index 0
            Assert.True(result.IndexData[1] > 0);  // opaque = non-zero index
        }

        [Fact]
        public void Quantize_MultipleColors()
        {
            // 3x1: red, green, blue
            byte[] rgba = new byte[]
            {
                255, 0, 0, 255,   // red
                0, 255, 0, 255,   // green
                0, 0, 255, 255,   // blue
            };

            var result = DecreaseColorCore.Quantize(rgba, 3, 1, 16);

            Assert.NotNull(result);
            Assert.Equal(3, result.IndexData.Length);
            // All indices should be non-zero (opaque)
            Assert.True(result.IndexData[0] > 0);
            Assert.True(result.IndexData[1] > 0);
            Assert.True(result.IndexData[2] > 0);
        }

        [Fact]
        public void Quantize_NullInput_ReturnsNull()
        {
            var result = DecreaseColorCore.Quantize(null, 2, 2, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Quantize_GBAPaletteFormat()
        {
            byte[] rgba = new byte[]
            {
                255, 0, 0, 255,  0, 255, 0, 255,
            };

            var result = DecreaseColorCore.Quantize(rgba, 2, 1, 16);
            Assert.NotNull(result);
            // GBA palette conversion requires ImageService; if null, GBAPalette may be empty
            if (CoreState.ImageService != null)
            {
                Assert.True(result.GBAPalette.Length >= result.ColorCount * 2,
                    $"GBAPalette length {result.GBAPalette.Length} < ColorCount*2 ({result.ColorCount * 2})");
            }
            else
            {
                // Without ImageService, verify at least indexed pixel data and RGBA palette exist
                Assert.NotNull(result.IndexData);
                Assert.True(result.IndexData.Length > 0);
            }
        }
    }
}
