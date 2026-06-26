// SPDX-License-Identifier: GPL-3.0-or-later
// TSA Animation editor (v1) per-category FRAMECOUNT frame enumerator (#1457).
//
// WinForms ImageTSAAnimeForm treats each tsaanime_ category as a sequence of
// FRAMECOUNT 12-byte frame entries (image @+0, palette @+4, TSA @+8) and
// enumerates ALL of them via ReInitPointer(pointer, count). The Avalonia v1
// editor previously listed only the first entry (frame 0) per category and
// discarded the FRAMECOUNT column, so frames 1..N-1 were invisible/uneditable.
//
// This pure, GUI-free helper reproduces the WinForms enumeration: it reads the
// FRAMECOUNT (column 0 of the LoadTSVResource value array) and yields one
// AddrResult per frame at base + i*12. Shared by ImageTSAAnimeViewModel.LoadList
// and ListParityHelper.BuildImageTSAAnimeList so the two surfaces cannot drift.
using System.Collections.Generic;

namespace FEBuilderGBA
{
    public static class ImageTSAAnimeFrameEnumCore
    {
        /// <summary>Bytes per frame entry (image P0 / palette P4 / TSA P8).</summary>
        public const uint SIZE = 12;

        /// <summary>
        /// Enumerate every FRAMECOUNT frame of every tsaanime_ category, mirroring
        /// WinForms ImageTSAAnimeForm's ReInitPointer(pointer, count) enumeration.
        /// </summary>
        /// <param name="rom">ROM to read category base pointers from (ROM-explicit,
        /// does NOT depend on ambient CoreState.ROM).</param>
        /// <param name="tsaAnime">Result of U.LoadTSVResource(ConfigDataFilename("tsaanime_")):
        /// pointer → [FRAMECOUNT, NAME, ...]. Column 0 = FRAMECOUNT (hex), column 1 = NAME.</param>
        /// <returns>One AddrResult per frame: addr = categoryBase + i*12,
        /// name = "{ptrHex} {NAME} {i}", tag = sequential row index. Never throws;
        /// returns an empty list for a null/empty input or null ROM.</returns>
        public static List<AddrResult> EnumerateFrames(ROM rom, Dictionary<uint, string[]> tsaAnime)
        {
            var result = new List<AddrResult>();
            if (rom == null || tsaAnime == null || tsaAnime.Count == 0) return result;

            uint romLen = (uint)rom.Data.Length;

            foreach (var pair in tsaAnime)
            {
                uint pointer = pair.Key;
                string[] sp = pair.Value;
                if (sp == null) continue;

                uint ptrOff = U.toOffset(pointer);
                if (!U.isSafetyOffset(ptrOff, rom)) continue;

                uint baseAddr = rom.p32(ptrOff);
                if (!U.isSafetyOffset(baseAddr, rom)) continue;

                // Column 0 = FRAMECOUNT (hex); column 1 = NAME (matches WinForms
                // ImageTSAAnimeForm: count = atoh(at(sp,0)), name = at(sp,1)).
                uint count = U.atoh(U.at(sp, 0));
                if (count == 0) continue;

                string name = U.at(sp, 1);
                string ptrHex = U.ToHexString(pointer);

                for (uint i = 0; i < count; i++)
                {
                    uint addr = baseAddr + i * SIZE;
                    // Bounds-guard: stop this category at the first frame that would
                    // overrun the ROM (defends a corrupt/oversized FRAMECOUNT).
                    if (addr + SIZE > romLen) break;

                    string rowName = ptrHex + " " + name + " " + i;
                    result.Add(new AddrResult(addr, rowName, (uint)result.Count));
                }
            }

            return result;
        }
    }
}
