// SPDX-License-Identifier: GPL-3.0-or-later
// UnitPaletteWriteCore — cross-platform LZ77 unit-palette write-back helper.
//
// Used by the Avalonia ImageUnitPaletteView (#397) to overwrite the
// LZ77-compressed palette referenced by a unit-palette row's P12 pointer
// slot. Algorithm matches the WinForms PaletteFormRef.MakePaletteUIToROM
// in-place-or-reallocate behavior with explicit P12 patching when the new
// compressed bytes don't fit in the original buffer.
//
// Multi-slot semantics (WF parity, mirrors PaletteFormRef.MakePaletteUIToROM):
//   - The decompressed P12 stream is a concatenation of one or more 32-byte
//     palettes (16 colors x RGB555 each). Slot index 0 = Ally, 1 = Enemy,
//     2 = NPC, 3 = Gray, 4 = Independent.
//   - When paletteIndex is in [0, slotCount), only that single 32-byte slot
//     is overwritten with the new colors; the other slots are preserved.
//   - When isOverrideAll is true, every 32-byte slot in the decompressed
//     buffer is overwritten with the new colors (matches WF's
//     `PaletteFormRef.OVERRAIDE_ALL_PALETTE`).
//
// Cross-platform: depends only on Core LZ77 + ROM + U helpers. No WinForms,
// no Avalonia, no System.Drawing dependencies.

namespace FEBuilderGBA
{
    public static class UnitPaletteWriteCore
    {
        const int PALETTE_COUNT = 16;
        const int RAW_PALETTE_BYTES = PALETTE_COUNT * 2; // 16 colors x RGB555 (2 bytes each) = 32

        /// <summary>
        /// Pack 16 RGB555 channel arrays into the GBA palette byte order
        /// (little-endian RGB555 per color, 32 bytes total).
        /// </summary>
        /// <param name="r">16 R channel values in 0-31 range.</param>
        /// <param name="g">16 G channel values in 0-31 range.</param>
        /// <param name="b">16 B channel values in 0-31 range.</param>
        /// <returns>32-byte buffer suitable for LZ77 compression.</returns>
        public static byte[] PackRgb555(uint[] r, uint[] g, uint[] b)
        {
            var raw = new byte[RAW_PALETTE_BYTES];
            for (int i = 0; i < PALETTE_COUNT; i++)
            {
                ushort c = (ushort)(((b[i] & 0x1F) << 10) | ((g[i] & 0x1F) << 5) | (r[i] & 0x1F));
                raw[i * 2] = (byte)c;
                raw[i * 2 + 1] = (byte)(c >> 8);
            }
            return raw;
        }

