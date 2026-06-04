// SPDX-License-Identifier: GPL-3.0-or-later
// Shared jump-target address resolution for editor views (#948 review).
//
// The Haiku Event editors (and other type-ID fields) need to translate a
// 1-based unit id or a text id into the ROM data address an IdFieldControl's
// Jump button should navigate to. These two computations were duplicated
// verbatim across EventHaikuView / EventHaikuFE6View / EventHaikuFE7View; this
// helper centralises them so the pointer-safety + offset math live in ONE place.
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Pure, ROM-explicit helpers that resolve a type-ID to the ROM data address
    /// its editor Jump button should open. All methods are bounds-safe and return
    /// 0 ("no target") on any failure rather than throwing.
    /// </summary>
    public static class EditorJumpAddressHelper
    {
        /// <summary>
        /// Resolve a 1-based unit id (WinForms convention; 0 = ANY/no unit) to the
        /// unit-table entry address in <paramref name="rom"/>. FE6 skips table
        /// entry 0 (offset by one record). Returns 0 when the id is 0, the ROM /
        /// pointer is unavailable, or the entry would fall out of bounds.
        /// </summary>
        public static uint UnitAddrFor(ROM rom, uint oneBasedId)
        {
            if (rom?.RomInfo == null) return 0;
            if (oneBasedId == 0) return 0; // 1-based: 0 = ANY/no unit
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize; // FE6 skips entry 0
            uint entryAddr = baseAddr + (oneBasedId - 1) * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        /// <summary>
        /// Resolve a text id to its text-table ROW address
        /// (<c>textBase + textId * 4</c>, one pointer per id) in
        /// <paramref name="rom"/>. Computes in 64-bit to detect wrap-around.
        /// Returns 0 when the ROM / pointer is unavailable or the row would fall
        /// out of bounds.
        /// </summary>
        public static uint TextRowAddrFor(ROM rom, uint textId)
        {
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            // Text-table ROW address = textBase + textId*4 (one pointer per id).
            ulong addr64 = (ulong)baseAddr + (ulong)textId * 4UL;
            if (addr64 > uint.MaxValue) return 0;
            uint addr = (uint)addr64;
            if (!U.isSafetyOffset(addr, rom)) return 0;
            if (!U.isSafetyOffset(addr + 3, rom)) return 0;
            return addr;
        }
    }
}
