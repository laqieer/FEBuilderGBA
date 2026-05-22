using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform map enumeration logic extracted from WinForms MapSettingForm.
    /// Reads map settings directly from ROM pointer tables.
    /// </summary>
    public static class MapSettingCore
    {
        /// <summary>
        /// Determines if the FE7 map setting struct uses the FE7U (152-byte) layout.
        /// Used to dispatch between FE7JP and FE7U editors.
        /// </summary>
        public static bool IsFE7ULayout(uint mapSettingDataSize) => mapSettingDataSize >= 152;

        /// <summary>
        /// Enumerate all maps from <c>CoreState.ROM</c>'s map setting pointer table.
        /// Returns list of (address, display-name) pairs.
        /// For ROM-explicit callers (e.g. <see cref="ItemShopCore"/>) use the
        /// <see cref="MakeMapIDList(ROM)"/> overload — that path never reads
        /// <c>CoreState.ROM</c>, so it is safe when operating on a ROM other
        /// than the global one.
        /// </summary>
        public static List<AddrResult> MakeMapIDList() => MakeMapIDList(CoreState.ROM);

        /// <summary>
        /// Enumerate all maps from the given ROM's map setting pointer table.
        /// Does NOT read <c>CoreState.ROM</c>; every lookup uses
        /// <paramref name="rom"/>.
        /// </summary>
        public static List<AddrResult> MakeMapIDList(ROM rom)
        {
            if (rom == null || rom.RomInfo == null)
                return new List<AddrResult>();

            uint basePointer = rom.RomInfo.map_setting_pointer;
            uint dataSize = rom.RomInfo.map_setting_datasize;

            if (basePointer == 0 || dataSize == 0)
                return new List<AddrResult>();

            // Read base address from the pointer
            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom))
                return new List<AddrResult>();

            var result = new List<AddrResult>();

            for (int i = 0; ; i++)
            {
                uint addr = (uint)(baseAddr + (i * dataSize));
                if (!U.isSafetyOffset(addr, rom) || addr + dataSize > (uint)rom.Data.Length)
                    break;

                // Check for end of map data
                if (!IsMapSettingValid(rom, addr))
                    break;

                string name = U.ToHexString((uint)i) + " " + GetMapName(rom, addr);
                result.Add(new AddrResult(addr, name, (uint)i));
            }

            return result;
        }

        /// <summary>
        /// Get the number of valid maps in <c>CoreState.ROM</c>.
        /// </summary>
        public static int GetMapCount()
        {
            return MakeMapIDList().Count;
        }

        /// <summary>
        /// Get the ROM address for a specific map ID in <c>CoreState.ROM</c>.
        /// For ROM-explicit callers use <see cref="GetMapAddr(ROM, uint)"/>.
        /// </summary>
        public static uint GetMapAddr(uint mapId) => GetMapAddr(CoreState.ROM, mapId);

        /// <summary>
        /// Get the ROM address for a specific map ID in the given ROM.
        /// Does NOT read <c>CoreState.ROM</c>.
        /// </summary>
        public static uint GetMapAddr(ROM rom, uint mapId)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            uint baseAddr = rom.p32(rom.RomInfo.map_setting_pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            uint addr = (uint)(baseAddr + (mapId * rom.RomInfo.map_setting_datasize));
            if (!U.isSafetyOffset(addr, rom)) return U.NOT_FOUND;

            return addr;
        }

        /// <summary>
        /// Check if the map setting at addr is valid (not past the end of map data).
        /// Returns true if the map entry is valid, false if it marks end-of-data.
        /// Logic extracted from WinForms MapSettingForm.IsMapSettingEnd.
        /// </summary>
        static bool IsMapSettingValid(ROM rom, uint addr)
        {
            // WinForms treats a pointer in the first dword as a valid map entry.
            uint a = rom.u32(addr + 0);
            if (U.isPointer(a))
                return true;

            // Weather check
            uint weather = rom.u8(addr + 12);
            if (weather >= 0xE)
                return false;

            // PLIST validation
            uint plist1 = rom.u32(addr + 4);
            if (plist1 == 0 || plist1 == 0xFFFFFFFF)
            {
                uint plist2 = rom.u32(addr + 8);
                if (plist2 == 0 || plist2 == 0xFFFFFFFF)
                    return false;
            }

            // For FE7/FE8-style ROMs with larger data size, do text ID bounds check
            if (rom.RomInfo.map_setting_datasize >= 148)
            {
                uint textmax = GetTextDataCount(rom);
                if (textmax > 0)
                {
                    // Map name text IDs are at the same offset for FE7/FE7U/FE8
                    uint map1 = rom.u16(addr + 0x70); // offset 112
                    if (map1 >= textmax) return false;

                    uint map2 = rom.u16(addr + 0x72); // offset 114
                    if (map2 >= textmax) return false;

                    // Clear condition text offsets differ by version:
                    // FE7U (152-byte struct): 0x8C/0x8E (offsets 140/142)
                    // FE7JP/FE8 (148-byte struct): 0x88/0x8A (offsets 136/138)
                    uint clearCondOff1, clearCondOff2;
                    if (rom.RomInfo.map_setting_datasize >= 152)
                    {
                        // FE7U: 4 extra bytes shift clear conditions
                        clearCondOff1 = 0x8C; // 140
                        clearCondOff2 = 0x8E; // 142
                    }
                    else
                    {
                        // FE7JP / FE8
                        clearCondOff1 = 0x88; // 136
                        clearCondOff2 = 0x8A; // 138
                    }

                    uint clearcond1 = rom.u16(addr + clearCondOff1);
                    if (clearcond1 >= textmax) return false;

                    uint clearcond2 = rom.u16(addr + clearCondOff2);
                    if (clearcond2 >= textmax) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the text data count from ROM (simplified - avoids TextForm dependency).
        /// </summary>
        static uint GetTextDataCount(ROM rom)
        {
            if (rom.RomInfo.text_pointer == 0) return 0;
            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            // Use the ROM-pinned safety check so this stays consistent with the
            // public MakeMapIDList(ROM rom) contract — never silently falls back
            // to CoreState.ROM.
            if (!U.isSafetyOffset(textBase, rom)) return 0;

            // Walk the text pointer table to find count
            // Simplified: use a reasonable upper bound
            for (uint i = 0; i < 0x2000; i++)
            {
                uint entryAddr = (uint)(textBase + i * 4);
                if (entryAddr + 4 > (uint)rom.Data.Length) return i;

                uint ptr = rom.u32(entryAddr);
                if (ptr == 0) return i;
                if (!U.isPointer(ptr) && ptr != 0) return i;
            }
            return 0x2000;
        }

        /// <summary>
        /// Get a human-readable name for a map at the given address.
        /// </summary>
        static string GetMapName(ROM rom, uint addr)
        {
            if (rom.RomInfo.version == 6)
            {
                // FE6: name text at offset 56
                uint id = rom.u16(addr + 56);
                return FETextDecode.Direct(id);
            }

            // FE7/FE8: chapter prefix + name text at offset 112
            // Chapter number offset: FE7U (152-byte) at 132, FE7JP/FE8 (148-byte) at 128
            string mapCp = "";
            uint chapterOffset = rom.RomInfo.map_setting_datasize >= 152 ? 132u : 128u;
            uint chaptere = rom.u8(addr + chapterOffset);
            if (chaptere > 0)
            {
                if (U.isEven(chaptere))
                    mapCp = "Ch" + (chaptere / 2).ToString();
                else
                    mapCp = "Ch" + (chaptere / 2).ToString() + "x";
            }

            uint textId = rom.u16(addr + 112);
            string textName = FETextDecode.Direct(textId);
            return (mapCp + " " + textName).Trim();
        }
    }
}
