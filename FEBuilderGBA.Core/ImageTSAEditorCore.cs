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
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (width8 == 0 || height8 == 0) return null;

            // Shared LZ77-image-decode + raw-palette read (with the truncation
            // guards). Behaviour-preserving extraction -- see
            // TryLoadTSATileAndPalette. RenderChipList reuses the SAME loader.
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette))
            {
                return null;
            }

            int wTiles = (int)width8;
            int hTiles = (int)height8;

            if (!U.isSafetyOffset(tsaAddr, rom)) return null;

            // --- TSA data ---
            if (isLZ77TSA)
            {
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
            // truncation guards). The chip list never reads the TSA stream.
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette))
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
            // RenderChipList. The raw tilesheet never reads the TSA stream.
            if (!TryLoadTSATileAndPalette(rom, imageAddr, paletteAddr,
                                          out byte[] tileData, out byte[] palette))
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
        /// LZ77-decode the tile image and read the raw (≤512-byte) palette block
        /// from already-resolved ROM addresses. Behaviour-preserving -- returns
        /// <c>false</c> exactly where the inline block previously returned
        /// <c>null</c> (the #810 <see cref="TryRenderMainImage"/> tests pin this).
        ///
        /// Validates the compressed image stream BEFORE decompressing:
        /// <c>LZ77.decompress</c> silently returns a zero-filled buffer on a
        /// truncated stream, so <c>getCompressedSize == 0</c> + an end-of-ROM
        /// bound check is the truncation guard (mirrors ImageBattleScreenCore's
        /// TryLoadChipsetAndPalette). The palette read is RAW (no LZ77), clamped
        /// to ROM end; <c>DecodeTileToPixels</c> bounds-checks short palettes so
        /// a clamped read is safe.
        /// </summary>
        static bool TryLoadTSATileAndPalette(ROM rom, uint imageAddr, uint paletteAddr,
            out byte[] tiles, out byte[] palette)
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

            // --- Palette: RAW up to 512 bytes (16 banks), clamped to ROM end
            // (no LZ77). ---
            if (!U.isSafetyOffset(paletteAddr, rom)) return false;
            int palBytes = PALETTE_BYTES;
            if ((ulong)paletteAddr + (ulong)palBytes > (ulong)rom.Data.Length)
            {
                palBytes = (int)((ulong)rom.Data.Length - paletteAddr);
            }
            if (palBytes <= 0) return false;
            byte[] palBuf = new byte[palBytes];
            Array.Copy(rom.Data, paletteAddr, palBuf, 0, palBytes);

            tiles = tileData;
            palette = palBuf;
            return true;
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
