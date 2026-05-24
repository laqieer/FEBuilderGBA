using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core helper for battle-animation palette I/O (#399).
    ///
    /// Mirrors the WinForms <c>PaletteFormRef.MakePaletteROMToUI</c> /
    /// <c>MakePaletteUIToROM</c> flow with <c>isCompress: true</c>: the
    /// palette block is LZ77-compressed and may contain 1..N slots of
    /// 0x20 bytes each (16 colors x u16). The selected slot is at
    /// <c>paletteIndex * 0x20</c> inside the decompressed block.
    ///
    /// <para><b>Offsets-throughout contract:</b> every <c>uint</c>
    /// parameter is a ROM offset (e.g. <c>0x123456</c>). Inputs are
    /// normalized via <c>U.toOffset</c> at entry; the return value is a
    /// ROM offset. <c>U.toPointer(...)</c> is applied ONLY when writing
    /// into ROM pointer slots (because pointer-slots store GBA
    /// <c>0x08...</c>-prefixed pointers).</para>
    ///
    /// Writes execute inside the caller-supplied ambient undo scope
    /// (<see cref="ROM.BeginUndoScope"/>) so the
    /// <c>Avalonia.Services.UndoService</c> Begin/Commit/Rollback
    /// envelope captures every byte change.
    /// </summary>
    public static class ImageBattleAnimePaletteCore
    {
        /// <summary>Number of colors in one palette slot.</summary>
        public const int ColorsPerSlot = 16;

        /// <summary>Byte size of one palette slot (16 u16 colors).</summary>
        public const int SlotByteSize = ColorsPerSlot * 2; // 0x20

        /// <summary>
        /// Read 16 GBA u16 colors from the slot at
        /// <paramref name="paletteIndex"/> inside the LZ77-compressed
        /// palette block at <paramref name="paletteOffset"/>. Returns
        /// <c>null</c> when decompression fails or the slot is out of
        /// range.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="paletteOffset">ROM offset of the LZ77 palette block.</param>
        /// <param name="paletteIndex">Slot index inside the decompressed block (0..N-1).</param>
        /// <returns>16-entry GBA u16 color array, or <c>null</c> on failure.</returns>
        public static ushort[] ReadPalette(ROM rom, uint paletteOffset, int paletteIndex)
        {
            if (rom == null || rom.Data == null)
            {
                return null;
            }

            paletteOffset = U.toOffset(paletteOffset);
            if (paletteOffset == 0 || paletteOffset >= (uint)rom.Data.Length)
            {
                return null;
            }
            if (paletteIndex < 0)
            {
                return null;
            }

            byte[] decompressed = LZ77.decompress(rom.Data, paletteOffset);
            if (decompressed == null || decompressed.Length == 0)
            {
                return null;
            }

            uint readStart = (uint)(paletteIndex * SlotByteSize);
            if (readStart + SlotByteSize > (uint)decompressed.Length)
            {
                return null;
            }

            ushort[] result = new ushort[ColorsPerSlot];
            for (int i = 0; i < ColorsPerSlot; i++)
            {
                result[i] = (ushort)U.u16(decompressed, readStart + (uint)(i * 2));
            }
            return result;
        }

        /// <summary>
        /// Number of 0x20-byte palette slots inside the LZ77-compressed
        /// block at <paramref name="paletteOffset"/>. Returns 0 when
        /// decompression fails or the block is corrupt.
        ///
        /// <para>Used internally by <see cref="WritePalette"/> for
        /// compressed-block sizing / diagnostics. <b>Not</b> a 32-color-mode
        /// signal — slot count over-reports because a block may legitimately
        /// have 4 slots without the animation actually using 32-color mode
        /// (see issue #399 plan-review Finding #2).</para>
        /// </summary>
        public static int GetPaletteSlotCount(ROM rom, uint paletteOffset)
        {
            if (rom == null || rom.Data == null)
            {
                return 0;
            }
            paletteOffset = U.toOffset(paletteOffset);
            if (paletteOffset == 0 || paletteOffset >= (uint)rom.Data.Length)
            {
                return 0;
            }

            byte[] decompressed = LZ77.decompress(rom.Data, paletteOffset);
            if (decompressed == null || decompressed.Length == 0)
            {
                return 0;
            }
            return decompressed.Length / SlotByteSize;
        }

        /// <summary>
        /// Write 16 GBA u16 <paramref name="colors"/> into the slot at
        /// <paramref name="paletteIndex"/> of the LZ77 palette block at
        /// <paramref name="paletteOffset"/>. Mirrors WF
        /// <c>PaletteFormRef.MakePaletteUIToROM(addr, isCompress: true,
        /// paletteIndex)</c> +
        /// <c>InputFormRef.WriteBinaryData(...callback_lz77...)</c>.
        ///
        /// <para>Algorithm:
        ///   1. Decompress block, splice slot, recompress.
        ///   2. If recompressed bytes fit in
        ///      <see cref="LZ77.getCompressedSize"/>(block), write in-place
        ///      + zero-fill trailing bytes. Return input offset.
        ///   3. Else allocate free space via
        ///      <paramref name="writerOverride"/> (defaults to
        ///      <see cref="ImageImportCore.FindAndWriteData"/>).
        ///   4. Rewrite every LDR/raw pointer referencing the old block
        ///      via <see cref="LZ77ToolCore.SearchPointersForAddress"/>,
        ///      unioned with <paramref name="sourcePointerSlot"/> when
        ///      non-null (guards against LDR-priority masking the table
        ///      slot — issue #399 plan-review v5 Finding #3).
        ///   5. Zero-fill old block. Return new offset.</para>
        ///
        /// All writes execute under the caller-supplied ambient undo scope.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="paletteOffset">ROM offset of the LZ77 palette block.</param>
        /// <param name="paletteIndex">Slot index inside the decompressed block (0..N-1).</param>
        /// <param name="colors">16-entry GBA u16 color array.</param>
        /// <param name="sourcePointerSlot">Optional ROM offset of the
        ///   back-pointer slot (e.g. <c>animeEntryAddr + 28</c>). When
        ///   non-null and not in the LDR search result, it is unioned in
        ///   so the source pointer is always rewritten on relocate.</param>
        /// <param name="writerOverride">Test injection point. Production
        ///   passes <c>null</c> to use
        ///   <see cref="ImageImportCore.FindAndWriteData"/>.</param>
        /// <returns>ROM offset of the palette block after write
        ///   (<c>==paletteOffset</c> for in-place, different for relocate)
        ///   or <see cref="U.NOT_FOUND"/> on failure.</returns>
        public static uint WritePalette(
            ROM rom,
            uint paletteOffset,
            int paletteIndex,
            ushort[] colors,
            uint? sourcePointerSlot = null,
            Func<ROM, byte[], uint> writerOverride = null)
        {
            if (rom == null || rom.Data == null || colors == null)
            {
                return U.NOT_FOUND;
            }
            if (colors.Length != ColorsPerSlot)
            {
                return U.NOT_FOUND;
            }
            if (paletteIndex < 0)
            {
                return U.NOT_FOUND;
            }

            paletteOffset = U.toOffset(paletteOffset);
            if (paletteOffset == 0 || paletteOffset >= (uint)rom.Data.Length)
            {
                return U.NOT_FOUND;
            }

            // -- Decompress current block.
            byte[] decompressed = LZ77.decompress(rom.Data, paletteOffset);
            if (decompressed == null || decompressed.Length == 0)
            {
                return U.NOT_FOUND;
            }

            // -- Splice the new slot at paletteIndex * 0x20. Resize block
            // when the index is beyond the current slot count (matches WF
            // PaletteFormRef.cs:705-711 "Array.Resize" behavior).
            uint writeStart = (uint)(paletteIndex * SlotByteSize);
            if (writeStart + SlotByteSize > (uint)decompressed.Length)
            {
                Array.Resize(ref decompressed, (int)(writeStart + SlotByteSize));
            }
            for (int i = 0; i < ColorsPerSlot; i++)
            {
                U.write_u16(decompressed, writeStart + (uint)(i * 2), colors[i]);
            }

            // -- Recompress.
            byte[] recompressed;
            try
            {
                recompressed = LZ77.compress(decompressed);
            }
            catch
            {
                return U.NOT_FOUND;
            }
            if (recompressed == null || recompressed.Length == 0)
            {
                return U.NOT_FOUND;
            }

            // -- In-place capacity check.
            uint originalCompressedLen = LZ77.getCompressedSize(rom.Data, paletteOffset);
            if (originalCompressedLen == 0)
            {
                // Shouldn't happen — decompress just succeeded — but be defensive.
                return U.NOT_FOUND;
            }

            if ((uint)recompressed.Length <= originalCompressedLen)
            {
                // In-place: overwrite + zero-fill the trailing bytes.
                rom.write_range(paletteOffset, recompressed);
                uint trailing = originalCompressedLen - (uint)recompressed.Length;
                if (trailing > 0)
                {
                    rom.write_fill(paletteOffset + (uint)recompressed.Length, trailing, 0);
                }
                return paletteOffset;
            }

            // -- Relocate branch.
            uint newOffset;
            if (writerOverride != null)
            {
                newOffset = writerOverride(rom, recompressed);
            }
            else
            {
                newOffset = ImageImportCore.FindAndWriteData(rom, recompressed);
            }
            if (newOffset == U.NOT_FOUND)
            {
                return U.NOT_FOUND;
            }

            // -- Pointer rewrite. Collect LDR + raw fallback hits, union
            // with the source slot if provided.
            LZ77ToolCore.SearchPointerResult searchResult =
                LZ77ToolCore.SearchPointersForAddress(rom.Data, paletteOffset);
            var rewriteSet = new HashSet<uint>();
            if (searchResult != null && searchResult.Pointers != null)
            {
                foreach (uint p in searchResult.Pointers)
                {
                    rewriteSet.Add(p);
                }
            }
            if (sourcePointerSlot.HasValue)
            {
                rewriteSet.Add(sourcePointerSlot.Value);
            }
            uint newPointer = U.toPointer(newOffset);
            foreach (uint slot in rewriteSet)
            {
                if (slot + 4 > (uint)rom.Data.Length)
                {
                    continue;
                }
                rom.write_u32(slot, newPointer);
            }

            // -- Zero-fill old block.
            rom.write_fill(paletteOffset, originalCompressedLen, 0);

            return newOffset;
        }
    }
}
