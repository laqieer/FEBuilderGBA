// SPDX-License-Identifier: GPL-3.0-or-later
// UnitPaletteImportCore — cross-platform helper for the Avalonia Unit Palette
// Editor's "Import Image" button (#904).
//
// Mirrors the WinForms PaletteFormRef.MakePaletteBitmapToUIEx path: read a
// 16-color image's palette IN INDEX ORDER and surface the RGB555 channel
// triples that the editor's 16 R/G/B NumericUpDowns expect. The order is
// SEMANTIC (index 0 = transparent/backdrop), so the extraction must preserve
// it — distinct colors are collected in first-appearance (pixel scan) order,
// never re-sorted or de-ordered through a set.
//
// CORRECTION 3a (plan review): reject any image that is NOT a ≤16-color image.
// Do NOT quantize: DecreaseColorCore.Quantize never rejects and scrambles the
// index order. This helper instead counts the distinct colors and refuses
// (returns false, no channel writes) when there are more than 16.
//
// CORRECTION 3b (plan review): extract the 16 entries IN INDEX ORDER. When the
// loader already exposes an indexed palette (GBA bytes via IImage.GetPaletteGBA),
// prefer it verbatim; otherwise derive the order from the RGBA pixels by
// first-appearance scan. Either way the result is index-ordered, never a set.
//
// Cross-platform: depends only on Core U helpers. No WinForms, no Avalonia,
// no System.Drawing.

using System.Collections.Generic;

namespace FEBuilderGBA
{
    public static class UnitPaletteImportCore
    {
        public const int PALETTE_COUNT = 16;

        /// <summary>
        /// Extract a ≤16-color palette IN INDEX ORDER from the supplied source.
        ///
        /// Preference order:
        ///   1. If <paramref name="gbaPalette"/> is a non-empty indexed palette
        ///      (2 bytes per color, GBA RGB555) with ≤16 colors, decode it
        ///      verbatim — the loader already preserved index order.
        ///   2. Otherwise scan <paramref name="rgbaPixels"/> (4 bytes/pixel:
        ///      R,G,B,A) and collect the distinct opaque-ish colors in
        ///      first-appearance order. Alpha is ignored for distinctness
        ///      (GBA palettes have no per-color alpha) but the scan still
        ///      preserves the top-left-first ordering.
        ///
        /// Output: 16 RGB555 channel arrays (0-31). When fewer than 16 distinct
        /// colors are present, the remaining slots are zero-filled (matching the
        /// WF MakePaletteBitmapToUIEx behavior, which leaves unused slots black).
        /// </summary>
        /// <param name="gbaPalette">
        /// Optional indexed palette (GBA RGB555, 2 bytes/color). Pass an empty
        /// or null array when the loader did not preserve an indexed palette.
        /// </param>
        /// <param name="rgbaPixels">
        /// Flat RGBA pixel data (4 bytes/pixel). Used only when
        /// <paramref name="gbaPalette"/> is empty.
        /// </param>
        /// <param name="r">Out: 16 R channel values (0-31), index-ordered.</param>
        /// <param name="g">Out: 16 G channel values (0-31), index-ordered.</param>
        /// <param name="b">Out: 16 B channel values (0-31), index-ordered.</param>
        /// <returns>
        /// <c>true</c> when a valid ≤16-color palette was extracted; <c>false</c>
        /// (with all channels left zeroed) when the source has more than 16
        /// distinct colors or is otherwise unusable. On <c>false</c> the caller
        /// MUST make no UI/ROM change (CORRECTION 3a).
        /// </returns>
        public static bool TryExtractIndexOrdered(
            byte[] gbaPalette,
            byte[] rgbaPixels,
            out uint[] r,
            out uint[] g,
            out uint[] b)
        {
            r = new uint[PALETTE_COUNT];
            g = new uint[PALETTE_COUNT];
            b = new uint[PALETTE_COUNT];

            // ----- Path 1: loader-preserved indexed palette (GBA bytes) -----
            if (gbaPalette != null && gbaPalette.Length >= 2)
            {
                int colorCount = gbaPalette.Length / 2;
                if (colorCount > PALETTE_COUNT) return false; // CORRECTION 3a
                for (int i = 0; i < colorCount; i++)
                {
                    ushort c = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                    r[i] = (uint)(c & 0x1F);
                    g[i] = (uint)((c >> 5) & 0x1F);
                    b[i] = (uint)((c >> 10) & 0x1F);
                }
                return true;
            }

            // ----- Path 2: derive index order from RGBA pixels (scan order) -----
            if (rgbaPixels == null || rgbaPixels.Length < 4) return false;

            // First-appearance ordered distinct list of RGB555 colors.
            var order = new List<ushort>();
            var seen = new HashSet<ushort>();
            for (int p = 0; p + 3 < rgbaPixels.Length; p += 4)
            {
                byte r8 = rgbaPixels[p + 0];
                byte g8 = rgbaPixels[p + 1];
                byte b8 = rgbaPixels[p + 2];
                ushort c = (ushort)(((r8 >> 3) & 0x1F)
                                  | (((g8 >> 3) & 0x1F) << 5)
                                  | (((b8 >> 3) & 0x1F) << 10));
                if (seen.Add(c))
                {
                    order.Add(c);
                    if (order.Count > PALETTE_COUNT) return false; // CORRECTION 3a — bail early
                }
            }

            if (order.Count == 0) return false;

            for (int i = 0; i < order.Count && i < PALETTE_COUNT; i++)
            {
                ushort c = order[i];
                r[i] = (uint)(c & 0x1F);
                g[i] = (uint)((c >> 5) & 0x1F);
                b[i] = (uint)((c >> 10) & 0x1F);
            }
            return true;
        }
    }
}
