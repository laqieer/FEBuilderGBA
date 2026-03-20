using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class CliLZ77Tests
    {
        [Fact]
        public void RoundTrip_SmallData()
        {
            byte[] original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                           0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };

            byte[] compressed = LZ77.compress(original);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
            Assert.Equal(0x10, compressed[0]); // LZ77 header marker

            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void RoundTrip_AllZeros()
        {
            byte[] original = new byte[256]; // all zeros

            byte[] compressed = LZ77.compress(original);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);

            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void RoundTrip_RepeatingPattern()
        {
            // Create a repeating 0x00-0xFF pattern (two full cycles = 512 bytes)
            byte[] original = new byte[512];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 256);

            byte[] compressed = LZ77.compress(original);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);

            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Decompress_InvalidData_ReturnsEmptyOrThrows()
        {
            // Garbage data that doesn't start with 0x10 header
            byte[] garbage = new byte[] { 0xFF, 0xAB, 0xCD, 0xEF, 0x12, 0x34 };

            // LZ77.decompress returns empty array for invalid data (header != 0x10)
            byte[] result = LZ77.decompress(garbage, 0);
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
