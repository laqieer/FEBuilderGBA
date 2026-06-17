// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform main-font bulk IMPORT seam (#1165) — port of WinForms
// FontForm.ImportAllButton_Click / ImportAll.
//
// Reads a `.fontall.txt` manifest (char<TAB>type<TAB>width<TAB>filename) and
// re-imports every glyph as ONE atomic transaction: all rows commit (one undo
// record via the caller's ambient scope) or the ROM is restored byte-identical.
//
// The character code (moji) is recovered from the PNG filename's hex suffix
// (`<type>_<mojiHex>.png`, written by FontBulkExportCore) — Core has no
// char→moji converter (WF uses U.ConvertMojiCharToUnit, a WinForms helper). The
// PNG → 16x16 4-color indexed-pixels decode is delegated to a caller callback
// so Core stays GUI-free.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Decoded glyph pixels supplied by the caller's PNG loader.
    /// <see cref="Indexed"/> is one byte per pixel (0..3, remapped to the
    /// 4-color font palette); <see cref="Width"/>/<see cref="Height"/> must be
    /// 16x16 (rejected otherwise, no mutation).
    /// </summary>
    public sealed class FontGlyphPixels
    {
        public byte[] Indexed = Array.Empty<byte>();
        public int Width;
        public int Height;
    }

    /// <summary>
    /// GUI-free bulk import of a `.fontall.txt` manifest back into the item +
    /// serif fonts (#1165). BULK-ATOMIC: one undo record or byte-identical
    /// restore. Runs under the caller's ambient undo scope.
    /// </summary>
    public static class FontBulkImportCore
    {
        /// <summary>
        /// Parse <paramref name="manifestText"/> and import every glyph row. For
        /// each row, <paramref name="loadGlyph"/> is invoked with the absolute
        /// PNG path (resolved by the caller); it returns the remapped 16x16
        /// indexed pixels or null to signal a load error (aborts + restores).
        /// Returns "" on success or a localized error (with ZERO surviving
        /// mutation on any failure).
        /// </summary>
        public static string ImportAll(ROM rom, string manifestText,
            Func<string, string, FontGlyphPixels> loadGlyph)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (manifestText == null) return R._("No manifest data.");
            if (loadGlyph == null) return R._("No image loader.");

            // Single snapshot for the whole transaction: a fault on row N restores
            // rows 0..N-1 too (BULK-ATOMIC).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                string[] lines = manifestText.Replace("\r\n", "\n").Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw;
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("//")) continue; // comment / header

                    string[] sp = line.Split('\t');
                    if (sp.Length < 4) continue;

                    string type = sp[1].Trim();
                    string pngName = sp[3].Trim();
                    if (pngName.Length == 0) continue;

                    bool isItemFont = (type == "item");

                    // Recover the engine char code from the filename hex suffix
                    // (<type>_<mojiHex>.png). Skip rows we can't key.
                    if (!TryParseMojiFromFilename(pngName, out uint moji)) continue;

                    FontGlyphPixels px = loadGlyph(pngName, type);
                    if (px == null)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Failed to load glyph image: {0}", pngName);
                    }

                    string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont, moji,
                        px.Indexed, px.Width, px.Height);
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
        /// Recover the moji code from a `<type>_<hex>.png` filename. Returns false
        /// when the suffix isn't valid hex (caller skips the row).
        /// </summary>
        public static bool TryParseMojiFromFilename(string pngName, out uint moji)
        {
            moji = 0;
            if (string.IsNullOrEmpty(pngName)) return false;

            int dot = pngName.LastIndexOf('.');
            string stem = dot >= 0 ? pngName.Substring(0, dot) : pngName;
            int us = stem.LastIndexOf('_');
            if (us < 0 || us + 1 >= stem.Length) return false;

            string hex = stem.Substring(us + 1);
            return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out moji);
        }

        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
