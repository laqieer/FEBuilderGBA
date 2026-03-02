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
            int screenWidthTiles, int screenHeightTiles, bool is4bpp = true)
        {
            if (CoreState.ImageService == null) return null;

            int width = screenWidthTiles * 8;
            int height = screenHeightTiles * 8;
            int colorCount = is4bpp ? 16 : 256;

            // Create output image
            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            int tsaEntryCount = Math.Min(tsaData.Length / 2, screenWidthTiles * screenHeightTiles);

            for (int i = 0; i < tsaEntryCount; i++)
            {
                ushort tsaEntry = (ushort)(tsaData[i * 2] | (tsaData[i * 2 + 1] << 8));
                int tileIndex = tsaEntry & 0x3FF;
                bool hFlip = (tsaEntry & 0x400) != 0;
                bool vFlip = (tsaEntry & 0x800) != 0;
                int palIndex = (tsaEntry >> 12) & 0xF;

                int tileX = (i % screenWidthTiles) * 8;
                int tileY = (i / screenWidthTiles) * 8;

                // Decode one tile
                DecodeTileToPixels(tileData, tileIndex, gbaPalette, palIndex,
                    pixels, width, tileX, tileY, hFlip, vFlip, is4bpp);
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
