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

            // --- Tile data: LZ77 image (validate the compressed stream BEFORE
            // decompress; LZ77.decompress silently returns a zero-filled buffer
            // on a truncated stream, so getCompressedSize==0 + an end-of-ROM
            // bound check is the truncation guard -- mirrors ImageBattleScreenCore). ---
            if (!U.isSafetyOffset(imageAddr, rom)) return null;
            uint imgCompressed = LZ77.getCompressedSize(rom.Data, imageAddr);
            if (imgCompressed == 0) return null;
            if ((ulong)imageAddr + imgCompressed > (ulong)rom.Data.Length) return null;
            byte[] tileData = LZ77.decompress(rom.Data, imageAddr);
            if (tileData == null || tileData.Length == 0) return null;

            // --- Palette: RAW up to 512 bytes (16 banks), clamped to ROM end
            // (no LZ77). DecodeTileToPixels bounds-checks short palettes, so a
            // clamped read is safe. ---
            if (!U.isSafetyOffset(paletteAddr, rom)) return null;
            int palBytes = PALETTE_BYTES;
            if ((ulong)paletteAddr + (ulong)palBytes > (ulong)rom.Data.Length)
            {
                palBytes = (int)((ulong)rom.Data.Length - paletteAddr);
            }
            if (palBytes <= 0) return null;
            byte[] palette = new byte[palBytes];
            Array.Copy(rom.Data, paletteAddr, palette, 0, palBytes);

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
    }
}
