// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform chapter-map composite render (NV6-PR1, issue #855) +
// Change-map overlay render (NV6-PR2, issue #857).
//
// WinForms reference (FEBuilderGBA/ImageUtilMap.cs):
//   * DrawMap (~L230-323): LZ77-decode OBJ tiles (raw bytes), RAW-read 512-byte
//     palette (16 banks × 16 colors × 2), LZ77-decode config chipset, LZ77-decode
//     map data; build expanded ushort[] TSA array then call ImageUtil.BitBltTSA.
//   * DrawMapChipOnly (~L21-90): LZ77-decode OBJ bytes + RAW palette;
//     optional OBJ2 appended (FE7 only, obj_plist high byte — see MR4 note below).
//   * DrawChangeMap (~L418-495): same as DrawMap but the tile-index array is
//     a RAW (uncompressed) u16 array of width*height entries instead of LZ77
//     MAR data. Width/height are caller-supplied (from the change record B3/B4).
//   * UnLZ77ChipsetData (~L93-107): LZ77-decompress the config stream.
//   * UnLZ77MapData (~L179-193): LZ77-decompress the map data stream.
//
// Algorithm divergence from WF DrawMap (by plan-review corrections):
//   MR1: config byte-offset = m << 1 (WF identical: tile_tsa_index = m << 1).
//   MR3: OBJ tiles = RAW LZ77-decompressed bytes; do NOT pass through
//        LoadROMTiles4bpp (which returns IImage, not byte[]).
//   MR4 (RESOLVED, #961 W2c): FE7 has an optional second OBJ tileset (obj2,
//        encoded in the HIGH byte of obj_plist) that is U.ArrayAppend-ed onto the
//        primary bytes before the final render in WF DrawMapChipOnly (~L55-63).
//        RenderMapImage / RenderChangeMap now take an OPTIONAL obj2Offset
//        parameter (default 0 = "no second tileset", matching FE6/FE8 where the
//        high byte is always 0). When the caller resolves a non-zero obj2 plist
//        (FE7 only), it passes the second tileset's ROM offset; the two
//        LZ77-decompressed byte arrays are concatenated (primary first, obj2
//        appended) BEFORE DecodeTSA — byte-for-byte the WF order. A 0 / NOT_FOUND
//        obj2Offset is treated as "no second tileset" (skip the concat).
//   MR5: opaqueIndex0 = true; GBA map tiles are an opaque background — palette
//        index 0 renders as the real palette color (alpha 255), not transparent.
//        WF BitBltTSA calls BitBlt with transparent_index=0xFF, which never
//        matches a 4-bit index (0..15), so all pixels including index 0 are
//        blitted opaque.
//
// LZ77 truncation guard pattern mirrors ImageTSAEditorCore.TryLoadTSATileAndPalette
// (PR #818/#819): isSafetyOffset + getCompressedSize == 0 check +
// end-of-ROM bound check BEFORE decompress, to avoid silent zero-fill on truncated
// streams.

