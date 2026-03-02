using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class RegexCacheTests
    {
        [Fact]
        public void Regex_ReturnsRegex()
        {
            var regex = RegexCache.Regex(@"\d+");
            Assert.NotNull(regex);
            Assert.True(regex.IsMatch("123"));
            Assert.False(regex.IsMatch("abc"));
        }

        [Fact]
        public void Regex_CachesRegex()
        {
            var regex1 = RegexCache.Regex(@"\w+");
            var regex2 = RegexCache.Regex(@"\w+");
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void Regex_DifferentPatterns()
        {
            var r1 = RegexCache.Regex(@"\d+");
            var r2 = RegexCache.Regex(@"\w+");
            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void IsMatch_Works()
        {
            Assert.True(RegexCache.IsMatch("hello123", @"\d+"));
            Assert.False(RegexCache.IsMatch("hello", @"\d+"));
        }

        [Fact]
        public void Replace_Works()
        {
            string result = RegexCache.Replace("abc123def", @"\d+", "X");
            Assert.Equal("abcXdef", result);
        }

        [Fact]
        public void MatchSimple_ReturnsGroup()
        {
            string result = RegexCache.MatchSimple("value=42", @"value=(\d+)");
            Assert.Equal("42", result);
        }

        [Fact]
        public void MatchSimple_ReturnsDefault()
        {
            string result = RegexCache.MatchSimple("nomatch", @"value=(\d+)", "default");
            Assert.Equal("default", result);
        }
    }
}
