// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform port of the WinForms map-PLIST label resolver
    /// (<c>MapPointerForm.GetPListNameSplited</c> /
    /// <c>GetPListNameNotSplite</c> / <c>ConvertBaseAddrToType</c>) plus the
    /// <c>MapSettingForm.GetMapPListsWhereAddr</c> + <c>PLists</c> struct it
    /// depends on (#952, T5 slice A).
    ///
    /// <para>Given a PLIST index and the pointer-table base it belongs to,
    /// the resolver answers WHICH MAP uses that PLIST for WHICH purpose,
    /// returning a label such as <c>"MAP Ch1"</c>, <c>"MAPCHANGE Ch5"</c>,
    /// <c>"ANIME1 Prologue"</c>, <c>"OBJ ..."</c>, <c>"NULL"</c>,
    /// <c>"-EMPTY-"</c> (split layout) or <c>"UNK"</c> (non-split layout).</para>
    ///
    /// <para>Reuses the existing Core seams: <see cref="MapChangeCore"/> for
    /// the <c>PlistType</c> enum + base-pointer resolution + split/limit
    /// helpers, and <see cref="MapSettingCore"/> for map enumeration + the
    /// chapter-prefixed map-name formatter. The WF resolver is faithfully
    /// reproduced — including the FE6 <c>WMEVENT</c> quirk where the
    /// world-map branch tests <c>wmapevent_plist == 0</c> (the early
    /// <c>plist == 0 → "NULL"</c> return guarantees <c>plist != 0</c> here,
    /// so this branch only fires when a map's world-map event PLIST byte is
    /// literally 0 — preserved verbatim).</para>
    ///
    /// All methods are ROM-explicit; nothing reads <see cref="CoreState.ROM"/>.
    /// </summary>
    public static class MapPListResolverCore
    {
        /// <summary>
        /// Per-map PLIST field bundle. Mirrors WinForms
        /// <c>MapSettingForm.PLists</c> field-for-field (plus the FE6-only
        /// world-map event PLIST, which WF reads via a separate routine —
        /// surfaced here so the resolver's FE6 branch stays self-contained).
        /// </summary>
        public sealed class PLists
        {
            public uint obj_plist;       // u16 @ +4 (low byte + high byte are TWO plist ids)
            public uint palette_plist;   // u8  @ +6
            public uint config_plist;    // u8  @ +7
            public uint mappointer_plist;// u8  @ +8
            public uint anime1_plist;    // u8  @ +9
            public uint anime2_plist;    // u8  @ +10
            public uint mapchange_plist; // u8  @ +11
            public uint event_plist;     // u8  @ map_setting_event_plist_pos
            public uint palette2_plist;  // u8  @ +146 or +45 (patch-dependent), 0 when unpatched
            public uint worldmapevent_plist; // u8 @ map_setting_worldmap_plist_pos (FE6 only)
        }

        /// <summary>
        /// Read every PLIST field for the map setting at
        /// <paramref name="mapSettingAddr"/>. Mirrors WinForms
        /// <c>MapSettingForm.GetMapPListsWhereAddr</c> (with the FE6-only
        /// world-map event PLIST folded in). Fields come from their REAL
        /// per-version sources — NOT a flat <c>+4..+11</c> run:
        /// <list type="bullet">
        /// <item><c>event_plist</c> from <c>RomInfo.map_setting_event_plist_pos</c></item>
        /// <item><c>worldmapevent_plist</c> from <c>RomInfo.map_setting_worldmap_plist_pos</c> (FE6)</item>
        /// <item><c>palette2_plist</c> offset (146 vs 45) from the ported
        /// <see cref="PatchDetection.SearchFlag0x28ToMapSecondPalettePatch(ROM)"/></item>
        /// </list>
        /// Returns an all-zero struct when the address is unsafe (matches WF).
        /// </summary>
        public static PLists GetMapPListsWhereAddr(ROM rom, uint mapSettingAddr)
        {
            var plists = new PLists();
            if (rom == null || rom.RomInfo == null) return plists;
            if (!U.isSafetyOffset(mapSettingAddr, rom)) return plists;

            plists.obj_plist        = (uint)rom.u16(mapSettingAddr + 4);
            plists.palette_plist    = (uint)rom.u8(mapSettingAddr + 6);
            plists.config_plist     = (uint)rom.u8(mapSettingAddr + 7);
            plists.mappointer_plist = (uint)rom.u8(mapSettingAddr + 8);
            plists.anime1_plist     = (uint)rom.u8(mapSettingAddr + 9);
            plists.anime2_plist     = (uint)rom.u8(mapSettingAddr + 10);
            plists.mapchange_plist  = (uint)rom.u8(mapSettingAddr + 11);

            plists.event_plist = (uint)rom.u8(mapSettingAddr + rom.RomInfo.map_setting_event_plist_pos);

            // FE6-only world-map event PLIST. WF reads this via a separate
            // routine (GetWorldMapEventIDWhereAddr); fold it in here only for
            // FE6 so the resolver's WMEVENT branch is self-contained.
            if (rom.RomInfo.version == 6)
            {
                plists.worldmapevent_plist =
                    (uint)rom.u8(mapSettingAddr + rom.RomInfo.map_setting_worldmap_plist_pos);
            }

            // Second palette PLIST byte — offset depends on the installed patch.
            PatchDetection.MapSecondPalette_extends secondPalette =
                PatchDetection.SearchFlag0x28ToMapSecondPalettePatch(rom);
            if (secondPalette == PatchDetection.MapSecondPalette_extends.Flag0x28_146)
            {
                plists.palette2_plist = (uint)rom.u8(mapSettingAddr + 146);
            }
            else if (secondPalette == PatchDetection.MapSecondPalette_extends.Flag0x28_45)
            {
                plists.palette2_plist = (uint)rom.u8(mapSettingAddr + 45);
            }

            return plists;
        }

        /// <summary>
        /// FE6-only: read the world-map event PLIST byte for a map setting.
        /// Mirrors WinForms <c>MapSettingForm.GetWorldMapEventIDWhereAddr</c>.
        /// </summary>
        public static uint GetWorldMapEventIDWhereAddr(ROM rom, uint mapSettingAddr)
        {
            if (rom == null || rom.RomInfo == null) return 0u;
            if (!U.isSafetyOffset(mapSettingAddr, rom)) return 0u;
            return (uint)rom.u8(mapSettingAddr + rom.RomInfo.map_setting_worldmap_plist_pos);
        }

        /// <summary>
        /// Map a pointer-table BASE ADDRESS (the dereferenced value of one of
        /// the <c>map_*_pointer</c> locations) back to its
        /// <see cref="MapChangeCore.PlistType"/>. Mirrors WinForms
        /// <c>MapPointerForm.ConvertBaseAddrToType</c> — including the FE6-only
        /// WORLDMAP table. Returns <c>null</c> (the WF <c>UNKNOWN</c>) when no
        /// table base matches.
        /// </summary>
        public static MapChangeCore.PlistType? ConvertBaseAddrToType(ROM rom, uint baseaddr)
        {
            if (rom == null || rom.RomInfo == null) return null;

            // WF order: CONFIG, ANIMATION, ANIMATION2, OBJECT, PALETTE,
            // MAP, CHANGE, EVENT, (FE6) WORLDMAP. The first match wins; in a
            // vanilla (non-split) ROM many of these share the same base, so
            // order matters — keep WF's exact precedence.
            if (rom.p32(rom.RomInfo.map_config_pointer) == baseaddr)
                return MapChangeCore.PlistType.CONFIG;
            if (rom.p32(rom.RomInfo.map_tileanime1_pointer) == baseaddr)
                return MapChangeCore.PlistType.ANIMATION;
            if (rom.p32(rom.RomInfo.map_tileanime2_pointer) == baseaddr)
                return MapChangeCore.PlistType.ANIMATION2;
            if (rom.p32(rom.RomInfo.map_obj_pointer) == baseaddr)
                return MapChangeCore.PlistType.OBJECT;
            if (rom.p32(rom.RomInfo.map_pal_pointer) == baseaddr)
                return MapChangeCore.PlistType.PALETTE;
            if (rom.p32(rom.RomInfo.map_map_pointer_pointer) == baseaddr)
                return MapChangeCore.PlistType.MAP;
            if (rom.p32(rom.RomInfo.map_mapchange_pointer) == baseaddr)
                return MapChangeCore.PlistType.CHANGE;
            if (rom.p32(rom.RomInfo.map_event_pointer) == baseaddr)
                return MapChangeCore.PlistType.EVENT;
            if (rom.RomInfo.version == 6
                && rom.p32(rom.RomInfo.map_worldmapevent_pointer) == baseaddr)
                return MapChangeCore.PlistType.WORLDMAP_FE6ONLY;

            return null; // UNKNOWN
        }

        /// <summary>
        /// Per-call local cache of (map setting address) → (PLists, map name).
        /// Built fresh for each list build so a 256-entry list scan reads each
        /// map's settings once instead of once-per-PLIST-row. NOT static — no
        /// global/cross-call state (matches the task's perf requirement).
        /// </summary>
        public sealed class ResolveCache
        {
            readonly ROM _rom;
            readonly IReadOnlyList<AddrResult> _maps;
            readonly Dictionary<uint, PLists> _plists = new Dictionary<uint, PLists>();
            readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();

            public ResolveCache(ROM rom, IReadOnlyList<AddrResult> maps)
            {
                _rom = rom;
                _maps = maps ?? Array.Empty<AddrResult>();
            }

            public IReadOnlyList<AddrResult> Maps => _maps;

            public PLists PListsAt(uint mapAddr)
            {
                if (!_plists.TryGetValue(mapAddr, out PLists p))
                {
                    p = GetMapPListsWhereAddr(_rom, mapAddr);
                    _plists[mapAddr] = p;
                }
                return p;
            }

            public string NameAt(uint mapAddr)
            {
                if (!_names.TryGetValue(mapAddr, out string n))
                {
                    n = MapSettingCore.GetMapNameWhereAddr(_rom, mapAddr);
                    _names[mapAddr] = n;
                }
                return n;
            }
        }

        /// <summary>
        /// Build a fresh per-call resolve cache over every map in
        /// <paramref name="rom"/>. Callers hold this for the duration of a
        /// single list build and pass it to
        /// <see cref="GetPListNameSplited"/> / <see cref="GetPListNameNotSplite"/>.
        /// </summary>
        public static ResolveCache BuildCache(ROM rom)
        {
            List<AddrResult> maps = MapSettingCore.MakeMapIDList(rom);
            return new ResolveCache(rom, maps);
        }

        /// <summary>
        /// Split-layout label resolver — literal port of WinForms
        /// <c>MapPointerForm.GetPListNameSplited</c>. Each PLIST table is its
        /// own block, so the <paramref name="baseaddr"/> (the table base the
        /// row belongs to) disambiguates which purpose a PLIST id serves.
        ///
        /// <list type="bullet">
        /// <item><c>plist == 0 → "NULL"</c> (reserved sentinel).</item>
        /// <item>ANIME1 and ANIME2 both match only when the base type is
        /// <c>ANIMATION</c> (they share the same table).</item>
        /// <item>PAL and PAL2 both match only when the base type is
        /// <c>OBJECT</c> (OBJECT and PALETTE share the same table).</item>
        /// <item>OBJ is a PACKED u16 — the low byte and the high byte are two
        /// separate PLIST ids; either matching (under OBJECT) yields "OBJ".</item>
        /// <item>FE6 WMEVENT: <c>worldmapevent_plist == 0</c> under
        /// <c>WORLDMAP_FE6ONLY</c> (the WF quirk — preserved verbatim).</item>
        /// <item>No match → "-EMPTY-".</item>
        /// </list>
        /// </summary>
        public static string GetPListNameSplited(ROM rom, uint plist, uint baseaddr, ResolveCache cache)
        {
            if (plist == 0) return "NULL";
            if (rom == null || rom.RomInfo == null || cache == null) return "-EMPTY-";

            MapChangeCore.PlistType? type = ConvertBaseAddrToType(rom, baseaddr);

            foreach (AddrResult map in cache.Maps)
            {
                uint addr = map.addr;
                PLists plists = cache.PListsAt(addr);

                if (plists.anime1_plist == plist && type == MapChangeCore.PlistType.ANIMATION)
                    return "ANIME1 " + cache.NameAt(addr);
                if (plists.anime2_plist == plist && type == MapChangeCore.PlistType.ANIMATION)
                    return "ANIME2 " + cache.NameAt(addr);
                if (plists.config_plist == plist && type == MapChangeCore.PlistType.CONFIG)
                    return "CONFIG " + cache.NameAt(addr);
                if (plists.event_plist == plist && type == MapChangeCore.PlistType.EVENT)
                    return "EVENT " + cache.NameAt(addr);
                if (plists.mapchange_plist == plist && type == MapChangeCore.PlistType.CHANGE)
                    return "MAPCHANGE " + cache.NameAt(addr);
                if (plists.mappointer_plist == plist && type == MapChangeCore.PlistType.MAP)
                    return "MAP " + cache.NameAt(addr);
                if (plists.palette_plist == plist && type == MapChangeCore.PlistType.OBJECT)
                    return "PAL " + cache.NameAt(addr);
                if (plists.palette2_plist == plist && type == MapChangeCore.PlistType.OBJECT)
                    return "PAL2 " + cache.NameAt(addr);

                uint obj_plist_low = (plists.obj_plist & 0xFF);
                uint obj_plist_high = ((plists.obj_plist >> 8) & 0xFF);
                if (obj_plist_low == plist && type == MapChangeCore.PlistType.OBJECT)
                    return "OBJ " + cache.NameAt(addr);
                if (obj_plist_high == plist && type == MapChangeCore.PlistType.OBJECT)
                    return "OBJ " + cache.NameAt(addr);

                if (rom.RomInfo.version == 6)
                {
                    uint wmapevent_plist = plists.worldmapevent_plist;
                    if (wmapevent_plist == 0 && type == MapChangeCore.PlistType.WORLDMAP_FE6ONLY)
                        return "WMEVENT " + cache.NameAt(addr);
                }
            }
            return "-EMPTY-";
        }

        /// <summary>
        /// Non-split-layout label resolver — literal port of WinForms
        /// <c>MapPointerForm.GetPListNameNotSplite</c>. Every PLIST table
        /// shares one base, so there's no base-type disambiguation: every
        /// PLIST field is scanned and the FIRST field whose value equals
        /// <paramref name="plist"/> names the row. Distinct semantics from
        /// the split path: returns <c>"UNK"</c> (not <c>"-EMPTY-"</c>) when
        /// nothing matches, and <c>"NULL"</c> for plist 0.
        /// </summary>
        public static string GetPListNameNotSplite(ROM rom, uint plist, ResolveCache cache)
        {
            if (plist == 0) return "NULL";
            if (rom == null || rom.RomInfo == null || cache == null) return "UNK";

            foreach (AddrResult map in cache.Maps)
            {
                uint addr = map.addr;
                PLists plists = cache.PListsAt(addr);

                if (plists.anime1_plist == plist)
                    return "ANIME1 " + cache.NameAt(addr);
                if (plists.anime2_plist == plist)
                    return "ANIME2 " + cache.NameAt(addr);
                if (plists.config_plist == plist)
                    return "CONFIG " + cache.NameAt(addr);
                if (plists.event_plist == plist)
                    return "EVENT " + cache.NameAt(addr);
                if (plists.mapchange_plist == plist)
                    return "MAPCHANGE " + cache.NameAt(addr);
                if (plists.mappointer_plist == plist)
                    return "MAP " + cache.NameAt(addr);
                if (plists.palette_plist == plist)
                    return "PAL " + cache.NameAt(addr);
                if (plists.palette2_plist == plist)
                    return "PAL2 " + cache.NameAt(addr);

                uint obj_plist_low = (plists.obj_plist & 0xFF);
                uint obj_plist_high = ((plists.obj_plist >> 8) & 0xFF);
                if (obj_plist_low == plist)
                    return "OBJ " + cache.NameAt(addr);
                if (obj_plist_high == plist)
                    return "OBJ " + cache.NameAt(addr);

                if (rom.RomInfo.version == 6)
                {
                    uint wmapevent_plist = plists.worldmapevent_plist;
                    if (wmapevent_plist == 0)
                        return "WMEVENT " + cache.NameAt(addr);
                }
            }
            return "UNK";
        }

        /// <summary>
        /// Convenience resolver for one PLIST id given the base type it
        /// belongs to. Picks split vs non-split by
        /// <see cref="MapChangeCore.IsPlistSplit(ROM)"/> and resolves the
        /// base pointer for <paramref name="type"/> via the shared
        /// <see cref="MapChangeCore.GetPlistBasePointer"/> seam. Builds a
        /// throwaway single-row cache when <paramref name="cache"/> is null.
        /// </summary>
        public static string ResolveLabel(ROM rom, MapChangeCore.PlistType type, uint plist, ResolveCache cache = null)
        {
            if (rom == null || rom.RomInfo == null) return "-EMPTY-";
            cache ??= BuildCache(rom);

            if (!MapChangeCore.IsPlistSplit(rom))
            {
                return GetPListNameNotSplite(rom, plist, cache);
            }

            uint basePointer = MapChangeCore.GetPlistBasePointer(rom, type);
            uint baseaddr = basePointer != 0 ? rom.p32(basePointer) : 0u;
            return GetPListNameSplited(rom, plist, baseaddr, cache);
        }
    }
}
