using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Minimal GIF89a animated GIF encoder. Ported from
    /// <c>FEBuilderGBA.CLI.GifEncoder</c> to Core so the Avalonia
    /// Map Action Animation export/import path can reuse it (#499).
    ///
    /// Optimized for GBA-style 16-color palette images with transparency support.
    /// </summary>
    public static class GifEncoderCore
    {
        /// <summary>A single GIF frame with RGBA pixel data.</summary>
        public class GifFrame
        {
            public byte[] RgbaPixels;
            public int Width;
            public int Height;
            public int DelayCs; // Delay in centiseconds (100ths of a second)
        }

        // Sentinel: transparent pixels use index 0, which is distinct from any real RGB color.
        // We use -1 as the "transparent" key in the color map to avoid conflating it with black (0x000000).
        const int TRANSPARENT_KEY = -1;

        /// <summary>
        /// Convert an indexed pixel buffer (1 byte per pixel, palette index) into
        /// an RGBA buffer (4 bytes per pixel) using the given RGBA palette.
        /// Used by callers that feed indexed <c>IImage</c> output (from
        /// <c>Decode4bppTiles</c>) into <see cref="Encode"/>.
        ///
        /// Addresses plan v3 B1 (Copilot CLI plan-review pass 2): the GIF encoder
        /// expects RGBA but <c>ImageUtilMapActionAnimationCore.DrawFrame()</c>
        /// returns indexed pixels.
        /// </summary>
        /// <param name="indexedPixels">1 byte per pixel (palette indices).</param>
        /// <param name="paletteRgba">RGBA palette (4 bytes per color).</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        public static byte[] IndexedToRgba(byte[] indexedPixels, byte[] paletteRgba, int width, int height)
        {
            if (indexedPixels == null) return Array.Empty<byte>();
            if (paletteRgba == null) paletteRgba = Array.Empty<byte>();

            int pixelCount = width * height;
            int safeCount = Math.Min(pixelCount, indexedPixels.Length);
            byte[] rgba = new byte[pixelCount * 4];
            int paletteEntries = paletteRgba.Length / 4;

            for (int i = 0; i < safeCount; i++)
            {
                int idx = indexedPixels[i];
                if (idx >= 0 && idx < paletteEntries)
                {
                    int srcOff = idx * 4;
                    int dstOff = i * 4;
                    rgba[dstOff + 0] = paletteRgba[srcOff + 0];
                    rgba[dstOff + 1] = paletteRgba[srcOff + 1];
                    rgba[dstOff + 2] = paletteRgba[srcOff + 2];
                    rgba[dstOff + 3] = paletteRgba[srcOff + 3];
                }
                else
                {
                    // Out-of-range palette index — emit fully transparent.
                    int dstOff = i * 4;
                    rgba[dstOff + 0] = 0;
                    rgba[dstOff + 1] = 0;
                    rgba[dstOff + 2] = 0;
                    rgba[dstOff + 3] = 0;
                }
            }
            return rgba;
        }

        /// <summary>
        /// Encode frames as an animated GIF file.
        /// Quantizes each frame to a shared global palette (up to 256 colors).
        /// Index 0 is reserved for transparency.
        /// </summary>
        public static void Encode(List<GifFrame> frames, string outputPath, int loop = 0)
        {
            if (frames == null || frames.Count == 0) return;

            int width = frames[0].Width;
            int height = frames[0].Height;

            var palette = BuildGlobalPalette(frames, out var colorMap);
            int colorBits = GetColorBits(palette.Length / 3);

            using var stream = File.Create(outputPath);
            using var writer = new BinaryWriter(stream);

            // GIF89a Header
            writer.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // "GIF89a"

            // Logical Screen Descriptor
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            byte packed = (byte)(0x80 | ((colorBits - 1) << 4) | (colorBits - 1));
            writer.Write(packed);
            writer.Write((byte)0); // background color index (= transparent index)
            writer.Write((byte)0); // pixel aspect ratio

            // Global Color Table
            int tableSize = 1 << colorBits;
            for (int i = 0; i < tableSize; i++)
            {
                if (i * 3 + 2 < palette.Length)
                {
                    writer.Write(palette[i * 3 + 0]); // R
                    writer.Write(palette[i * 3 + 1]); // G
                    writer.Write(palette[i * 3 + 2]); // B
                }
                else
                {
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                }
            }

            // NETSCAPE Application Extension (for looping)
            writer.Write((byte)0x21); // Extension Introducer
            writer.Write((byte)0xFF); // Application Extension
            writer.Write((byte)11);   // Block size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write((byte)3);    // Sub-block size
            writer.Write((byte)1);    // Sub-block ID
            writer.Write((ushort)loop); // Loop count (0 = infinite)
            writer.Write((byte)0);    // Block terminator

            foreach (var frame in frames)
            {
                // Graphic Control Extension
                writer.Write((byte)0x21); // Extension Introducer
                writer.Write((byte)0xF9); // Graphic Control Label
                writer.Write((byte)4);    // Block size
                // Packed: disposal=2 (restore to background), transparency=true
                // Bits: reserved(3)=000, disposal(3)=010, user_input(1)=0, transparent(1)=1 → 0x09
                writer.Write((byte)0x09);
                writer.Write((ushort)Math.Max(1, frame.DelayCs)); // Delay
                writer.Write((byte)0);    // Transparent color index = 0
                writer.Write((byte)0);    // Block terminator

                // Image Descriptor
                writer.Write((byte)0x2C); // Image Separator
                writer.Write((ushort)0);  // Left
                writer.Write((ushort)0);  // Top
                writer.Write((ushort)frame.Width);
                writer.Write((ushort)frame.Height);
                writer.Write((byte)0);    // No local color table, not interlaced

                // LZW Compressed Image Data
                byte minCodeSize = (byte)Math.Max(2, colorBits);
                writer.Write(minCodeSize);

                byte[] indices = QuantizeFrame(frame, colorMap);
                byte[] compressed = LzwCompress(indices, minCodeSize);
                WriteSubBlocks(writer, compressed);
                writer.Write((byte)0); // Block terminator
            }

            // GIF Trailer
            writer.Write((byte)0x3B);
        }

        static byte[] BuildGlobalPalette(List<GifFrame> frames, out Dictionary<int, byte> colorMap)
        {
            colorMap = new Dictionary<int, byte>();
            var palette = new List<byte>();

            // Index 0 = transparent (magenta placeholder, won't be visible)
            colorMap[TRANSPARENT_KEY] = 0;
            palette.AddRange(new byte[] { 0, 0, 0 });

            foreach (var frame in frames)
            {
                for (int i = 0; i < frame.RgbaPixels.Length; i += 4)
                {
                    byte a = frame.RgbaPixels[i + 3];
                    if (a < 128) continue; // Transparent pixel

                    int rgb = (frame.RgbaPixels[i] << 16) | (frame.RgbaPixels[i + 1] << 8) | frame.RgbaPixels[i + 2];
                    if (!colorMap.ContainsKey(rgb) && colorMap.Count < 256)
                    {
                        colorMap[rgb] = (byte)colorMap.Count;
                        palette.Add(frame.RgbaPixels[i]);     // R
                        palette.Add(frame.RgbaPixels[i + 1]); // G
                        palette.Add(frame.RgbaPixels[i + 2]); // B
                    }
                }
            }

            return palette.ToArray();
        }

        static byte[] QuantizeFrame(GifFrame frame, Dictionary<int, byte> colorMap)
        {
            int pixelCount = frame.Width * frame.Height;
            byte[] indices = new byte[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                int off = i * 4;
                if (off + 3 >= frame.RgbaPixels.Length) break;

                byte a = frame.RgbaPixels[off + 3];
                if (a < 128)
                {
                    indices[i] = 0; // Transparent
                    continue;
                }

                int rgb = (frame.RgbaPixels[off] << 16) | (frame.RgbaPixels[off + 1] << 8) | frame.RgbaPixels[off + 2];
                if (colorMap.TryGetValue(rgb, out byte idx))
                    indices[i] = idx;
                else
                    indices[i] = 0; // Fallback to transparent
            }

            return indices;
        }

        static int GetColorBits(int colorCount)
        {
            if (colorCount <= 2) return 1;
            if (colorCount <= 4) return 2;
            if (colorCount <= 8) return 3;
            if (colorCount <= 16) return 4;
            if (colorCount <= 32) return 5;
            if (colorCount <= 64) return 6;
            if (colorCount <= 128) return 7;
            return 8;
        }

        /// <summary>GIF LZW compression using packed-int dictionary keys for efficiency.</summary>
        static byte[] LzwCompress(byte[] data, int minCodeSize)
        {
            int clearCode = 1 << minCodeSize;
            int eoiCode = clearCode + 1;
            int nextCode = eoiCode + 1;
            int codeSize = minCodeSize + 1;

            var bitStream = new List<byte>();
            int bitBuffer = 0;
            int bitCount = 0;

            void WriteBits(int code, int bits)
            {
                bitBuffer |= code << bitCount;
                bitCount += bits;
                while (bitCount >= 8)
                {
                    bitStream.Add((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            // Dictionary keyed by (prefixCode, nextByte) packed as int
            var dict = new Dictionary<long, int>();
            void ResetDict()
            {
                dict.Clear();
                nextCode = eoiCode + 1;
                codeSize = minCodeSize + 1;
            }

            ResetDict();
            WriteBits(clearCode, codeSize);

            if (data.Length == 0)
            {
                WriteBits(eoiCode, codeSize);
                if (bitCount > 0)
                    bitStream.Add((byte)(bitBuffer & 0xFF));
                return bitStream.ToArray();
            }

            int w = data[0]; // Current prefix code (single-byte codes are their own index)

            for (int i = 1; i < data.Length; i++)
            {
                int c = data[i];
                long key = ((long)w << 16) | (long)c;

                if (dict.TryGetValue(key, out int existingCode))
                {
                    w = existingCode;
                }
                else
                {
                    WriteBits(w, codeSize);
                    if (nextCode < 4096)
                    {
                        dict[key] = nextCode++;
                        if (nextCode > (1 << codeSize) && codeSize < 12)
                            codeSize++;
                    }
                    else
                    {
                        WriteBits(clearCode, codeSize);
                        ResetDict();
                    }
                    w = c;
                }
            }

            WriteBits(w, codeSize);
            WriteBits(eoiCode, codeSize);

            if (bitCount > 0)
                bitStream.Add((byte)(bitBuffer & 0xFF));

            return bitStream.ToArray();
        }

        static void WriteSubBlocks(BinaryWriter writer, byte[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int blockSize = Math.Min(255, data.Length - offset);
                writer.Write((byte)blockSize);
                writer.Write(data, offset, blockSize);
                offset += blockSize;
            }
        }
    }
}
