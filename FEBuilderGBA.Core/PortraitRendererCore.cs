using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform portrait rendering for GBA Fire Emblem ROMs.
    /// Ports the assembly logic from WinForms ImagePortraitForm to use IImage/IImageService.
    /// </summary>
    public static class PortraitRendererCore
    {
        // Portrait part dimensions (pixels)
        const int FaceWidth = 96;   // 12 * 8
        const int FaceHeight = 80;  // 10 * 8
        const int PartsWidth = 32;  // 4 * 8
        const int PartsHeight = 16; // 2 * 8

        // Sprite sheet dimensions (tiles)
        const int SheetWidthTiles = 32;
        const int SheetHeightTiles = 5;
        const int SheetWidthPx = SheetWidthTiles * 8; // 256
        const int SheetHeightPx = SheetHeightTiles * 8; // 40

        /// <summary>
        /// Assemble the full 96x80 unit portrait from the sprite sheet parts.
        /// This is the cross-platform equivalent of ImagePortraitForm.DrawPortraitUnit.
        /// </summary>
        public static IImage DrawPortraitUnit(
            uint unitFacePtr, uint palettePtr,
            byte eyeX, byte eyeY, byte state)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;

            uint unitFace = U.toOffset(unitFacePtr);
            uint palette = U.toOffset(palettePtr);

            if (unitFace == 0 || !U.isSafetyOffset(unitFace) || !U.isSafetyOffset(palette))
                return null;

            // Get palette
            byte[] gbaPalette = ImageUtilCore.GetPalette(palette, 16);
            if (gbaPalette == null) return null;

            // Decompress or load sprite sheet
            byte[] tileData;
            int sheetHeight = SheetHeightPx;

            if (LZ77.iscompress(rom.Data, unitFace))
            {
                tileData = LZ77.decompress(rom.Data, unitFace);
                if (tileData == null || tileData.Length == 0) return null;
            }
            else
            {
                // Uncompressed: skip 4-byte header, check for half-body
                if (IsHalfBodyFlag(unitFace))
                    sheetHeight = 10 * 8;
                uint dataOffset = unitFace + 4;
                int dataLen = (SheetWidthPx / 8) * (sheetHeight / 8) * 32; // 4bpp: 32 bytes per tile
                if (dataOffset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, dataOffset, tileData, 0, dataLen);
            }

            // Decode sprite sheet to RGBA pixels
            int sheetW = SheetWidthPx;
            int sheetH = sheetHeight;
            byte[] sheetPixels = DecodeTilesToRGBA(tileData, sheetW, sheetH, gbaPalette, svc);
            if (sheetPixels == null) return null;

            // Create output canvas
            byte[] facePixels = new byte[FaceWidth * FaceHeight * 4];

            // Assemble face parts (matching WinForms BitBlt calls exactly)
            // Upper face: 64x32 from (0,0) → dest (16,0)
            BlitPixels(sheetPixels, sheetW, 0, 0, PartsWidth * 2, PartsHeight * 2,
                       facePixels, FaceWidth, PartsWidth / 2, 0);
            // Lower face: 64x32 from (64,0) → dest (16,32)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 2, 0, PartsWidth * 2, PartsHeight * 2,
                       facePixels, FaceWidth, PartsWidth / 2, PartsHeight * 2);
            // Right shoulder: 32x16 from (128,0) → dest (16,64)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 4, 0, PartsWidth, PartsHeight,
                       facePixels, FaceWidth, PartsWidth / 2, PartsHeight * 4);
            // Right edge: 16x32 from (160,0) → dest (0,48)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 5, 0, PartsWidth / 2, PartsHeight * 2,
                       facePixels, FaceWidth, 0, PartsHeight * 3);
            // Left shoulder: 32x16 from (128,16) → dest (48,64)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 4, PartsHeight, PartsWidth, PartsHeight,
                       facePixels, FaceWidth, PartsWidth + PartsWidth / 2, PartsHeight * 4);
            // Left edge: 16x32 from (176,0) → dest (80,48)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 5 + PartsWidth / 2, 0, PartsWidth / 2, PartsHeight * 2,
                       facePixels, FaceWidth, PartsWidth * 2 + PartsWidth / 2, PartsHeight * 3);

            // Eyes closed overlay (state == 0x06)
            if (state == 0x06)
            {
                BlitPixelsWithTransparency(
                    sheetPixels, sheetW, SheetWidthPx - PartsWidth * 2, PartsHeight,
                    PartsWidth, PartsHeight,
                    facePixels, FaceWidth, eyeX * 8, eyeY * 8);
            }

            // Create IImage from pixel data
            var image = svc.CreateImage(FaceWidth, FaceHeight);
            image.SetPixelData(facePixels);
            return image;
        }

        /// <summary>
        /// Draw the mini/map portrait (32x32).
        /// </summary>
        public static IImage DrawPortraitMap(uint mapFacePtr, uint palettePtr)
        {
            uint mapFace = U.toOffset(mapFacePtr);
            uint palette = U.toOffset(palettePtr);

            if (mapFace == 0 || !U.isSafetyOffset(mapFace) || !U.isSafetyOffset(palette))
                return null;

            byte[] gbaPalette = ImageUtilCore.GetPalette(palette, 16);
            if (gbaPalette == null) return null;

            return ImageUtilCore.LoadROMTiles4bpp(mapFace, gbaPalette, 4, 4, true);
        }

        /// <summary>
        /// Draw the class card portrait (80x80, or auto-height from compressed data).
        /// </summary>
        public static IImage DrawPortraitClass(uint classFacePtr, uint palettePtr)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;

            uint classFace = U.toOffset(classFacePtr);
            uint palette = U.toOffset(palettePtr);

            if (!U.isSafetyOffset(classFace) || !U.isSafetyOffset(palette))
                return null;

            byte[] gbaPalette = ImageUtilCore.GetPalette(palette, 16);
            if (gbaPalette == null) return null;

            byte[] tileData = LZ77.decompress(rom.Data, classFace);
            if (tileData == null || tileData.Length == 0) return null;

            int widthTiles = 10; // 80px
            int heightTiles = CalcHeightTiles(widthTiles, tileData.Length);
            if (heightTiles <= 0) return null;

            int width = widthTiles * 8;
            int height = heightTiles * 8;

            byte[] pixels = DecodeTilesToRGBA(tileData, width, height, gbaPalette, svc);
            if (pixels == null) return null;

            var image = svc.CreateImage(width, height);
            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Check if portrait data has the half-body flag (header == 0x00200400).
        /// </summary>
        static bool IsHalfBodyFlag(uint unitFace)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !U.isSafetyOffset(unitFace + 4)) return false;
            uint header = rom.u32(unitFace);
            return header == 0x00200400;
        }

        /// <summary>
        /// Calculate tile height from data length for a given tile width.
        /// </summary>
        static int CalcHeightTiles(int widthTiles, int dataLength)
        {
            int bytesPerTile = 32; // 4bpp
            int totalTiles = dataLength / bytesPerTile;
            if (widthTiles <= 0) return 0;
            return totalTiles / widthTiles;
        }

        /// <summary>
        /// Decode 4bpp tile data into RGBA pixel array.
        /// </summary>
        static byte[] DecodeTilesToRGBA(byte[] tileData, int width, int height, byte[] gbaPalette, IImageService svc)
        {
            int tilesX = width / 8;
            int tilesY = height / 8;
            byte[] pixels = new byte[width * height * 4];

            int tileIndex = 0;
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int tileOffset = tileIndex * 32;
                    if (tileOffset + 32 > tileData.Length) break;

                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            int bytePos = tileOffset + py * 4 + px / 2;
                            if (bytePos >= tileData.Length) continue;

                            byte b = tileData[bytePos];
                            int colorIndex = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                            int palByteOffset = colorIndex * 2;
                            if (palByteOffset + 2 > gbaPalette.Length) continue;

                            ushort gbaColor = (ushort)(gbaPalette[palByteOffset] | (gbaPalette[palByteOffset + 1] << 8));
                            svc.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte bl);

                            int destX = tx * 8 + px;
                            int destY = ty * 8 + py;
                            int idx = (destY * width + destX) * 4;
                            if (idx + 3 >= pixels.Length) continue;

                            pixels[idx + 0] = r;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = bl;
                            pixels[idx + 3] = (byte)(colorIndex == 0 ? 0 : 255);
                        }
                    }
                    tileIndex++;
                }
            }
            return pixels;
        }

        /// <summary>
        /// Copy a rectangular region between RGBA pixel arrays (opaque copy, overwrites destination).
        /// </summary>
        internal static void BlitPixels(byte[] src, int srcW, int srcX, int srcY,
            int w, int h, byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < h; y++)
            {
                int sy = srcY + y;
                int dy = dstY + y;
                for (int x = 0; x < w; x++)
                {
                    int sx = srcX + x;
                    int dx = dstX + x;

                    int srcIdx = (sy * srcW + sx) * 4;
                    int dstIdx = (dy * dstW + dx) * 4;

                    if (srcIdx + 3 >= src.Length || dstIdx + 3 >= dst.Length) continue;

                    dst[dstIdx + 0] = src[srcIdx + 0];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
        }

        /// <summary>
        /// Copy a rectangular region, skipping transparent pixels (alpha == 0) in source.
        /// Used for overlays like eye/mouth animations.
        /// </summary>
        internal static void BlitPixelsWithTransparency(byte[] src, int srcW, int srcX, int srcY,
            int w, int h, byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < h; y++)
            {
                int sy = srcY + y;
                int dy = dstY + y;
                for (int x = 0; x < w; x++)
                {
                    int sx = srcX + x;
                    int dx = dstX + x;

                    int srcIdx = (sy * srcW + sx) * 4;
                    int dstIdx = (dy * dstW + dx) * 4;

                    if (srcIdx + 3 >= src.Length || dstIdx + 3 >= dst.Length) continue;
                    if (src[srcIdx + 3] == 0) continue; // skip transparent

                    dst[dstIdx + 0] = src[srcIdx + 0];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
        }
    }
}
