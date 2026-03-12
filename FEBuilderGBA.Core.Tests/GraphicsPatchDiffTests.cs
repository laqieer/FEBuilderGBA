using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the binary diff algorithm used by GraphicsToolPatchMakerViewViewModel.
    /// The algorithm is replicated here to test independently of the Avalonia ViewModel.
    /// </summary>
    public class GraphicsPatchDiffTests
    {
        /// <summary>
        /// Core diff algorithm extracted from GraphicsToolPatchMakerViewViewModel.GeneratePatch.
        /// Finds contiguous regions of byte differences between two byte arrays.
        /// </summary>
        static List<(int Offset, byte[] Data)> FindDiffRegions(byte[] original, byte[] modified)
        {
            int maxLen = Math.Max(original.Length, modified.Length);
            var regions = new List<(int Offset, byte[] Data)>();
            int i = 0;

            while (i < maxLen)
            {
                byte origByte = i < original.Length ? original[i] : (byte)0x00;
                byte modByte = i < modified.Length ? modified[i] : (byte)0x00;

                if (origByte != modByte)
                {
                    int start = i;
                    var data = new List<byte>();

                    while (i < maxLen)
                    {
                        origByte = i < original.Length ? original[i] : (byte)0x00;
                        modByte = i < modified.Length ? modified[i] : (byte)0x00;

                        if (origByte == modByte)
                        {
                            int gapEnd = Math.Min(i + 8, maxLen);
                            bool moreChanges = false;
                            for (int g = i + 1; g < gapEnd; g++)
                            {
                                byte go = g < original.Length ? original[g] : (byte)0x00;
                                byte gm = g < modified.Length ? modified[g] : (byte)0x00;
                                if (go != gm) { moreChanges = true; break; }
                            }
                            if (!moreChanges) break;
                        }

                        data.Add(modByte);
                        i++;
                    }

                    regions.Add((start, data.ToArray()));
                }
                else
                {
                    i++;
                }
            }

            return regions;
        }

        [Fact]
        public void IdenticalArrays_NoDifferences()
        {
            byte[] a = { 0x00, 0x01, 0x02, 0x03 };
            byte[] b = { 0x00, 0x01, 0x02, 0x03 };

            var regions = FindDiffRegions(a, b);
            Assert.Empty(regions);
        }

        [Fact]
        public void SingleByteDifference_OneRegion()
        {
            byte[] a = { 0x00, 0x01, 0x02, 0x03 };
            byte[] b = { 0x00, 0xFF, 0x02, 0x03 };

            var regions = FindDiffRegions(a, b);
            Assert.Single(regions);
            Assert.Equal(1, regions[0].Offset);
            Assert.Equal(new byte[] { 0xFF }, regions[0].Data);
        }

        [Fact]
        public void ConsecutiveDifferences_MergedIntoOneRegion()
        {
            byte[] a = { 0x00, 0x01, 0x02, 0x03, 0x04 };
            byte[] b = { 0x00, 0xAA, 0xBB, 0xCC, 0x04 };

            var regions = FindDiffRegions(a, b);
            Assert.Single(regions);
            Assert.Equal(1, regions[0].Offset);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, regions[0].Data);
        }

        [Fact]
        public void TwoSeparateRegions_WhenGapExceeds8Bytes()
        {
            // Create arrays with two changes separated by >8 identical bytes
            byte[] a = new byte[30];
            byte[] b = new byte[30];
            Array.Copy(a, b, 30);

            b[2] = 0xFF;   // First change
            b[20] = 0xEE;  // Second change, 17 bytes away

            var regions = FindDiffRegions(a, b);
            Assert.Equal(2, regions.Count);
            Assert.Equal(2, regions[0].Offset);
            Assert.Equal(new byte[] { 0xFF }, regions[0].Data);
            Assert.Equal(20, regions[1].Offset);
            Assert.Equal(new byte[] { 0xEE }, regions[1].Data);
        }

        [Fact]
        public void CloseChanges_MergedViaGapTolerance()
        {
            // Two changes within 8 bytes of each other should be merged
            byte[] a = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
            byte[] b = { 0x00, 0xFF, 0x02, 0x03, 0x04, 0xEE, 0x06 };

            var regions = FindDiffRegions(a, b);
            // Should be merged into one region because the gap is < 8
            Assert.Single(regions);
            Assert.Equal(1, regions[0].Offset);
        }

        [Fact]
        public void ModifiedLongerThanOriginal_ExtraBytesDetected()
        {
            byte[] a = { 0x00, 0x01 };
            byte[] b = { 0x00, 0x01, 0xAA, 0xBB };

            var regions = FindDiffRegions(a, b);
            Assert.Single(regions);
            Assert.Equal(2, regions[0].Offset);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, regions[0].Data);
        }

        [Fact]
        public void OriginalLongerThanModified_TrailingZerosDiffer()
        {
            byte[] a = { 0x00, 0x01, 0xAA, 0xBB };
            byte[] b = { 0x00, 0x01 };

            var regions = FindDiffRegions(a, b);
            // Bytes at offset 2,3 differ (0xAA/0xBB vs 0x00/0x00)
            Assert.Single(regions);
            Assert.Equal(2, regions[0].Offset);
            Assert.Equal(new byte[] { 0x00, 0x00 }, regions[0].Data);
        }

        [Fact]
        public void EmptyArrays_NoDifferences()
        {
            var regions = FindDiffRegions(Array.Empty<byte>(), Array.Empty<byte>());
            Assert.Empty(regions);
        }

        [Fact]
        public void CompletelyDifferent_OneRegion()
        {
            byte[] a = { 0x00, 0x00, 0x00, 0x00 };
            byte[] b = { 0xFF, 0xFF, 0xFF, 0xFF };

            var regions = FindDiffRegions(a, b);
            Assert.Single(regions);
            Assert.Equal(0, regions[0].Offset);
            Assert.Equal(4, regions[0].Data.Length);
        }

        [Fact]
        public void TotalChangedBytes_MatchesSumOfRegionLengths()
        {
            byte[] a = new byte[50];
            byte[] b = new byte[50];
            b[5] = 0xFF;
            b[6] = 0xFE;
            b[40] = 0xAA;

            var regions = FindDiffRegions(a, b);
            int total = 0;
            foreach (var r in regions) total += r.Data.Length;
            Assert.True(total >= 3); // At least 3 changed bytes
        }
    }
}
