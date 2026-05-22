// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform terrain-lookup-table pointer enumeration helpers.
//
// Extracted from FEBuilderGBA/MapTerrainBGLookupTableForm.cs and
// MapTerrainFloorLookupTableForm.cs so both the legacy WinForms editors and
// the new Avalonia views (#442 / #441) call into the same Core surface — no
// duplication, no AV-side fork of patch detection or table walking.
//
// All methods are pure functions of a passed-in <see cref="ROM"/> instance;
// there is no static cache here (PatchDetection holds the cache for the
// extends-patch detection result).
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Helpers for enumerating the BG / Floor lookup-table pointers shared
    /// between WinForms <c>MapTerrain{BG,Floor}LookupTableForm</c> and the
    /// Avalonia counterparts. Used by gap-sweep parity work (#442, #441).
    /// </summary>
    public static class MapTerrainLookupCore
    {
        /// <summary>
        /// Returns the FE8 ROM address that the "Extends Battle BG" patch
        /// uses as a pointer-of-pointer to its extended struct table. 0
        /// when this version is not FE8J/FE8U (the patch only exists for
        /// FE8). Mirrors WinForms <c>MapTerrainBGLookupTableForm.GetExtendsPointer</c>.
        /// </summary>
        public static uint GetExtendsPointer(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            if (rom.RomInfo.version == 8)
            {
                if (rom.RomInfo.is_multibyte)
                {
                    // FE8J
                    return 0x58D34;
                }
                else
                {
                    // FE8U
                    return 0x57EE8;
                }
            }
            return 0;
        }

        /// <summary>
        /// Vanilla 21-entry pointer list for either BG (<paramref name="isFloor"/>=false)
        /// or Floor (<paramref name="isFloor"/>=true) lookup tables. Pulls
        /// directly from <see cref="ROMFEINFO"/>'s named slots.
        /// </summary>
        public static uint[] GetPointersVanilla(ROM rom, bool isFloor)
        {
            if (rom?.RomInfo == null) return System.Array.Empty<uint>();
            var info = rom.RomInfo;
            if (isFloor)
            {
                return new uint[]
                {
                    info.lookup_table_battle_terrain_00_pointer,
                    info.lookup_table_battle_terrain_01_pointer,
                    info.lookup_table_battle_terrain_02_pointer,
                    info.lookup_table_battle_terrain_03_pointer,
                    info.lookup_table_battle_terrain_04_pointer,
                    info.lookup_table_battle_terrain_05_pointer,
                    info.lookup_table_battle_terrain_06_pointer,
                    info.lookup_table_battle_terrain_07_pointer,
                    info.lookup_table_battle_terrain_08_pointer,
                    info.lookup_table_battle_terrain_09_pointer,
                    info.lookup_table_battle_terrain_10_pointer,
                    info.lookup_table_battle_terrain_11_pointer,
                    info.lookup_table_battle_terrain_12_pointer,
                    info.lookup_table_battle_terrain_13_pointer,
                    info.lookup_table_battle_terrain_14_pointer,
                    info.lookup_table_battle_terrain_15_pointer,
                    info.lookup_table_battle_terrain_16_pointer,
                    info.lookup_table_battle_terrain_17_pointer,
                    info.lookup_table_battle_terrain_18_pointer,
                    info.lookup_table_battle_terrain_19_pointer,
                    info.lookup_table_battle_terrain_20_pointer,
                };
            }
            return new uint[]
            {
                info.lookup_table_battle_bg_00_pointer,
                info.lookup_table_battle_bg_01_pointer,
                info.lookup_table_battle_bg_02_pointer,
                info.lookup_table_battle_bg_03_pointer,
                info.lookup_table_battle_bg_04_pointer,
                info.lookup_table_battle_bg_05_pointer,
                info.lookup_table_battle_bg_06_pointer,
                info.lookup_table_battle_bg_07_pointer,
                info.lookup_table_battle_bg_08_pointer,
                info.lookup_table_battle_bg_09_pointer,
                info.lookup_table_battle_bg_10_pointer,
                info.lookup_table_battle_bg_11_pointer,
                info.lookup_table_battle_bg_12_pointer,
                info.lookup_table_battle_bg_13_pointer,
                info.lookup_table_battle_bg_14_pointer,
                info.lookup_table_battle_bg_15_pointer,
                info.lookup_table_battle_bg_16_pointer,
                info.lookup_table_battle_bg_17_pointer,
                info.lookup_table_battle_bg_18_pointer,
                info.lookup_table_battle_bg_19_pointer,
                info.lookup_table_battle_bg_20_pointer,
            };
        }

        /// <summary>
        /// Pointer list as seen when the "Extends Battle BG" patch is
        /// installed (or vanilla when it isn't). <paramref name="plus"/>=0
        /// returns floor pointers, <paramref name="plus"/>=4 returns BG
        /// pointers (each extended row is an 8-byte struct: 4 bytes floor
        /// pointer + 4 bytes BG pointer).
        ///
        /// Mirrors WinForms <c>MapTerrainBGLookupTableForm.GetPointersExtendsPatch</c>
        /// — same termination sentinel (0xFFFFFFFF), same `0x4f` soft cap,
        /// same fallback-to-vanilla on missing pointer.
        /// </summary>
        public static uint[] GetPointersExtendsPatch(ROM rom, uint plus)
        {
            // Falling back to vanilla when:
            //   - no ExtendsPointer for this ROM version (non-FE8)
            //   - the pointer-of-pointer dereferences to an unsafe offset
            //   - the patch isn't actually installed (PatchDetection says NO)
            // Same behaviour as WinForms, so the WinForms editors that
            // already delegated to this code keep producing identical output.
            bool isFloor = (plus == 0);

            uint pointer = GetExtendsPointer(rom);
            if (pointer == 0)
            {
                return GetPointersVanilla(rom, isFloor);
            }
            if (PatchDetection.SearchExtendsBattleBG(rom) != PatchDetection.ExtendsBattleBG_extends.Extends)
            {
                return GetPointersVanilla(rom, isFloor);
            }
            uint addr = rom.p32(pointer);
            if (!U.isSafetyOffset(addr, rom))
            {
                return GetPointersVanilla(rom, isFloor);
            }

            var pointers = new List<uint>(0x4f);
            for (int i = 0; i < 0xff; i++, addr += 8)
            {
                uint p = rom.u32(addr + plus);
                if (p == 0xffffffff)
                {
                    break;
                }
                pointers.Add(addr + plus);
            }
            return pointers.ToArray();
        }

        /// <summary>
        /// Aggregate pointer enumeration: when the extends patch is
        /// installed AND the version supports it, returns the extended
        /// table; otherwise the vanilla 21-entry list. The Avalonia view
        /// models use this single entry point so they don't need to
        /// reproduce the patch-detection condition.
        /// </summary>
        public static uint[] GetPointers(ROM rom, bool isFloor)
        {
            if (PatchDetection.SearchExtendsBattleBG(rom) == PatchDetection.ExtendsBattleBG_extends.Extends)
            {
                return GetPointersExtendsPatch(rom, isFloor ? 0u : 4u);
            }
            return GetPointersVanilla(rom, isFloor);
        }

        /// <summary>
        /// Load the localized "battle-terrain set" name dictionary from the
        /// shared config (e.g. "Plain", "Forest", "Mountain"). When the
        /// extends-battle-bg patch is installed, the per-entry comment-cache
        /// names (if any) are merged on top of the base config so extended
        /// rows show up with the user's preferred name. Falls back to
        /// "Extends XX" for unnamed extended rows.
        ///
        /// Mirrors WinForms <c>MapTerrainBGLookupTableForm.MakeCache_Cache_TerrainSetDicLow</c>.
        /// </summary>
        public static Dictionary<uint, string> GetTerrainSetDic(ROM rom)
        {
            // Always start from the base "battleterrain_set_*.txt" file.
            string filename = U.ConfigDataFilename("battleterrain_set_", rom);
            Dictionary<uint, string> data = U.LoadDicResource(filename);

            if (rom?.RomInfo == null)
                return data;

            // When the extends patch is not installed, the 21-entry base
            // file is the authoritative list — nothing more to merge.
            if (PatchDetection.SearchExtendsBattleBG(rom) != PatchDetection.ExtendsBattleBG_extends.Extends)
                return data;

            uint pointer = GetExtendsPointer(rom);
            if (pointer == 0)
                return data;

            uint addr = rom.p32(pointer);
            if (!U.isSafetyOffset(addr, rom))
                return data;

            int baseSize = data.Count;
            for (int i = 0; i < 0xff; i++, addr += 8)
            {
                uint p = rom.u32(addr);
                if (p == 0xffffffff)
                {
                    break;
                }

                // The comment cache holds user-edited labels per ROM address.
                // CoreState.CommentCache may be null in some test contexts; the
                // fallback path keeps the canonical name in that case.
                string name = CoreState.CommentCache?.At(addr) ?? "";
                if (i < baseSize)
                {
                    if (name != "")
                    {
                        data[(uint)i] = name;
                    }
                    continue;
                }

                if (name == "")
                {
                    name = "Extends" + U.ToHexString2(i);
                }
                data[(uint)i] = name;
            }

            return data;
        }

        /// <summary>
        /// Filter-index lookup mirroring WinForms
        /// <c>MapTerrain*LookupTableForm.GetFilterIndexOfAddr</c>. Given the
        /// list head pointer of one entry, returns the matching filter
        /// index (0..n-1) or 0 if not found.
        /// </summary>
        public static uint ResolveFilterIndexForAddress(ROM rom, uint addr, bool isFloor)
        {
            if (rom == null) return 0;
            addr = U.toOffset(addr);
            uint[] pointers = GetPointers(rom, isFloor);
            for (int i = 0; i < pointers.Length; i++)
            {
                uint p = pointers[i];
                if (p == 0) continue;
                uint a = rom.p32(p);
                if (a == addr)
                {
                    return (uint)i;
                }
            }
            return 0;
        }
    }
}
