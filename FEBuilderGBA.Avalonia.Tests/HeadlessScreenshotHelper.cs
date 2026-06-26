// SPDX-License-Identifier: GPL-3.0-or-later
// Shared, dependency-free BGRA8888 WriteableBitmap -> PNG encoder for the
// Avalonia headless screenshot tests. Several PR-proof screenshot tests
// (ToolWorkSupport, TextCharCode, SkillAssignmentUnitCSkillSys, DisASMArgGrep, ...)
// previously copy/pasted this exact encoder. Extracted here (#1463 review) so the
// encoder lives in one place; new screenshot tests should call SaveFramePng.
using System;
using System.IO;
using global::Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Minimal PNG writer for Avalonia headless <c>CaptureRenderedFrame()</c>
    /// output. No external image dependency — emits an 8-bit RGBA PNG with a
    /// single zlib-compressed IDAT.
    /// </summary>
    internal static class HeadlessScreenshotHelper
    {
        /// <summary>Encode a captured BGRA8888 frame to a PNG file at <paramref name="path"/>.</summary>
        public static void SaveFramePng(WriteableBitmap bmp, string path)
        {
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            int stride = w * 4;
            byte[] bgra = new byte[stride * h];
            using (var fb = bmp.Lock())
            {
                int srcStride = fb.RowBytes;
                IntPtr basePtr = fb.Address;
                for (int y = 0; y < h; y++)
                {
                    IntPtr rowPtr = IntPtr.Add(basePtr, y * srcStride);
                    System.Runtime.InteropServices.Marshal.Copy(rowPtr, bgra, y * stride, stride);
                }
            }

            byte[] raw = new byte[(stride + 1) * h];
            int o = 0;
            for (int y = 0; y < h; y++)
            {
                raw[o++] = 0;
                int rowStart = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = rowStart + x * 4;
                    raw[o++] = bgra[i + 2];
                    raw[o++] = bgra[i + 1];
                    raw[o++] = bgra[i + 0];
                    raw[o++] = bgra[i + 3];
                }
            }

            using var ms = new MemoryStream();
            WriteBytes(ms, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            WriteBe(ihdr, 0, (uint)w);
            WriteBe(ihdr, 4, (uint)h);
            ihdr[8] = 8;
            ihdr[9] = 6;
            WriteChunk(ms, "IHDR", ihdr);
            byte[] compressed = ZlibCompress(raw);
            WriteChunk(ms, "IDAT", compressed);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            File.WriteAllBytes(path, ms.ToArray());
        }

        static byte[] ZlibCompress(byte[] data)
        {
            using var outMs = new MemoryStream();
            outMs.WriteByte(0x78);
            outMs.WriteByte(0x01);
            using (var ds = new System.IO.Compression.DeflateStream(outMs, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                ds.Write(data, 0, data.Length);
            }
            uint a = 1, b = 0;
            foreach (byte by in data) { a = (a + by) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            outMs.WriteByte((byte)(adler >> 24));
            outMs.WriteByte((byte)(adler >> 16));
            outMs.WriteByte((byte)(adler >> 8));
            outMs.WriteByte((byte)adler);
            return outMs.ToArray();
        }

        static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBe(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc32(typeBytes, data);
            byte[] crcb = new byte[4];
            WriteBe(crcb, 0, crc);
            s.Write(crcb, 0, 4);
        }

        static uint[]? _crcTable;
        static uint Crc32(byte[] type, byte[] data)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = ((c & 1) != 0) ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                    _crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte by in type) crc = _crcTable[(crc ^ by) & 0xFF] ^ (crc >> 8);
            foreach (byte by in data) crc = _crcTable[(crc ^ by) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        static void WriteBe(byte[] buf, int off, uint val)
        {
            buf[off] = (byte)(val >> 24);
            buf[off + 1] = (byte)(val >> 16);
            buf[off + 2] = (byte)(val >> 8);
            buf[off + 3] = (byte)val;
        }

        static void WriteBytes(Stream s, byte[] b) => s.Write(b, 0, b.Length);
    }
}
