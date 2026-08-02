// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;

namespace FEBuilderGBA.Core
{
    public enum FontLibraryFormat
    {
        Main16x16,
        Zh16x13,
    }

    public enum FontLibraryStyle
    {
        Item,
        Text,
    }

    /// <summary>Outcome of the canonical font-package PNG reader.</summary>
    public sealed class IndexedPngCoreReadResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public byte[] Indices { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Canonical 16x16, 2-bit indexed PNG codec for font-library packages.
    /// It intentionally implements its single stored-DEFLATE block directly:
    /// package bytes therefore do not depend on a compression library/runtime.
    /// </summary>
    public static class IndexedPngCore
    {
        const int Width = 16;
        const int Height = 16;
        const int PackedRowBytes = Width / 4;
        const int ScanlineBytes = 1 + PackedRowBytes;
        const int RawBytes = Height * ScanlineBytes;
        static readonly byte[] Signature =
            { 137, 80, 78, 71, 13, 10, 26, 10 };
        static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Encode exactly 256 row-major palette indices (0..3).</summary>
        public static byte[] Encode(
            byte[] indices,
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            ValidateEnums(format, style);
            if (indices == null || indices.Length != Width * Height)
                throw new ArgumentException(
                    "A font PNG requires exactly 256 indices.", nameof(indices));
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] > 3)
                    throw new ArgumentException(
                        "Font PNG indices must be in 0..3.", nameof(indices));
            }

            byte[] raw = new byte[RawBytes];
            for (int y = 0; y < Height; y++)
            {
                int row = y * ScanlineBytes;
                raw[row] = 0; // PNG filter None
                for (int group = 0; group < PackedRowBytes; group++)
                {
                    int pixel = y * Width + group * 4;
                    // PNG sub-byte samples are high-bit first.
                    raw[row + 1 + group] = (byte)(
                        (indices[pixel] << 6)
                        | (indices[pixel + 1] << 4)
                        | (indices[pixel + 2] << 2)
                        | indices[pixel + 3]);
                }
            }

            byte[] palette = GetPaletteRgb(format, style);
            byte[] ihdr = new byte[13];
            WriteU32BE(ihdr, 0, Width);
            WriteU32BE(ihdr, 4, Height);
            ihdr[8] = 2;  // bit depth
            ihdr[9] = 3;  // indexed
            ihdr[10] = 0; // deflate
            ihdr[11] = 0; // adaptive filters
            ihdr[12] = 0; // no interlace

            byte[] idat = MakeStoredZlib(raw);
            using var output = new MemoryStream(192);
            output.Write(Signature, 0, Signature.Length);
            WriteChunk(output, "IHDR", ihdr);
            WriteChunk(output, "PLTE", palette);
            WriteChunk(output, "tRNS", new byte[] { 0, 255, 255, 255 });
            WriteChunk(output, "IDAT", idat);
            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        /// <summary>Compatibility spelling for callers that treat codecs as writers.</summary>
        public static byte[] Write(
            byte[] indices,
            FontLibraryFormat format,
            FontLibraryStyle style)
            => Encode(indices, format, style);

        /// <summary>
        /// Read only the exact byte grammar emitted by <see cref="Encode"/>.
        /// Alternate filters, compression blocks, chunk splits/order, metadata,
        /// palette spellings and trailing bytes are all rejected.
        /// </summary>
        public static IndexedPngCoreReadResult Read(
            byte[] png,
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            var result = new IndexedPngCoreReadResult();
            try
            {
                ValidateEnums(format, style);
                if (png == null || png.Length < Signature.Length)
                    return Fail(result, "PNG is null or truncated.");
                for (int i = 0; i < Signature.Length; i++)
                {
                    if (png[i] != Signature[i])
                        return Fail(result, "Invalid PNG signature.");
                }

                int position = Signature.Length;
                byte[] ihdr = ReadExactChunk(png, ref position, "IHDR", 13);
                if (ReadU32BE(ihdr, 0) != Width
                    || ReadU32BE(ihdr, 4) != Height
                    || ihdr[8] != 2
                    || ihdr[9] != 3
                    || ihdr[10] != 0
                    || ihdr[11] != 0
                    || ihdr[12] != 0)
                {
                    return Fail(result,
                        "IHDR is not canonical 16x16 indexed bit-depth 2.");
                }

                byte[] plte = ReadExactChunk(png, ref position, "PLTE", 12);
                if (!BytesEqual(plte, GetPaletteRgb(format, style)))
                    return Fail(result, "PLTE does not match the selected format/style.");

                byte[] trns = ReadExactChunk(png, ref position, "tRNS", 4);
                if (!BytesEqual(
                    trns, new byte[] { 0, 255, 255, 255 }))
                    return Fail(result,
                        "tRNS must be exactly {0,255,255,255}.");

                // zlib(2) + stored block header(5) + 80 raw bytes + Adler32(4)
                byte[] idat = ReadExactChunk(
                    png, ref position, "IDAT", 2 + 5 + RawBytes + 4);
                ReadExactChunk(png, ref position, "IEND", 0);
                if (position != png.Length)
                    return Fail(result, "Trailing data after IEND is forbidden.");

                byte[] raw = ReadStoredZlib(idat);
                var indices = new byte[Width * Height];
                for (int y = 0; y < Height; y++)
                {
                    int row = y * ScanlineBytes;
                    if (raw[row] != 0)
                        return Fail(result, "Only PNG filter zero is canonical.");
                    for (int group = 0; group < PackedRowBytes; group++)
                    {
                        byte packed = raw[row + 1 + group];
                        int pixel = y * Width + group * 4;
                        indices[pixel] = (byte)((packed >> 6) & 3);
                        indices[pixel + 1] = (byte)((packed >> 4) & 3);
                        indices[pixel + 2] = (byte)((packed >> 2) & 3);
                        indices[pixel + 3] = (byte)(packed & 3);
                    }
                }

                // This also locks every implementation detail not separately
                // diagnosed above (CRC spelling and stored-block bytes included).
                if (!BytesEqual(png, Encode(indices, format, style)))
                    return Fail(result, "PNG is valid but not canonical.");

                result.Success = true;
                result.Indices = indices;
                return result;
            }
            catch (Exception ex)
            {
                return Fail(result, "Canonical PNG read failed: " + ex.Message);
            }
        }

