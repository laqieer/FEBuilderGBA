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

        [Fact]
        public void CompareWithFillBounded_WithinLimit_MatchesUnboundedResult()
        {
            byte[] baseline = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
            byte[] target = new byte[] { 0xFF, 0x01, 0x02, 0x03, 0xFF, 0x05 };
            var unbounded = RomDiffCore.CompareWithFill(baseline, target, 0x00);
            var bounded = RomDiffCore.CompareWithFillBounded(baseline, target, 0x00, maxRanges: 16384);
            Assert.Equal(unbounded.Ranges.Count, bounded.Ranges.Count);
            for (int i = 0; i < unbounded.Ranges.Count; i++)
            {
                Assert.Equal(unbounded.Ranges[i].Offset, bounded.Ranges[i].Offset);
                Assert.Equal(unbounded.Ranges[i].Length, bounded.Ranges[i].Length);
            }
            Assert.Equal(unbounded.TotalDiffBytes, bounded.TotalDiffBytes);
        }

        [Fact]
        public void CompareWithFillBounded_ExactlyAtLimit_Succeeds()
        {
            // 4 separated one-byte ranges (limit == 4) must succeed — the limit check is a
            // "would EXCEED" check, not an off-by-one under-count.
            byte[] baseline = new byte[8];
            byte[] target = (byte[])baseline.Clone();
            target[0] = 1; target[2] = 1; target[4] = 1; target[6] = 1;
            var bounded = RomDiffCore.CompareWithFillBounded(baseline, target, 0x00, maxRanges: 4);
            Assert.Equal(4, bounded.Ranges.Count);
        }

        [Fact]
        public void CompareWithFillBounded_ExceedsLimit_ThrowsDiffRangeLimitExceeded()
        {
            // A pathological alternating-byte diff (every other byte differs) must be rejected
            // the instant the (limit+1)th range would be produced — proving the bound is
            // enforced BEFORE any downstream materialization, without allocating millions of
            // ranges for a large ROM (Copilot review finding: unbounded 16M ranges/files).
            const int limit = 16384;
            // limit+1 separated one-byte diff ranges, each 2 bytes apart (1 changed + 1 same).
            int neededLength = (limit + 1) * 2;
            byte[] baseline = new byte[neededLength];
            byte[] target = (byte[])baseline.Clone();
            for (int i = 0; i < neededLength; i += 2)
                target[i] = 0x01;

            var ex = Assert.Throws<RomDiffCore.DiffRangeLimitExceededException>(() =>
                RomDiffCore.CompareWithFillBounded(baseline, target, 0x00, maxRanges: limit));
            Assert.Equal(limit, ex.Limit);
        }

        [Fact]
        public void CompareWithFillBounded_NonPositiveMaxRanges_Throws()
        {
            byte[] rom = new byte[] { 0x00 };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RomDiffCore.CompareWithFillBounded(rom, rom, 0x00, maxRanges: 0));
        }

        [Fact]
        public void CompareWithFill_ThreeArgOverload_StillUnboundedForLargeAlternatingDiff()
        {
            // Regression guard: the existing public 3-argument CompareWithFill must remain
            // completely unaffected by the new bounded overload — it never throws regardless of
            // range count.
            int rangeCount = BuildfileExportOptions.MaxPayloadRanges + 1;
            byte[] baseline = new byte[rangeCount * 2];
            byte[] target = (byte[])baseline.Clone();
            for (int i = 0; i < target.Length; i += 2)
                target[i] = 0x01;

            var result = RomDiffCore.CompareWithFill(baseline, target, 0x00);
            Assert.Equal(rangeCount, result.Ranges.Count);
        }
    }
}
