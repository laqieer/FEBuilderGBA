// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM-MUTATING port of the WinForms "PLIST Split/Expand"
    /// operation — the "PLIST分割" button on the Map Pointer editor
    /// (<c>MapPointerForm.PListSplitsExpands</c> /
    /// <c>PListSplitsExpandsOne</c>, <c>MapPointerForm.cs:745-963</c>) — for
    /// the Avalonia Map Pointer editor (#1432).
    ///
    /// <para>On a vanilla (non-split) ROM every <c>map_*_pointer</c> slot
    /// shares ONE PLIST table base, so the usable PLIST count is capped at the
    /// per-version <c>map_map_pointer_list_default_size</c> (~0xE5..0xFC). The
    /// split breaks that shared table into one independent 256-entry
    /// (1024-byte) table per PLIST PURPOSE, lifting the limit to the full byte
    /// range (256). After the split each purpose can use all 255 slots
    /// independently.</para>
    ///
    /// <para>Purposes split (WF order): OBJECT (folds OBJ-low + OBJ-high +
    /// PALETTE), CONFIG, MAP, CHANGE, ANIMATION (folds ANIME1 + ANIME2), EVENT;
    /// plus the FE6-only WORLDMAP table. Each new table copies, for every map,
    /// the original dereferenced pointer for that map's PLIST byte into
    /// <c>newArray[plist*4]</c>, so existing maps keep pointing at the exact
    /// same data after the split.</para>
    ///
    /// <para><b>Atomicity (self-contained, #1432 Copilot plan-review point 1):</b>
    /// <see cref="Split"/> owns its own defensive <c>byte[]</c> snapshot +
    /// <see cref="Undo.UndoData"/> + <see cref="ROM.BeginUndoScope"/>. On ANY
    /// fault it restores the ROM byte-identical (including a down-resize if a
    /// free-space append grew the ROM) and returns <c>false</c>; on success it
    /// <c>CoreState.Undo.Push</c>es the captured undo. This mirrors the
    /// <see cref="SkillSystemsAnimeImportCore"/> single-import contract — no
    /// caller can leave partial appends/repoints/resizes behind.</para>
    ///
    /// <para>Reuses existing Core seams (no fork): <see cref="MapChangeCore"/>
    /// (PlistType / GetPlistBasePointer / IsPlistSplit / GetPlistLimit),
    /// <see cref="MapSettingCore.MakeMapIDList(ROM)"/>,
    /// <see cref="MapPListResolverCore.GetMapPListsWhereAddr(ROM, uint)"/>, and
    /// <see cref="ImageImportCore.FindAndWriteData(ROM, byte[])"/>.</para>
    /// </summary>
    public static class MapPlistSplitCore
    {
        /// <summary>One PLIST table is byte-indexed → 256 entries × 4 bytes.</summary>
        internal const int PLIST_ENTRY_COUNT = 256;
        internal const int PLIST_TABLE_BYTES = PLIST_ENTRY_COUNT * 4;

        /// <summary>
        /// True when the Map Pointer editor should offer the PLIST Split
        /// operation: a valid ROM whose PLIST tables are NOT already split
        /// (<see cref="MapChangeCore.IsPlistSplit"/> == false). WF shows the
        /// "PLIST分割" panel under the same condition.
        /// </summary>
        public static bool CanSplit(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return false;
            return !MapChangeCore.IsPlistSplit(rom);
        }

        /// <summary>
        /// The PLIST purposes that get an independent table, in WF
        /// <c>PListSplitsExpands</c> order. WORLDMAP is appended only for FE6.
        /// </summary>
        static List<MapChangeCore.PlistType> SplitPurposes(ROM rom)
        {
            var list = new List<MapChangeCore.PlistType>
            {
                MapChangeCore.PlistType.OBJECT,    // shares its table with PALETTE
                MapChangeCore.PlistType.CONFIG,
                MapChangeCore.PlistType.MAP,
                MapChangeCore.PlistType.CHANGE,
                MapChangeCore.PlistType.ANIMATION, // shares its table with ANIMATION2
                MapChangeCore.PlistType.EVENT,
            };
            if (rom.RomInfo.version == 6)
                list.Add(MapChangeCore.PlistType.WORLDMAP_FE6ONLY);
            return list;
        }

        /// <summary>
        /// Perform the PLIST split. Validate-all-before-mutate, one ambient
        /// undo scope, byte-identical restore on fault.
        /// </summary>
        /// <param name="rom">ROM to mutate.</param>
        /// <param name="error">Human-readable failure reason ("" on success).</param>
        /// <returns><c>true</c> on success (tables split, ROM left in the split
        /// state and the undo pushed); <c>false</c> on any failure (ROM left
        /// byte-identical to entry).</returns>
        public static bool Split(ROM rom, out string error)
            => Split(rom, out error, null);

        /// <summary>
        /// Test seam: <paramref name="faultInjector"/> is invoked after each
        /// purpose's array is appended; returning <c>true</c> forces that
        /// append to be treated as a free-space failure (NOT_FOUND), exercising
        /// the byte-identical rollback path without needing a genuinely
        /// exhausted ROM. Production callers use the public 2-arg overload
        /// (<paramref name="faultInjector"/> == null).
        /// </summary>
        internal static bool Split(ROM rom, out string error, Func<MapChangeCore.PlistType, bool> faultInjector)
        {
            error = "";
            if (rom == null || rom.RomInfo == null) { error = "no ROM"; return false; }
            if (MapChangeCore.IsPlistSplit(rom))
            {
                // Already split — WF never reaches PListSplitsExpands in this
                // state (the panel is hidden). Refuse without mutating.
                error = "PLIST tables are already split.";
                return false;
            }

            // Validate the maps + old bases BEFORE any mutation.
            List<AddrResult> mapList = MapSettingCore.MakeMapIDList(rom);
            if (mapList == null || mapList.Count == 0) { error = "no maps found"; return false; }

            // Capture EVERY old base address (deref of each purpose's base
            // pointer) BEFORE mutating, deduped. On a vanilla ROM all of these
            // are the SAME shared base; on a partially-split layout (IsPlistSplit
            // still false because CONFIG shares with at least one other table)
            // there can be several distinct old bases — wipe each safe one.
            // (#1432 Copilot plan-review point 2.)
            var oldBases = new HashSet<uint>();
            foreach (MapChangeCore.PlistType t in SplitPurposes(rom))
            {
                uint basePtr = MapChangeCore.GetPlistBasePointer(rom, t);
                if (basePtr == 0) continue;
                uint baseAddr = rom.p32(basePtr);
                if (U.isSafetyOffset(baseAddr, rom))
                    oldBases.Add(baseAddr);
            }

            // Old-region wipe size: the vanilla list spanned the per-version
            // default count of 4-byte entries. WF's ClearOrignalData walks the
            // shared table with this same limit (BlockSize 4) and fills it 0x00.
            uint wipeCount = rom.RomInfo.map_map_pointer_list_default_size;

            byte[] snapshot = (byte[])rom.Data.Clone();
            // Build the UndoData WITHOUT going through CoreState.Undo.NewUndoData:
            // that path reads CoreState.ROM.Data.Length for its file-size
            // snapshot and would NRE on a ROM-explicit/headless caller whose
            // CoreState.ROM is not the passed rom. We set the fields from the
            // passed rom directly so the helper never touches CoreState.ROM.
            Undo.UndoData undoData = new Undo.UndoData
            {
                time = DateTime.Now.ToLocalTime(),
                name = "MapPlistSplitCore.Split",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
                is_f5test = false,
            };

            try
            {
                using (ROM.BeginUndoScope(undoData))
                {
                    foreach (MapChangeCore.PlistType purpose in SplitPurposes(rom))
                    {
                        byte[] newArray = BuildPlistArray(rom, purpose, mapList);

                        uint newPos;
                        if (faultInjector != null && faultInjector(purpose))
                            newPos = U.NOT_FOUND; // simulated free-space failure
                        else
                            newPos = ImageImportCore.FindAndWriteData(rom, newArray);

                        if (newPos == U.NOT_FOUND)
                            throw new InvalidOperationException(
                                "PLIST expand failed (out of free space): " + purpose);

                        RepointPurpose(rom, purpose, newPos);
                    }

                    // Wipe every old shared base region (deduped). Idempotent in
                    // the vanilla single-base case; clears each distinct base in
                    // a partially-split layout.
                    foreach (uint baseAddr in oldBases)
                        ClearOldRegion(rom, baseAddr, wipeCount);
                }
            }
            catch (Exception ex)
            {
                // Restore byte-for-byte. Down-resize first if a free-space
                // append grew rom.Data, then in-place copy keeps any cached
                // offsets valid (SkillSystemsAnimeImportCore #923 H1 pattern).
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                error = ex.Message;
                return false;
            }

            // Post-condition: the operation must leave the ROM in the split
            // state (defensive — a silent no-op would be a logic bug).
            if (!MapChangeCore.IsPlistSplit(rom))
            {
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                error = "PLIST split did not take effect.";
                return false;
            }

            if (CoreState.Undo != null)
                CoreState.Undo.Push(undoData);
            return true;
        }

        /// <summary>
        /// Build the fresh 256-entry pointer table for one PLIST purpose.
        /// Ports WF <c>PListSplitsExpandsOne</c>'s per-map convert loop: for
        /// every map, copy the original dereferenced pointer for that map's
        /// PLIST byte into <c>newArray[plist*4]</c>. OBJECT folds in
        /// OBJ-low + OBJ-high + PALETTE; ANIMATION folds in ANIME1 + ANIME2.
        /// </summary>
        static byte[] BuildPlistArray(ROM rom, MapChangeCore.PlistType purpose, List<AddrResult> mapList)
        {
            byte[] newArray = new byte[PLIST_TABLE_BYTES];

            // The slot pointers are read from the CURRENT (still-shared) table
            // for this purpose. WF uses InputFormRef.ReInitPointer(GetBasePointer)
            // then IDToAddr(plist) → p32. Here we read directly from the old
            // base table: oldBaseAddr + plist*4.
            uint basePtr = MapChangeCore.GetPlistBasePointer(rom, purpose);
            uint oldBaseAddr = basePtr != 0 ? rom.p32(basePtr) : 0;

            foreach (AddrResult map in mapList)
            {
                if (purpose == MapChangeCore.PlistType.WORLDMAP_FE6ONLY)
                {
                    uint wmap = MapPListResolverCore.GetWorldMapEventIDWhereAddr(rom, map.addr);
                    Convert(rom, wmap, oldBaseAddr, newArray);
                    continue;
                }

                MapPListResolverCore.PLists plists =
                    MapPListResolverCore.GetMapPListsWhereAddr(rom, map.addr);

                switch (purpose)
                {
                    case MapChangeCore.PlistType.CHANGE:
                        Convert(rom, plists.mapchange_plist, oldBaseAddr, newArray);
                        break;
                    case MapChangeCore.PlistType.EVENT:
                        Convert(rom, plists.event_plist, oldBaseAddr, newArray);
                        break;
                    case MapChangeCore.PlistType.MAP:
                        Convert(rom, plists.mappointer_plist, oldBaseAddr, newArray);
                        break;
                    case MapChangeCore.PlistType.CONFIG:
                        Convert(rom, plists.config_plist, oldBaseAddr, newArray);
                        break;
                    case MapChangeCore.PlistType.ANIMATION:
                        Convert(rom, plists.anime1_plist, oldBaseAddr, newArray);
                        Convert(rom, plists.anime2_plist, oldBaseAddr, newArray);
                        break;
                    default: // OBJECT (also covers OBJ + PALETTE)
                        uint obj1 = plists.obj_plist & 0xFF;
                        uint obj2 = (plists.obj_plist >> 8) & 0xFF; // FE7 dual-tileset high byte
                        Convert(rom, obj1, oldBaseAddr, newArray);
                        Convert(rom, obj2, oldBaseAddr, newArray);
                        Convert(rom, plists.palette_plist, oldBaseAddr, newArray);
                        break;
                }
            }

            return newArray;
        }

        /// <summary>
        /// Port of WF <c>PListSplitExpandsOneConvertPointer</c>: copy the
        /// dereferenced pointer at <c>oldBaseAddr + plist*4</c> into
        /// <c>newArray[plist*4]</c>. Skips plist 0 (the reserved NULL slot) and
        /// any unsafe source/target — leaving that entry 0 (an empty/null PLIST).
        /// </summary>
        static void Convert(ROM rom, uint plist, uint oldBaseAddr, byte[] newArray)
        {
            if (plist == 0 || plist >= PLIST_ENTRY_COUNT) return;
            if (!U.isSafetyOffset(oldBaseAddr, rom)) return;

            uint slotAddr = oldBaseAddr + plist * 4u;
            if (slotAddr + 4u > (uint)rom.Data.Length) return;

            uint addr = rom.p32(slotAddr);
            if (!U.isSafetyOffset(addr, rom)) return;

            U.write_p32(newArray, plist * 4u, addr);
        }

        /// <summary>
        /// Repoint every <c>map_*_pointer</c> bound to <paramref name="purpose"/>
        /// at the newly appended table. OBJECT also repoints PALETTE; ANIMATION
        /// also repoints ANIMATION2 (the two share one split table). Mirrors WF
        /// <c>PListSplitsExpandsOne</c>'s repoint block. Writes go through the
        /// ambient undo scope opened by <see cref="Split"/>.
        /// </summary>
        static void RepointPurpose(ROM rom, MapChangeCore.PlistType purpose, uint newPos)
        {
            ROMFEINFO info = rom.RomInfo;
            switch (purpose)
            {
                case MapChangeCore.PlistType.CONFIG:
                    rom.write_p32(info.map_config_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.ANIMATION:
                    rom.write_p32(info.map_tileanime1_pointer, newPos);
                    rom.write_p32(info.map_tileanime2_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.OBJECT:
                    rom.write_p32(info.map_obj_pointer, newPos);
                    rom.write_p32(info.map_pal_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.MAP:
                    rom.write_p32(info.map_map_pointer_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.CHANGE:
                    rom.write_p32(info.map_mapchange_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.EVENT:
                    rom.write_p32(info.map_event_pointer, newPos);
                    break;
                case MapChangeCore.PlistType.WORLDMAP_FE6ONLY:
                    rom.write_p32(info.map_worldmapevent_pointer, newPos);
                    break;
                default:
                    throw new InvalidOperationException("unexpected PLIST purpose: " + purpose);
            }
        }

        /// <summary>
        /// Wipe the old shared PLIST region with 0x00. Mirrors WF
        /// <c>ClearOrignalData</c>: the region is <paramref name="wipeCount"/>
        /// 4-byte entries. No-op on an unsafe base or a near-EOF region.
        /// </summary>
        static void ClearOldRegion(ROM rom, uint baseAddr, uint wipeCount)
        {
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint bytes = wipeCount * 4u;
            if (bytes == 0) return;
            if (baseAddr + bytes > (uint)rom.Data.Length)
                bytes = (uint)rom.Data.Length - baseAddr; // clamp at EOF
            rom.write_fill(baseAddr, bytes, 0x00);
        }
    }
}
