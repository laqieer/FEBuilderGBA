using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class LZ77Tests
    {
        [Fact]
        public void CompressDecompress_Roundtrip()
        {
            byte[] original = new byte[256];
            for (int i = 0; i < 256; i++)
                original[i] = (byte)(i % 16); // repetitive data for compression

            byte[] compressed = LZ77.compress(original);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);

            // LZ77 header byte should be 0x10
            Assert.Equal(0x10, compressed[0]);

            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void CompressDecompress_AllZeros()
        {
            byte[] original = new byte[128];
            byte[] compressed = LZ77.compress(original);
            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void CompressDecompress_SmallData()
        {
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] compressed = LZ77.compress(original);
            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original, decompressed);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void GetCompressedSize_ShortHeaderAtEnd_ReturnsZeroWithoutThrow(int remaining)
        {
            byte[] data = new byte[8];
            uint offset = (uint)(data.Length - remaining);
            data[offset] = 0x10;

            var ex = Record.Exception(() =>
            {
                uint size = LZ77.getCompressedSize(data, offset);
                Assert.Equal(0u, size);
            });

            Assert.Null(ex);
        }
    }
}
