using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Builds named list items from ROM tables for use in dropdown/combo controls.
    /// Each method returns a list of (id, displayName) tuples.
    /// </summary>
    public static class ComboResourceHelper
    {
        public static List<(uint id, string name)> MakeUnitList()
        {
            var result = new List<(uint, string)>();
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;
            uint count = rom.RomInfo.unit_maxcount;
            if (count == 0) count = 0x100;
            for (uint i = 0; i < count; i++)
                result.Add((i, $"{U.ToHexString(i)} {NameResolver.GetUnitName(i)}"));
            return result;
        }

        public static List<(uint id, string name)> MakeClassList()
        {
            var result = new List<(uint, string)>();
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;
            uint baseAddr = ResolvePointer(rom, rom.RomInfo.class_pointer);
            uint dataSize = rom.RomInfo.class_datasize;
            if (baseAddr == 0 || dataSize == 0) return result;
            uint count = rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
            {
                if (i == 0) return true;
                if (i > 0xFF) return false;
                return rom.u8(addr + 4) != 0;
            });
            for (uint i = 0; i < count; i++)
                result.Add((i, $"{U.ToHexString(i)} {NameResolver.GetClassName(i)}"));
            return result;
        }

        public static List<(uint id, string name)> MakeItemList()
        {
            var result = new List<(uint, string)>();
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;
            uint baseAddr = ResolvePointer(rom, rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            if (baseAddr == 0 || dataSize == 0) return result;
            uint count = rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
            {
                if (i > 0xFF) return false;
                return U.isPointerOrNULL(rom.u32(addr + 12));
            });
            for (uint i = 0; i < count; i++)
                result.Add((i, $"{U.ToHexString(i)} {NameResolver.GetItemName(i)}"));
            return result;
        }

        public static List<(uint id, string name)> MakeSongList()
        {
            var result = new List<(uint, string)>();
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;
            // Use a reasonable default count; song table details are in WinForms layer
            uint count = 0x80;
            for (uint i = 0; i < count; i++)
                result.Add((i, $"{U.ToHexString(i)} {NameResolver.GetSongName(i)}"));
            return result;
        }

        public static List<(uint id, string name)> MakeAffinityList()
        {
            // Affinities are fixed across FE versions
            string[] names = { "None", "Fire", "Thunder", "Wind", "Ice", "Dark", "Light", "Anima" };
            var result = new List<(uint, string)>();
            for (uint i = 0; i < (uint)names.Length; i++)
                result.Add((i, $"{U.ToHexString(i)} {names[i]}"));
            return result;
        }

        public static List<(uint id, string name)> MakeWeaponTypeList()
        {
            string[] names = { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark", "Item" };
            var result = new List<(uint, string)>();
            for (uint i = 0; i < (uint)names.Length; i++)
                result.Add((i, $"{U.ToHexString(i)} {names[i]}"));
            return result;
        }

        /// <summary>
        /// Resolve a ROMFEINFO pointer address to a ROM offset.
        /// Mirrors StructExportCore.ResolvePointer.
        /// </summary>
        private static uint ResolvePointer(ROM rom, uint pointerAddr)
        {
            if (pointerAddr == 0 || pointerAddr == U.NOT_FOUND) return 0;
            uint offset = U.toOffset(pointerAddr);
            if (!U.isSafetyOffset(offset, rom)) return 0;
            return rom.p32(offset);
        }
    }
}
