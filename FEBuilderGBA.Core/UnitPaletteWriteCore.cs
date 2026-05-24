// SPDX-License-Identifier: GPL-3.0-or-later
// UnitPaletteWriteCore — cross-platform LZ77 unit-palette write-back helper.
//
// Used by the Avalonia ImageUnitPaletteView (#397) to overwrite the
// LZ77-compressed palette referenced by a unit-palette row's P12 pointer
// slot. Algorithm matches the WinForms PaletteFormRef.MakePaletteUIToROM
// in-place-or-reallocate behavior with explicit P12 patching when the new
// compressed bytes don't fit in the original buffer.
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
        /// pointer. If the new compressed bytes fit within the original buffer
        /// the write is in-place (and the trailing bytes are zero-filled).
        /// Otherwise the compressed bytes are appended at ROM end, the ROM is
        /// resized, and the row's P12 slot is patched to the new GBA pointer.
        /// </summary>
        /// <param name="rom">ROM to mutate. Must be non-null.</param>
        /// <param name="rowP12SlotOffset">
        /// ROM offset of the unit-palette row's P12 pointer slot. On reallocation
        /// this slot is patched to point at the appended palette bytes.
        /// </param>
        /// <param name="r">16 R channel values in 0-31 range.</param>
        /// <param name="g">16 G channel values in 0-31 range.</param>
        /// <param name="b">16 B channel values in 0-31 range.</param>
        /// <param name="undo">
        /// Optional undo-data context. When non-null the helper records explicit
        /// undo entries via the (addr, byte[], undodata) overloads so the
        /// resulting undo group can revert the write + the P12 patch + the file
        /// size delta. When null the helper falls back to the no-undo overloads.
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
            Undo.UndoData? undo)
        {
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

            // ----- Read raw P12 pointer (preserve raw for round-trip) -----
            if (rowP12SlotOffset + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;
            uint rawP12 = rom.u32(rowP12SlotOffset);
            if (!U.isPointer(rawP12)) return U.NOT_FOUND;

            uint srcOffset = U.toOffset(rawP12);
            if (!U.isSafetyOffset(srcOffset, rom)) return U.NOT_FOUND;

            // ----- Measure existing compressed length -----
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, srcOffset);
            if (oldCompressedLen == 0) return U.NOT_FOUND;

            // ----- Pack + compress the new palette -----
            byte[] raw = PackRgb555(r, g, b);
            byte[] compressed = LZ77.compress(raw);
            uint newCompressedLen = (uint)compressed.Length;

            // ----- In-place vs reallocate -----
            if (newCompressedLen <= oldCompressedLen)
            {
                // In-place write: replace the compressed bytes, then zero-fill the trailing bytes
                // that the old (longer) stream used.
                if (undo != null)
                {
                    rom.write_range(srcOffset, compressed, undo);
                    uint trailing = oldCompressedLen - newCompressedLen;
                    if (trailing > 0)
                    {
                        rom.write_fill(srcOffset + newCompressedLen, trailing, 0x00, undo);
                    }
                }
                else
                {
                    rom.write_range(srcOffset, compressed);
                    uint trailing = oldCompressedLen - newCompressedLen;
                    if (trailing > 0)
                    {
                        rom.write_fill(srcOffset + newCompressedLen, trailing, 0x00);
                    }
                }
                return rawP12;
            }
            else
            {
                // Reallocation: append at ROM end + patch P12.
                uint appendOffset = (uint)rom.Data.Length;
                uint newRomSize = appendOffset + newCompressedLen;
                // Pad to 4-byte alignment to keep subsequent GBA pointers aligned.
                // (rom.write_resize_data internally pads via U.Padding4 already, but we
                // compute the resize value verbatim so the caller can predict the new size.)
                if (!rom.write_resize_data(newRomSize)) return U.NOT_FOUND;

                if (undo != null)
                {
                    rom.write_range(appendOffset, compressed, undo);
                    uint newPointer = U.toPointer(appendOffset);
                    rom.write_p32(rowP12SlotOffset, appendOffset, undo);
                    return newPointer;
                }
                else
                {
                    rom.write_range(appendOffset, compressed);
                    uint newPointer = U.toPointer(appendOffset);
                    rom.write_p32(rowP12SlotOffset, appendOffset);
                    return newPointer;
                }
            }
        }
    }
}
