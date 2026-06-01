// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform extraction of WinForms ClassForm.SetSimClass for #428.
//
// The Avalonia UnitFE7View needs to populate a `GrowSimulator` with the
// class-base / class-grow / class-magic-ext inputs from the active class
// entry in the ROM. The original helper (FEBuilderGBA/ClassForm.cs::SetSimClass)
// reads those bytes via InputFormRef + Program.ROM, both of which are
// WinForms-only. This Core-side mirror reads the same bytes via the abstract
// `ROM` type (FEBuilderGBA.Core.Rom) so the Avalonia view can call it without
// taking a dependency on the WinForms project. The WinForms ClassForm.SetSimClass
// delegates here so behavior stays identical across UIs (parity-test gated).
//
// Class-entry byte layout (matches WinForms ClassForm.SetSimClass + Init):
//   base addr = ROM.p32(RomInfo.class_pointer) + cid * RomInfo.class_datasize
//   offset +11..+16 : class base hp / str / skl / spd / def / res (signed byte)
//   offset +27..+33 : class grow hp / str / skl / spd / def / res / luck
//   magic-extends (FE7UMAGIC / FE8UMAGIC) come from MagicSplitUtil.GetClassAs(...)
using FEBuilderGBA; // ROM, GrowSimulator, MagicSplitUtil, U all live in the parent namespace.

namespace FEBuilderGBA.Core
{
    public static class ClassFormCore
    {
        /// <summary>
        /// Populate <paramref name="sim"/>'s class-base + class-grow inputs by
        /// reading bytes from class <paramref name="cid"/>'s entry in
        /// <paramref name="rom"/>. No-op when <paramref name="rom"/> is null,
        /// <paramref name="cid"/> is zero, or the resolved address is unsafe.
        /// Mirrors the behavior of the WinForms `ClassForm.SetSimClass`
        /// helper (read-only + magic-split aware).
        /// </summary>
        public static void SetSimClass(ref GrowSimulator sim, uint cid, ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return;
            if (cid == 0) return;
            uint classPtr = rom.RomInfo.class_pointer;
            uint classDataSize = rom.RomInfo.class_datasize;
            if (classPtr == 0 || classDataSize == 0) return;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint addr = baseAddr + cid * classDataSize;
            if (!U.isSafetyOffset(addr, rom)) return;
            if (!U.isSafetyOffset(addr + classDataSize - 1, rom)) return;

            // Class base stats (offsets 11..16 are signed-byte deltas to the
            // unit-base values; magic-ext base offset depends on the magic-split
            // flavour and is delegated to MagicSplitUtil).
            sim.SetClassBase(
                  (int)(sbyte)rom.u8(addr + 11)  //hp
                , (int)(sbyte)rom.u8(addr + 12)  //str
                , (int)(sbyte)rom.u8(addr + 13)  //skill
                , (int)(sbyte)rom.u8(addr + 14)  //spd
                , (int)(sbyte)rom.u8(addr + 15)  //def
                , (int)(sbyte)rom.u8(addr + 16)  //res
                , (int)(sbyte)MagicSplitUtil.GetClassBaseMagicExtends(cid, addr) //magic ext
                );

            // Class growth rates (offsets 27..33 are unsigned bytes; class-grow
            // magic-ext source again depends on the magic-split flavour).
            sim.SetClassGrow(
                  (int)rom.u8(addr + 27)  //hp
                , (int)rom.u8(addr + 28)  //str
                , (int)rom.u8(addr + 29)  //skill
                , (int)rom.u8(addr + 30)  //spd
                , (int)rom.u8(addr + 31)  //def
                , (int)rom.u8(addr + 32)  //res
                , (int)rom.u8(addr + 33)  //luck
                , (int)MagicSplitUtil.GetClassGrowMagicExtends(cid, addr) //magic ext
                );
        }

