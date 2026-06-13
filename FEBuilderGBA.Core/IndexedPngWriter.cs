using System;
using System.IO;
using System.IO.Compression;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, dependency-free indexed (palettized) PNG encoder for decomp-pipeline compatibility.
    /// Produces color-type 3 PNG files so tools like gbagfx can recover original palette indices.
    ///
    /// The existing SkiaSharp path flattens GBA tiles to RGBA which makes the palette indices
    /// unrecoverable. This encoder preserves them in PNG color type 3 (indexed) format (#1133).
    ///
    /// This class is PURE (no ROM mutation, no IImageService dependency) and NEVER throws:
    /// every public method is fully guarded and returns null on any fault.
    /// </summary>
    public static class IndexedPngWriter
    {
        // PNG signature bytes
        static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Encode an indexed (palettized) image as PNG color-type 3 bytes.
        /// </summary>
        /// <param name="indices">Flat pixel array, one byte per pixel, row-major.
        /// Length must equal width*height.</param>
        /// <param name="width">Image width in pixels. Must be > 0.</param>
        /// <param name="height">Image height in pixels. Must be > 0.</param>
        /// <param name="gbaPalette">GBA palette bytes (2 bytes/color, BGR555 LE).
        /// Must contain at least paletteColorCount*2 bytes.</param>
        /// <param name="paletteColorCount">Number of palette entries to write (1..256).</param>
        /// <param name="transparentIndex">Index treated as fully transparent; all others are opaque.
        /// Pass a negative value to omit the tRNS chunk entirely.
        /// GBA convention: index 0 is transparent, so default is 0.</param>
        /// <returns>Encoded PNG bytes, or null on any fault (never throws).</returns>
        public static byte[] Write(
            byte[] indices,
            int width,
            int height,
            byte[] gbaPalette,
            int paletteColorCount,
            int transparentIndex = 0)
        {
            try
            {
                // ---- Validate inputs ----
                if (indices == null) return null;
                if (width <= 0 || height <= 0) return null;
                if (indices.Length != width * height) return null;
                if (gbaPalette == null) return null;
                if (paletteColorCount < 1 || paletteColorCount > 256) return null;
                if (gbaPalette.Length < paletteColorCount * 2) return null;

                using var ms = new MemoryStream();

                // ---- PNG signature ----
                ms.Write(PngSignature, 0, PngSignature.Length);

                // ---- IHDR ----
                // 13 bytes: width(4) height(4) bitDepth(1) colorType(1) compression(1) filter(1) interlace(1)
                byte[] ihdrData = new byte[13];
                WriteU32BE(ihdrData, 0, (uint)width);
                WriteU32BE(ihdrData, 4, (uint)height);
                ihdrData[8] = 8;   // bit depth = 8
                ihdrData[9] = 3;   // color type = indexed (palette)
                ihdrData[10] = 0;  // compression method = deflate
                ihdrData[11] = 0;  // filter method = adaptive
                ihdrData[12] = 0;  // interlace method = none
                WriteChunk(ms, "IHDR", ihdrData);

                // ---- PLTE ----
                // 3 bytes per entry (R,G,B), in palette index order, converting BGR555 → 8-bit RGB
                byte[] plteData = new byte[paletteColorCount * 3];
                for (int i = 0; i < paletteColorCount; i++)
                {
                    ushort gba = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                    // BGR555 → 8-bit: R = (c & 0x1F) << 3, G = ((c>>5) & 0x1F) << 3, B = ((c>>10) & 0x1F) << 3
                    plteData[i * 3 + 0] = (byte)((gba & 0x1F) << 3);
                    plteData[i * 3 + 1] = (byte)(((gba >> 5) & 0x1F) << 3);
                    plteData[i * 3 + 2] = (byte)(((gba >> 10) & 0x1F) << 3);
                }
                WriteChunk(ms, "PLTE", plteData);

                // ---- tRNS (optional) ----
                // Write (transparentIndex+1) alpha bytes where index transparentIndex=0, others=255
                if (transparentIndex >= 0 && transparentIndex < paletteColorCount)
                {
                    byte[] trnsData = new byte[transparentIndex + 1];
                    // All entries before transparentIndex are opaque (255)
                    for (int i = 0; i < transparentIndex; i++)
                        trnsData[i] = 255;
                    // The transparent entry
                    trnsData[transparentIndex] = 0;
                    WriteChunk(ms, "tRNS", trnsData);
                }

                // ---- IDAT ----
                // Raw scanlines: for each row, prefix with filter byte 0, then width pixel indices
                byte[] scanlines = new byte[height * (1 + width)];
                for (int y = 0; y < height; y++)
                {
                    int scanlineBase = y * (1 + width);
                    scanlines[scanlineBase] = 0; // filter type = None
                    Array.Copy(indices, y * width, scanlines, scanlineBase + 1, width);
                }

                // zlib compress: 2-byte header (0x78 0x01) + deflate body + Adler-32 (4 bytes BE)
                byte[] deflateBytes;
                using (var deflateMs = new MemoryStream())
                {
                    using (var deflate = new DeflateStream(deflateMs, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        deflate.Write(scanlines, 0, scanlines.Length);
                    }
                    deflateBytes = deflateMs.ToArray();
                }

                uint adler = ComputeAdler32(scanlines);
                byte[] idatData = new byte[2 + deflateBytes.Length + 4];
                idatData[0] = 0x78; // zlib CMF: deflate, window 32K
                idatData[1] = 0x01; // zlib FLG: no dict, check bits
                Array.Copy(deflateBytes, 0, idatData, 2, deflateBytes.Length);
                // Adler-32 big-endian at end
                int adlerOffset = 2 + deflateBytes.Length;
                idatData[adlerOffset + 0] = (byte)(adler >> 24);
                idatData[adlerOffset + 1] = (byte)(adler >> 16);
                idatData[adlerOffset + 2] = (byte)(adler >> 8);
                idatData[adlerOffset + 3] = (byte)(adler);
                WriteChunk(ms, "IDAT", idatData);

                // ---- IEND ----
                WriteChunk(ms, "IEND", Array.Empty<byte>());

                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ---- Chunk writer ----

        /// <summary>
        /// Write a PNG chunk: [length BE][type 4 bytes][data][CRC BE].
        /// CRC covers (type || data) concatenated.
        /// </summary>
        static void WriteChunk(Stream s, string type, byte[] data)
        {
            // Length (4 bytes BE)
            byte[] lenBytes = new byte[4];
            WriteU32BE(lenBytes, 0, (uint)data.Length);
            s.Write(lenBytes, 0, 4);

            // Type (4 ASCII bytes)
            byte[] typeBytes = new byte[4];
            for (int i = 0; i < 4; i++)
                typeBytes[i] = (byte)type[i];
            s.Write(typeBytes, 0, 4);

            // Data
            if (data.Length > 0)
                s.Write(data, 0, data.Length);

            // CRC over (type || data)
            byte[] crcInput = new byte[4 + data.Length];
            Array.Copy(typeBytes, 0, crcInput, 0, 4);
            Array.Copy(data, 0, crcInput, 4, data.Length);
            uint crc = ComputePngCrc(crcInput);
            byte[] crcBytes = new byte[4];
            WriteU32BE(crcBytes, 0, crc);
            s.Write(crcBytes, 0, 4);
        }

        // ---- Helpers ----

        static void WriteU32BE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)(value);
        }

        // ---- CRC-32 (PNG standard: polynomial 0xEDB88320, init 0xFFFFFFFF, final XOR 0xFFFFFFFF) ----

        static readonly uint[] CrcTable = BuildCrcTable();

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                table[n] = c;
            }
            return table;
        }

        static uint ComputePngCrc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        // ---- Adler-32 ----

        static uint ComputeAdler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;
            foreach (byte byt in data)
            {
                a = (a + byt) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }
            return (b << 16) | a;
        }
    }
}
