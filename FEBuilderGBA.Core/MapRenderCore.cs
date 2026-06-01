// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform chapter-map composite render (NV6-PR1, issue #855).
//
// WinForms reference (FEBuilderGBA/ImageUtilMap.cs):
//   * DrawMap (~L230-323): LZ77-decode OBJ tiles (raw bytes), RAW-read 512-byte
//     palette (16 banks × 16 colors × 2), LZ77-decode config chipset, LZ77-decode
//     map data; build expanded ushort[] TSA array then call ImageUtil.BitBltTSA.
//   * DrawMapChipOnly (~L21-90): LZ77-decode OBJ bytes + RAW palette;
//     optional OBJ2 appended (FE7 only, obj_plist high byte — see MR4 note below).
//   * UnLZ77ChipsetData (~L93-107): LZ77-decompress the config stream.
//   * UnLZ77MapData (~L179-193): LZ77-decompress the map data stream.
//
// Algorithm divergence from WF DrawMap (by plan-review corrections):
//   MR1: config byte-offset = m << 1 (WF identical: tile_tsa_index = m << 1).
//   MR3: OBJ tiles = RAW LZ77-decompressed bytes; do NOT pass through
//        LoadROMTiles4bpp (which returns IImage, not byte[]).
//   MR4 (deferred): FE7 has an optional second OBJ tileset (obj2, encoded in the
//        high byte of obj_plist) that is U.ArrayAppend-ed onto the primary bytes
//        before the final render in WF DrawMapChipOnly. PR1 takes a SINGLE
//        objOffset. A follow-up PR should add obj2Offset + Array.concat before
//        DecodeTSA when the caller resolves a non-zero obj2 plist.
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
    /// Cross-platform chapter-map tile composite renderer (issue #855, NV6-PR1).
    /// Ports WinForms <c>ImageUtilMap.DrawMap</c> to the Core library using
    /// <see cref="ImageUtilCore.DecodeTSA"/> with <c>opaqueIndex0=true</c>.
    ///
    /// <para><b>FE7 OBJ2 deferred (MR4):</b> FE7 maps may specify a second OBJ
    /// tileset (encoded in the high byte of <c>obj_plist</c>) that WF appends
    /// via <c>U.ArrayAppend</c> before the render. This PR takes a single
    /// <paramref name="objOffset"/>. A follow-up must add an <c>obj2Offset</c>
    /// parameter and concat the two byte arrays before calling
    /// <see cref="ImageUtilCore.DecodeTSA"/> when the caller supplies a valid
    /// second tileset offset.</para>
    /// </summary>
    public static class MapRenderCore
    {
        // 16 palette banks × 16 colors × 2 bytes each = 512 bytes.
        const int PALETTE_BYTES = 16 * 16 * 2;

        // GBA LZ77 stream header is 4 bytes (0x10 marker + 3-byte uncompressed size).
        const int LZ77_HEADER_BYTES = 4;

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
            uint configOffset, uint mapOffset)
        {
            try
            {
                return RenderMapImageCore(rom, objOffset, paletteOffset, configOffset, mapOffset);
            }
            catch (Exception ex)
            {
                Log.Error($"MapRenderCore.RenderMapImage failed: {ex}");
                return null;
            }
        }

        // =====================================================================
        // Internal implementation — same contract as the public surface above,
        // but allowed to throw (the public wrapper catches all exceptions).
        // =====================================================================

        static IImage RenderMapImageCore(ROM rom, uint objOffset, uint paletteOffset,
            uint configOffset, uint mapOffset)
        {
            // --- Guard 1: null checks ---
            // rom.RomInfo is intentionally NOT checked: RenderMapImage only uses
            // rom.Data + the explicit offsets passed by the caller, so synthetic/unknown
            // ROMs with valid offsets and Data can render without a matched RomInfo.
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // --- Step 2 (MR3): OBJ tiles — LZ77 raw bytes ---
            // Validate the compressed stream BEFORE decompress (truncation guard
            // mirrors ImageTSAEditorCore.TryLoadTSATileAndPalette, PR #818/#819).
            if (!IsLZ77HeaderSafe(rom, objOffset)) return null;
            uint objCompressedSize = LZ77.getCompressedSize(rom.Data, objOffset);
            if (objCompressedSize == 0) return null;
            if ((ulong)objOffset + objCompressedSize > (ulong)rom.Data.Length) return null;
            byte[] objBytes = LZ77.decompress(rom.Data, objOffset);
            if (objBytes == null || objBytes.Length == 0) return null;

            // --- Step 3 (CORRECTION): Palette — RAW 512 bytes from passed rom ---
            // Use the passed `rom` directly (NOT CoreState.ROM / GetPalette) so
            // the call is authoritative on the ROM instance the caller controls.
            if (!U.isSafetyOffset(paletteOffset, rom)) return null;
            int palBytes = PALETTE_BYTES;
            if ((ulong)paletteOffset + (ulong)palBytes > (ulong)rom.Data.Length)
            {
                // Clamp to ROM end (matching ImageTSAEditorCore's palette truncation
                // tolerance — DecodeTileToPixels bounds-checks short palettes).
                palBytes = (int)((ulong)rom.Data.Length - paletteOffset);
            }
            if (palBytes <= 0) return null;
            byte[] palette = new byte[palBytes];
            Array.Copy(rom.Data, paletteOffset, palette, 0, palBytes);

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
            // Total bytes needed: 2 header + width*height u16 entries.
            if (2 + width * height * 2 > mar.Length) return null;

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
            for (int i = 2; i + 1 < mar.Length; i += 2)
            {
                // Read the MAR u16 for this logical tile (little-endian).
                int m = mar[i] | ((int)mar[i + 1] << 8);

                // MR1: config byte-offset = m << 1 (WF: tile_tsa_index = m << 1).
                int descOff = m << 1;

                if (descOff + 7 >= configUZ.Length)
                {
                    // DIVERGENCE from WF: WF `ImageUtilMap.DrawMap` (line 284-287)
                    // calls `return ImageUtil.BlankDummy()` when a tile's descriptor
                    // offset is out of range, blanking the ENTIRE map output.
                    // For a read-only preview we instead skip only the offending tile
                    // (the zero-initialized canvas slot stays zero, mapping to
                    // tile 0, palette bank 0, no flip — a benign blank tile) and
                    // continue rendering the rest of the map.  A partial image is
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
