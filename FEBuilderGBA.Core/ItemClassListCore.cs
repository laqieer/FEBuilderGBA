using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper backing the Avalonia Item Effectiveness and Item
    /// Promotion editors (issue #368). Provides item-driven master/detail
    /// semantics that mirror the WinForms <c>ItemEffectivenessForm</c> and
    /// <c>ItemPromotionForm</c>:
    ///   * Scan a null-terminated byte array of class IDs.
    ///   * Write a single class byte through Undo.
    ///   * Expand a null-terminated array by appending a new zero slot before
    ///     the terminator and updating the owning pointer.
    ///   * Fork a shared array into an independent copy by relocating it and
    ///     repointing only the supplied owner pointer.
    ///   * Find the set of items that point at a given effectiveness array.
    ///
    /// The helper is intentionally platform-agnostic so it can be unit tested
    /// from <c>FEBuilderGBA.Core.Tests</c> and called from both WinForms and
    /// Avalonia without dragging in any UI dependencies.
    /// </summary>
    public static class ItemClassListCore
    {
        /// <summary>
        /// Scan a null-terminated array of class IDs starting at
        /// <paramref name="baseAddr"/> and return the IDs encountered up to
        /// (but not including) the first <c>0x00</c> terminator. Stops at the
        /// end of <c>rom.Data</c>. Returns an empty list if
        /// <paramref name="baseAddr"/> is zero or beyond the ROM.
        /// </summary>
        public static List<uint> ScanClassList(ROM rom, uint baseAddr)
        {
            var result = new List<uint>();
            if (rom == null || rom.Data == null) return result;
            if (baseAddr >= (uint)rom.Data.Length) return result;

            // Cap iteration to avoid pathological cases — class IDs cannot
            // exceed 0xFF so any list of > 0x200 is bogus, but we still want a
            // hard cap.
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
        /// Write a single class byte to <paramref name="addr"/> and record the
        /// undo. Use during single-slot edits in the editor's right pane.
        /// </summary>
        public static void WriteClassByte(ROM rom, uint addr, uint classId, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (addr >= (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(addr), "Address is out of ROM bounds.");
            rom.write_u8(addr, classId & 0xFFu, undo);
        }

        /// <summary>
        /// Walk the item table and return the IDs of all items whose +16
        /// effectiveness pointer dereferences to
        /// <paramref name="effectivenessAddr"/>. Termination follows the
        /// WinForms <c>ItemEffectivenessForm</c> rule (first row whose +12 or
        /// +16 is not pointer-or-null ends the table).
        /// </summary>
        public static List<uint> FindItemsSharingPointer(ROM rom, uint effectivenessAddr)
        {
            var owners = new List<uint>();
            if (rom?.RomInfo == null) return owners;

            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return owners;

            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return owners;

            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return owners;

            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = itemBase + i * dataSize;
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;

                // Item-table termination: stop on the first row whose +12 OR
                // +16 is not pointer-or-null.
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 16))) break;

                uint critPtr = rom.u32(itemAddr + 16);
                if (!U.isPointer(critPtr)) continue;

                uint critOff = U.toOffset(critPtr);
                if (critOff == effectivenessAddr)
                {
                    owners.Add(i);
                }
            }
            return owners;
        }

        /// <summary>
        /// Expand the null-terminated class array referenced by
        /// <paramref name="pointerAddr"/> by one slot. Copies the existing
        /// array to free space, writes a new 0-slot just before the terminator
        /// (so the user can edit the new slot in place), updates the owning
        /// pointer to reference the new array, and frees the original bytes
        /// with <c>0xFF</c>. All writes are recorded on <paramref name="undo"/>.
        /// </summary>
        /// <returns>The new array base offset.</returns>
        /// <remarks>
        /// The array layout grows from <c>[a, b, 0]</c> to <c>[a, b, 0, 0]</c>:
        /// the appended zero is the new editable slot, and the trailing zero
        /// stays as the terminator. Callers can immediately edit the new slot
        /// at <c>newAddr + oldCount</c>.
        /// </remarks>
        public static uint ExpandClassList(ROM rom, uint pointerAddr, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(pointerAddr));

            uint oldPtr = rom.u32(pointerAddr);
            if (!U.isPointer(oldPtr))
                throw new InvalidOperationException("Owner pointer does not reference a ROM address.");

            uint oldBase = U.toOffset(oldPtr);
            if (oldBase >= (uint)rom.Data.Length)
                throw new InvalidOperationException("Owner pointer references an out-of-range address.");

            var oldList = ScanClassList(rom, oldBase);
            uint oldCount = (uint)oldList.Count;

            // New array layout: [old0, old1, ..., 0, 0]  (oldCount + 2 bytes)
            uint newSize = oldCount + 2;
            uint newBase = rom.FindFreeSpace(oldBase, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to expand class list.");

            // Write the relocated array byte-by-byte through undo.
            for (uint i = 0; i < oldCount; i++)
            {
                rom.write_u8(newBase + i, oldList[(int)i] & 0xFFu, undo);
            }
            // Append a new editable 0-slot and the terminator.
            rom.write_u8(newBase + oldCount, 0u, undo);
            rom.write_u8(newBase + oldCount + 1, 0u, undo);

            // Update the owning pointer.
            rom.write_p32(pointerAddr, newBase, undo);

            // Free the old bytes (oldCount + 1 to cover terminator).
            for (uint i = 0; i < oldCount + 1; i++)
            {
                rom.write_u8(oldBase + i, 0xFFu, undo);
            }

            return newBase;
        }

        /// <summary>
        /// Copy the null-terminated class array at <paramref name="sourceAddr"/>
        /// to a new free location and rewrite <paramref name="ownerPointerAddr"/>
        /// to reference the new copy. Leaves the original bytes untouched so
        /// other owners that still point at <paramref name="sourceAddr"/>
        /// continue to work. All writes are recorded on
        /// <paramref name="undo"/>.
        /// </summary>
        /// <returns>The new array base offset.</returns>
        public static uint MakeIndependentCopy(ROM rom, uint sourceAddr, uint ownerPointerAddr, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (ownerPointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(ownerPointerAddr));
            if (sourceAddr >= (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(sourceAddr));

            var list = ScanClassList(rom, sourceAddr);
            uint count = (uint)list.Count;

            // New copy size: count + 1 (terminator).
            uint newSize = count + 1;
            uint newBase = rom.FindFreeSpace(sourceAddr, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to fork class list.");

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
