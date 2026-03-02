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
        /// Enumerate all maps from the ROM's map setting pointer table.
        /// Returns list of (address, display-name) pairs.
        /// </summary>
        public static List<AddrResult> MakeMapIDList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null)
                return new List<AddrResult>();

            uint basePointer = rom.RomInfo.map_setting_pointer;
            uint dataSize = rom.RomInfo.map_setting_datasize;

            if (basePointer == 0 || dataSize == 0)
                return new List<AddrResult>();

            // Read base address from the pointer
            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr))
                return new List<AddrResult>();

            var result = new List<AddrResult>();

            for (int i = 0; ; i++)
            {
                uint addr = (uint)(baseAddr + (i * dataSize));
                if (!U.isSafetyOffset(addr) || addr + dataSize > (uint)rom.Data.Length)
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
        /// Get the number of valid maps.
        /// </summary>
        public static int GetMapCount()
        {
            return MakeMapIDList().Count;
        }

        /// <summary>
        /// Get the ROM address for a specific map ID.
        /// </summary>
        public static uint GetMapAddr(uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;

            uint baseAddr = rom.p32(rom.RomInfo.map_setting_pointer);
            if (!U.isSafetyOffset(baseAddr)) return U.NOT_FOUND;

            uint addr = (uint)(baseAddr + (mapId * rom.RomInfo.map_setting_datasize));
            if (!U.isSafetyOffset(addr)) return U.NOT_FOUND;

            return addr;
        }

        /// <summary>
        /// Check if the map setting at addr is valid (not past the end of map data).
        /// Returns true if the map entry is valid, false if it marks end-of-data.
        /// Logic extracted from MapSettingForm.IsMapSettingEnd (inverted: that returned true for "keep going").
        /// </summary>
        static bool IsMapSettingValid(ROM rom, uint addr)
        {
            // First u32 being a pointer indicates end-of-data
            uint a = rom.u32(addr + 0);
            if (U.isPointer(a))
                return false;

            // Weather check
            uint weather = rom.u8(addr + 12);
            if (weather >= 0xE)
                return true; // Original returns false=stop, but this means "valid" in original code

            // PLIST validation
            uint plist1 = rom.u32(addr + 4);
            if (plist1 == 0 || plist1 == 0xFFFFFFFF)
            {
                uint plist2 = rom.u32(addr + 8);
                if (plist2 == 0 || plist2 == 0xFFFFFFFF)
                    return true; // Invalid PLIST but original code continues
            }

            // For FE8-style ROMs with larger data size, do text ID bounds check
            if (rom.RomInfo.map_setting_datasize >= 148)
            {
                // These offsets are FE8-specific
                uint textmax = GetTextDataCount(rom);
                if (textmax > 0)
                {
                    uint map1 = rom.u16(addr + 0x70);
                    if (map1 >= textmax) return true;

                    uint map2 = rom.u16(addr + 0x72);
                    if (map2 >= textmax) return true;

                    uint clearcond1 = rom.u16(addr + 0x88);
                    if (clearcond1 >= textmax) return true;

                    uint clearcond2 = rom.u16(addr + 0x8A);
                    if (clearcond2 >= textmax) return true;
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
            if (!U.isSafetyOffset(textBase)) return 0;

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
            string mapCp = "";
            uint chaptere = rom.u8(addr + 128);
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
