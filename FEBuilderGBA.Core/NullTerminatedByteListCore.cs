using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper for editing null-terminated arrays of 1-byte IDs
    /// referenced through a 32-bit ROM pointer (issue #926, #769 bucket 2 slice 1).
    /// It backs the SkillConfig FE8N Ver2/Ver3 per-skill sub-lists (re-pointed via
    /// <c>P4/P8/P12/P16/P20</c>) but is intentionally id-neutral so any
    /// null-terminated 1-byte-ID list (unit / class / item / skill IDs) can reuse it.
    ///
    /// This is the id-neutral generalization of <see cref="ItemClassListCore"/>: it
    /// mirrors that class's proven null-terminated-u8-array mechanics
    /// (<c>0x00</c> terminator, non-zero new-slot placeholder, realloc + repoint,
    /// shared-array preservation) without any item-effectiveness assumptions. The
    /// item-effectiveness editor keeps using <see cref="ItemClassListCore"/>
    /// untouched.
    ///
    /// Mechanics:
    ///   * Scan a null-terminated byte array of IDs (<see cref="ScanByteList"/>).
    ///   * Write a single ID byte through Undo (<see cref="WriteByte"/>).
    ///   * Append one slot before the terminator (<see cref="ExpandByteList"/>).
    ///   * Replace the whole list with an exact, caller-owned set of IDs
    ///     (<see cref="WriteByteList"/> — handles add/delete/reorder/set).
    ///   * Fork a shared array into an independent copy
    ///     (<see cref="MakeIndependentCopy"/>).
    ///
    /// <para><b>Error contract:</b> the realloc methods
    /// (<see cref="ExpandByteList"/>, <see cref="WriteByteList"/>,
    /// <see cref="MakeIndependentCopy"/>) <b>throw</b>
    /// <see cref="InvalidOperationException"/> on allocation failure — there is no
    /// <c>U.NOT_FOUND</c> sentinel (mirrors
    /// <see cref="ItemClassListCore.ExpandClassList"/>). Slice-2 callers wrap each
    /// op in <c>try/catch</c> + <c>UndoService.Rollback</c> + <c>ShowError</c>. All
    /// mutations are undo-aware <c>rom.write_*</c>/<c>write_p32</c> recorded on the
    /// passed <see cref="Undo.UndoData"/>, so a single rollback fully reverts both
    /// the bytes and the repoint (no <c>write_resize_data</c> side effects).</para>
    ///
    /// <para><b>Shared-array preservation:</b> the realloc methods relocate the
    /// list to fresh free space and leave the ORIGINAL bytes intact, so any other
    /// owner still pointing at the old base keeps working — an
    /// <c>ExpandByteList</c>/<c>WriteByteList</c> on a shared array implicitly forks
    /// the caller's owner pointer.</para>
    /// </summary>
    public static class NullTerminatedByteListCore
    {
        /// <summary>
        /// Placeholder ID written into the new slot by <see cref="ExpandByteList"/>.
        /// Must be non-zero so the slot survives the null-terminator scan in
        /// <see cref="ScanByteList"/> (a <c>0x00</c> there would be read as the
        /// terminator and the new slot would be invisible to the UI). The value is
        /// arbitrary but valid — the caller edits it immediately after the grow.
        /// Mirrors <see cref="ItemClassListCore.NewSlotPlaceholder"/>.
        /// </summary>
        public const uint NewSlotPlaceholder = 0x01;

        /// <summary>
        /// Scan a null-terminated array of 1-byte IDs starting at
        /// <paramref name="baseAddr"/> and return the IDs encountered up to (but not
        /// including) the first <c>0x00</c> terminator. Stops at the end of
        /// <c>rom.Data</c>. Returns an empty list if <paramref name="baseAddr"/> is
        /// zero (treated as a null pointer) or beyond the ROM. Iteration is capped at
        /// <c>0x200</c> entries (a 1-byte ID list longer than that is bogus).
        /// </summary>
        public static List<uint> ScanByteList(ROM rom, uint baseAddr)
        {
            var result = new List<uint>();
            if (rom == null || rom.Data == null) return result;
            // Pointer semantics: address 0 represents a null pointer and yields an
            // empty list (ROM data tables never legitimately point at offset 0).
            // Combined with the bounds check below this guards against scanning
            // unrelated data when the owner pointer is null.
            if (baseAddr == 0) return result;
            if (baseAddr >= (uint)rom.Data.Length) return result;

            // Hard cap: 1-byte IDs cannot exceed 0xFF so any list of > 0x200 is
            // bogus. Every read is bounds-checked individually.
            for (uint i = 0; i < 0x200; i++)
            {
                uint a = baseAddr + i;
                if (a >= (uint)rom.Data.Length) break;
                uint c = rom.u8(a);
                if (c == 0) break;
                result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Write a single ID byte to <paramref name="addr"/> and record the undo.
        /// Use during single-slot in-place edits. Mirrors
        /// <see cref="ItemClassListCore.WriteClassByte"/>.
        /// </summary>
        public static void WriteByte(ROM rom, uint addr, uint value, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (addr >= (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(addr), "Address is out of ROM bounds.");
            rom.write_u8(addr, value & 0xFFu, undo);
        }

        /// <summary>
        /// Expand the null-terminated ID array referenced by
        /// <paramref name="pointerAddr"/> by one slot. Copies the existing array to
        /// free space, writes a new <see cref="NewSlotPlaceholder"/> (<c>0x01</c>)
        /// slot just before the terminator (so the user can edit it in place), and
        /// updates the owning pointer to reference the new array. All writes are
        /// recorded on <paramref name="undo"/>. Mirrors
        /// <see cref="ItemClassListCore.ExpandClassList"/>.
        /// </summary>
        /// <returns>The new array base offset (a ROM offset, not a GBA pointer).</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the owner pointer is NON-zero but is not a ROM address, or when
        /// no free space large enough is available (alloc failure). A NULL (<c>0</c>)
        /// owner pointer is NOT an error — see remarks. On any failure no relocation is
        /// committed beyond the writes already recorded on <paramref name="undo"/>,
        /// which a single rollback reverts.
        /// </exception>
        /// <remarks>
        /// A NULL (<c>0</c>) owner pointer is treated as an UNSET/empty list rather than
        /// an error: the method allocates a fresh single-entry list
        /// (<c>[0x01, 0x00]</c>) and repoints the owner to it. This lets the UI add the
        /// FIRST entry to a skill whose FE8N Ver2/Ver3 sub-list pointer
        /// (<c>P4/P8/P12/P16/P20</c>) is still <c>0</c>, and matches
        /// <see cref="WriteByteList"/>, which also accepts a null owner.
        /// <para>
        /// The array layout grows from <c>[a, b, 0]</c> to <c>[a, b, 1, 0]</c>: the
        /// appended slot is initialized to the non-zero placeholder (<c>0x01</c>) so
        /// it remains visible in <see cref="ScanByteList"/> after a reload (a
        /// <c>0</c> there would be the terminator and the new slot would be invisible
        /// to the UI). Callers can immediately edit the new slot at
        /// <c>newBase + oldCount</c>.
        /// </para>
        /// <para>
        /// The original bytes at <c>oldBase</c> are intentionally LEFT INTACT so any
        /// other owners still pointing at <c>oldBase</c> keep working; this means
        /// <c>ExpandByteList</c> on a shared array implicitly forks the caller's
        /// owner pointer (other owners retain the original list). The orphaned bytes
        /// are negligible and a ROM rebuild reclaims them.
        /// </para>
        /// </remarks>
        public static uint ExpandByteList(ROM rom, uint pointerAddr, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(pointerAddr));

            uint oldPtr = rom.u32(pointerAddr);

            // Resolve the owner pointer into an existing list + a FindFreeSpace start
            // hint. A NULL (0) owner pointer is a legitimate UNSET/empty state — FE8N
            // Ver2/Ver3 per-skill sub-list pointers (P4/P8/P12/P16/P20) are 0 until the
            // skill grows its first entry — so treat it as an empty list and allocate a
            // fresh one (mirrors the 0x100 start hint WriteByteList already uses for a
            // null owner). A NON-zero pointer that is not a ROM address is genuine
            // garbage and still throws.
            List<uint> oldList;
            uint startHint;
            if (oldPtr == 0)
            {
                oldList = new List<uint>();
                startHint = 0x100;
            }
            else
            {
                if (!U.isPointer(oldPtr))
                    throw new InvalidOperationException("Owner pointer does not reference a ROM address.");

                uint oldBase = U.toOffset(oldPtr);
                if (oldBase >= (uint)rom.Data.Length)
                    throw new InvalidOperationException("Owner pointer references an out-of-range address.");

                oldList = ScanByteList(rom, oldBase);
                startHint = oldBase;
            }
            uint oldCount = (uint)oldList.Count;

            // New array layout: [old0, old1, ..., placeholder, 0]  (oldCount + 2 bytes)
            uint newSize = oldCount + 2;
            uint newBase = rom.FindFreeSpace(startHint, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to expand byte list.");

            // Write the relocated array byte-by-byte through undo.
            for (uint i = 0; i < oldCount; i++)
            {
                rom.write_u8(newBase + i, oldList[(int)i] & 0xFFu, undo);
            }
            // Append the non-zero placeholder (visible after reload) then the
            // trailing 0 terminator.
            rom.write_u8(newBase + oldCount, NewSlotPlaceholder, undo);
            rom.write_u8(newBase + oldCount + 1, 0u, undo);

            // Update the owning pointer (write_p32 takes a ROM offset).
            rom.write_p32(pointerAddr, newBase, undo);

            // INTENTIONAL: do not clear the old bytes. Other owners may still point
            // at oldBase (shared sub-lists); zeroing/0xFF-filling here would corrupt
            // their data. The orphaned bytes are negligible and a rebuild reclaims them.
            return newBase;
        }

        /// <summary>
        /// Replace the ENTIRE list referenced by <paramref name="pointerAddr"/> with
        /// exactly <paramref name="ids"/> (each masked to a byte) followed by a single
        /// <c>0x00</c> terminator, then repoint the owner to the new array. The caller
        /// owns every byte: there is NO placeholder and NO zero-fill heuristic, so a
        /// list of N IDs scans back as exactly N (with no interior <c>0x00</c> except
        /// the terminator). This is the add / delete / reorder / set primitive — the
        /// UI builds the list it wants (add ⇒ <c>ids + [placeholder]</c>; delete ⇒
        /// remove an index; reorder ⇒ permute) and persists it here. Passing an empty
        /// <paramref name="ids"/> writes a terminator-only list (scan returns 0).
        /// All writes are recorded on <paramref name="undo"/>.
        /// </summary>
        /// <returns>The new array base offset (a ROM offset, not a GBA pointer).</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="ids"/> has more than <c>0xFF</c> entries (a
        /// 1-byte-ID null-terminated list cannot exceed that).
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no free space large enough is available (alloc failure). On
        /// failure the operation is reverted by a single rollback of
        /// <paramref name="undo"/>.
        /// </exception>
        /// <remarks>
        /// Like <see cref="ExpandByteList"/>, the original bytes are LEFT INTACT so a
        /// <c>WriteByteList</c> on a shared array implicitly forks the caller's owner
        /// pointer (other owners retain the original list).
        /// </remarks>
        public static uint WriteByteList(ROM rom, uint pointerAddr, IReadOnlyList<uint> ids, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(pointerAddr));
            if (ids.Count > 0xFF)
                throw new ArgumentException("A null-terminated 1-byte-ID list cannot exceed 0xFF entries.", nameof(ids));

            // Use the current array base as the FindFreeSpace start hint (mirrors
            // ItemClassListCore). A null/out-of-range owner pointer just starts the
            // scan at 0x100 below.
            uint oldPtr = rom.u32(pointerAddr);
            uint startHint = 0x100;
            if (U.isPointer(oldPtr))
            {
                uint oldBase = U.toOffset(oldPtr);
                if (oldBase < (uint)rom.Data.Length)
                    startHint = oldBase;
            }

            uint count = (uint)ids.Count;
            // New array layout: [id0, id1, ..., 0]  (count + 1 bytes)
            uint newSize = count + 1;
            uint newBase = rom.FindFreeSpace(startHint, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to write byte list.");

            // Write the caller's IDs verbatim (no placeholder, no zero-fill).
            for (uint i = 0; i < count; i++)
            {
                rom.write_u8(newBase + i, ids[(int)i] & 0xFFu, undo);
            }
            // Single terminator.
            rom.write_u8(newBase + count, 0u, undo);

            // Update the owning pointer (write_p32 takes a ROM offset).
            rom.write_p32(pointerAddr, newBase, undo);

            // INTENTIONAL: leave the old bytes intact (shared-array preservation).
            return newBase;
        }

        /// <summary>
        /// Copy the null-terminated ID array at <paramref name="sourceAddr"/>
        /// (including its <c>0x00</c> terminator) to a new free location and rewrite
        /// <paramref name="ownerPointerAddr"/> to reference the new copy. Leaves the
        /// original bytes untouched so other owners that still point at
        /// <paramref name="sourceAddr"/> continue to work. All writes are recorded on
        /// <paramref name="undo"/>. Mirrors
        /// <see cref="ItemClassListCore.MakeIndependentCopy"/>.
        /// </summary>
        /// <returns>The new array base offset (a ROM offset, not a GBA pointer).</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no free space large enough is available (alloc failure). On
        /// failure the operation is reverted by a single rollback of
        /// <paramref name="undo"/>.
        /// </exception>
        public static uint MakeIndependentCopy(ROM rom, uint sourceAddr, uint ownerPointerAddr, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (ownerPointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(ownerPointerAddr));
            if (sourceAddr >= (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(sourceAddr));

            var list = ScanByteList(rom, sourceAddr);
            uint count = (uint)list.Count;

            // New copy size: count + 1 (terminator).
            uint newSize = count + 1;
            uint newBase = rom.FindFreeSpace(sourceAddr, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to fork byte list.");

            for (uint i = 0; i < count; i++)
            {
                rom.write_u8(newBase + i, list[(int)i] & 0xFFu, undo);
            }
            rom.write_u8(newBase + count, 0u, undo); // terminator

            // Re-point only the requested owner. Other owners still point at sourceAddr.
            rom.write_p32(ownerPointerAddr, newBase, undo);

            return newBase;
        }
    }
}
