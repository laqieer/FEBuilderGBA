using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class RomDiffCoreTests
    {
        [Fact]
        public void Compare_IdenticalRoms_ReturnsZeroRanges()
        {
            byte[] rom = new byte[] { 0x00, 0x01, 0x02, 0x03 };
            var result = RomDiffCore.Compare(rom, rom);
            Assert.Empty(result.Ranges);
            Assert.Equal(0u, result.TotalDiffBytes);
            Assert.False(result.HasSizeDifference);
        }

        [Fact]
        public void Compare_SingleByteDiff_ReturnsOneRange()
        {
            byte[] rom1 = new byte[] { 0x00, 0x01, 0x02, 0x03 };
            byte[] rom2 = new byte[] { 0x00, 0xFF, 0x02, 0x03 };
            var result = RomDiffCore.Compare(rom1, rom2);
            Assert.Single(result.Ranges);
            Assert.Equal(1u, result.Ranges[0].Offset);
            Assert.Equal(1u, result.Ranges[0].Length);
            Assert.Equal(1u, result.TotalDiffBytes);
        }

        [Fact]
        public void Compare_ContiguousDiff_MergedIntoOneRange()
        {
            byte[] rom1 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
            byte[] rom2 = new byte[] { 0x00, 0xAA, 0xBB, 0xCC, 0x04 };
            var result = RomDiffCore.Compare(rom1, rom2);
            Assert.Single(result.Ranges);
            Assert.Equal(1u, result.Ranges[0].Offset);
            Assert.Equal(3u, result.Ranges[0].Length);
        }

        [Fact]
        public void Compare_TwoSeparateRanges_ReturnsBoth()
        {
            byte[] rom1 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
            byte[] rom2 = new byte[] { 0xFF, 0x01, 0x02, 0x03, 0xFF, 0x05 };
            var result = RomDiffCore.Compare(rom1, rom2);
            Assert.Equal(2, result.Ranges.Count);
            Assert.Equal(0u, result.Ranges[0].Offset);
            Assert.Equal(1u, result.Ranges[0].Length);
            Assert.Equal(4u, result.Ranges[1].Offset);
            Assert.Equal(1u, result.Ranges[1].Length);
            Assert.Equal(2u, result.TotalDiffBytes);
        }

        [Fact]
        public void Compare_DifferentSizes_ReportsExtraRegion()
        {
            byte[] rom1 = new byte[] { 0x00, 0x01, 0x02 };
            byte[] rom2 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
            var result = RomDiffCore.Compare(rom1, rom2);
            Assert.Single(result.Ranges);
            Assert.Equal(3u, result.Ranges[0].Offset);  // Extra starts at common length
            Assert.Equal(2u, result.Ranges[0].Length);   // 2 extra bytes
            Assert.True(result.HasSizeDifference);
            Assert.Equal(3u, result.Rom1Size);
            Assert.Equal(5u, result.Rom2Size);
        }

        [Fact]
        public void Compare_EmptyRoms_ReturnsZeroRanges()
        {
            var result = RomDiffCore.Compare(new byte[0], new byte[0]);
            Assert.Empty(result.Ranges);
        }

        [Fact]
        public void Compare_OneEmpty_ReportsAllAsExtra()
        {
            byte[] rom1 = new byte[0];
            byte[] rom2 = new byte[] { 0x01, 0x02, 0x03 };
            var result = RomDiffCore.Compare(rom1, rom2);
            Assert.Single(result.Ranges);
            Assert.Equal(0u, result.Ranges[0].Offset);
            Assert.Equal(3u, result.Ranges[0].Length);
        }

        [Fact]
        public void Compare_NullRom1_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RomDiffCore.Compare(null, new byte[1]));
        }

        [Fact]
        public void FormatSummary_NoRanges_ReportsIdentical()
        {
            var result = new RomDiffCore.DiffResult();
            string summary = RomDiffCore.FormatSummary(result);
            Assert.Contains("identical", summary);
        }

        [Fact]
        public void FormatSummary_WithRanges_ShowsAddressesAndTotal()
        {
            var result = new RomDiffCore.DiffResult
            {
                TotalDiffBytes = 260,
                Rom1Size = 1000,
                Rom2Size = 1000,
            };
            result.Ranges.Add(new RomDiffCore.DiffRange { Offset = 0x1000, Length = 256 });
            result.Ranges.Add(new RomDiffCore.DiffRange { Offset = 0x2400, Length = 4 });

            string summary = RomDiffCore.FormatSummary(result);
            Assert.Contains("0x001000", summary);
            Assert.Contains("256 bytes", summary);
            Assert.Contains("0x002400", summary);
            Assert.Contains("2 region", summary);
        }

        [Fact]
        public void FormatTSV_ProducesHeaderAndRows()
        {
            byte[] rom1 = new byte[] { 0x00, 0xAA, 0x02 };
            byte[] rom2 = new byte[] { 0x00, 0xBB, 0x02 };
            var result = RomDiffCore.Compare(rom1, rom2);
            string tsv = RomDiffCore.FormatTSV(result, rom1, rom2);
            Assert.Contains("StartAddr\tLength\tPreviewOld\tPreviewNew", tsv);
            Assert.Contains("AA", tsv);
            Assert.Contains("BB", tsv);
        }
    }
}
