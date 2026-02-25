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
    }
}
