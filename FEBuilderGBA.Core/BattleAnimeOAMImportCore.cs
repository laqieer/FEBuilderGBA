using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform OAM assembler — converts an indexed-colour image into
    /// the tile sheet + palette + OAM record bytes consumed by the FE GBA
    /// battle-animation engine.
    ///
    /// Ports WinForms <c>ImageUtilOAM.ImportOAM.MakeBattleAnimeLow</c> /
    /// <c>MakeMagicAnime</c> / <c>tryCopy</c> without any System.Drawing or
    /// WinForms dependencies.  All pixel work uses raw indexed-pixel arrays
    /// (1 byte per pixel, palette index).
    ///
    /// The produced OAM records are compatible with
    /// <see cref="BattleAnimeRendererCore.DrawOAMSprites"/> — the assemble→render
    /// round-trip should reproduce the input image exactly.
    /// </summary>
    public static class BattleAnimeOAMImportCore
    {
        // ---- Seat (tile sheet) dimensions (in tiles) ----
        // The "seat" is the destination 256×64 (or 256×32 for magic) tile
        // sheet into which non-blank 8×8 blocks are packed.
        public const int SEAT_TILE_WIDTH  = 256 / 8;   // 32 tiles wide
        public const int SEAT_TILE_HEIGHT = 64  / 8;   // 8 tiles tall  (normal)
        public const int SEAT_MAGIC_TILE_HEIGHT = 32 / 8;   // 4 tiles tall  (magic)
        public const int SEAT_BORDERAP_TILE_HEIGHT = 40 / 8; // 5 tiles tall (border AP)

        // ---- Screen viewport (in tiles) — the source image grid ----
        public const int SCREEN_TILE_WIDTH  = 248 / 8;  // 31 (WF: SCREEN_TILE_WIDTH)
        public const int SCREEN_TILE_HEIGHT = 160 / 8;  // 20

        // ---- GBA OAM centering offsets (matches BattleAnimeRendererCore) ----
        private const int BITMAP_ADDX       = 0x94;  // 148
        private const int BITMAP_ADDY       = 0x58;  //  88
        private const int BITMAP_SPELL_ADDX = 0xAC;  // 172

        // ---- OAM shape/size nibbles (byte[1] align bits6-7, byte[3] area bits6-7) ----
        private const byte SHAPE_SQUARE     = 0x00;   // 0 << 6
        private const byte SHAPE_HORIZONTAL = 0x40;   // 1 << 6
        private const byte SHAPE_VERTICAL   = 0x80;   // 2 << 6

        private const byte SIZE_TIMES1 = 0x00;        // 0 << 6
        private const byte SIZE_TIMES2 = 0x40;        // 1 << 6
        private const byte SIZE_TIMES4 = 0x80;        // 2 << 6
        private const byte SIZE_TIMES8 = 0xC0;        // 3 << 6

        // ====================================================================
        // Public result types
        // ====================================================================

        /// <summary>
        /// Result of a single <see cref="AssembleOAM"/> call.
        /// On success <see cref="Error"/> is null; on failure it contains the
        /// human-readable reason and the other fields are null/empty.
        /// </summary>
        public class OAMAssembleResult
        {
            /// <summary>
            /// Uncompressed 4bpp tile data for the seat sheet (ready to LZ77-compress
            /// and write to ROM).  Null on failure.
            /// </summary>
            public byte[] TileData4bpp   { get; set; }

            /// <summary>
            /// Raw GBA palette bytes (16 colors × 2 bytes = 32 bytes per sub-palette,
            /// up to 4 sub-palettes = 128 bytes max).  Null on failure.
            /// </summary>
            public byte[] PaletteBytes   { get; set; }

            /// <summary>
            /// Raw OAM record stream (12 bytes per entry, terminated by a
            /// <c>0x01 00 00 00 …</c> terminator entry).  Null on failure.
            /// </summary>
            public byte[] OamRecords     { get; set; }

            /// <summary>
            /// Non-null on any failure.  Contains the reason the assembly failed.
            /// </summary>
            public string Error          { get; set; }

            /// <summary>True when assembly succeeded.</summary>
            public bool   Success        => Error == null;
        }

        // ====================================================================
        // Main public entry point
        // ====================================================================

        /// <summary>
        /// Assemble OAM for a single animation frame image.
        ///
        /// <para>
        /// The caller supplies an <em>indexed</em> pixel buffer (1 byte per pixel,
        /// values 0 = transparent / palette-index 0).  The palette is embedded in
        /// <paramref name="gbaPalette"/> as raw GBA BGR555 shorts packed into
        /// <c>byte[]</c> (little-endian, 2 bytes per color, 16 colors per bank,
        /// up to 4 banks = 128 bytes).  Color index 0 in each bank is the
        /// transparent color.
        /// </para>
        ///
        /// <para>
        /// The function packs non-blank 8×8 tiles into a 256×<em>seatHeight</em>
        /// tile sheet using a greedy largest-first rectangle strategy identical to
        /// WinForms <c>MakeBattleAnimeLow</c>.  It emits one 12-byte OAM record per
        /// packed rectangle, terminated by a standard end-of-list entry.
        /// </para>
        /// </summary>
        /// <param name="indexedPixels">
        ///   Packed indexed pixel data, row-major, 1 byte per pixel.
        ///   Width and height must both be multiples of 8.
        /// </param>
        /// <param name="width">Image width in pixels (must be a multiple of 8).</param>
        /// <param name="height">Image height in pixels (must be a multiple of 8).</param>
        /// <param name="gbaPalette">
        ///   GBA BGR555 palette bytes.  Must be 32 bytes (1 bank of 16 colors)
        ///   for normal/BorderAP mode, or up to 128 bytes (4 banks) for
        ///   multi-palette (magic) mode.
        /// </param>
        /// <param name="isMagic">
        ///   When true, use the magic-animation rules: seat height = 32 px instead
        ///   of 64 px; the "8×8 full square" OAM size is not emitted (matches WF);
        ///   X centering uses <c>BITMAP_SPELL_ADDX</c>.
        /// </param>
        /// <param name="isMultiPalette">
        ///   When true, up to 4 sub-palettes are processed (palette banks 0–3).
        ///   When false only bank 0 is processed.
        /// </param>
        /// <param name="existingSeat">
        ///   Optional: an already-partially-filled seat from a previous frame.
        ///   When provided the assembler attempts to pack tiles into this seat first
        ///   before starting a fresh one.  Pass null to always start fresh.
        /// </param>
        /// <returns>
        ///   A <see cref="OAMAssembleResult"/> with <see cref="OAMAssembleResult.Success"/>
        ///   true on success, or <see cref="OAMAssembleResult.Error"/> set on failure.
        /// </returns>
        public static OAMAssembleResult AssembleOAM(
            byte[] indexedPixels,
            int    width,
            int    height,
            byte[] gbaPalette,
            bool   isMagic        = false,
            bool   isMultiPalette = false,
            Seat   existingSeat   = null)
        {
            // ---- Input validation ----
            if (indexedPixels == null)
                return Fail("indexedPixels is null");
            if (width <= 0 || height <= 0 || width % 8 != 0 || height % 8 != 0)
                return Fail($"Image dimensions {width}x{height} must be positive multiples of 8");
            if (indexedPixels.Length < width * height)
                return Fail($"indexedPixels too short: {indexedPixels.Length} < {width * height}");
            if (gbaPalette == null || gbaPalette.Length < 32)
                return Fail("gbaPalette must be at least 32 bytes (1×16 colors)");

            int maxPalette = isMultiPalette ? 4 : 1;
            // clamp to what the palette buffer actually holds
            int availableBanks = gbaPalette.Length / 32;
            if (availableBanks < maxPalette) maxPalette = availableBanks;

            // ---- Set up seat (tile sheet) ----
            int seatTileH = isMagic ? SEAT_MAGIC_TILE_HEIGHT : SEAT_TILE_HEIGHT;
            int seatH = seatTileH * 8;   // 64 or 32 px

            Seat seat = existingSeat ?? new Seat(SEAT_TILE_WIDTH, seatTileH);

            // ---- OAM output list ----
            var oamBytes = new List<byte>(256);

            // ---- Per-sub-palette pass (matches MakeBattleAnime outer loop) ----
            for (int paletteBank = 0; paletteBank < maxPalette; paletteBank++)
            {
                // Filter the source image to only pixels belonging to this bank
                // (palette index paletteBank*16 … paletteBank*16+15 → 0…15)
                byte[] bankPixels = ExtractByPaletteBank(indexedPixels, width, height, paletteBank);

                bool ok = PackTilesIntoSeat(
                    bankPixels, width, height,
                    seat,
                    paletteBank, oamBytes,
                    isMagic);

                if (!ok)
                    return Fail($"Seat full while packing palette bank {paletteBank} (image is too large or seat has too little space)");
            }

            // ---- Append terminator OAM entry ----
            AppendTerminator(oamBytes);

            // ---- Encode seat as 4bpp tile data ----
            byte[] tileData4bpp = EncodeSeat4bpp(seat);

            return new OAMAssembleResult
            {
                TileData4bpp = tileData4bpp,
                PaletteBytes = gbaPalette,   // returned as-is; caller LZ77-compresses as needed
                OamRecords   = oamBytes.ToArray(),
                Error        = null,
            };
        }

        // ====================================================================
        // Seat — the mutable tile-sheet accumulator
        // ====================================================================

        /// <summary>
        /// Represents the 256×H tile sheet that accumulates sprite tiles across
        /// frames.  Tracks which 8×8 tile positions are already filled.
        /// </summary>
        public sealed class Seat
        {
            // Pixel buffer for the seat, 1 byte per pixel (palette index)
            internal byte[] Pixels;
            internal bool[] Used;   // one bool per 8×8 tile

            internal readonly int TileW;  // seat width in tiles  (= 32)
            internal readonly int TileH;  // seat height in tiles (= 8 or 4)
            internal readonly int PixW;   // seat width in pixels
            internal readonly int PixH;   // seat height in pixels

            public Seat(int tileW, int tileH)
            {
                TileW  = tileW;
                TileH  = tileH;
                PixW   = tileW * 8;
                PixH   = tileH * 8;
                Pixels = new byte[PixW * PixH];
                Used   = new bool[TileW * TileH];
            }
        }

        // ====================================================================
        // Core packing algorithm
        // ====================================================================

        /// <summary>
        /// For one palette bank, scan every non-blank 8×8 tile in the source
        /// image and pack it (or a larger rectangle of contiguous non-blank tiles)
        /// into the seat, emitting one OAM record per rectangle.
        ///
        /// Mirrors <c>ImageUtilOAM.ImportOAM.MakeBattleAnimeLow</c>.
        /// </summary>
        static bool PackTilesIntoSeat(
            byte[] bankPixels, int imgW, int imgH,
            Seat   seat,
            int    paletteBank,
            List<byte> oamOut,
            bool   isMagic)
        {
            int screenTileW = imgW  / 8;
            int screenTileH = imgH  / 8;

            // Mark blank tiles (all zeros) as already "used" so we skip them
            bool[] useTileData = MakeUseTileData(bankPixels, imgW, imgH);

            int end = useTileData.Length;
            for (int i = 0; i < end; i++)
            {
                if (useTileData[i])  // blank or already emitted
                    continue;

                int bx = i % screenTileW;  // tile col in image
                int by = i / screenTileW;  // tile row in image

                // Compute vram position (signed, relative to screen center)
                int vramX = bx * 8 - (isMagic ? BITMAP_SPELL_ADDX : BITMAP_ADDX);
                int vramY = by * 8 - BITMAP_ADDY;

                // Try to pack the largest rectangle first (matches WF ordering)
                // WF skips 8×8 for magic (seat half-height can't accommodate them)
                int sx, sy;
                if (!isMagic)
                {
                    if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 8, 8, out sx, out sy))
                    { EmitOAM(oamOut, SHAPE_SQUARE, SIZE_TIMES8, sx, sy, vramX, vramY, paletteBank); continue; }
                }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 8, 4, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_HORIZONTAL, SIZE_TIMES8, sx, sy, vramX, vramY, paletteBank); continue; }
                if (!isMagic)
                {
                    if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 4, 8, out sx, out sy))
                    { EmitOAM(oamOut, SHAPE_VERTICAL, SIZE_TIMES8, sx, sy, vramX, vramY, paletteBank); continue; }
                }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 4, 4, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_SQUARE, SIZE_TIMES4, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 4, 2, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_HORIZONTAL, SIZE_TIMES4, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 2, 4, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_VERTICAL, SIZE_TIMES4, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 2, 2, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_SQUARE, SIZE_TIMES2, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 4, 1, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_HORIZONTAL, SIZE_TIMES2, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 1, 4, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_VERTICAL, SIZE_TIMES2, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 2, 1, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_HORIZONTAL, SIZE_TIMES1, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 1, 2, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_VERTICAL, SIZE_TIMES1, sx, sy, vramX, vramY, paletteBank); continue; }
                if (TryCopy(bankPixels, useTileData, imgW, imgH, seat, i, 1, 1, out sx, out sy))
                { EmitOAM(oamOut, SHAPE_SQUARE, SIZE_TIMES1, sx, sy, vramX, vramY, paletteBank); continue; }

                // Seat is full — caller must create a new seat
                return false;
            }
            return true;
        }

        // ====================================================================
        // tryCopy — port of WF ImageUtilOAM.ImportOAM.tryCopy
        // ====================================================================

        /// <summary>
        /// Attempt to place a w×h tile rectangle starting at image-tile index
        /// <paramref name="imgTileIdx"/> into the seat.
        ///
        /// Steps (matching WF tryCopy exactly):
        ///   1. Check the w×h rectangle fits within the image bounds.
        ///   2. Verify none of the w×h tiles are already processed (useTileData true).
        ///   3. Search the seat for an identical block that was placed earlier (reuse).
        ///   4. If not found, search the seat for an empty w×h slot and copy pixels in.
        ///   5. On success mark image tiles as used and return the seat tile position.
        /// </summary>
        static bool TryCopy(
            byte[]   bankPixels, bool[] useTileData, int imgW, int imgH,
            Seat     seat,
            int      imgTileIdx,    // linearized tile index in image
            int      w, int h,      // rectangle size in tiles
            out int  outSeatTileX,
            out int  outSeatTileY)
        {
            outSeatTileX = 0;
            outSeatTileY = 0;

            int imgTileW = imgW / 8;
            int imgTileH = imgH / 8;

            int bx = imgTileIdx % imgTileW;
            int by = imgTileIdx / imgTileW;

            // 1. Bounds check on image
            if (bx + w > imgTileW) return false;
            if (by + h > imgTileH) return false;

            // 2. All w×h source tiles must be unprocessed (not blank/used)
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                if (useTileData[(bx + dx) + (by + dy) * imgTileW])
                    return false;
            }

            // Extract the w×h pixel block from the source image
            byte[] block = ExtractBlock(bankPixels, imgW, bx * 8, by * 8, w * 8, h * 8);

            // 3. Search seat for identical existing block (tile-aligned scan)
            if (GrepBlockInSeat(seat, block, w * 8, h * 8, out outSeatTileX, out outSeatTileY))
            {
                // Reuse — just mark image tiles as used
                MarkUsed(useTileData, bx, by, w, h, imgTileW);
                return true;
            }

            // 4. Find an empty w×h slot in the seat
            for (int sy = 0; sy <= seat.TileH - h; sy++)
            for (int sx = 0; sx <= seat.TileW - w; sx++)
            {
                if (SeatSlotEmpty(seat, sx, sy, w, h))
                {
                    // Copy block into seat
                    CopyBlockToSeat(seat, block, w * 8, h * 8, sx, sy);
                    // Mark seat slots as used
                    MarkUsed(seat.Used, sx, sy, w, h, seat.TileW);
                    // Mark image tiles as used
                    MarkUsed(useTileData, bx, by, w, h, imgTileW);

                    outSeatTileX = sx;
                    outSeatTileY = sy;
                    return true;
                }
            }

            // Seat is full for this rectangle size
            return false;
        }

        // ====================================================================
        // Indexed-pixel helpers (Bitmap-free equivalents of WF ops)
        // ====================================================================

        /// <summary>
        /// Build a "use-tile-data" map for <paramref name="bankPixels"/>.
        /// A tile is considered blank (marked <c>true</c> = skip) when every
        /// pixel in the 8×8 block is 0 (transparent).
        /// The top-right tile is always marked as used (FEditorAdv palette-map area).
        ///
        /// Ports WF <c>ImageUtil.MakeUseTileData(Bitmap)</c> + the static
        /// <c>MakeUseTileData(byte[], int, int)</c> variant in <c>ImportOAM</c>.
        /// </summary>
        internal static bool[] MakeUseTileData(byte[] pixels, int imgW, int imgH)
        {
            int tileW = imgW / 8;
            int tileH = imgH / 8;
            bool[] result = new bool[tileW * tileH];

            for (int ty = 0; ty < tileH; ty++)
            for (int tx = 0; tx < tileW; tx++)
            {
                bool blank = true;
                for (int py = 0; py < 8 && blank; py++)
                for (int px = 0; px < 8 && blank; px++)
                {
                    int idx = (ty * 8 + py) * imgW + (tx * 8 + px);
                    if (pixels[idx] != 0) blank = false;
                }
                result[tx + ty * tileW] = blank;  // blank → skip
            }

            // Top-right tile is the FEditorAdv palette-map area — always skip
            result[tileW - 1] = true;

            return result;
        }

        /// <summary>
        /// Extract a rectangular pixel block from <paramref name="source"/>
        /// (row-major, 1 byte/pixel, width <paramref name="imgW"/>).
        /// The block starts at pixel (srcX, srcY) and has size blockW×blockH.
        /// Returns a new row-major byte[] of size blockW×blockH.
        /// </summary>
        internal static byte[] ExtractBlock(byte[] source, int imgW,
                                            int srcX, int srcY,
                                            int blockW, int blockH)
        {
            byte[] block = new byte[blockW * blockH];
            for (int y = 0; y < blockH; y++)
            {
                int srcRow = (srcY + y) * imgW + srcX;
                int dstRow = y * blockW;
                Array.Copy(source, srcRow, block, dstRow, blockW);
            }
            return block;
        }

        /// <summary>
        /// Filter <paramref name="source"/> so that only pixels belonging to
        /// palette bank <paramref name="paletteBank"/> remain; all others become 0.
        /// Within the result, pixel values are re-based to 0..15 (subtract bank*16).
        ///
        /// Ports WF <c>ImageUtil.CopyByPalette</c>.
        /// </summary>
        internal static byte[] ExtractByPaletteBank(
            byte[] source, int imgW, int imgH, int paletteBank)
        {
            int bankStart = paletteBank * 16;
            int bankEnd   = bankStart + 16;
            byte[] result = new byte[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                byte v = source[i];
                if (v >= bankStart && v < bankEnd)
                    result[i] = (byte)(v - bankStart);
                // else remains 0 (transparent)
            }
            return result;
        }

        /// <summary>
        /// Search the seat for an existing pixel block that matches
        /// <paramref name="block"/> exactly, at 8-pixel-aligned positions.
        /// On success sets outSeatTileX/Y (tile coords) and returns true.
        ///
        /// Ports WF <c>ImageUtil.GrepTileBitmap</c>.
        /// </summary>
        static bool GrepBlockInSeat(
            Seat   seat,
            byte[] block, int blockW, int blockH,
            out int outSeatTileX, out int outSeatTileY)
        {
            int searchW = seat.PixW - blockW;
            int searchH = seat.PixH - blockH;

            for (int sy = 0; sy <= searchH; sy += 8)
            for (int sx = 0; sx <= searchW; sx += 8)
            {
                // Compare block against seat at (sx, sy)
                bool match = true;
                for (int by = 0; by < blockH && match; by++)
                {
                    int seatRowStart = (sy + by) * seat.PixW + sx;
                    int blkRowStart  = by * blockW;
                    for (int bx = 0; bx < blockW && match; bx++)
                    {
                        if (seat.Pixels[seatRowStart + bx] != block[blkRowStart + bx])
                            match = false;
                    }
                }
                if (match)
                {
                    outSeatTileX = sx / 8;
                    outSeatTileY = sy / 8;
                    return true;
                }
            }
            outSeatTileX = 0;
            outSeatTileY = 0;
            return false;
        }

        /// <summary>
        /// Check whether a w×h tile region of the seat (starting at seat tile sx,sy)
        /// is entirely empty (all <c>Used</c> flags false).
        /// </summary>
        static bool SeatSlotEmpty(Seat seat, int sx, int sy, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                if (seat.Used[(sx + dx) + (sy + dy) * seat.TileW])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Copy a pixel block into the seat at tile position (seatTileX, seatTileY).
        /// </summary>
        static void CopyBlockToSeat(Seat seat, byte[] block,
                                    int blockW, int blockH,
                                    int seatTileX, int seatTileY)
        {
            int dstX = seatTileX * 8;
            int dstY = seatTileY * 8;
            for (int y = 0; y < blockH; y++)
            {
                int seatRow = (dstY + y) * seat.PixW + dstX;
                int blkRow  = y * blockW;
                Array.Copy(block, blkRow, seat.Pixels, seatRow, blockW);
            }
        }

        /// <summary>
        /// Mark a w×h rectangle of <paramref name="flags"/> as used (true),
        /// starting at tile position (startX, startY) in a grid of width
        /// <paramref name="gridW"/> tiles.
        /// </summary>
        static void MarkUsed(bool[] flags, int startX, int startY, int w, int h, int gridW)
        {
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                flags[(startX + dx) + (startY + dy) * gridW] = true;
        }

        // ====================================================================
        // OAM record emission
        // ====================================================================

        /// <summary>
        /// Emit a 12-byte OAM entry into <paramref name="oam"/>.
        ///
        /// FE custom OAM format (per <see cref="BattleAnimeRendererCore"/>):
        /// <list type="bullet">
        ///   <item>[0]     = 0x00 (normal entry)</item>
        ///   <item>[1]     = align: shape (bits 6-7) | 0 (no rotation)</item>
        ///   <item>[2]     = 0x00</item>
        ///   <item>[3]     = area:  size  (bits 6-7) | 0 (no flip)</item>
        ///   <item>[4]     = seat tile ref: (seatX &amp; 0x1F) | ((seatY &lt;&lt; 5) &amp; 0xE0)</item>
        ///   <item>[5]     = palette bank in bits 4-7</item>
        ///   <item>[6..7]  = vramX as signed 16-bit LE</item>
        ///   <item>[8..9]  = vramY as signed 16-bit LE</item>
        ///   <item>[10..11]= 0x00 0x00</item>
        /// </list>
        ///
        /// Mirrors WF <c>AppendOAM</c>.
        /// </summary>
        static void EmitOAM(
            List<byte> oam,
            byte alignShape, byte areaSize,
            int seatTileX, int seatTileY,
            int vramX,     int vramY,
            int paletteBank)
        {
            oam.Add(0x00);
            oam.Add(alignShape);
            oam.Add(0x00);
            oam.Add(areaSize);

            // Tile reference: low 5 bits = tile X, high 3 bits (bits 5-7) = tile Y
            oam.Add((byte)((seatTileX & 0x1F) | ((seatTileY << 5) & 0xE0)));
            oam.Add((byte)((paletteBank & 0xF) << 4));

            // vramX as signed 16-bit little-endian
            short sx = (short)vramX;
            oam.Add((byte)(sx & 0xFF));
            oam.Add((byte)((sx >> 8) & 0xFF));

            // vramY as signed 16-bit little-endian
            short sy = (short)vramY;
            oam.Add((byte)(sy & 0xFF));
            oam.Add((byte)((sy >> 8) & 0xFF));

            oam.Add(0x00);
            oam.Add(0x00);
        }

        /// <summary>
        /// Append the standard 12-byte OAM end-of-list terminator.
        /// Mirrors WF <c>AppendTermOAM</c>.
        /// </summary>
        static void AppendTerminator(List<byte> oam)
        {
            oam.Add(0x01);
            for (int i = 0; i < 11; i++) oam.Add(0x00);
        }

        // ====================================================================
        // 4bpp tile encoding — port of WF ImageUtil.ImageToByte16Tile
        // ====================================================================

        /// <summary>
        /// Encode the seat's indexed-pixel buffer into GBA 4bpp tile format.
        ///
        /// GBA 4bpp tile layout: each 8×8 tile is stored as 32 bytes, two pixels
        /// packed per byte (low nibble = left pixel, high nibble = right pixel),
        /// tiles arranged left-to-right then top-to-bottom.
        ///
        /// Ports WF <c>ImageUtil.ImageToByte16Tile</c>.
        /// </summary>
        static byte[] EncodeSeat4bpp(Seat seat)
        {
            int tileW = seat.TileW;
            int tileH = seat.TileH;
            int totalTiles = tileW * tileH;
            byte[] data = new byte[totalTiles * 32]; // 32 bytes per 8×8 4bpp tile

            int outIdx = 0;
            for (int ty = 0; ty < tileH; ty++)
            for (int tx = 0; tx < tileW; tx++)
            {
                int baseX = tx * 8;
                int baseY = ty * 8;

                for (int py = 0; py < 8; py++)
                {
                    int rowBase = (baseY + py) * seat.PixW + baseX;
                    for (int px = 0; px < 8; px += 2)
                    {
                        byte lo = (byte)(seat.Pixels[rowBase + px]     & 0x0F);
                        byte hi = (byte)(seat.Pixels[rowBase + px + 1] & 0x0F);
                        data[outIdx++] = (byte)(lo | (hi << 4));
                    }
                }
            }
            return data;
        }

        // ====================================================================
        // Convert Left-to-Right OAM
        // ====================================================================

        /// <summary>
        /// Mirror an OAM record array for the left-to-right (enemy) facing direction.
        /// Sets the horizontal-flip bit and negates the X position.
        ///
        /// Ports WF <c>ImageUtilOAM.ConvertLeftToRightOAM</c>.
        /// </summary>
        public static byte[] ConvertToLeftToRightOAM(byte[] rightToLeftOAM)
        {
            if (rightToLeftOAM == null) return null;

            byte[] result = (byte[])rightToLeftOAM.Clone();
            for (int i = 0; i + 12 <= result.Length; i += 12)
            {
                // Terminator
                if (result[i] == 0x01) continue;
                // Affine matrix entry
                if (result[i + 2] == 0xFF && result[i + 3] == 0xFF) continue;

                byte align = result[i + 1];
                byte area  = result[i + 3];

                // Decode sprite width in tiles
                GetSpriteSize(align, area, out int widthTiles, out _);

                // Set horizontal-flip bit (bit 5 of area byte)
                result[i + 3] |= 0x20;

                // Negate vramX: new_vramX = -(width*8) - old_vramX
                short oldVramX = (short)(result[i + 6] | (result[i + 7] << 8));
                short newVramX = (short)(-(widthTiles * 8) - oldVramX);
                result[i + 6] = (byte)(newVramX & 0xFF);
                result[i + 7] = (byte)((newVramX >> 8) & 0xFF);
            }
            return result;
        }

        /// <summary>
        /// Decode sprite width and height (in tiles) from OAM align and area bytes.
        /// Mirrors WF <c>DrawOAM.convertAlignAreaToWH</c> and
        /// <see cref="BattleAnimeRendererCore.GetOAMSize"/>.
        /// </summary>
        public static void GetSpriteSize(byte align, byte area,
                                         out int widthTiles, out int heightTiles)
        {
            int shapeBits = align & 0xC0;
            int sizeBits  = area  & 0xC0;

            widthTiles  = 0;
            heightTiles = 0;

            if (sizeBits == SIZE_TIMES8)
            {
                if      (shapeBits == SHAPE_VERTICAL)   { widthTiles = 4; heightTiles = 8; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 8; heightTiles = 4; }
                else                                     { widthTiles = 8; heightTiles = 8; }
            }
            else if (sizeBits == SIZE_TIMES4)
            {
                if      (shapeBits == SHAPE_VERTICAL)   { widthTiles = 2; heightTiles = 4; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 4; heightTiles = 2; }
                else                                     { widthTiles = 4; heightTiles = 4; }
            }
            else if (sizeBits == SIZE_TIMES2)
            {
                if      (shapeBits == SHAPE_VERTICAL)   { widthTiles = 1; heightTiles = 4; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 4; heightTiles = 1; }
                else                                     { widthTiles = 2; heightTiles = 2; }
            }
            else // SIZE_TIMES1
            {
                if      (shapeBits == SHAPE_VERTICAL)   { widthTiles = 1; heightTiles = 2; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 2; heightTiles = 1; }
                else                                     { widthTiles = 1; heightTiles = 1; }
            }
        }

        // ====================================================================
        // Helper
        // ====================================================================

        static OAMAssembleResult Fail(string reason) =>
            new OAMAssembleResult { Error = reason };
    }
}
