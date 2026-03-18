namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform FE6-specific portrait rendering.
    /// FE6 portraits differ from FE7/8: always LZ77 compressed, no eye states,
    /// mouth frames embedded in sprite sheet (not from separate D12 pointer).
    /// The base face assembly (6 BitBlt calls) is identical to FE7/8.
    /// </summary>
    public static class PortraitRendererCoreFE6
    {
        const int FaceWidth = 96;
        const int FaceHeight = 80;
        const int PartsWidth = 32;
        const int PartsHeight = 16;
        const int SheetWidthPx = 256;
        const int SheetHeightPx = 40;

        // FE6 mouth frame positions in the sprite sheet (px coordinates)
        // These slots are eye frames in FE7/8, but mouth frames in FE6.
        internal static readonly (int x, int y, int w, int h)[] MouthFrames = new[]
        {
            (192, 0,  PartsWidth, PartsHeight),      // Frame 1
            (224, 0,  PartsWidth, PartsHeight),      // Frame 2
            (192, 16, PartsWidth, PartsHeight),      // Frame 3
            (224, 16, PartsWidth, PartsHeight),      // Frame 4
        };
        // Frame 5 is special: split across two half-height regions
        internal const int Frame5TopX = 0, Frame5TopY = 32;
        internal const int Frame5BotX = 32, Frame5BotY = 32;

        /// <summary>
        /// Assemble the full 96x80 FE6 unit portrait from the sprite sheet.
        /// FE6 portrait struct (16 bytes): +0=face, +4=mapface, +8=palette, +12=mouthX, +13=mouthY.
        /// </summary>
        public static IImage DrawPortraitUnitFE6(
            uint unitFacePtr, uint palettePtr,
            byte mouthX, byte mouthY, int showFrame)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;

            uint unitFace = U.toOffset(unitFacePtr);
            uint palette = U.toOffset(palettePtr);

            if (unitFace == 0 || !U.isSafetyOffset(unitFace) || !U.isSafetyOffset(palette))
                return null;

            byte[] gbaPalette = ImageUtilCore.GetPalette(palette, 16);
            if (gbaPalette == null) return null;

            // FE6 portraits are always LZ77 compressed
            byte[] tileData = LZ77.decompress(rom.Data, unitFace);
            if (tileData == null || tileData.Length == 0) return null;

            byte[] sheetPixels = DecodeTilesToRGBA(tileData, SheetWidthPx, SheetHeightPx, gbaPalette, svc);
            if (sheetPixels == null) return null;

            // Assemble 96x80 face — identical to FE7/8
            byte[] facePixels = new byte[FaceWidth * FaceHeight * 4];
            AssembleFace(sheetPixels, SheetWidthPx, facePixels, FaceWidth);

            // Apply FE6 mouth frame overlay (frames 1-5)
            if (showFrame >= 1 && showFrame <= 5)
            {
                ApplyMouthFrame(showFrame, sheetPixels, SheetWidthPx,
                    mouthX, mouthY, facePixels, FaceWidth);
            }

            var image = svc.CreateImage(FaceWidth, FaceHeight);
            image.SetPixelData(facePixels);
            return image;
        }

        /// <summary>
        /// Assemble the 6-part face from the sprite sheet.
        /// This is identical to FE7/8 face assembly.
        /// </summary>
        internal static void AssembleFace(byte[] sheetPixels, int sheetW, byte[] facePixels, int faceW)
        {
            // Upper face: 64x32 from (0,0) -> dest (16,0)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, 0, 0, PartsWidth * 2, PartsHeight * 2,
                facePixels, faceW, PartsWidth / 2, 0);
            // Lower face: 64x32 from (64,0) -> dest (16,32)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, PartsWidth * 2, 0, PartsWidth * 2, PartsHeight * 2,
                facePixels, faceW, PartsWidth / 2, PartsHeight * 2);
            // Right shoulder: 32x16 from (128,0) -> dest (16,64)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, PartsWidth * 4, 0, PartsWidth, PartsHeight,
                facePixels, faceW, PartsWidth / 2, PartsHeight * 4);
            // Right edge: 16x32 from (160,0) -> dest (0,48)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, PartsWidth * 5, 0, PartsWidth / 2, PartsHeight * 2,
                facePixels, faceW, 0, PartsHeight * 3);
            // Left shoulder: 32x16 from (128,16) -> dest (48,64)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, PartsWidth * 4, PartsHeight, PartsWidth, PartsHeight,
                facePixels, faceW, PartsWidth + PartsWidth / 2, PartsHeight * 4);
            // Left edge: 16x32 from (176,0) -> dest (80,48)
            PortraitRendererCore.BlitPixels(sheetPixels, sheetW, PartsWidth * 5 + PartsWidth / 2, 0, PartsWidth / 2, PartsHeight * 2,
                facePixels, faceW, PartsWidth * 2 + PartsWidth / 2, PartsHeight * 3);
        }

        /// <summary>
        /// Apply FE6-specific mouth frame overlay from embedded sheet positions.
        /// </summary>
        internal static void ApplyMouthFrame(int showFrame, byte[] sheetPixels, int sheetW,
            byte mouthX, byte mouthY, byte[] facePixels, int faceW)
        {
            int destX = mouthX * 8;
            int destY = mouthY * 8;

            if (showFrame >= 1 && showFrame <= 4)
            {
                var f = MouthFrames[showFrame - 1];
                PortraitRendererCore.BlitPixelsWithTransparency(
                    sheetPixels, sheetW, f.x, f.y, f.w, f.h,
                    facePixels, faceW, destX, destY);
            }
            else if (showFrame == 5)
            {
                // Frame 5 is split: top half from (0,32), bottom half from (32,32)
                PortraitRendererCore.BlitPixelsWithTransparency(
                    sheetPixels, sheetW, Frame5TopX, Frame5TopY,
                    PartsWidth, PartsHeight / 2,
                    facePixels, faceW, destX, destY);
                PortraitRendererCore.BlitPixelsWithTransparency(
                    sheetPixels, sheetW, Frame5BotX, Frame5BotY,
                    PartsWidth, PartsHeight / 2,
                    facePixels, faceW, destX, destY + PartsHeight / 2);
            }
        }

        /// <summary>
        /// Decode 4bpp tile data into RGBA pixel array (same algorithm as PortraitRendererCore).
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

                            int destXp = tx * 8 + px;
                            int destYp = ty * 8 + py;
                            int idx = (destYp * width + destXp) * 4;
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
    }
}
