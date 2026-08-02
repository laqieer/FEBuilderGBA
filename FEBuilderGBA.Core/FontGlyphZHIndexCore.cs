// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Pure index-space geometry used by reproducible ZH packages.  Legacy
    /// bulk import keeps its historically permissive 16x16 extraction path.
    /// </summary>
    public static class FontGlyphZHIndexCore
    {
        public const int ItemShift = 2;
        public const int SerifShift = 1;

        public static int VanillaShift(bool isItemFont)
            => isItemFont ? ItemShift : SerifShift;

        /// <summary>
        /// Take the established native source geometry: rows 0..12 of the
        /// ordinary 16x16 raster.  The package shift is applied only after this
        /// crop and after packing has removed unrepresentable columns/tail.
        /// </summary>
        public static byte[] CropTop16x13(byte[] indexed16x16)
        {
            const int width = FontGlyphZHCore.GLYPH_W;
            if (indexed16x16 == null || indexed16x16.Length != width * width)
                return null;
            var result = new byte[width * FontGlyphZHCore.GLYPH_H];
            Array.Copy(indexed16x16, result, result.Length);
            for (int i = 0; i < indexed16x16.Length; i++)
            {
                if (indexed16x16[i] > 3)
                    throw new ArgumentException(
                        "ZH indices must be in 0..3.",
                        nameof(indexed16x16));
            }
            return result;
        }

        /// <summary>Place a native 16x13 glyph into a zero-filled 16x16 cell.</summary>
        public static byte[] ShiftTo16x16(
            byte[] indexed16x13,
            bool isItemFont)
        {
            if (indexed16x13 == null
                || indexed16x13.Length !=
                    FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H)
            {
                return null;
            }

            int width = FontGlyphZHCore.GLYPH_W;
            int shift = VanillaShift(isItemFont);
            byte[] result = new byte[width * width];
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                Array.Copy(
                    indexed16x13, y * width,
                    result, (y + shift) * width,
                    width);
            }
            return result;
        }

        /// <summary>
        /// Recover native rows from a package cell.  Nonzero pixels outside
        /// the shifted window are rejected rather than silently discarded.
        /// </summary>
        public static byte[] UnshiftTo16x13(
            byte[] indexed16x16,
            bool isItemFont)
        {
            int width = FontGlyphZHCore.GLYPH_W;
            if (indexed16x16 == null
                || indexed16x16.Length != width * width)
            {
                return null;
            }
            if (!HasZeroPadding(indexed16x16, isItemFont))
                throw new ArgumentException(
                    "ZH package padding rows must be index zero.",
                    nameof(indexed16x16));

            int shift = VanillaShift(isItemFont);
            byte[] result = new byte[width * FontGlyphZHCore.GLYPH_H];
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                Array.Copy(
                    indexed16x16, (y + shift) * width,
                    result, y * width,
                    width);
            }
            return result;
        }

        /// <summary>
        /// Legacy inverse geometry. Padding rows and trailing caller bytes are
        /// intentionally ignored; strict packages use
        /// <see cref="UnshiftTo16x13(byte[], bool)"/>.
        /// </summary>
        public static byte[] UnshiftLegacyTo16x13(
            byte[] indexed16x16,
            int width,
            int height,
            bool isItemFont)
        {
            if (indexed16x16 == null
                || width != FontGlyphZHCore.GLYPH_W
                || height != FontGlyphZHCore.GLYPH_W
                || indexed16x16.Length < width * height)
            {
                return null;
            }
            int shift = VanillaShift(isItemFont);
            var result = new byte[
                FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                Array.Copy(
                    indexed16x16,
                    (y + shift) * width,
                    result,
                    y * FontGlyphZHCore.GLYPH_W,
                    FontGlyphZHCore.GLYPH_W);
            }
            return result;
        }

        /// <summary>Places exact 16x13 RGBA rows in a 16x16 canvas.</summary>
        public static byte[] ShiftRgbaTo16x16(
            byte[] rgba16x13,
            bool isItemFont)
        {
            int rowBytes = FontGlyphZHCore.GLYPH_W * 4;
            if (rgba16x13 == null
                || rgba16x13.Length
                    != rowBytes * FontGlyphZHCore.GLYPH_H)
            {
                return null;
            }
            var result = new byte[
                FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_W * 4];
            int shift = VanillaShift(isItemFont);
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                Array.Copy(
                    rgba16x13,
                    y * rowBytes,
                    result,
                    (y + shift) * rowBytes,
                    rowBytes);
            }
            return result;
        }

        /// <summary>
        /// True only when every row outside the shifted 16x13 window is index
        /// zero.  Package validation uses this to reject hidden ZH padding data.
        /// </summary>
        public static bool HasZeroPadding(
            byte[] indexed16x16,
            bool isItemFont)
        {
            const int width = FontGlyphZHCore.GLYPH_W;
            if (indexed16x16 == null || indexed16x16.Length != width * width)
                return false;
            int first = VanillaShift(isItemFont);
            int lastExclusive = first + FontGlyphZHCore.GLYPH_H;
            for (int y = 0; y < width; y++)
            {
                if (y >= first && y < lastExclusive) continue;
                for (int x = 0; x < width; x++)
                {
                    if (indexed16x16[y * width + x] != 0) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Decode the ZH engine's 40-byte continuous low-bit-first 2bpp stream
        /// into a zero-filled 16x13 index grid at the declared render width.
        /// This is the pure index-space inverse of
        /// <see cref="FontGlyphZHCore.PackGlyphZHBytes"/>.
        /// </summary>
        public static byte[] UnpackGlyphBytes(
            byte[] packed,
            int width)
        {
            if (packed == null
                || packed.Length != FontGlyphZHCore.GLYPH_BITMAP_BYTES
                || width < 1 || width > FontGlyphZHCore.GLYPH_W)
                return null;
            int canvasWidth = FontGlyphZHCore.GLYPH_W;
            var result = new byte[
                canvasWidth * FontGlyphZHCore.GLYPH_H];
            int x = 0;
            int y = 0;
            for (int i = 0; i < packed.Length; i++)
            {
                for (int sub = 0; sub < 4; sub++)
                {
                    if (y >= FontGlyphZHCore.GLYPH_H)
                        return result;
                    result[y * canvasWidth + x] =
                        (byte)((packed[i] >> (sub * 2)) & 3);
                    x++;
                    if (x >= width)
                    {
                        x = 0;
                        y++;
                    }
                }
            }
            return result;
        }
    }
}
