// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform port of WinForms ImageUnitPaletteForm.MakeClassList (#985).
//
// The Avalonia Unit Palette Editor's Edit tab shows a class battle-anime sample
// preview + the resolved battle-anime ID for the SELECTED unit-palette slot.
// To render anything it first needs to know WHICH class uses that palette slot.
// WinForms ImageUnitPaletteForm.MakeClassList(selectindex) builds that mapping
// by scanning every unit (FE6/FE7) or the dedicated FE8 unit-palette tables
// (color + class) and collecting the classes that point at the selected slot.
//
// All the WinForms helpers it relies on (UnitForm.GetPaletteLowClass /
// GetPaletteHighClass / GetClassID / GetHighClass, ClassForm.isHighClass /
// GetChangeClassID) route through InputFormRef + Program.ROM, both WinForms-only.
// This Core mirror reads the same bytes via the abstract `ROM` type so the
// Avalonia view can resolve the default preview class without depending on the
// WinForms project. Strictly READ-ONLY: never writes the ROM, never throws.
//
// Byte layout (matches the WinForms helpers above):
//   Unit entry  base = ROM.p32(RomInfo.unit_pointer)  + (uid-1) * unit_datasize
//                  +5  : base class id          (UnitForm.GetClassID)
//                  +35 : low-class palette id    (UnitForm.GetPaletteLowClass)
//                  +36 : high-class palette id   (UnitForm.GetPaletteHighClass)
//   Class entry base = ROM.p32(RomInfo.class_pointer) + cid   * class_datasize
//                  +5  : change(CC) class id     (ClassForm.GetChangeClassID)
//                  +37 : FE6 high-class flag bit0 (ClassFE6Form.isHighClassFE6)
//                  +41 : FE7/8 high-class flag bit0 (ClassForm.isHighClassAddr)
//   FE8 unit-palette tables (RomInfo.unit_palette_color/class_pointer derefs):
//                  colorBase + i*7 + n : palette id  (1-based; 0 == none)
//                  classBase + i*7 + n : class id
using FEBuilderGBA; // ROM, U all live in the parent namespace.

namespace FEBuilderGBA.Core
{
    public static class UnitPaletteClassResolverCore
    {
        /// <summary>
        /// Resolve the FIRST class id that uses unit-palette slot
        /// <paramref name="slotIndex"/> (0-based, == WinForms
        /// AddressList.SelectedIndex), or 0 when no class references it. Pure
        /// read-only port of WinForms <c>ImageUnitPaletteForm.MakeClassList</c>
        /// (first match wins). Never throws — every pointer location and computed
        /// address is guarded before the read.
        /// </summary>
        public static uint ResolveDefaultPreviewClass(ROM rom, int slotIndex)
        {
            if (rom == null || rom.RomInfo == null || slotIndex < 0)
            {
                return 0;
            }

            if (rom.RomInfo.version >= 8)
            {
                return ResolveFE8(rom, slotIndex);
            }
            return ResolveFE67(rom, slotIndex);
        }

        // FE8: a dedicated pair of byte tables (color + class), 7 palettes per
        // unit. Mirrors WinForms ImageUnitPaletteForm.MakeClassList :142-171.
        static uint ResolveFE8(ROM rom, int slotIndex)
        {
            uint colorPtrLoc = rom.RomInfo.unit_palette_color_pointer;
            uint classPtrLoc = rom.RomInfo.unit_palette_class_pointer;
            if (!IsReadablePointerLocation(rom, colorPtrLoc)) return 0;
            if (!IsReadablePointerLocation(rom, classPtrLoc)) return 0;

            uint colorBase = rom.p32(colorPtrLoc);
            uint classBase = rom.p32(classPtrLoc);
            // Guard the DEREFERENCED bases before scanning: a 0/invalid pointer
            // slot makes p32 return 0, and an unguarded scan would then read
            // unrelated ROM bytes (~0x200+) and could return a bogus class id.
            if (!U.isSafetyOffset(colorBase, rom) || !U.isSafetyOffset(classBase, rom)) return 0;
            uint maxcount = rom.RomInfo.unit_maxcount;

            for (uint i = 0; i < maxcount; i++)
            {
                for (uint n = 0; n < 7; n++)
                {
                    uint colorAddr = colorBase + i * 7 + n;
                    if (!U.isSafetyOffset(colorAddr, rom)) continue;
                    uint paletteid = rom.u8(colorAddr);
                    if (paletteid <= 0) continue;
                    if (paletteid - 1 != (uint)slotIndex) continue;

                    uint classAddr = classBase + i * 7 + n;
                    if (!U.isSafetyOffset(classAddr, rom)) continue;
                    return rom.u8(classAddr); // first match wins
                }
            }
            return 0;
        }

