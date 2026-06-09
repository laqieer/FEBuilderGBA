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

using System;

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
        /// <param name="forceNewAlloc">
        /// When true, the in-place branch is skipped entirely and the helper
        /// ALWAYS takes the reallocate (append at ROM end + repoint P12) path,
        /// even when the new compressed bytes would fit within the original
        /// buffer. This gives the slot its OWN independent palette block in free
        /// space (the "New Palette Allocation" flow, #1067). The default
        /// <c>false</c> preserves the original in-place-or-reallocate behavior
        /// byte-identically — existing callers are unaffected.
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
            bool forceNewAlloc = false,
            Undo.UndoData? undo = null)
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
            // forceNewAlloc skips the in-place branch entirely and always
            // appends + repoints, giving the slot its own independent palette
            // block (the "New Palette Allocation" flow, #1067).
            if (!forceNewAlloc && newCompressedLen <= oldCompressedLen)
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

        /// <summary>
        /// Allocate a FRESH free-space palette block for the row's P12 slot and
        /// repoint the slot to it (the "New Palette Allocation" flow, #1067).
        /// Unlike <see cref="WritePalette"/> with the default in-place branch,
        /// this ALWAYS appends a brand-new LZ77-compressed block at ROM end and
        /// repoints P12 — even when the recompressed bytes would have fit in the
        /// original buffer — so the slot gets its OWN independent palette and no
        /// longer shares the previous block with any other slot.
        ///
        /// Multi-bank correctness: when the P12 slot points at a valid LZ77
        /// stream this reuses <see cref="WritePalette"/>'s splice logic, so it
        /// overwrites ONLY the <paramref name="paletteIndex"/> 32-byte bank
        /// (unless <paramref name="isOverrideAll"/>), recompresses the FULL
        /// decompressed stream, and preserves the untouched banks.
        ///
        /// Invalid / zero / non-LZ77 P12: there is no existing stream to splice
        /// into, so a DETERMINISTIC fresh buffer is built that still HONORS the
        /// requested bank — <c>(paletteIndex + 1)</c> 32-byte banks, zero-filled,
        /// with the supplied colors written into bank
        /// <paramref name="paletteIndex"/> (or EVERY bank when
        /// <paramref name="isOverrideAll"/>), LZ77-compressed, appended at ROM
        /// end, and the P12 slot repointed to it. For <c>paletteIndex == 0</c>
        /// non-override this is a single 32-byte bank.
        /// (<see cref="WritePalette"/> itself early-outs to
        /// <see cref="U.NOT_FOUND"/> for a bad pointer because it must not invent
        /// a stream during an in-place write; the New-Alloc flow always
        /// allocates, so a fresh slot is the well-defined result.)
        ///
        /// The OLD compressed block is left UNTOUCHED (no recycle) — this is the
        /// shared-palette safety guarantee: another slot may still reference it.
        ///
        /// Fault restore: a defensive byte-for-byte snapshot is taken before any
        /// mutation. If the underlying write throws OR returns
        /// <see cref="U.NOT_FOUND"/> (no free space / resize failure), the ROM is
        /// restored byte-identical (length-aware, since a free-space append can
        /// GROW <c>rom.Data</c>) and <see cref="U.NOT_FOUND"/> is returned. This
        /// method NEVER throws.
        ///
        /// Undo handling matches <see cref="WritePalette"/>: writes record into
        /// the caller's ambient <see cref="ROM.BeginUndoScope"/> automatically.
        /// </summary>
        /// <param name="rom">ROM to mutate. Must be non-null.</param>
        /// <param name="rowP12SlotOffset">ROM offset of the row's P12 pointer slot.</param>
        /// <param name="r">16 R channel values in 0-31 range.</param>
        /// <param name="g">16 G channel values in 0-31 range.</param>
        /// <param name="b">16 B channel values in 0-31 range.</param>
        /// <param name="paletteIndex">Zero-based slot index to overwrite (ignored when override-all).</param>
        /// <param name="isOverrideAll">When true, every 32-byte slot is replaced.</param>
        /// <returns>
        /// The freshly-allocated GBA pointer (0x08xxxxxx) the P12 slot now holds,
        /// or <see cref="U.NOT_FOUND"/> on any invalid input or write fault (with
        /// the ROM restored byte-identical).
        /// </returns>
        public static uint AllocNewPalette(
            ROM rom,
            uint rowP12SlotOffset,
            uint[] r,
            uint[] g,
            uint[] b,
            int paletteIndex,
            bool isOverrideAll)
        {
            if (rom == null) return U.NOT_FOUND;

            // Defensive snapshot — restored byte-identical on ANY fault so a
            // failed alloc never leaves the ROM half-mutated (the #885/#923
            // WaitIconImportCore / SkillSystemsAnimeImportCore pattern).
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                uint result;
                if (HasValidLz77Source(rom, rowP12SlotOffset))
                {
                    // Existing multi-bank stream: splice + force-append (preserves
                    // untouched banks).
                    result = WritePalette(
                        rom, rowP12SlotOffset, r, g, b, paletteIndex, isOverrideAll,
                        forceNewAlloc: true, undo: null);
                }
                else
                {
                    // No existing stream to splice into (zero / non-pointer /
                    // non-LZ77 P12): build a deterministic fresh buffer that
                    // honors the requested bank (paletteIndex) + override-all,
                    // append + repoint.
                    result = AppendFreshBanks(rom, rowP12SlotOffset, r, g, b, paletteIndex, isOverrideAll);
                }
                if (result == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snapshot);
                    return U.NOT_FOUND;
                }
                return result;
            }
            catch (Exception)
            {
                RestoreSnapshot(rom, snapshot);
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// True when the P12 slot at <paramref name="rowP12SlotOffset"/> holds a
        /// valid in-bounds pointer to a decodable LZ77 stream of at least one
        /// 32-byte palette bank — i.e. the same preconditions
        /// <see cref="WritePalette"/> requires before it splices. Mirrors that
        /// validation exactly so the splice-vs-fresh decision in
        /// <see cref="AllocNewPalette"/> never feeds <see cref="WritePalette"/> a
        /// pointer it would reject. Guarded against every fault; never throws.
        /// </summary>
        static bool HasValidLz77Source(ROM rom, uint rowP12SlotOffset)
        {
            try
            {
                if (rowP12SlotOffset + 4 > (uint)rom.Data.Length) return false;
                uint rawP12 = rom.u32(rowP12SlotOffset);
                if (!U.isPointer(rawP12)) return false;
                uint srcOffset = U.toOffset(rawP12);
                if (!U.isSafetyOffset(srcOffset, rom)) return false;
                if (LZ77.getCompressedSize(rom.Data, srcOffset) == 0) return false;
                byte[] decompressed = LZ77.decompress(rom.Data, srcOffset);
                return decompressed != null && decompressed.Length >= RAW_PALETTE_BYTES;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Build a DETERMINISTIC fresh palette buffer that HONORS the requested
        /// bank, LZ77-compress it, append at ROM end, and repoint the P12 slot to
        /// it. Used by <see cref="AllocNewPalette"/> when the slot has no existing
        /// LZ77 stream to splice into. Mirrors <see cref="WritePalette"/>'s
        /// resize/splice semantics applied to a fresh zero-filled buffer:
        /// <list type="bullet">
        /// <item>The buffer is <c>(paletteIndex + 1)</c> 32-byte banks long, so
        /// the requested bank index is in range and any lower banks stay
        /// black/zero.</item>
        /// <item>When <paramref name="isOverrideAll"/> is true, the packed colors
        /// fill EVERY 32-byte bank; otherwise only bank
        /// <paramref name="paletteIndex"/> is written and lower banks stay zero.</item>
        /// </list>
        /// So <c>paletteIndex == 0</c> (non-override) is a single 32-byte bank
        /// (backward compatible). Validates the R/G/B inputs and
        /// <paramref name="paletteIndex"/> exactly like <see cref="WritePalette"/>
        /// and returns <see cref="U.NOT_FOUND"/> on bad input or a resize failure
        /// (the caller restores the snapshot). Writes record into the caller's
        /// ambient undo scope.
        /// </summary>
        static uint AppendFreshBanks(
            ROM rom, uint rowP12SlotOffset, uint[] r, uint[] g, uint[] b,
            int paletteIndex, bool isOverrideAll)
        {
            if (r == null || g == null || b == null) return U.NOT_FOUND;
            if (r.Length != PALETTE_COUNT || g.Length != PALETTE_COUNT || b.Length != PALETTE_COUNT)
                return U.NOT_FOUND;
            for (int i = 0; i < PALETTE_COUNT; i++)
            {
                if (r[i] > 0x1F || g[i] > 0x1F || b[i] > 0x1F) return U.NOT_FOUND;
            }
            if (paletteIndex < 0) return U.NOT_FOUND;
            if (rowP12SlotOffset + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;

            byte[] newSlot = PackRgb555(r, g, b);

            // Zero-filled buffer large enough to hold the requested bank index;
            // lower banks remain black until the user edits them.
            int bankCount = paletteIndex + 1;
            byte[] raw = new byte[bankCount * RAW_PALETTE_BYTES];
            if (isOverrideAll)
            {
                for (int s = 0; s < bankCount; s++)
                {
                    System.Buffer.BlockCopy(newSlot, 0, raw, s * RAW_PALETTE_BYTES, RAW_PALETTE_BYTES);
                }
            }
            else
            {
                System.Buffer.BlockCopy(newSlot, 0, raw, paletteIndex * RAW_PALETTE_BYTES, RAW_PALETTE_BYTES);
            }

            byte[] compressed = LZ77.compress(raw);

            uint appendOffset = (uint)rom.Data.Length;
            uint newRomSize = appendOffset + (uint)compressed.Length;
            if (!rom.write_resize_data(newRomSize)) return U.NOT_FOUND;

            rom.write_range(appendOffset, compressed);
            rom.write_p32(rowP12SlotOffset, appendOffset);
            return U.toPointer(appendOffset);
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space append can GROW
        /// <c>rom.Data</c> via <see cref="ROM.write_resize_data"/>, so down-resize
        /// back to the snapshot length BEFORE the in-place copy (a naive
        /// Array.Copy would leave the grown tail alive). Mirrors
        /// <c>WaitIconImportCore.RestoreSnapshot</c> (#885/#923).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snapshot)
        {
            if (rom.Data.Length != snapshot.Length)
                rom.write_resize_data((uint)snapshot.Length);
            Array.Copy(snapshot, rom.Data, snapshot.Length);
        }
    }
}
