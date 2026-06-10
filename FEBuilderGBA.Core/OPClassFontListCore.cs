// SPDX-License-Identifier: GPL-3.0-or-later
//
// #1029 — Cross-platform Core port of the WinForms OPClassFontForm.MakeList() /
// OPClassFontFE8UForm.MakeList() table walks (READ-ONLY, never mutates the ROM).
// The JP-class-reel-font wipe flow (ToolTranslateROMWipeJPClassReelFont) needs the
// list of OP-class-font slot addresses; the WF version is built through
// InputFormRef (Form-bound), so this provides the same enumeration WinForms-free.
//
// The table at p32(op_class_font_pointer) is an array of 4-byte glyph-image
// pointers (WF InputFormRef BlockSize 4):
//   * JP (multibyte) ROMs: DataCount = the contiguous run of pointer slots
//     (mirrors WF OPClassFontForm.Init's U.isPointer(u32(addr)) callback).
//   * FE8U (English) ROMs: DataCount = the fixed run i <= 0x7a (mirrors WF
//     OPClassFontFE8UForm.Init's i <= 0x7a callback).
// The WipeJPClassReelFont path only fires on FE8J (version 8 + multibyte), so the
// wipe consumes the JP contiguous run; the FE8U branch is included for parity /
// future reuse.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY cross-platform enumerator for the OP-class JP-name font slot
    /// table (port of WinForms <c>OPClassFontForm.MakeList</c> /
    /// <c>OPClassFontFE8UForm.MakeList</c> minus the <c>InputFormRef</c> /
    /// <c>Form</c> dependency).
    /// </summary>
    public static class OPClassFontListCore
    {
        /// <summary>4-byte glyph-image pointer slot.</summary>
        public const uint EntrySize = 4;

        /// <summary>FE8U fixed DataCount upper index (slots 0..0x7a inclusive).</summary>
        const uint FE8UMaxIndex = 0x7a;

        /// <summary>Generous upper bound on the JP table size (the DataCount scan).</summary>
        const uint ScanCap = 0x4000;

        /// <summary>
        /// Enumerate the OP-class-font glyph slots on the given ROM. Returns one
        /// <see cref="AddrResult"/> per slot (addr = the slot address, name = the
        /// slot index hex), mirroring the WF MakeList routing: FE8U uses the fixed
        /// i &lt;= 0x7a run, every other (JP) layout uses the contiguous pointer
        /// run. Returns an empty list (never null, never throws) when the table
        /// pointer is missing / unsafe.
        /// </summary>
        public static List<AddrResult> MakeList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint tablePtr = rom.RomInfo.op_class_font_pointer;
            // Guard the 4-byte p32 read.
            if (!U.isSafetyOffset(tablePtr, rom) || tablePtr + 4 > (uint)rom.Data.Length)
                return result;
            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase, rom)) return result;

            bool isFE8U = rom.RomInfo.version == 8 && !rom.RomInfo.is_multibyte;

            if (isFE8U)
            {
                // FE8U: fixed i <= 0x7a run (WF OPClassFontFE8UForm.Init).
                for (uint i = 0; i <= FE8UMaxIndex; i++)
                {
                    uint slot = tableBase + i * EntrySize;
                    if (slot + 4 > (uint)rom.Data.Length) break; // EOF / overflow-safe
                    result.Add(new AddrResult(slot, U.ToHexString(i)));
                }
                return result;
            }

            // JP / other: contiguous run where u32(slot) is a valid pointer
            // (WF OPClassFontForm.Init).
            for (uint i = 0; i < ScanCap; i++)
            {
                uint slot = tableBase + i * EntrySize;
                if (slot + 4 > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(slot))) break; // contiguous-run terminator

                result.Add(new AddrResult(slot, U.ToHexString(i)));
            }

            return result;
        }
    }
}
