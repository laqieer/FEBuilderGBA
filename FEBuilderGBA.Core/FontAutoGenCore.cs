// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform desktop-font auto-generation seam (#1232) — the Avalonia Font
// editor's "Load .ttf/.otf" + Auto-Generate flow, ported GUI-free from WinForms
// FontForm (L1105 LoadFontFromFile / L1150 AutoGenerateFont).
//
// WinForms FontForm rasterizes a desktop TrueType / OpenType font into a ROM
// glyph bitmap via System.Drawing GDI (ImageUtil.AutoGenerateFont +
// ImageUtil.Image4ToByte). This seam drives that purely through the
// platform-neutral IFontRasterizer (#796), so the same auto-gen works on
// Windows / Linux / macOS via FEBuilderGBA.SkiaSharp.SkiaFontRasterizer.
//
// The write-back reuses FontGlyphRenderCore.ImportGlyph (#1165): in-place update
// for an existing glyph, MakeNewFontData + append + chain-link for a new one,
// ambient undo, and byte-identical fault restore. RasterizeGlyph returns a
// PACKED 64-byte 2bpp tile, but ImportGlyph wants a 16x16 one-index-per-pixel
// buffer (it re-packs internally), so this helper UNPACKS the rasterizer output
// back to 256 indices before handing it off.
using System;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// GUI-free desktop-font glyph auto-generator (#1232). Rasterizes one
    /// character from a .ttf/.otf file (or installed family) into a ROM font
    /// glyph via the platform-neutral <see cref="IFontRasterizer"/> seam, then
    /// writes it back through <see cref="FontGlyphRenderCore.ImportGlyph"/>
    /// (ambient undo + byte-identical fault restore). Never throws.
    /// </summary>
    public static class FontAutoGenCore
    {
        /// <summary>
        /// Rasterize <paramref name="character"/> with <paramref name="rasterizer"/>
        /// /<paramref name="font"/> and write the resulting 16x16 glyph into the
        /// item (true) or serif (false) font for the engine character code
        /// <paramref name="moji"/>.
        ///
        /// Runs under the caller's ambient undo scope: an existing glyph is
        /// updated in place; a new one is appended + chain-linked. On any fault
        /// the ROM is restored byte-identical (delegated to
        /// <see cref="FontGlyphRenderCore.ImportGlyph"/>). The rasterizer's
        /// reported advance width is preserved (passed as the explicit width so
        /// it is not re-derived from the pixels).
        /// </summary>
        /// <param name="rom">Target ROM (mutated on success).</param>
        /// <param name="rasterizer">The desktop-font rasterizer (the Avalonia
        /// layer supplies a SkiaSharp implementation; tests inject a stub).</param>
        /// <param name="font">Typeface / size selector (see <see cref="FontSpec"/>;
        /// a <c>FontFilePath</c> loads a .ttf/.otf file).</param>
        /// <param name="character">The single character to rasterize (a 1-length
        /// string; surrogate pairs pass whole).</param>
        /// <param name="moji">The engine character code of the target glyph slot
        /// (from the editor's selected list row).</param>
        /// <param name="isItemFont">Item-font (true) vs serif/text-font (false).</param>
        /// <param name="verticalOffset">Extra vertical pixel shift forwarded to
        /// the rasterizer (WF <c>V.Offset</c>).</param>
        /// <returns><c>""</c> on success, or a localized error string. Never
        /// throws and never leaves a partial mutation.</returns>
        public static string AutoGenerateGlyph(ROM rom, IFontRasterizer rasterizer,
            FontSpec font, string character, uint moji, bool isItemFont, int verticalOffset)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (rasterizer == null) return R._("No font rasterizer is available.");
            if (string.IsNullOrEmpty(character)) return R._("No character to generate.");

            // Rasterize the glyph into a packed 64-byte 2bpp tile. A rasterizer
            // fault must never throw out of here — convert to a localized error so
            // the never-throws contract holds (nothing has been written yet).
            byte[] packed;
            int glyphWidth;
            try
            {
                packed = rasterizer.RasterizeGlyph(font, character, isItemFont,
                    verticalOffset, out glyphWidth);
            }
            catch (Exception ex)
            {
                return R._("Font auto-generation failed: {0}", ex.Message);
            }

            if (packed == null || packed.Length < FontGlyphRenderCore.GLYPH_BITMAP_BYTES)
                return R._("Font auto-generation produced no glyph data.");

            // Unpack the 64-byte 2bpp tile (4 px/byte, low 2 bits = leftmost) to a
            // 16x16 one-index-per-pixel buffer, which ImportGlyph re-packs.
            byte[] indexedPixels = UnpackGlyphBytes(packed);

            // ImportGlyph owns the snapshot/restore + ambient undo. Pass the
            // rasterizer's advance width explicitly so the auto-gen width wins
            // (>= 0 => the supplied value is used, not re-derived from pixels).
            return FontGlyphRenderCore.ImportGlyph(rom, isItemFont, moji, indexedPixels,
                FontGlyphRenderCore.GLYPH_W, FontGlyphRenderCore.GLYPH_H,
                explicitWidth: glyphWidth, manageSnapshot: true);
        }

        /// <summary>
        /// Unpack a 64-byte 2bpp font tile into a 16x16 one-index-per-pixel buffer
        /// (row-major, 1 byte/pixel, values 0..3). Inverse of
        /// <see cref="FontGlyphRenderCore.PackGlyphBytes"/> / WF Image4ToByte:
        /// 4 horizontal pixels per byte, low 2 bits = leftmost.
        /// </summary>
        public static byte[] UnpackGlyphBytes(byte[] packed)
        {
            int w = FontGlyphRenderCore.GLYPH_W;
            int h = FontGlyphRenderCore.GLYPH_H;
            byte[] idx = new byte[w * h];
            if (packed == null) return idx;

            int x = 0, y = 0;
            for (int i = 0; i < FontGlyphRenderCore.GLYPH_BITMAP_BYTES && i < packed.Length; i++)
            {
                byte a = packed[i];
                for (int sub = 0; sub < 4; sub++)
                {
                    int px = x + sub;
                    if (px < w && y < h)
                        idx[y * w + px] = (byte)((a >> (sub * 2)) & 0x03);
                }
                x += 4;
                if (x >= w) { x = 0; y++; }
            }
            return idx;
        }
    }
}
