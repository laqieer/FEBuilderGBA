// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform main-font bulk EXPORT seam (#1165) — port of WinForms
// FontForm.ExportALLButton_Click / ExportALL.
//
// Writes a `.fontall.txt` manifest (one TAB-separated row per glyph:
// char<TAB>type<TAB>width<TAB>filename) plus one PNG per glyph. Rendering each
// glyph to a PNG file is delegated to a caller-supplied `writePng` callback so
// Core stays GUI-free (mirrors SkillConfigSkillSystemBulkExportCore's writeAnime
// delegate). READ-ONLY — never mutates the ROM.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// GUI-free bulk export of the main item+serif fonts to a `.fontall.txt`
    /// manifest + per-glyph PNGs (#1165). The caller supplies a <c>writePng</c>
    /// delegate that persists one glyph image (rendered via
    /// <see cref="FontGlyphRenderCore.RenderGlyph"/>) to <c>pngFilename</c>.
    /// </summary>
    public static class FontBulkExportCore
    {
        /// <summary>
        /// Enumerate the item + serif fonts and build the manifest text. For each
        /// glyph the <paramref name="writePng"/> callback is invoked with the
        /// glyph's rendered image + the PNG filename (relative to the manifest).
        /// </summary>
        /// <param name="userFontOnly">When true, only glyphs after
        /// <c>font_default_end</c> (user-added) are exported — matches WF's
        /// "export only user fonts" prompt.</param>
        /// <param name="writePng">Persist one glyph PNG. Receives the rendered
        /// image (Core disposes it after the call returns) + the bare filename.
        /// Return false to SKIP this glyph (it is omitted from the manifest and
        /// export continues with the next glyph); return true on success.</param>
        /// <returns>The manifest text (TSV), or "" if the ROM is unusable.</returns>
        public static string ExportAll(ROM rom, bool userFontOnly,
            Func<IImage, string, bool> writePng)
        {
            if (rom?.RomInfo == null || writePng == null) return "";

            var sb = new StringBuilder();
            sb.Append("//char\ttype\tWidth\tFilename\n");
            ExportOne(rom, isItemFont: true, userFontOnly, writePng, sb);
            ExportOne(rom, isItemFont: false, userFontOnly, writePng, sb);
            return sb.ToString();
        }

        static void ExportOne(ROM rom, bool isItemFont, bool userFontOnly,
            Func<IImage, string, bool> writePng, StringBuilder sb)
        {
            uint defaultEnd = rom.RomInfo.font_default_end;
            string type = isItemFont ? "item" : "text";

            var glyphs = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont);
            foreach (var g in glyphs)
            {
                if (userFontOnly && g.Addr <= defaultEnd) continue; // a default (ROM-shipped) glyph
                if (g.Width <= 0) continue;

                IImage img = FontGlyphRenderCore.RenderGlyph(rom, g.Addr, isItemFont);
                if (img == null) continue;

                // Filesystem-safe name: <type>_<mojiHex>.png (the raw char may be
                // an unrepresentable control code, so we key on the hex code).
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
    }
}
