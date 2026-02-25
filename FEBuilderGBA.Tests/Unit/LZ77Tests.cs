namespace FEBuilderGBA.Tests.Unit
{
    public class LZ77Tests
    {
        #region Test Data Helpers

        /// <summary>
        /// Known test vector from TEST_LZ77() method
        /// Input: "abrakataabraxyz" (15 bytes)
        /// </summary>
        private byte[] GetKnownTestInput()
        {
            // Exact copy from LZ77.TEST_LZ77()
            byte[] a = new byte[15];
            a[0] = (byte)'a';
            a[1] = (byte)'b';
            a[2] = (byte)'r';
            a[3] = (byte)'a';
            a[4] = (byte)'k';
            a[5] = (byte)'a';
            a[6] = (byte)'t';
            a[7] = (byte)'a';
            a[8] = (byte)'b';  // Position 8 is 'b', not 'a'!
            a[9] = (byte)'r';
            a[10] = (byte)'a';
            a[11] = (byte)'x';
            a[12] = (byte)'y';
            a[13] = (byte)'z';
            a[14] = (byte)'a';
            return a;
        }

        /// <summary>
        /// Expected compressed output for "abrakataabraxyz"
        /// From LZ77.TEST_LZ77() method
        /// </summary>
        private byte[] GetKnownTestCompressed()
        {
            // Exact copy from LZ77.TEST_LZ77()
            byte[] z_ans = new byte[20];
            z_ans[0] = (byte)0x10;
            z_ans[1] = (byte)0x0F;
            z_ans[2] = (byte)0x00;
            z_ans[3] = (byte)0x00;
            z_ans[4] = (byte)0x01;
            z_ans[5] = (byte)0x61;
            z_ans[6] = (byte)0x62;
            z_ans[7] = (byte)0x72;
            z_ans[8] = (byte)0x61;
            z_ans[9] = (byte)0x6B;
            z_ans[10] = (byte)0x61;
            z_ans[11] = (byte)0x74;
            z_ans[12] = (byte)0x10;
            z_ans[13] = (byte)0x06;
            z_ans[14] = (byte)0x00;
            z_ans[15] = (byte)0x78;
            z_ans[16] = (byte)0x79;
            z_ans[17] = (byte)0x7A;
            z_ans[18] = (byte)0x61;
            z_ans[19] = (byte)0x00;
            return z_ans;
        }

        #endregion

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

        #region Known Test Vector Tests

        [Fact]
        public void IsCompress_WithValidCompressedData_ReturnsTrue()
        {
            // Arrange
            var compressedData = GetKnownTestCompressed();

            // Act
            var result = LZ77.iscompress(compressedData, 0);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GetUncompressSize_WithKnownTestVector_Returns15()
        {
            // Arrange
            var compressedData = GetKnownTestCompressed();

            // Act
            var result = LZ77.getUncompressSize(compressedData, 0);

            // Assert
            Assert.Equal(15u, result); // "abrakataabraxyz" is 15 bytes
        }

        [Fact]
        public void GetCompressedSize_WithKnownTestVector_ReturnsCorrectSize()
        {
            // Arrange
            var compressedData = GetKnownTestCompressed();

            // Act
            var result = LZ77.getCompressedSize(compressedData, 0);

            // Assert
            // getCompressedSize returns actual data length (19) without trailing padding byte
            Assert.Equal(19u, result);
        }

        [Fact]
        public void Decompress_WithKnownTestVector_ReturnsOriginalData()
        {
            // Arrange
            var compressed = GetKnownTestCompressed();
            var expected = GetKnownTestInput();

            // Act
            var result = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(expected.Length, result.Length);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Compress_WithKnownTestVector_ProducesValidOutput()
        {
            // Arrange
            var input = GetKnownTestInput();

            // Act
            var result = LZ77.compress(input);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0x10, result[0]); // Magic byte
            Assert.Equal(input.Length, result[1] | (result[2] << 8) | (result[3] << 16)); // Size
            Assert.Equal(0, result.Length % 4); // 4-byte aligned
        }

        [Fact]
        public void RoundTrip_KnownTestVector_PreservesData()
        {
            // Arrange
            var originalData = GetKnownTestInput();

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(originalData, decompressed);
        }

        #endregion

        #region GetCompressDataLow Tests

        [Fact]
        public void GetCompressDataLow_WithValidData_ReturnsCompressedData()
        {
            // Arrange
            var data = GetKnownTestCompressed();

            // Act
            var result = LZ77.GetCompressDataLow(data, 0);

            // Assert
            // GetCompressDataLow returns 19 bytes (actual compressed data, no padding)
            Assert.Equal(19, result.Length);
            // Verify it starts with correct header and data
            Assert.Equal(0x10, result[0]); // Magic byte
            Assert.Equal(0x0F, result[1]); // Size = 15
        }

        [Fact]
        public void GetCompressDataLow_WithInvalidData_ReturnsEmpty()
        {
            // Arrange
            var data = new byte[] { 0x00, 0x01, 0x02 };

            // Act
            var result = LZ77.GetCompressDataLow(data, 0);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetCompressDataLow_WithOffset_ExtractsCorrectData()
        {
            // Arrange
            var padding = new byte[] { 0xAA, 0xBB, 0xCC };
            var compressed = GetKnownTestCompressed();
            var dataWithPadding = new byte[padding.Length + compressed.Length];
            Array.Copy(padding, 0, dataWithPadding, 0, padding.Length);
            Array.Copy(compressed, 0, dataWithPadding, padding.Length, compressed.Length);

            // Act
            var result = LZ77.GetCompressDataLow(dataWithPadding, (uint)padding.Length);

            // Assert
            // Returns 19 bytes (without trailing padding)
            Assert.Equal(19, result.Length);
            Assert.Equal(0x10, result[0]); // Magic byte
            Assert.Equal(0x0F, result[1]); // Size = 15
        }

        [Fact]
        public void GetCompressDataLow_WithCompressedOutput_ExtractsCorrectly()
        {
            // Arrange
            var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var compressed = LZ77.compress(original);

            // Act
            var result = LZ77.GetCompressDataLow(compressed, 0);

            // Assert
            // Result should be valid compressed data (may not include trailing padding)
            Assert.True(result.Length > 0);
            Assert.True(result.Length <= compressed.Length);
            Assert.Equal(0x10, result[0]); // Magic byte
            Assert.Equal((byte)original.Length, result[1]); // Size
        }

        #endregion

        #region Decompress Offset Tests

        [Fact]
        public void Decompress_WithOffset_DecompressesCorrectly()
        {
            // Arrange
            var padding = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            var compressed = GetKnownTestCompressed();
            var dataWithPadding = new byte[padding.Length + compressed.Length];
            Array.Copy(padding, 0, dataWithPadding, 0, padding.Length);
            Array.Copy(compressed, 0, dataWithPadding, padding.Length, compressed.Length);
            var expected = GetKnownTestInput();

            // Act
            var result = LZ77.decompress(dataWithPadding, (uint)padding.Length);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Decompress_WithEmptyInput_ReturnsEmptyArray()
        {
            // Arrange
            var data = new byte[0];

            // Act
            var result = LZ77.decompress(data, 0);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Decompress_WithTooSmallSize_ReturnsEmptyArray()
        {
            // Arrange
            var data = new byte[] { 0x10, 0x02, 0x00, 0x00 }; // Size = 2 (less than 3)

            // Act
            var result = LZ77.decompress(data, 0);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Decompress_WithInvalidMagicByte_ReturnsEmptyArray()
        {
            // Arrange
            var data = new byte[] { 0x00, 0x10, 0x00, 0x00 };

            // Act
            var result = LZ77.decompress(data, 0);

            // Assert
            Assert.Empty(result);
        }

        #endregion

        #region Compression Output Validation Tests

        [Fact]
        public void Compress_OutputHasValidHeader()
        {
            // Arrange
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            // Act
            var result = LZ77.compress(data);

            // Assert
            Assert.True(result.Length >= 4);
            Assert.Equal(0x10, result[0]); // Magic byte
            Assert.Equal((byte)data.Length, result[1]); // Size (low byte)
        }

        [Fact]
        public void Compress_OutputIs4ByteAligned()
        {
            // Arrange - Various sizes
            var testSizes = new[] { 1, 3, 5, 7, 10, 15, 20, 33 };

            foreach (var size in testSizes)
            {
                var data = new byte[size];
                for (int i = 0; i < size; i++)
                {
                    data[i] = (byte)(i & 0xFF);
                }

                // Act
                var result = LZ77.compress(data);

                // Assert
                Assert.Equal(0, result.Length % 4); // Must be 4-byte aligned
            }
        }

        [Fact]
        public void Compress_CompressedOutputIsValidLZ77()
        {
            // Arrange
            var data = new byte[] { 0x11, 0x22, 0x33, 0x44 };

            // Act
            var compressed = LZ77.compress(data);

            // Assert - Compressed output should be recognized as LZ77
            Assert.True(LZ77.iscompress(compressed, 0));
            Assert.Equal((uint)data.Length, LZ77.getUncompressSize(compressed, 0));
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void RoundTrip_WithTextData_PreservesData()
        {
            // Arrange
            var text = "The quick brown fox jumps over the lazy dog";
            var originalData = System.Text.Encoding.ASCII.GetBytes(text);

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(originalData, decompressed);
        }

        [Fact]
        public void RoundTrip_WithAllZeros_PreservesData()
        {
            // Arrange - Highly compressible
            var originalData = new byte[100]; // All zeros

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(originalData, decompressed);
            Assert.True(compressed.Length < originalData.Length); // Should compress well
        }

        [Fact]
        public void RoundTrip_WithSingleByte_CannotDecompress()
        {
            // Arrange - LZ77 has minimum size of 3 bytes for decompression
            var originalData = new byte[] { 0x42 };

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert - decompress() rejects size < 3, returns empty array
            Assert.Empty(decompressed);
        }

        [Fact]
        public void RoundTrip_WithThreeBytes_PreservesData()
        {
            // Arrange - Minimum valid size
            var originalData = new byte[] { 0x01, 0x02, 0x03 };

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(originalData, decompressed);
        }

        [Fact]
        public void RoundTrip_WithSequentialData_PreservesData()
        {
            // Arrange
            var originalData = new byte[128];
            for (int i = 0; i < originalData.Length; i++)
            {
                originalData[i] = (byte)i;
            }

            // Act
            var compressed = LZ77.compress(originalData);
            var decompressed = LZ77.decompress(compressed, 0);

            // Assert
            Assert.Equal(originalData, decompressed);
        }

        [Fact]
        public void GetUncompressSize_AfterCompression_ReturnsOriginalSize()
        {
            // Arrange
            var originalData = new byte[42];
            for (int i = 0; i < originalData.Length; i++)
            {
                originalData[i] = (byte)(i * 3);
            }
            var compressed = LZ77.compress(originalData);

            // Act
            var result = LZ77.getUncompressSize(compressed, 0);

            // Assert
            Assert.Equal((uint)originalData.Length, result);
        }

        [Fact]
        public void GetCompressedSize_AfterCompression_ReturnsCompressedLength()
        {
            // Arrange
            var originalData = new byte[50];
            for (int i = 0; i < originalData.Length; i++)
            {
                originalData[i] = (byte)(i % 7);
            }
            var compressed = LZ77.compress(originalData);

            // Act
            var result = LZ77.getCompressedSize(compressed, 0);

            // Assert
            Assert.Equal((uint)compressed.Length, result);
        }

        #endregion
    }
}
