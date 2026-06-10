// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform render helper for the TSA Tile Editor (#808, follow-up to #769
// item 1). Renders the TSA-composited "main image" (the WinForms
// ImageTSAEditorForm Battle canvas) so the Avalonia ImageTSAEditorView can show
// it and export it to PNG read-only -- no System.Drawing / WinForms dependency.
//
// WinForms reference (FEBuilderGBA/ImageTSAEditorForm.cs):
//   * GetChipImage:    image = rom.p32(zimgPointer); tileData = LZ77.decompress
//                      (rom.Data, image) -> 4bpp tiles, 0x20 bytes each.
//   * palette base:    palettePointer==NOT_FOUND ? toOffset(paletteAddress)
//                      : rom.p32(palettePointer).  Read RAW (no LZ77).
//   * LoadBattleScreen: LZ77 TSA -> decompress then ByteToTSA/ByteToHeaderTSA at
//                       offset 0; raw TSA -> ByteToTSA/ByteToHeaderTSA(rom.Data,
//                       tsapos).
//
// This helper takes ALREADY-RESOLVED data addresses (ROM offsets) so the
// read-only render carries no pointer-slot / repoint coupling; the ViewModel
// resolves the slots exactly like WF before calling in.
//
// Decode reuse (FEBuilderGBA.Core/ImageUtilCore.cs):
//   * DecodeTSA       (mirrors ByteToTSA, has a byte tsaOffset parameter)
//   * DecodeHeaderTSA (mirrors ByteToHeaderTSA, has NO offset parameter)
//   * DecodeTileToPixels (4bpp, per-cell palette bank, index0 = transparent)
//
// Note: DecodeTSA uses the full 10-bit TSA tile index (& 0x3FF) like the generic
// ImageUtil.ByteToImage16Tile, whereas the WF TSA *canvas* masks tile numbers to
// 8 bits (m & 0xff). For TSA entries whose tile index is >= 0x100 the exported
// image is therefore intentionally NOT byte-identical to the WF canvas. This is
// acceptable for a read-only export (the 10-bit value is the on-ROM truth).

