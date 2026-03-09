using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-independent image operations extracted from WinForms ImageUtil.
    /// Uses IImageService/IImage instead of System.Drawing.
    /// </summary>
    public static class ImageUtilCore
    {
        /// <summary>
        /// Load 4bpp tiles from ROM at the given offset.
        /// Decompresses LZ77 if isCompressed is true.
        /// Returns indexed IImage with the given palette.
        /// </summary>
        public static IImage LoadROMTiles4bpp(uint offset, byte[] gbaPalette, int tileCountX, int tileCountY, bool isCompressed = false)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null)
                return null;

            byte[] tileData;
            if (isCompressed)
            {
                tileData = LZ77.decompress(rom.Data, offset);
                if (tileData == null) return null;
            }
            else
            {
                int dataLen = tileCountX * tileCountY * 32; // 32 bytes per 8x8 tile at 4bpp
                if (offset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, offset, tileData, 0, dataLen);
            }

            int width = tileCountX * 8;
            int height = tileCountY * 8;
            int colorCount = Math.Min(gbaPalette.Length / 2, 16);

            return CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, gbaPalette);
        }

        /// <summary>
        /// Load 8bpp tiles from ROM at the given offset.
        /// </summary>
        public static IImage LoadROMTiles8bpp(uint offset, byte[] gbaPalette, int tileCountX, int tileCountY, bool isCompressed = false)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null)
                return null;

            byte[] tileData;
            if (isCompressed)
            {
                tileData = LZ77.decompress(rom.Data, offset);
                if (tileData == null) return null;
            }
            else
            {
                int dataLen = tileCountX * tileCountY * 64; // 64 bytes per 8x8 tile at 8bpp
                if (offset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, offset, tileData, 0, dataLen);
            }

            int width = tileCountX * 8;
            int height = tileCountY * 8;
            int colorCount = Math.Min(gbaPalette.Length / 2, 256);

            return CoreState.ImageService.Decode8bppTiles(tileData, 0, width, height, gbaPalette);
        }

        /// <summary>
        /// Read a GBA palette from ROM (array of 16-bit colors).
        /// </summary>
        public static byte[] GetPalette(uint offset, int colorCount = 16)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            int byteLen = colorCount * 2;
            if (offset + byteLen > (uint)rom.Data.Length)
                return null;

            byte[] palette = new byte[byteLen];
            Array.Copy(rom.Data, offset, palette, 0, byteLen);
            return palette;
        }

        /// <summary>
        /// Read a compressed palette from ROM (LZ77).
        /// </summary>
        public static byte[] GetCompressedPalette(uint offset, int colorCount = 16)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            byte[] decompressed = LZ77.decompress(rom.Data, offset);
            if (decompressed == null) return null;

            int byteLen = colorCount * 2;
            if (decompressed.Length < byteLen)
                return decompressed;

            byte[] palette = new byte[byteLen];
            Array.Copy(decompressed, palette, byteLen);
            return palette;
        }

        /// <summary>
        /// Decode TSA (Tile Screen Arrangement) data to produce a tile map.
        /// TSA entries are 16-bit: bits 0-9 = tile index, bits 10-11 = flip, bits 12-15 = palette.
        /// </summary>
        public static IImage DecodeTSA(byte[] tileData, byte[] tsaData, byte[] gbaPalette,
            int screenWidthTiles, int screenHeightTiles, bool is4bpp = true, int tsaOffset = 0)
        {
            if (CoreState.ImageService == null) return null;

            int width = screenWidthTiles * 8;
            int height = screenHeightTiles * 8;

            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            int maxEntries = screenWidthTiles * screenHeightTiles;
            int availableEntries = (tsaData.Length - tsaOffset) / 2;
            int tsaEntryCount = Math.Min(availableEntries, maxEntries);

            for (int i = 0; i < tsaEntryCount; i++)
            {
                int bytePos = tsaOffset + i * 2;
                ushort tsaEntry = (ushort)(tsaData[bytePos] | (tsaData[bytePos + 1] << 8));
                int tileIndex = tsaEntry & 0x3FF;
                bool hFlip = (tsaEntry & 0x400) != 0;
                bool vFlip = (tsaEntry & 0x800) != 0;
                int palIndex = (tsaEntry >> 12) & 0xF;

                int tileX = (i % screenWidthTiles) * 8;
                int tileY = (i / screenWidthTiles) * 8;

                DecodeTileToPixels(tileData, tileIndex, gbaPalette, palIndex,
                    pixels, width, tileX, tileY, hFlip, vFlip, is4bpp);
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Decode TSA with a 2-byte header (used by Big CG, OP Prologue).
        /// Matches WinForms ImageUtil.ByteToHeaderTSA: reads header (width,height),
        /// then fills a 32-wide tile grid bottom-to-top starting at row=headerY.
        /// </summary>
        public static IImage DecodeHeaderTSA(byte[] tileData, byte[] tsaData, byte[] gbaPalette,
            int screenWidthTiles, int screenHeightTiles, bool is4bpp = true,
            int tsaAddend = 0, int paletteShift = 0)
        {
            if (CoreState.ImageService == null) return null;

            int size = screenWidthTiles * screenHeightTiles;

            if (tsaData.Length < 2)
                return DecodeTSA(tileData, tsaData, gbaPalette, screenWidthTiles, screenHeightTiles, is4bpp, 0);

            int masterHeaderX = tsaData[0];
            int masterHeaderY = tsaData[1];
            if (masterHeaderX > 32 || masterHeaderY > 32)
                return DecodeTSA(tileData, tsaData, gbaPalette, screenWidthTiles, screenHeightTiles, is4bpp, 0);

            if (masterHeaderX * masterHeaderY > size)
                size = masterHeaderX * masterHeaderY;

            ushort[] tile = new ushort[size];

            int length = 2 + (size * 2);
            length = Math.Min(length, tsaData.Length);

            int i = 2; // skip header

            // Start position: bottom-to-top fill matching WinForms ByteToHeaderTSA
            int n = masterHeaderY << 5; // masterHeaderY * 32
            if (n >= size)
                return DecodeTSA(tileData, new byte[0], gbaPalette, screenWidthTiles, screenHeightTiles, is4bpp, 0);

            for (int headery = 0; headery <= masterHeaderY; headery++)
            {
                for (int headerx = 0; headerx <= masterHeaderX; headerx++)
                {
                    if (i + 1 >= length) goto done;
                    if (n >= tile.Length) goto done;

                    ushort tsadata = (ushort)(tsaData[i] | (tsaData[i + 1] << 8));
                    tile[n] = (ushort)(tsadata + tsaAddend);

                    i += 2;
                    n++;
                }
                n = n - masterHeaderX;
                n = n - (0x42 / 2); // = n - 0x21
            }

            done:
            // Render using the decoded TSA tile array
            int width = screenWidthTiles * 8;
            int height = screenHeightTiles * 8;
            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4];

            int tileLength = tile.Length;
            int x = 0, y = 0;

            for (int tsaindex = 0; tsaindex < tileLength; tsaindex++, x += 8)
            {
                if (x >= width) { x = 0; y += 8; if (y >= height) break; }

                ushort tsatile = tile[tsaindex];
                if (tsatile == 0xFFFF || tsatile == 0) continue;

                int tileIndex = tsatile & 0x3FF;
                bool hFlip = (tsatile & 0x400) != 0;
                bool vFlip = (tsatile & 0x800) != 0;
                int palIndex = ((tsatile >> 12) & 0xF);

                // Apply palette shift (e.g., BigCG uses -0x80 → shift palette index)
                int adjustedPalIndex = palIndex + paletteShift;
                if (adjustedPalIndex < 0) adjustedPalIndex = 0;
                if (adjustedPalIndex > 15) adjustedPalIndex = 15;

                DecodeTileToPixels(tileData, tileIndex, gbaPalette, adjustedPalIndex,
                    pixels, width, x, y, hFlip, vFlip, is4bpp);
            }

            image.SetPixelData(pixels);
            return image;
        }

        static void DecodeTileToPixels(byte[] tileData, int tileIndex, byte[] gbaPalette, int palIndex,
            byte[] pixels, int imageWidth, int tileX, int tileY, bool hFlip, bool vFlip, bool is4bpp)
        {
            if (CoreState.ImageService == null) return;

            int bytesPerTile = is4bpp ? 32 : 64;
            int tileOffset = tileIndex * bytesPerTile;
            if (tileOffset + bytesPerTile > tileData.Length) return;

            int palOffset = is4bpp ? palIndex * 16 * 2 : 0; // palette offset in bytes

            for (int py = 0; py < 8; py++)
            {
                int srcY = vFlip ? (7 - py) : py;
                for (int px = 0; px < 8; px++)
                {
                    int srcX = hFlip ? (7 - px) : px;

                    int colorIndex;
                    if (is4bpp)
                    {
                        int bytePos = tileOffset + srcY * 4 + srcX / 2;
                        if (bytePos >= tileData.Length) continue;
                        byte b = tileData[bytePos];
                        colorIndex = (srcX % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);
                    }
                    else
                    {
                        int bytePos = tileOffset + srcY * 8 + srcX;
                        if (bytePos >= tileData.Length) continue;
                        colorIndex = tileData[bytePos];
                    }

                    // Convert to RGBA
                    int palByteOffset = palOffset + colorIndex * 2;
                    if (palByteOffset + 2 > gbaPalette.Length) continue;

                    ushort gbaColor = (ushort)(gbaPalette[palByteOffset] | (gbaPalette[palByteOffset + 1] << 8));
                    CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b2);

                    int destX = tileX + px;
                    int destY = tileY + py;
                    if (destX >= imageWidth) continue;

                    int idx = (destY * imageWidth + destX) * 4;
                    if (idx + 3 >= pixels.Length) continue;

                    pixels[idx + 0] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b2;
                    pixels[idx + 3] = (byte)(colorIndex == 0 ? 0 : 255); // index 0 = transparent
                }
            }
        }
    }
}
