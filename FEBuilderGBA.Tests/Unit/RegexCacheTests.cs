using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    public class RegexCacheTests
    {
        [Fact]
        public void Regex_SamePattern_ReturnsSameInstance()
        {
            // Arrange
            string pattern = @"\d+";

            // Act
            var regex1 = RegexCache.Regex(pattern);
            var regex2 = RegexCache.Regex(pattern);

            // Assert
            Assert.Same(regex1, regex2);
        }

        [Fact]
        public void Regex_DifferentPatterns_ReturnsDifferentInstances()
        {
            // Arrange
            string pattern1 = @"\d+";
            string pattern2 = @"[a-z]+";

            // Act
            var regex1 = RegexCache.Regex(pattern1);
            var regex2 = RegexCache.Regex(pattern2);

            // Assert
            Assert.NotSame(regex1, regex2);
        }

        [Theory]
        [InlineData("hello123world", @"\d+", "XXX", "helloXXXworld")]
        [InlineData("foo bar baz", @"\s+", "-", "foo-bar-baz")]
        [InlineData("test", @"\d+", "XXX", "test")]
        public void Replace_WithPattern_ReplacesCorrectly(string input, string pattern, string replacement, string expected)
        {
            // Act
            var result = RegexCache.Replace(input, pattern, replacement);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Replace_WithEvaluator_TransformsMatches()
        {
            // Arrange
            string input = "abc123def456";
            string pattern = @"\d+";
            MatchEvaluator evaluator = m => (int.Parse(m.Value) * 2).ToString();

            // Act
            var result = RegexCache.Replace(input, pattern, evaluator);

            // Assert
            Assert.Equal("abc246def912", result);
        }

        [Theory]
        [InlineData("hello123", @"\d+", true)]
        [InlineData("hello", @"\d+", false)]
        [InlineData("test@email.com", @"@", true)]
        public void IsMatch_WithPattern_ReturnsCorrectResult(string input, string pattern, bool expected)
        {
            // Act
            var result = RegexCache.IsMatch(input, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Match_WithPattern_ReturnsMatch()
        {
            // Arrange
            string input = "test123";
            string pattern = @"\d+";

            // Act
            var match = RegexCache.Match(input, pattern);

            // Assert
            Assert.True(match.Success);
            Assert.Equal("123", match.Value);
        }

        [Fact]
        public void Matches_WithPattern_ReturnsAllMatches()
        {
            // Arrange
            string input = "a1b2c3";
            string pattern = @"\d";

            // Act
            var matches = RegexCache.Matches(input, pattern);

            // Assert
            Assert.Equal(3, matches.Count);
            Assert.Equal("1", matches[0].Value);
            Assert.Equal("2", matches[1].Value);
            Assert.Equal("3", matches[2].Value);
        }

        [Theory]
        [InlineData("a,b,c", ",", new[] { "a", "b", "c" })]
        [InlineData("one two  three", @"\s+", new[] { "one", "two", "three" })]
        public void Split_WithPattern_SplitsCorrectly(string input, string pattern, string[] expected)
        {
            // Act
            var result = RegexCache.Split(input, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("value=123", @"value=(\d+)", "123")]
        [InlineData("name:test", @"name:(\w+)", "test")]
        [InlineData("no match", @"value=(\d+)", "")]
        public void MatchSimple_WithCaptureGroup_ReturnsFirstGroup(string input, string pattern, string expected)
        {
            // Act
            var result = RegexCache.MatchSimple(input, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void MatchSimple_NoMatch_ReturnsDefaultValue()
        {
            // Arrange
            string input = "no numbers";
            string pattern = @"value=(\d+)";
            string defaultValue = "DEFAULT";

            // Act
            var result = RegexCache.MatchSimple(input, pattern, defaultValue);

            // Assert
            Assert.Equal(defaultValue, result);
        }
    }
}
