using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.CLI
{
    /// <summary>
    /// Minimal GIF89a animated GIF encoder.
    /// Optimized for GBA-style 16-color palette images.
    /// </summary>
    internal static class GifEncoder
    {
        /// <summary>A single GIF frame with RGBA pixel data.</summary>
        public class GifFrame
        {
            public byte[] RgbaPixels;
            public int Width;
            public int Height;
            public int DelayCs; // Delay in centiseconds (100ths of a second)
        }

        /// <summary>
        /// Encode frames as an animated GIF file.
        /// Quantizes each frame to a shared global palette (up to 256 colors).
        /// </summary>
        public static void Encode(List<GifFrame> frames, string outputPath, int loop = 0)
        {
            if (frames == null || frames.Count == 0) return;

            int width = frames[0].Width;
            int height = frames[0].Height;

            // Build global palette from all frames (simple: collect unique colors, max 256)
            var palette = BuildGlobalPalette(frames, out var colorMap);
            int colorBits = GetColorBits(palette.Length / 3);

            using var stream = File.Create(outputPath);
            using var writer = new BinaryWriter(stream);

            // GIF89a Header
            writer.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // "GIF89a"

            // Logical Screen Descriptor
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            byte packed = (byte)(0x80 | ((colorBits - 1) << 4) | (colorBits - 1)); // global color table flag + size
            writer.Write(packed);
            writer.Write((byte)0); // background color index
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

            // Write each frame
            foreach (var frame in frames)
            {
                // Graphic Control Extension
                writer.Write((byte)0x21); // Extension Introducer
                writer.Write((byte)0xF9); // Graphic Control Label
                writer.Write((byte)4);    // Block size
                writer.Write((byte)0x04); // Disposal: restore to background, no transparency
                writer.Write((ushort)Math.Max(1, frame.DelayCs)); // Delay
                writer.Write((byte)0);    // Transparent color index (unused)
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

                byte[] indices = QuantizeFrame(frame, colorMap, palette.Length / 3);
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

            // Reserve index 0 for transparent/background (black)
            colorMap[0] = 0;
            palette.AddRange(new byte[] { 0, 0, 0 });

            foreach (var frame in frames)
            {
                for (int i = 0; i < frame.RgbaPixels.Length; i += 4)
                {
                    byte a = frame.RgbaPixels[i + 3];
                    if (a < 128) continue; // Skip transparent pixels

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

        static byte[] QuantizeFrame(GifFrame frame, Dictionary<int, byte> colorMap, int colorCount)
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
                    indices[i] = 0; // Transparent → background
                    continue;
                }

                int rgb = (frame.RgbaPixels[off] << 16) | (frame.RgbaPixels[off + 1] << 8) | frame.RgbaPixels[off + 2];
                if (colorMap.TryGetValue(rgb, out byte idx))
                    indices[i] = idx;
                else
                    indices[i] = 0; // Fallback
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

        /// <summary>GIF LZW compression.</summary>
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

            // Initialize dictionary
            var dict = new Dictionary<string, int>();
            void ResetDict()
            {
                dict.Clear();
                for (int i = 0; i < clearCode; i++)
                    dict[((char)i).ToString()] = i;
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

            string w = ((char)data[0]).ToString();

            for (int i = 1; i < data.Length; i++)
            {
                string wc = w + (char)data[i];
                if (dict.ContainsKey(wc))
                {
                    w = wc;
                }
                else
                {
                    WriteBits(dict[w], codeSize);
                    if (nextCode < 4096)
                    {
                        dict[wc] = nextCode++;
                        if (nextCode > (1 << codeSize) && codeSize < 12)
                            codeSize++;
                    }
                    else
                    {
                        // Table full — reset
                        WriteBits(clearCode, codeSize);
                        ResetDict();
                    }
                    w = ((char)data[i]).ToString();
                }
            }

            WriteBits(dict[w], codeSize);
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
