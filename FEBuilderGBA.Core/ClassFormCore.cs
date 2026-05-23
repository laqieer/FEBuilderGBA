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
    }
}
