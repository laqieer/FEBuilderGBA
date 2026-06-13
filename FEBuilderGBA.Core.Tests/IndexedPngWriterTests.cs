using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="IndexedPngWriter"/>.
    /// Verifies the PNG structure, palette ordering, tRNS behavior, and null/invalid inputs.
    /// </summary>
    public class IndexedPngWriterTests
    {
        // ---- Helpers ----

        /// <summary>
        /// Build a synthetic 16-color GBA palette: each entry encodes the index as R (bits 0-4),
        /// so color 0 = R=0, color 1 = R=8 (1<<3), ... color 15 = R=120 (15<<3).
        /// </summary>
        static byte[] MakeSyntheticPalette16()
        {
            byte[] pal = new byte[16 * 2];
            for (int i = 0; i < 16; i++)
            {
                // BGR555: bits 0-4 = R, bits 5-9 = G, bits 10-14 = B
                // Set R = i (low 5 bits), G = 0, B = 0
                ushort gba = (ushort)(i & 0x1F);
                pal[i * 2] = (byte)(gba & 0xFF);
                pal[i * 2 + 1] = (byte)(gba >> 8);
            }
            return pal;
        }

        /// <summary>Create an index array of size w*h cycling through 0..15.</summary>
        static byte[] MakeIndices(int w, int h)
        {
            byte[] idx = new byte[w * h];
            for (int i = 0; i < idx.Length; i++)
                idx[i] = (byte)(i % 16);
            return idx;
        }

        // ---- Helpers for reading chunks ----

        static (uint length, string type, byte[] data, uint crc) ReadChunk(byte[] png, ref int pos)
        {
            uint length = ReadU32BE(png, pos); pos += 4;
            string type = Encoding.ASCII.GetString(png, pos, 4); pos += 4;
            byte[] data = new byte[length];
            Array.Copy(png, pos, data, 0, (int)length); pos += (int)length;
            uint crc = ReadU32BE(png, pos); pos += 4;
            return (length, type, data, crc);
        }

        static uint ReadU32BE(byte[] buf, int pos)
            => ((uint)buf[pos] << 24) | ((uint)buf[pos + 1] << 16) | ((uint)buf[pos + 2] << 8) | buf[pos + 3];

        static ushort ReadU16BE(byte[] buf, int pos)
            => (ushort)(((uint)buf[pos] << 8) | buf[pos + 1]);

        // ---- Tests ----

        [Fact]
        public void Write_ReturnsNonNullBytes()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16);
            Assert.NotNull(png);
            Assert.True(png.Length > 0);
        }

        [Fact]
        public void Write_StartsWith_PngSignature()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16);

            Assert.NotNull(png);
            // PNG signature: 137 80 78 71 13 10 26 10
            byte[] expected = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], png[i]);
        }

        [Fact]
        public void Write_IHDR_HasCorrectColorTypeAndBitDepth()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16);
            Assert.NotNull(png);

            // Skip 8-byte signature, then read IHDR chunk
            int pos = 8;
            var (length, type, data, _) = ReadChunk(png, ref pos);
            Assert.Equal("IHDR", type);
            Assert.Equal(13u, length);

            // IHDR: width(4) height(4) bitDepth(1) colorType(1) compression(1) filter(1) interlace(1)
            uint w = ReadU32BE(data, 0);
            uint h = ReadU32BE(data, 4);
            byte bitDepth = data[8];
            byte colorType = data[9];

            Assert.Equal(16u, w);
            Assert.Equal(16u, h);
            Assert.Equal(8, bitDepth);    // bit depth 8
            Assert.Equal(3, colorType);   // color type 3 = indexed palette
        }

        [Fact]
        public void Write_PLTE_Has16x3Bytes_InPaletteIndexOrder()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16);
            Assert.NotNull(png);

            // Find PLTE chunk (skip IHDR)
            int pos = 8;
            ReadChunk(png, ref pos); // IHDR
            var (length, type, data, _) = ReadChunk(png, ref pos); // PLTE
            Assert.Equal("PLTE", type);
            Assert.Equal(16u * 3u, length);

            // Palette color 0: R = (0 & 0x1F)<<3 = 0, G = 0, B = 0
            Assert.Equal(0, data[0]); // R
            Assert.Equal(0, data[1]); // G
            Assert.Equal(0, data[2]); // B

            // Palette color 1: GBA value = 1 (R=1 in BGR555), R = (1)<<3 = 8
            Assert.Equal((byte)(1 << 3), data[3]); // R of color 1
            Assert.Equal(0, data[4]);               // G
            Assert.Equal(0, data[5]);               // B

            // Palette color 15: GBA = 15, R = (15)<<3 = 120
            Assert.Equal((byte)(15 << 3), data[45]); // R of color 15
        }

        [Fact]
        public void Write_tRNS_FirstAlphaByte_IsZero_WhenTransparentIndex0()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16, transparentIndex: 0);
            Assert.NotNull(png);

            // Find tRNS chunk (after IHDR, PLTE)
            int pos = 8;
            ReadChunk(png, ref pos); // IHDR
            ReadChunk(png, ref pos); // PLTE
            var (length, type, data, _) = ReadChunk(png, ref pos);
            Assert.Equal("tRNS", type);
            Assert.True(length >= 1, "tRNS must have at least 1 byte when transparentIndex=0");
            Assert.Equal(0, data[0]); // index 0 is transparent
        }

        [Fact]
        public void Write_tRNS_Omitted_WhenTransparentIndexNegative()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16, transparentIndex: -1);
            Assert.NotNull(png);

            // tRNS should NOT be present; next chunk after PLTE should be IDAT
            int pos = 8;
            ReadChunk(png, ref pos); // IHDR
            ReadChunk(png, ref pos); // PLTE
            var (_, type, _, _) = ReadChunk(png, ref pos);
            Assert.Equal("IDAT", type); // no tRNS
        }

        [Fact]
        public void Write_IDAT_InflatesBackToCorrectIndices()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] png = IndexedPngWriter.Write(idx, 16, 16, pal, 16);
            Assert.NotNull(png);

            // Find IDAT chunk
            int pos = 8;
            ReadChunk(png, ref pos); // IHDR
            ReadChunk(png, ref pos); // PLTE
            // Might have tRNS between PLTE and IDAT
            byte[] idatData = null;
            while (pos < png.Length)
            {
                var (length, type, data, _) = ReadChunk(png, ref pos);
                if (type == "IDAT") { idatData = data; break; }
                if (type == "IEND") break;
            }
            Assert.NotNull(idatData);
            Assert.True(idatData.Length > 6, "IDAT must contain zlib header + deflate data + adler");

            // Strip zlib header (2 bytes) and Adler-32 footer (4 bytes), then inflate
            int deflateLen = idatData.Length - 2 - 4;
            Assert.True(deflateLen > 0);
            byte[] deflateBody = new byte[deflateLen];
            Array.Copy(idatData, 2, deflateBody, 0, deflateLen);

            byte[] scanlines;
            using (var ms = new MemoryStream(deflateBody))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                scanlines = outMs.ToArray();
            }

            // Expect height*(1+width) bytes: each row has filter byte 0 + pixel indices
            Assert.Equal(16 * (1 + 16), scanlines.Length);

            // Check filter bytes and indices for each row
            for (int y = 0; y < 16; y++)
            {
                int rowBase = y * (1 + 16);
                Assert.Equal(0, scanlines[rowBase]); // filter byte = 0
                for (int x = 0; x < 16; x++)
                {
                    Assert.Equal(idx[y * 16 + x], scanlines[rowBase + 1 + x]);
                }
            }
        }

        [Fact]
        public void Write_NullIndices_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] result = IndexedPngWriter.Write(null, 16, 16, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_EmptyIndices_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] result = IndexedPngWriter.Write(Array.Empty<byte>(), 16, 16, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_MismatchedLength_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = new byte[10]; // wrong length for 16x16
            byte[] result = IndexedPngWriter.Write(idx, 16, 16, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_ZeroWidth_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = new byte[0];
            byte[] result = IndexedPngWriter.Write(idx, 0, 16, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_ZeroHeight_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = new byte[0];
            byte[] result = IndexedPngWriter.Write(idx, 16, 0, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_NullPalette_ReturnsNull()
        {
            byte[] idx = MakeIndices(16, 16);
            byte[] result = IndexedPngWriter.Write(idx, 16, 16, null, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_ZeroPaletteColorCount_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] result = IndexedPngWriter.Write(idx, 16, 16, pal, 0);
            Assert.Null(result);
        }

        [Fact]
        public void Write_PaletteColorCountTooLarge_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16();
            byte[] idx = MakeIndices(16, 16);
            byte[] result = IndexedPngWriter.Write(idx, 16, 16, pal, 257);
            Assert.Null(result);
        }

        // ---- FINDING 4: pixel index out of palette range → invalid PNG → null ----

        [Fact]
        public void Write_PixelIndexExceedsPaletteCount_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16(); // 16 colors
            // 8x8 image, but one pixel index = 20 which is >= paletteColorCount (16)
            byte[] idx = new byte[8 * 8];
            idx[10] = 20; // out-of-range index for a 16-entry palette
            byte[] result = IndexedPngWriter.Write(idx, 8, 8, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_PixelIndexEqualsPaletteCount_ReturnsNull()
        {
            byte[] pal = MakeSyntheticPalette16(); // 16 colors → valid indices 0..15
            byte[] idx = new byte[8 * 8];
            idx[0] = 16; // index == paletteColorCount is out of range (valid is 0..15)
            byte[] result = IndexedPngWriter.Write(idx, 8, 8, pal, 16);
            Assert.Null(result);
        }

        [Fact]
        public void Write_NeverThrows_OnAnyInput()
        {
            // Should not throw regardless of inputs
            var r1 = Record.Exception(() => IndexedPngWriter.Write(null, 0, 0, null, 0));
            Assert.Null(r1);
            var r2 = Record.Exception(() => IndexedPngWriter.Write(Array.Empty<byte>(), -1, -1, null, -1));
            Assert.Null(r2);
        }
    }
}
