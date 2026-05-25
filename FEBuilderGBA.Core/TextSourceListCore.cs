// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform list iterators for the multibyte text-source tables that
// ToolTranslateROM.ExportallText / ImportAllText reach into (#536). These
// are the menu / map terrain / sound-room / other-text tables that the
// WinForms InputFormRef-backed forms (`MenuDefinitionForm`, `MenuCommandForm`,
// `MapTerrainNameForm`, `SoundRoomForm`, `OtherTextForm`) expose via
// MakeList* statics. We re-implement them against `Rom.getBlockDataCount`
// so the WinForms `InputFormRef` migration is not a prerequisite.
//
// The WinForms forms keep their existing static MakeList methods. They will
// continue calling their own InputFormRef-based iteration so we don't risk a
// behaviour regression on the WinForms side. The Core helpers here are used
// by `ToolTranslateROMCore.ExportTextsToFile` / `ImportFont` so the Avalonia
// view can drive the same iteration without depending on WinForms.
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform enumerators for the text-source tables that
    /// ToolTranslateROM iterates over: menu definitions, menu commands,
    /// map terrain names, sound room entries, and "other text" pointer
    /// blocks. Each helper returns a list of <see cref="AddrResult"/>
    /// matching the WinForms `MakeList()` output shape.
    /// </summary>
    public static class TextSourceListCore
    {
        // ---------- Menu Definition (FE6/7/8 menu master table) ----------

        /// <summary>
        /// Return the menu-definition pointer set the WinForms
        /// MenuDefinitionForm iterates. Matches WF GetPointers().
        /// </summary>
        public static uint[] GetMenuDefinitionPointers(ROM rom)
        {
            if (rom?.RomInfo == null) return Array.Empty<uint>();
            return new uint[]
            {
                rom.RomInfo.menu_definiton_pointer,
                rom.RomInfo.menu_promotion_pointer,
                rom.RomInfo.menu_promotion_branch_pointer,
                rom.RomInfo.menu_definiton_split_pointer,
                rom.RomInfo.menu_definiton_worldmap_pointer,
                rom.RomInfo.menu_definiton_worldmap_shop_pointer,
            };
        }

        /// <summary>
        /// List ALL menu-definition rows across every menu-definition pointer
        /// the ROM exposes. Mirrors WF `MenuDefinitionForm.MakeListAll()`.
        /// Each row is 36 bytes; iteration stops at the first row whose
        /// pointer-field (offset +8) isn't a valid ROM pointer.
        /// </summary>
        public static List<AddrResult> MakeMenuDefinitionList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            const uint blockSize = 36;
            foreach (uint headPointer in GetMenuDefinitionPointers(rom))
            {
                if (headPointer == 0) continue;
                uint baseAddr = rom.p32(headPointer);
                if (!U.isSafetyOffset(baseAddr, rom)) continue;

                AppendBlockList(rom, baseAddr, blockSize, result,
                    (i, addr) => U.isPointer(rom.u32(addr + 8)));
            }
            return result;
        }

        // ---------- Menu Command (one MenuDefinition's command list) ----------

        /// <summary>
        /// List the menu-command entries pointed to by <paramref name="pointer"/>.
        /// Mirrors WF `MenuCommandForm.MakeListPointer(pointer)`. Each entry is
        /// 36 bytes; iteration stops at the first entry whose +0xC field is not
        /// a valid ROM pointer.
        /// </summary>
        public static List<AddrResult> MakeMenuCommandList(ROM rom, uint pointer)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            // Accept either a raw GBA pointer (0x08xxxxxx) or a ROM offset.
            // Mirrors WF `ReInitPointer` which first toOffset's the argument.
            uint offset = U.toOffset(pointer);
            if (!U.isSafetyOffset(offset, rom)) return result;

            uint baseAddr = rom.p32(offset);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            const uint blockSize = 36;
            AppendBlockList(rom, baseAddr, blockSize, result,
                (i, addr) => U.isPointer(rom.u32(addr + 0xC)));
            return result;
        }

        // ---------- Map Terrain Names (FE6/7/8 multibyte terrain labels) ----------

        /// <summary>
        /// List terrain-name pointer entries from `map_terrain_name_pointer`.
        /// Mirrors WF `MapTerrainNameForm.MakeList()` (multibyte path). Each
        /// entry is 4 bytes; iteration stops at the first entry whose +0 field
        /// is neither a valid pointer nor NULL.
        /// </summary>
        public static List<AddrResult> MakeMapTerrainNameList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;
            if (!rom.RomInfo.is_multibyte) return result;

            uint baseAddr = rom.p32(rom.RomInfo.map_terrain_name_pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            const uint blockSize = 4;
            AppendBlockList(rom, baseAddr, blockSize, result,
                (i, addr) => U.isPointerOrNULL(rom.u32(addr)));
            return result;
        }

        // ---------- Sound Room (FE7J only - multibyte song labels) ----------

        /// <summary>
        /// List sound-room entries from `sound_room_pointer`. Mirrors WF
        /// `SoundRoomForm.MakeList()`. Each entry is `sound_room_datasize`
        /// bytes; iteration stops when +0 is 0xFFFFFFFF or when after the first
        /// 10 rows the next 10 rows are all empty.
        /// </summary>
        public static List<AddrResult> MakeSoundRoomList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint datasize = rom.RomInfo.sound_room_datasize;
            uint baseAddr = rom.p32(rom.RomInfo.sound_room_pointer);
            if (datasize == 0 || !U.isSafetyOffset(baseAddr, rom)) return result;

            AppendBlockList(rom, baseAddr, datasize, result, (i, addr) =>
            {
                if (rom.u32(addr) == 0xFFFFFFFF) return false;
                if (i > 10 && rom.IsEmpty(addr, datasize * 10)) return false;
                return true;
            });
            return result;
        }

        // ---------- Other Text (config-driven pointer list, e.g. fixed UI strings) ----------

        /// <summary>
        /// List "other text" pointer entries read from `config/data/other_text_*.txt`.
        /// Mirrors WF `OtherTextForm.MakeOtherTextMap()`. Each line in the
        /// config file is one hex pointer; the entry name is the C-string at
        /// the pointer.
        /// </summary>
        public static List<AddrResult> MakeOtherTextList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            string configFile = U.ConfigDataFilename("other_text_", rom);
            if (string.IsNullOrEmpty(configFile) || !File.Exists(configFile)) return result;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(configFile);
            }
            catch (IOException)
            {
                return result;
            }

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                if (U.IsComment(raw) || U.OtherLangLine(raw)) continue;
                string trimmed = U.ClipComment(raw);
                if (string.IsNullOrEmpty(trimmed)) continue;

                uint addr = U.toOffset(U.atoh(trimmed));
                if (!U.isSafetyOffset(addr, rom)) continue;

                uint p_str = rom.p32(addr);
                string name = U.isSafetyOffset(p_str, rom) ? rom.getString(p_str) : string.Empty;
                result.Add(new AddrResult(addr, name, p_str));
            }
            return result;
        }

        // ---------- Internals ----------

        static void AppendBlockList(ROM rom, uint addr, uint blockSize,
            List<AddrResult> result, Func<int, uint, bool> isDataExists)
        {
            uint count = rom.getBlockDataCount(addr, blockSize, isDataExists);
            for (uint i = 0; i < count; i++)
            {
                result.Add(new AddrResult(addr + (i * blockSize), string.Empty));
            }
        }
    }
}
