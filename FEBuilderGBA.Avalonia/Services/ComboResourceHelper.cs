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

        public static List<(uint id, string name)> MakeWeaponTypeList(uint? currentId = null)
        {
            var result = new List<(uint id, string name)>
            {
                (0x00, LocalizeComboLabel("00=Sword")),
                (0x01, LocalizeComboLabel("01=Lance")),
                (0x02, LocalizeComboLabel("02=Axe")),
                (0x03, LocalizeComboLabel("03=Bow")),
                (0x04, LocalizeComboLabel("04=Staff")),
                (0x05, LocalizeComboLabel("05=Anima")),
                (0x06, LocalizeComboLabel("06=Light")),
                (0x07, LocalizeComboLabel("07=Dark")),
                (0x09, LocalizeComboLabel("09=Item")),
                (0x0B, LocalizeComboLabel("0B=Dragonstone/Monster Weapon")),
                (0x0C, LocalizeComboLabel("0C=Ring")),
                (0x11, LocalizeComboLabel("11=Dragonstone")),
                (0x12, LocalizeComboLabel("12=Dancer's Ring")),
            };

            if (currentId.HasValue && !result.Exists(x => x.id == currentId.Value))
                result.Add((currentId.Value, $"{U.ToHexString(currentId.Value)} {R._("Unknown")}"));

            return result;
        }

        private static string LocalizeComboLabel(string source)
            => R._(source).Replace('=', ' ');

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
