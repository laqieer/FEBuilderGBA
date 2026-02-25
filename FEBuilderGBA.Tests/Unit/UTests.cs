namespace FEBuilderGBA.Tests.Unit
{
    public class UTests
    {
        #region At Methods - Array Access

        [Fact]
        public void At_ByteArray_WithValidIndex_ReturnsValue()
        {
            // Arrange
            byte[] array = { 10, 20, 30 };

            // Act
            var result = U.at(array, 1);

            // Assert
            Assert.Equal(20, result);
        }

        [Fact]
        public void At_ByteArray_WithInvalidIndex_ReturnsDefault()
        {
            // Arrange
            byte[] array = { 10, 20, 30 };

            // Act
            var result = U.at(array, 10, (byte)99);

            // Assert
            Assert.Equal(99, result);
        }

        [Fact]
        public void At_StringArray_WithValidIndex_ReturnsValue()
        {
            // Arrange
            string[] array = { "foo", "bar", "baz" };

            // Act
            var result = U.at(array, 1);

            // Assert
            Assert.Equal("bar", result);
        }

        [Fact]
        public void At_StringArray_WithInvalidIndex_ReturnsDefault()
        {
            // Arrange
            string[] array = { "foo", "bar" };

            // Act
            var result = U.at(array, 5, "default");

            // Assert
            Assert.Equal("default", result);
        }

        [Fact]
        public void At_UintArray_WithNegativeIndex_ReturnsDefault()
        {
            // Arrange
            uint[] array = { 1, 2, 3 };

            // Act
            var result = U.at(array, -1, 999u);

            // Assert
            Assert.Equal(999u, result);
        }

        #endregion

        #region String Parsing Methods

        [Theory]
        [InlineData("0x10", 16u)]
        [InlineData("0xFF", 255u)]
        [InlineData("0x0", 0u)]
        [InlineData("0x100", 256u)]
        [InlineData("$FF", 255u)]
        [InlineData("$10", 16u)]
        [InlineData("42", 42u)]
        [InlineData("100", 100u)]
        public void Atoi0x_WithValidInput_ReturnsCorrectValue(string input, uint expected)
        {
            // Act
            var result = U.atoi0x(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("invalid")]
        [InlineData("0x")]
        [InlineData("$")]
        public void Atoi0x_WithInvalidInput_ReturnsZero(string input)
        {
            // Act
            var result = U.atoi0x(input);

            // Assert
            Assert.Equal(0u, result);
        }

        [Theory]
        [InlineData("42", 42u)]
        [InlineData("100", 100u)]
        [InlineData("0", 0u)]
        [InlineData("42abc", 42u)] // Should parse up to non-digit
        [InlineData("123xyz", 123u)]
        public void Atoi_WithValidInput_ReturnsCorrectValue(string input, uint expected)
        {
            // Act
            var result = U.atoi(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        public void Atoi_WithInvalidInput_ReturnsZero(string input)
        {
            // Act
            var result = U.atoi(input);

            // Assert
            Assert.Equal(0u, result);
        }

        [Theory]
        [InlineData("FF", 255u)]
        [InlineData("10", 16u)]
        [InlineData("ABCD", 0xABCDu)]
        [InlineData("0", 0u)]
        [InlineData("FFxyz", 255u)] // Should parse up to non-hex
        public void Atoh_WithValidInput_ReturnsCorrectValue(string input, uint expected)
        {
            // Act
            var result = U.atoh(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("2147483812", 2147483812u)]
        [InlineData("4294967295", 4294967295u)] // Max uint
        [InlineData("0", 0u)]
        [InlineData("100abc", 100u)] // Should parse up to non-digit
        public void Atou_WithValidInput_ReturnsCorrectValue(string input, uint expected)
        {
            // Act
            var result = U.atou(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1.5", 1.5)]
        [InlineData("42", 42.0)]
        [InlineData("3.14159", 3.14159)]
        [InlineData("0", 0.0)]
        [InlineData("12.5abc", 12.5)] // Should parse up to non-float char
        public void Atof_WithValidInput_ReturnsCorrectValue(string input, double expected)
        {
            // Act
            var result = U.atof(input);

            // Assert
            Assert.Equal(expected, result, 0.00001);
        }

        #endregion

        #region Hex String Conversion

        [Theory]
        [InlineData(0x10u, "10")]
        [InlineData(0xFFu, "FF")]
        [InlineData(0x100u, "0100")]
        [InlineData(0xFFFFu, "FFFF")]
        [InlineData(0x10000u, "010000")]
        [InlineData(0xFFFFFFu, "FFFFFF")]
        [InlineData(0x1000000u, "01000000")]
        [InlineData(0xFFFFFFFFu, "FFFFFFFF")]
        public void ToHexString_WithUint_ReturnsCorrectFormat(uint value, string expected)
        {
            // Act
            var result = U.ToHexString(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x10u, "0x10")]
        [InlineData(0xFFu, "0xFF")]
        [InlineData(0x1234u, "0x1234")]
        public void To0xHexString_WithUint_ReturnsWithPrefix(uint value, string expected)
        {
            // Act
            var result = U.To0xHexString(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x10, "10")]
        [InlineData(0xFF, "FF")]
        [InlineData(0x1234, "1234")]
        public void ToHexString2_ReturnsFixedWidth2(int value, string expected)
        {
            // Act
            var result = U.ToHexString2(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x10, "0010")]
        [InlineData(0xFFFF, "FFFF")]
        [InlineData(0x1234, "1234")]
        public void ToHexString4_ReturnsFixedWidth4(int value, string expected)
        {
            // Act
            var result = U.ToHexString4(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x10, "00000010")]
        [InlineData(0xFFFFFFFF, "FFFFFFFF")]
        [InlineData(0x12345678, "12345678")]
        public void ToHexString8_ReturnsFixedWidth8(uint value, string expected)
        {
            // Act
            var result = U.ToHexString8(value);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Even/Odd Checks

        [Theory]
        [InlineData(0, true)]
        [InlineData(2, true)]
        [InlineData(4, true)]
        [InlineData(100, true)]
        [InlineData(1, false)]
        [InlineData(3, false)]
        [InlineData(99, false)]
        public void IsEven_WithInt_ReturnsCorrectResult(int value, bool expected)
        {
            // Act
            var result = U.isEven(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, true)]
        [InlineData(2u, true)]
        [InlineData(1u, false)]
        [InlineData(3u, false)]
        public void IsEven_WithUint_ReturnsCorrectResult(uint value, bool expected)
        {
            // Act
            var result = U.isEven(value);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region IsEmpty

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("   ", false)] // Not empty, contains whitespace
        [InlineData("test", false)]
        public void IsEmpty_WithString_ReturnsCorrectResult(string value, bool expected)
        {
            // Act
            var result = U.IsEmpty(value);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Offset Conversion

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 1u)]
        [InlineData(0x100u, 0x100u)]
        [InlineData(0x08000000u, 0u)] // Pointer to offset
        [InlineData(0x08000100u, 0x100u)] // Pointer to offset
        [InlineData(0x08FFFFFFu, 0xFFFFFFu)] // Pointer to offset
        public void ToOffset_WithPointerOrOffset_ReturnsOffset(uint input, uint expected)
        {
            // Act
            var result = U.toOffset(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 1u)]
        [InlineData(0x100u, 0x08000100u)]
        [InlineData(0xFFFFu, 0x0800FFFFu)]
        public void ToPointer_WithOffset_ReturnsPointer(uint input, uint expected)
        {
            // Act
            var result = U.toPointer(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x08000000u, true)]
        [InlineData(0x08000100u, true)]
        [InlineData(0x09FFFFFFu, true)]
        [InlineData(0x07FFFFFFu, false)]
        [InlineData(0x0A000000u, false)]
        [InlineData(0x100u, false)]
        public void IsPointer_WithValue_ReturnsCorrectResult(uint value, bool expected)
        {
            // Act
            var result = U.isPointer(value);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Padding and Alignment Methods

        [Theory]
        [InlineData(0u, true)]
        [InlineData(4u, true)]
        [InlineData(8u, true)]
        [InlineData(12u, true)]
        [InlineData(1u, false)]
        [InlineData(3u, false)]
        [InlineData(5u, false)]
        [InlineData(7u, false)]
        public void IsPadding4_ReturnsCorrectResult(uint value, bool expected)
        {
            // Act
            var result = U.isPadding4(value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(4u, 4u)]
        [InlineData(5u, 4u)]
        [InlineData(6u, 4u)]
        [InlineData(7u, 4u)]
        [InlineData(8u, 8u)]
        [InlineData(11u, 8u)]
        [InlineData(15u, 12u)]
        public void SubPadding4_RoundsDownToFourByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.SubPadding4(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 2u)]
        [InlineData(2u, 2u)]
        [InlineData(3u, 4u)]
        [InlineData(4u, 4u)]
        [InlineData(5u, 6u)]
        [InlineData(10u, 10u)]
        [InlineData(11u, 12u)]
        public void Padding2_RoundsUpToTwoByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.Padding2(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 0u)]
        [InlineData(2u, 2u)]
        [InlineData(3u, 2u)]
        [InlineData(4u, 4u)]
        [InlineData(5u, 4u)]
        [InlineData(10u, 10u)]
        [InlineData(11u, 10u)]
        public void Padding2Before_RoundsDownToTwoByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.Padding2Before(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 4u)]
        [InlineData(2u, 4u)]
        [InlineData(3u, 4u)]
        [InlineData(4u, 4u)]
        [InlineData(5u, 8u)]
        [InlineData(6u, 8u)]
        [InlineData(7u, 8u)]
        [InlineData(8u, 8u)]
        [InlineData(15u, 16u)]
        [InlineData(16u, 16u)]
        [InlineData(17u, 20u)]
        public void Padding4_Uint_RoundsUpToFourByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.Padding4(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 4)]
        [InlineData(2, 4)]
        [InlineData(3, 4)]
        [InlineData(4, 4)]
        [InlineData(5, 8)]
        [InlineData(15, 16)]
        [InlineData(16, 16)]
        [InlineData(17, 20)]
        public void Padding4_Int_RoundsUpToFourByteBoundary(int input, int expected)
        {
            // Act
            var result = U.Padding4(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 8u)]
        [InlineData(7u, 8u)]
        [InlineData(8u, 8u)]
        [InlineData(9u, 16u)]
        [InlineData(15u, 16u)]
        [InlineData(16u, 16u)]
        [InlineData(17u, 24u)]
        [InlineData(23u, 24u)]
        [InlineData(24u, 24u)]
        public void Padding8_Uint_RoundsUpToEightByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.Padding8(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 8)]
        [InlineData(7, 8)]
        [InlineData(8, 8)]
        [InlineData(9, 16)]
        [InlineData(15, 16)]
        [InlineData(16, 16)]
        [InlineData(17, 24)]
        public void Padding8_Int_RoundsUpToEightByteBoundary(int input, int expected)
        {
            // Act
            var result = U.Padding8(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(1u, 16u)]
        [InlineData(15u, 16u)]
        [InlineData(16u, 16u)]
        [InlineData(17u, 32u)]
        [InlineData(31u, 32u)]
        [InlineData(32u, 32u)]
        [InlineData(33u, 48u)]
        [InlineData(47u, 48u)]
        [InlineData(48u, 48u)]
        public void Padding16_Uint_RoundsUpToSixteenByteBoundary(uint input, uint expected)
        {
            // Act
            var result = U.Padding16(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 16)]
        [InlineData(15, 16)]
        [InlineData(16, 16)]
        [InlineData(17, 32)]
        [InlineData(31, 32)]
        [InlineData(32, 32)]
        [InlineData(33, 48)]
        public void Padding16_Int_RoundsUpToSixteenByteBoundary(int input, int expected)
        {
            // Act
            var result = U.Padding16(input);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion
    }
}
