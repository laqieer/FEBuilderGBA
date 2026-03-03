using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-independent median-cut palette quantization.
    /// Extracted from WinForms DecreaseColor.cs.
    /// Input: RGBA pixel data + target palette count.
    /// Output: palette (GBA format) + indexed pixel data.
    /// </summary>
    public static class DecreaseColorCore
    {
        /// <summary>Result of palette quantization.</summary>
        public class QuantizeResult
        {
            /// <summary>Indexed pixel data (1 byte per pixel).</summary>
            public byte[] IndexData { get; set; }
            /// <summary>GBA palette (2 bytes per color, 16-bit RGB555).</summary>
            public byte[] GBAPalette { get; set; }
            /// <summary>RGBA palette (4 bytes per color).</summary>
            public byte[] RGBAPalette { get; set; }
            /// <summary>Actual number of colors used.</summary>
            public int ColorCount { get; set; }
            /// <summary>Image width.</summary>
            public int Width { get; set; }
            /// <summary>Image height.</summary>
            public int Height { get; set; }
        }

        /// <summary>
        /// Quantize RGBA pixel data to a limited palette using median-cut algorithm.
        /// Color index 0 is reserved for transparency (pixels with alpha &lt; 128) unless noReserve1stColor is true.
        /// </summary>
        /// <param name="rgbaPixels">RGBA pixel data (4 bytes per pixel)</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="maxColors">Maximum palette colors (including transparent slot 0). Typically 16 for 4bpp.</param>
        /// <param name="noScale">If true, do not scale RGB values to GBA 5-bit range during palette conversion.</param>
        /// <param name="noReserve1stColor">If true, do not reserve palette slot 0 for transparency.</param>
        /// <param name="ignoreTSA">If true, ignore TSA 8x8 tile deduplication constraints.</param>
        public static QuantizeResult Quantize(byte[] rgbaPixels, int width, int height, int maxColors = 16,
            bool noScale = false, bool noReserve1stColor = false, bool ignoreTSA = false)
        {
            if (rgbaPixels == null || rgbaPixels.Length < width * height * 4)
                return null;

            // Collect opaque pixel colors
            var colors = new List<int[]>(); // [r, g, b]
            int pixelCount = width * height;
            for (int i = 0; i < pixelCount; i++)
            {
                byte a = rgbaPixels[i * 4 + 3];
                if (a < 128) continue; // transparent

                colors.Add(new int[]
                {
                    rgbaPixels[i * 4 + 0], // R
                    rgbaPixels[i * 4 + 1], // G
                    rgbaPixels[i * 4 + 2], // B
                });
            }

            // Reserve slot 0 for transparency (unless noReserve1stColor)
            int transparentOffset = noReserve1stColor ? 0 : 1;
            int paletteSlots = maxColors - transparentOffset;
            if (paletteSlots < 1) paletteSlots = 1;

            // Median-cut
            var palette = MedianCut(colors, paletteSlots);

            // Build full palette
            int colorCount = palette.Count + transparentOffset;
            byte[] rgbaPalette = new byte[colorCount * 4];
            // Slot 0: transparent (0,0,0,0) — only if reserving
            for (int i = 0; i < palette.Count; i++)
            {
                int idx = (i + transparentOffset) * 4;
                rgbaPalette[idx + 0] = (byte)palette[i][0];
                rgbaPalette[idx + 1] = (byte)palette[i][1];
                rgbaPalette[idx + 2] = (byte)palette[i][2];
                rgbaPalette[idx + 3] = 255;
            }

            // Convert to GBA palette
            byte[] gbaPalette = new byte[colorCount * 2];
            if (CoreState.ImageService != null && !noScale)
            {
                gbaPalette = CoreState.ImageService.RGBAPaletteToGBA(rgbaPalette, colorCount);
            }
            else
            {
                // Manual conversion — when noScale is true, use raw 8-bit values truncated
                for (int i = 0; i < colorCount; i++)
                {
                    byte rv = rgbaPalette[i * 4 + 0];
                    byte gv = rgbaPalette[i * 4 + 1];
                    byte bv = rgbaPalette[i * 4 + 2];
                    ushort gba;
                    if (noScale)
                    {
                        // No scaling: keep raw 5 most-significant bits
                        gba = (ushort)((rv >> 3) | ((gv >> 3) << 5) | ((bv >> 3) << 10));
                    }
                    else
                    {
                        gba = (ushort)((rv >> 3) | ((gv >> 3) << 5) | ((bv >> 3) << 10));
                    }
                    gbaPalette[i * 2 + 0] = (byte)(gba & 0xFF);
                    gbaPalette[i * 2 + 1] = (byte)((gba >> 8) & 0xFF);
                }
            }

            // Map pixels to palette indices
            byte[] indexData = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                byte a = rgbaPixels[i * 4 + 3];
                if (a < 128)
                {
                    indexData[i] = 0; // transparent (or first color if noReserve1stColor)
                    continue;
                }

                int r = rgbaPixels[i * 4 + 0];
                int g = rgbaPixels[i * 4 + 1];
                int b = rgbaPixels[i * 4 + 2];

                indexData[i] = (byte)(FindNearestColor(palette, r, g, b) + transparentOffset);
            }

            return new QuantizeResult
            {
                IndexData = indexData,
                GBAPalette = gbaPalette,
                RGBAPalette = rgbaPalette,
                ColorCount = colorCount,
                Width = width,
                Height = height,
            };
        }

        /// <summary>
        /// Median-cut algorithm: recursively partition color space.
        /// </summary>
        static List<int[]> MedianCut(List<int[]> colors, int targetCount)
        {
            if (colors.Count == 0)
                return new List<int[]> { new int[] { 0, 0, 0 } };

            var buckets = new List<List<int[]>> { colors };

            while (buckets.Count < targetCount)
            {
                // Find the bucket with the largest range
                int bestIdx = 0;
                int bestRange = 0;
                int bestChannel = 0;

                for (int i = 0; i < buckets.Count; i++)
                {
                    if (buckets[i].Count < 2) continue;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        int min = 255, max = 0;
                        foreach (var c in buckets[i])
                        {
                            if (c[ch] < min) min = c[ch];
                            if (c[ch] > max) max = c[ch];
                        }
                        int range = max - min;
                        if (range > bestRange)
                        {
                            bestRange = range;
                            bestIdx = i;
                            bestChannel = ch;
                        }
                    }
                }

                if (bestRange == 0) break; // Can't split further

                // Sort by best channel and split at median
                var bucket = buckets[bestIdx];
                int ch2 = bestChannel;
                bucket.Sort((a, b) => a[ch2].CompareTo(b[ch2]));
                int mid = bucket.Count / 2;

                buckets[bestIdx] = bucket.GetRange(0, mid);
                buckets.Add(bucket.GetRange(mid, bucket.Count - mid));
            }

            // Compute average color of each bucket
            var result = new List<int[]>();
            foreach (var bucket in buckets)
            {
                if (bucket.Count == 0) continue;
                long rSum = 0, gSum = 0, bSum = 0;
                foreach (var c in bucket)
                {
                    rSum += c[0]; gSum += c[1]; bSum += c[2];
                }
                result.Add(new int[]
                {
                    (int)(rSum / bucket.Count),
                    (int)(gSum / bucket.Count),
                    (int)(bSum / bucket.Count),
                });
            }

            return result;
        }

        static int FindNearestColor(List<int[]> palette, int r, int g, int b)
        {
            int bestIdx = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i < palette.Count; i++)
            {
                int dr = r - palette[i][0];
                int dg = g - palette[i][1];
                int db = b - palette[i][2];
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }
    }
}
