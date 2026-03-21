using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ViewHelpersTests
    {
        [Fact]
        public void ParseHexText_NullReturnsZero()
        {
            Assert.Equal(0u, ViewHelpers.ParseHexText(null));
        }

        [Fact]
        public void ParseHexText_EmptyReturnsZero()
        {
            Assert.Equal(0u, ViewHelpers.ParseHexText(""));
        }

        [Fact]
        public void ParseHexText_WhitespaceReturnsZero()
        {
            Assert.Equal(0u, ViewHelpers.ParseHexText("   "));
        }

        [Fact]
        public void ParseHexText_HexWithPrefix()
        {
            Assert.Equal(0x08000200u, ViewHelpers.ParseHexText("0x08000200"));
        }

        [Fact]
        public void ParseHexText_HexWithUpperPrefix()
        {
            Assert.Equal(0xABCDu, ViewHelpers.ParseHexText("0XABCD"));
        }

        [Fact]
        public void ParseHexText_HexWithoutPrefix()
        {
            Assert.Equal(0xFFu, ViewHelpers.ParseHexText("FF"));
        }

        [Fact]
        public void ParseHexText_WithLeadingTrailingWhitespace()
        {
            Assert.Equal(0x10u, ViewHelpers.ParseHexText("  0x10  "));
        }

        [Fact]
        public void ParseHexText_InvalidReturnsZero()
        {
            Assert.Equal(0u, ViewHelpers.ParseHexText("ZZZZ"));
        }

        [Fact]
        public void ParseHexText_ZeroValue()
        {
            Assert.Equal(0u, ViewHelpers.ParseHexText("0x00000000"));
        }

        [Fact]
        public void ParseHexText_MaxUint()
        {
            Assert.Equal(0xFFFFFFFFu, ViewHelpers.ParseHexText("0xFFFFFFFF"));
        }
    }
}
