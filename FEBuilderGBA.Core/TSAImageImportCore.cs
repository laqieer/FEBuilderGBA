// SPDX-License-Identifier: GPL-3.0-or-later
// TSA editor "Main Image" import (tilesheet-only) — Avalonia parity for the
// WinForms ImageTSAEditorForm image1_Import button (#901).
//
// SCOPE (rescoped by Copilot plan review — a data-loss bug was averted):
//   The WF TSA editor constructs its main-image ImageFormRef with
//   tsa_pointer = 0 and only the "image1_ZIMAGE" named control wired, so the
//   ImageFormRef.Import path falls to the final `else` branch:
//
//       image = ImageUtil.ImageToByte16Tile(bitmap, Width, Height);   // ImageFormRef.cs:1214
//       writeAddr = WriteImageData(ZIMAGE, IMAGEPointer, ..., useLZ77:true); // :1244
//
//   TSA (:1270-1285) and palette (:1299-1313) are ALL-NULL — never written.
//   Therefore the correct Avalonia behavior is: load a SAME-SIZE PNG, encode
//   it to plain 4bpp tiles (no TSA dedup), LZ77-compress, write to free space
//   and repoint ONLY the ZImg pointer. TSA + palette pointers are left
//   byte-for-byte untouched.
//
// ENCODE: reuses ImageImportCore.EncodeDirectTiles4bpp — already confirmed
//   byte-identical to WF ImageUtil.ImageToByte16Tile in #898 (same tile-row-
//   major y(8)->x(8)->y8->x8(2) layout packing lo | (hi<<4)). No new encoder.
//
// WRITE: mirrors the #891 (MagicEffectCSAImportCore) single-region primitive —
//   recycle the OLD LZ77 tilesheet region, RecycleAddress.WriteAmbient the new
//   compressed tiles, write_p32 the ZImg pointer, BlackOutAmbient the leftover.
//   All under the caller's ambient ROM.BeginUndoScope (UndoService.Begin/
//   Commit/Rollback), so a failed write leaves no ROM residue.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform "Main Image" (tilesheet) import for the TSA editor.
    /// Tilesheet-only: TSA and palette regions are never touched.
    /// </summary>
    public static class TSAImageImportCore
    {
        // The GBA LZ77 stream header is 4 bytes (0x10 + 3-byte uncompressed
        // size). A pointer to the last 1-3 bytes of the ROM passes
        // isSafetyOffset yet makes the header read throw — require the FULL
        // header in-bounds before any LZ77 call (mirrors ImageBattleScreenCore).
        const int LZ77_HEADER_BYTES = 4;
        const int LINER_ALIGN = 8;

        /// <summary>
        /// Import a same-size PNG/tilesheet into the TSA editor's main image.
        /// Writes ONLY the ZImg pointer's tilesheet region; TSA and palette
        /// pointers are left untouched (WF image1_Import parity).
        ///
        /// Validation runs BEFORE any mutation:
        ///   * <paramref name="rom"/> must be the active <see cref="CoreState.ROM"/>.
        ///   * <paramref name="zimgPointerAddr"/> must be a safe 4-byte slot and
        ///     resolve to a safe LZ77 tilesheet whose decoded dimensions equal
        ///     (<paramref name="width"/> x <paramref name="height"/>) — SAME-SIZE only.
        ///   * <paramref name="indexedPixels"/> length must equal width*height.
        ///   * width/height must be positive multiples of 8.
        ///   * every pixel index must be &lt;= 15 (4bpp).
        ///
        /// Caller MUST wrap in UndoService.Begin/Commit/Rollback (ambient undo
        /// scope). Returns "" on success, a non-empty error string on rejection
        /// (no ROM mutation on any rejection path).
        /// </summary>
        public static string ImportTSAImage(ROM rom, byte[] indexedPixels,
            int width, int height, uint zimgPointerAddr)
        {
            // --- Validate (no mutation before this point) ---
            if (rom == null || rom.Data == null) return "ROM is not loaded.";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "Internal error: ROM is not the active CoreState.ROM.";
            if (indexedPixels == null) return "No image data.";
            if (width <= 0 || height <= 0) return "Image dimensions must be positive.";
            if (width % 8 != 0 || height % 8 != 0)
                return $"Image dimensions must be multiples of 8 (got {width}x{height}).";
            if ((long)indexedPixels.Length != (long)width * height)
                return $"Image data length {indexedPixels.Length} does not match {width}x{height}.";

            // 4bpp: every palette index must fit in a nibble.
            for (int i = 0; i < indexedPixels.Length; i++)
            {
                if ((int)indexedPixels[i] > 15)
                    return $"Pixel index {(int)indexedPixels[i]} at offset {i} exceeds 15 (4bpp limit).";
            }

            // The ZImg pointer slot must be a safe 4-byte region.
            if (!IsRegionSafe(rom, zimgPointerAddr, 4))
                return "Image pointer slot is out of range.";

            // SAME-SIZE enforcement: derive the existing tilesheet dimensions
            // from the ZImg pointer (mirrors WF U.CalcLZ77ImageToSizePointer)
            // and reject any mismatch BEFORE encoding or writing anything.
            if (!TryCalcTilesheetSize(rom, zimgPointerAddr, out int curW, out int curH))
                return "Could not determine the existing tilesheet size.";
            if (curW != width || curH != height)
                return $"Import must be the same size as the existing tilesheet " +
                       $"({curW}x{curH}); got {width}x{height}.";

            // --- Encode (no TSA dedup; byte-identical to WF ImageToByte16Tile) ---
            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tiles == null || tiles.Length == 0)
                return "Failed to encode tile data.";

            // --- Write SINGLE region under the caller's ambient undo scope ---
            // Recycle the OLD LZ77 tilesheet region first so the new (possibly
            // smaller) compressed stream reuses it; leftover bytes are blacked
            // out. Mirrors the #891 RecycleAddress.WriteAmbient + write_p32 +
            // BlackOutAmbient single-region primitive.
            var recycle = new List<Address>();
            uint oldDataAddr = rom.p32(zimgPointerAddr);
            if (U.isSafetyOffset(oldDataAddr, rom) && IsLZ77HeaderSafe(rom, oldDataAddr))
            {
                Address.AddLZ77Pointer(recycle, zimgPointerAddr, "OLD_TSA_MAIN_IMAGE",
                    false, Address.DataTypeEnum.LZ77IMG);
            }
            var ra = new RecycleAddress(recycle);

            byte[] compressed = LZ77.compress(tiles);
            if (compressed == null || compressed.Length == 0)
                return "Failed to compress tile data.";

            uint newAddr = ra.WriteAmbient(compressed);
            if (newAddr == U.NOT_FOUND)
                return "Could not allocate space for the imported tilesheet.";

            rom.write_p32(zimgPointerAddr, newAddr);
            ra.BlackOutAmbient();

            return string.Empty; // success
        }

        /// <summary>
        /// Resolve the ZImg pointer slot to its data address and decode the
        /// natural tilesheet (width, height). Port of WinForms
        /// <c>U.CalcLZ77ImageToSizePointer</c> + <c>CalcLZ77ImageToSize</c>:
        /// floor the liner width to <c>(uncompSize/2/2/align)*align</c>, then
        /// pick the first "nice divisor" width in <c>w = 32..1</c>. Returns
        /// false (no throw) when the pointer/data is unsafe or unmeasurable.
        /// </summary>
        public static bool TryCalcTilesheetSize(ROM rom, uint zimgPointerAddr,
            out int width, out int height)
        {
            width = 0;
            height = 0;
            if (rom == null || rom.Data == null) return false;
            if (!IsRegionSafe(rom, zimgPointerAddr, 4)) return false;

            uint dataAddr = rom.p32(zimgPointerAddr);
            if (!U.isSafetyOffset(dataAddr, rom)) return false;
            if (!IsLZ77HeaderSafe(rom, dataAddr)) return false;

            uint size = LZ77.getUncompressSize(rom.Data, dataAddr);
            if (size <= 0) return false;

            // Liner width (height-1 guess), floored to a multiple of align.
            int linerWidth = (int)size / 2 / 2 / LINER_ALIGN;
            if (linerWidth <= 0) return false;
            linerWidth *= LINER_ALIGN;

            for (int w = 32; w >= 1; w--)
            {
                if (linerWidth % (w * 8) == 0)
                {
                    width = w * 8;
                    height = linerWidth / (w * 8) * 8;
                    return true;
                }
            }
            // Unreachable in practice (w=1 divides any multiple-of-8 width).
            return false;
        }

        /// <summary>
        /// Bounds-safe region check: <paramref name="addr"/> in [0x200, 0x02000000)
        /// AND the whole <paramref name="bytes"/>-byte span stays inside rom.Data.
        /// ulong arithmetic guards against overflow. Mirrors
        /// ImageBattleScreenCore.IsRegionSafe.
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }

        static bool IsLZ77HeaderSafe(ROM rom, uint addr) => IsRegionSafe(rom, addr, LZ77_HEADER_BYTES);
    }
}