using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform chapter-map tile composite renderer (issue #855, NV6-PR1)
    /// and change-map overlay renderer (issue #857, NV6-PR2).
    /// Ports WinForms <c>ImageUtilMap.DrawMap</c> + <c>DrawChangeMap</c> to the
    /// Core library using <see cref="ImageUtilCore.DecodeTSA"/> with
    /// <c>opaqueIndex0=true</c>.
    ///
    /// <para><b>FE7 OBJ2 (MR4, resolved in #961 W2c):</b> FE7 maps may specify a
    /// second OBJ tileset (encoded in the HIGH byte of <c>obj_plist</c>) that WF
    /// appends via <c>U.ArrayAppend</c> before the render. Both methods accept an
    /// OPTIONAL <c>obj2Offset</c> parameter (default 0). When the caller resolves
    /// a non-zero second-tileset offset (FE7 only — FE6/FE8 keep the high byte 0),
    /// the obj2 LZ77 stream is decompressed and concatenated onto the primary OBJ
    /// bytes (primary first) before calling
    /// <see cref="ImageUtilCore.DecodeTSA"/>.</para>
    /// </summary>
    public static class MapRenderCore
    {
        // 16 palette banks × 16 colors × 2 bytes each = 512 bytes.
        const int PALETTE_BYTES = 16 * 16 * 2;

        // GBA LZ77 stream header is 4 bytes (0x10 marker + 3-byte uncompressed size).
        const int LZ77_HEADER_BYTES = 4;

        // Sanity upper bound on the total tile count for a change-map overlay.
        // cap = 256×256 logical tiles (a 4096×4096-pixel canvas) — guards against
        // a corrupt change record requesting a huge allocation.
        // Per-dimension bounds (width > 256 || height > 256) are enforced separately
        // so a degenerate shape such as width=65536, height=1 is rejected before
        // the product check; this constant is the final product cap.
        const int MAX_CHANGE_TILES = 256 * 256; // = 65536

        /// <summary>
        /// Composite a full chapter-map image from its four already-resolved ROM
        /// data offsets, mirroring WinForms <c>ImageUtilMap.DrawMap</c>.
        ///
        /// <para><b>Returned image dimensions:</b>
        /// <c>width * 16 × height * 16</c> pixels, where
        /// <c>width</c>/<c>height</c> are the logical tile counts stored in the
        /// first two bytes of the decompressed map data.</para>
        ///
        /// <para><b>Never throws.</b> Returns <c>null</c> on any null/out-of-bounds/
        /// corrupt input or missing <see cref="CoreState.ImageService"/>.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (read-only; never written).</param>
        /// <param name="objOffset">
        /// ROM offset of the LZ77-compressed OBJ tile data (primary tileset only;
        /// see MR4 note in the file header for FE7 obj2 deferral).
        /// </param>
        /// <param name="paletteOffset">
        /// ROM offset of the RAW (uncompressed) 512-byte palette block
        /// (16 banks × 16 colors × 2 bytes, BGR555 little-endian).
        /// </param>
        /// <param name="configOffset">
        /// ROM offset of the LZ77-compressed config (chipset descriptor) data.
        /// Each logical-tile descriptor is 8 bytes: four u16 TSA entries
        /// (lefttop, righttop, leftbottom, rightbottom).
        /// </param>
        /// <param name="mapOffset">
        /// ROM offset of the LZ77-compressed map arrangement data. The first two
        /// bytes of the decompressed stream are <c>width</c> and <c>height</c>
        /// in logical tiles; the remainder is a width×height array of u16 MAR
        /// values that index into <paramref name="configOffset"/>.
        /// </param>
        /// <returns>
        /// RGBA <see cref="IImage"/> of <c>width*16 × height*16</c> pixels on success.
        ///
        /// Returns <c>null</c> when:
        /// <list type="bullet">
        ///   <item><description><paramref name="rom"/> is <c>null</c> or
        ///   <c>rom.Data</c> is <c>null</c>.</description></item>
        ///   <item><description><see cref="CoreState.ImageService"/> is
        ///   <c>null</c>.</description></item>
        ///   <item><description>Any of the four LZ77-compressed streams
        ///   (<paramref name="objOffset"/>, <paramref name="configOffset"/>,
        ///   <paramref name="mapOffset"/>) is truncated, has a zero compressed
        ///   size, or decompresses to an empty result.</description></item>
        ///   <item><description>The decompressed map data has zero
        ///   <c>width</c> or <c>height</c>.</description></item>
        /// </list>
        ///
        /// When an individual MAR tile's config descriptor index is out of range,
        /// that tile is <b>skipped</b> (left as zero-filled blank), and a partial
        /// image is still returned.  This differs from WF
        /// <c>ImageUtilMap.DrawMap</c>, which bails to <c>BlankDummy()</c>
        /// (blanks the whole map) on the first such tile; see the divergence note
        /// in the implementation body.
        /// </returns>
        public static IImage RenderMapImage(ROM rom, uint objOffset, uint paletteOffset,
            uint configOffset, uint mapOffset, uint obj2Offset = 0,
            byte[] animeOverlayBytes = null, int animeOverlayDestOffset = 0)
        {
            try
            {
                return RenderMapImageCore(rom, objOffset, paletteOffset, configOffset, mapOffset,
                    obj2Offset, animeOverlayBytes, animeOverlayDestOffset);
            }
            catch (Exception ex)
            {
                Log.Error($"MapRenderCore.RenderMapImage failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Byte offset within the decompressed OBJ tile buffer where a Map Tile
        /// Animation Type 1 frame is patched in, mirroring WinForms
        /// <c>ImageUtilMap.DrawMapChipOnly</c> (~L72,
        /// <c>U.ArrayPatch(anime.change_bitmap_bytes, 0, objUZ, 32 * (8 / 2) * 4 * 16)</c>).
        /// <c>32 * (8 / 2) * 4 * 16 = 8192</c> bytes = 256 tiles × 32 bytes/tile (each
        /// 8×8 4bpp tile is 32 bytes), i.e. the anime tile region begins right after
        /// the first 256 OBJ tiles (32 tiles/row × 8 rows). Exposed so the anime1 GIF
        /// export (#1602) and tests use the same constant as the renderer rather than
        /// a magic number.
        /// </summary>
        public const int ANIME1_OBJ_PATCH_OFFSET = 32 * (8 / 2) * 4 * 16; // = 8192 = 256 tiles × 32 bytes

        /// <summary>
        /// Composite a change-map overlay image from its already-resolved ROM offsets
        /// and caller-supplied dimensions, mirroring WinForms
        /// <c>ImageUtilMap.DrawChangeMap</c>.
        ///
        /// <para>The change-data at <paramref name="changeDataOffset"/> is a
        /// <b>RAW (uncompressed)</b> array of <c>width * height</c> u16 tile-index
        /// values — NOT LZ77. The same <c>index = u16value &lt;&lt; 1</c> descriptor
        /// lookup into <paramref name="configOffset"/> is used as in
        /// <see cref="RenderMapImage"/>.</para>
        ///
        /// <para>OBJ (<paramref name="objOffset"/>) and config
        /// (<paramref name="configOffset"/>) remain LZ77-compressed (same as the
        /// chapter-map path). Palette (<paramref name="paletteOffset"/>) remains
        /// RAW 512 bytes.</para>
        ///
        /// <para><b>Never throws.</b> Returns <c>null</c> on any guard failure.
        /// Width/height of zero, out-of-bounds changeDataOffset, or oversized
        /// dimensions all return <c>null</c> without allocating.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (read-only; never written).</param>
        /// <param name="objOffset">ROM offset of the LZ77-compressed OBJ tile data.</param>
        /// <param name="paletteOffset">ROM offset of the RAW 512-byte palette block.</param>
        /// <param name="configOffset">ROM offset of the LZ77-compressed config data.</param>
        /// <param name="changeDataOffset">
        /// ROM offset of the RAW u16 array of <c>width * height</c> tile-index values.
        /// Mirrors the WF <c>DrawChangeMap</c> <c>change_address</c> parameter
        /// (already converted from GBA pointer to ROM offset by the caller).
        /// </param>
        /// <param name="width">Width of the change region in logical tiles (from B3 of the change record).</param>
        /// <param name="height">Height of the change region in logical tiles (from B4 of the change record).</param>
        /// <param name="obj2Offset">
        /// OPTIONAL ROM offset of the FE7 secondary (obj2) LZ77-compressed OBJ tile
        /// data, resolved by the caller from the HIGH byte of <c>obj_plist</c>
        /// (<c>(obj_plist &gt;&gt; 8) &amp; 0xFF</c>). Pass 0 (the default) when the
        /// map has no second tileset — FE6/FE8 always pass 0 since their high byte
        /// is 0. When non-zero, the obj2 stream is LZ77-decompressed and
        /// concatenated onto the primary OBJ bytes (primary first) before render,
        /// mirroring WF <c>DrawMapChipOnly</c> (#961 W2c).
        /// </param>
        /// <returns>
        /// RGBA <see cref="IImage"/> of <c>width*16 × height*16</c> pixels on success,
        /// or <c>null</c> on any guard failure (including oversized or empty dimensions).
        ///
        /// When an individual change-data entry's config descriptor index is out of
        /// range, that tile is <b>skipped</b> (matching the PR1 inherited divergence
        /// from WF <c>DrawChangeMap</c> which returns <c>BlankDummy</c> on the first
        /// out-of-range tile). A partial image is returned rather than null.
        ///
        /// When <paramref name="obj2Offset"/> is non-zero but its LZ77 stream is
        /// truncated / invalid, the whole render returns <c>null</c> (matching WF
        /// <c>DrawMapChipOnly</c> which bails to <c>BlankDummy</c> on a bad obj2).
        /// </returns>
        public static IImage RenderChangeMap(ROM rom, uint objOffset, uint paletteOffset,
            uint configOffset, uint changeDataOffset, int width, int height, uint obj2Offset = 0)
        {
            try
            {
                return RenderChangeMapCore(rom, objOffset, paletteOffset, configOffset,
                    changeDataOffset, width, height, obj2Offset);
            }
            catch (Exception ex)
            {
                Log.Error($"MapRenderCore.RenderChangeMap failed: {ex}");
                return null;
            }
        }

        // =====================================================================
        // Internal implementation — same contract as the public surface above,
        // but allowed to throw (the public wrappers catch all exceptions).
        // =====================================================================

        static IImage RenderMapImageCore(ROM rom, uint objOffset, uint paletteOffset,
            uint configOffset, uint mapOffset, uint obj2Offset,
            byte[] animeOverlayBytes = null, int animeOverlayDestOffset = 0)
        {
            // --- Guard 1: null checks ---
            // rom.RomInfo is intentionally NOT checked: RenderMapImage only uses
            // rom.Data + the explicit offsets passed by the caller, so synthetic/unknown
            // ROMs with valid offsets and Data can render without a matched RomInfo.
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // --- Step 2 (MR3): OBJ tiles — LZ77 raw bytes (+ FE7 obj2 append, MR4) ---
            byte[] objBytes = DecodeObjBytesWithObj2(rom, objOffset, obj2Offset);
            if (objBytes == null || objBytes.Length == 0) return null;

            // --- Step 2b (#1602): Map Tile Animation Type 1 overlay ---
            // Patch the anime1 frame's RAW 4bpp bytes into the decompressed OBJ
            // buffer, mirroring WF DrawMapChipOnly (~L72). Bounded copy: never
            // grows objBytes and clamps to the buffer end (the WF U.ArrayPatch
            // semantic).
            ApplyAnimeOverlay(objBytes, animeOverlayBytes, animeOverlayDestOffset);

            // --- Step 3: Palette — RAW 512 bytes ---
            byte[] palette = ReadRawPalette(rom, paletteOffset);
            if (palette == null) return null;

            // --- Step 4: Config (chipset) — LZ77 decompress ---
            if (!IsLZ77HeaderSafe(rom, configOffset)) return null;
            uint cfgCompressedSize = LZ77.getCompressedSize(rom.Data, configOffset);
            if (cfgCompressedSize == 0) return null;
            if ((ulong)configOffset + cfgCompressedSize > (ulong)rom.Data.Length) return null;
            byte[] configUZ = LZ77.decompress(rom.Data, configOffset);
            if (configUZ == null || configUZ.Length == 0) return null;

            // --- Step 5: Map data — LZ77 decompress; extract width/height ---
            if (!IsLZ77HeaderSafe(rom, mapOffset)) return null;
            uint mapCompressedSize = LZ77.getCompressedSize(rom.Data, mapOffset);
            if (mapCompressedSize == 0) return null;
            if ((ulong)mapOffset + mapCompressedSize > (ulong)rom.Data.Length) return null;
            byte[] mar = LZ77.decompress(rom.Data, mapOffset);
            if (mar == null || mar.Length < 2) return null;

            int width = mar[0];  // logical tile count (TILES, not pixels)
            int height = mar[1];
            if (width <= 0 || height <= 0) return null;

            // The u16 tile-index array starts at mar[2].
            if (2 + width * height * 2 > mar.Length) return null;

            // Build the ushort[] tile-index array from the MAR data.
            int tileCount = width * height;
            ushort[] tileIndices = new ushort[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                int byteOff = 2 + i * 2;
                tileIndices[i] = (ushort)(mar[byteOff] | ((int)mar[byteOff + 1] << 8));
            }

            return RenderTsaComposite(rom, objBytes, palette, configUZ, tileIndices, width, height);
        }

        static IImage RenderChangeMapCore(ROM rom, uint objOffset, uint paletteOffset,
            uint configOffset, uint changeDataOffset, int width, int height, uint obj2Offset)
        {
            // --- Guard 1: null / ImageService ---
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // --- Guard 2: dimension sanity ---
            // Check each dimension individually first so a degenerate shape
            // (e.g. width=65536, height=1) is rejected before the product check.
            // WF map dimensions are byte-sourced (B3/B4 of the change record, u8),
            // so they can never exceed 255 in practice; 256 is a safe ceiling.
            if (width <= 0 || height <= 0) return null;
            if (width > 256 || height > 256) return null;
            // Now both width and height are ≤ 256, so their product fits in int.
            // Use long for the bounds arithmetic to avoid any intermediate overflow,
            // then assign to int once we know the value is ≤ MAX_CHANGE_TILES.
            long tileCountL = (long)width * height;
            if (tileCountL > MAX_CHANGE_TILES) return null;
            int tileCount = (int)tileCountL; // provably ≤ 65536, safe as int

            // --- Guard 3: change-data bounds ---
            // changeDataOffset is a ROM offset (already converted by caller via U.toOffset).
            if (!U.isSafetyOffset(changeDataOffset, rom)) return null;
            long changeDataEnd = (long)changeDataOffset + (long)tileCount * 2; // long to avoid overflow
            if (changeDataEnd > rom.Data.Length) return null;

            // --- Step OBJ: LZ77 raw bytes (+ FE7 obj2 append, MR4) ---
            byte[] objBytes = DecodeObjBytesWithObj2(rom, objOffset, obj2Offset);
            if (objBytes == null || objBytes.Length == 0) return null;

            // --- Step PAL: RAW 512 bytes ---
            byte[] palette = ReadRawPalette(rom, paletteOffset);
            if (palette == null) return null;

            // --- Step CFG: LZ77 decompress ---
            if (!IsLZ77HeaderSafe(rom, configOffset)) return null;
            uint cfgCompressedSize = LZ77.getCompressedSize(rom.Data, configOffset);
            if (cfgCompressedSize == 0) return null;
            if ((ulong)configOffset + cfgCompressedSize > (ulong)rom.Data.Length) return null;
            byte[] configUZ = LZ77.decompress(rom.Data, configOffset);
            if (configUZ == null || configUZ.Length == 0) return null;

            // --- Step CHG: read the RAW u16 tile-index array ---
            ushort[] tileIndices = new ushort[tileCount]; // int index — provably safe
            for (int i = 0; i < tileCount; i++)
            {
                uint byteOff = (uint)((long)changeDataOffset + (long)i * 2);
                tileIndices[i] = (ushort)U.u16(rom.Data, byteOff);
            }

            return RenderTsaComposite(rom, objBytes, palette, configUZ, tileIndices, width, height);
        }

        /// <summary>
        /// Shared private helper: given already-decoded OBJ bytes, palette bytes,
        /// config (chipset) bytes, and a flat ushort[] tile-index array, composite
        /// the <c>width × height</c> logical-tile canvas and return an
        /// <see cref="IImage"/>.
        ///
        /// <para>This is the byte-identical core extracted from PR1's
        /// <c>RenderMapImageCore</c> step 6+7. Both <c>RenderMapImage</c> and
        /// <c>RenderChangeMap</c> differ only in how they obtain the
        /// <paramref name="tileIndices"/> array (LZ77-MAR vs. raw u16 block).</para>
        ///
        /// <para>Out-of-range config <c>descOff</c> entries are <b>skipped</b>
        /// (zero-filled subtile slots) — matching the PR1 divergence from WF
        /// <c>DrawMap</c> / <c>DrawChangeMap</c>, which bail on the first such tile.
        /// A partial image is returned rather than null.</para>
        /// </summary>
        static IImage RenderTsaComposite(ROM rom,
            byte[] objBytes, byte[] palette, byte[] configUZ,
            ushort[] tileIndices, int width, int height)
        {
            // --- Step 6 (MR1): Build expanded per-subtile TSA byte[] ---
            // Each logical tile (16×16 px) expands to 2×2 subtiles (8×8 px each),
            // so the canvas is (width*2) × (height*2) subtiles.
            // expandedTSA stores each subtile TSA entry as 2 bytes (little-endian).
            int canvasW = width * 2;  // subtile columns
            int canvasH = height * 2; // subtile rows
            byte[] expandedTSA = new byte[canvasW * canvasH * 2];

            // Iterate logical tiles using the same x/y pattern as WF DrawMap
            // (lines 277-318) to stay bug-for-bug identical with the reference.
            int x = 0;  // subtile x (increments by 2 per logical tile)
            int y = 0;  // subtile y
            int totalTiles = tileIndices.Length;
            for (int t = 0; t < totalTiles; t++)
            {
                // Read the u16 tile index for this logical tile.
                int m = tileIndices[t];

                // MR1: config byte-offset = m << 1 (WF: tile_tsa_index = m << 1).
                int descOff = m << 1;

                if (descOff + 7 >= configUZ.Length)
                {
                    // DIVERGENCE from WF: WF `ImageUtilMap.DrawMap` (line 284-287) and
                    // `DrawChangeMap` (line 464-466) return `BlankDummy` / `BlankDummy(16)`
                    // when a tile's descriptor offset is out of range, blanking the ENTIRE
                    // canvas output.
                    // For a read-only preview we instead skip only the offending tile
                    // (the zero-initialized canvas slot stays zero, mapping to
                    // tile 0, palette bank 0, no flip — a benign blank tile) and
                    // continue rendering the rest of the canvas.  A partial image is
                    // more useful than a fully-blank preview for a single corrupt
                    // descriptor.  Each tile writes to independent subtile slots, so
                    // skipping one tile cannot corrupt the rest of the render.
                }
                else
                {
                    // Read four u16 subtile TSA descriptors (already in GBA TSA
                    // format: bits 0-9 = tile index, 10 = hflip, 11 = vflip,
                    // 12-15 = palette bank).
                    ushort lt = (ushort)(configUZ[descOff]     | (configUZ[descOff + 1] << 8));
                    ushort rt = (ushort)(configUZ[descOff + 2] | (configUZ[descOff + 3] << 8));
                    ushort lb = (ushort)(configUZ[descOff + 4] | (configUZ[descOff + 5] << 8));
                    ushort rb = (ushort)(configUZ[descOff + 6] | (configUZ[descOff + 7] << 8));

                    // Write into expandedTSA at the four subtile positions.
                    // Subtile (sx, sy) -> byte offset (sy * canvasW + sx) * 2.
                    WriteTSAEntry(expandedTSA, canvasW, x,     y,     lt);
                    WriteTSAEntry(expandedTSA, canvasW, x + 1, y,     rt);
                    WriteTSAEntry(expandedTSA, canvasW, x,     y + 1, lb);
                    WriteTSAEntry(expandedTSA, canvasW, x + 1, y + 1, rb);
                }

                // Advance x/y exactly as WF DrawMap (lines 308-317).
                x += 2;
                if (x >= canvasW)
                {
                    x = 0;
                    y += 2;
                    if (y >= canvasH)
                        break;
                }
            }

            // --- Step 7 (MR5): Composite via DecodeTSA with opaqueIndex0:true ---
            // DecodeTSA signature (confirmed from ImageUtilCore.cs):
            //   DecodeTSA(byte[] tileData, byte[] tsaData, byte[] gbaPalette,
            //             int screenWidthTiles, int screenHeightTiles,
            //             bool is4bpp = true, int tsaOffset = 0,
            //             bool opaqueIndex0 = false)
            // 7th arg = tsaOffset (pass 0 — our expandedTSA starts at index 0).
            // 8th arg = opaqueIndex0 (pass true — maps are opaque backgrounds).
            return ImageUtilCore.DecodeTSA(objBytes, expandedTSA, palette,
                canvasW, canvasH, is4bpp: true, tsaOffset: 0, opaqueIndex0: true);
        }

        // =====================================================================
        // Private helpers
        // =====================================================================

        /// <summary>
        /// Patch the Map Tile Animation Type 1 frame bytes
        /// (<paramref name="overlay"/>) into the decompressed OBJ tile buffer at
        /// <paramref name="destOffset"/>, mirroring WinForms
        /// <c>U.ArrayPatch(anime.change_bitmap_bytes, 0, objUZ, ...)</c> in
        /// <c>ImageUtilMap.DrawMapChipOnly</c> (~L72). A null/empty overlay or a
        /// destination offset at/after the buffer end is a no-op (leaves the base
        /// OBJ tiles untouched). The copy length is clamped so it never grows
        /// <paramref name="objBytes"/> and never reads past the overlay end —
        /// matching the WF bounded <c>Array.Copy</c> behaviour.
        /// </summary>
        static void ApplyAnimeOverlay(byte[] objBytes, byte[] overlay, int destOffset)
        {
            if (objBytes == null || overlay == null || overlay.Length == 0) return;
            if (destOffset < 0 || destOffset >= objBytes.Length) return;
            int copyLength = Math.Min(overlay.Length, objBytes.Length - destOffset);
            if (copyLength <= 0) return;
            Array.Copy(overlay, 0, objBytes, destOffset, copyLength);
        }

        /// <summary>
        /// Decompress the primary OBJ tile stream at <paramref name="objOffset"/>
        /// and, when <paramref name="obj2Offset"/> is non-zero (FE7 secondary
        /// tileset, MR4), decompress that stream too and append it to the primary
        /// bytes — mirroring WF <c>DrawMapChipOnly</c> (~L55-63,
        /// <c>U.ArrayAppend(objUZ, obj2UZ)</c>).
        ///
        /// <para>Returns <c>null</c> when the primary stream is truncated/invalid/empty,
        /// OR when <paramref name="obj2Offset"/> is non-zero but its stream is
        /// truncated/invalid/empty (matching WF's bail-to-BlankDummy on a bad obj2).
        /// A zero / <see cref="U.NOT_FOUND"/> <paramref name="obj2Offset"/> means
        /// "no second tileset" and only the primary bytes are returned.</para>
        /// </summary>
        static byte[] DecodeObjBytesWithObj2(ROM rom, uint objOffset, uint obj2Offset)
        {
            // Primary OBJ tiles — LZ77 raw bytes.
            if (!IsLZ77HeaderSafe(rom, objOffset)) return null;
            uint objCompressedSize = LZ77.getCompressedSize(rom.Data, objOffset);
            if (objCompressedSize == 0) return null;
            if ((ulong)objOffset + objCompressedSize > (ulong)rom.Data.Length) return null;
            byte[] objBytes = LZ77.decompress(rom.Data, objOffset);
            if (objBytes == null || objBytes.Length == 0) return null;

            // FE7 obj2 (MR4): only when the caller resolved a real second tileset.
            // 0 and U.NOT_FOUND both mean "no second tileset".
            if (obj2Offset == 0 || obj2Offset == U.NOT_FOUND) return objBytes;

            if (!IsLZ77HeaderSafe(rom, obj2Offset)) return null;
            uint obj2CompressedSize = LZ77.getCompressedSize(rom.Data, obj2Offset);
            if (obj2CompressedSize == 0) return null;
            if ((ulong)obj2Offset + obj2CompressedSize > (ulong)rom.Data.Length) return null;
            byte[] obj2Bytes = LZ77.decompress(rom.Data, obj2Offset);
            if (obj2Bytes == null || obj2Bytes.Length == 0) return null;

            // Concatenate: primary first, obj2 appended (WF byte order).
            byte[] combined = new byte[objBytes.Length + obj2Bytes.Length];
            Array.Copy(objBytes, 0, combined, 0, objBytes.Length);
            Array.Copy(obj2Bytes, 0, combined, objBytes.Length, obj2Bytes.Length);
            return combined;
        }

        /// <summary>
        /// Read the raw palette bytes from ROM at <paramref name="paletteOffset"/>.
        /// Clamps to ROM end when the full 512 bytes are not available (matching
        /// the ImageTSAEditorCore truncation-tolerance pattern).
        /// Returns null when the offset is unsafe or no bytes are available.
        /// </summary>
        static byte[] ReadRawPalette(ROM rom, uint paletteOffset)
        {
            if (!U.isSafetyOffset(paletteOffset, rom)) return null;
            int palBytes = PALETTE_BYTES;
            if ((ulong)paletteOffset + (ulong)palBytes > (ulong)rom.Data.Length)
                palBytes = (int)((ulong)rom.Data.Length - paletteOffset);
            if (palBytes <= 0) return null;
            byte[] palette = new byte[palBytes];
            Array.Copy(rom.Data, paletteOffset, palette, 0, palBytes);
            return palette;
        }

        /// <summary>
        /// Write a 16-bit TSA entry as little-endian bytes into <paramref name="tsaBuffer"/>
        /// at the position corresponding to subtile (<paramref name="sx"/>, <paramref name="sy"/>).
        /// Bounds-checks silently (out-of-range writes are no-ops).
        /// </summary>
        static void WriteTSAEntry(byte[] tsaBuffer, int canvasWidthTiles, int sx, int sy, ushort entry)
        {
            int byteOff = (sy * canvasWidthTiles + sx) * 2;
            if ((uint)(byteOff + 1) >= (uint)tsaBuffer.Length) return;
            tsaBuffer[byteOff]     = (byte)(entry & 0xFF);
            tsaBuffer[byteOff + 1] = (byte)(entry >> 8);
        }

        /// <summary>
        /// True when the 4-byte GBA LZ77 stream header starting at
        /// <paramref name="addr"/> fits within the ROM data.
        /// <c>LZ77.getCompressedSize</c> reads <c>input[offset+3]</c> but only
        /// rejects fewer-than-3-byte tails, so a 1-3 byte tail passes isSafetyOffset
        /// yet makes the header read throw. Require the full 4-byte header to be
        /// in-bounds BEFORE any LZ77 call. Mirrors ImageTSAEditorCore.IsLZ77HeaderSafe
        /// (#818).
        /// </summary>
        static bool IsLZ77HeaderSafe(ROM rom, uint addr) =>
            U.isSafetyOffset(addr, rom) &&
            (ulong)addr + LZ77_HEADER_BYTES <= (ulong)rom.Data.Length;
    }
}
