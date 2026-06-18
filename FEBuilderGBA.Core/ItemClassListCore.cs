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
        /// Placeholder class ID written into the new slot by
        /// <see cref="ExpandClassList"/>. Must be non-zero so the slot
        /// survives the null-terminator scan in
        /// <see cref="ScanClassList"/>. Class ID 0x01 is the FE Lord class
        /// across all GBA Fire Emblem versions (Eirika in FE8 / Eliwood in
        /// FE7 / Roy in FE6) — a recognizable valid placeholder the user
        /// will obviously want to edit.
        /// </summary>
        public const uint NewSlotPlaceholder = 0x01;

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
            // Pointer semantics: address 0 represents a null pointer and yields
            // an empty list (the WinForms data tables never legitimately point
            // at offset 0). Combined with the bounds check below this guards
            // against scanning unrelated data when the owner pointer is null.
            if (baseAddr == 0) return result;
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
        /// (so the user can edit the new slot in place), and updates the
        /// owning pointer to reference the new array. All writes are recorded
        /// on <paramref name="undo"/>.
        /// </summary>
        /// <returns>The new array base offset.</returns>
        /// <remarks>
        /// The array layout grows from <c>[a, b, 0]</c> to <c>[a, b, 1, 0]</c>:
        /// the appended slot is initialized to a non-zero placeholder (0x01)
        /// so it remains visible in <see cref="ScanClassList"/> after the
        /// reload (a 0 there would be interpreted as the terminator and the
        /// new slot would be invisible to the UI — Copilot CLI review on
        /// PR #463 caught this). Callers can immediately edit the new slot
        /// at <c>newAddr + oldCount</c> to set the desired class ID.
        /// <para>
        /// The original bytes at <c>oldBase</c> are intentionally LEFT INTACT
        /// so any other owners still pointing at <c>oldBase</c> (the
        /// effectiveness editor detects this case via
        /// <see cref="FindItemsSharingPointer"/>) keep working. Wasting a few
        /// bytes is preferable to corrupting another item's data; this means
        /// <c>ExpandClassList</c> on a shared array implicitly forks the
        /// caller's owner pointer (other owners retain the original list).
        /// Tests verify this behaviour via
        /// <c>ExpandClassList_PreservesSharedArrayForOtherOwners</c> and
        /// <c>ExpandClassList_OnSharedArray_OtherOwnersUnchanged</c>.
        /// </para>
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
            // Append a non-zero placeholder (0x01) so the new slot remains
            // visible after a reload (ScanClassList terminates on 0). Then
            // write the trailing 0 terminator.
            rom.write_u8(newBase + oldCount, NewSlotPlaceholder, undo);
            rom.write_u8(newBase + oldCount + 1, 0u, undo);

            // Update the owning pointer.
            rom.write_p32(pointerAddr, newBase, undo);

            // INTENTIONAL: do not clear the old bytes. Other items may still
            // point at oldBase (shared effectiveness arrays); zeroing or
            // 0xFF-filling here would corrupt their data. The orphaned bytes
            // are negligible and ROM rebuild reclaims them.
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

        // =====================================================================
        // SkillSystems "Effectiveness Rework" format (issue #1175)
        //
        // The classic single-byte methods above back the non-rework
        // ItemEffectivenessForm / ItemPromotionForm (issue #368). The FE8U
        // SkillSystems 特効リワーク variant
        // (ItemEffectivenessSkillSystemsReworkForm) stores a DIFFERENT layout:
        // a null-(u32)-terminated array of 4-byte entries
        //   [0]=00, [1]=coefficient*2, [2..3]=class-type (u16 bitmask).
        // Termination follows WinForms N_Init: an entry counts while its
        // leading u8 is 0 AND its u32 is non-zero; the first u32==0 ends the
        // list. Confirmed by ItemAllocCore.BuildEffectivenessTemplate(true)
        // (zero-filled 4-byte stride seeded [1]=6,[2]=1 armor / [5]=6,[6]=2
        // cavalry).
        //
        // The class-type field is the same bitmask the WinForms
        // SkillSystemsEffectivenessReworkClassTypeForm edits
        // (armor/cavalry/flying/dragon/monster/sword/unknown1/unknown2).
        // =====================================================================

        /// <summary>Stride, in bytes, of one Rework effectiveness entry.</summary>
        public const uint ReworkEntrySize = 4;

        /// <summary>
        /// One decoded SkillSystems-Rework effectiveness entry. <see cref="Addr"/>
        /// is the ROM offset of the 4-byte record; <see cref="Coefficient"/> is
        /// the raw +1 byte (the WinForms label is "coefficient_times*2"); and
        /// <see cref="ClassType"/> is the u16 class-type bitmask at +2.
        /// </summary>
        public readonly struct ReworkEntry
        {
            public ReworkEntry(uint addr, uint coefficient, uint classType)
            {
                Addr = addr;
                Coefficient = coefficient;
                ClassType = classType;
            }
            public uint Addr { get; }
            public uint Coefficient { get; }
            public uint ClassType { get; }
        }

        /// <summary>
        /// Scan the null-(u32)-terminated array of 4-byte Rework entries
        /// starting at <paramref name="baseAddr"/>. Mirrors WinForms
        /// <c>ItemEffectivenessSkillSystemsReworkForm.N_Init</c>: an entry is
        /// valid while its leading u8 is 0 AND its u32 is non-zero; the first
        /// u32==0 ends the list. Returns an empty list if
        /// <paramref name="baseAddr"/> is zero, beyond the ROM, or not even
        /// room for one entry.
        /// </summary>
        public static List<ReworkEntry> ScanReworkEntries(ROM rom, uint baseAddr)
        {
            var result = new List<ReworkEntry>();
            if (rom == null || rom.Data == null) return result;
            // Address 0 is a null pointer (same convention as ScanClassList).
            if (baseAddr == 0) return result;
            if (baseAddr >= (uint)rom.Data.Length) return result;

            for (uint i = 0; i < 0x200; i++)
            {
                uint a = baseAddr + i * ReworkEntrySize;
                // Need a full 4-byte record to read the entry + its terminator.
                if (a + ReworkEntrySize > (uint)rom.Data.Length) break;
                // WinForms N_Init termination: leading byte must be 0 and the
                // u32 must be non-zero. The first u32==0 (the terminator) ends
                // the scan; a non-zero leading byte is also treated as the end
                // (defensive — the data should never have one).
                if (rom.u8(a) != 0) break;
                if (rom.u32(a) == 0) break;
                result.Add(new ReworkEntry(a, rom.u8(a + 1), rom.u16(a + 2)));
            }
            return result;
        }

        /// <summary>
        /// Write one Rework entry (the +1 coefficient byte and the +2 u16
        /// class-type) at <paramref name="addr"/>, leaving the leading 0 byte
        /// intact. Records the writes on <paramref name="undo"/>.
        /// </summary>
        public static void WriteReworkEntry(ROM rom, uint addr, uint coefficient, uint classType, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (addr + ReworkEntrySize > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(addr), "Address is out of ROM bounds.");
            // Keep the leading byte at 0 (the format's record marker) so the
            // entry survives the ScanReworkEntries termination check.
            rom.write_u8(addr, 0u, undo);
            rom.write_u8(addr + 1, coefficient & 0xFFu, undo);
            rom.write_u16(addr + 2, classType & 0xFFFFu, undo);
        }

        /// <summary>
        /// Expand the null-(u32)-terminated Rework array referenced by
        /// <paramref name="pointerAddr"/> by one entry. Relocates the existing
        /// entries to free space, appends a new editable entry (leading 0,
        /// coefficient = <see cref="ReworkNewSlotCoefficient"/>, class-type =
        /// <see cref="ReworkNewSlotClassType"/> so the new entry survives the
        /// terminator scan and is visible to the UI), writes a u32==0
        /// terminator, and repoints only the owning pointer. The original bytes
        /// are intentionally LEFT INTACT so other owners that still point at the
        /// old array keep working (copy-on-write, mirroring
        /// <see cref="ExpandClassList"/>).
        /// </summary>
        /// <returns>The new array base offset.</returns>
        public static uint ExpandReworkList(ROM rom, uint pointerAddr, Undo.UndoData undo)
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

            var oldList = ScanReworkEntries(rom, oldBase);
            uint oldCount = (uint)oldList.Count;

            // New layout: oldCount entries + 1 new entry + 1 terminator (u32 0).
            uint newSize = (oldCount + 1) * ReworkEntrySize + ReworkEntrySize;
            uint newBase = rom.FindFreeSpace(oldBase, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to expand rework list.");

            // Copy the relocated entries.
            for (uint i = 0; i < oldCount; i++)
            {
                uint dst = newBase + i * ReworkEntrySize;
                rom.write_u8(dst, 0u, undo);
                rom.write_u8(dst + 1, oldList[(int)i].Coefficient & 0xFFu, undo);
                rom.write_u16(dst + 2, oldList[(int)i].ClassType & 0xFFFFu, undo);
            }
            // Append the new editable entry (non-zero so it is visible).
            uint newEntry = newBase + oldCount * ReworkEntrySize;
            rom.write_u8(newEntry, 0u, undo);
            rom.write_u8(newEntry + 1, ReworkNewSlotCoefficient, undo);
            rom.write_u16(newEntry + 2, ReworkNewSlotClassType, undo);
            // Write the trailing u32==0 terminator.
            uint term = newEntry + ReworkEntrySize;
            rom.write_u32(term, 0u, undo);

            // Repoint only the requested owner.
            rom.write_p32(pointerAddr, newBase, undo);

            // INTENTIONAL: leave the old bytes intact (shared-array safety).
            return newBase;
        }

        /// <summary>
        /// Fork the null-(u32)-terminated Rework array at
        /// <paramref name="sourceAddr"/> into an independent copy and repoint
        /// only <paramref name="ownerPointerAddr"/>. Leaves the original bytes
        /// untouched so other owners keep working. Mirrors
        /// <see cref="MakeIndependentCopy"/> for the 4-byte Rework format.
        /// </summary>
        /// <returns>The new array base offset.</returns>
        public static uint MakeIndependentReworkCopy(ROM rom, uint sourceAddr, uint ownerPointerAddr, Undo.UndoData undo)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (ownerPointerAddr + 4 > (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(ownerPointerAddr));
            if (sourceAddr >= (uint)rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(sourceAddr));

            var list = ScanReworkEntries(rom, sourceAddr);
            uint count = (uint)list.Count;

            // New copy: count entries + 1 terminator (u32 0).
            uint newSize = count * ReworkEntrySize + ReworkEntrySize;
            uint newBase = rom.FindFreeSpace(sourceAddr, newSize);
            if (newBase == U.NOT_FOUND)
                newBase = rom.FindFreeSpace(0x100, newSize);
            if (newBase == U.NOT_FOUND)
                throw new InvalidOperationException("No free space large enough to fork rework list.");

            for (uint i = 0; i < count; i++)
            {
                uint dst = newBase + i * ReworkEntrySize;
                rom.write_u8(dst, 0u, undo);
                rom.write_u8(dst + 1, list[(int)i].Coefficient & 0xFFu, undo);
                rom.write_u16(dst + 2, list[(int)i].ClassType & 0xFFFFu, undo);
            }
            rom.write_u32(newBase + count * ReworkEntrySize, 0u, undo); // terminator

            rom.write_p32(ownerPointerAddr, newBase, undo);
            return newBase;
        }

        /// <summary>
        /// Coefficient byte written into a new entry by
        /// <see cref="ExpandReworkList"/>. 6 == "x3" effectiveness in the
        /// SkillSystems rework (coefficient_times*2), matching the armor/cavalry
        /// seed in <c>ItemAllocCore.BuildEffectivenessTemplate(true)</c>.
        /// </summary>
        public const uint ReworkNewSlotCoefficient = 6;

        /// <summary>
        /// Class-type bitmask written into a new entry by
        /// <see cref="ExpandReworkList"/>. 0x01 == armor — a recognizable,
        /// non-zero default so the new entry survives the terminator scan and
        /// is immediately visible/editable.
        /// </summary>
        public const uint ReworkNewSlotClassType = 0x01;

        // Class-type bit → name table (matches WinForms
        // SkillSystemsEffectivenessReworkClassTypeForm.GetText). The display
        // strings are localized at the Avalonia layer; Core returns the
        // canonical English keys so the UI can run them through R._().
        static readonly (uint Bit, string Key)[] ReworkClassTypeBits =
        {
            (0x01, "Armor"),
            (0x02, "Cavalry"),
            (0x04, "Flying"),
            (0x08, "Dragon"),
            (0x10, "Monster"),
            (0x20, "Sword"),
            (0x40, "Unknown1"),
            (0x80, "Unknown2"),
        };

        /// <summary>
        /// Decode a Rework class-type bitmask into its set bit names (English
        /// keys, comma-joined), mirroring WinForms
        /// <c>SkillSystemsEffectivenessReworkClassTypeForm.GetText</c>. Returns
        /// an empty string when no known bit is set. The Avalonia layer runs
        /// each key through <c>R._()</c> for localization.
        /// </summary>
        public static string GetClassTypeNames(uint classType)
        {
            var parts = new List<string>();
            foreach (var (bit, key) in ReworkClassTypeBits)
            {
                if ((classType & bit) == bit) parts.Add(key);
            }
            return string.Join(",", parts);
        }
    }
}
