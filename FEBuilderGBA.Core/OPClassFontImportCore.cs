// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform OP Class Font glyph import seam (#999).
//
// Mirrors the wait-icon write-back pattern (WaitIconImportCore): the OP class
// font is a shared-palette, single-image-pointer editor. WinForms
// OPClassFontForm can Write the glyph image pointer + Export PNG but has no
// Import path; this seam adds the write-back half:
//   * validate the imported glyph dims (parameterized — NOT hardcoded 32x32 so a
//     future FE8U 16x32 editor can reuse this; width/height must be positive
//     multiples of 8)
//   * encode the indexed pixels to 4bpp tiles (EncodeDirectTiles4bpp)
//   * LZ77-compress + write to free space + repoint the glyph entry's D0 pointer
//     slot (WriteCompressedToROM owns the D0 GBA pointer at glyphPtrAddr)
//
// The shared op_class_font_palette is used by ALL glyphs, so the caller remaps
// the imported image onto it (nearest color) BEFORE this seam (the glyph entry
// has no per-glyph palette slot) — this seam receives already-remapped indexed
// pixels.
//
// Atomicity: runs under the CALLER's ambient undo scope (the View owns
// _undoService.Begin/Commit/Rollback). A defensive rom.Data snapshot is kept so
// any fault — including a free-space resize-append inside WriteCompressedToROM —
// is restored byte-identical (length-aware: down-resize to the snapshot length
// BEFORE the in-place copy so a grown tail can't survive). Models the
// #885/#923 snapshot-restore pattern (same as WaitIconImportCore).
using System;
using FEBuilderGBA; // ROM, U, LZ77, ImageImportCore, R live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform write-back seam for the OP Class Font editor's static
    /// PNG/BMP glyph import (#999). ROM-MUTATING; runs inside the caller's
    /// ambient undo scope.
    /// </summary>
    public static class OPClassFontImportCore
    {
        /// <summary>
        /// Validate + import a static OP class font glyph at
        /// <paramref name="glyphPtrAddr"/> (the 4-byte D0 pointer slot). On
        /// success the glyph pointer (D0) is repointed to freshly-LZ77-written
        /// tile data. Returns "" on success or a localized error string on
        /// failure — with ZERO surviving mutation on any failure (defensive
        /// length-aware snapshot restore).
        /// </summary>
        /// <param name="indexedPixels">Already-remapped indexed pixels (one byte
        /// per pixel, row-major). The caller must remap to the shared
        /// op_class_font_palette first — the glyph entry has no palette slot.</param>
        public static string Import(ROM rom, uint glyphPtrAddr, byte[] indexedPixels, int width, int height)
        {
            if (rom == null) return R._("ROM is not loaded.");
            if (indexedPixels == null) return R._("No image data.");
            // Parameterized (NOT hardcoded 32x32) so a future FE8U 16x32 editor can reuse this.
            if (width <= 0 || height <= 0 || width % 8 != 0 || height % 8 != 0)
                return R._("The image size is not correct. Width/height must be positive multiples of 8. Selected: {0}x{1}", width, height);
            // Overflow-safe: long math so glyphPtrAddr+4 / width*height can't wrap.
            if (glyphPtrAddr == 0 || (long)glyphPtrAddr + 4 > rom.Data.Length)
                return R._("The glyph entry address is out of range.");
            if (indexedPixels.Length < (long)width * height)
                return R._("Image data is too small for {0}x{1}.", width, height);

            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tiles == null || tiles.Length == 0) return R._("Failed to encode glyph tiles.");

            // Defensive snapshot: a FAILED import mutates ZERO bytes (the caller's ambient
            // undo scope captures the success-path writes for UNDO).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // LZ77-compress + append to free space + repoint the D0 GBA pointer slot at
                // glyphPtrAddr, all through the ambient undo scope. NOT_FOUND => no free space.
                uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, tiles, glyphPtrAddr);
                if (writeAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write glyph image. Check ROM free space.");
                }
                return "";
            }
            catch (Exception ex)
            {
                // Never throws (WaitIcon parity): restore byte-identical and return the error.
                RestoreSnapshot(rom, snap);
                return R._("OP class font import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
