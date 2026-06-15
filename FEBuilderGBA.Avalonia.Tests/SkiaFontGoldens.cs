// SPDX-License-Identifier: GPL-3.0-or-later
//
// Shared golden bytes + tolerances + tile helpers for the SkiaSharp font
// rasterizer regression lock (#796) and the Android render byte-parity smoke
// test (#1125 / #1126).
//
// SINGLE SOURCE OF TRUTH. This file is the ONE place the Tuffy 'A' golden
// tiles, their widths, the per-glyph-kind pixel tolerances, and the
// tile-unpack/diff helpers live. It is <Compile Include … Link="…"/>-linked
// into BOTH test assemblies:
//   - FEBuilderGBA.Avalonia.Tests  (SkiaFontRasterizerTests.cs — desktop lock)
//   - FEBuilderGBA.Core.Tests       (SkiaRenderByteParityTests.cs — cross-platform
//                                     parity smoke test; conceptually runs on the
//                                     4 Android ABIs under #1126's emulator CI)
// Linking (not copy-pasting) keeps the two suites from silently diverging:
// regenerate the goldens here on an intentional rasterizer change and both
// suites move together.
//
// The baseline golden bytes are the Windows/Linux (x64) SkiaSharp render of
// Tuffy 'A' at 12pt, loaded via FontSpec.FontFileData with Hinting=None (Skia
// geometric rasterizer, no OS hinting) under native libSkiaSharp 2.88 (aligned
// to Avalonia 11.2.3). Identical on win + ubuntu (x64); arm64 (macOS) drifts a
// few px via AA-threshold rounding, absorbed by the tolerances below.
using System;

namespace FEBuilderGBA.SharedTest
{
    /// <summary>
    /// Shared golden tiles + tolerances + helpers for the SkiaSharp font
    /// rasterizer regression / parity tests. See file header for the rationale.
    /// </summary>
    public static class SkiaFontGoldens
    {
        // ---------- golden tiles ----------

        /// <summary>
        /// Baseline golden: the x64 SkiaSharp render of Tuffy 'A' at 12pt,
        /// TEXT font (idx 0 background, idx 3 foreground). 64-byte 2bpp tile.
        /// </summary>
        public static readonly byte[] GoldenTextA =
        {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x3C,0x00,0x00,0x00,0x3C,0x00,0x00,0x00,0x3C,0x00,0x00,0x00,0x33,0x00,0x00,0x00,
            0xC3,0x00,0x00,0x00,0xC3,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,
        };

        /// <summary>Advance width of the golden text 'A'.</summary>
        public const int GoldenTextAWidth = 4;

        /// <summary>
        /// Baseline golden: the x64 SkiaSharp render of Tuffy 'A' at 12pt,
        /// ITEM font (idx 0 background, idx 2 glyph fill, idx 3 outline ring).
        /// 64-byte 2bpp tile.
        /// </summary>
        public static readonly byte[] GoldenItemA =
        {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xF0,0x00,0x00,0x00,
            0xAC,0x03,0x00,0x00,0xAC,0x03,0x00,0x00,0xBB,0x03,0x00,0x00,0xFB,0x0E,0x00,0x00,
            0xAB,0x0E,0x00,0x00,0xFE,0x0E,0x00,0x00,0x02,0x3B,0x00,0x00,0x02,0x3B,0x00,0x00,
        };

        /// <summary>Advance width of the golden item 'A'.</summary>
        public const int GoldenItemAWidth = 6;

        // ---------- tolerances ----------
        //
        // We compare with a small PER-PIXEL tolerance instead of exact byte
        // equality because Apple-Silicon (arm64) Skia performs floating-point
        // antialiasing slightly differently than x64: a glyph-edge pixel can
        // land marginally on the other side of the `R < 0xA0` foreground
        // threshold. With Hinting=None there is no OS hinting variance — only
        // this sub-pixel AA-threshold drift (deterministic per native build).
        //
        // The two glyph kinds get SEPARATE tolerances because the item font's
        // 4-neighbour outline AMPLIFIES the drift: a boundary AA flip on a TEXT
        // glyph changes only that one pixel, but on an ITEM glyph it can
        // additionally toggle up to ~4 surrounding outline (idx-3) pixels.
        //   - text tolerance 12  (~2x the observed text drift)
        //   - item tolerance 18  (~2.25x the observed 8 px, with native headroom)
        // Both stay FAR below a real regression footprint (a broken threshold /
        // scale / composite-offset / pack flips 30+ of the tile's 256 pixels).

        /// <summary>Per-pixel diff tolerance for the text golden.</summary>
        public const int GoldenTextPixelTolerance = 12;

        /// <summary>Per-pixel diff tolerance for the item golden.</summary>
        public const int GoldenItemPixelTolerance = 18;

        // ---------- helpers ----------

        /// <summary>
        /// Unpack EACH 64-byte 2bpp tile (<paramref name="expected"/> and
        /// <paramref name="actual"/>) into its 256 palette indices — a single
        /// 64-byte tile already encodes 256 pixels (4 px/byte; reverse of the
        /// Image4ToByte pack: per byte, 4 pixels =
        /// (b&gt;&gt;0)&amp;3, (b&gt;&gt;2)&amp;3, (b&gt;&gt;4)&amp;3, (b&gt;&gt;6)&amp;3)
        /// — and count how many of the 256 pixels differ between the two tiles.
        /// Used by the golden tests to tolerate the small x64-vs-arm64
        /// antialiasing drift.
        /// </summary>
        public static int CountDifferingPixels(byte[] expected, byte[] actual)
        {
            byte[] e = UnpackIndices(expected);
            byte[] a = UnpackIndices(actual);
            int diff = 0;
            for (int i = 0; i < e.Length; i++)
            {
                if (e[i] != a[i]) diff++;
            }
            return diff;
        }

        /// <summary>
        /// Unpack a 64-byte 2bpp tile into its 256 palette indices (4 px/byte).
        /// </summary>
        public static byte[] UnpackIndices(byte[] tile)
        {
            var idx = new byte[256];
            int p = 0;
            for (int i = 0; i < 64; i++)
            {
                byte b = tile[i];
                idx[p++] = (byte)(b & 0x03);
                idx[p++] = (byte)((b >> 2) & 0x03);
                idx[p++] = (byte)((b >> 4) & 0x03);
                idx[p++] = (byte)((b >> 6) & 0x03);
            }
            return idx;
        }

        /// <summary>
        /// Unpack a 64-byte 2bpp tile into a 16x16 [x,y] index grid.
        /// </summary>
        public static byte[,] UnpackGrid(byte[] tile)
        {
            byte[] flat = UnpackIndices(tile);
            var grid = new byte[16, 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    grid[x, y] = flat[x + y * 16];
            return grid;
        }
    }
}
