// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper for resolving a map's change-data address.
    /// Ports the WinForms <c>MapSettingForm.GetMapChangeAddrWhereMapID</c>
    /// + <c>MapPointerForm.PlistToOffsetAddrFast(CHANGE, ...)</c>
    /// path so the Avalonia EventMapChange editor can navigate by map ID
    /// without depending on WinForms code or <c>CoreState.ROM</c>.
    ///
    /// All methods are ROM-explicit — pass in the <see cref="ROM"/> you
    /// want to query. The helper validates pointer safety using
    /// <see cref="U.isSafetyOffset(uint, ROM)"/> so synthetic-ROM tests
    /// never accidentally fall back to <c>CoreState.ROM</c>.
    /// </summary>
    public static class MapChangeCore
    {
        /// <summary>
        /// Core-local equivalent of <c>MapPointerForm.PLIST_TYPE</c>.
        /// Only the values needed by Core helpers are surfaced; the
        /// WinForms enum has additional types (ANIMATION, etc.) that
        /// are not yet exposed to Core.
        /// </summary>
        public enum PlistType
        {
            CHANGE,
            CONFIG,
            /// <summary>
            /// OBJ tileset PLIST table (<c>map_obj_pointer</c>). Used by
            /// the Map Style Editor OBJ Image Import path (#710) to
            /// rewrite the per-style OBJ tileset slot with a newly
            /// LZ77-compressed 4bpp tile sheet.
            /// </summary>
            OBJECT,
            /// <summary>
            /// Palette PLIST table (<c>map_pal_pointer</c>). Used by the
            /// Event Map Change overlay render (#857) to resolve the shared
            /// palette for a given change event's parent map.
            /// </summary>
            PALETTE,
            /// <summary>
            /// Map-arrangement PLIST table (<c>map_map_pointer_pointer</c>).
            /// Added by the map-PLIST label resolver (#952) so the
            /// MapPointer editor can resolve a MAP-type base back to a map name.
            /// </summary>
            MAP,
            /// <summary>
            /// Event PLIST table (<c>map_event_pointer</c>). Added by #952.
            /// </summary>
            EVENT,
            /// <summary>
            /// Tile-animation #1 PLIST table (<c>map_tileanime1_pointer</c>).
            /// Added by #952. ANIME1/ANIME2 share the same base in vanilla
            /// ROMs (both resolve under the WF "ANIMATION" filter).
            /// </summary>
            ANIMATION,
            /// <summary>
            /// Tile-animation #2 PLIST table (<c>map_tileanime2_pointer</c>).
            /// Added by #952.
            /// </summary>
            ANIMATION2,
            /// <summary>
            /// FE6-only world-map event PLIST table
            /// (<c>map_worldmapevent_pointer</c>). Added by #952. The WF
            /// <c>PLIST_TYPE.WORLDMAP_FE6ONLY</c> uses this pointer — NOT
            /// the worldmap_point table.
            /// </summary>
            WORLDMAP_FE6ONLY,
        }

        /// <summary>
        /// Port of <c>MapPointerForm.IsPlistSplits()</c>. Returns true
        /// only when the CONFIG plist base differs from EVERY other
        /// plist base (ANIMATION/OBJECT/MAP/CHANGE/EVENT — plus the
        /// FE6-only WORLDMAP table when <c>RomInfo.version == 6</c>).
        /// Any single match returns false (the WF semantics — sharing
        /// any base means the tables have not been split).
        ///
        /// For FE6 (<c>RomInfo.version == 6</c>) the WinForms
        /// <c>MapPointerForm.IsPlistSplits</c> also compares against the
        /// FE6-only <c>map_worldmapevent_pointer</c> table. That match
        /// is included here so a FE6 ROM that splits everything but the
        /// world-map pointer is correctly classified as "not split"
        /// (matching WF behaviour). Without this branch the Core helper
        /// would return <c>true</c> in cases WF returns <c>false</c>,
        /// inflating <see cref="GetPlistLimit"/> from the vanilla
        /// per-ROM value to 256 and accepting PLIST indexes that WF
        /// would reject.
        /// </summary>
        public static bool IsPlistSplit(ROM rom)
        {
            if (rom == null || rom.RomInfo == null)
                return false;

            uint configBase = rom.p32(rom.RomInfo.map_config_pointer);
            uint animBase = rom.p32(rom.RomInfo.map_tileanime1_pointer);
            if (configBase == animBase) return false;

            uint objBase = rom.p32(rom.RomInfo.map_obj_pointer);
            if (configBase == objBase) return false;

            uint mapBase = rom.p32(rom.RomInfo.map_map_pointer_pointer);
            if (configBase == mapBase) return false;

            uint changeBase = rom.p32(rom.RomInfo.map_mapchange_pointer);
            if (configBase == changeBase) return false;

            uint eventBase = rom.p32(rom.RomInfo.map_event_pointer);
            if (configBase == eventBase) return false;

            // FE6-only: the world-map pointer shares the same vanilla
            // base when the ROM is not split. Skipping this check would
            // misclassify FE6 ROMs where CONFIG differs from
            // ANIMATION/OBJECT/MAP/CHANGE/EVENT but matches the world
            // map as split (Copilot CLI re-review on issue #423).
            if (rom.RomInfo.version == 6 && rom.RomInfo.map_worldmapevent_pointer != 0)
            {
                uint wmapBase = rom.p32(rom.RomInfo.map_worldmapevent_pointer);
                if (configBase == wmapBase) return false;
            }

            return true;
        }

        /// <summary>
        /// Port of <c>MapPointerForm.Init</c>'s `limit` calculation.
        /// Returns 256 when the ROM has split plist tables (every plist
        /// byte is a uint8 index — capped at 255), else the per-version
        /// vanilla default size <see cref="ROMFEINFO.map_map_pointer_list_default_size"/>.
        /// </summary>
        public static uint GetPlistLimit(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return 0;
            if (IsPlistSplit(rom)) return 256u;
            return rom.RomInfo.map_map_pointer_list_default_size;
        }

        /// <summary>
        /// Resolve a CHANGE-type plist index to its dereferenced target
        /// ROM offset. Mirrors <c>MapPointerForm.PlistToOffsetAddrFast(CHANGE, plist)</c>.
        /// </summary>
        /// <param name="rom">The ROM to query.</param>
        /// <param name="type">Currently only <see cref="PlistType.CHANGE"/> is supported.</param>
        /// <param name="plist">PLIST index (typically 0..255).</param>
        /// <param name="outPointer">On return, the ROM offset of the plist entry
        /// (the slot inside the table that holds the pointer). Set to
        /// <see cref="U.NOT_FOUND"/> when resolution fails.</param>
        /// <returns>The dereferenced ROM offset, or <see cref="U.NOT_FOUND"/>
        /// when the plist is out of range, the pointer table is missing, or
        /// the dereferenced value is not a valid GBA pointer.</returns>
        public static uint PlistToOffsetAddr(ROM rom, PlistType type, uint plist, out uint outPointer)
        {
            outPointer = U.NOT_FOUND;
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            // Bound the plist by the version-specific limit (256 for split
            // tables, vanilla default otherwise). WF Init's callback uses
            // i >= limit ⇒ invalid.
            uint limit = GetPlistLimit(rom);
            if (limit == 0 || plist >= limit) return U.NOT_FOUND;

            uint basePointer = GetPlistBasePointer(rom, type);
            if (basePointer == 0) return U.NOT_FOUND;

            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            // Each PLIST entry is 4 bytes (a pointer).
            uint entryAddr = baseAddr + plist * 4u;
            if (entryAddr + 4u > (uint)rom.Data.Length) return U.NOT_FOUND;

            uint target = rom.p32(entryAddr);
            if (target == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(target, rom)) return U.NOT_FOUND;

            outPointer = entryAddr;
            return target;
        }

        /// <summary>
        /// Return the RomInfo pointer base for the given PLIST type.
        /// 0 when the version-specific pointer is not defined.
        /// </summary>
        internal static uint GetPlistBasePointer(ROM rom, PlistType type)
        {
            if (rom?.RomInfo == null) return 0u;
            return type switch
            {
                PlistType.CHANGE           => rom.RomInfo.map_mapchange_pointer,
                PlistType.CONFIG           => rom.RomInfo.map_config_pointer,
                PlistType.OBJECT           => rom.RomInfo.map_obj_pointer,
                PlistType.PALETTE          => rom.RomInfo.map_pal_pointer,
                PlistType.MAP              => rom.RomInfo.map_map_pointer_pointer,
                PlistType.EVENT            => rom.RomInfo.map_event_pointer,
                PlistType.ANIMATION        => rom.RomInfo.map_tileanime1_pointer,
                PlistType.ANIMATION2       => rom.RomInfo.map_tileanime2_pointer,
                PlistType.WORLDMAP_FE6ONLY => rom.RomInfo.map_worldmapevent_pointer,
                _ => 0u,
            };
        }

        /// <summary>
        /// Resolve the ROM offset of the per-PLIST slot inside the pointer
        /// table for <paramref name="type"/> (i.e. the dword that holds the
        /// table's pointer for entry <paramref name="plist"/>). Unlike
        /// <see cref="PlistToOffsetAddr"/>, this accepts entries whose
        /// dereferenced target is currently zero/null — it only needs the
        /// slot address itself so callers can <c>write_p32</c> a new target
        /// after appending the data to free space.
        ///
        /// <para>Mirrors the slot-resolution path of WF
        /// <c>MapPointerForm.Write_Plsit</c>: the per-version PLIST limit
        /// gates the entry index, the resolved slot must sit within the
        /// pointer table, and PLIST 0 is reserved (returning
        /// <see cref="U.NOT_FOUND"/>) — but the dereferenced target value
        /// itself is NOT inspected.</para>
        ///
        /// <para>Returns <see cref="U.NOT_FOUND"/> when <paramref name="type"/>
        /// has no defined base pointer, <paramref name="plist"/> is 0 or
        /// past the version-specific limit, the base pointer is unsafe, or
        /// the slot would land past the end of the ROM.</para>
        /// </summary>
        public static uint ResolvePlistSlotAddr(ROM rom, PlistType type, uint plist)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            // WF Write_Plsit rejects plist == 0 explicitly (it would
            // overwrite the reserved sentinel slot).
            if (plist == 0u) return U.NOT_FOUND;

            uint limit = GetPlistLimit(rom);
            if (limit == 0 || plist >= limit) return U.NOT_FOUND;

            uint basePointer = GetPlistBasePointer(rom, type);
            if (basePointer == 0) return U.NOT_FOUND;

            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            uint slotAddr = baseAddr + plist * 4u;
            if (slotAddr + 4u > (uint)rom.Data.Length) return U.NOT_FOUND;
            return slotAddr;
        }

        /// <summary>
        /// Append <paramref name="compressed"/> bytes to ROM free space
        /// (via <see cref="ImageImportCore.FindAndWriteData"/>) and update
        /// the PLIST entry at (<paramref name="type"/>, <paramref name="plist"/>)
        /// to point at the new data. Mirrors WF
        /// <c>MapStyleEditorForm.WriteMapConfig</c> +
        /// <c>MapPointerForm.Write_Plsit(CONFIG, ...)</c>.
        ///
        /// <para>All writes are recorded into the ambient undo scope opened
        /// by <see cref="ROM.BeginUndoScope"/> (callers must open one before
        /// invoking this helper). On any failure the partial writes are
        /// captured by the ambient scope, so the caller's rollback path
        /// restores ROM state to the pre-call snapshot.</para>
        ///
        /// <para>Returns the new ROM offset of the appended data on success,
        /// or <see cref="U.NOT_FOUND"/> on any failure (invalid arguments,
        /// out-of-range PLIST, free-space exhaustion). <paramref name="error"/>
        /// carries a short human-readable reason for the failure.</para>
        /// </summary>
        public static uint WritePlistData(ROM rom, PlistType type, uint plist, byte[] compressed, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null) { error = "no ROM"; return U.NOT_FOUND; }
            if (compressed == null || compressed.Length == 0) { error = "empty data"; return U.NOT_FOUND; }

            uint slotAddr = ResolvePlistSlotAddr(rom, type, plist);
            if (slotAddr == U.NOT_FOUND) { error = $"invalid PLIST slot ({type}, {plist})"; return U.NOT_FOUND; }

            uint newAddr = ImageImportCore.FindAndWriteData(rom, compressed);
            if (newAddr == U.NOT_FOUND) { error = "free space exhausted"; return U.NOT_FOUND; }

            // write_p32 records the 4-byte slot write into ambient undo.
            rom.write_p32(slotAddr, newAddr);
            return newAddr;
        }

        /// <summary>
        /// Port of <c>MapSettingForm.GetMapChangeAddrWhereMapID</c>.
        /// Looks up the map setting at <paramref name="mapId"/>, reads the
        /// per-map mapchange PLIST byte at offset 11, then resolves that
        /// PLIST via <see cref="PlistToOffsetAddr"/>.
        /// </summary>
        /// <returns>The ROM offset of the change-data block, or
        /// <see cref="U.NOT_FOUND"/> when any step fails (invalid map ID,
        /// plist 0/0xFF, table missing, deref invalid).</returns>
        public static uint GetMapChangeAddrWhereMapID(ROM rom, uint mapId, out uint outPointer)
        {
            outPointer = U.NOT_FOUND;
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            uint mapAddr = MapSettingCore.GetMapAddr(rom, mapId);
            if (!U.isSafetyOffset(mapAddr, rom)) return U.NOT_FOUND;

            // Bound-check the full map-setting record against ROM
            // length BEFORE calling IsMapSettingValid — that helper
            // reads up to offset 0x8E (for FE7U layout) so the
            // previous `+12` check was insufficient for large mapIds
            // that produced an in-bounds first-byte but truncated
            // tail. Use the per-version `map_setting_datasize`
            // (Copilot bot 3rd-pass review on issue #423).
            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) return U.NOT_FOUND;
            if (mapAddr + dataSize > (uint)rom.Data.Length) return U.NOT_FOUND;

            // Bound-check mapId against the populated map count. WF's
            // `InputFormRef.IDToAddr` returns U.NOT_FOUND when
            // `id >= DataCount`; mirror that here so an out-of-range
            // mapId does not accidentally resolve to an unrelated
            // in-ROM address that happens to satisfy `isSafetyOffset`
            // (Copilot bot review on issue #423).
            if (!MapSettingCore.IsMapSettingValid(rom, mapAddr)) return U.NOT_FOUND;

            uint plist = rom.u8(mapAddr + 11);
            // WF treats 0/0xFF as "no change data". 0xFF is the
            // explicit "no PLIST" marker in vanilla map records. 0 is
            // reserved by the WF semantics — `MapPointerForm.GetPListNameSplited`
            // returns "NULL" for plist 0, and the per-map PLIST byte
            // is documented as 1-based (entry 0 in the table is
            // unused). Hard-block both indexes here so a modified ROM
            // that plants a non-null pointer at entry 0 does not
            // accidentally resolve through it (Copilot bot review on
            // issue #423).
            if (plist == 0u || plist == 0xFFu) return U.NOT_FOUND;

            uint target = PlistToOffsetAddr(rom, PlistType.CHANGE, plist, out outPointer);
            return target;
        }

        /// <summary>
        /// Convenience overload that discards the output pointer.
        /// </summary>
        public static uint GetMapChangeAddrWhereMapID(ROM rom, uint mapId)
            => GetMapChangeAddrWhereMapID(rom, mapId, out _);

        // ============================================================
        // #1192 — per-chapter map-change flag scan.
        //
        // InputFormRef-free port of WinForms MapChangeForm.MakeFlagIDArray
        // (which used N_Init's InputFormRef: a 12-byte block whose record
        // count walks until u8(addr)==0xFF, with the flag id as the u16 at
        // record offset +5). The WinForms path delegated to
        // UseFlagID.AppendFlagIDFixedMapID — "FixedMapID" because every
        // change record of the selected chapter belongs to THAT chapter, so
        // we attribute the selected mapid directly (matching the tool's
        // intent of listing the chapter's own map-change flags).
        // ============================================================

        /// <summary>Block stride of one map-change record (WF N_Init blocksize).</summary>
        const uint MapChangeRecordSize = 12;
        /// <summary>u16 flag id offset inside a map-change record (WF flagIDPlus).</summary>
        const uint MapChangeFlagOffset = 5;
        /// <summary>Defensive bound so a corrupt 0xFF-less table can never loop forever.</summary>
        const int MapChangeMaxRecords = 4096;

        /// <summary>
        /// Append every map-change flag used by <paramref name="mapId"/> to
        /// <paramref name="list"/> (lint category <see cref="FELintCore.Type.MAPCHANGE"/>).
        /// Strictly READ-ONLY: every read is bounds-guarded, malformed data
        /// terminates the walk and yields a (possibly empty) partial list
        /// instead of throwing. Mirrors WF MapChangeForm.MakeFlagIDArray.
        /// </summary>
        public static void MakeFlagIDArray(ROM rom, uint mapId, List<UseFlagIDCore> list)
        {
            if (rom?.RomInfo == null || list == null) return;

            uint changeAddr = GetMapChangeAddrWhereMapID(rom, mapId);
            if (changeAddr == U.NOT_FOUND || !U.isSafetyOffset(changeAddr, rom)) return;

            uint addr = changeAddr;
            for (int i = 0; i < MapChangeMaxRecords; i++, addr += MapChangeRecordSize)
            {
                // Bounds-check the full record (incl. the +5 flag u16) before
                // any read so a near-EOF table can't throw.
                if (!U.isSafetyOffset(addr, rom)) break;
                if (addr + MapChangeRecordSize > (uint)rom.Data.Length) break;

                // WF N_Init DataCount predicate: a record whose first byte is
                // 0xFF terminates the change table.
                if (rom.u8(addr) == 0xFF) break;

                uint flag = rom.u16(addr + MapChangeFlagOffset);
                UseFlagIDCore.AppendUseFlagID(
                    list, FELintCore.Type.MAPCHANGE, addr, U.ToHexString((uint)i), flag, mapId, (uint)i);
            }
        }
    }
}
