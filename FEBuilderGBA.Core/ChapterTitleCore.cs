// SPDX-License-Identifier: GPL-3.0-or-later
//
// #1029 — Cross-platform Core port of the WinForms ImageChapterTitleForm.MakeList()
// table walk (READ-ONLY, never mutates the ROM). The chapter-title editor and the
// JP-chapter-name wipe flow (ToolTranslateROMWipeJPChapterName) need the list of
// chapter-title struct addresses; the WF version is built through InputFormRef
// (Form-bound), so this provides the same enumeration WinForms-free.
//
// The table at p32(image_chapter_title_pointer) is an array of 12-byte structs:
//   +0 : LZ77 image pointer (the "Save" picture)
//   +4 : LZ77 image pointer (the chapter Number)
//   +8 : LZ77 image pointer (the chapter Title)
// DataCount = the contiguous run of rows whose +0 entry is a valid pointer (mirrors
// the WF InputFormRef "is data exists" callback: U.isPointer(u32(addr+0))).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY cross-platform enumerator for the chapter-title struct table
    /// (port of WinForms <c>ImageChapterTitleForm.MakeList</c> minus the
    /// <c>InputFormRef</c> / <c>Form</c> dependency).
    /// </summary>
    public static class ChapterTitleCore
    {
        /// <summary>12-byte struct (Save/Number/Title LZ77 image pointers).</summary>
        public const uint EntrySize = 12;

        /// <summary>Generous upper bound on the chapter-title table size (the DataCount scan).</summary>
        const uint ScanCap = 0x4000;

        /// <summary>
        /// Enumerate the chapter-title struct rows on the given ROM. Returns one
        /// <see cref="AddrResult"/> per row (addr = the row base, name = the map
        /// index hex). The list stops at the first row whose +0 image pointer is
        /// not a valid pointer (mirrors the WF InputFormRef contiguous-run
        /// DataCount). Returns an empty list (never null, never throws) when the
        /// table pointer is missing / unsafe.
        /// </summary>
        public static List<AddrResult> MakeList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint tablePtr = rom.RomInfo.image_chapter_title_pointer;
            // isSafetyOffset only checks tablePtr < Data.Length, not that the 4-byte
            // p32 read is in-bounds; guard tablePtr+4 so rom.p32 can't throw.
            if (!U.isSafetyOffset(tablePtr, rom) || tablePtr + 4 > (uint)rom.Data.Length)
                return result;
            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase, rom)) return result;

            for (uint i = 0; i < ScanCap; i++)
            {
                uint row = tableBase + i * EntrySize;
                // Need the +0 pointer slot in-bounds before reading it.
                if (row + 4 > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(row))) break; // contiguous-run terminator

                result.Add(new AddrResult(row, U.ToHexString(i)));
            }

            return result;
        }
    }
}
