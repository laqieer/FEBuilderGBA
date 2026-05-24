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

        // -----------------------------------------------------------------
        // TryParseHexText — strict variant used by editors that validate
        // user input (e.g. MapSettingFE6View CP pointer). #389
        // -----------------------------------------------------------------

        [Fact]
        public void TryParseHexText_NullReturnsFalse()
        {
            Assert.False(ViewHelpers.TryParseHexText(null, out uint v));
            Assert.Equal(0u, v);
        }

        [Fact]
        public void TryParseHexText_EmptyReturnsFalse()
        {
            Assert.False(ViewHelpers.TryParseHexText("", out uint v));
            Assert.Equal(0u, v);
        }

        [Fact]
        public void TryParseHexText_WhitespaceReturnsFalse()
        {
            Assert.False(ViewHelpers.TryParseHexText("   ", out uint v));
            Assert.Equal(0u, v);
        }

        [Fact]
        public void TryParseHexText_PrefixOnlyReturnsFalse()
        {
            Assert.False(ViewHelpers.TryParseHexText("0x", out uint v));
            Assert.Equal(0u, v);
        }

        [Fact]
        public void TryParseHexText_InvalidReturnsFalse()
        {
            Assert.False(ViewHelpers.TryParseHexText("ZZZZ", out uint v));
            Assert.Equal(0u, v);
        }

        [Fact]
        public void TryParseHexText_ValidReturnsTrue()
        {
            Assert.True(ViewHelpers.TryParseHexText("0x08123456", out uint v));
            Assert.Equal(0x08123456u, v);
        }

        [Fact]
        public void TryParseHexText_NoPrefix_ValidReturnsTrue()
        {
            Assert.True(ViewHelpers.TryParseHexText("ABCD", out uint v));
            Assert.Equal(0xABCDu, v);
        }
    }
}
