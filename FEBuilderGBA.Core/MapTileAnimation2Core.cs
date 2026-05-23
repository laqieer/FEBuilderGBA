// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for map tile animation type 2 (palette animation),
    /// extracted from <c>WinForms MapTileAnimation2Form</c> so the Avalonia view
    /// can drive the editor without WinForms dependencies (gap-sweep #426).
    ///
    /// The WF form combines four responsibilities: (a) build the filter list of
    /// PLISTs referenced by any map setting, (b) iterate the 8-byte entries
    /// rooted at a PLIST's data pointer, (c) decode the per-row palette bytes
    /// into RGB, and (d) round-trip RGB through the 15-bit GBA format used by
    /// the BG palette engine. (a)-(d) all live here; the import/export and
    /// list-expansion plumbing remains WF-coupled (follow-up #524).
    /// </summary>
    public static class MapTileAnimation2Core
    {
        /// <summary>
        /// One row in the filter combo / address list. Matches the WF
        /// <c>AddrResult(addr, name, tag)</c> tuple but adds the explicit
        /// <c>isBroken</c> flag so the Avalonia view can render the row with a
        /// warning glyph instead of duplicating the WF "(broken)" string
        /// suffix.
        /// </summary>
        /// <param name="Plist">The map setting's <c>anime2_plist</c> value.</param>
        /// <param name="Addr">The ROM offset the PLIST resolves to, or
        /// <c>U.NOT_FOUND</c> if the PLIST table entry dereferences to zero
        /// or an unsafe offset (<c>IsBroken == true</c>).</param>
        /// <param name="Display">Human-readable label for the row, mirroring
        /// the WF <c>"タイルアニメーション2 パレットアニメ:{plist}"</c> format
        /// (English here: <c>"Tile Animation 2 Palette: 0x{plist:X2}"</c>).</param>
        /// <param name="IsBroken">Set when the row dereferences to an
        /// unusable address - the row is kept so the user can repair it.</param>
        public record PlistRow(uint Plist, uint Addr, string Display, bool IsBroken);

        /// <summary>
        /// One row in the palette sub-list. The WF view shows R/G/B sliders
        /// plus the encoded 15-bit GBA word; this record holds all three so
        /// the headless caller does not need to re-decode.
        /// </summary>
        /// <param name="Index">Row index (0-based).</param>
        /// <param name="Gba">Raw 15-bit GBA color word read from ROM.</param>
        /// <param name="R">Decoded red channel, top 5 bits left-shifted to 8
        /// bits (i.e. <c>(gba &amp; 0x1F) &lt;&lt; 3</c>).</param>
        /// <param name="G">Decoded green channel.</param>
        /// <param name="B">Decoded blue channel.</param>
        public record PaletteRow(int Index, ushort Gba, byte R, byte G, byte B);

        /// <summary>
        /// One entry in the main animation list (8 bytes per row in WF).
        /// </summary>
        /// <param name="Addr">Absolute ROM offset of the entry.</param>
        /// <param name="P0">Raw u32 at <c>addr+0</c> (GBA pointer).</param>
        /// <param name="Wait">u8 at <c>addr+4</c> - animation interval.</param>
        /// <param name="Count">u8 at <c>addr+5</c> - data count (number of
        /// palette rows).</param>
        /// <param name="StartIndex">u8 at <c>addr+6</c> - start palette
        /// index.</param>
        /// <param name="Padding">u8 at <c>addr+7</c> - opaque.</param>
        public record EntryRow(uint Addr, uint P0, uint Wait, uint Count, uint StartIndex, uint Padding);

        /// <summary>
        /// Encode an 8-bit RGB triple into the 15-bit GBA color word.
        /// Top 5 bits of each channel are kept, ordered <c>rrrrr gggggbbbbb</c>
        /// from LSB. Matches the WF <c>ImageUtil.ColorToGBARGB</c> formula
        /// <c>(r&gt;&gt;3) | ((g&gt;&gt;3)&lt;&lt;5) | ((b&gt;&gt;3)&lt;&lt;10)</c>.
        /// </summary>
        public static ushort RgbToGba(byte r, byte g, byte b)
        {
            int r5 = (r >> 3) & 0x1F;
            int g5 = (g >> 3) & 0x1F;
            int b5 = (b >> 3) & 0x1F;
            return (ushort)((b5 << 10) | (g5 << 5) | r5);
        }

        /// <summary>
        /// Decode a 15-bit GBA color word into an 8-bit RGB triple.
        /// Bits 0-4 = red, 5-9 = green, 10-14 = blue. Each channel is
        /// left-shifted by 3 to fill the high 5 bits of the byte.
        /// </summary>
        public static (byte r, byte g, byte b) GbaToRgb(ushort gba)
        {
            byte r = (byte)((gba & 0x1F) << 3);
            byte g = (byte)(((gba >> 5) & 0x1F) << 3);
            byte b = (byte)(((gba >> 10) & 0x1F) << 3);
            return (r, g, b);
        }

        /// <summary>
        /// Walk the 8-byte entry table rooted at <paramref name="baseAddr"/>
        /// and return every row whose <c>P0</c> is a valid GBA pointer. Stops
        /// at the first non-pointer row (mirrors the WF
        /// <c>InputFormRef</c> termination predicate
        /// <c>isPointer(u32(addr+0))</c>) or after <paramref name="maxRows"/>
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
                uint p0 = rom.u32(addr + 0);
                if (!U.isPointer(p0)) break;
                uint wait = rom.u8(addr + 4);
                uint count = rom.u8(addr + 5);
                uint startindex = rom.u8(addr + 6);
                uint padding = rom.u8(addr + 7);
                result.Add(new EntryRow(addr, p0, wait, count, startindex, padding));
            }
            return result;
        }

        /// <summary>
        /// Build the filter list shown at the top of the WF editor. Walks
        /// every valid map setting via
        /// <see cref="MapSettingCore.MakeMapIDList(ROM)"/>, reads
        /// <c>anime2_plist</c> at <c>+10</c> (skip zero, dedupe), and resolves
        /// each PLIST through the <c>map_tileanime2_pointer</c> table.
        /// Rows whose PLIST table entry dereferences to zero or to an unsafe
        /// offset are kept with <see cref="PlistRow.IsBroken"/> set to
        /// <c>true</c> so the editor can offer a repair affordance.
        /// </summary>
        public static List<PlistRow> BuildPlistList(ROM rom)
        {
            var result = new List<PlistRow>();
            if (rom?.RomInfo == null) return result;

            uint plistTablePtr = rom.RomInfo.map_tileanime2_pointer;
            if (plistTablePtr == 0) return result;
            uint plistTableBase = rom.p32(plistTablePtr);
            if (!U.isSafetyOffset(plistTableBase, rom)) return result;

            var seen = new HashSet<uint>();
            var maps = MapSettingCore.MakeMapIDList(rom);
            foreach (var map in maps)
            {
                if (map.addr + 11 > (uint)rom.Data.Length) continue;
                uint plist = rom.u8(map.addr + 10);
                if (plist == 0) continue;
                if (!seen.Add(plist)) continue;

                // Resolve the PLIST to a data offset via the WF
                // PlistToOffsetAddr path: entry slot = plistTableBase + plist*4
                // and the data offset is the dereferenced u32 (with the high
                // bit stripped).
                uint plistSlot = plistTableBase + plist * 4;
                bool broken = false;
                uint dataAddr;
                if (plistSlot + 4 > (uint)rom.Data.Length)
                {
                    dataAddr = U.NOT_FOUND;
                    broken = true;
                }
                else
                {
                    uint dataPtr = rom.u32(plistSlot);
                    if (dataPtr == 0)
                    {
                        dataAddr = U.NOT_FOUND;
                        broken = true;
                    }
                    else if (!U.isPointer(dataPtr))
                    {
                        dataAddr = U.NOT_FOUND;
                        broken = true;
                    }
                    else
                    {
                        dataAddr = U.toOffset(dataPtr);
                        if (!U.isSafetyOffset(dataAddr, rom))
                        {
                            dataAddr = U.NOT_FOUND;
                            broken = true;
                        }
                    }
                }

                string display = broken
                    ? string.Format("Tile Animation 2 Palette: 0x{0:X2} (broken)", plist)
                    : string.Format("Tile Animation 2 Palette: 0x{0:X2}", plist);
                result.Add(new PlistRow(plist, dataAddr, display, broken));
            }
            return result;
        }

        /// <summary>
        /// Decode a palette data block into the per-row R/G/B triples used
        /// by the Avalonia palette sub-list. Reads <c>2 * count</c> bytes from
        /// <c>U.toOffset(dataPointer)</c>. Returns an empty list if the
        /// pointer cannot be resolved to a safe ROM offset.
        /// </summary>
        public static List<PaletteRow> BuildPaletteList(ROM rom, uint dataPointer, uint count)
        {
            var result = new List<PaletteRow>();
            if (rom == null) return result;
            if (dataPointer == 0) return result;
            uint offset = U.toOffset(dataPointer);
            if (!U.isSafetyOffset(offset, rom)) return result;
            for (uint i = 0; i < count; i++)
            {
                uint addr = offset + i * 2;
                if (addr + 2 > (uint)rom.Data.Length) break;
                ushort gba = (ushort)rom.u16(addr);
                var (r, g, b) = GbaToRgb(gba);
                result.Add(new PaletteRow((int)i, gba, r, g, b));
            }
            return result;
        }
    }
}
