using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for text validation and cross-reference scanning logic
    /// used by the Avalonia TextViewerViewModel.
    /// </summary>
    [Collection("SharedState")]
    public class TextValidationTests
    {
        [Fact]
        public void NameResolver_GetTextById_ReturnsEmpty_ForZero()
        {
            // Text ID 0 should return empty string (not decoded)
            string result = NameResolver.GetTextById(0);
            Assert.Equal("", result);
        }

        [Fact]
        public void NameResolver_GetTextById_ReturnsQuestionMarks_WhenNoROM()
        {
            // With no ROM loaded, non-zero ID should return "???"
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                string result = NameResolver.GetTextById(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void NameResolver_GetUnitName_ReturnsQuestionMarks_WhenNoROM()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                NameResolver.ClearCache();
                string result = NameResolver.GetUnitName(0);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
        }

        [Fact]
        public void NameResolver_GetClassName_ReturnsQuestionMarks_WhenNoROM()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                NameResolver.ClearCache();
                string result = NameResolver.GetClassName(0);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
        }

        [Fact]
        public void NameResolver_GetItemName_ReturnsQuestionMarks_WhenNoROM()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                NameResolver.ClearCache();
                string result = NameResolver.GetItemName(0);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
        }

        [Fact]
        public void FETextEncode_IsUnHuffmanPatchPointer_False_ForNormalPointer()
        {
            // Normal GBA pointer (0x08XXXXXX) should not be UnHuffman
            Assert.False(FETextEncode.IsUnHuffmanPatchPointer(0x08100000));
        }

        [Fact]
        public void FETextEncode_IsUnHuffmanPatchPointer_False_ForZero()
        {
            Assert.False(FETextEncode.IsUnHuffmanPatchPointer(0));
        }
    }
}
