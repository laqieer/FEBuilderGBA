using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class MultiByteJPUtilTests
    {
        #region mb_convert_kana Tests

        [Fact]
        public void MbConvertKana_WithOptionR_ConvertsFullWidthAlphaToHalfWidth()
        {
            // Arrange - Full-width A becomes half-width A
            string input = "ＡＢＣ";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "r");

            // Assert
            Assert.Equal("ABC", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperR_ConvertsHalfWidthAlphaToFullWidth()
        {
            // Arrange
            string input = "ABC";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "R");

            // Assert
            Assert.Equal("ＡＢＣ", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionN_ConvertsFullWidthNumbersToHalfWidth()
        {
            // Arrange - Full-width numbers
            string input = "１２３４５";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "n");

            // Assert
            Assert.Equal("12345", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperN_ConvertsHalfWidthNumbersToFullWidth()
        {
            // Arrange
            string input = "12345";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "N");

            // Assert
            Assert.Equal("１２３４５", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionA_ConvertsFullWidthAlphanumericToHalfWidth()
        {
            // Arrange - Full-width alphanumeric
            string input = "ＡＢＣ１２３";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "a");

            // Assert
            Assert.Equal("ABC123", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperA_ConvertsHalfWidthAlphanumericToFullWidth()
        {
            // Arrange
            string input = "ABC123";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "A");

            // Assert
            Assert.Equal("ＡＢＣ１２３", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionS_ConvertsFullWidthSpaceToHalfWidth()
        {
            // Arrange - Full-width space (U+3000)
            string input = "A　B";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "s");

            // Assert
            Assert.Equal("A B", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperS_ConvertsHalfWidthSpaceToFullWidth()
        {
            // Arrange - Half-width space
            string input = "A B";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "S");

            // Assert
            Assert.Equal("A　B", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionK_ConvertsHalfWidthKatakanaToFullWidth()
        {
            // Arrange - Half-width katakana
            string input = "ｱｲｳｴｵ";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "K");

            // Assert
            Assert.Equal("アイウエオ", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionLowerK_ConvertsFullWidthKatakanaToHalfWidth()
        {
            // Arrange - Full-width katakana
            string input = "アイウエオ";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "k");

            // Assert
            Assert.Equal("ｱｲｳｴｵ", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionC_ConvertsKatakanaToHiragana()
        {
            // Arrange - Katakana
            string input = "アイウエオ";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "c");

            // Assert
            Assert.Equal("あいうえお", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperC_ConvertsHiraganaToKatakana()
        {
            // Arrange - Hiragana
            string input = "あいうえお";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "C");

            // Assert
            Assert.Equal("アイウエオ", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionH_ConvertsHiraganaToHalfWidthKatakana()
        {
            // Arrange - Hiragana
            string input = "あいうえお";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "h");

            // Assert
            Assert.Equal("ｱｲｳｴｵ", result);
        }

        [Fact]
        public void MbConvertKana_WithOptionUpperH_ConvertsHalfWidthKatakanaToHiragana()
        {
            // Arrange - Half-width katakana
            string input = "ｱｲｳｴｵ";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "H");

            // Assert
            Assert.Equal("あいうえお", result);
        }

        [Fact]
        public void MbConvertKana_WithEmptyString_ReturnsEmpty()
        {
            // Act
            var result = MultiByteJPUtil.mb_convert_kana("", "a");

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void MbConvertKana_WithNoMatchingCharacters_ReturnsUnchanged()
        {
            // Arrange - Characters not in conversion tables
            string input = "xyz";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "K");

            // Assert
            Assert.Equal("xyz", result);
        }

        [Fact]
        public void MbConvertKana_WithMultipleOptions_AppliesAll()
        {
            // Arrange - Full-width alphanumeric with space
            string input = "ＡＢＣ　１２３";

            // Act - Convert both alphanumeric and space
            var result = MultiByteJPUtil.mb_convert_kana(input, "as");

            // Assert
            Assert.Equal("ABC 123", result);
        }

        [Fact]
        public void MbConvertKana_WithMixedContent_ConvertsOnlyMatchingCharacters()
        {
            // Arrange - Mix of convertible and non-convertible
            string input = "Ａｂｃ123";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "a");

            // Assert - Only full-width chars converted
            Assert.Equal("Abc123", result);
        }

        #endregion

        #region ConvertKanaToNumber Tests

        [Fact]
        public void ConvertKanaToNumber_WithValidHiraganaHex_ReturnsHexString()
        {
            // Arrange - ぬふあ = 123
            string input = "ぬふあ";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert
            Assert.Equal("123", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithHiraganaHexAF_ReturnsHexString()
        {
            // Arrange - ちこそしいは = ABCDEF
            string input = "ちこそしいは";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert
            Assert.Equal("ABCDEF", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithZero_ReturnsZero()
        {
            // Arrange - わ = 0
            string input = "わ";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert
            Assert.Equal("0", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithSpaces_ReturnsEmptyIfNotValidHex()
        {
            // Arrange - Space breaks hex validity
            string input = "ぬ　ふ";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert - Result "1 2" is not valid hex, returns empty
            Assert.Equal("", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithInvalidResult_ReturnsEmpty()
        {
            // Arrange - Characters that don't form valid hex
            string input = "かきく";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert - Result is not a valid hex string, returns empty
            Assert.Equal("", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithEmptyString_ReturnsEmpty()
        {
            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber("");

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithMixedValidInvalid_ReturnsEmptyIfNotHex()
        {
            // Arrange - Mix that doesn't form valid hex
            string input = "ぬかふ";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert
            Assert.Equal("", result);
        }

        #endregion

        #region ConvertNarrowFontToAlpha Tests

        [Fact]
        public void ConvertNarrowFontToAlpha_WithNarrowFontPattern_RemovesBrackets()
        {
            // Arrange - [_A] pattern
            string input = "[_A][_B][_C]";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert
            Assert.Equal("ABC", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithNumbers_RemovesBrackets()
        {
            // Arrange
            string input = "[_1][_2][_3]";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert
            Assert.Equal("123", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithLowercase_RemovesBrackets()
        {
            // Arrange
            string input = "[_a][_b][_c]";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert
            Assert.Equal("abc", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithMixedContent_OnlyConvertsPatterns()
        {
            // Arrange - Mix of patterns and regular text
            string input = "Test[_A]normal[_1]text";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert
            Assert.Equal("TestAnormal1text", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithoutPattern_ReturnsUnchanged()
        {
            // Arrange
            string input = "Normal text without patterns";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert
            Assert.Equal("Normal text without patterns", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithEmptyString_ReturnsEmpty()
        {
            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha("");

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void ConvertNarrowFontToAlpha_WithInvalidPattern_LeavesUnchanged()
        {
            // Arrange - Pattern with special char (not alphanumeric)
            string input = "[_!][_@]";

            // Act
            var result = MultiByteJPUtil.ConvertNarrowFontToAlpha(input);

            // Assert - Pattern not matched, should remain
            Assert.Equal("[_!][_@]", result);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void MbConvertKana_WithSpecialCharacters_ConvertsCorrectly()
        {
            // Arrange - Full-width parentheses
            string input = "（）";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "a");

            // Assert
            Assert.Equal("()", result);
        }

        [Fact]
        public void MbConvertKana_WithFullWidthSlash_ConvertsToHalfWidth()
        {
            // Arrange
            string input = "／";

            // Act
            var result = MultiByteJPUtil.mb_convert_kana(input, "a");

            // Assert
            Assert.Equal("/", result);
        }

        [Fact]
        public void ConvertKanaToNumber_WithAllDigits_ReturnsAllDigits()
        {
            // Arrange - わぬふあうえおやゆよ = 0123456789
            string input = "わぬふあうえおやゆよ";

            // Act
            var result = MultiByteJPUtil.ConvertKanaToNumber(input);

            // Assert
            Assert.Equal("0123456789", result);
        }

        #endregion
    }
}
