using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for DecreaseColorCore.Quantize sub-flags:
    /// noScale, noReserve1stColor, ignoreTSA.
    /// </summary>
    public class DecreaseColorSubFlagTests
    {
        private static byte[] MakeSolidRGBA(int w, int h, byte r, byte g, byte b)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }

        [Fact]
        public void NoReserve1stColor_AllPixelsSameIndex()
        {
            // With noReserve1stColor=true, slot 0 is not reserved for transparency.
            // All opaque pixels of the same color should map to index 0.
            byte[] rgba = MakeSolidRGBA(2, 2, 255, 0, 0);
            var result = DecreaseColorCore.Quantize(rgba, 2, 2, 16, noReserve1stColor: true);
            Assert.NotNull(result);
            // All pixels should be index 0 (first color, since no transparent reservation)
            Assert.Equal(0, result.IndexData[0]);
            Assert.Equal(0, result.IndexData[1]);
            Assert.Equal(0, result.IndexData[2]);
            Assert.Equal(0, result.IndexData[3]);
        }

        [Fact]
        public void NoReserve1stColor_ColorCountHasNoTransparentSlot()
        {
            byte[] rgba = MakeSolidRGBA(2, 2, 100, 200, 50);
            var resultDefault = DecreaseColorCore.Quantize(rgba, 2, 2, 16);
            var resultNoReserve = DecreaseColorCore.Quantize(rgba, 2, 2, 16, noReserve1stColor: true);

            Assert.NotNull(resultDefault);
            Assert.NotNull(resultNoReserve);
            // Default: 1 (transparent) + 1 (color) = 2
            Assert.Equal(2, resultDefault.ColorCount);
            // NoReserve: just 1 (color), no transparent slot
            Assert.Equal(1, resultNoReserve.ColorCount);
        }

        [Fact]
        public void DefaultFlags_ReserveFirstColor()
        {
            // Default: slot 0 reserved for transparency, so opaque pixels start at index 1
            byte[] rgba = MakeSolidRGBA(2, 2, 128, 64, 32);
            var result = DecreaseColorCore.Quantize(rgba, 2, 2, 16);
            Assert.NotNull(result);
            Assert.Equal(1, result.IndexData[0]); // First opaque pixel at index 1
        }

        [Fact]
        public void NoScale_ProducesValidGBAPalette()
        {
            byte[] rgba = MakeSolidRGBA(2, 2, 255, 0, 0);
            var result = DecreaseColorCore.Quantize(rgba, 2, 2, 16, noScale: true);
            Assert.NotNull(result);
            // GBA palette should still be valid (2 bytes per color)
            Assert.Equal(result.ColorCount * 2, result.GBAPalette.Length);
        }

        [Fact]
        public void NoScale_PaletteStillConvertedToGBAFormat()
        {
            // With noScale, colors are still converted to 16-bit GBA format
            byte[] rgba = MakeSolidRGBA(1, 1, 248, 0, 0); // Almost pure red
            var result = DecreaseColorCore.Quantize(rgba, 1, 1, 16, noScale: true);
            Assert.NotNull(result);
            Assert.True(result.GBAPalette.Length >= 2);
        }

        [Fact]
        public void IgnoreTSA_ProducesResult()
        {
            // ignoreTSA flag should not break basic quantization
            byte[] rgba = new byte[]
            {
                255, 0, 0, 255,   0, 255, 0, 255,
                0, 0, 255, 255,   255, 255, 0, 255,
            };
            var result = DecreaseColorCore.Quantize(rgba, 2, 2, 16, ignoreTSA: true);
            Assert.NotNull(result);
            Assert.Equal(4, result.IndexData.Length);
        }

        [Fact]
        public void AllFlags_Combined_ProducesResult()
        {
            byte[] rgba = MakeSolidRGBA(4, 4, 100, 150, 200);
            var result = DecreaseColorCore.Quantize(rgba, 4, 4, 16,
                noScale: true, noReserve1stColor: true, ignoreTSA: true);
            Assert.NotNull(result);
            Assert.Equal(16, result.IndexData.Length);
        }

        [Fact]
        public void NoReserve1stColor_TransparentPixelsMappedToIndex0()
        {
            // Even without reservation, transparent pixels map to index 0
            byte[] rgba = new byte[]
            {
                0, 0, 0, 0,       // transparent
                255, 0, 0, 255,   // red
            };
            var result = DecreaseColorCore.Quantize(rgba, 2, 1, 16, noReserve1stColor: true);
            Assert.NotNull(result);
            Assert.Equal(0, result.IndexData[0]); // transparent
        }
    }
}
