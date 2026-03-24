using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the NormalizeHex helper used in --data-verify field-level cross-checks.
    /// Ensures signed byte values (e.g. promotion gains) compare correctly against
    /// unsigned ROM bytes.
    /// </summary>
    public class NormalizeHexValueTests
    {
        [Theory]
        [InlineData("0xFF", "0XFF")]       // Already hex, uppercased
        [InlineData("0x25", "0X25")]       // Already hex
        [InlineData("0x0001", "0X0001")]   // 4-digit hex preserved
        [InlineData("0x00000000", "0X00000000")] // 8-digit hex
        public void HexInput_NormalizedToUpperCase(string input, string expected)
        {
            Assert.Equal(expected, DataVerifiableSweepTests.NormalizeHex(input));
        }

        [Theory]
        [InlineData("0", "0x00")]
        [InlineData("37", "0x25")]         // PromoHp=37 should match raw 0x25
        [InlineData("255", "0xFF")]
        [InlineData("128", "0x80")]
        [InlineData("1", "0x01")]
        public void PositiveDecimal_ConvertedToHexByte(string input, string expected)
        {
            Assert.Equal(expected, DataVerifiableSweepTests.NormalizeHex(input));
        }

        [Theory]
        [InlineData("-1", "0xFF")]         // signed byte -1 = unsigned 0xFF
        [InlineData("-128", "0x80")]       // signed byte -128 = unsigned 0x80
        [InlineData("-2", "0xFE")]
        [InlineData("-100", "0x9C")]       // -100 signed = 0x9C unsigned
        public void NegativeDecimal_TreatedAsSignedByte(string input, string expected)
        {
            Assert.Equal(expected, DataVerifiableSweepTests.NormalizeHex(input));
        }

        [Theory]
        [InlineData("256", "0x00000100")]  // Out of byte range, 8-digit hex
        [InlineData("1000", "0x000003E8")]
        public void LargePositiveDecimal_ConvertedTo8DigitHex(string input, string expected)
        {
            Assert.Equal(expected, DataVerifiableSweepTests.NormalizeHex(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void EmptyOrNull_ReturnedAsIs(string? input)
        {
            Assert.Equal(input, DataVerifiableSweepTests.NormalizeHex(input!));
        }

        [Fact]
        public void NonNumericString_ReturnedUnchanged()
        {
            Assert.Equal("abc", DataVerifiableSweepTests.NormalizeHex("abc"));
        }

        /// <summary>
        /// Regression: signed promo value 37 (decimal) must match u8 raw 0x25.
        /// This was the exact case from ClassEditorView b34_PromoHp.
        /// </summary>
        [Fact]
        public void PromoHpRegression_37MatchesHex25()
        {
            string dataVal = "37";         // from GetDataReport: $"{PromoHp}"
            string rawVal = "0x25";        // from GetRawRomReport: $"0x{rom.u8(a+34):X02}"

            string normData = DataVerifiableSweepTests.NormalizeHex(dataVal);
            string normRaw = DataVerifiableSweepTests.NormalizeHex(rawVal);

            Assert.True(
                string.Equals(normData, normRaw, System.StringComparison.OrdinalIgnoreCase),
                $"Expected decimal 37 and hex 0x25 to match after normalization: normData={normData}, normRaw={normRaw}");
        }

        /// <summary>
        /// Regression: signed promo value -1 must match u8 raw 0xFF.
        /// </summary>
        [Fact]
        public void NegativePromoRegression_Minus1MatchesHexFF()
        {
            string dataVal = "-1";
            string rawVal = "0xFF";

            string normData = DataVerifiableSweepTests.NormalizeHex(dataVal);
            string normRaw = DataVerifiableSweepTests.NormalizeHex(rawVal);

            Assert.True(
                string.Equals(normData, normRaw, System.StringComparison.OrdinalIgnoreCase),
                $"Expected decimal -1 and hex 0xFF to match: normData={normData}, normRaw={normRaw}");
        }
    }
}
