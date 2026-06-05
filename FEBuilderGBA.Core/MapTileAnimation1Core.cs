// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform helpers for the MapTileAnimation1 editor (#955, #957 W1c).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for map tile animation type 1 (tile-image
    /// animation), extracted from <c>WinForms MapTileAnimation1Form</c> so the
    /// Avalonia view can drive the editor without WinForms dependencies (#955).
    ///
    /// <para>The WF form builds a <b>filter list of anime1 PLISTs</b> referenced
    /// by any map setting (<see cref="BuildPlistList"/>, mirroring WF
    /// <c>MakeTileAnimation1</c>), then reads the SELECTED PLIST's resolved data
    /// table (<see cref="ScanEntries"/>, mirroring WF <c>Init</c>'s 8-byte
    /// record). This is the structural counterpart of
    /// <see cref="MapTileAnimation2Core.BuildPlistList"/> /
    /// <see cref="MapTileAnimation2Core.ScanEntries"/>; the only differences are
    /// the per-map PLIST field read (<c>anime1_plist</c> at <c>+9</c> instead of
    /// <c>anime2_plist</c> at <c>+10</c>), the PLIST type
    /// (<see cref="MapChangeCore.PlistType.ANIMATION"/> →
    /// <c>map_tileanime1_pointer</c>; ANIME1/ANIME2 share that base in vanilla
    /// ROMs and both resolve under the WF "ANIMATION" filter), and the entry
    /// record schema.</para>
    ///
    /// <para><b>Entry schema</b> (8 bytes per row, WF <c>Init</c> +
    /// <c>ExportAll</c>): <c>wait = u16@+0</c>, <c>length = u16@+2</c>,
    /// <c>imagePointer = p32@+4</c>. The termination predicate is
    /// <c>isPointer(u32(addr+4))</c> — the image pointer lives at <c>+4</c>, NOT
    /// <c>+0</c> (the inverse of anime2). The Avalonia VM already used this
    /// schema for the inner data fields; the gap (#955) was the MISSING PLIST
    /// filter and the WRONG base resolution (the old VM treated
    /// <c>map_tileanime1_pointer</c> — the PLIST TABLE — as the flat entry
    /// table).</para>
    /// </summary>
    public static class MapTileAnimation1Core
    {
        /// <summary>
        /// One row in the filter combo. Matches
        /// <see cref="MapTileAnimation2Core.PlistRow"/> field-for-field so the
        /// Avalonia view + golden builder can share the wiring shape.
        /// </summary>
        /// <param name="Plist">The map setting's <c>anime1_plist</c> value.</param>
        /// <param name="Addr">The ROM offset the PLIST resolves to, or
        /// <see cref="U.NOT_FOUND"/> when the PLIST table entry dereferences to
        /// zero or to an unsafe offset (<see cref="IsBroken"/> == true).</param>
        /// <param name="Display">Resolved "ANIME1 MapName" label via
        /// <see cref="MapPListResolverCore.ResolveLabel"/> (mirroring how anime2
        /// uses <see cref="MapChangeCore.PlistType.ANIMATION2"/>), with the
        /// WF "(破損)" suffix appended on a broken PLIST.</param>
        /// <param name="IsBroken">Set when the row dereferences to an unusable
        /// address — the row is kept so the user can repair it.</param>
        public record PlistRow(uint Plist, uint Addr, string Display, bool IsBroken);

        /// <summary>
        /// One entry in the main animation list (8 bytes per row in WF).
        /// </summary>
        /// <param name="Addr">Absolute ROM offset of the entry.</param>
        /// <param name="Wait">u16 at <c>addr+0</c> — animation interval.</param>
        /// <param name="Length">u16 at <c>addr+2</c> — image byte length.</param>
        /// <param name="P4">Raw u32 at <c>addr+4</c> (GBA image pointer; the
        /// termination predicate <c>isPointer(P4)</c> gates the row).</param>
        public record EntryRow(uint Addr, uint Wait, uint Length, uint P4);

        /// <summary>
        /// Walk the 8-byte entry table rooted at <paramref name="baseAddr"/>
        /// and return every row whose <c>P4</c> (image pointer at <c>+4</c>) is
        /// a valid GBA pointer. Stops at the first non-pointer row (mirrors the
        /// WF <c>InputFormRef</c> termination predicate
        /// <c>isPointer(u32(addr+4))</c>) or after <paramref name="maxRows"/>
        /// iterations.
        /// </summary>
        public static List<EntryRow> ScanEntries(ROM rom, uint baseAddr, int maxRows = 256)
        {
            var result = new List<EntryRow>();
            if (rom == null) return result;
            const uint blockSize = 8;
            for (int i = 0; i < maxRows; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // WF predicate: the image pointer at +4 must be a valid pointer.
                if (!U.isPointer(rom.u32(addr + 4))) break;
                uint wait = rom.u16(addr + 0);
                uint length = rom.u16(addr + 2);
                uint p4 = rom.u32(addr + 4);
                result.Add(new EntryRow(addr, wait, length, p4));
            }
            return result;
        }

        /// <summary>
        /// Build the filter list shown at the top of the WF editor (mirrors WF
        /// <c>MapTileAnimation1Form.MakeTileAnimation1</c>). Walks every valid
        /// map setting, reads <c>anime1_plist</c> at <c>+9</c> (skip zero,
        /// dedupe), and resolves each PLIST through the
        /// <c>map_tileanime1_pointer</c> table via
        /// <see cref="MapChangeCore.PlistToOffsetAddr"/> with
        /// <see cref="MapChangeCore.PlistType.ANIMATION"/>. Rows whose PLIST
        /// table entry dereferences to zero / an unsafe offset / past the
        /// version-specific limit are kept with <see cref="PlistRow.IsBroken"/>
        /// set so the editor can offer a repair affordance.
        /// </summary>
        public static List<PlistRow> BuildPlistList(ROM rom)
        {
            var result = new List<PlistRow>();
            if (rom?.RomInfo == null) return result;

            uint plistTablePtr = rom.RomInfo.map_tileanime1_pointer;
            if (plistTablePtr == 0) return result;
            uint plistTableBase = rom.p32(plistTablePtr);
            if (!U.isSafetyOffset(plistTableBase, rom)) return result;

            // Resolve each filter row to an "ANIME1 MapName" label via the
            // shared resolver (#952, #11) — the same resolver anime2 uses with
            // ANIMATION2. Built once and reused across the loop (per-call local
            // cache). The cache already enumerated every map via
            // MapSettingCore.MakeMapIDList, so reuse cache.Maps for the filter
            // scan instead of a second MakeMapIDList allocation/scan.
            var resolveCache = MapPListResolverCore.BuildCache(rom);

            var seen = new HashSet<uint>();
            foreach (var map in resolveCache.Maps)
            {
                // anime1_plist is the u8 at map setting +9.
                if (map.addr + 10 > (uint)rom.Data.Length) continue;
                uint plist = rom.u8(map.addr + 9);
                if (plist == 0) continue;
                if (!seen.Add(plist)) continue;

                // Resolve the PLIST to a data offset via the same WF
                // PlistToOffsetAddr(ANIMATION, plist) path the WF anime1 form
                // uses. The Core helper applies the version-specific PLIST
                // limit + safety guards and returns U.NOT_FOUND on a broken
                // entry — exactly WF's "(破損)" trigger.
                uint dataAddr = MapChangeCore.PlistToOffsetAddr(
                    rom, MapChangeCore.PlistType.ANIMATION, plist, out uint _);
                bool broken = dataAddr == U.NOT_FOUND;

                string baseLabel = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.ANIMATION, plist, resolveCache);
                string display = broken
                    ? baseLabel + R._("(破損)")
                    : baseLabel;
                result.Add(new PlistRow(plist, dataAddr, display, broken));
            }
            return result;
        }
    }
}
