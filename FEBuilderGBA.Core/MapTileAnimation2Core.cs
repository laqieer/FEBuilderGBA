// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform helpers for the MapTileAnimation2 editor (#426, #524).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

            // Resolve each filter row to an "ANIME2 MapName" label via the
            // shared resolver (#952, #11) instead of the raw
            // "タイルアニメーション2 パレットアニメ:{plist}" PLIST-hex string.
            // Built once and reused across the loop (per-call local cache).
            // The cache already enumerated every map via MapSettingCore.MakeMapIDList,
            // so reuse cache.Maps for the filter scan instead of a second
            // MakeMapIDList allocation/scan (#954 review).
            var resolveCache = MapPListResolverCore.BuildCache(rom);

            var seen = new HashSet<uint>();
            foreach (var map in resolveCache.Maps)
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
                    // WinForms PlistToOffsetAddr (MapPointerForm.cs:425)
                    // calls rom.p32(slotAddr) which silently strips the
                    // 0x08000000 high bit. It accepts both GBA-pointer
                    // form (0x08800000) AND raw ROM offset form
                    // (0x00800000) - it just runs U.toOffset on whatever
                    // is stored. Treating non-pointer values as broken
                    // would incorrectly hide valid PLIST entries whose
                    // slot stores a raw offset (Copilot bot review on
                    // PR #535).
                    uint dataPtr = rom.u32(plistSlot);
                    if (dataPtr == 0)
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

                // Resolve the filter label to "ANIME2 MapName" via the shared
                // resolver (#952, #11) — the same enhancement #953 applied to
                // the MapPointer / MapChange editors. WinForms historically
                // showed "タイルアニメーション2 パレットアニメ:{plist}"; the
                // resolved map name is far more useful, and the lockstep golden
                // builder ListParityHelper.BuildMapTileAnimation2FilterList
                // copies this Display verbatim. The "(破損)" suffix is still
                // appended on a broken PLIST so the repair affordance stays
                // discoverable.
                string baseLabel = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.ANIMATION2, plist, resolveCache);
                string display = broken
                    ? baseLabel + R._("(破損)")
                    : baseLabel;
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

        // ====================================================================
        // BulkExport / BulkImport / ExpandEntryList / ExpandPaletteRowList
        // (#524) - Core extraction of the WinForms ImportAll / ExportAll /
        // InputFormRef.AppendBinaryData repoint plumbing so the Avalonia view
        // can drive bulk flows headlessly. The WF form keeps its existing
        // path (uses InputFormRef + the explicit-undo RecycleAddress
        // overloads); these helpers use the ambient-undo RecycleAddress
        // overloads (#524 WU1) so callers wrap them in ROM.BeginUndoScope
        // and every write records exactly once.
        // ====================================================================

        /// <summary>
        /// Export the 8-byte main animation entries rooted at
        /// <paramref name="baseAddr"/> to a tab-separated text file. Mirrors
        /// the WF <c>MapTileAnimation2Form.ExportAll</c> output format:
        /// header line plus one row per entry as
        /// <c>wait{TAB}startindex{TAB}R,G,B{TAB}R,G,B...</c>. Returns the
        /// empty string on success, or an error message describing the
        /// failure (e.g. unsafe base, unsafe palette pointer, IO error).
        /// </summary>
        public static string BulkExport(ROM rom, string filename, uint baseAddr, uint dataCount)
        {
            if (rom == null) return "ROM is null.";
            if (!U.isSafetyOffset(baseAddr, rom)) return "baseAddr is not a safe ROM offset.";
            if (string.IsNullOrEmpty(filename)) return "filename is empty.";

            try
            {
                var lines = new List<string>();
                // Header line - matches WF format byte-for-byte.
                lines.Add("//wait\tstartindex\tR,G,B Colors...");

                uint addr = baseAddr;
                for (uint i = 0; i < dataCount; i++, addr += 8)
                {
                    if (addr + 8 > (uint)rom.Data.Length) break;
                    uint p0 = rom.p32(addr + 0);
                    uint wait = rom.u8(addr + 4);
                    uint count = rom.u8(addr + 5);
                    uint startindex = rom.u8(addr + 6);
                    if (!U.isSafetyOffset(p0, rom)) continue;

                    var sb = new StringBuilder();
                    sb.Append(wait);
                    sb.Append('\t');
                    sb.Append(startindex);
                    uint paladdr = p0;
                    for (uint n = 0; n < count; n++, paladdr += 2)
                    {
                        if (paladdr + 2 > (uint)rom.Data.Length) break;
                        ushort pal = (ushort)rom.u16(paladdr);
                        var (r, g, b) = GbaToRgb(pal);
                        sb.Append('\t');
                        sb.Append(r);
                        sb.Append(',');
                        sb.Append(g);
                        sb.Append(',');
                        sb.Append(b);
                    }
                    lines.Add(sb.ToString());
                }

                File.WriteAllLines(filename, lines);
                return "";
            }
            catch (Exception ex)
            {
                return "BulkExport failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Import a tab-separated text file (produced by
        /// <see cref="BulkExport"/>) into the main animation entry table.
        /// The caller MUST open an ambient undo scope via
        /// <see cref="ROM.BeginUndoScope"/> before calling this method; the
        /// helper uses the ambient-undo <see cref="RecycleAddress"/> overloads
        /// so every ROM write records exactly once into the active UndoData.
        ///
        /// Mirrors WF <c>MapTileAnimation2Form.ImportAll</c> step-for-step
        /// including the malformed-line semantics: lines with fewer than 2
        /// tab-separated fields are silently skipped (WF parity, V3
        /// plan-review #4).
        ///
        /// <para>Parameters:</para>
        /// <para><c>rom</c> — target ROM. The method temporarily swaps
        /// <c>CoreState.ROM</c> for the duration so the <c>RecycleAddress</c>
        /// helpers (which read CoreState.ROM directly) mutate the supplied
        /// instance.</para>
        /// <para><c>filename</c> — TSV path produced by <see cref="BulkExport"/>.</para>
        /// <para><c>pointer</c> — the 4-byte slot that holds the entry-table
        /// pointer; the helper repoints it to the freshly-written table.</para>
        /// <para><c>baseAddr</c> — the entry-table base address before
        /// import. Used to enumerate existing entries for the recycle pool
        /// so the WF "free old palette blocks" behavior matches.</para>
        /// <para><c>dataCount</c> — number of existing entries at
        /// <paramref name="baseAddr"/>; the helper feeds them into the
        /// RecycleAddress pool exactly as WF does.</para>
        ///
        /// Returns empty string on success, an error message on failure.
        /// </summary>
        // Serialise the CoreState.ROM swap below so two concurrent BulkImport
        // calls cannot interleave their save/restore (Copilot bot review on
        // PR #634). CoreState.ROM is a static global, not thread-local, so
        // the swap inside BulkImport is not thread-safe on its own. The lock
        // narrows the race to a per-call serialisation - acceptable because
        // ROM editing is itself inherently single-writer.
        static readonly object _coreStateRomSwapLock = new object();

        public static string BulkImport(ROM rom, string filename, uint pointer, uint baseAddr, uint dataCount)
        {
            if (rom == null) return "ROM is null.";
            if (!U.isSafetyOffset(pointer, rom)) return "pointer is not a safe ROM offset.";
            if (string.IsNullOrEmpty(filename)) return "filename is empty.";
            if (!File.Exists(filename)) return "filename does not exist: " + filename;

            // Save & swap CoreState.ROM so RecycleAddress (which reads
            // CoreState.ROM internally) mutates the passed ROM. The lock
            // prevents two concurrent BulkImport calls from racing on the
            // global (Copilot bot review on PR #634).
            lock (_coreStateRomSwapLock)
            {
                var prevRom = CoreState.ROM;
                try
                {
                    CoreState.ROM = rom;
                    return BulkImportInner(rom, filename, pointer, baseAddr, dataCount);
                }
                finally
                {
                    CoreState.ROM = prevRom;
                }
            }
        }

        static string BulkImportInner(ROM rom, string filename, uint pointer, uint baseAddr, uint dataCount)
        {
            // Build the recycle pool from existing entries (each row's
            // palette block becomes a reusable region). Matches WF
            // MapTileAnimation2Form.ImportAll:474-503.
            var recycle = new List<Address>();
            if (U.isSafetyOffset(baseAddr, rom))
            {
                for (uint i = 0; i < dataCount; i++)
                {
                    uint addr = baseAddr + i * 8;
                    if (addr + 8 > (uint)rom.Data.Length) break;
                    uint p0 = rom.p32(addr + 0);
                    uint count = rom.u8(addr + 5);
                    if (!U.isSafetyOffset(p0, rom)) continue;
                    Address.AddPointer(recycle, addr + 0, count * 2, "", Address.DataTypeEnum.BIN);
                }
                // Also recycle the existing entry table itself.
                Address.AddAddress(recycle, baseAddr, dataCount * 8 + 8, 0,
                    "MapTileAnimation2.entryTable", Address.DataTypeEnum.BIN);
            }

            var ra = new RecycleAddress(recycle);

            // Parse lines and build entries.
            string[] lines;
            try { lines = File.ReadAllLines(filename); }
            catch (Exception ex) { return "BulkImport: failed to read file: " + ex.Message; }

            var writedata = new List<byte>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                line = U.ClipComment(line);
                if (line == "") continue;
                string[] sp = line.Split('\t');
                if (sp.Length < 2) continue; // WF parity (V3 plan-review #4) - skip malformed.

                uint wait = U.atoi(sp[0]);
                uint startindex = U.atoi(sp[1]);
                uint count = 0;

                var palByte = new List<byte>();
                for (int n = 2; n < sp.Length; n++, count++)
                {
                    string pal = sp[n];
                    string[] rgbarray = pal.Split(',');
                    int r = (int)U.atoi0x(U.at(rgbarray, 0));
                    int g = (int)U.atoi0x(U.at(rgbarray, 1));
                    int b = (int)U.atoi0x(U.at(rgbarray, 2));
                    ushort gba = RgbToGba((byte)r, (byte)g, (byte)b);
                    U.append_u16(palByte, gba);
                }

                uint newaddr = ra.WriteAmbient(palByte.ToArray());
                if (newaddr == U.NOT_FOUND)
                {
                    return string.Format(
                        "BulkImport: failed to write palette data at line {0} (file: {1}).",
                        i, filename);
                }

                U.append_u32(writedata, U.toPointer(newaddr));
                U.append_u8(writedata, wait);
                U.append_u8(writedata, count);
                U.append_u8(writedata, startindex);
                U.append_u8(writedata, 0);
            }

            // Terminator row.
            U.append_u32(writedata, 0);
            U.append_u8(writedata, 0);
            U.append_u8(writedata, 0);
            U.append_u8(writedata, 0);
            U.append_u8(writedata, 0);

            uint newpointer = ra.WriteAndWritePointerAmbient(pointer, writedata.ToArray());
            if (newpointer == U.NOT_FOUND)
            {
                return string.Format(
                    "BulkImport: failed to repoint entry table (file: {0}).", filename);
            }
            ra.BlackOutAmbient();
            return "";
        }

        /// <summary>
        /// Grow the main 8-byte entry table from <paramref name="oldCount"/>
        /// to <paramref name="newCount"/> rows. Allocates a fresh region in
        /// free space, copies the existing rows, fills the new rows with
        /// row-0 template clones (WF parity, V1 plan-review #2), and
        /// repoints <paramref name="pointerSlot"/> to the new base.
        ///
        /// The caller MUST open an ambient undo scope via
        /// <see cref="ROM.BeginUndoScope"/> so the ambient scope captures
        /// every write exactly once.
        ///
        /// Returns the new base ROM offset (not a GBA pointer), or
        /// <see cref="U.NOT_FOUND"/> on failure.
        /// </summary>
        public static uint ExpandEntryList(ROM rom, uint pointerSlot, uint oldBase, uint oldCount, uint newCount)
        {
            return ExpandTableCore(rom, pointerSlot, oldBase, oldCount, newCount, blockSize: 8);
        }

        /// <summary>
        /// Compatibility overload that opens an ambient undo scope around
        /// the supplied <paramref name="undo"/> (or reuses the active one
        /// if it already matches), then dispatches to the parameterless
        /// variant. Matches the two-overload pattern used by
        /// <see cref="ImageBattleBGCore.ExpandList(ROM, uint, uint, Undo.UndoData)"/>.
        /// </summary>
        public static uint ExpandEntryList(ROM rom, uint pointerSlot, uint oldBase, uint oldCount, uint newCount, Undo.UndoData undo)
        {
            if (undo == null) return U.NOT_FOUND;
            if (ROM.GetAmbientUndoData() == undo)
            {
                return ExpandEntryList(rom, pointerSlot, oldBase, oldCount, newCount);
            }
            using (ROM.BeginUndoScope(undo))
            {
                return ExpandEntryList(rom, pointerSlot, oldBase, oldCount, newCount);
            }
        }

        /// <summary>
        /// Grow the 2-byte palette sub-table from <paramref name="oldCount"/>
        /// colors to <paramref name="newCount"/> colors. Allocates a fresh
        /// region in free space, copies the existing colors, fills the new
        /// slots with row-0 template clones (the first color of the existing
        /// palette block), and repoints
        /// <paramref name="paletteDataPointerSlot"/> to the new base.
        ///
        /// The caller MUST open an ambient undo scope.
        /// </summary>
        public static uint ExpandPaletteRowList(ROM rom, uint paletteDataPointerSlot, uint oldBase, uint oldCount, uint newCount)
        {
            return ExpandTableCore(rom, paletteDataPointerSlot, oldBase, oldCount, newCount, blockSize: 2);
        }

        /// <summary>Explicit-undo overload (mirrors <see cref="ExpandEntryList(ROM, uint, uint, uint, uint, Undo.UndoData)"/>).</summary>
        public static uint ExpandPaletteRowList(ROM rom, uint paletteDataPointerSlot, uint oldBase, uint oldCount, uint newCount, Undo.UndoData undo)
        {
            if (undo == null) return U.NOT_FOUND;
            if (ROM.GetAmbientUndoData() == undo)
            {
                return ExpandPaletteRowList(rom, paletteDataPointerSlot, oldBase, oldCount, newCount);
            }
            using (ROM.BeginUndoScope(undo))
            {
                return ExpandPaletteRowList(rom, paletteDataPointerSlot, oldBase, oldCount, newCount);
            }
        }

        /// <summary>
        /// Internal shared implementation for both ExpandEntryList (8 bytes)
        /// and ExpandPaletteRowList (2 bytes). Uses row-0 template copy
        /// semantics per V1 plan-review #2.
        /// </summary>
        static uint ExpandTableCore(ROM rom, uint pointerSlot, uint oldBase, uint oldCount, uint newCount, uint blockSize)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (oldCount == 0) return U.NOT_FOUND;
            if (newCount <= oldCount) return U.NOT_FOUND;
            if (blockSize == 0) return U.NOT_FOUND;
            if (!U.isSafetyOffset(pointerSlot, rom)) return U.NOT_FOUND;
            if (pointerSlot + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;
            if (!U.isSafetyOffset(oldBase, rom)) return U.NOT_FOUND;
            if (oldBase + oldCount * blockSize > (uint)rom.Data.Length) return U.NOT_FOUND;

            // Allocate (newCount + 1) * blockSize so the terminator row
            // (all zeros) lives past the user-visible rows. Matches the WF
            // pattern used by MapEventUnitCore.ExpandUnitList.
            uint needSize = (newCount + 1) * blockSize;
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint newBase = rom.FindFreeSpace(searchStart, needSize);
            if (newBase == U.NOT_FOUND)
            {
                newBase = rom.FindFreeSpace(0x100u, needSize);
            }
            if (newBase == U.NOT_FOUND) return U.NOT_FOUND;

            // 1. Copy existing rows.
            byte[] oldRows = rom.getBinaryData(oldBase, oldCount * blockSize);
            rom.write_range(newBase, oldRows);

            // 2. Row-0 template clones for new rows.
            byte[] row0 = rom.getBinaryData(oldBase, blockSize);
            for (uint i = oldCount; i < newCount; i++)
            {
                rom.write_range(newBase + i * blockSize, row0);
            }

            // 3. Zero terminator row past newCount.
            byte[] terminator = new byte[blockSize];
            rom.write_range(newBase + newCount * blockSize, terminator);

            // 4. Repoint the slot.
            rom.write_p32(pointerSlot, newBase);
            return newBase;
        }
    }
}