        /// <summary>
        /// Resolve the battle-anime-setting offset for class <paramref name="classID"/>.
        /// Cross-platform mirror of WinForms <c>ClassForm.GetBattleAnimeAddrWhereID</c>
        /// (<c>ClassForm.cs:574-596</c>): the class entry holds the anime-setting
        /// pointer at a version-dependent offset — <c>addr + 48</c> for FE6
        /// (<c>RomInfo.version == 6</c>), <c>addr + 52</c> for FE7/FE8. The class
        /// base address is resolved via <c>RomInfo.class_pointer</c> /
        /// <c>class_datasize</c> (the same way <see cref="SetSimClass"/> does),
        /// NOT via the WinForms-coupled <c>InputFormRef.IDToAddr</c>.
        /// </summary>
        /// <returns>The anime-setting ROM OFFSET (the read goes through
        /// <c>rom.p32</c>, which applies <c>U.toOffset</c> — callers must NOT
        /// double-convert), or <see cref="U.NOT_FOUND"/> when the ROM/class is
        /// null/zero/unresolvable.</returns>
        public static uint GetBattleAnimeAddrWhereID(ROM rom, int classID)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            // classID 0 is "none" for the other class-ID APIs — don't accidentally
            // resolve entry 0; reject <= 0 (and any negative).
            if (classID <= 0) return U.NOT_FOUND;
            uint classPtr = rom.RomInfo.class_pointer;
            uint classDataSize = rom.RomInfo.class_datasize;
            if (classPtr == 0 || classDataSize == 0) return U.NOT_FOUND;

            // rom.p32 reads 4 bytes (U.u32) — guard the full 4-byte span before
            // the read so a table pointer near EOF cannot throw IndexOutOfRange
            // (the #818/#827 4-byte-header pattern). NOTE: class_pointer is a
            // fixed RomInfo header-region location (often < 0x200), so we do NOT
            // apply the isSafetyOffset lower-bound here — only the EOF bound,
            // matching the original direct rom.p32(classPtr) read.
            if ((ulong)classPtr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            uint addr = baseAddr + (uint)classID * classDataSize;
            if (!U.isSafetyOffset(addr, rom)) return U.NOT_FOUND;

            // FE6 stores the anime-setting pointer at +48; FE7/FE8 at +52.
            uint settingSlot = rom.RomInfo.version == 6 ? addr + 48 : addr + 52;
            if (!U.isSafetyOffset(settingSlot, rom)) return U.NOT_FOUND;
            // 4-byte EOF guard before the p32 read (settingSlot may sit in the
            // last 1-3 bytes near EOF where isSafetyOffset still passes).
            if ((ulong)settingSlot + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            return rom.p32(settingSlot);
        }

        /// <summary>
        /// Read the first battle-anime ID from an anime-setting pointer. Mirror of
        /// WinForms <c>ImageBattleAnimeForm.GetAnimeIDByAnimeSettingPointer</c>
        /// (<c>ImageBattleAnimeForm.cs:339-350</c>): the anime-setting block stores
        /// the anime ID as a <c>u16</c> at <c>pointer + 2</c> (note the <c>+2</c>).
        /// A null / out-of-range pointer yields <c>0</c> (WF's safety-guard return),
        /// not a crash.
        /// </summary>
        public static uint GetAnimeIDByAnimeSettingPointer(ROM rom, uint animeSettingPointer)
        {
            if (rom == null) return 0;
            uint off = U.toOffset(animeSettingPointer);
            if (!U.isSafetyOffset(off, rom)) return 0;
            // The +2 read must itself stay in-bounds (EOF safety the WF u16 read
            // relied on the ROM array length for).
            if (off + 2 + 2 > (uint)rom.Data.Length) return 0;
            return rom.u16(off + 2);
        }

        /// <summary>
        /// Resolve the battle-anime ID used by class <paramref name="classID"/>.
        /// Cross-platform mirror of WinForms
        /// <c>ImageBattleAnimeForm.GetAnimeIDByClassID</c> (<c>ImageBattleAnimeForm.cs:334</c>):
        /// <c>GetAnimeIDByAnimeSettingPointer(GetBattleAnimeAddrWhereID(classID))</c>
        /// — the <c>p32</c> (FE6 <c>+48</c> / FE7-8 <c>+52</c>) class-indirection
        /// followed by the <c>u16(ptr + 2)</c> read. Returns <c>0</c> on an
        /// unresolvable class / ROM (WF returns 0, which is the "first anime"
        /// sentinel, never a crash).
        /// </summary>
        public static uint GetAnimeIDByClassID(ROM rom, int classID)
        {
            uint settingPointer = GetBattleAnimeAddrWhereID(rom, classID);
            if (settingPointer == U.NOT_FOUND) return 0;
            return GetAnimeIDByAnimeSettingPointer(rom, settingPointer);
        }
    }
}
