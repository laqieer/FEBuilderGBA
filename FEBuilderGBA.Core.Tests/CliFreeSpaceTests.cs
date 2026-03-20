using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class CliFreeSpaceTests
    {
        /// <summary>
        /// Find contiguous runs of 0xFF bytes in a ROM data array.
        /// Returns a list of (offset, length) tuples for each free region
        /// that meets the minimum size threshold.
        /// </summary>
        private static List<(int offset, int length)> FindFreeRegions(byte[] data, int minSize = 1)
        {
            var regions = new List<(int offset, int length)>();
            int i = 0;
            while (i < data.Length)
            {
                if (data[i] == 0xFF)
                {
                    int start = i;
                    while (i < data.Length && data[i] == 0xFF)
                        i++;
                    int length = i - start;
                    if (length >= minSize)
                        regions.Add((start, length));
                }
                else
                {
                    i++;
                }
            }
            return regions;
        }

        [Fact]
        public void FindFreeRegions_SingleBlock()
        {
            // 1KB ROM with 0xFF from offset 100 to 199 (100 bytes)
            byte[] rom = new byte[1024];
            for (int i = 100; i < 200; i++)
                rom[i] = 0xFF;

            var regions = FindFreeRegions(rom, 16);

            Assert.Single(regions);
            Assert.Equal(100, regions[0].offset);
            Assert.Equal(100, regions[0].length);
        }

        [Fact]
        public void FindFreeRegions_NoFreeSpace()
        {
            // ROM filled with non-0xFF data
            byte[] rom = new byte[512];
            for (int i = 0; i < rom.Length; i++)
                rom[i] = (byte)(i % 255); // 0x00-0xFE, never 0xFF

            var regions = FindFreeRegions(rom, 1);

            Assert.Empty(regions);
        }

        [Fact]
        public void FindFreeRegions_MultipleBlocks()
        {
            byte[] rom = new byte[1024];

            // Block 1: offset 50-99 (50 bytes)
            for (int i = 50; i < 100; i++)
                rom[i] = 0xFF;

            // Block 2: offset 300-399 (100 bytes)
            for (int i = 300; i < 400; i++)
                rom[i] = 0xFF;

            // Block 3: offset 800-899 (100 bytes)
            for (int i = 800; i < 900; i++)
                rom[i] = 0xFF;

            var regions = FindFreeRegions(rom, 16);

            Assert.Equal(3, regions.Count);

            Assert.Equal(50, regions[0].offset);
            Assert.Equal(50, regions[0].length);

            Assert.Equal(300, regions[1].offset);
            Assert.Equal(100, regions[1].length);

            Assert.Equal(800, regions[2].offset);
            Assert.Equal(100, regions[2].length);
        }
    }
}
