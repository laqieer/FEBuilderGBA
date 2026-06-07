// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Unit Wait Icon import seam (#991).
//
// Ports the write-back half of WinForms ImageUnitWaitIconFrom.ImportButton_Click:
//   * validate the imported sheet dims -> animType byte (b2):
//       16x48 -> 0,  16x96 -> 1,  32x96 -> 2  (else localized error, NO mutation)
//   * encode the indexed pixels to 4bpp tiles (EncodeDirectTiles4bpp)
//   * LZ77-compress + write to free space + repoint the entry's +4 pointer slot
//     (WriteCompressedToROM owns the +4 GBA pointer)
//   * write the animType byte into W2 (u16 @ +2)
//
// The WF "force palette" interactive dialog is replaced by a nearest-color
// remap onto the shared self-army palette done by the caller BEFORE this seam
// (the wait-icon entry has no palette slot — see plan v2 HIGH-1), so this seam
// receives already-remapped indexed pixels.
//
// Atomicity: runs under the CALLER's ambient undo scope (the View owns
// _undoService.Begin/Commit/Rollback). A defensive rom.Data snapshot is kept so
// any fault — including a free-space resize-append inside WriteCompressedToROM —
// is restored byte-identical (length-aware: down-resize to the snapshot length
// BEFORE the in-place copy so a grown tail can't survive). Models the
// #885/#923 snapshot-restore pattern.
using System;
using FEBuilderGBA; // ROM, U, LZ77, ImageImportCore, R live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform write-back seam for the Unit Wait Icon editor's static
    /// PNG/BMP sheet import (#991). ROM-MUTATING; runs inside the caller's
    /// ambient undo scope.
    /// </summary>
    public static class WaitIconImportCore
    {
        /// <summary>
        /// Validate + import a static wait-icon sheet at <paramref name="entryAddr"/>
        /// (the 8-byte table entry). On success the sprite pointer (+4) is
        /// repointed to freshly-LZ77-written tile data and the animType byte
        /// (W2 @ +2) is set to the resolved b2. Returns "" on success or a
        /// localized error string on failure — with ZERO surviving mutation on
        /// any failure (defensive length-aware snapshot restore).
        /// </summary>
        /// <param name="indexedPixels">Already-remapped indexed pixels (one byte
        /// per pixel, row-major). The caller must remap to the shared self-army
        /// palette first — the entry has no palette slot.</param>
        public static string Import(ROM rom, uint entryAddr, byte[] indexedPixels, int width, int height)
        {
            if (rom == null) return R._("ROM is not loaded.");
            if (indexedPixels == null) return R._("No image data.");

            // Validate dims -> animType byte (b2). WF only accepts 16x48 / 16x96
            // / 32x96; anything else is a hard error with NO mutation.
            uint b2;
            if (width == 16 && height == 48) b2 = 0;
            else if (width == 16 && height == 96) b2 = 1;
            else if (width == 32 && height == 96) b2 = 2;
            else
            {
                return R._(
                    "The image size is not correct.\r\nIt must be one of:\r\n16x48\r\n16x96\r\n32x96\r\n\r\nSelected image size Width:{0} Height:{1}",
                    width, height);
            }

            // The entry must be in-bounds for the +2 / +4 writes.
            if (entryAddr + 8 > (uint)rom.Data.Length)
                return R._("The wait icon entry address is out of range.");

            // The +4 pointer slot must itself be a safe, pre-existing pointer
            // slot (WriteCompressedToROM owns it via write_p32). We do NOT
            // pre-validate the OLD pointer target (WF appends new data + repoints
            // regardless of the old value).
            if (indexedPixels.Length < width * height)
                return R._("Image data is too small for {0}x{1}.", width, height);

            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tiles == null || tiles.Length == 0)
                return R._("Failed to encode wait icon tiles.");

            // Defensive snapshot for the byte-identical restore on fault. The
            // caller's ambient undo scope captures the writes for UNDO; this
            // snapshot guarantees a FAILED import mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // WriteCompressedToROM: LZ77-compress, append to free space, and
                // repoint the +4 GBA pointer slot — all through the ambient undo
                // scope. NOT_FOUND => no free space / compression failure.
                uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, tiles, entryAddr + 4);
                if (writeAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write wait icon image. Check ROM free space.");
                }

                // Write the animType byte into W2 (u16 @ +2). Uses the ambient
                // write_u16 overload so it composes into the caller's scope.
                rom.write_u16(entryAddr + 2, (ushort)b2);
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Wait icon import failed: {0}", ex.Message);
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
