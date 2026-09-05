using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class IndexedPngReaderFilterTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ReconstructIndexedScanlines_SupportsStandardFilters(int filter)
        {
            const int width = 5;
            const int height = 3;
            byte[] expected =
            {
                0, 1, 2, 3, 4,
                5, 6, 7, 8, 9,
                10, 11, 12, 13, 14,
            };
            byte[] raw = EncodeFiltered(expected, width, height, filter);

            byte[] actual = IndexedPngReader.ReconstructIndexedScanlines(
                raw, width, height, out bool unsupported);

            Assert.False(unsupported);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ReconstructIndexedScanlines_UnknownFilter_IsUnavailable()
        {
            byte[] raw = { 5, 0 };

            byte[] actual = IndexedPngReader.ReconstructIndexedScanlines(
                raw, 1, 1, out bool unsupported);

            Assert.True(unsupported);
            Assert.Null(actual);
        }

        static byte[] EncodeFiltered(
            byte[] pixels, int width, int height, int filter)
        {
            var raw = new byte[height * (width + 1)];
            for (int y = 0; y < height; y++)
            {
                int rawRow = y * (width + 1);
                int sourceRow = y * width;
                raw[rawRow] = (byte)filter;
                for (int x = 0; x < width; x++)
                {
                    int value = pixels[sourceRow + x];
                    int left = x > 0 ? pixels[sourceRow + x - 1] : 0;
                    int up = y > 0 ? pixels[sourceRow - width + x] : 0;
                    int upLeft = x > 0 && y > 0
                        ? pixels[sourceRow - width + x - 1]
                        : 0;
                    int predictor = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => PaethPredictor(left, up, upLeft),
                        _ => 0,
                    };
                    raw[rawRow + 1 + x] = unchecked((byte)(value - predictor));
                }
            }
            return raw;
        }

        static int PaethPredictor(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int leftDistance = Math.Abs(estimate - left);
            int upDistance = Math.Abs(estimate - up);
            int upperLeftDistance = Math.Abs(estimate - upLeft);
            if (leftDistance <= upDistance && leftDistance <= upperLeftDistance)
                return left;
            return upDistance <= upperLeftDistance ? up : upLeft;
        }
    }
}
