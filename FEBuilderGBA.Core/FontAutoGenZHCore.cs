// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform Chinese-font auto-generation seam (#1268, Slice 2 of #1166) —
// the Avalonia ZH Font editor's "Load .ttf/.otf + Auto-Generate" flow, ported
// GUI-free from WinForms FontZHForm.AutoGenbutton_Click (ImageUtil.
// AutoGenerateFont + Image4ToByteZH).
//
// WinForms rasterizes a desktop TrueType/OpenType font into a ROM glyph via
// System.Drawing GDI. This seam drives that through the platform-neutral
// IFontRasterizer (#796) — the SAME rasterizer the main font auto-gen uses
// (FontAutoGenCore) — so it works on Windows / Linux / macOS via
// FEBuilderGBA.SkiaSharp.SkiaFontRasterizer.
//
// The rasterizer emits a 16x16 (64-byte) 2bpp tile (indices 0/2/3 item, 0/3
// text). The ZH glyph is 16x13 with the same 0..3 palette range (0=bg, 1=white,
// 2=gray, 3=black), so this helper UNPACKS the 64-byte tile, takes the top 16x13
// region, and writes it back through FontGlyphZHCore.ImportGlyphZH (validate-all-
// before-mutate, ambient undo, byte-identical fault restore). Never throws.
using System;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// GUI-free Chinese-font glyph auto-generator (#1268). Rasterizes one
    /// character from a .ttf/.otf file (or installed family) into a 16x13 ROM ZH
    /// font glyph via the platform-neutral <see cref="IFontRasterizer"/>, then
    /// writes it back through <see cref="FontGlyphZHCore.ImportGlyphZH"/>. Never
    /// throws and never leaves a partial mutation.
    /// </summary>
    public static class FontAutoGenZHCore
    {
        /// <summary>
        /// Rasterize <paramref name="character"/> with <paramref name="rasterizer"/>
        /// /<paramref name="font"/> and write the resulting 16x13 glyph into the
        /// item (true) or serif (false) Chinese font for the engine character code
        /// <paramref name="moji"/>.
        ///
        /// Runs under the caller's ambient undo scope (ImportGlyphZH owns the
        /// snapshot/restore). The rasterizer's reported advance width is preserved
        /// (passed as the explicit width so it is not re-derived from the pixels).
        /// </summary>
        /// <param name="rom">Target ROM (mutated on success).</param>
        /// <param name="rasterizer">The desktop-font rasterizer (the Avalonia layer
        /// supplies a SkiaSharp implementation; tests inject a stub).</param>
        /// <param name="font">Typeface / size selector (a <c>FontFilePath</c> loads
        /// a .ttf/.otf file).</param>
        /// <param name="character">The single character to rasterize.</param>
        /// <param name="moji">The engine character code of the target glyph slot.</param>
        /// <param name="isItemFont">Item-font (true) vs serif/text-font (false).</param>
        /// <param name="verticalOffset">Extra vertical pixel shift forwarded to the
        /// rasterizer.</param>
        /// <returns><c>""</c> on success, or a localized error string.</returns>
        public static string AutoGenerateGlyphZH(ROM rom, IFontRasterizer rasterizer,
            FontSpec font, string character, uint moji, bool isItemFont, int verticalOffset)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (!FontGlyphZHCore.IsZHRom(rom)) return R._("This is not a Chinese ROM.");
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

            // The rasterizer always emits a 16x16 (64-byte) tile.
            const int RASTER_TILE_BYTES = 64;
            if (packed == null || packed.Length < RASTER_TILE_BYTES)
                return R._("Font auto-generation produced no glyph data.");

            // Unpack the 64-byte 2bpp tile to a 16x16 one-index-per-pixel buffer,
            // then keep the top 16x13 region (the ZH glyph box). The rasterizer's
            // index range (0/2/3) already matches the ZH 0..3 palette, so no remap.
            byte[] indexed13 = UnpackTo16x13(packed);

            // ImportGlyphZH owns the snapshot/restore + ambient undo. Pass the
            // rasterizer's advance width explicitly so the auto-gen width wins
            // (>= 0 => the supplied value is used, not re-derived from pixels).
            return FontGlyphZHCore.ImportGlyphZH(rom, isItemFont, moji, indexed13,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, explicitWidth: glyphWidth);
        }

        /// <summary>
        /// Unpack a 64-byte 2bpp 16x16 font tile into a 16x13 one-index-per-pixel
        /// buffer (row-major, 1 byte/pixel, values 0..3) — the top 13 rows of the
        /// 16x16 tile. 4 horizontal pixels per byte, low 2 bits = leftmost (the WF
        /// Image4ToByte packing). Exposed for tests.
        /// </summary>
        public static byte[] UnpackTo16x13(byte[] packed)
        {
            int w = FontGlyphZHCore.GLYPH_W;   // 16
            int h = FontGlyphZHCore.GLYPH_H;   // 13
            byte[] idx = new byte[w * h];
            if (packed == null) return idx;

            // The source tile is 16 wide; each row is 16/4 = 4 bytes. Only the top
            // 13 rows (52 bytes) are needed for the ZH glyph.
            int x = 0, y = 0;
            int rowBytes = w / 4; // 4
            for (int i = 0; i < rowBytes * h && i < packed.Length; i++)
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
