using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class NumericInputParserTests
{
    [Theory]
    [InlineData("0", 0u)]
    [InlineData("66", 66u)]
    [InlineData("255", 255u)]
    [InlineData("0x00", 0u)]
    [InlineData("0x42", 66u)]
    [InlineData("0XFF", 255u)]
    [InlineData(" 0x2a ", 42u)]
    public void TryParseUInt32_AcceptsDecimalOrPrefixedHex(string text, uint expected)
    {
        Assert.True(NumericInputParser.TryParseUInt32(text, 0, 255, out uint actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("FF")]
    [InlineData("0x")]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("0x100")]
    [InlineData("12.5")]
    public void TryParseUInt32_RejectsAmbiguousMalformedOrOutOfRangeText(string? text)
    {
        Assert.False(NumericInputParser.TryParseUInt32(text, 0, 255, out _));
    }

    [Fact]
    public void FormatHexByte_UsesCanonicalTwoDigitUppercaseForm()
    {
        Assert.Equal("0x00", NumericInputParser.FormatHexByte(0));
        Assert.Equal("0x2A", NumericInputParser.FormatHexByte(42));
        Assert.Equal("0xFF", NumericInputParser.FormatHexByte(255));
    }
}
