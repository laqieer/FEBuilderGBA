// SPDX-License-Identifier: GPL-3.0-or-later
using System;

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
        /// Only the values needed by this helper are surfaced; the
        /// WinForms enum has additional types (CONFIG, ANIMATION, etc.)
        /// that are not yet exposed to Core.
        /// </summary>
        public enum PlistType
        {
            CHANGE,
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

            uint basePointer = type switch
            {
                PlistType.CHANGE => rom.RomInfo.map_mapchange_pointer,
                _ => 0u,
            };
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
    }
}
