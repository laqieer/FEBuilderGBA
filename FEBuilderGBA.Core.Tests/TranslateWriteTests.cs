using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for TranslateCore.WriteTexts implementation.
    /// </summary>
    [Collection("SharedState")]
    public class TranslateWriteTests
    {
        [Fact]
        public void WriteTexts_NullRom_ReturnsZero()
        {
            var entries = new List<(uint textId, string text)> { (0, "test") };
            int count = TranslateCore.WriteTexts(null, entries);
            Assert.Equal(0, count);
        }

        [Fact]
        public void WriteTexts_RomWithNullRomInfo_ReturnsZero()
        {
            var rom = new ROM();
            var entries = new List<(uint textId, string text)> { (0, "test") };
            int count = TranslateCore.WriteTexts(rom, entries);
            Assert.Equal(0, count);
        }

        [Fact]
        public void WriteTexts_NullEncoder_ReturnsZero()
        {
            // Save and restore state
            var origEncoder = CoreState.FETextEncoder;
            try
            {
                CoreState.FETextEncoder = null;
                var rom = new ROM();
                var entries = new List<(uint textId, string text)> { (0, "test") };
                int count = TranslateCore.WriteTexts(rom, entries);
                Assert.Equal(0, count);
            }
            finally
            {
                CoreState.FETextEncoder = origEncoder;
            }
        }

        [Fact]
        public void WriteTexts_EmptyEntries_ReturnsZero()
        {
            var rom = new ROM();
            var entries = new List<(uint textId, string text)>();
            int count = TranslateCore.WriteTexts(rom, entries);
            Assert.Equal(0, count);
        }

        [Fact]
        public void GetTextCount_NullRom_ReturnsZero()
        {
            uint count = TranslateCore.GetTextCount(null);
            Assert.Equal(0u, count);
        }

        [Fact]
        public void GetTextCount_EmptyRom_ReturnsZero()
        {
            var rom = new ROM();
            uint count = TranslateCore.GetTextCount(rom);
            Assert.Equal(0u, count);
        }
    }
}