        /// <summary>
        /// Overwrite the LZ77-compressed palette referenced by the row's P12
        /// pointer, preserving any other 32-byte palette slots stored in the
        /// same LZ77 stream. If <paramref name="isOverrideAll"/> is true,
        /// every slot in the decompressed buffer is replaced with the new
        /// colors. Otherwise only the slot at <paramref name="paletteIndex"/>
        /// (0 = Ally, 1 = Enemy, 2 = NPC, 3 = Gray, 4 = Independent) is
        /// overwritten and the other slots survive untouched.
        ///
        /// If the new compressed bytes fit within the original buffer the
        /// write is in-place (and the trailing bytes of the original stream
        /// are zero-filled). Otherwise the compressed bytes are appended at
        /// ROM end, the ROM is resized via <see cref="ROM.write_resize_data"/>
        /// (which pads internally to a 4-byte boundary), and the row's P12
        /// slot is patched to the new GBA pointer.
        ///
        /// Undo handling: the helper relies on the caller having opened an
        /// ambient undo scope via <see cref="ROM.BeginUndoScope"/> (which the
        /// Avalonia <c>UndoService</c> does inside <c>Begin/Commit</c>). All
        /// <c>rom.write_*</c> calls record into that ambient slot
        /// automatically. The <paramref name="undo"/> parameter is accepted
        /// for API parity but UNUSED — passing the ambient `UndoData` through
        /// the explicit (addr, value, undo) overloads would double-record
        /// every write (Copilot bot review #585 caught this).
        /// </summary>
        /// <param name="rom">ROM to mutate. Must be non-null.</param>
        /// <param name="rowP12SlotOffset">
        /// ROM offset of the unit-palette row's P12 pointer slot. On reallocation
        /// this slot is patched to point at the appended palette bytes.
        /// </param>
        /// <param name="r">16 R channel values in 0-31 range.</param>
        /// <param name="g">16 G channel values in 0-31 range.</param>
        /// <param name="b">16 B channel values in 0-31 range.</param>
        /// <param name="paletteIndex">
        /// Zero-based slot index to overwrite (0 = Ally, 1 = Enemy, 2 = NPC,
        /// 3 = Gray, 4 = Independent). Ignored when <paramref name="isOverrideAll"/>
        /// is true. If the existing stream only has one slot, an out-of-range
        /// index expands the buffer.
        /// </param>
        /// <param name="isOverrideAll">
        /// When true, every 32-byte slot in the decompressed stream is replaced
        /// with the new colors (matches WF `PaletteFormRef.OVERRAIDE_ALL_PALETTE`).
        /// </param>
        /// <param name="undo">
        /// Accepted for API parity but UNUSED. The helper relies on the
        /// caller having opened an ambient undo scope via
        /// <see cref="ROM.BeginUndoScope"/>. Pass null when there is no
        /// ambient scope; this is logically equivalent because
        /// <c>rom.write_*</c> only records into the ambient slot when it is
        /// non-null. Avoiding the explicit undo overloads prevents
        /// double-recording entries.
        /// </param>
        /// <returns>
        /// The GBA pointer (0x08xxxxxx) the P12 slot now holds, or
        /// <see cref="U.NOT_FOUND"/> if any input was invalid or the source
        /// pointer was missing / pointed at a non-LZ77 stream.
        /// </returns>
        public static uint WritePalette(
            ROM rom,
            uint rowP12SlotOffset,
            uint[] r,
            uint[] g,
            uint[] b,
            int paletteIndex,
            bool isOverrideAll,
            Undo.UndoData? undo)
        {
            // `undo` is intentionally ignored — see XML doc.
            _ = undo;

            // ----- Input validation -----
            if (rom == null) return U.NOT_FOUND;
            if (r == null || g == null || b == null) return U.NOT_FOUND;
            if (r.Length != PALETTE_COUNT || g.Length != PALETTE_COUNT || b.Length != PALETTE_COUNT)
                return U.NOT_FOUND;
            for (int i = 0; i < PALETTE_COUNT; i++)
            {
                if (r[i] > 0x1F || g[i] > 0x1F || b[i] > 0x1F)
                    return U.NOT_FOUND;
            }
            if (paletteIndex < 0) return U.NOT_FOUND;

            // ----- Read raw P12 pointer (preserve raw for round-trip) -----
            if (rowP12SlotOffset + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;
            uint rawP12 = rom.u32(rowP12SlotOffset);
            if (!U.isPointer(rawP12)) return U.NOT_FOUND;

            uint srcOffset = U.toOffset(rawP12);
            if (!U.isSafetyOffset(srcOffset, rom)) return U.NOT_FOUND;

            // ----- Measure existing compressed length + decompress -----
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, srcOffset);
            if (oldCompressedLen == 0) return U.NOT_FOUND;
            byte[] decompressed = LZ77.decompress(rom.Data, srcOffset);
            if (decompressed == null || decompressed.Length < RAW_PALETTE_BYTES)
                return U.NOT_FOUND;

            // ----- Pack new colors + splice into decompressed buffer -----
            byte[] newSlot = PackRgb555(r, g, b);

            if (isOverrideAll)
            {
                // Walk every 32-byte slot and replace with the new colors.
                int slotCount = decompressed.Length / RAW_PALETTE_BYTES;
                if (slotCount < 1) return U.NOT_FOUND;
                for (int s = 0; s < slotCount; s++)
                {
                    System.Buffer.BlockCopy(newSlot, 0, decompressed, s * RAW_PALETTE_BYTES, RAW_PALETTE_BYTES);
                }
            }
            else
            {
                // Replace only the selected slot. If the index is past the end
                // of the current buffer, grow it to fit (matches WF semantics).
                int writeStart = paletteIndex * RAW_PALETTE_BYTES;
                if (decompressed.Length < writeStart + RAW_PALETTE_BYTES)
                {
                    System.Array.Resize(ref decompressed, writeStart + RAW_PALETTE_BYTES);
                }
                System.Buffer.BlockCopy(newSlot, 0, decompressed, writeStart, RAW_PALETTE_BYTES);
            }

            // ----- Compress the spliced buffer -----
            byte[] compressed = LZ77.compress(decompressed);
            uint newCompressedLen = (uint)compressed.Length;

            // ----- In-place vs reallocate (writes go through the ambient undo scope) -----
            if (newCompressedLen <= oldCompressedLen)
            {
                rom.write_range(srcOffset, compressed);
                uint trailing = oldCompressedLen - newCompressedLen;
                if (trailing > 0)
                {
                    rom.write_fill(srcOffset + newCompressedLen, trailing, 0x00);
                }
                return rawP12;
            }
            else
            {
                // Reallocation: append at ROM end + patch P12. Note that
                // rom.write_resize_data pads internally via U.Padding4, so
                // the final rom.Data.Length may be slightly larger than the
                // value we request — that's the caller's expected behaviour.
                uint appendOffset = (uint)rom.Data.Length;
                uint newRomSize = appendOffset + newCompressedLen;
                if (!rom.write_resize_data(newRomSize)) return U.NOT_FOUND;

                rom.write_range(appendOffset, compressed);
                uint newPointer = U.toPointer(appendOffset);
                rom.write_p32(rowP12SlotOffset, appendOffset);
                return newPointer;
            }
        }
    }
}
