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
        /// Reverse of <see cref="GetBattleAnimeAddrWhereID"/>: scan the class table
        /// for the FIRST class whose battle-anime SETTING pointer
        /// (<c>p32(classAddr + 48)</c> FE6 / <c>p32(classAddr + 52)</c> FE7-8)
        /// equals <paramref name="findAddress"/>, and return its class id (0-based
        /// table index). Cross-platform mirror of WinForms
        /// <c>ClassForm.GetIDWhereBattleAnimeAddr</c> (<c>ClassForm.cs:624-653</c>):
        /// the lookup normalizes <paramref name="findAddress"/> through
        /// <see cref="U.toOffset"/> first (so a raw GBA pointer and its offset both
        /// resolve), rejects an unsafe offset, then compares against each class's
        /// stored setting pointer (already an offset — <c>rom.p32</c> applies
        /// <c>U.toOffset</c>). Returns <see cref="U.NOT_FOUND"/> on a miss or an
        /// unresolvable ROM/table. Guarded — never throws.
        /// <para>
        /// Used by the Avalonia Battle Animation Editor's Class-Editor Jump (#1377)
        /// to recognize a class battle-anime setting pointer (which lives in a
        /// per-class region, NOT the global anime-list table) so the editor can
        /// load the correct entry instead of falling back to entry 0.
        /// </para>
        /// </summary>
        public static uint GetIDWhereBattleAnimeAddr(ROM rom, uint findAddress)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            findAddress = U.toOffset(findAddress);
            if (!U.isSafetyOffset(findAddress, rom)) return U.NOT_FOUND;

            uint classPtr = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (classPtr == 0 || (ulong)classPtr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            // FE6 stores the anime-setting pointer at +48; FE7/FE8 at +52.
            uint settingOffsetInEntry = rom.RomInfo.version == 6 ? 48u : 52u;

            int count = GetClassCount(rom, baseAddr, datasize);
            uint addr = baseAddr;
            for (int i = 0; i < count; i++, addr += datasize)
            {
                uint settingSlot = addr + settingOffsetInEntry;
                if (!U.isSafetyOffset(settingSlot, rom)) break;
                // 4-byte EOF guard before the p32 read (the slot may sit in the
                // last 1-3 bytes near EOF where isSafetyOffset still passes).
                if ((ulong)settingSlot + 4 > (ulong)rom.Data.Length) break;
                if (rom.p32(settingSlot) == findAddress)
                    return (uint)i;
            }
            return U.NOT_FOUND;
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

        // ============================================================
        // Unit Wait Icon <-> Class back-references (#991)
        // Ports WF ClassForm.GetClassIDWhereWaitIconID /
        // GetClassMoveIcon / GetClassNameWhereWaitIconID.
        // ============================================================

        /// <summary>
        /// Scan the class table for the FIRST class whose wait-icon field
        /// (<c>u8(classAddr + 6)</c>) equals <paramref name="waitIconId"/>, and
        /// return its class id (0-based table index, matching WF). Returns
        /// <see cref="U.NOT_FOUND"/> on a miss or an unresolvable ROM/table.
        /// Mirror of WF <c>ClassForm.GetClassIDWhereWaitIconID</c>. Guarded —
        /// never throws.
        /// </summary>
        public static uint GetClassIdWhereWaitIconId(ROM rom, uint waitIconId)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            uint classPtr = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (classPtr == 0 || (ulong)classPtr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            int count = GetClassCount(rom, baseAddr, datasize);
            uint addr = baseAddr;
            for (int i = 0; i < count; i++, addr += datasize)
            {
                uint waitSlot = addr + 6;
                if (!U.isSafetyOffset(waitSlot, rom)) break;
                if (rom.u8(waitSlot) == waitIconId)
                    return (uint)i;
            }
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Read the move-icon id for class <paramref name="classId"/>
        /// (<c>u8(classAddr + 4)</c>). Mirror of WF
        /// <c>ClassForm.GetClassMoveIcon</c>: returns <see cref="U.NOT_FOUND"/>
        /// for class 0, an out-of-range <paramref name="classId"/> (beyond the
        /// class table's row count), or an unresolvable ROM/address. Guarded —
        /// never throws.
        /// </summary>
        public static uint GetClassMoveIcon(ROM rom, uint classId)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (classId == 0) return U.NOT_FOUND;
            uint classPtr = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (classPtr == 0 || (ulong)classPtr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            // Bound classId against the table's actual row count (#993 Copilot
            // review): without this, an out-of-range classId that still lands
            // inside rom.Data would return an arbitrary byte instead of
            // NOT_FOUND, violating the documented contract. Uses the SAME
            // GetClassCount logic as GetClassIdWhereWaitIconId.
            int count = GetClassCount(rom, baseAddr, datasize);
            if (classId >= (uint)count) return U.NOT_FOUND;

            // Overflow-safe address arithmetic (#993 Copilot review): compute in
            // ulong + bounds-check the +4 read before casting back.
            ulong moveSlot64 = (ulong)baseAddr + (ulong)classId * datasize + 4UL;
            if (moveSlot64 + 1UL > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint moveSlot = (uint)moveSlot64;
            if (!U.isSafetyOffset(moveSlot, rom)) return U.NOT_FOUND;
            return rom.u8(moveSlot);
        }

        /// <summary>
        /// Read the wait-icon id for class <paramref name="classId"/>
        /// (<c>u8(classAddr + 6)</c>). Mirror of WF
        /// <c>ClassForm.GetClassWaitIcon</c>: returns <see cref="U.NOT_FOUND"/>
        /// for class 0, an out-of-range <paramref name="classId"/>, or an
        /// unresolvable ROM/address. Guarded — never throws. Drives the Unit
        /// Move Icon editor's "Jump to Wait Icon" button (#1177, the reciprocal
        /// of the Wait Icon editor's jump-to-move-icon).
        /// </summary>
        public static uint GetClassWaitIcon(ROM rom, uint classId)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (classId == 0) return U.NOT_FOUND;
            uint classPtr = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (classPtr == 0 || (ulong)classPtr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            int count = GetClassCount(rom, baseAddr, datasize);
            if (classId >= (uint)count) return U.NOT_FOUND;

            ulong waitSlot64 = (ulong)baseAddr + (ulong)classId * datasize + 6UL;
            if (waitSlot64 + 1UL > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint waitSlot = (uint)waitSlot64;
            if (!U.isSafetyOffset(waitSlot, rom)) return U.NOT_FOUND;
            return rom.u8(waitSlot);
        }

        /// <summary>
        /// Resolve the class NAME that owns wait-icon <paramref name="waitIconId"/>
        /// (via <see cref="GetClassIdWhereWaitIconId"/> →
        /// <see cref="NameResolver.GetClassName"/>). Mirror of WF
        /// <c>ClassForm.GetClassNameWhereWaitIconID</c>. Returns "" on a miss.
        /// Guarded — never throws.
        /// <para>
        /// ROM-CONSISTENCY (#993 Copilot review): the cid is scanned from the
        /// passed <paramref name="rom"/>, but <see cref="NameResolver.GetClassName"/>
        /// (and the text decode it triggers) read from the ambient
        /// <see cref="CoreState.ROM"/> and cache globally by id. If
        /// <paramref name="rom"/> were a DIFFERENT instance, the name could be
        /// resolved against the wrong ROM (a mismatched/stale label). Rather than
        /// emit a wrong name, this method requires <paramref name="rom"/> to BE
        /// the ambient <c>CoreState.ROM</c> (the established Core-seam pattern,
        /// e.g. <c>SkillSystemsAnimeExportCore</c>) and returns "" otherwise. The
        /// real callers (<c>ImageUnitWaitIconViewModel.LoadList</c> /
        /// <c>ListParityHelper.BuildImageUnitWaitIconList</c>) both pass
        /// <c>CoreState.ROM</c>, so labels are unaffected in practice.
        /// </para>
        /// </summary>
        public static string GetClassNameWhereWaitIconId(ROM rom, uint waitIconId)
        {
            if (rom == null) return string.Empty;
            // Name resolution is ambient-ROM-bound; refuse to resolve against a
            // different ROM instance (would produce a wrong label).
            if (!ReferenceEquals(rom, CoreState.ROM)) return string.Empty;
            uint cid = GetClassIdWhereWaitIconId(rom, waitIconId);
            if (cid == U.NOT_FOUND) return string.Empty;
            try
            {
                return NameResolver.GetClassName(cid) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Class-table row count, mirroring <c>ClassForm.Init</c>'s read-max
        /// callback: cid 0 always counts, then scan while <c>u8(addr+4) != 0</c>,
        /// capped at 0xFF.
        /// </summary>
        static int GetClassCount(ROM rom, uint baseAddr, uint datasize)
        {
            uint count = rom.getBlockDataCount(baseAddr, datasize, (i, addr) =>
            {
                if (i == 0) return true;
                if (i > 0xFF) return false;
                uint flagAddr = addr + 4;
                if (!U.isSafetyOffset(flagAddr, rom)) return false;
                return rom.u8(flagAddr) != 0;
            });
            return (int)count;
        }
    }
}
