// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform Chinese-font bulk IMPORT seam (#1268, Slice 2 of #1166) — port
// of WinForms FontZHForm bulk re-import.
//
// Reads a `.fontall.txt` manifest (char<TAB>type<TAB>width<TAB>filename) and
// re-imports every glyph as ONE atomic transaction: all rows commit (one undo
// record via the caller's ambient scope) or the ROM is restored byte-identical.
//
// The character code (moji) is recovered from the PNG filename's hex suffix
// (`<type>_<mojiHex>.png`, written by FontBulkExportZHCore). The PNG -> indexed-
// pixels decode is delegated to a caller callback so Core stays GUI-free.
//
// FontBulkExportZHCore writes a 16x16 PNG (the 13px ZH glyph shifted down by 2 px
// for item / 1 px for serif — the WF ConvertVanillaFontSizeBitmap geometry). The
// caller's loader therefore returns 16x16 indexed pixels; this seam UN-SHIFTS them
// back to a 16x13 buffer (the inverse BitBlt) before feeding FontGlyphZHCore.
// ImportGlyphZH, so export -> import-all -> export is a clean round-trip.
using System;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Decoded glyph pixels supplied by the caller's PNG loader for the ZH bulk
    /// import. <see cref="Indexed"/> is one byte per pixel (0..3, remapped to the
    /// 4-color ZH font palette); <see cref="Width"/>/<see cref="Height"/> are the
    /// loaded PNG dims (expected 16x16 — the vanilla-size export geometry).
    /// </summary>
    public sealed class FontGlyphZHPixels
    {
        public byte[] Indexed = Array.Empty<byte>();
        public int Width;
        public int Height;
    }

    /// <summary>
    /// GUI-free bulk import of a `.fontall.txt` manifest back into the Chinese
    /// item + serif fonts (#1268). BULK-ATOMIC: one undo record or byte-identical
    /// restore. Runs under the caller's ambient undo scope.
    /// </summary>
    public static class FontBulkImportZHCore
    {
        /// <summary>
        /// Parse <paramref name="manifestText"/> and import every glyph row. For
        /// each row, <paramref name="loadGlyph"/> is invoked with the manifest's
        /// PNG filename (column 4) plus the type string ("item"/"text"); it returns
        /// the remapped 16x16 indexed pixels (the vanilla-size export geometry) or
        /// null to signal a load error (aborts + restores). The manifest's stored
        /// advance width (column 3) is preserved on import so export -> import ->
        /// export round-trips widths. Returns "" on success or a localized error
        /// (with ZERO surviving mutation on any failure — ONE snapshot for the
        /// whole transaction).
        /// </summary>
        public static string ImportAll(ROM rom, string manifestText,
            Func<string, string, FontGlyphZHPixels> loadGlyph)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (!FontGlyphZHCore.IsZHRom(rom)) return R._("This is not a Chinese ROM.");
            if (manifestText == null) return R._("No manifest data.");
            if (loadGlyph == null) return R._("No image loader.");

            // Single snapshot for the whole transaction: a fault on row N restores
            // rows 0..N-1 too (BULK-ATOMIC). Per-glyph ImportGlyphZH calls run with
            // manageSnapshot:false so they do NOT each clone the ROM — a real ZH ROM
            // has thousands of glyphs, so a per-row clone would be O(N × romSize) and
            // OOM. The OUTER snapshot is what guarantees the whole-batch atomic restore.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                string[] lines = manifestText.Replace("\r\n", "\n").Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.TrimEnd('\r');
                    // Only blank lines + // comments/header are skipped silently —
                    // every other line is a DATA row and is validated (no silent
                    // drop / misclassification, so a "successful" import can't omit
                    // rows).
                    if (line.Trim().Length == 0) continue;
                    if (line.StartsWith("//")) continue; // comment / header

                    string[] sp = line.Split('\t');
                    if (sp.Length < 4)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Invalid font manifest row (expected 4 tab-separated columns: char, type, width, filename): {0}", line);
                    }

                    // Validate the type column (item / text) instead of silently
                    // defaulting an unknown type to serif.
                    string type = sp[1].Trim();
                    if (type != "item" && type != "text")
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Invalid font type in manifest (expected 'item' or 'text'): {0}", type);
                    }
                    bool isItemFont = (type == "item");

                    // Advance width (column 3) must be an integer in 0..16 (the
                    // glyph is 16px wide). A non-numeric OR out-of-range width is a
                    // manifest error (reject rather than silently deriving from
                    // pixels or clamping).
                    int width = ParseWidth(sp[2]);
                    if (width < 0 || width > FontGlyphZHCore.GLYPH_W)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Invalid font width in manifest (expected an integer 0..{0}): {1}",
                            FontGlyphZHCore.GLYPH_W, sp[2]);
                    }

                    string pngName = sp[3].Trim();
                    if (pngName.Length == 0)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Invalid font manifest row (empty filename): {0}", line);
                    }

                    // Recover the engine char code from the filename hex suffix
                    // (<type>_<mojiHex>.png). A data row whose filename can't be
                    // keyed is a real manifest error — FAIL (atomic restore) rather
                    // than silently omitting the glyph.
                    if (!FontBulkImportCore.TryParseMojiFromFilename(pngName, out uint moji))
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Cannot determine the character code from the font filename: {0}", pngName);
                    }

                    FontGlyphZHPixels px = loadGlyph(pngName, type);
                    if (px == null || px.Indexed == null)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Failed to load glyph image: {0}", pngName);
                    }

                    // Un-shift the 16x16 vanilla-size PNG back to a 16x13 buffer
                    // (the inverse of FontBulkExportZHCore's ConvertVanillaFontSize
                    // BitBlt) so ImportGlyphZH sees the native 16x13 glyph again.
                    byte[] indexed13 = UnshiftTo16x13(px.Indexed, px.Width, px.Height, isItemFont);
                    if (indexed13 == null)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Invalid font glyph image size: {0}", pngName);
                    }

                    // manageSnapshot:false — the per-row import must NOT clone the ROM
                    // (the OUTER snapshot above already covers the atomic rollback). On
                    // a per-row error we restore the whole batch here.
                    string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont, moji,
                        indexed13, FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H,
                        explicitWidth: width, manageSnapshot: false);
                    if (!string.IsNullOrEmpty(err))
                    {
                        RestoreSnapshot(rom, snap);
                        return err;
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Font bulk import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Reverse FontBulkExportZHCore's vanilla-size shift: take the first 13 rows
        /// of the 16x16 indexed PNG starting at y = shift (item 2 / serif 1) and
        /// return a 16x13 indexed buffer. Returns null when the source is not 16x16.
        /// Exposed for tests.
        /// </summary>
        public static byte[] UnshiftTo16x13(byte[] indexed16x16, int width, int height, bool isItemFont)
        {
            int w = FontGlyphZHCore.GLYPH_W;   // 16
            int dstH = FontGlyphZHCore.GLYPH_H; // 13
            if (indexed16x16 == null) return null;
            if (width != w || height != w) return null;            // must be 16x16
            if (indexed16x16.Length < w * w) return null;

            int shift = FontBulkExportZHCore.VanillaShift(isItemFont);
            byte[] dst = new byte[w * dstH]; // 16x13, default index 0 (background)
            for (int y = 0; y < dstH; y++)
            {
                int sy = y + shift;
                if (sy < 0 || sy >= w) continue; // outside the 16px source (stays bg)
                Array.Copy(indexed16x16, (sy * w), dst, (y * w), w);
            }
            return dst;
        }

        // Parse the manifest width column; returns -1 on a non-numeric value (the
        // caller treats -1 as a manifest error and ABORTS).
        static int ParseWidth(string s)
        {
            return int.TryParse(s.Trim(), out int width) && width >= 0 ? width : -1;
        }

        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
