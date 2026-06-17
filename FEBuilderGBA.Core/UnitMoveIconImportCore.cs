// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Unit Move Icon import seam (#1177).
//
// Sibling of WaitIconImportCore (#991). Ports the write-back halves of WinForms
// ImageUnitMoveIconFrom:
//   * Import      — ImportButton_Click: validate the imported sheet dims (WF
//     normalizes to 32x480 via ConvertSizeFormat first), encode the indexed
//     pixels to 4bpp tiles, LZ77-compress + write to free space + repoint the
//     entry's +0 (P0) image pointer slot.
//   * ImportAP    — ImportAPButton_Click: write the RAW .romtcs.ap.bin bytes to
//     free space + repoint the entry's +4 (P4) AP pointer slot. WF refuses to
//     overwrite the OLD AP region in place when >=2 entries reference it
//     (GrepPointerAll over the table range), appending a fresh copy instead.
//
// Both run under the CALLER's ambient undo scope (the View owns
// UndoService.Begin/Commit/Rollback). A defensive rom.Data snapshot — taken
// LAZILY only after validation/encode succeeds — guarantees a FAILED import
// mutates ZERO bytes (length-aware restore: a free-space resize-append can grow
// rom.Data, so down-resize to the snapshot length BEFORE the in-place copy).
// Models the #885/#923 snapshot-restore pattern.
//
// Compression assumptions (verified against WF):
//   * Sheet (P0): LZ77  (WriteImageData(P0, image, useLZ77=true)).
//   * AP    (P4): RAW   (File.ReadAllBytes -> WriteBinaryData, no LZ77).
using System;
using System.Collections.Generic;
using FEBuilderGBA; // ROM, U, LZ77, ImageImportCore, R live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform write-back seam for the Unit Move Icon editor's sheet +
    /// AP imports (#1177). ROM-MUTATING; runs inside the caller's ambient undo
    /// scope.
    /// </summary>
    public static class UnitMoveIconImportCore
    {
        /// <summary>Sheet width = 4*8 = 32px (WF DrawMoveUnitIcon).</summary>
        public const int SHEET_WIDTH = 4 * 8;

        /// <summary>
        /// Validate + import a move-icon sheet at <paramref name="entryAddr"/>
        /// (the 8-byte table entry). On success the sprite pointer (+0, P0) is
        /// repointed to freshly-LZ77-written tile data. Returns "" on success or
        /// a localized error string on failure — with ZERO surviving mutation on
        /// any failure (defensive length-aware snapshot restore).
        /// </summary>
        /// <param name="indexedPixels">Already-remapped indexed pixels (one byte
        /// per pixel, row-major). The caller remaps to the shared self-army unit
        /// palette first — the entry has no palette slot (WaitIcon contract).</param>
        /// <param name="width">Must be 32 (the move-icon sheet width).</param>
        /// <param name="height">Must be a positive multiple of 8.</param>
        public static string Import(ROM rom, uint entryAddr, byte[] indexedPixels, int width, int height)
        {
            if (rom == null) return R._("ROM is not loaded.");
            if (indexedPixels == null) return R._("No image data.");

            // WF normalizes to 32x480 before ImageToByte16Tile. A natural sheet
            // is 32 wide and a multiple of 8 tall (a taller sheet is valid — WF
            // only rejects width != 32 or a non-tile-aligned size). We do NOT
            // impose the 480 ceiling; the caller has already normalized.
            if (width != SHEET_WIDTH)
                return R._("The image width must be {0} pixels (got {1}).", SHEET_WIDTH, width);
            if (height <= 0 || height % 8 != 0)
                return R._("The image height must be a positive multiple of 8 (got {0}).", height);

            // The entry must be in-bounds for the +0 pointer write. ulong
            // arithmetic so a large entryAddr can't wrap uint and bypass the
            // guard (Copilot PR review on #1225).
            if ((ulong)entryAddr + 8UL > (ulong)rom.Data.Length)
                return R._("The move icon entry address is out of range.");

            if (indexedPixels.Length < width * height)
                return R._("Image data is too small for {0}x{1}.", width, height);

            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tiles == null || tiles.Length == 0)
                return R._("Failed to encode move icon tiles.");

            // Defensive snapshot for the byte-identical restore on fault. Taken
            // LAZILY — only AFTER encode/validate succeeds, just before the first
            // write — so a rejected import never clones the ROM.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // WriteCompressedToROM: LZ77-compress, append to free space, and
                // repoint the +0 (P0) GBA pointer slot — all through the ambient
                // undo scope. NOT_FOUND => no free space / compression failure.
                uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, tiles, entryAddr + 0);
                if (writeAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write move icon image. Check ROM free space.");
                }
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Move icon import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Import RAW AP (Animated Parts) bytes at <paramref name="entryAddr"/>
        /// and repoint the entry's +4 (P4) AP pointer. Ports WF
        /// <c>ImportAPButton_Click</c>'s write-back. On success "" is returned; on
        /// any failure a localized error string and ZERO surviving mutation.
        /// <para>
        /// The new AP is ALWAYS appended to free space (never overwritten in
        /// place), so a previously SHARED region (referenced by &gt;=2 table
        /// entries) is automatically left intact for the other referencers — the
        /// shared-region safety WF protects with its in-place-vs-append branch is
        /// inherent here. WF's in-place reuse for an UNSHARED region is a pure
        /// allocation optimization; leaving the old unshared bytes as recyclable
        /// free space is harmless. <see cref="IsApRegionShared"/> is exposed for a
        /// caller that wants to WARN before re-pointing a shared region.
        /// </para>
        /// </summary>
        /// <param name="apBytes">RAW .romtcs.ap.bin bytes (no LZ77).</param>
        public static string ImportAP(ROM rom, uint entryAddr, byte[] apBytes)
        {
            if (rom == null) return R._("ROM is not loaded.");
            if (apBytes == null || apBytes.Length == 0) return R._("No AP data.");

            // ulong arithmetic so a large entryAddr can't wrap uint and bypass
            // the guard (Copilot PR review on #1225).
            if ((ulong)entryAddr + 8UL > (ulong)rom.Data.Length)
                return R._("The move icon entry address is out of range.");

            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // WriteRawToROM appends + repoints the +4 (P4) pointer slot through
                // the ambient undo scope. NOT_FOUND => no free space.
                uint writeAddr = ImageImportCore.WriteRawToROM(rom, apBytes, entryAddr + 4);
                if (writeAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write AP data. Check ROM free space.");
                }
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("AP import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Read the RAW AP bytes at the given GBA AP pointer for export, sized by
        /// <see cref="ImageUtilAPCore.CalcAPLength"/> (padded, WF-parity). Returns
        /// null on a zero/unsafe pointer or an unparseable AP region.
        /// </summary>
        public static byte[] ReadApBytes(ROM rom, uint apGba)
        {
            if (rom == null) return null;
            uint apOff = U.toOffset(apGba);
            if (!U.isSafetyOffset(apOff, rom)) return null;
            uint len = ImageUtilAPCore.CalcAPLength(rom.Data, apOff);
            if (len == 0) return null;
            if ((ulong)apOff + len > (ulong)rom.Data.Length) return null;
            return rom.getBinaryData(apOff, len);
        }

        /// <summary>
        /// True when the AP region at <paramref name="apGba"/> is referenced by
        /// &gt;=2 entries in the move-icon table (mirrors WF GrepPointerAll over
        /// the table range). <see cref="ImportAP"/> appends fresh regardless, so
        /// this is purely advisory — a caller can use it to WARN the user before
        /// re-pointing a shared region.
        /// </summary>
        public static bool IsApRegionShared(ROM rom, uint apGba, uint tableBase, int dataCount)
        {
            if (apGba == 0 || dataCount <= 0) return false;
            uint apOff = U.toOffset(apGba);
            if (!U.isSafetyOffset(apOff, rom)) return false;

            uint start = tableBase;
            uint end = tableBase + (uint)dataCount * 8u;
            if (end > (uint)rom.Data.Length) end = (uint)rom.Data.Length;
            List<uint> refs = U.GrepPointerAll(rom.Data, U.toPointer(apOff), start, end);
            return refs.Count >= 2;
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