        // FE6/FE7: the palette id lives inside each unit record (+35 low / +36
        // high). Mirrors WinForms ImageUnitPaletteForm.MakeClassList :174-195.
        static uint ResolveFE67(ROM rom, int slotIndex)
        {
            uint maxcount = rom.RomInfo.unit_maxcount;
            for (uint i = 0; i < maxcount; i++)
            {
                // WinForms loops uid = 0 .. maxcount-1 and passes uid straight to
                // GetPaletteLowClass/GetClassID, which internally do `uid--` then
                // index (uid-1). uid 0 is the "none" sentinel (returns 0), so the
                // i==0 iteration never matches — preserve that by starting uids at
                // the same value WinForms uses (i), with the helpers handling the
                // 0-sentinel + (uid-1) index.
                uint uid = i;

                uint low = ReadUnitByte(rom, uid, 35);
                uint high = ReadUnitByte(rom, uid, 36);

                if (low > 0 && low - 1 == (uint)slotIndex)
                {
                    return GetClassID(rom, uid); // first match wins
                }
                if (high > 0 && high - 1 == (uint)slotIndex)
                {
                    return GetHighClass(rom, uid); // first match wins
                }
            }
            return 0;
        }

        // Read a single byte at unit-record offset `off` for `uid` (1-based; 0 ==
        // none). Mirrors UnitForm/UnitFE7Form/UnitFE6Form palette getters.
        static uint ReadUnitByte(ROM rom, uint uid, uint off)
        {
            uint addr = GetUnitAddr(rom, uid);
            if (addr == U.NOT_FOUND) return 0;
            uint readAddr = addr + off;
            if (!U.isSafetyOffset(readAddr, rom)) return 0;
            return rom.u8(readAddr);
        }

        // unit record base for `uid` (UnitForm.Init addressing: base = p32(unit_pointer)
        // + (uid-1) * unit_datasize). Returns NOT_FOUND for uid 0 or unsafe addr.
        static uint GetUnitAddr(ROM rom, uint uid)
        {
            if (uid == 0) return U.NOT_FOUND;
            uint unitPtrLoc = rom.RomInfo.unit_pointer;
            uint datasize = rom.RomInfo.unit_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (!IsReadablePointerLocation(rom, unitPtrLoc)) return U.NOT_FOUND;
            uint baseAddr = rom.p32(unitPtrLoc);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            uint addr = baseAddr + (uid - 1) * datasize;
            if (!U.isSafetyOffset(addr, rom)) return U.NOT_FOUND;
            return addr;
        }

        // UnitForm.GetClassID: unit record +5 (after uid-- inside the helper).
        static uint GetClassID(ROM rom, uint uid)
        {
            return ReadUnitByte(rom, uid, 5);
        }

