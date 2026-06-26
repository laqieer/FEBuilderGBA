using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    // ======================================================================
    // World Map Road (Path) editor — Core (#1185). FE8-ONLY.
    //
    // Ports the data + render half of WF WorldMapPathEditorForm.cs (the
    // interactive FE8 world-map road painter). The road-pointer table is at
    // worldmap_road_pointer (FE8U 0xC2C0), 12-byte entries:
    //   +0  path-data pointer
    //   +4  base-point id #1   (label only)
    //   +5  base-point id #2   (label only)
    //
    // Packed path data (LoadPathLow / WritePathData):
    //   repeated header [x8, y8, count, 0x01] then `count` x [tileRow, flag]
    //   pairs, terminated by [0xFF, 0, 0, 0]. flag 0/4/8/0xC = no-flip /
    //   H-flip / V-flip / HV-flip. A chip's world pixel coord is
    //   (x8*8 + ix*8, y8*8); its source road tile is row `tileRow` of the
    //   8x120 road strip (worldmap_road_tile_pointer), flipped per `flag`.
    //
    // CORRECTNESS FIX vs WF (Copilot plan review #1): WF's WritePathData
    // groups ALL chips on the same Y into one header and writes the run X as
    // contiguous (ix*8). A user-painted row with a GAP (e.g. x=0 and x=16)
    // would reload as x=0 and x=8. PackPath here splits each Y-row into
    // CONTIGUOUS WorldX runs, so non-contiguous paints round-trip exactly.
    // ======================================================================

    /// <summary>
    /// One painted road chip. Pixel coordinates: <see cref="WorldX"/>/<see cref="WorldY"/>
    /// are the chip's top-left on the world map (multiples of 8);
    /// <see cref="PathX"/>/8 is the flip variant (0=none,1=H,2=V,3=HV) and
    /// <see cref="PathY"/>/8 is the source row in the road strip.
    /// </summary>
    public struct PathChip
    {
        public int WorldX;
        public int WorldY;
        public int PathX;
        public int PathY;

        public PathChip(int worldX, int worldY, int pathX, int pathY)
        {
            WorldX = worldX;
            WorldY = worldY;
            PathX = pathX;
            PathY = pathY;
        }
    }

    /// <summary>
    /// READ-ONLY decode + PURE pack + ROM-MUTATING write + render helpers for
    /// the FE8 World Map Road (Path) editor (#1185). All methods never throw
    /// and guard every ROM access. FE8-only (FE6/FE7 reject / return empty).
    /// </summary>
    public static class WorldMapPathCore
    {
        // FE8 = ROMFEINFO.version 8 (matches ImageWorldMapCore's FE8-only gate).
        const int VERSION_FE8 = 8;

        // 12-byte road-pointer table entry.
        const int ENTRY_SIZE = 12;
        // Max table rows scanned (matches WF's 0x20 default + expansion headroom).
        const int MAX_ENTRIES = 0x200;

        // The road strip is 1 tile wide x 15 tiles tall = 8x120 px (the same
        // dims ImageWorldMapCore.TryRenderRoad uses). PathY/8 must be 0..14.
        const int ROAD_STRIP_TILES_Y = 15;
        // The chip palette is 5 columns (col0..3 = the 4 flip variants, col4 =
        // the erase sentinel). A STORED chip's PathX/8 must be 0..3 (the erase
        // column is a painter-only sentinel and must NEVER reach a chip).
        const int FLIP_VARIANTS = 4;
        const int CHIP_PALETTE_COLS = 5;

        // WF DrawPath打ち切り guard — an entry claiming >= 200 chips in one row
        // is corrupt; stop decoding (do NOT throw).
        const int MAX_CHIPS_PER_ROW = 200;

        // The header x8 byte (offset +0) of 0xFF is the TERMINATOR sentinel, so a
        // run may NOT start at tile X 0xFF — the max addressable X tile is 0xFE.
        const int MAX_X_TILE = 0xFF; // x8/8 must be < this (0..0xFE)

        // ==================================================================
        // List
        // ==================================================================

        /// <summary>
        /// Build the road/path list (port of WF <c>WorldMapPathForm.Init</c>):
        /// iterate the 12-byte table while entry <c>+0</c> is a pointer, label
        /// each <c>"&lt;idx&gt; &lt;pt1&gt; -&gt; &lt;pt2&gt;"</c> from the two
        /// base-point ids at <c>+4</c>/<c>+5</c>. The path id is carried in
        /// <see cref="AddrResult.tag"/> (stable under list filtering). Bounded
        /// scan; returns empty on null / non-FE8 / unresolved table. Never throws.
        /// </summary>
        public static List<AddrResult> MakePathList(ROM rom)
        {
            var list = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null || rom.Data == null) return list;
            if (rom.RomInfo.version != VERSION_FE8) return list;

            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return list;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return list;

            for (uint i = 0; i < MAX_ENTRIES; i++)
            {
                uint addr = baseAddr + i * ENTRY_SIZE;
                if (!IsRegionSafe(rom, addr, ENTRY_SIZE)) break;
                // Terminate on the first entry whose +0 path pointer is not a
                // pointer (matches WF's IsPointer列挙 condition).
                if (!U.isPointer(rom.u32(addr + 0))) break;

                uint pt1 = rom.u8(addr + 4);
                uint pt2 = rom.u8(addr + 5);
                string name = U.ToHexString(i) + " "
                    + GetPointName(rom, pt1) + " -> " + GetPointName(rom, pt2);
                list.Add(new AddrResult(addr, name, i));
            }
            return list;
        }

        /// <summary>
        /// Resolve a base-point id to its name text (the point table's name
        /// text-id at <c>+28</c>). Returns the hex id on any miss. Never throws.
        /// </summary>
        static string GetPointName(ROM rom, uint pointId)
        {
            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return U.ToHexString(pointId);
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.ToHexString(pointId);

            uint addr = baseAddr + pointId * 32;
            if (!IsRegionSafe(rom, addr, 32)) return U.ToHexString(pointId);
            uint nameTextId = rom.u16(addr + 28);
            if (nameTextId == 0) return U.ToHexString(pointId);
            string n = NameResolver.GetTextById(nameTextId);
            return string.IsNullOrEmpty(n) ? U.ToHexString(pointId) : n;
        }

        // ==================================================================
        // Decode (load)
        // ==================================================================

        /// <summary>
        /// Resolve the path-data offset for entry <paramref name="pathId"/>
        /// (entry <c>+0</c>). Returns false on any guard failure. READ-ONLY.
        /// </summary>
        public static bool GetPathDataOffset(ROM rom, int pathId, out uint dataOffset)
        {
            dataOffset = 0;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != VERSION_FE8) return false;
            if (pathId < 0) return false;

            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return false;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return false;

            uint entry = baseAddr + (uint)pathId * ENTRY_SIZE;
            if (!IsRegionSafe(rom, entry, ENTRY_SIZE)) return false;
            uint encoded = rom.u32(entry + 0);
            if (!U.isPointer(encoded)) return false;
            uint off = U.toOffset(encoded);
            if (!U.isSafetyOffset(off, rom)) return false;
            dataOffset = off;
            return true;
        }

        /// <summary>
        /// Decode the packed path data for entry <paramref name="pathId"/> into
        /// a flat <see cref="PathChip"/> list (port of WF
        /// <c>LoadPathLow</c> + the <c>DrawPath</c> safety guards). READ-ONLY,
        /// never throws, guards every read. Returns an empty list on
        /// null / non-FE8 / out-of-bounds / null-pointer / corrupt data.
        ///
        /// <para><b>All-or-nothing</b> (Copilot PR #1228 review #1): a clean
        /// <c>0xFF</c> terminator yields the full decoded list; ANY mid-decode
        /// corruption (out-of-bounds header/pair, <c>count &gt;= 200</c>) yields
        /// an EMPTY list — a partial list must NOT reach the editor, since
        /// writing it back would silently TRUNCATE the road.</para>
        /// </summary>
        public static List<PathChip> LoadPath(ROM rom, int pathId)
        {
            if (!GetPathDataOffset(rom, pathId, out uint p)) return new List<PathChip>();

            // WF treats u32(p)==0 as "no road data" (the first header byte is
            // always >=1 for a real path).
            if (!IsRegionSafe(rom, p, 4)) return new List<PathChip>();
            if (rom.u32(p) == 0) return new List<PathChip>();

            var list = new List<PathChip>();
            while (true)
            {
                if (!IsRegionSafe(rom, p, 4)) return new List<PathChip>(); // header OOB -> corrupt
                uint x8 = rom.u8(p + 0);
                uint y8 = rom.u8(p + 1);
                uint count = rom.u8(p + 2);
                // p + 3 (the constant 0x01) is unused on read.

                if (x8 == 0xFF) return list;                  // clean terminator -> full list
                if (count >= MAX_CHIPS_PER_ROW) return new List<PathChip>(); // corrupt -> empty

                p += 4;
                for (uint ix = 0; ix < count; ix++)
                {
                    if (!IsRegionSafe(rom, p, 2)) return new List<PathChip>(); // pair OOB -> corrupt
                    uint tile = rom.u8(p + 0);
                    uint flag = rom.u8(p + 1);
                    p += 2;

                    var chip = new PathChip
                    {
                        WorldX = (int)(x8 * 8 + ix * 8),
                        WorldY = (int)(y8 * 8),
                        PathY = (int)tile * 8,
                        PathX = FlagToPathX(flag),
                    };
                    list.Add(chip);
                }
            }
        }

        // flag (stored) -> PathX pixel (PathX/8 = flip variant).
        static int FlagToPathX(uint flag)
        {
            switch (flag)
            {
                case 4: return 1 * 8;
                case 8: return 2 * 8;
                case 0xC: return 3 * 8;
                default: return 0;
            }
        }

        // PathX/8 (flip variant 0..3) -> stored flag.
        static uint PathXToFlag(int pathX)
        {
            switch (pathX / 8)
            {
                case 1: return 0x4;
                case 2: return 0x8;
                case 3: return 0xC;
                default: return 0x0;
            }
        }

        // ==================================================================
        // Pack (pure)
        // ==================================================================

        /// <summary>
        /// PURE port of WF <c>WritePathData</c>'s packer, with the
        /// contiguous-run fix (Copilot plan review #1). Sorts the chips by
        /// (WorldY, WorldX), then emits one <c>[x8,y8,count,1]</c> header per
        /// CONTIGUOUS WorldX run on a row (a new header starts whenever the X
        /// gap to the previous chip is not exactly 8), each followed by
        /// <c>count</c> x <c>[tileRow, flag]</c> pairs, ending with the
        /// <c>[0xFF,0,0,0]</c> terminator.
        ///
        /// <para><b>Validation</b> (zero-output on any violation —
        /// <paramref name="error"/> non-empty): every chip must have
        /// <c>WorldX</c>/<c>WorldY</c> non-negative multiples of 8 with
        /// <c>X/8 &lt;= 0xFE</c> (0xFF is the terminator sentinel),
        /// <c>Y/8 &lt;= 255</c>; <c>PathX/8</c> in 0..3 (the erase column 4 is a
        /// painter sentinel and must never be stored); <c>PathY/8</c> in 0..14
        /// (the road strip rows). A contiguous run longer than
        /// <c>MAX_CHIPS_PER_ROW-1</c> is split into multiple headers so the
        /// stored <c>count</c> always round-trips through <see cref="LoadPath"/>.</para>
        /// </summary>
        /// <returns>The packed bytes, or <c>null</c> with a non-empty
        /// <paramref name="error"/> on a validation failure.</returns>
        public static byte[] PackPath(IReadOnlyList<PathChip> chips, out string error)
        {
            error = "";
            if (chips == null) { error = R.Error("Path chip list is null."); return null; }

            // Validate every chip BEFORE producing any output. User-facing error
            // strings go through R.Error so the Avalonia ShowError surfaces a
            // localized message (Copilot PR #1228 review — ShowError does NOT
            // auto-translate).
            foreach (var c in chips)
            {
                if (c.WorldX < 0 || c.WorldY < 0 || (c.WorldX % 8) != 0 || (c.WorldY % 8) != 0)
                { error = R.Error("Chip world coordinates must be non-negative multiples of 8."); return null; }
                // X/8 == 0xFF would write a header byte LoadPath reads as the
                // TERMINATOR (silently dropping that chip + everything after), so
                // the X tile cap is 0xFE (Copilot PR #1228 re-review). The Y tile
                // is never the terminator byte (offset +1), so it caps at 255.
                if (c.WorldX / 8 >= MAX_X_TILE || c.WorldY / 8 > 255)
                { error = R.Error("Chip world coordinates exceed the addressable tile range."); return null; }
                if (c.PathX < 0 || (c.PathX % 8) != 0 || c.PathX / 8 >= FLIP_VARIANTS)
                { error = R.Error("Chip flip variant is out of range (must be 0..3; the erase column cannot be stored)."); return null; }
                if (c.PathY < 0 || (c.PathY % 8) != 0 || c.PathY / 8 >= ROAD_STRIP_TILES_Y)
                { error = R.Error("Chip road row is out of range (must be 0..14)."); return null; }
            }

            // Stable sort by (Y, X).
            var sorted = new List<PathChip>(chips);
            sorted.Sort((a, b) =>
            {
                if (a.WorldY != b.WorldY) return a.WorldY < b.WorldY ? -1 : 1;
                if (a.WorldX != b.WorldX) return a.WorldX < b.WorldX ? -1 : 1;
                return 0;
            });

            var data = new List<byte>();
            int i = 0;
            while (i < sorted.Count)
            {
                int rowY = sorted[i].WorldY;
                int runStartX = sorted[i].WorldX;

                // Extend the run while the row is the same AND each next chip is
                // exactly 8 px to the right of the previous (contiguous). A gap
                // (or a duplicate X) starts a NEW header so X reconstructs
                // exactly on reload.
                //
                // CAP the run at MAX_CHIPS_PER_ROW-1 (Copilot PR #1228 review #2):
                // the stored `count` is a u8 AND LoadPath rejects count>=200 as
                // corrupt. A longer contiguous run is SPLIT into multiple headers
                // (the next header's runStartX = prevX+8, so the reload still
                // reconstructs every chip) — never a wrapped/unreadable count.
                int n = i + 1;
                int prevX = runStartX;
                while (n < sorted.Count
                       && (n - i) < MAX_CHIPS_PER_ROW - 1
                       && sorted[n].WorldY == rowY
                       && sorted[n].WorldX == prevX + 8)
                {
                    prevX = sorted[n].WorldX;
                    n++;
                }

                int runLength = n - i;
                int startXTile = runStartX / 8;
                // Defensive emit-time guard (Copilot PR #1228 re-review): a header
                // whose x8 == 0xFF reads as the terminator, and a count >= 200
                // reads as corrupt — neither round-trips through LoadPath. The
                // per-chip validation + run cap already prevent both; assert it
                // here so PackPath NEVER emits a non-round-tripping stream.
                if (startXTile >= MAX_X_TILE || runLength >= MAX_CHIPS_PER_ROW)
                {
                    error = R.Error("Path run cannot be encoded (start tile or run length out of range).");
                    return null;
                }

                U.append_u8(data, (uint)startXTile);
                U.append_u8(data, (uint)(rowY / 8));
                U.append_u8(data, (uint)runLength); // contiguous count
                U.append_u8(data, 1);               // always 1

                for (; i < n; i++)
                {
                    U.append_u8(data, (uint)(sorted[i].PathY / 8));
                    U.append_u8(data, PathXToFlag(sorted[i].PathX));
                }
            }

            // Terminator.
            U.append_u8(data, 0xFF);
            U.append_u8(data, 0x0);
            U.append_u8(data, 0x0);
            U.append_u8(data, 0x0);
            return data.ToArray();
        }

        // ==================================================================
        // File export / import (*.road.bin) — #1458
        //
        // Mirrors WF WorldMapPathEditorForm.SaveASbutton_Click /
        // LoadButton_Click. Save exports the RAW packed road stream straight
        // from ROM (NOT a re-pack of the editor buffer), so a non-canonical-
        // but-loadable stream round-trips byte-for-byte. Load decodes the
        // FILE bytes into a chip buffer (the editor then Writes them to ROM
        // through the existing undo-tracked Write path).
        // ==================================================================

        /// <summary>
        /// Export the RAW packed road stream at <paramref name="dataOffset"/>
        /// (a ROM file offset) into a <c>.road.bin</c> byte array — a faithful
        /// port of WF <c>SaveASbutton_Click</c>
        /// (<c>getBinaryData(addr, CalcPathDataLength(addr))</c>). The length is
        /// measured through the <c>0xFF</c> terminator by the shared verbatim
        /// walker <see cref="RebuildProducerCore.CalcPathDataLength(ROM,uint)"/>,
        /// so the exported bytes are byte-for-byte identical to the ROM stream
        /// for ANY loadable path — including non-canonical streams (a header
        /// byte 3 that is not <c>1</c>, unusual run segmentation, otherwise-
        /// ignored bytes). It does NOT re-pack / canonicalize / export unsaved
        /// buffer edits. READ-ONLY; never throws; guards every read.
        /// </summary>
        /// <returns>The raw stream bytes, or <c>null</c> with a non-empty
        /// <paramref name="error"/> on null ROM / unsafe offset / empty stream.</returns>
        public static byte[] ExportPathBinFromRom(ROM rom, uint dataOffset, out string error)
        {
            error = "";
            if (rom == null || rom.Data == null) { error = R.Error("ROM not loaded."); return null; }
            if (!IsRegionSafe(rom, dataOffset, 4)) { error = R.Error("Road data offset is out of range."); return null; }

            uint length = RebuildProducerCore.CalcPathDataLength(rom, dataOffset);
            if (length == 0) { error = R.Error("No road data to export at this address."); return null; }
            // Defensive: the walker should never exceed ROM bounds (it stops on
            // an unsafe next header), but re-validate the full span before the
            // copy so a corrupt length can never read past the end.
            if (!IsRegionSafe(rom, dataOffset, (int)length)) { error = R.Error("Road data length is out of range."); return null; }

            return rom.getBinaryData(dataOffset, length);
        }

        /// <summary>
        /// Decode a <c>.road.bin</c> FILE buffer (read from offset 0) into a flat
        /// <see cref="PathChip"/> list — a buffer port of WF
        /// <c>LoadPathLow(bin, 0)</c>, reusing the SAME all-or-nothing corruption
        /// guards as <see cref="LoadPath"/>.
        ///
        /// <para><b>Safety divergence from WF</b> (documented, deliberate): WF
        /// <c>LoadPathLow</c> is permissive/partial on a malformed file (it keeps
        /// whatever it decoded before the bad byte). Here, a clean <c>0xFF</c>
        /// terminator yields the full list; ANY mid-decode corruption (out-of-
        /// bounds header/pair, <c>count &gt;= 200</c>, or NO terminator before EOF)
        /// yields <c>null</c> + a non-empty <paramref name="error"/>, so a
        /// truncated/corrupt file can NEVER silently reach the editor and be
        /// written back truncated. READ-ONLY; never throws.</para>
        /// </summary>
        /// <returns>The decoded chip list, or <c>null</c> with a non-empty
        /// <paramref name="error"/> on any corruption.</returns>
        public static List<PathChip> DecodePathBin(byte[] bin, out string error)
        {
            error = "";
            if (bin == null) { error = R.Error("Road file is empty."); return null; }
            // A real stream is at least the 4-byte terminator.
            if (bin.Length < 4) { error = R.Error("Road file is too short."); return null; }

            var list = new List<PathChip>();
            uint p = 0;
            while (true)
            {
                if (p + 4 > (uint)bin.Length) { error = R.Error("Road file is malformed (missing terminator)."); return null; }
                uint x8 = U.u8(bin, p + 0);
                uint y8 = U.u8(bin, p + 1);
                uint count = U.u8(bin, p + 2);
                // p + 3 (the constant 0x01 in canonical data) is unused on read.

                if (x8 == 0xFF) return list;                  // clean terminator -> full list
                if (count >= MAX_CHIPS_PER_ROW) { error = R.Error("Road file is corrupt (run count out of range)."); return null; }

                p += 4;
                for (uint ix = 0; ix < count; ix++)
                {
                    if (p + 2 > (uint)bin.Length) { error = R.Error("Road file is truncated."); return null; }
                    uint tile = U.u8(bin, p + 0);
                    uint flag = U.u8(bin, p + 1);
                    p += 2;

                    list.Add(new PathChip
                    {
                        WorldX = (int)(x8 * 8 + ix * 8),
                        WorldY = (int)(y8 * 8),
                        PathY = (int)tile * 8,
                        PathX = FlagToPathX(flag),
                    });
                }
            }
        }

        // ==================================================================
        // Write (ROM-mutating)
        // ==================================================================

        /// <summary>
        /// Pack <paramref name="chips"/> and write the road data for entry
        /// <paramref name="pathId"/> to ROM free space, repointing the entry
        /// <c>+0</c> slot (the inverse of <see cref="LoadPath"/>). FE8-only.
        ///
        /// <para><b>Validate-ALL-before-mutate</b> + a LAZY
        /// <c>rom.Data.Clone()</c> snapshot taken AFTER validation/packing but
        /// BEFORE the first write, with a length-aware byte-identical fault
        /// restore (#885/#923): a FAILED write mutates ZERO bytes. All writes
        /// go through the no-undoData overloads so they land in the caller's
        /// ambient <c>ROM.BeginUndoScope</c> (ambient undo only).</para>
        ///
        /// <para><b>WF parity gap:</b> unlike WF <c>InputFormRef.WriteBinaryData</c>
        /// (which length-calculates + recycles the OLD region), this appends
        /// the new data + repoints WITHOUT recycling the old bytes — the same
        /// simplification the #1000 world-map strip imports made. The old road
        /// bytes become unreferenced free-space candidates on the next scan.</para>
        /// </summary>
        /// <returns>Empty string on success; a non-empty user-facing error
        /// (with ZERO ROM mutation) on any failure. Never throws.</returns>
        public static string WritePath(ROM rom, int pathId, IReadOnlyList<PathChip> chips)
        {
            // User-facing error strings go through R.Error so the Avalonia
            // ShowError surfaces a localized message (Copilot PR #1228 review —
            // ShowError does NOT auto-translate).
            if (rom == null || rom.RomInfo == null || rom.Data == null) return R.Error("ROM not loaded.");
            if (rom.RomInfo.version != VERSION_FE8) return R.Error("World map roads are FE8-only.");
            if (chips == null) return R.Error("Path chip list is null.");

            // Resolve the entry slot to repoint (validate before mutate).
            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return R.Error("World map road table pointer is out of range.");
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return R.Error("World map road table is out of range.");
            if (pathId < 0) return R.Error("Invalid path id.");
            uint entry = baseAddr + (uint)pathId * ENTRY_SIZE;
            if (!IsRegionSafe(rom, entry, ENTRY_SIZE)) return R.Error("World map road entry is out of range.");

            // Pack (validates every chip; zero-output on any violation). packError
            // is already localized (PackPath wraps it in R.Error).
            byte[] data = PackPath(chips, out string packError);
            if (data == null) return packError;

            // Lazy snapshot: AFTER validation/packing, BEFORE the first write.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                uint newAddr = ImageImportCore.WriteRawToROM(rom, data, entry + 0);
                if (newAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R.Error("Failed to write road data. Check ROM free space.");
                }
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R.Error("World map road write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore (#885/#923): a free-space
        /// resize-append can GROW rom.Data, so down-resize to the snapshot
        /// length BEFORE the in-place copy.
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }

        // ==================================================================
        // Render
        // ==================================================================

        /// <summary>
        /// Render the road-chip palette (port of WF <c>MakePATHCHIPLIST</c>): a
        /// 5-column strip — col0 = the road strip as-is, col1 = H-flip,
        /// col2 = V-flip, col3 = HV-flip, col4 = blank (the erase column).
        /// 40x120 px. Returns <c>null</c> on any failure. READ-ONLY. FE8-only
        /// (Copilot PR #1228 re-review): <see cref="ImageWorldMapCore.TryRenderRoad"/>
        /// is version-agnostic, so gate here to honor the FE8-only contract.
        /// </summary>
        /// <param name="cols">The column count (always 5 on success).</param>
        public static IImage TryRenderChipPalette(ROM rom, out int cols)
        {
            cols = 0;
            if (rom == null || rom.RomInfo == null || CoreState.ImageService == null) return null;
            if (rom.RomInfo.version != VERSION_FE8) return null;

            IImage road = ImageWorldMapCore.TryRenderRoad(rom);
            if (road == null) return null;
            try
            {
                int tileH = road.Height;            // 120
                int tileW = road.Width;             // 8
                if (tileW <= 0 || tileH <= 0) return null;

                if (!ToRgba(road, out byte[] roadRgba, out int rw, out int rh)) return null;

                int outW = tileW * CHIP_PALETTE_COLS; // 40
                int outH = tileH;                     // 120
                byte[] outRgba = new byte[outW * outH * 4];

                // For each 8x8 source row block, blit the 4 flip variants.
                for (int y = 0; y < tileH; y += 8)
                {
                    BlitChip(outRgba, outW, outH, roadRgba, rw, rh, 0, y, 0 * 8, y, false, false);
                    BlitChip(outRgba, outW, outH, roadRgba, rw, rh, 0, y, 1 * 8, y, true, false);
                    BlitChip(outRgba, outW, outH, roadRgba, rw, rh, 0, y, 2 * 8, y, false, true);
                    BlitChip(outRgba, outW, outH, roadRgba, rw, rh, 0, y, 3 * 8, y, true, true);
                    // col4 (erase) intentionally left blank/transparent.
                }

                var img = CoreState.ImageService.CreateImage(outW, outH);
                img.SetPixelData(outRgba);
                cols = CHIP_PALETTE_COLS;
                return img;
            }
            finally
            {
                road.Dispose();
            }
        }

        /// <summary>
        /// Render the FE8 world map with the current <paramref name="chips"/>
        /// drawn as a road overlay, plus a base-point endpoint marker at each
        /// world-map point (port of WF <c>MakeWorldMap</c>). Background via
        /// <see cref="ImageWorldMapCore.TryRenderMainFieldMap"/>, road tiles via
        /// <see cref="ImageWorldMapCore.TryRenderRoad"/>. Returns <c>null</c> on
        /// any failure. READ-ONLY.
        ///
        /// <para><b>Scope cut (documented):</b> base points are drawn as simple
        /// endpoint MARKERS (the road-editing endpoint context), NOT the full
        /// per-icon sprites of WF <c>DrawWorldMapIcon</c> (a large
        /// icon-id-&gt;strip-region mapping). The road overlay — the actual
        /// editing target — is faithful.</para>
        /// </summary>
        public static IImage TryRenderPathComposite(ROM rom, IReadOnlyList<PathChip> chips)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (rom.RomInfo.version != VERSION_FE8) return null;

            IImage bg = ImageWorldMapCore.TryRenderMainFieldMap(rom);
            if (bg == null) return null;

            IImage road = null;
            try
            {
                // Background is already RGBA (ByteToImage16TilePaletteMap).
                if (!ToRgba(bg, out byte[] canvas, out int w, out int h)) return null;

                if (chips != null && chips.Count > 0)
                {
                    road = ImageWorldMapCore.TryRenderRoad(rom);
                    if (road != null && ToRgba(road, out byte[] roadRgba, out int rw, out int rh))
                    {
                        foreach (var c in chips)
                        {
                            int variant = c.PathX / 8;
                            bool flipX = variant == 1 || variant == 3;
                            bool flipY = variant == 2 || variant == 3;
                            BlitChip(canvas, w, h, roadRgba, rw, rh,
                                0, c.PathY, c.WorldX, c.WorldY, flipX, flipY);
                        }
                    }
                }

                // Base-point endpoint markers (scope-cut: markers, not sprites).
                DrawBasePointMarkers(rom, canvas, w, h);

                var img = CoreState.ImageService.CreateImage(w, h);
                img.SetPixelData(canvas);
                return img;
            }
            finally
            {
                bg.Dispose();
                road?.Dispose();
            }
        }

        /// <summary>
        /// Overlay a small endpoint marker at each world-map point's (X,Y)
        /// (read from the point table at <c>+24</c>/<c>+26</c>). A 6x6 filled
        /// red square centered on the point — the road-editing endpoint
        /// context. Never throws; silently skips on any guard failure.
        /// </summary>
        static void DrawBasePointMarkers(ROM rom, byte[] canvas, int w, int h)
        {
            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            for (uint i = 0; i < MAX_ENTRIES; i++)
            {
                uint addr = baseAddr + i * 32;
                if (!IsRegionSafe(rom, addr, 32)) break;
                // Termination: the three shop pointers must be pointer-or-null
                // (same guard the point editor uses).
                if (!U.isPointerOrNULL(rom.u32(addr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 16))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 20))) break;

                int px = (int)rom.u16(addr + 24);
                int py = (int)rom.u16(addr + 26);
                FillMarker(canvas, w, h, px, py, 3);
            }
        }

        // 7x7 filled red marker centered on (cx, cy).
        static void FillMarker(byte[] canvas, int w, int h, int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= w) continue;
                    int di = (y * w + x) * 4;
                    if (di + 3 >= canvas.Length) continue;
                    canvas[di + 0] = 255; // R
                    canvas[di + 1] = 0;   // G
                    canvas[di + 2] = 0;   // B
                    canvas[di + 3] = 255; // A
                }
            }
        }

        /// <summary>
        /// Blit an 8x8 source tile from <paramref name="src"/> (RGBA) at
        /// (<paramref name="srcX"/>, <paramref name="srcY"/>) onto
        /// <paramref name="dst"/> (RGBA) at (<paramref name="dstX"/>,
        /// <paramref name="dstY"/>), optionally flipped. Fully-transparent
        /// source pixels (alpha 0) are skipped so the underlying map shows
        /// through (matches the index-0-transparent convention).
        /// </summary>
        static void BlitChip(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH,
            int srcX, int srcY, int dstX, int dstY, bool flipX, bool flipY)
        {
            for (int ty = 0; ty < 8; ty++)
            {
                int sy = srcY + (flipY ? (7 - ty) : ty);
                if (sy < 0 || sy >= srcH) continue;
                int dy = dstY + ty;
                if (dy < 0 || dy >= dstH) continue;
                for (int tx = 0; tx < 8; tx++)
                {
                    int sx = srcX + (flipX ? (7 - tx) : tx);
                    if (sx < 0 || sx >= srcW) continue;
                    int dx = dstX + tx;
                    if (dx < 0 || dx >= dstW) continue;

                    int si = (sy * srcW + sx) * 4;
                    int di = (dy * dstW + dx) * 4;
                    if (si + 3 >= src.Length || di + 3 >= dst.Length) continue;
                    if (src[si + 3] == 0) continue; // transparent — skip

                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        /// <summary>
        /// Get an <see cref="IImage"/>'s pixels as RGBA (4 B/px). RGBA images
        /// return their data directly; indexed images are expanded via their
        /// RGBA palette (index 0 / out-of-range -&gt; transparent, matching the
        /// platform's index-0-transparent convention). Returns false on a
        /// malformed buffer.
        /// </summary>
        static bool ToRgba(IImage img, out byte[] rgba, out int w, out int h)
        {
            rgba = null; w = 0; h = 0;
            if (img == null) return false;
            w = img.Width; h = img.Height;
            if (w <= 0 || h <= 0) return false;

            byte[] data = img.GetPixelData();
            if (data == null) return false;

            if (!img.IsIndexed)
            {
                if (data.Length < w * h * 4) return false;
                rgba = data;
                return true;
            }

            // Indexed: expand via the RGBA palette.
            if (data.Length < w * h) return false;
            byte[] pal = img.GetPaletteRGBA();
            if (pal == null || pal.Length == 0) return false;
            int colorCount = pal.Length / 4;

            byte[] outRgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                int idx = data[i];
                int di = i * 4;
                if (idx <= 0 || idx >= colorCount)
                {
                    // transparent (index 0 / out-of-range).
                    outRgba[di + 3] = 0;
                    continue;
                }
                outRgba[di + 0] = pal[idx * 4 + 0];
                outRgba[di + 1] = pal[idx * 4 + 1];
                outRgba[di + 2] = pal[idx * 4 + 2];
                outRgba[di + 3] = pal[idx * 4 + 3];
            }
            rgba = outRgba;
            return true;
        }

        // ==================================================================
        // Local guards
        // ==================================================================

        /// <summary>
        /// True when <c>[addr, addr+bytes)</c> is a valid in-ROM region
        /// (isSafetyOffset domain + an explicit ulong end-of-range check so the
        /// addition cannot overflow). Mirrors ImageWorldMapCore.IsRegionSafe.
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }
    }
}
