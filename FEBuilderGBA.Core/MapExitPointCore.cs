using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Map Exit Point listing/allocation logic extracted
    /// from WinForms <c>MapExitPointForm</c> (#425).
    ///
    /// The exit-point table is structured as a pointer-of-pointers:
    /// <list type="number">
    ///   <item><c>RomInfo.map_exit_point_pointer</c> is a 4-byte slot
    ///         holding the GBA pointer to the per-map slot table.</item>
    ///   <item>The slot table contains one 4-byte GBA pointer per map.
    ///         The first <c>RomInfo.map_exit_point_npc_blockadd</c> slots
    ///         hold enemy exit points; the remaining slots hold NPC exit
    ///         points (the table is contiguous — filter just shifts the
    ///         base).</item>
    ///   <item>Each per-map pointer either equals
    ///         <c>RomInfo.map_exit_point_blank</c> (the universal NULL
    ///         marker — "no exits for this map") OR points to a list of
    ///         4-byte rows {X, Y, EscapeMethod, Flag} terminated by a row
    ///         with <c>B0 == 0xFF</c>.</item>
    /// </list>
    ///
    /// This class is intentionally headless: every method takes the
    /// <see cref="ROM"/> explicitly and never reads <see cref="CoreState.ROM"/>
    /// directly, so the Avalonia VM, WinForms form, and headless CLI all
    /// share the same logic.
    /// </summary>
    public static class MapExitPointCore
    {
        /// <summary>Width of one slot in the per-filter pointer table.</summary>
        const uint SLOT_SIZE = 4;
        /// <summary>Width of one row in the per-map exit-point block.</summary>
        const uint ROW_SIZE = 4;
        /// <summary>Initial allocation size used by WF NewListAlloc — one row + one terminator.</summary>
        const uint NEW_ALLOC_SIZE = 8;

        /// <summary>
        /// Resolve the base address of the per-map pointer table for the
        /// requested filter (0 = Enemy, 1 = NPC).
        ///
        /// Enemy filter: <c>p32(map_exit_point_pointer)</c>.
        /// NPC filter:   <c>p32(map_exit_point_pointer) + 4 * npc_blockadd</c>.
        ///
        /// Returns <see cref="U.NOT_FOUND"/> when the ROM, RomInfo, or the
        /// resolved pointer is unsafe.
        /// </summary>
        public static uint ResolveBaseAddress(ROM rom, uint filterIndex)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            uint pointerSlot = rom.RomInfo.map_exit_point_pointer;
            if (pointerSlot == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(pointerSlot, rom)) return U.NOT_FOUND;

            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            if (filterIndex == 0) return baseAddr;

            // Filter 1 (NPC): shift by npc_blockadd * 4.
            uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd;
            uint npcBase = baseAddr + npcBlockAdd * SLOT_SIZE;
            if (!U.isSafetyOffset(npcBase, rom)) return U.NOT_FOUND;
            return npcBase;
        }

        /// <summary>
        /// Enumerate the per-map pointer-slot entries for the requested
        /// filter. Each result row's <see cref="AddrResult.addr"/> is the
        /// slot address (NOT the address of the exit-point list itself —
        /// the caller dereferences via <c>rom.p32(slot)</c> when needed).
        /// The display name is taken from <see cref="MapSettingCore.MakeMapIDList(ROM)"/>
        /// (which uses the chapter/map names from the map setting table).
        ///
        /// The walk stops at the first slot whose value is NOT a valid
        /// GBA-pointer-or-NULL (matches WF <c>MapExitPointForm.Init</c>),
        /// OR at <c>map_exit_point_npc_blockadd</c> for enemy filter
        /// (filter 0), matching the WF "i &lt; npc_blockadd" predicate.
        /// </summary>
        public static List<AddrResult> ListMapEntries(ROM rom, uint filterIndex)
        {
            var result = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null) return result;

            uint baseAddr = ResolveBaseAddress(rom, filterIndex);
            if (baseAddr == U.NOT_FOUND) return result;

            // Use map names from MapSettingCore for the display column —
            // mirrors the WF lambda `(int i, uint addr) => MapSettingForm.GetMapName((uint)i)`.
            var mapNameList = MapSettingCore.MakeMapIDList(rom);

            // Both filters cap by npc_blockadd — this mirrors WF
            // MapExitPointForm.Init's predicate `i < npc_blockadd`. Using a
            // larger NPC cap would let stray garbage past the real table end
            // leak in as fake entries (Copilot PR #531 review thread on
            // MapExitPointCore line 98).
            uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd;
            uint maxEntries = npcBlockAdd > 0 ? npcBlockAdd : 0x100u;
            for (uint i = 0; i < maxEntries; i++)
            {
                uint slot = baseAddr + i * SLOT_SIZE;
                if (!U.isSafetyOffset(slot, rom)) break;
                if (slot + SLOT_SIZE > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(slot);
                if (!U.isPointerOrNULL(ptr)) break;

                // Display name: prefer the matching map setting entry,
                // else fall back to a hex-id string.
                string displayName;
                if (i < mapNameList.Count)
                {
                    displayName = mapNameList[(int)i].name;
                }
                else
                {
                    displayName = U.ToHexString(i);
                }
                result.Add(new AddrResult(slot, displayName, i));
            }
            return result;
        }

        /// <summary>
        /// The exit-point pointer-table SLOT address for a given map id, taken
        /// from the main (filter 0) table via <see cref="ListMapEntries(ROM, uint)"/>
        /// (so it respects the <c>npc_blockadd</c> cap). Returns
        /// <see cref="U.NOT_FOUND"/> when no entry exists for that map id.
        /// PURE / read-only — used by the FE6 Map Settings "Jump to ExitPoint".
        /// </summary>
        public static uint GetMapEntrySlotAddr(ROM rom, uint mapId)
        {
            if (rom == null) return U.NOT_FOUND;

            // filterIndex 0 is the DEFAULT / enemy exit-point table. NPC exit
            // entries live in the npc_blockadd-shifted table (filter 1) and are
            // intentionally NOT selected by this Map Settings jump.
            var entries = ListMapEntries(rom, 0);
            foreach (var e in entries)
                if (e.tag == mapId) return e.addr;
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Walk the per-map exit-point block starting at
        /// <paramref name="exitPointAddr"/> (the dereferenced pointer value
        /// — NOT a slot). Each row is 4 bytes; the walk stops on the first
        /// row whose <c>B0 == 0xFF</c> (WF terminator rule) or when the
        /// block runs out of safe ROM bounds.
        ///
        /// Returns an empty list when <paramref name="exitPointAddr"/> is
        /// unsafe, equal to the blank marker, or yields zero rows.
        /// </summary>
        public static List<AddrResult> ListExitPointsForMap(ROM rom, uint exitPointAddr)
        {
            var result = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null) return result;
            if (!U.isSafetyOffset(exitPointAddr, rom)) return result;
            if (IsBlankPointer(rom, exitPointAddr)) return result;

            const uint MAX_ROWS = 0x100; // WF MakeCheckError flags > 0x12 as suspicious; 256 is a generous cap.
            for (uint i = 0; i < MAX_ROWS; i++)
            {
                uint rowAddr = exitPointAddr + i * ROW_SIZE;
                if (rowAddr + ROW_SIZE > (uint)rom.Data.Length) break;
                if (!U.isSafetyOffset(rowAddr, rom)) break;

                uint b0 = rom.u8(rowAddr + 0);
                if (b0 == 0xFFu) break; // terminator row — matches WF N_Init predicate

                result.Add(new AddrResult(rowAddr, U.ToHexString(i), i));
            }
            return result;
        }

        /// <summary>
        /// Count the exit rows in the per-map block at <paramref name="exitPointAddr"/>,
        /// returning true ONLY when a B0==0xFF terminator row is found within the safe
        /// ROM range / 0x100 row cap. <paramref name="count"/> = rows before the
        /// terminator. Returns false (count=0) for unsafe, blank, or unterminated/corrupt
        /// blocks — callers must NOT expand those.
        /// </summary>
        public static bool TryCountExitRows(ROM rom, uint exitPointAddr, out uint count)
        {
            count = 0;
            if (rom?.RomInfo == null) return false;
            if (!U.isSafetyOffset(exitPointAddr, rom)) return false;
            if (IsBlankPointer(rom, exitPointAddr)) return false;
            const uint MAX_ROWS = 0x100;
            for (uint i = 0; i < MAX_ROWS; i++)
            {
                uint rowAddr = exitPointAddr + i * ROW_SIZE;
                if (rowAddr + ROW_SIZE > (uint)rom.Data.Length) return false; // ran off ROM, no terminator
                if (!U.isSafetyOffset(rowAddr, rom)) return false;
                if (rom.u8(rowAddr) == 0xFFu) { count = i; return true; }      // terminator found
            }
            return false; // hit MAX_ROWS without a terminator → corrupt
        }

        /// <summary>
        /// True iff <paramref name="addr"/> equals the version-specific
        /// <c>map_exit_point_blank</c> marker (the universal NULL pointer
        /// shared across all versions — see ROMFE6JP / ROMFE7U / ROMFE8U).
        /// </summary>
        public static bool IsBlankPointer(ROM rom, uint addr)
        {
            if (rom == null || rom.RomInfo == null) return false;
            return addr == rom.RomInfo.map_exit_point_blank;
        }

        /// <summary>
        /// Allocate a new 8-byte exit-point block in free space and repoint
        /// <paramref name="exitPointerSlotAddr"/> to it. The allocated block
        /// contains one zero-initialized row followed by a B0=0xFF
        /// terminator (matches WF <c>NewListAlloc_Click</c>:
        /// <c>data[4] = 0xFF</c>).
        ///
        /// Undo tracking: when <paramref name="undodata"/> is non-null AND
        /// the caller has opened an ambient undo scope (via
        /// <see cref="ROM.BeginUndoScope"/>), the freespace allocation and
        /// the pointer repoint are both captured. Passing a null
        /// <paramref name="undodata"/> is safe (no throw); the caller just
        /// won't be able to rollback the allocation.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="exitPointerSlotAddr">The 4-byte pointer slot to repoint.</param>
        /// <param name="undodata">Active undo group, or null to skip tracking.</param>
        /// <returns>The new block's ROM offset, or <see cref="U.NOT_FOUND"/> on failure.</returns>
        public static uint NewAlloc(ROM rom, uint exitPointerSlotAddr, Undo.UndoData? undodata)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (!U.isSafetyOffset(exitPointerSlotAddr, rom)) return U.NOT_FOUND;
            if (exitPointerSlotAddr + SLOT_SIZE > (uint)rom.Data.Length) return U.NOT_FOUND;

            // Build the WF-shaped initial payload: 8 bytes, data[4] = 0xFF (terminator).
            byte[] data = new byte[NEW_ALLOC_SIZE];
            data[4] = 0xFF;

            // Allocate via the AppendBinaryData seam if wired (production),
            // else fall back to a direct FindFreeSpace + write_range path
            // (headless tests). Both code paths capture undo through the
            // ambient scope opened by BeginUndoScope, so the explicit
            // undodata argument is mostly documentation here — but we still
            // accept it so the API matches the WF
            // `InputFormRef.AppendBinaryData(data, undodata)` shape and
            // future Core seam upgrades don't break callers.
            uint newaddr;
            if (CoreState.AppendBinaryData != null && undodata != null)
            {
                newaddr = CoreState.AppendBinaryData(data, undodata);
            }
            else
            {
                // Headless path: find free space in the upper half, fall
                // back to the lower half. Matches the pattern used by
                // MapEventUnitCore.ExpandUnitList.
                uint searchStart = (uint)(rom.Data.Length / 2);
                newaddr = rom.FindFreeSpace(searchStart, NEW_ALLOC_SIZE);
                if (newaddr == U.NOT_FOUND)
                {
                    newaddr = rom.FindFreeSpace(0x100u, NEW_ALLOC_SIZE);
                }
                if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;
                rom.write_range(newaddr, data);
            }
            if (newaddr == U.NOT_FOUND || newaddr == 0) return U.NOT_FOUND;

            // Repoint the pointer slot. The single-arg `write_p32(addr, a)`
            // overload records undo through the ambient scope (when one is
            // open via ROM.BeginUndoScope), while the three-arg overload
            // appends to the explicit undodata.list. To avoid recording the
            // same position TWICE when both paths are active (the explicit
            // undodata IS the ambient scope's UndoData — same instance), we
            // dispatch based on the ambient state (Copilot PR #531 review
            // thread on MapExitPointCore line 234):
            //
            //   - If undodata == GetAmbientUndoData(): use the no-undo
            //     overload — the ambient scope captures it once.
            //   - If undodata != GetAmbientUndoData() (legacy WF caller
            //     without an ambient scope): use the explicit overload so
            //     the position lands in the caller's UndoData.
            //   - If undodata is null: use the no-undo overload — no
            //     tracking, matches the "headless test" path.
            Undo.UndoData? ambient = ROM.GetAmbientUndoData();
            if (undodata == null || ReferenceEquals(undodata, ambient))
            {
                rom.write_p32(exitPointerSlotAddr, newaddr);
            }
            else
            {
                rom.write_p32(exitPointerSlotAddr, newaddr, undodata);
            }
            return newaddr;
        }
    }
}
