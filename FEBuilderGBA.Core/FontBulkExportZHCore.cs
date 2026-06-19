// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform Chinese-font bulk EXPORT seam (#1268, Slice 2 of #1166) — port
// of WinForms FontZHForm.ExportALLButton_Click / ExportALL.
//
// Writes a `.fontall.txt` manifest (one TAB-separated row per glyph:
// char<TAB>type<TAB>width<TAB>filename) plus one PNG per glyph for the ZH 44-byte
// (16x13, 40-byte 2bpp bitmap) font format. Rendering each glyph to a PNG file is
// delegated to a caller-supplied `writePng` callback so Core stays GUI-free
// (mirrors FontBulkExportCore.ExportAll's writePng delegate). READ-ONLY — never
// mutates the ROM.
//
// Unlike the main-font bulk export (FontBulkExportCore, native 16x16 glyphs), the
// ZH glyph is 16x13. WinForms ExportALL renders each glyph into a 16x16 canvas via
// ConvertVanillaFontSizeBitmap (shift the 13px glyph DOWN by 2 px for the item
// font, 1 px for the serif font), so the exported PNG matches the vanilla 16x16
// font cell. This seam reproduces that geometry; FontBulkImportZHCore reverses the
// shift (un-shift back to 16x13) before re-importing, so export -> import-all is a
// clean round-trip.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// GUI-free bulk export of the Chinese item+serif fonts to a `.fontall.txt`
    /// manifest + per-glyph 16x16 PNGs (#1268). The caller supplies a
    /// <c>writePng</c> delegate that persists one glyph image (rendered via
    /// <see cref="RenderVanillaSizeGlyph"/>) to <c>pngFilename</c>.
    /// </summary>
    public static class FontBulkExportZHCore
    {
        // The WF ConvertVanillaFontSizeBitmap vertical shift: the 13px ZH glyph is
        // BitBlt'd into a 16px canvas at y = shift (item 2, serif 1). The shift IS
        // recoverable from the type column, so the import path un-shifts.
        internal const int ITEM_SHIFT = 2;
        internal const int SERIF_SHIFT = 1;

        internal static int VanillaShift(bool isItemFont) => isItemFont ? ITEM_SHIFT : SERIF_SHIFT;

        /// <summary>
        /// Enumerate the item + serif Chinese fonts and build the manifest text.
        /// For each glyph the <paramref name="writePng"/> callback is invoked with
        /// the glyph's rendered 16x16 image + the PNG filename (relative to the
        /// manifest).
        /// </summary>
        /// <param name="userFontOnly">When true, only glyphs after
        /// <c>font_default_end</c> (user-added) are exported — matches WF's
        /// "export only user fonts" path.</param>
        /// <param name="writePng">Persist one glyph PNG. Receives the rendered
        /// image (Core disposes it after the call returns) + the bare filename.
        /// Return false to SKIP this glyph (it is omitted from the manifest and
        /// export continues with the next glyph); return true on success.</param>
        /// <returns>The manifest text (TSV), or "" if the ROM is unusable.</returns>
        public static string ExportAll(ROM rom, bool userFontOnly,
            Func<IImage, string, bool> writePng)
        {
            if (rom?.RomInfo == null || writePng == null) return "";
            if (!FontGlyphZHCore.IsZHRom(rom)) return "";
            if (CoreState.ImageService == null) return "";

            var sb = new StringBuilder();
            sb.Append("//char\ttype\tWidth\tFilename\n");
            ExportOne(rom, isItemFont: true, userFontOnly, writePng, sb);
            ExportOne(rom, isItemFont: false, userFontOnly, writePng, sb);
            return sb.ToString();
        }

        /// <summary>
        /// Count the DATA rows in a `.fontall.txt` manifest (non-blank, non-comment
        /// lines). The View uses this to detect a "nothing was exported" result — a
        /// "" return (hard failure) OR a header-only manifest both yield 0, so the
        /// caller can refuse to write an empty file / claim false success.
        /// </summary>
        public static int CountManifestDataRows(string manifest)
        {
            if (string.IsNullOrEmpty(manifest)) return 0;
            int count = 0;
            foreach (string raw in manifest.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                if (line.StartsWith("//")) continue; // comment / header
                count++;
            }
            return count;
        }

        static void ExportOne(ROM rom, bool isItemFont, bool userFontOnly,
            Func<IImage, string, bool> writePng, StringBuilder sb)
        {
            uint defaultEnd = rom.RomInfo.font_default_end;
            string type = isItemFont ? "item" : "text";

            var glyphs = FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont);
            foreach (var g in glyphs)
            {
                if (userFontOnly && g.Addr <= defaultEnd) continue; // a default (ROM-shipped) glyph
                // Skip empty (width 0) AND corrupt (width > 16) slots: the glyph is
                // 16px wide, so a stored width outside 1..16 is not a real glyph and
                // the import would reject it (keeps export -> import a clean round-trip).
                if (g.Width <= 0 || g.Width > FontGlyphZHCore.GLYPH_W) continue;

                IImage img = RenderVanillaSizeGlyph(rom, g.Addr, isItemFont);
                if (img == null) continue;

                // Filesystem-safe name: <type>_<mojiHex>.png (the raw char may be
                // an unrepresentable control code, so we key on the hex code — the
                // import path recovers the moji from this hex suffix).
                string pngName = type + "_" + U.ToHexString(g.Moji) + ".png";
                bool ok;
                try { ok = writePng(img, pngName); }
                finally { img.Dispose(); }
                if (!ok) continue;

                sb.Append(g.Name);
                sb.Append('\t');
                sb.Append(type);
                sb.Append('\t');
                sb.Append(g.Width);
                sb.Append('\t');
                sb.Append(pngName);
                sb.Append('\n');
            }
        }

        /// <summary>
        /// Render the glyph at <paramref name="addr"/> as a 16x16 RGBA image, the
        /// 13px ZH glyph shifted DOWN into a 16px canvas (item +2, serif +1) —
        /// the WF ConvertVanillaFontSizeBitmap geometry. The bottom rows stay
        /// transparent. Returns null on a bad addr / null ROM / null ImageService;
        /// never throws.
        /// </summary>
        public static IImage RenderVanillaSizeGlyph(ROM rom, uint addr, bool isItemFont)
        {
            if (rom == null || CoreState.ImageService == null) return null;

            using IImage glyph16x13 = FontGlyphZHCore.RenderGlyphZH(rom, addr, isItemFont);
            if (glyph16x13 == null) return null;

            return ShiftInto16x16(glyph16x13, VanillaShift(isItemFont));
        }

        /// <summary>
        /// Copy a 16x13 RGBA glyph into a fresh transparent 16x16 canvas, shifted
        /// down by <paramref name="shift"/> rows (the WF ConvertVanillaFontSizeBitmap
        /// BitBlt). Exposed for tests + the import un-shift. Returns null on bad
        /// input. The shift is fully invertible: the 13 source rows land at
        /// y = shift .. shift+12, all in-bounds for shift in {1,2}.
        /// </summary>
        internal static IImage ShiftInto16x16(IImage glyph16x13, int shift)
        {
            if (CoreState.ImageService == null || glyph16x13 == null) return null;
            int w = FontGlyphZHCore.GLYPH_W;   // 16
            int srcH = FontGlyphZHCore.GLYPH_H; // 13

            byte[] src = glyph16x13.GetPixelData(); // RGBA
            if (src == null || src.Length < w * srcH * 4) return null;

            var dst = CoreState.ImageService.CreateImage(w, w); // 16x16
            byte[] outPx = new byte[w * w * 4]; // default transparent
            for (int y = 0; y < srcH; y++)
            {
                int dy = y + shift;
                if (dy < 0 || dy >= w) continue; // clipped off the 16px canvas
                Array.Copy(src, (y * w) * 4, outPx, (dy * w) * 4, w * 4);
            }
            dst.SetPixelData(outPx);
            return dst;
        }
    }
}