using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform render helper for the TSA Tile Editor main image (#808).
    /// See the file-level comment for the WinForms reference semantics and the
    /// documented 10-bit-vs-8-bit tile-index divergence.
    /// </summary>
    public static class ImageTSAEditorCore
    {
        // 16 palette banks * 16 colors * 2 bytes = 512 bytes (full 4bpp palette).
        const int PALETTE_BYTES = 16 * 16 * 2;

        // Largest possible header-TSA payload: 2 header bytes + 32 x 32 u16 cells.
        // DecodeHeaderTSA accepts header dimensions up to 32 x 32, so slicing this
        // many bytes covers every valid header regardless of width8/height8.
        const int HEADER_TSA_MAX_BYTES = 2 + (32 * 32 * 2);

        /// <summary>
        /// Render the TSA-composited main image from already-resolved ROM data
        /// addresses. Returns the composed <see cref="IImage"/> sized
        /// <paramref name="width8"/>*8 x <paramref name="height8"/>*8 pixels, or
        /// <c>null</c> on any null/out-of-bounds/corrupt input (never throws).
        ///
        /// 8-arg back-compat wrapper (#1074 refinement #4): delegates to the
        /// 10-arg overload with <c>image2Addr: 0</c> + <c>isCompressedPalette:
        /// false</c>, so this call is BYTE-IDENTICAL to the pre-#1074 behaviour
        /// (#1030 entrypoints, the #810 tests).
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="width8">Canvas width in 8-pixel tiles.</param>
        /// <param name="height8">Canvas height in 8-pixel tiles.</param>
        /// <param name="imageAddr">Resolved ROM offset of the LZ77 tile image.</param>
        /// <param name="isHeaderTSA">True if the TSA stream carries a {w,h} header.</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaAddr">Resolved ROM offset of the TSA stream.</param>
        /// <param name="paletteAddr">Resolved ROM offset of the palette block.</param>
        public static IImage TryRenderMainImage(ROM rom, uint width8, uint height8,
            uint imageAddr, bool isHeaderTSA, bool isLZ77TSA, uint tsaAddr, uint paletteAddr)
            => TryRenderMainImage(rom, width8, height8, imageAddr, isHeaderTSA,
                isLZ77TSA, tsaAddr, paletteAddr, 0u, false);

        /// <summary>
        /// Render the TSA-composited main image with optional <b>image2-join</b>
        /// and <b>compressed-palette</b> support (#1074). Returns the composed
        /// <see cref="IImage"/> sized <paramref name="width8"/>*8 x
        /// <paramref name="height8"/>*8 pixels, or <c>null</c> on any
        /// null/out-of-bounds/corrupt input (never throws).
        ///
        /// With the defaults (<paramref name="image2Addr"/> = 0,
        /// <paramref name="isCompressedPalette"/> = false) this is BYTE-IDENTICAL
        /// to the pre-#1074 8-arg render path (#1030 byte-identical).
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="width8">Canvas width in 8-pixel tiles.</param>
        /// <param name="height8">Canvas height in 8-pixel tiles.</param>
        /// <param name="imageAddr">Resolved ROM offset of the LZ77 tile image.</param>
        /// <param name="isHeaderTSA">True if the TSA stream carries a {w,h} header.</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaAddr">Resolved ROM offset of the TSA stream.</param>
        /// <param name="paletteAddr">Resolved ROM offset of the palette block.</param>
        /// <param name="image2Addr">Resolved ROM offset of a SECOND LZ77 tile
        ///   image to join after the first (WF <c>ImageOption == 2</c>, order
        ///   <c>image ++ image2</c>). 0 or invalid ⇒ single image (no join).</param>
        /// <param name="isCompressedPalette">True if the palette block is an LZ77
        ///   stream (WF <c>PaletteOption == 1</c>); false ⇒ RAW palette read.</param>
        public static IImage TryRenderMainImage(ROM rom, uint width8, uint height8,
            uint imageAddr, bool isHeaderTSA, bool isLZ77TSA, uint tsaAddr, uint paletteAddr,
            uint image2Addr = 0, bool isCompressedPalette = false)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (width8 == 0 || height8 == 0) return null;

            // Shared LZ77-image-decode + palette read (raw OR compressed, with the
            // truncation guards). See TryLoadTSATileAndPalette. RenderChipList /
            // RenderRawTilesheet reuse the SAME loader (raw palette only).
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette,
                                          isCompressedPalette))
            {
                return null;
            }

            // --- image2 join (#1074, mirrors WF ImageOption == 2; order
            // image ++ image2) ---
            // If image2Addr is a valid non-zero LZ77 pointer, decompress it and
            // concatenate AFTER the first image's tiles so a TSA cell with a tile
            // index >= image1's tile count reaches the joined image2 tiles. An
            // invalid / zero image2 is a no-op (single image). Never throws.
            if (image2Addr != 0
                && U.isSafetyOffset(image2Addr, rom)
                && IsLZ77HeaderSafe(rom, image2Addr))
            {
                uint img2Compressed = LZ77.getCompressedSize(rom.Data, image2Addr);
                if (img2Compressed != 0
                    && (ulong)image2Addr + img2Compressed <= (ulong)rom.Data.Length)
                {
                    byte[] tile2 = LZ77.decompress(rom.Data, image2Addr);
                    if (tile2 != null && tile2.Length != 0)
                    {
                        byte[] joined = new byte[tileData.Length + tile2.Length];
                        Array.Copy(tileData, 0, joined, 0, tileData.Length);
                        Array.Copy(tile2, 0, joined, tileData.Length, tile2.Length);
                        tileData = joined;
                    }
                }
            }

            int wTiles = (int)width8;
            int hTiles = (int)height8;

            if (!U.isSafetyOffset(tsaAddr, rom)) return null;

            // --- TSA data ---
            if (isLZ77TSA)
            {
                // #1074 refinement #1: a TSA pointer in the last 1-3 ROM bytes
                // passes isSafetyOffset yet would throw inside getCompressedSize
                // (it reads input[tsaAddr+3]). Guard the full 4-byte LZ77 header
                // BEFORE getCompressedSize (mirrors the image path).
                if (!IsLZ77HeaderSafe(rom, tsaAddr)) return null;
                uint tsaCompressed = LZ77.getCompressedSize(rom.Data, tsaAddr);
                if (tsaCompressed == 0) return null;
                if ((ulong)tsaAddr + tsaCompressed > (ulong)rom.Data.Length) return null;
                byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaData == null || tsaData.Length == 0) return null;

                return isHeaderTSA
                    ? ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, wTiles, hTiles, true, 0, 0)
                    : ImageUtilCore.DecodeTSA(tileData, tsaData, palette, wTiles, hTiles, true, 0);
            }

            // RAW (uncompressed) TSA stored in ROM at tsaAddr.
            if (isHeaderTSA)
            {
                // DecodeHeaderTSA has no byte-offset parameter, so slice a bounded
                // window from tsaAddr instead of copying the whole ROM tail.
                long tail = (long)rom.Data.Length - tsaAddr;
                if (tail <= 0) return null;
                int sliceLen = (int)Math.Min((long)HEADER_TSA_MAX_BYTES, tail);
                byte[] tsaData = new byte[sliceLen];
                Array.Copy(rom.Data, tsaAddr, tsaData, 0, sliceLen);
                return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, wTiles, hTiles, true, 0, 0);
            }

            // RAW non-header: DecodeTSA accepts a byte offset and caps reads to
            // wTiles*hTiles entries, so pass rom.Data directly (no large slice).
            if (tsaAddr > int.MaxValue) return null;
            return ImageUtilCore.DecodeTSA(tileData, rom.Data, palette, wTiles, hTiles, true, (int)tsaAddr);
        }

        /// <summary>
        /// Render the chip-list thumbnail (read-only) from the already-resolved
        /// image + palette ROM addresses (#819, follow-up to #808/#810/#807).
        ///
        /// Mirrors the WinForms <c>ImageTSAEditorForm.MakeCHIPLIST</c> grid: the
        /// LZ77 main-image tiles laid out in a 4-column permutation strip --
        /// col0=original, col1=H-flip, col2=V-flip, col3=HV-flip -- one 8x8 cell
        /// per tile-row, all in the <b>default palette bank 0</b> (WF reads the
        /// selected palette index; live re-render on palette change is out of
        /// scope -- the read-only thumbnail shows bank 0). Index 0 renders
        /// OPAQUE: WF blits col0 with transparent index 0 and cols1-3 opaque,
        /// but both resolve to palette color 0 over WF's Blank(color0)
        /// background, so an opaque index-0 render reproduces the WF thumbnail
        /// (same choice as the #807 8-column battle-screen sibling).
        ///
        /// The chip list is a pure tile-strip -- it never touches the TSA
        /// stream -- so this takes NO <c>tsaAddr</c>.
        ///
        /// Output size is <c>32 x (tileCount * 8)</c> pixels (4 cols * 8px = 32
        /// wide; tileCount rows * 8px tall), exactly WF's
        /// <c>Blank(ChipCache.Width * 4, ChipCache.Height)</c>. Returns
        /// <c>null</c> (never throws) on any null/out-of-bounds/corrupt input or
        /// when no <see cref="IImageService"/> is set.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="imageAddr">Resolved ROM offset of the LZ77 tile image.</param>
        /// <param name="paletteAddr">Resolved ROM offset of the palette block.</param>
        public static IImage RenderChipList(ROM rom, uint imageAddr, uint paletteAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // Same LZ77-image-decode + raw-palette read as TryRenderMainImage
            // (inherits the isSafetyOffset / getCompressedSize / end-of-ROM
            // truncation guards). The chip list never reads the TSA stream and
            // always uses a RAW palette (isCompressedPalette: false).
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette,
                                          isCompressedPalette: false))
            {
                return null;
            }

            // WF ChipCache is an 8px-wide tile sheet (one 8x8 tile per 8px row).
            // tile count = tileData.Length / 32 (4bpp = 32 bytes/tile).
            const int bytesPerTile = 32;
            int tileCount = tileData.Length / bytesPerTile;
            if (tileCount <= 0) return null;

            // WF MakeCHIPLIST: ChipDisplayCache = Blank(ChipCache.Width * 4,
            // ChipCache.Height) -> 8*4 = 32 wide, tileCount*8 tall. SINGLE bank
            // (this is the structural difference from the #807 8-col/2-bank
            // battle-screen chip list).
            int width = 8 * 4;          // 4 columns of 8px
            int height = tileCount * 8;

            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            // Column layout: {orig, Hflip, Vflip, HVflip}, all palette bank 0 --
            // exactly WF MakeCHIPLIST's blit order (Copy / Copy(true) /
            // Copy(false,true) / Copy(true,true)).
            // (hFlip, vFlip)
            (bool h, bool v)[] columns =
            {
                (false, false), // col 0 = original
                (true,  false), // col 1 = H-flip
                (false, true),  // col 2 = V-flip
                (true,  true),  // col 3 = HV-flip
            };

            for (int tile = 0; tile < tileCount; tile++)
            {
                int tileY = tile * 8;
                for (int col = 0; col < columns.Length; col++)
                {
                    var (h, v) = columns[col];
                    ImageUtilCore.DecodeTileToPixels(
                        tileData, tile, palette, 0 /* palBank */,
                        pixels, width, col * 8, tileY,
                        h, v, is4bpp: true, opaqueIndex0: true);
                }
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Render the RAW tilesheet (read-only, #974) from the already-resolved
        /// image + palette ROM addresses — the WinForms <c>image1_Export</c>
        /// raw 4bpp tile dump, NOT the TSA-composited canvas (#808) and NOT the
        /// 4-column flip strip (#819).
        ///
        /// The ZImg stream is LZ77-decompressed (via the shared
        /// <see cref="TryLoadTSATileAndPalette"/> loader, inheriting the
        /// isSafetyOffset / getCompressedSize / end-of-ROM truncation guards) and
        /// the 4bpp tiles are laid out left-to-right, top-to-bottom in an
        /// <b>8-tile-wide</b> strip — 64px wide, <c>ceil(tileCount / 8) * 8</c>
        /// tall — exactly like a plain 4bpp tilesheet PNG. The final row may be
        /// partially filled; cells beyond the last tile are left as the
        /// zero-initialized (blank/transparent) pixel-buffer background because
        /// they are simply never drawn. Note the tiles themselves are decoded
        /// with <c>opaqueIndex0:true</c>, so palette index 0 WITHIN a drawn tile
        /// is opaque (it is only the never-drawn padding cells that stay
        /// transparent).
        ///
        /// The raw tilesheet never reads the TSA stream, so this takes NO
        /// <c>tsaAddr</c>. Always renders in palette bank 0. Returns <c>null</c>
        /// (never throws) on any null / out-of-bounds / corrupt input or when no
        /// <see cref="IImageService"/> is set.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="imageAddr">Resolved ROM offset of the LZ77 tile image.</param>
        /// <param name="paletteAddr">Resolved ROM offset of the palette block.</param>
        public static IImage RenderRawTilesheet(ROM rom, uint imageAddr, uint paletteAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // Same LZ77-image-decode + raw-palette read as TryRenderMainImage /
            // RenderChipList. The raw tilesheet never reads the TSA stream and
            // always uses a RAW palette (isCompressedPalette: false).
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette,
                                          isCompressedPalette: false))
            {
                return null;
            }

            const int bytesPerTile = 32; // 4bpp = 32 bytes/tile
            int tileCount = tileData.Length / bytesPerTile;
            if (tileCount <= 0) return null;

            // 8-tile-wide strip; height rounds up to a full row so the last
            // (possibly partial) row of tiles is included.
            const int tilesPerRow = 8;
            int rows = (tileCount + tilesPerRow - 1) / tilesPerRow;
            int width = tilesPerRow * 8;   // 64px
            int height = rows * 8;

            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA, zero-filled = transparent padding

            for (int tile = 0; tile < tileCount; tile++)
            {
                int col = tile % tilesPerRow;
                int row = tile / tilesPerRow;
                ImageUtilCore.DecodeTileToPixels(
                    tileData, tile, palette, 0 /* palBank */,
                    pixels, width, col * 8, row * 8,
                    hFlip: false, vFlip: false, is4bpp: true, opaqueIndex0: true);
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Shared loader extracted from <see cref="TryRenderMainImage"/> (#819):
        /// LZ77-decode the tile image and read the palette block from
        /// already-resolved ROM addresses. Behaviour-preserving -- returns
        /// <c>false</c> exactly where the inline block previously returned
        /// <c>null</c> (the #810 <see cref="TryRenderMainImage"/> tests pin this).
        ///
        /// Validates the compressed image stream BEFORE decompressing:
        /// <c>LZ77.decompress</c> silently returns a zero-filled buffer on a
        /// truncated stream, so <c>getCompressedSize == 0</c> + an end-of-ROM
        /// bound check is the truncation guard (mirrors ImageBattleScreenCore's
        /// TryLoadChipsetAndPalette).
        ///
        /// Palette read (#1074):
        /// <list type="bullet">
        ///   <item><paramref name="isCompressedPalette"/> <c>false</c> — RAW read
        ///     (no LZ77), clamped to ROM end (≤512 bytes / 16 banks). UNCHANGED:
        ///     byte-for-byte identical to the pre-#1074 loader for every existing
        ///     caller. <c>DecodeTileToPixels</c> bounds-checks short palettes so a
        ///     clamped read is safe.</item>
        ///   <item><paramref name="isCompressedPalette"/> <c>true</c> — the palette
        ///     address is an LZ77 stream (WF <c>PaletteOption == 1</c>): guard the
        ///     full 4-byte header (<see cref="IsLZ77HeaderSafe"/>) +
        ///     <c>getCompressedSize != 0</c> + an end-of-ROM bound, decompress,
        ///     then clamp to <see cref="PALETTE_BYTES"/> (512) like WF
        ///     <c>U.subrange(decompressed, 0, 0x20*16)</c> — take up to 512 bytes
        ///     (fewer if the decompressed buffer is shorter; NOT required to be
        ///     exactly 512). A decode FAILURE (bad header / null / empty) returns
        ///     <c>false</c> — NO silent fall back to a raw read (so the
        ///     paletteType==1 failure surfaces as a null render, #1074
        ///     refinement #2).</item>
        /// </list>
        /// </summary>
        static bool TryLoadTSATileAndPalette(ROM rom, uint imageAddr, uint paletteAddr,
            out byte[] tiles, out byte[] palette, bool isCompressedPalette)
        {
            tiles = null;
            palette = null;

            // --- Tile data: LZ77 image (validate the compressed stream BEFORE
            // decompress; truncation guard via getCompressedSize==0 + end-of-ROM
            // bound check). ---
            // The GBA LZ77 header is 4 bytes (0x10 + a 3-byte uncompressed size).
            // LZ77.getCompressedSize reads input[offset+3] but only rejects when
            // FEWER THAN 3 bytes remain, so a pointer to the LAST 1-3 bytes of the
            // ROM passes isSafetyOffset yet makes that header read throw
            // IndexOutOfRangeException. Require the FULL 4-byte header to be
            // in-bounds BEFORE any LZ77 call (mirrors ImageBattleScreenCore's
            // IsLZ77HeaderSafe / IsRegionSafe, #818) so this null-safe loader
            // returns false instead of throwing -- this also hardens
            // TryRenderMainImage (#810), which now delegates here.
            if (!IsLZ77HeaderSafe(rom, imageAddr)) return false;
            uint imgCompressed = LZ77.getCompressedSize(rom.Data, imageAddr);
            if (imgCompressed == 0) return false;
            if ((ulong)imageAddr + imgCompressed > (ulong)rom.Data.Length) return false;
            byte[] tileData = LZ77.decompress(rom.Data, imageAddr);
            if (tileData == null || tileData.Length == 0) return false;

            // --- Palette ---
            if (!U.isSafetyOffset(paletteAddr, rom)) return false;

            byte[] palBuf;
            if (isCompressedPalette)
            {
                // LZ77-compressed palette (WF PaletteOption == 1). Guard the full
                // 4-byte header + truncation BEFORE decompress, then clamp to 512
                // bytes (WF U.subrange(decompressed, 0, 0x20*16)). A decode
                // failure returns false -- NO raw fallback (#1074 refinement #2).
                if (!IsLZ77HeaderSafe(rom, paletteAddr)) return false;
                uint palCompressed = LZ77.getCompressedSize(rom.Data, paletteAddr);
                if (palCompressed == 0) return false;
                if ((ulong)paletteAddr + palCompressed > (ulong)rom.Data.Length) return false;
                byte[] decompressed = LZ77.decompress(rom.Data, paletteAddr);
                if (decompressed == null || decompressed.Length == 0) return false;

                // Clamp to PALETTE_BYTES (take fewer if the buffer is shorter).
                int palLen = Math.Min(decompressed.Length, PALETTE_BYTES);
                palBuf = new byte[palLen];
                Array.Copy(decompressed, palBuf, palLen);
            }
            else
            {
                // RAW up to 512 bytes (16 banks), clamped to ROM end (no LZ77).
                int palBytes = PALETTE_BYTES;
                if ((ulong)paletteAddr + (ulong)palBytes > (ulong)rom.Data.Length)
                {
                    palBytes = (int)((ulong)rom.Data.Length - paletteAddr);
                }
                if (palBytes <= 0) return false;
                palBuf = new byte[palBytes];
                Array.Copy(rom.Data, paletteAddr, palBuf, 0, palBytes);
            }

            tiles = tileData;
            palette = palBuf;
            return true;
        }

        // =================================================================
        // Per-cell TSA editing (#1005) — NON-header TSA ONLY.
        //
        // A non-header TSA is a clean row-major width8*height8 grid of u16
        // GBA "screen entries". Each entry packs:
        //   bits 0-9  = tile index   (& 0x3FF)
        //   bit  10   = H-flip        (0x400)
        //   bit  11   = V-flip        (0x800)
        //   bits 12-15 = palette bank ((entry >> 12) & 0xF)
        // The serialize below is the EXACT inverse of DecodeTSA's unpack at
        // ImageUtilCore.cs (tileIndex = entry & 0x3FF; hFlip = entry & 0x400;
        // vFlip = entry & 0x800; palIndex = (entry >> 12) & 0xF), stored
        // little-endian.
        //
        // Header-TSA per-cell editing now lives in the #1071 block lower in this
        // file (DecodeHeaderTsaCells / RenderHeaderMainImageFromCells /
        // WriteHeaderTsaCells) — the NON-header methods here refuse / are never
        // called for isHeaderTSA, and the header path uses the shared 32-wide
        // bottom-to-top stride in ImageUtilCore.DecodeHeaderTSAToCells.
        // =================================================================

        const int TSA_CELL_BYTES = 2; // one GBA screen entry = u16

        /// <summary>
        /// READ-ONLY: the number of 8x8 tiles in the LZ77 tile image at
        /// <paramref name="imageAddr"/> (4bpp = 32 bytes/tile). Used by the
        /// per-cell editor to clamp the editable tile-id range. Returns 0
        /// (never throws) on any null/out-of-bounds/corrupt input.
        /// </summary>
        public static int GetTilesheetTileCount(ROM rom, uint imageAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return 0;
            if (!IsLZ77HeaderSafe(rom, imageAddr)) return 0;
            uint imgCompressed = LZ77.getCompressedSize(rom.Data, imageAddr);
            if (imgCompressed == 0) return 0;
            if ((ulong)imageAddr + imgCompressed > (ulong)rom.Data.Length) return 0;
            byte[] tileData = LZ77.decompress(rom.Data, imageAddr);
            if (tileData == null || tileData.Length == 0) return 0;
            return tileData.Length / 32; // 4bpp = 32 bytes/tile
        }

        /// <summary>
        /// Serialize one non-header TSA cell to its GBA screen-entry u16. This
        /// is the EXACT inverse of <see cref="ImageUtilCore.DecodeTSA"/>'s
        /// bit-unpack: <c>(tileId &amp; 0x3FF) | (h?0x400:0) | (v?0x800:0) |
        /// ((bank &amp; 0xF) &lt;&lt; 12)</c>.
        /// </summary>
        public static ushort SerializeCell(int tileId, bool hflip, bool vflip, int bank)
        {
            return (ushort)((tileId & 0x3FF)
                | (hflip ? 0x400 : 0)
                | (vflip ? 0x800 : 0)
                | ((bank & 0xF) << 12));
        }

        /// <summary>
        /// READ-ONLY: decode the current NON-header TSA into a row-major
        /// <c>width8*height8</c> array of GBA screen-entry u16 cells.
        ///
        /// LZ77 path: guard the full 4-byte header before getCompressedSize,
        /// then decompress and slice the first <c>width8*height8</c> entries
        /// (a short stream is zero-padded so the grid is always full-length).
        /// RAW path: slice <c>width8*height8*2</c> bytes at the resolved data
        /// offset (clamped; missing trailing bytes are zero).
        ///
        /// Returns <c>null</c> (never throws) on any null/out-of-bounds/corrupt
        /// input. The caller MUST NOT call this for header-TSA — there is no
        /// isHeaderTSA parameter precisely because this path is non-header only.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="width8">Canvas width in 8-pixel tiles.</param>
        /// <param name="height8">Canvas height in 8-pixel tiles.</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaAddr">Resolved ROM offset of the TSA stream.</param>
        public static ushort[] DecodeTsaCells(ROM rom, uint width8, uint height8,
            bool isLZ77TSA, uint tsaAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (width8 == 0 || height8 == 0) return null;

            long cellCountL = (long)width8 * (long)height8;
            if (cellCountL <= 0 || cellCountL > int.MaxValue) return null;
            int cellCount = (int)cellCountL;

            if (!U.isSafetyOffset(tsaAddr, rom)) return null;

            byte[] tsaBytes;
            if (isLZ77TSA)
            {
                if (!IsLZ77HeaderSafe(rom, tsaAddr)) return null;
                uint tsaCompressed = LZ77.getCompressedSize(rom.Data, tsaAddr);
                if (tsaCompressed == 0) return null;
                if ((ulong)tsaAddr + tsaCompressed > (ulong)rom.Data.Length) return null;
                tsaBytes = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaBytes == null || tsaBytes.Length == 0) return null;
            }
            else
            {
                // RAW: slice width8*height8*2 bytes at the resolved data offset,
                // clamped to ROM end (missing trailing bytes stay zero).
                // cellCountL is already capped at int.MaxValue, but *2 can exceed
                // it — bound-check before the (int) array allocation.
                long needBytes = cellCountL * TSA_CELL_BYTES;
                if (needBytes > int.MaxValue) return null;
                long available = (long)rom.Data.Length - tsaAddr;
                if (available <= 0) return null;
                int sliceLen = (int)Math.Min(needBytes, available);
                tsaBytes = new byte[(int)needBytes];
                Array.Copy(rom.Data, tsaAddr, tsaBytes, 0, sliceLen);
            }

            ushort[] cells = new ushort[cellCount];
            int avail = tsaBytes.Length / TSA_CELL_BYTES;
            int n = Math.Min(cellCount, avail);
            for (int i = 0; i < n; i++)
            {
                int p = i * TSA_CELL_BYTES;
                cells[i] = (ushort)(tsaBytes[p] | (tsaBytes[p + 1] << 8));
            }
            return cells;
        }

        /// <summary>
        /// READ-ONLY: render the NON-header main image from in-memory cells —
        /// serialize <paramref name="cells"/> to TSA bytes then feed
        /// <see cref="ImageUtilCore.DecodeTSA"/> (same code path as
        /// <see cref="TryRenderMainImage"/>'s raw non-header branch). Tile data
        /// + palette come from ROM via the shared loader. Returns <c>null</c>
        /// (never throws) on any null/out-of-bounds/corrupt input or when
        /// <paramref name="cells"/> is null.
        /// </summary>
        public static IImage RenderMainImageFromCells(ROM rom, uint width8, uint height8,
            uint imageAddr, ushort[] cells, uint paletteAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (width8 == 0 || height8 == 0) return null;
            if (cells == null) return null;

            // In-memory cell editing always renders against a RAW palette.
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette,
                                          isCompressedPalette: false))
            {
                return null;
            }

            int wTiles = (int)width8;
            int hTiles = (int)height8;

            // Serialize cells to a row-major little-endian byte buffer and decode
            // with the SAME DecodeTSA the read-only ROM path uses (tsaOffset 0).
            byte[] tsaBytes = new byte[cells.Length * TSA_CELL_BYTES];
            for (int i = 0; i < cells.Length; i++)
            {
                tsaBytes[i * TSA_CELL_BYTES] = (byte)(cells[i] & 0xFF);
                tsaBytes[i * TSA_CELL_BYTES + 1] = (byte)(cells[i] >> 8);
            }

            return ImageUtilCore.DecodeTSA(tileData, tsaBytes, palette, wTiles, hTiles, true, 0);
        }

        /// <summary>
        /// ROM-MUTATING, NON-header ONLY: serialize <paramref name="cells"/>
        /// (row-major little-endian) and write them back to the TSA stream.
        ///
        ///   * <paramref name="isLZ77TSA"/> true  → LZ77-compress + append to
        ///     free space + repoint the POINTER SLOT
        ///     (<paramref name="tsaPointerSlot"/>) via
        ///     <see cref="ImageImportCore.WriteCompressedToROM"/>.
        ///   * raw → bounds-check <c>[tsaDataAddr, tsaDataAddr+bytes)</c> and
        ///     assert the serialized byte length EQUALS the existing
        ///     <c>width8*height8*2</c> footprint (SAME-SIZE in-place overwrite —
        ///     a raw TSA has no length header to grow), then
        ///     <c>rom.write_range(tsaDataAddr, bytes)</c> (NOT the silently-no-op
        ///     WriteBytes). No repoint on the raw path.
        ///
        /// Runs inside the caller's ambient undo scope. A defensive byte-identical
        /// snapshot (#885/#923) is restored on ANY fault — including a free-space
        /// resize-append inside WriteCompressedToROM — so a failed write mutates
        /// ZERO bytes. Never throws; returns "" on success or a localized error.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="width8">Canvas width in 8-pixel tiles.</param>
        /// <param name="height8">Canvas height in 8-pixel tiles.</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaPointerSlot">Pointer-table slot that holds the TSA
        ///   data pointer (used by the LZ77 repoint path).</param>
        /// <param name="tsaDataAddr">Resolved ROM offset of the TSA stream
        ///   (used by the raw in-place path).</param>
        /// <param name="cells">Row-major width8*height8 cells to serialize.</param>
        public static string WriteTsaCells(ROM rom, uint width8, uint height8,
            bool isLZ77TSA, uint tsaPointerSlot, uint tsaDataAddr, ushort[] cells)
        {
            if (rom == null || rom.Data == null) return R._("ROM is not loaded.");
            if (cells == null) return R._("No TSA cell data.");
            if (width8 == 0 || height8 == 0) return R._("TSA dimensions are invalid.");

            long cellCountL = (long)width8 * (long)height8;
            if (cellCountL <= 0 || cellCountL > int.MaxValue || cells.Length != cellCountL)
            {
                return R._("TSA cell count {0} does not match {1}x{2}.",
                    cells.Length, width8, height8);
            }

            // Serialize row-major little-endian (inverse of DecodeTSA's unpack).
            byte[] bytes = new byte[cells.Length * TSA_CELL_BYTES];
            for (int i = 0; i < cells.Length; i++)
            {
                bytes[i * TSA_CELL_BYTES] = (byte)(cells[i] & 0xFF);
                bytes[i * TSA_CELL_BYTES + 1] = (byte)(cells[i] >> 8);
            }

            // Defensive snapshot: the ambient undo scope captures the writes for
            // UNDO; this snapshot guarantees a FAILED write mutates ZERO bytes
            // (length-aware restore handles a free-space resize-append).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                if (isLZ77TSA)
                {
                    // LZ77: compress + append to free space + repoint the SLOT.
                    uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, bytes, tsaPointerSlot);
                    if (writeAddr == U.NOT_FOUND)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Failed to write TSA data. Check ROM free space.");
                    }
                    return "";
                }

                // RAW: SAME-SIZE in-place overwrite at the resolved data addr.
                // A raw TSA has no length header, so the serialized length MUST
                // equal the existing width8*height8*2 footprint.
                long footprint = cellCountL * TSA_CELL_BYTES;
                if (bytes.Length != footprint)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("TSA raw write size mismatch.");
                }
                if (!U.isSafetyOffset(tsaDataAddr, rom)
                    || (ulong)tsaDataAddr + (ulong)bytes.Length > (ulong)rom.Data.Length)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("TSA data range is outside ROM bounds.");
                }
                rom.write_range(tsaDataAddr, bytes);
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("TSA write failed: {0}", ex.Message);
            }
        }

        // =================================================================
        // Per-cell HEADER-TSA editing (#1071) — follow-up to the non-header
        // #1005 path above.
        //
        // A header-TSA carries a 2-byte {masterHeaderX, masterHeaderY} header,
        // then u16 cells filled bottom-to-top in a 32-WIDE stride (not the
        // clean row-major non-header grid). The decode/serialize geometry lives
        // in ImageUtilCore.DecodeHeaderTSAToCells / SerializeHeaderTSA (ONE
        // shared stride), so this Core seam only owns the ROM read/write +
        // byte-identical fault restore. The editor only exposes cells inside
        // the header region (x <= mhx && y <= mhy); the min-clamped canvas
        // outside that region is display-only.
        // =================================================================

        /// <summary>
        /// READ-ONLY: decode the current HEADER-TSA into the 32-wide
        /// bottom-to-top-stride <c>tile[]</c> array (sized to the
        /// <paramref name="width8"/>*<paramref name="height8"/> canvas) plus the
        /// decoded header dimensions. Mirrors the header branch of
        /// <see cref="TryRenderMainImage"/>:
        ///
        ///   * LZ77 path: guard the full 4-byte header before getCompressedSize,
        ///     then decompress.
        ///   * RAW path: slice a bounded window (<see cref="HEADER_TSA_MAX_BYTES"/>)
        ///     from <paramref name="tsaAddr"/> (DecodeHeaderTSAToCells caps reads
        ///     at the header geometry, so the window only needs to cover the
        ///     largest valid 32x32 header).
        ///
        /// Returns <c>null</c> (never throws) on any null/out-of-bounds/corrupt
        /// input OR when the header is NOT a genuine in-range decode
        /// (<see cref="ImageUtilCore.HeaderTSACells.IsValidHeader"/> false) — so
        /// the ViewModel only enables per-cell editing for a valid header, never
        /// from the linear/blank fallback cells. <paramref name="masterHeaderX"/>
        /// / <paramref name="masterHeaderY"/> are set to the decoded header dims
        /// on success, 0 otherwise.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="width8">Canvas width in 8-pixel tiles.</param>
        /// <param name="height8">Canvas height in 8-pixel tiles.</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaAddr">Resolved ROM offset of the TSA stream.</param>
        /// <param name="masterHeaderX">Out: decoded header X (0 when null).</param>
        /// <param name="masterHeaderY">Out: decoded header Y (0 when null).</param>
        public static ushort[] DecodeHeaderTsaCells(ROM rom, uint width8, uint height8,
            bool isLZ77TSA, uint tsaAddr, out int masterHeaderX, out int masterHeaderY)
        {
            masterHeaderX = 0;
            masterHeaderY = 0;

            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (width8 == 0 || height8 == 0) return null;
            if (!U.isSafetyOffset(tsaAddr, rom)) return null;

            byte[] tsaData;
            if (isLZ77TSA)
            {
                if (!IsLZ77HeaderSafe(rom, tsaAddr)) return null;
                uint tsaCompressed = LZ77.getCompressedSize(rom.Data, tsaAddr);
                if (tsaCompressed == 0) return null;
                if ((ulong)tsaAddr + tsaCompressed > (ulong)rom.Data.Length) return null;
                tsaData = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaData == null || tsaData.Length == 0) return null;
            }
            else
            {
                // RAW: bounded window from tsaAddr (DecodeHeaderTSAToCells caps
                // reads at the header geometry).
                long tail = (long)rom.Data.Length - tsaAddr;
                if (tail <= 0) return null;
                int sliceLen = (int)Math.Min((long)HEADER_TSA_MAX_BYTES, tail);
                tsaData = new byte[sliceLen];
                Array.Copy(rom.Data, tsaAddr, tsaData, 0, sliceLen);
            }

            ImageUtilCore.HeaderTSACells decoded = ImageUtilCore.DecodeHeaderTSAToCells(
                tsaData, (int)width8, (int)height8, 0);
            if (!decoded.IsValidHeader || decoded.Tile == null) return null;

            masterHeaderX = decoded.MasterHeaderX;
            masterHeaderY = decoded.MasterHeaderY;
            return decoded.Tile;
        }

        /// <summary>
        /// READ-ONLY: render the HEADER-TSA main image from an in-memory 32-wide
        /// bottom-to-top-stride <paramref name="editedTile"/> array. Serializes
        /// the tile array back to a header-TSA byte stream
        /// (<see cref="ImageUtilCore.SerializeHeaderTSA"/>) then feeds
        /// <see cref="ImageUtilCore.DecodeHeaderTSA"/> — the SAME render path the
        /// read-only ROM branch uses — so the preview reflects unsaved edits.
        /// Returns <c>null</c> (never throws) on any null/out-of-bounds/corrupt
        /// input or a degenerate serialize.
        /// </summary>
        public static IImage RenderHeaderMainImageFromCells(ROM rom, uint width8, uint height8,
            uint imageAddr, ushort[] editedTile, int masterHeaderX, int masterHeaderY,
            uint paletteAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (width8 == 0 || height8 == 0) return null;
            if (editedTile == null) return null;

            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette,
                                          isCompressedPalette: false))
            {
                return null;
            }

            byte[] tsaBytes = ImageUtilCore.SerializeHeaderTSA(
                editedTile, masterHeaderX, masterHeaderY, 0);
            // A degenerate serialize (length 2) cannot render a valid header.
            int expected = 2 + (masterHeaderX + 1) * (masterHeaderY + 1) * 2;
            if (tsaBytes == null || tsaBytes.Length != expected) return null;

            return ImageUtilCore.DecodeHeaderTSA(
                tileData, tsaBytes, palette, (int)width8, (int)height8, true, 0, 0);
        }

        /// <summary>
        /// ROM-MUTATING, HEADER-TSA ONLY: serialize the 32-wide
        /// bottom-to-top-stride <paramref name="editedTile"/> array
        /// (<see cref="ImageUtilCore.SerializeHeaderTSA"/>, preserving the
        /// original <paramref name="masterHeaderX"/>/<paramref name="masterHeaderY"/>)
        /// and write it back to the TSA stream, preserving the read-path stream
        /// format:
        ///
        ///   * <paramref name="isLZ77TSA"/> true  → LZ77-compress + append to
        ///     free space + repoint the POINTER SLOT
        ///     (<paramref name="tsaPointerSlot"/>) via
        ///     <see cref="ImageImportCore.WriteCompressedToROM"/>.
        ///   * raw → the serialized length equals the EXISTING header byte length
        ///     (same <c>{mhx, mhy}</c>), so a SAME-SIZE in-place
        ///     <c>rom.write_range</c> at <paramref name="tsaDataAddr"/> after
        ///     bounds checks (no growth). No repoint on the raw path.
        ///
        /// A degenerate serialize (the serializer's <c>byte[2]</c> guard output,
        /// or any length != <c>2 + (mhx+1)*(mhy+1)*2</c>) is treated as an error:
        /// it NEVER LZ77-compresses/repoints or raw-writes a degenerate payload
        /// (Copilot review on #1071). Runs inside the caller's ambient undo scope;
        /// a defensive byte-identical snapshot (#885/#923) is restored on ANY
        /// fault — including a free-space resize-append. Never throws; returns ""
        /// on success or a localized error.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="editedTile">The 32-wide bottom-to-top-stride tile array.</param>
        /// <param name="masterHeaderX">The ORIGINAL header X (preserved verbatim).</param>
        /// <param name="masterHeaderY">The ORIGINAL header Y (preserved verbatim).</param>
        /// <param name="isLZ77TSA">True if the TSA stream is LZ77-compressed.</param>
        /// <param name="tsaPointerSlot">Pointer-table slot that holds the TSA
        ///   data pointer (used by the LZ77 repoint path).</param>
        /// <param name="tsaDataAddr">Resolved ROM offset of the TSA stream
        ///   (used by the raw in-place path).</param>
        public static string WriteHeaderTsaCells(ROM rom, ushort[] editedTile,
            int masterHeaderX, int masterHeaderY, bool isLZ77TSA,
            uint tsaPointerSlot, uint tsaDataAddr)
        {
            if (rom == null || rom.Data == null) return R._("ROM is not loaded.");
            if (editedTile == null) return R._("No TSA cell data.");
            if (masterHeaderX < 0 || masterHeaderY < 0
                || masterHeaderX > 32 || masterHeaderY > 32)
            {
                return R._("Header-TSA dimensions {0}x{1} are invalid.",
                    masterHeaderX, masterHeaderY);
            }

            // Serialize via the EXACT inverse of DecodeHeaderTSAToCells,
            // preserving the original {mhx, mhy}.
            byte[] bytes = ImageUtilCore.SerializeHeaderTSA(
                editedTile, masterHeaderX, masterHeaderY, 0);

            // Reject any degenerate serialize: the serializer returns byte[2] on
            // a guard, so an output whose length != the expected header footprint
            // must NOT be written.
            int expected = 2 + (masterHeaderX + 1) * (masterHeaderY + 1) * 2;
            if (bytes == null || bytes.Length != expected)
            {
                return R._("Header-TSA serialization produced a degenerate output; refusing to write.");
            }

            // Defensive snapshot: the ambient undo scope captures the writes for
            // UNDO; this snapshot guarantees a FAILED write mutates ZERO bytes
            // (length-aware restore handles a free-space resize-append).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                if (isLZ77TSA)
                {
                    // LZ77: compress + append to free space + repoint the SLOT.
                    uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, bytes, tsaPointerSlot);
                    if (writeAddr == U.NOT_FOUND)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("Failed to write TSA data. Check ROM free space.");
                    }
                    return "";
                }

                // RAW: SAME-SIZE in-place overwrite at the resolved data addr. A
                // header-TSA's serialized length is fixed by {mhx, mhy}, so it
                // EQUALS the existing header byte footprint — no growth.
                if (!U.isSafetyOffset(tsaDataAddr, rom)
                    || (ulong)tsaDataAddr + (ulong)bytes.Length > (ulong)rom.Data.Length)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("TSA data range is outside ROM bounds.");
                }
                rom.write_range(tsaDataAddr, bytes);
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("TSA write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore (#885/#923): a free-space
        /// resize-append can GROW rom.Data, so down-resize back to the snapshot
        /// length BEFORE the in-place copy (a naive Array.Copy would leave the
        /// grown tail alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }

        // The GBA LZ77 stream header is 4 bytes (0x10 + a 3-byte uncompressed
        // size). LZ77.getCompressedSize reads input[offset + 3] but only rejects
        // when FEWER THAN 3 bytes remain, so a pointer to the LAST 1-3 bytes of
        // the ROM passes isSafetyOffset yet makes that header read throw
        // IndexOutOfRangeException (Copilot PR #818 review). Require the FULL
        // 4-byte header to be in-bounds BEFORE any LZ77 call. Mirrors
        // ImageBattleScreenCore.IsLZ77HeaderSafe / IsRegionSafe.
        const int LZ77_HEADER_BYTES = 4;
        static bool IsLZ77HeaderSafe(ROM rom, uint addr) => IsRegionSafe(rom, addr, LZ77_HEADER_BYTES);

        /// <summary>
        /// True when <c>[addr, addr+bytes)</c> is a valid in-ROM region:
        /// <see cref="U.isSafetyOffset(uint, ROM)"/> domain constraints (offset
        /// in [0x200, 0x02000000) and within rom.Data) PLUS an explicit
        /// end-of-range check using <c>ulong</c> arithmetic so the addition
        /// cannot overflow on near-<see cref="uint.MaxValue"/> inputs. Mirrors
        /// ImageBattleScreenCore.IsRegionSafe (#594/#818).
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
