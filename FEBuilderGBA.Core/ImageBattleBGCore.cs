// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side extraction of ImageBattleBGForm's helpers that the Avalonia
// view needs (#434):
//
//   1. `ExpandList`           - the 255-cap list-expansion semantics from
//                              `InputFormRef.OnAddressListExpandsEventHandler`,
//                              minus the interactive `MoveToFreeSapceForm`
//                              UI. Allocates new space via
//                              `ROM.FindFreeSpace`, preserves old rows,
//                              fills new rows from row[0], repoints
//                              `battle_bg_pointer`, records undo data.
//   2. `MakeListByUseTerrain` - the X_REF cross-reference list shown in
//                              the AV view's "References" pane. Mirrors
//                              `MapTerrainBGLookupTableForm
//                              .MakeListByUseTerrain` but Core-only.
//
// Both helpers are platform-independent and reusable from WinForms,
// Avalonia, and CLI.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    public static class ImageBattleBGCore
    {
        /// <summary>
        /// Size of one battle-BG table entry in ROM bytes.
        ///
        /// Entry layout:
        ///   +0  u32 image  pointer (LZ77-compressed graphic)
        ///   +4  u32 tsa    pointer (LZ77-compressed TSA)
        ///   +8  u32 palette pointer (LZ77-compressed 16-color palette)
        /// </summary>
        public const uint EntrySize = 12;

        /// <summary>
        /// Maximum count the WinForms `AddressListExpandsButton_255`
        /// button accepts (the suffix is the inclusive cap; see
        /// `InputFormRef.GetAddressListExpandsMax`). New counts greater
        /// than this are rejected to prevent unbounded ROM growth.
        /// </summary>
        public const uint MaxListCount = 255;

        /// <summary>
        /// Expand the battle-BG pointer table to <paramref name="newCount"/>
        /// entries.
        ///
        /// Mirrors the WinForms `AddressListExpandsButton_255` flow
        /// without the interactive `MoveToFreeSapceForm` dialog:
        /// <list type="number">
        ///   <item>Validate inputs (caps at <see cref="MaxListCount"/>,
        ///     refuses zero <paramref name="oldCount"/>, refuses shrinks).</item>
        ///   <item>Find a contiguous free region big enough for
        ///     <c>newCount * EntrySize</c> bytes via
        ///     <see cref="ROM.FindFreeSpace"/>.</item>
        ///   <item>Copy the <paramref name="oldCount"/> existing rows to
        ///     the new region byte-for-byte (under undo).</item>
        ///   <item>Fill the new rows by duplicating row[0] (matches WF's
        ///     "fill from first record" behavior under undo).</item>
        ///   <item>Repoint <c>rom.RomInfo.battle_bg_pointer</c> to the
        ///     new region (under undo).</item>
        /// </list>
        ///
        /// Returns the new base ROM offset (not a GBA pointer), or
        /// <see cref="U.NOT_FOUND"/> on failure.
        /// </summary>
        /// <param name="rom">Target ROM. Must be non-null with valid
        ///   <c>RomInfo.battle_bg_pointer</c>.</param>
        /// <param name="oldCount">The currently-loaded number of rows the
        ///   caller has determined. The Core helper trusts this rather
        ///   than re-scanning the table for determinism.</param>
        /// <param name="newCount">The desired number of rows after
        ///   expansion. Must be > <paramref name="oldCount"/> and
        ///   &lt;= <see cref="MaxListCount"/>.</param>
        /// <param name="undo">Undo data the caller has staged for this
        ///   operation. The helper appends every byte it writes so a
        ///   rollback restores the original ROM state.</param>
        /// <returns>New base ROM offset, or <see cref="U.NOT_FOUND"/>
        ///   on failure.</returns>
        public static uint ExpandList(ROM rom, uint oldCount, uint newCount, Undo.UndoData undo)
        {
            if (rom == null || rom.RomInfo == null)
                return U.NOT_FOUND;
            if (undo == null)
                return U.NOT_FOUND;
            if (oldCount == 0)
                return U.NOT_FOUND;
            if (newCount <= oldCount)
                return U.NOT_FOUND; // refuse shrinks and no-ops
            if (newCount > MaxListCount)
                return U.NOT_FOUND;

            uint pointerSlot = rom.RomInfo.battle_bg_pointer;
            if (pointerSlot == 0)
                return U.NOT_FOUND;

            uint origBase = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(origBase))
                return U.NOT_FOUND;
            if (origBase + oldCount * EntrySize > (uint)rom.Data.Length)
                return U.NOT_FOUND;

            // Snapshot row[0] before we relocate — it's the template for
            // every new row.
            byte[] row0 = rom.getBinaryData(origBase, EntrySize);

            // Find a contiguous free region. Start the search past the
            // existing data area so we don't overlap the old table — the
            // BattleAnimeImportCore pattern uses ROM.Data.Length / 2 as
            // the conventional split.
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint needSize = newCount * EntrySize;
            uint newBase = rom.FindFreeSpace(searchStart, needSize);
            if (newBase == U.NOT_FOUND)
            {
                // Fallback to the conservative search start used by
                // other Core importers when the high-half is fragmented.
                newBase = rom.FindFreeSpace(0x100, needSize);
            }
            if (newBase == U.NOT_FOUND)
                return U.NOT_FOUND;

            // 1. Copy old rows under undo.
            byte[] oldRows = rom.getBinaryData(origBase, oldCount * EntrySize);
            rom.write_range(newBase, oldRows, undo);

            // 2. Fill the new rows by cloning row[0] under undo.
            for (uint i = oldCount; i < newCount; i++)
            {
                uint rowAddr = newBase + i * EntrySize;
                rom.write_range(rowAddr, row0, undo);
            }

            // 3. Repoint battle_bg_pointer to the new base (GBA-format).
            uint newGbaPointer = newBase | 0x08000000u;
            rom.write_u32(pointerSlot, newGbaPointer, undo);

            return newBase;
        }

        /// <summary>
        /// Build the X_REF cross-reference list shown in the AV view's
        /// "References" pane. For each terrain-set slot that references
        /// the given <paramref name="terrainId"/> by way of the floor
        /// lookup table, emit one <see cref="AddrResult"/> with a label
        /// pointing back at the BG entry that the terrain uses.
        ///
        /// Mirrors `MapTerrainBGLookupTableForm.MakeListByUseTerrain` but
        /// the Core-only path. When the ROM has no terrain-lookup table
        /// populated (e.g. a synthetic test ROM, a not-yet-loaded ROM,
        /// or a ROM where the MapTerrainLookup pointer is zero), returns
        /// an empty list — never throws.
        /// </summary>
        /// <param name="rom">Target ROM. May be null (returns empty).</param>
        /// <param name="terrainId">The terrain index that the BG entry
        ///   is being asked about. Typically the AddressList's selected
        ///   index in the AV view.</param>
        /// <returns>List of <see cref="AddrResult"/> rows for display
        ///   in the X_REF pane. Empty on any failure to read.</returns>
        public static List<AddrResult> MakeListByUseTerrain(ROM rom, uint terrainId)
        {
            var ret = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null)
                return ret;

            // WF `MapTerrainBGLookupTableForm.MakeListByUseTerrain` iterates
            // the BG lookup-table pointers (one per terrain set), and for
            // each pointer scans the small N-byte block looking for an
            // entry whose icon byte matches the requested terrain id.
            //
            // We reuse `MapTerrainLookupCore.GetPointers` (already extracted
            // by #482) to get the BG-side pointer array, then walk each
            // pointer's block looking for matches. Empty list on any
            // missing-data condition — never throws.
            uint count = rom.RomInfo.map_terrain_type_count;
            if (count == 0 || count > 0x100)
                return ret;

            uint[] pointers;
            try
            {
                pointers = MapTerrainLookupCore.GetPointers(rom, isFloor: false);
            }
            catch
            {
                return ret;
            }
            if (pointers == null || pointers.Length == 0)
                return ret;

            // Each lookup-table block has `count` entries; each entry's
            // first byte is the icon (terrain id + 1, per the WF helper).
            for (int set = 0; set < pointers.Length; set++)
            {
                uint ptrSlot = pointers[set];
                if (ptrSlot == 0) continue;

                uint baseAddr = rom.p32(ptrSlot);
                if (!U.isSafetyOffset(baseAddr))
                    continue;

                for (uint i = 0; i < count; i++)
                {
                    uint entryAddr = baseAddr + i;
                    if (entryAddr >= (uint)rom.Data.Length)
                        break;
                    uint icon = rom.u8(entryAddr);
                    if (icon == 0)
                        continue;
                    uint iconTerrain = (uint)(icon - 1);
                    if (iconTerrain != terrainId)
                        continue;
                    string name = U.ToHexString(i)
                        + ":Set " + U.To0xHexString((uint)set);
                    ret.Add(new AddrResult(entryAddr, name, (uint)set));
                }
            }
            return ret;
        }
    }
}
