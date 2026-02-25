namespace FEBuilderGBA.Tests.Unit
{
    public class LZ77Tests
    {
        [Fact]
        public void IsCompress_WithNonLZ77Data_ReturnsFalse()
        {
            // Arrange - Data not starting with 0x10
            byte[] data = { 0x00, 0x01, 0x02, 0x03 };

            // Act
            var result = LZ77.iscompress(data, 0);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsCompress_WithInvalidSize_ReturnsFalse()
        {
            // Arrange - Starts with 0x10 but size is invalid (< 3)
            byte[] data = { 0x10, 0x02, 0x00, 0x00 };

            // Act
            var result = LZ77.iscompress(data, 0);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsCompress_WithOffsetBeyondArray_ReturnsFalse()
        {
            // Arrange
            byte[] data = { 0x10, 0x05, 0x00, 0x00 };

            // Act
            var result = LZ77.iscompress(data, 100);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetUncompressSize_WithInvalidHeader_ReturnsZero()
        {
            // Arrange - Data not starting with 0x10
            byte[] data = { 0x00, 0x01, 0x02, 0x03 };

            // Act
            var result = LZ77.getUncompressSize(data, 0);

            // Assert
            Assert.Equal(0u, result);
        }

        [Fact]
        public void GetUncompressSize_WithValidHeader_ReturnsSize()
        {
            // Arrange - Valid LZ77 header with size = 0x100 (256 bytes)
            byte[] data = { 0x10, 0x00, 0x01, 0x00 };

            // Act
            var result = LZ77.getUncompressSize(data, 0);

            // Assert
            Assert.Equal(0x100u, result);
        }

        [Fact]
        public void GetUncompressSize_WithSizeTooSmall_ReturnsZero()
        {
            // Arrange - Size < 3
            byte[] data = { 0x10, 0x02, 0x00, 0x00 };

            // Act
            var result = LZ77.getUncompressSize(data, 0);

            // Assert
            Assert.Equal(0u, result);
        }

        [Fact]
        public void GetUncompressSize_WithSizeTooLarge_ReturnsZero()
        {
            // Arrange - Size > MAX_UNCOMP_DATA_LIMIT (0x14000)
            byte[] data = { 0x10, 0x01, 0x40, 0x01 }; // 0x014001 > 0x14000

            // Act
            var result = LZ77.getUncompressSize(data, 0);

            // Assert
            Assert.Equal(0u, result);
        }

        [Fact]
        public void GetUncompressSize_WithOffset_ReadsFromCorrectPosition()
        {
            // Arrange - Valid header at offset 4
            byte[] data = { 0x00, 0x00, 0x00, 0x00, 0x10, 0x80, 0x00, 0x00 };

            // Act
            var result = LZ77.getUncompressSize(data, 4);

            // Assert
            Assert.Equal(0x80u, result);
        }

        [Fact]
        public void GetCompressedSize_WithInvalidHeader_ReturnsZero()
        {
            // Arrange
            byte[] data = { 0x00, 0x01, 0x02, 0x03 };

            // Act
            var result = LZ77.getCompressedSize(data, 0);

            // Assert
            Assert.Equal(0u, result);
        }

        [Fact]
        public void GetCompressedSize_WithValidButIncompleteData_ReturnsZero()
        {
            // Arrange - Valid header but not enough data following
            byte[] data = { 0x10, 0x10, 0x00, 0x00, 0xFF }; // Says 16 bytes uncompressed but incomplete

            // Act
            var result = LZ77.getCompressedSize(data, 0);

            // Assert - Should return 0 because data is incomplete
            Assert.Equal(0u, result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(10)]
        public void GetUncompressSize_WithInsufficientDataLength_ReturnsZero(uint offset)
        {
            // Arrange - Array too small
            byte[] data = { 0x10, 0x05 }; // Only 2 bytes

            // Act
            var result = LZ77.getUncompressSize(data, offset);

            // Assert
            Assert.Equal(0u, result);
        }

        [Fact]
        public void Decompress_WithNullInput_ThrowsException()
        {
            // Arrange
            byte[] nullInput = null;

            // Act & Assert - LZ77.decompress throws NullReferenceException for null input
            Assert.Throws<NullReferenceException>(() => LZ77.decompress(nullInput, 0));
        }

        [Fact]
        public void Decompress_WithInvalidData_ReturnsEmptyOrInvalid()
        {
            // Arrange - Invalid LZ77 data (not starting with 0x10)
            byte[] data = { 0x00, 0x01, 0x02, 0x03 };

            // Act
            var result = LZ77.decompress(data, 0);

            // Assert - May return empty array or small array depending on implementation
            Assert.NotNull(result);
        }

        [Fact]
        public void Compress_WithNullInput_ThrowsException()
        {
            // Arrange
            byte[] nullInput = null;

            // Act & Assert - LZ77.compress throws NullReferenceException for null input
            Assert.Throws<NullReferenceException>(() => LZ77.compress(nullInput));
        }

        [Fact]
        public void Compress_WithEmptyArray_ReturnsData()
        {
            // Arrange
            byte[] emptyArray = Array.Empty<byte>();

            // Act
            var result = LZ77.compress(emptyArray);

            // Assert - LZ77 returns header even for empty data
            Assert.NotNull(result);
            Assert.True(result.Length >= 4); // At least has LZ77 header
        }

        [Fact]
        public void CompressDecompress_RoundTrip_PreservesData()
        {
            // Arrange - Simple pattern that should compress well
            byte[] original = new byte[100];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = (byte)(i % 16);
            }

            // Act
            var compressed = LZ77.compress(original);
            var decompressed = compressed != null ? LZ77.decompress(compressed, 0) : null;

            // Assert
            Assert.NotNull(compressed);
            Assert.NotNull(decompressed);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void CompressDecompress_WithRepeatingData_CompressesEfficiently()
        {
            // Arrange - Highly repeating data should compress well
            byte[] original = new byte[256];
            Array.Fill(original, (byte)0xAA);

            // Act
            var compressed = LZ77.compress(original);
            var decompressed = compressed != null ? LZ77.decompress(compressed, 0) : null;

            // Assert
            Assert.NotNull(compressed);
            Assert.NotNull(decompressed);
            Assert.True(compressed.Length < original.Length, "Compressed size should be smaller");
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void CompressDecompress_WithRandomData_PreservesData()
        {
            // Arrange - Random data may not compress well
            byte[] original = new byte[64];
            var random = new Random(12345); // Use seed for reproducibility
            random.NextBytes(original);

            // Act
            var compressed = LZ77.compress(original);
            var decompressed = compressed != null ? LZ77.decompress(compressed, 0) : null;

            // Assert
            Assert.NotNull(compressed);
            Assert.NotNull(decompressed);
            Assert.Equal(original, decompressed);
        }
    }
}