        public static bool TryRead(
            byte[] png,
            FontLibraryFormat format,
            FontLibraryStyle style,
            out byte[] indices,
            out string error)
        {
            IndexedPngCoreReadResult read = Read(png, format, style);
            indices = read.Indices;
            error = read.Error;
            return read.Success;
        }

        /// <summary>Unpack the engine's low-bit-first 2bpp 16x16 tile.</summary>
        public static byte[] UnpackEngineTile(byte[] tile)
        {
            if (tile == null || tile.Length != 64)
                throw new ArgumentException(
                    "An engine font tile must contain exactly 64 bytes.",
                    nameof(tile));
            var indices = new byte[256];
            int pixel = 0;
            for (int i = 0; i < tile.Length; i++)
            {
                byte packed = tile[i];
                indices[pixel++] = (byte)(packed & 3);
                indices[pixel++] = (byte)((packed >> 2) & 3);
                indices[pixel++] = (byte)((packed >> 4) & 3);
                indices[pixel++] = (byte)((packed >> 6) & 3);
            }
            return indices;
        }

        /// <summary>Pack 256 indices in the engine's low-bit-first tile order.</summary>
        public static byte[] PackEngineTile(byte[] indices)
        {
            if (indices == null || indices.Length != 256)
                throw new ArgumentException("Exactly 256 indices are required.",
                    nameof(indices));
            var tile = new byte[64];
            for (int i = 0; i < tile.Length; i++)
            {
                int pixel = i * 4;
                for (int n = 0; n < 4; n++)
                {
                    if (indices[pixel + n] > 3)
                        throw new ArgumentException("Indices must be in 0..3.",
                            nameof(indices));
                }
                tile[i] = (byte)(
                    indices[pixel]
                    | (indices[pixel + 1] << 2)
                    | (indices[pixel + 2] << 4)
                    | (indices[pixel + 3] << 6));
            }
            return tile;
        }

