using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class NameResolverTests
    {
        [Fact]
        public void GetTextById_ZeroReturnsEmpty()
        {
            Assert.Equal("", NameResolver.GetTextById(0));
        }

        [Fact]
        public void GetTextById_NullRom_ReturnsFallback()
        {
            // FETextDecode.Direct will fail if no ROM is loaded
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                string result = NameResolver.GetTextById(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetUnitName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetUnitName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetClassName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetClassName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetItemName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetItemName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetSongName_ReturnsFormattedString()
        {
            NameResolver.ClearCache();
            string result = NameResolver.GetSongName(0x1A);
            Assert.StartsWith("Song", result);
        }

        [Fact]
        public void ClearCache_DoesNotThrow()
        {
            NameResolver.ClearCache();
            NameResolver.GetSongName(1);
            NameResolver.ClearCache();
        }

        [Theory]
        [InlineData("@0501Lord", "Lord")]
        [InlineData("@0501@0102Knight", "Knight")]
        [InlineData("NormalText", "NormalText")]
        [InlineData("", "")]
        [InlineData(null, null)]
        [InlineData("@000C@0010@0001Name", "Name")]
        [InlineData("@000C@0010@0080@0004", "")]
        public void StripControlCodes_RemovesAtCodes(string? input, string? expected)
        {
            Assert.Equal(expected, NameResolver.StripControlCodes(input!));
        }

        [Theory]
        [InlineData(" Lord ", "Lord")]
        [InlineData("\r\nKnight\n", "Knight")]
        [InlineData("\x1FName\x1F", "Name")]
        [InlineData("\u3000Name\u3000", "Name")]
        public void StripControlCodes_TrimsWhitespace(string input, string expected)
        {
            Assert.Equal(expected, NameResolver.StripControlCodes(input));
        }
    }
}