        // UnitForm.GetHighClass -> UnitFE7Form.GetHighClassFE7 (FE6/FE7 share the
        // class-table promotion; FE8 never reaches here). Faithful port:
        //   base class = GetClassID(uid); if it's already a high class -> done;
        //   else change(CC) class = class+5; if that's a high class -> done;
        //   else fall back to the base class.
        static uint GetHighClass(ROM rom, uint uid)
        {
            uint baseClass = GetClassID(rom, uid);
            if (baseClass <= 0) return 0;
            if (IsHighClass(rom, baseClass)) return baseClass;

            uint changeClass = GetChangeClassID(rom, baseClass);
            if (changeClass <= 0) return baseClass;
            if (IsHighClass(rom, changeClass)) return changeClass;

            return baseClass;
        }

        // ClassForm.GetChangeClassID: class record +5.
        static uint GetChangeClassID(ROM rom, uint cid)
        {
            if (cid <= 0) return 0;
            uint addr = GetClassAddr(rom, cid);
            if (addr == U.NOT_FOUND) return 0;
            uint readAddr = addr + 5;
            if (!U.isSafetyOffset(readAddr, rom)) return 0;
            return rom.u8(readAddr);
        }

        // ClassForm.isHighClass: FE6 reads +37 bit0, FE7/8 reads +41 bit0.
        static bool IsHighClass(ROM rom, uint cid)
        {
            if (cid <= 0) return false;
            uint addr = GetClassAddr(rom, cid);
            if (addr == U.NOT_FOUND) return false;
            uint flagOff = rom.RomInfo.version <= 6 ? 37u : 41u;
            uint readAddr = addr + flagOff;
            if (!U.isSafetyOffset(readAddr, rom)) return false;
            uint flag2 = rom.u8(readAddr);
            return (flag2 & 0x01) == 0x01;
        }

        // class record base for `cid` (ClassForm.Init addressing: base =
        // p32(class_pointer) + cid * class_datasize — cid indexes directly,
        // cid 0 is a valid entry index). Returns NOT_FOUND on unsafe addr.
        static uint GetClassAddr(ROM rom, uint cid)
        {
            uint classPtrLoc = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return U.NOT_FOUND;
            if (!IsReadablePointerLocation(rom, classPtrLoc)) return U.NOT_FOUND;
            uint baseAddr = rom.p32(classPtrLoc);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;
            uint addr = baseAddr + cid * datasize;
            if (!U.isSafetyOffset(addr, rom)) return U.NOT_FOUND;
            return addr;
        }

        /// <summary>
        /// Fallback used by the view when <see cref="ResolveDefaultPreviewClass"/>
        /// finds no class for the selected slot: return the first class id (1..N)
        /// that actually has a battle animation, else 0. Read-only.
        /// </summary>
        public static uint FindFirstClassWithAnime(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return 0;
            int classCount = GetClassCount(rom);
            for (int cid = 1; cid <= classCount; cid++)
            {
                if (ClassFormCore.GetAnimeIDByClassID(rom, cid) > 0)
                {
                    return (uint)cid;
                }
            }
            return 0;
        }

        // Class table count, mirroring ClassForm.Init's read-max callback: scan
        // while u8(addr+4) != 0 (cid 0 always counts), capped at 0xFF.
        static int GetClassCount(ROM rom)
        {
            uint classPtrLoc = rom.RomInfo.class_pointer;
            uint datasize = rom.RomInfo.class_datasize;
            if (datasize == 0) return 0;
            if (!IsReadablePointerLocation(rom, classPtrLoc)) return 0;
            uint baseAddr = rom.p32(classPtrLoc);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
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

        // A pointer location must hold a full 4-byte pointer in-bounds before we
        // p32 it. RomInfo pointer-locations live in the fixed header region (often
        // < 0x200), so we only enforce the EOF upper bound here — matching the
        // direct `rom.p32(loc)` reads in the WinForms helpers.
        static bool IsReadablePointerLocation(ROM rom, uint loc)
        {
            if (loc == 0 || loc == U.NOT_FOUND) return false;
            if ((ulong)loc + 4 > (ulong)rom.Data.Length) return false;
            return true;
        }
    }
}
