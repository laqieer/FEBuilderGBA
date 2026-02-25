using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class TextEscapeTests
    {
        #region Find Method Tests

        [Fact]
        public void Find_WithValidEscapeSequence_ReturnsTrue()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find("[NL]");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Find_WithInvalidEscapeSequence_ReturnsFalse()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find("[INVALID]");

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("[Clear]")]
        [InlineData("[A]")]
        [InlineData("[Left]")]
        [InlineData("[Right]")]
        [InlineData("[FastPrint]")]
        public void Find_WithCommonEscapes_ReturnsTrue(string escape)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find(escape);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("NL")]
        [InlineData("@0001")]
        [InlineData("[")]
        [InlineData("]")]
        public void Find_WithInvalidFormats_ReturnsFalse(string input)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find(input);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region table_replace Tests

        [Fact]
        public void TableReplace_WithValidEscape_ReplacesCorrectly()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace("@0001");

            // Assert
            Assert.Equal("[NL]", result);
        }

        [Fact]
        public void TableReplace_WithMultipleEscapes_ReplacesAll()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace("Hello@0001World@0002");

            // Assert - Should replace escape codes with their text equivalents
            Assert.Contains("[NL]", result);
        }

        [Fact]
        public void TableReplace_WithNoEscapes_ReturnsUnchanged()
        {
            // Arrange
            var textEscape = new TextEscape();
            string input = "Hello World";

            // Act
            var result = textEscape.table_replace(input);

            // Assert
            Assert.Equal(input, result);
        }

        [Fact]
        public void TableReplace_WithEmptyString_ReturnsEmpty()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace("");

            // Assert
            Assert.Equal("", result);
        }

        #endregion

        #region table_replace_rev Tests

        [Fact]
        public void TableReplaceRev_WithValidTag_ReplacesToEscapeCode()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace_rev("[NL]");

            // Assert
            Assert.Equal("@0001", result);
        }

        [Fact]
        public void TableReplaceRev_WithMultipleTags_ReplacesAll()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace_rev("Hello[NL]World[Clear]");

            // Assert - Should replace tags back to escape codes
            Assert.Contains("@0001", result);
            Assert.Contains("@0002", result);
        }

        [Fact]
        public void TableReplaceRev_WithNoTags_ReturnsUnchanged()
        {
            // Arrange
            var textEscape = new TextEscape();
            string input = "Hello World";

            // Act
            var result = textEscape.table_replace_rev(input);

            // Assert
            Assert.Equal(input, result);
        }

        [Fact]
        public void TableReplaceRev_WithEmptyString_ReturnsEmpty()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace_rev("");

            // Assert
            Assert.Equal("", result);
        }

        #endregion

        #region Round-Trip Tests

        [Theory]
        [InlineData("@0001")]
        [InlineData("@0002")]
        [InlineData("@0003")]
        [InlineData("@0040")]
        public void RoundTrip_EscapeToTagAndBack_PreservesValue(string escapeCode)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var tag = textEscape.table_replace(escapeCode);
            var backToEscape = textEscape.table_replace_rev(tag);

            // Assert
            Assert.Equal(escapeCode, backToEscape);
        }

        [Theory]
        [InlineData("[NL]")]
        [InlineData("[Clear]")]
        [InlineData("[A]")]
        public void RoundTrip_TagToEscapeAndBack_PreservesValue(string tag)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var escapeCode = textEscape.table_replace_rev(tag);
            var backToTag = textEscape.table_replace(escapeCode);

            // Assert
            Assert.Equal(tag, backToTag);
        }

        #endregion

        #region Add Method Tests

        [Fact]
        public void Add_WithNewEscape_AddsSuccessfully()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            textEscape.Add("@9999", "[CustomTag]", "Custom escape");

            // Assert
            var result = textEscape.Find("[CustomTag]");
            Assert.True(result);
        }

        [Fact]
        public void Add_WithDuplicateKey_DoesNotAddAgain()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            textEscape.Add("@9999", "[CustomTag1]", "First");
            textEscape.Add("@9999", "[CustomTag2]", "Second"); // Same key, should be ignored

            // Assert - Should find the first one added
            var result = textEscape.Find("[CustomTag1]");
            Assert.True(result);
        }

        #endregion

        #region Special Escape Tests

        [Theory]
        [InlineData("[LoadOverworldFaces]")]
        [InlineData("[G]")]
        [InlineData("[MoveFarLeft]")]
        [InlineData("[EnableBlinking]")]
        [InlineData("[OpenEyes]")]
        [InlineData("[Item]")]
        public void Find_WithFE80Escapes_ReturnsTrue(string escape)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find(escape);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("[OpenQuote]")]
        [InlineData("[CloseQuote]")]
        public void Find_WithEnglishOnlyEscapes_ReturnsTrue(string escape)
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find(escape);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void TableReplace_WithAtSymbolEscape_ReplacesCorrectly()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.table_replace("@0040");

            // Assert - @0040 represents the @ symbol
            Assert.Equal("[@]", result);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Find_WithNull_ReturnsFalse()
        {
            // Arrange
            var textEscape = new TextEscape();

            // Act
            var result = textEscape.Find(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void TableReplace_WithComplexString_WorksCorrectly()
        {
            // Arrange
            var textEscape = new TextEscape();
            string input = "Line1@0001Line2@0002Line3@0003";

            // Act
            var result = textEscape.table_replace(input);

            // Assert - Should have replaced escape codes
            Assert.NotEqual(input, result);
            Assert.Contains("[NL]", result);
        }

        #endregion
    }
}
