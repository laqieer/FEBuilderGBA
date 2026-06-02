// SPDX-License-Identifier: GPL-3.0-or-later
// Generic Enemy Portrait editor Image Import — Avalonia parity for the
// WinForms ImageGenericEnemyPortraitForm Export/Import buttons (#907).
//
// SCOPE (Copilot plan review — 4 REQUIRED CORRECTIONS baked in):
//   The WF form (ImageGenericEnemyPortraitForm.cs:112-161) treats each
//   generic-enemy-portrait entry as TWO ROM pointer slots:
//     * image   @ entryAddr + 0x00  (RAW 4bpp 32x32 tilesheet, 512 bytes)
//     * palette @ entryAddr + 0x20  (RAW 16-color GBA palette, 32 bytes)
//   The palette slot is FIXED at +0x20 (8 image slots + 8 palette slots are
//   reserved per table) across FE6/FE7/FE8 — NOT count*4 (FE6 uses 7 image
//   slots, FE7 6, FE8 8). CORRECTION 4: never derive the palette slot from
//   the live count; always entryAddr + 0x20.
//
//   CORRECTION 1/2: there are exactly TWO ROM pointer slots. In WF the image
//   pointer slot @ +0 is written by the trailing WriteButton.PerformClick()
//   (it repoints the D0 image control), and the palette slot @ +0x20 is
//   written via write_p32. This Core helper writes BOTH slots explicitly so
//   the Avalonia caller never relies on a hidden "Write" button: a dangling
//   image pointer can never result.
//
// ENCODE: reuses ImageImportCore.EncodeDirectTiles4bpp (== WF
//   ImageUtil.ImageToByte16Tile, confirmed byte-identical in #898) — RAW
//   4bpp tiles, NO LZ77 (the generic-enemy-portrait image is uncompressed).
//
// WRITE: mirrors the #901 TSAImageImportCore single-region primitive but RAW
//   (uncompressed): recycle the OLD raw region (known fixed length via
//   Address.AddPointer), RecycleAddress.WriteAmbient the new bytes, write_p32
//   the slot, BlackOutAmbient the leftover. Both slots are written under the
//   caller's ambient ROM.BeginUndoScope (UndoService.Begin/Commit/Rollback),
//   so a failed write leaves no ROM residue. On any failure after a partial
//   mutation the ROM bytes are snapshot-restored before returning.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Image Import for the Generic Enemy Portrait editor.
    /// Writes a RAW (uncompressed) 32x32 4bpp tilesheet to the image slot
    /// (entryAddr + 0) and a RAW 16-color palette to the palette slot
    /// (entryAddr + 0x20). Both slots are repointed under the caller's
    /// ambient undo scope.
    /// </summary>
    public static class GenericEnemyPortraitImportCore
    {
        // 32x32 image = 4x4 tiles = 16 tiles * 32 bytes/tile = 512 bytes RAW 4bpp.
        const int IMAGE_WIDTH = 32;
        const int IMAGE_HEIGHT = 32;
        const int IMAGE_RAW_BYTES = (IMAGE_WIDTH / 8) * (IMAGE_HEIGHT / 8) * 32; // 512
        // 16-color GBA palette = 16 * 2 bytes = 32 bytes RAW.
        const int PALETTE_BYTES = 16 * 2; // 32

        /// <summary>
        /// Import a 32x32 indexed image + 16-color palette into one generic
        /// enemy portrait entry. Writes BOTH the image slot
        /// (<paramref name="imageSlotAddr"/> = entryAddr + 0) and the palette
        /// slot (<paramref name="paletteSlotAddr"/> = entryAddr + 0x20).
        ///
        /// Validation runs BEFORE any mutation:
        ///   * <paramref name="rom"/> must be the active <see cref="CoreState.ROM"/>.
        ///   * <paramref name="indexedPixels"/> length must equal 32*32 (1024).
        ///   * <paramref name="palette"/> length must equal 32 (16 colors).
        ///   * every pixel index must be &lt;= 15 (4bpp).
        ///   * both slots must be safe 4-byte regions.
        ///
        /// Caller MUST wrap in UndoService.Begin/Commit/Rollback (ambient undo
        /// scope). Returns "" on success, a non-empty error string on rejection
        /// (no ROM mutation on a rejection path; snapshot-restore on a failure
        /// after partial mutation).
        /// </summary>
        public static string ImportPortrait(ROM rom, byte[] indexedPixels, byte[] palette,
            uint imageSlotAddr, uint paletteSlotAddr)
        {
            // --- Validate (no mutation before this point) ---
            if (rom == null || rom.Data == null) return "ROM is not loaded.";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "Internal error: ROM is not the active CoreState.ROM.";
            if (indexedPixels == null) return "No image data.";
            if (palette == null) return "No palette data.";
            if (indexedPixels.Length != IMAGE_WIDTH * IMAGE_HEIGHT)
                return $"Image data length {indexedPixels.Length} does not match {IMAGE_WIDTH}x{IMAGE_HEIGHT} ({IMAGE_WIDTH * IMAGE_HEIGHT}).";
            if (palette.Length != PALETTE_BYTES)
                return $"Palette length {palette.Length} does not match {PALETTE_BYTES} bytes (16 colors).";

            // 4bpp: every palette index must fit in a nibble.
            for (int i = 0; i < indexedPixels.Length; i++)
            {
                if ((int)indexedPixels[i] > 15)
                    return $"Pixel index {(int)indexedPixels[i]} at offset {i} exceeds 15 (4bpp limit).";
            }

            // Both pointer slots must be safe 4-byte regions.
            if (!IsRegionSafe(rom, imageSlotAddr, 4))
                return "Image pointer slot is out of range.";
            if (!IsRegionSafe(rom, paletteSlotAddr, 4))
                return "Palette pointer slot is out of range.";

            // --- Encode RAW 4bpp tiles (no TSA dedup, no LZ77) ---
            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, IMAGE_WIDTH, IMAGE_HEIGHT);
            if (tiles == null || tiles.Length != IMAGE_RAW_BYTES)
                return "Failed to encode tile data.";

            // --- Write BOTH slots under the caller's ambient undo scope ---
            // Snapshot the ENTIRE ROM byte array so a failure after the first
            // slot is written leaves the ROM byte-identical (the ambient undo
            // scope already records the writes; this snapshot is a belt-and-
            // braces atomic guard for any partial mutation).
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                // Image slot @ +0 — RAW 4bpp tiles (512 bytes), recycle the OLD
                // raw region (fixed 512-byte length) then repoint.
                if (!WriteRawRegion(rom, tiles, imageSlotAddr, IMAGE_RAW_BYTES, "OLD_GENERIC_ENEMY_PORTRAIT_IMAGE"))
                {
                    RestoreSnapshot(rom, snapshot);
                    return "Could not allocate space for the imported image.";
                }

                // Palette slot @ +0x20 — RAW 16-color palette (32 bytes).
                if (!WriteRawRegion(rom, palette, paletteSlotAddr, PALETTE_BYTES, "OLD_GENERIC_ENEMY_PORTRAIT_PALETTE"))
                {
                    RestoreSnapshot(rom, snapshot);
                    return "Could not allocate space for the imported palette.";
                }
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snapshot);
                return $"Image Import failed: {ex.Message}";
            }

            return string.Empty; // success
        }

        /// <summary>
        /// Recycle the OLD raw region pointed at by <paramref name="pointerSlot"/>
        /// (known fixed <paramref name="oldLength"/>), allocate + write the new
        /// raw <paramref name="data"/>, repoint the slot, and black out the
        /// leftover. All under the active ambient undo scope. Returns false on
        /// an allocation failure (no repoint occurred).
        /// </summary>
        static bool WriteRawRegion(ROM rom, byte[] data, uint pointerSlot, uint oldLength, string info)
        {
            var recycle = new List<Address>();
            uint oldDataAddr = rom.p32(pointerSlot);
            if (U.isSafetyOffset(oldDataAddr, rom)
                && IsRegionSafe(rom, oldDataAddr, (int)oldLength))
            {
                // Resolve the slot's old data region (fixed length) into the
                // recycle pool so the new same-size bytes reuse it.
                Address.AddPointer(recycle, pointerSlot, oldLength, info, Address.DataTypeEnum.BIN);
            }
            var ra = new RecycleAddress(recycle);

            uint newAddr = ra.WriteAmbient(data);
            if (newAddr == U.NOT_FOUND)
                return false;

            rom.write_p32(pointerSlot, newAddr);
            ra.BlackOutAmbient();
            return true;
        }

        static void RestoreSnapshot(ROM rom, byte[] snapshot)
        {
            // Restore the ROM byte array to the pre-mutation state. The ambient
            // undo scope still records the net diff; the caller's
            // UndoService.Rollback unwinds the scope itself.
            if (rom?.Data == null || snapshot == null) return;
            if (rom.Data.Length != snapshot.Length)
            {
                // The ROM was resized during a freespace fallback — shrink back.
                rom.write_resize_data((uint)snapshot.Length);
            }
            Array.Copy(snapshot, rom.Data, Math.Min(snapshot.Length, rom.Data.Length));
        }

        /// <summary>
        /// Bounds-safe region check: <paramref name="addr"/> in [0x200, 0x02000000)
        /// AND the whole <paramref name="bytes"/>-byte span stays inside rom.Data.
        /// ulong arithmetic guards against overflow. Mirrors
        /// TSAImageImportCore.IsRegionSafe / ImageBattleScreenCore.IsRegionSafe.
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }
    }
}
