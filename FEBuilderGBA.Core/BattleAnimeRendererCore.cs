using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform battle animation sprite rendering.
    /// Decodes LZ77-compressed 4bpp tile data + GBA palette into a tile sheet image.
    /// </summary>
    public static class BattleAnimeRendererCore
    {
        const int TILE_SIZE = 8;          // 8x8 pixels per tile
        const int BYTES_PER_TILE_4BPP = 32; // 4bpp: 32 bytes per tile

        /// <summary>
        /// Render decompressed 4bpp tile data as a grid (tile sheet / sprite atlas).
        /// Each 8x8 tile is placed left-to-right, wrapping after tilesPerRow tiles.
        /// </summary>
        /// <param name="tileData">Raw 4bpp tile data (32 bytes per tile).</param>
        /// <param name="gbaPalette">GBA palette (16 colors, 32 bytes).</param>
        /// <param name="tilesPerRow">Number of tiles per row in the output image.</param>
        /// <returns>An IImage containing the rendered tile sheet, or null on failure.</returns>
        public static IImage RenderTileSheet(byte[] tileData, byte[] gbaPalette, int tilesPerRow = 16)
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null || tileData == null || tileData.Length == 0
                || gbaPalette == null || gbaPalette.Length < 2)
                return null;

            if (tilesPerRow <= 0) tilesPerRow = 16;

            int totalTiles = tileData.Length / BYTES_PER_TILE_4BPP;
            if (totalTiles == 0) return null;

            int rows = (totalTiles + tilesPerRow - 1) / tilesPerRow;
            int width = tilesPerRow * TILE_SIZE;
            int height = rows * TILE_SIZE;

            byte[] pixels = new byte[width * height * 4]; // RGBA

            for (int t = 0; t < totalTiles; t++)
            {
                int tileOffset = t * BYTES_PER_TILE_4BPP;
                if (tileOffset + BYTES_PER_TILE_4BPP > tileData.Length) break;

                int tileCol = t % tilesPerRow;
                int tileRow = t / tilesPerRow;
                int baseX = tileCol * TILE_SIZE;
                int baseY = tileRow * TILE_SIZE;

                for (int py = 0; py < TILE_SIZE; py++)
                {
                    for (int px = 0; px < TILE_SIZE; px++)
                    {
                        int bytePos = tileOffset + py * 4 + px / 2;
                        if (bytePos >= tileData.Length) continue;

                        byte b = tileData[bytePos];
                        int colorIndex = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                        int palByteOffset = colorIndex * 2;
                        if (palByteOffset + 2 > gbaPalette.Length) continue;

                        ushort gbaColor = (ushort)(gbaPalette[palByteOffset] | (gbaPalette[palByteOffset + 1] << 8));
                        svc.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte bl);

                        int destX = baseX + px;
                        int destY = baseY + py;
                        int idx = (destY * width + destX) * 4;
                        if (idx + 3 >= pixels.Length) continue;

                        pixels[idx + 0] = r;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = bl;
                        pixels[idx + 3] = (byte)(colorIndex == 0 ? 0 : 255);
                    }
                }
            }

            var image = svc.CreateImage(width, height);
            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Read a battle animation record and render its tile sheet.
        /// Reads palette from offset 28, decompresses frame data from offset 16.
        /// </summary>
        /// <param name="animeRecordAddr">Address of the 32-byte animation data record in ROM.</param>
        /// <param name="tilesPerRow">Tiles per row in the output sheet.</param>
        /// <returns>An IImage of the tile sheet, or null on failure.</returns>
        public static IImage RenderAnimationTileSheet(uint animeRecordAddr, int tilesPerRow = 16)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null) return null;
            if (animeRecordAddr + 32 > (uint)rom.Data.Length) return null;

            uint frameRaw = rom.u32(animeRecordAddr + 16);
            uint paletteRaw = rom.u32(animeRecordAddr + 28);

            // Read palette (raw 16-color GBA palette = 32 bytes)
            byte[] gbaPalette;
            if (U.isPointer(paletteRaw))
            {
                uint palOff = U.toOffset(paletteRaw);
                if (!U.isSafetyOffset(palOff, rom)) return null;
                gbaPalette = ImageUtilCore.GetPalette(palOff, 16);
            }
            else
            {
                return null;
            }
            if (gbaPalette == null) return null;

            // Decompress frame/sheet data (LZ77)
            byte[] tileData = null;
            if (U.isPointer(frameRaw))
            {
                uint frameOff = U.toOffset(frameRaw);
                if (U.isSafetyOffset(frameOff, rom))
                {
                    tileData = LZ77.decompress(rom.Data, frameOff);
                }
            }

            if (tileData == null || tileData.Length == 0) return null;

            return RenderTileSheet(tileData, gbaPalette, tilesPerRow);
        }
    }
}