        /// <summary>
        /// Convert the main rasterizer's logical index order to the package
        /// format's palette order. ZH swaps white/gray (indices 1 and 2).
        /// </summary>
        public static byte[] RemapRasterizerIndices(
            byte[] indices,
            FontLibraryFormat format)
        {
            if (indices == null || indices.Length != 256)
                throw new ArgumentException("Exactly 256 indices are required.",
                    nameof(indices));
            var result = (byte[])indices.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] > 3)
                    throw new ArgumentException("Indices must be in 0..3.",
                        nameof(indices));
                if (format == FontLibraryFormat.Zh16x13)
                {
                    if (result[i] == 1) result[i] = 2;
                    else if (result[i] == 2) result[i] = 1;
                }
            }
            return result;
        }

        public static byte[] GetPaletteRgb(
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            ValidateEnums(format, style);
            bool item = style == FontLibraryStyle.Item;
            byte[] gba = format == FontLibraryFormat.Main16x16
                ? FontGlyphRenderCore.GetFontPaletteGBA(item)
                : FontGlyphZHCore.GetFontPaletteGBA(item);
            var rgb = new byte[12];
            for (int i = 0; i < 4; i++)
            {
                ushort color =
                    (ushort)(gba[i * 2] | (gba[i * 2 + 1] << 8));
                rgb[i * 3] = (byte)((color & 31) << 3);
                rgb[i * 3 + 1] = (byte)(((color >> 5) & 31) << 3);
                rgb[i * 3 + 2] = (byte)(((color >> 10) & 31) << 3);
            }
            return rgb;
        }

        static byte[] MakeStoredZlib(byte[] raw)
        {
            var zlib = new byte[2 + 5 + raw.Length + 4];
            zlib[0] = 0x78;
            zlib[1] = 0x01;
            zlib[2] = 0x01; // BFINAL=1, BTYPE=stored, byte padding zero
            ushort length = checked((ushort)raw.Length);
            ushort inverse = (ushort)~length;
            zlib[3] = (byte)length;
            zlib[4] = (byte)(length >> 8);
            zlib[5] = (byte)inverse;
            zlib[6] = (byte)(inverse >> 8);
            Array.Copy(raw, 0, zlib, 7, raw.Length);
            WriteU32BE(zlib, 7 + raw.Length, ComputeAdler32(raw));
            return zlib;
        }

        static byte[] ReadStoredZlib(byte[] zlib)
        {
            if (zlib[0] != 0x78 || zlib[1] != 0x01 || zlib[2] != 0x01)
                throw new InvalidDataException(
                    "IDAT must use the canonical stored zlib block.");
            int length = zlib[3] | (zlib[4] << 8);
            int inverse = zlib[5] | (zlib[6] << 8);
            if (length != RawBytes || ((length ^ inverse) & 0xFFFF) != 0xFFFF)
                throw new InvalidDataException("Stored DEFLATE LEN/NLEN is invalid.");
            var raw = new byte[RawBytes];
            Array.Copy(zlib, 7, raw, 0, raw.Length);
            uint expected = ReadU32BE(zlib, 7 + raw.Length);
            if (expected != ComputeAdler32(raw))
                throw new InvalidDataException("IDAT Adler-32 mismatch.");
            return raw;
        }

        static byte[] ReadExactChunk(
            byte[] png,
            ref int position,
            string expectedType,
            int expectedLength)
        {
            if (position < 0 || position + 12L + expectedLength > png.Length)
                throw new InvalidDataException(
                    "Truncated " + expectedType + " chunk.");
            int length = checked((int)ReadU32BE(png, position));
            if (length != expectedLength)
                throw new InvalidDataException(
                    expectedType + " has a non-canonical length.");
            for (int i = 0; i < 4; i++)
            {
                if (png[position + 4 + i] != (byte)expectedType[i])
                    throw new InvalidDataException(
                        "Expected " + expectedType + " chunk.");
            }
            var data = new byte[length];
            Array.Copy(png, position + 8, data, 0, length);
            uint actualCrc = ReadU32BE(png, position + 8 + length);
            uint expectedCrc = ComputeCrc(
                png, position + 4, 4 + length);
            if (actualCrc != expectedCrc)
                throw new InvalidDataException(
                    expectedType + " CRC-32 mismatch.");
            position += 12 + length;
            return data;
        }

        static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] header = new byte[8];
            WriteU32BE(header, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
            stream.Write(header, 0, header.Length);
            stream.Write(data, 0, data.Length);
            byte[] crcInput = new byte[4 + data.Length];
            Array.Copy(header, 4, crcInput, 0, 4);
            Array.Copy(data, 0, crcInput, 4, data.Length);
            byte[] crc = new byte[4];
            WriteU32BE(crc, 0, ComputeCrc(crcInput, 0, crcInput.Length));
            stream.Write(crc, 0, crc.Length);
        }

        static void ValidateEnums(
            FontLibraryFormat format,
            FontLibraryStyle style)
        {
            if (format != FontLibraryFormat.Main16x16
                && format != FontLibraryFormat.Zh16x13)
                throw new ArgumentOutOfRangeException(nameof(format));
            if (style != FontLibraryStyle.Item
                && style != FontLibraryStyle.Text)
                throw new ArgumentOutOfRangeException(nameof(style));
        }

        static IndexedPngCoreReadResult Fail(
            IndexedPngCoreReadResult result,
            string error)
        {
            result.Success = false;
            result.Error = error;
            result.Indices = Array.Empty<byte>();
            return result;
        }

        static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        static uint ComputeAdler32(byte[] data)
        {
            const uint modulus = 65521;
            uint a = 1;
            uint b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % modulus;
                b = (b + a) % modulus;
            }
            return (b << 16) | a;
        }

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0
                        ? 0xEDB88320u ^ (value >> 1)
                        : value >> 1;
                table[i] = value;
            }
            return table;
        }

        static uint ComputeCrc(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
                crc = CrcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static uint ReadU32BE(byte[] data, int offset)
            => ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];

        static void WriteU32BE(byte[] data, int offset, int value)
            => WriteU32BE(data, offset, checked((uint)value));

        static void WriteU32BE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
