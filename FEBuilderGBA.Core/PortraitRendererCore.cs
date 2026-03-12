using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Result of splitting a 128x112 composite portrait sheet into its component parts.
    /// </summary>
    public class PortraitSheetParts
    {
        /// <summary>Sprite sheet RGBA pixels (256x32) ready for tile encoding.</summary>
        public byte[] SpriteSheetPixels;
        public int SpriteSheetW, SpriteSheetH;

        /// <summary>Map/mini face RGBA pixels (32x32).</summary>
        public byte[] MiniPixels;
        public int MiniW, MiniH;

        /// <summary>Mouth frame RGBA pixels (32x96, 6 frames of 32x16).</summary>
        public byte[] MouthPixels;
        public int MouthW, MouthH;
    }

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

        // Mouth frame data: 4 tiles wide (32px), 6 frames each 2 tiles high (16px) = 96px total
        const int MouthFrameCount = 6;
        const int MouthTilesX = 4;
        const int MouthTilesY = 2; // per frame
        const int MouthFrameWidth = MouthTilesX * 8;   // 32
        const int MouthFrameHeight = MouthTilesY * 8;   // 16

        /// <summary>
        /// Show frame indices for the portrait display.
        /// 0 = Normal (no overlay), 1 = Half-closed eyes, 2 = Closed eyes,
        /// 3-8 = Mouth frames 1-6 (from D12 data), 9 = Mouth frame 7 (from sheet).
        /// </summary>
        public const int FrameNormal = 0;
        public const int FrameEyeHalf = 1;
        public const int FrameEyeClosed = 2;
        public const int FrameMouth1 = 3;
        public const int FrameMouth2 = 4;
        public const int FrameMouth3 = 5;
        public const int FrameMouth4 = 6;
        public const int FrameMouth5 = 7;
        public const int FrameMouth6 = 8;
        public const int FrameMouth7 = 9;
        public const int MaxShowFrame = 9;

        /// <summary>
        /// Assemble the full 96x80 unit portrait from the sprite sheet parts.
        /// This is the cross-platform equivalent of ImagePortraitForm.DrawPortraitUnit.
        /// </summary>
        public static IImage DrawPortraitUnit(
            uint unitFacePtr, uint palettePtr,
            byte eyeX, byte eyeY, byte state)
        {
            return DrawPortraitUnitWithFrame(unitFacePtr, palettePtr, 0, 0, 0,
                eyeX, eyeY, state, 0);
        }

        /// <summary>
        /// Assemble the full 96x80 unit portrait with mouth/eye frame overlay.
        /// showFrame: 0=normal, 1=half-eye, 2=closed-eye, 3-8=mouth1-6, 9=mouth7.
        /// </summary>
        public static IImage DrawPortraitUnitWithFrame(
            uint unitFacePtr, uint palettePtr, uint mouthPtr,
            byte mouthX, byte mouthY,
            byte eyeX, byte eyeY, byte state, int showFrame)
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
            // Upper face: 64x32 from (0,0) -> dest (16,0)
            BlitPixels(sheetPixels, sheetW, 0, 0, PartsWidth * 2, PartsHeight * 2,
                       facePixels, FaceWidth, PartsWidth / 2, 0);
            // Lower face: 64x32 from (64,0) -> dest (16,32)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 2, 0, PartsWidth * 2, PartsHeight * 2,
                       facePixels, FaceWidth, PartsWidth / 2, PartsHeight * 2);
            // Right shoulder: 32x16 from (128,0) -> dest (16,64)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 4, 0, PartsWidth, PartsHeight,
                       facePixels, FaceWidth, PartsWidth / 2, PartsHeight * 4);
            // Right edge: 16x32 from (160,0) -> dest (0,48)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 5, 0, PartsWidth / 2, PartsHeight * 2,
                       facePixels, FaceWidth, 0, PartsHeight * 3);
            // Left shoulder: 32x16 from (128,16) -> dest (48,64)
            BlitPixels(sheetPixels, sheetW, PartsWidth * 4, PartsHeight, PartsWidth, PartsHeight,
                       facePixels, FaceWidth, PartsWidth + PartsWidth / 2, PartsHeight * 4);
            // Left edge: 16x32 from (176,0) -> dest (80,48)
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

            // Apply show frame overlay (eye/mouth animation)
            if (showFrame > 0)
            {
                ApplyShowFrame(showFrame, sheetPixels, sheetW, gbaPalette, svc,
                    mouthPtr, mouthX, mouthY, eyeX, eyeY,
                    facePixels, FaceWidth);
            }

            // Create IImage from pixel data
            var image = svc.CreateImage(FaceWidth, FaceHeight);
            image.SetPixelData(facePixels);
            return image;
        }

        /// <summary>
        /// Apply a show frame overlay (eye or mouth) onto the assembled face pixels.
        /// </summary>
        static void ApplyShowFrame(int showFrame, byte[] sheetPixels, int sheetW,
            byte[] gbaPalette, IImageService svc,
            uint mouthPtr, byte mouthX, byte mouthY, byte eyeX, byte eyeY,
            byte[] facePixels, int faceW)
        {
            switch (showFrame)
            {
                case FrameEyeHalf: // Half-closed eyes from sheet
                    BlitPixelsWithTransparency(
                        sheetPixels, sheetW, SheetWidthPx - PartsWidth * 2, 0,
                        PartsWidth, PartsHeight,
                        facePixels, faceW, eyeX * 8, eyeY * 8);
                    break;
                case FrameEyeClosed: // Closed eyes from sheet
                    BlitPixelsWithTransparency(
                        sheetPixels, sheetW, SheetWidthPx - PartsWidth * 2, PartsHeight,
                        PartsWidth, PartsHeight,
                        facePixels, faceW, eyeX * 8, eyeY * 8);
                    break;
                case FrameMouth1:
                case FrameMouth2:
                case FrameMouth3:
                case FrameMouth4:
                case FrameMouth5:
                case FrameMouth6:
                {
                    // Mouth frames 1-6 from D12 data
                    byte[] mouthPixels = DecodeMouthFrames(mouthPtr, gbaPalette, svc);
                    if (mouthPixels != null)
                    {
                        int frameIdx = showFrame - FrameMouth1; // 0-5
                        BlitPixelsWithTransparency(
                            mouthPixels, MouthFrameWidth, 0, frameIdx * MouthFrameHeight,
                            MouthFrameWidth, MouthFrameHeight,
                            facePixels, faceW, mouthX * 8, mouthY * 8);
                    }
                    break;
                }
                case FrameMouth7: // Mouth frame 7 from main sheet
                    BlitPixelsWithTransparency(
                        sheetPixels, sheetW, SheetWidthPx - PartsWidth, 0,
                        PartsWidth, PartsHeight,
                        facePixels, faceW, mouthX * 8, mouthY * 8);
                    break;
            }
        }

        /// <summary>
        /// Decode the 6 mouth frames from the D12 pointer into RGBA pixels.
        /// Returns a 32x96 RGBA pixel array (6 frames of 32x16 each).
        /// </summary>
        static byte[] DecodeMouthFrames(uint mouthPtr, byte[] gbaPalette, IImageService svc)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint mouth = U.toOffset(mouthPtr);
            if (mouth == 0 || !U.isSafetyOffset(mouth)) return null;

            int totalTiles = MouthTilesX * MouthTilesY * MouthFrameCount; // 4*2*6 = 48 tiles
            int dataLen = totalTiles * 32; // 32 bytes per 4bpp tile
            if (mouth + dataLen > (uint)rom.Data.Length) return null;

            byte[] tileData = new byte[dataLen];
            Array.Copy(rom.Data, mouth, tileData, 0, dataLen);

            int totalHeight = MouthFrameHeight * MouthFrameCount; // 96px
            return DecodeTilesToRGBA(tileData, MouthFrameWidth, totalHeight, gbaPalette, svc);
        }

        /// <summary>
        /// Render all 6 mouth frames as a single strip image (32x96).
        /// Used for displaying the mouth frame strip in the UI.
        /// </summary>
        public static IImage DrawMouthFrameStrip(uint mouthPtr, uint palettePtr)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;

            uint palette = U.toOffset(palettePtr);
            if (!U.isSafetyOffset(palette)) return null;

            byte[] gbaPalette = ImageUtilCore.GetPalette(palette, 16);
            if (gbaPalette == null) return null;

            byte[] pixels = DecodeMouthFrames(mouthPtr, gbaPalette, svc);
            if (pixels == null) return null;

            int totalHeight = MouthFrameHeight * MouthFrameCount;
            var image = svc.CreateImage(MouthFrameWidth, totalHeight);
            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Render eye frames from the main sprite sheet.
        /// Returns a 32x32 image: top 16px = half-closed eyes, bottom 16px = closed eyes.
        /// </summary>
        public static IImage DrawEyeFrameStrip(uint unitFacePtr, uint palettePtr)
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

            byte[] tileData;
            if (LZ77.iscompress(rom.Data, unitFace))
            {
                tileData = LZ77.decompress(rom.Data, unitFace);
                if (tileData == null || tileData.Length == 0) return null;
            }
            else
            {
                int sheetH = IsHalfBodyFlag(unitFace) ? 10 * 8 : SheetHeightPx;
                uint dataOffset = unitFace + 4;
                int dataLen = (SheetWidthPx / 8) * (sheetH / 8) * 32;
                if (dataOffset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, dataOffset, tileData, 0, dataLen);
            }

            byte[] sheetPixels = DecodeTilesToRGBA(tileData, SheetWidthPx, SheetHeightPx, gbaPalette, svc);
            if (sheetPixels == null) return null;

            // Extract 2 eye frames (32x16 each) into a 32x32 strip
            int stripW = PartsWidth;
            int stripH = PartsHeight * 2;
            byte[] stripPixels = new byte[stripW * stripH * 4];

            // Half-closed eyes: from (sheetW - 64, 0)
            BlitPixels(sheetPixels, SheetWidthPx, SheetWidthPx - PartsWidth * 2, 0,
                PartsWidth, PartsHeight, stripPixels, stripW, 0, 0);
            // Closed eyes: from (sheetW - 64, 16)
            BlitPixels(sheetPixels, SheetWidthPx, SheetWidthPx - PartsWidth * 2, PartsHeight,
                PartsWidth, PartsHeight, stripPixels, stripW, 0, PartsHeight);

            var image = svc.CreateImage(stripW, stripH);
            image.SetPixelData(stripPixels);
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
        /// Split a 128x112 composite portrait sheet into sprite sheet, mini face, and mouth frames.
        /// The 128x112 sheet layout matches the WinForms export format:
        ///   Face (96x80) at (16, 0) — assembled portrait
        ///   Mini face (32x32) at (96, 16)
        ///   Half-closed eyes (32x16) at (96, 48)
        ///   Closed eyes (32x16) at (96, 64)
        ///   Mouth frames: (0,80)(32,80)(64,80)(0,96)(32,96)(64,96) — 6 frames of 32x16
        ///   Mouth7 (32x16) at (96, 80) — stored in sprite sheet
        ///   Unused (32x16) at (96, 96)
        /// Returns null if the image is not 128x112.
        /// </summary>
        public static PortraitSheetParts SplitPortraitSheet(byte[] rgba, int w, int h)
        {
            if (w != 128 || h != 112) return null;
            if (rgba == null || rgba.Length < w * h * 4) return null;

            // Sprite sheet: 256x32 (32 tiles wide x 4 tiles high)
            int sheetW = 256, sheetH = 32;
            byte[] sheet = new byte[sheetW * sheetH * 4];

            // Reverse-assemble face parts from the composed 96x80 face into sprite sheet layout
            // Upper face: seet(16, 0) 64x32 -> sheet(0, 0)
            BlitPixels(rgba, w, PartsWidth / 2, 0, PartsWidth * 2, PartsHeight * 2,
                sheet, sheetW, 0, 0);
            // Lower face: seet(16, 32) 64x32 -> sheet(64, 0)
            BlitPixels(rgba, w, PartsWidth / 2, PartsHeight * 2, PartsWidth * 2, PartsHeight * 2,
                sheet, sheetW, PartsWidth * 2, 0);
            // Right shoulder: seet(16, 64) 32x16 -> sheet(128, 0)
            BlitPixels(rgba, w, PartsWidth / 2, PartsHeight * 4, PartsWidth, PartsHeight,
                sheet, sheetW, PartsWidth * 4, 0);
            // Right edge: seet(0, 48) 16x32 -> sheet(160, 0)
            BlitPixels(rgba, w, 0, PartsHeight * 3, PartsWidth / 2, PartsHeight * 2,
                sheet, sheetW, PartsWidth * 5, 0);
            // Left shoulder: seet(48, 64) 32x16 -> sheet(128, 16)
            BlitPixels(rgba, w, PartsWidth + PartsWidth / 2, PartsHeight * 4, PartsWidth, PartsHeight,
                sheet, sheetW, PartsWidth * 4, PartsHeight);
            // Left edge: seet(80, 48) 16x32 -> sheet(176, 0)
            BlitPixels(rgba, w, PartsWidth * 2 + PartsWidth / 2, PartsHeight * 3, PartsWidth / 2, PartsHeight * 2,
                sheet, sheetW, PartsWidth * 5 + PartsWidth / 2, 0);

            // Half-closed eyes: seet(96, 48) 32x16 -> sheet(192, 0)
            BlitPixels(rgba, w, FaceWidth, 32 + PartsHeight, PartsWidth, PartsHeight,
                sheet, sheetW, sheetW - PartsWidth * 2, 0);
            // Closed eyes: seet(96, 64) 32x16 -> sheet(192, 16)
            BlitPixels(rgba, w, FaceWidth, 32 + PartsHeight * 2, PartsWidth, PartsHeight,
                sheet, sheetW, sheetW - PartsWidth * 2, PartsHeight);

            // Mouth7: seet(96, 80) 32x16 -> sheet(224, 0)
            BlitPixels(rgba, w, FaceWidth, FaceHeight, PartsWidth, PartsHeight,
                sheet, sheetW, sheetW - PartsWidth, 0);
            // Unused slot: seet(96, 96) 32x16 -> sheet(224, 16)
            BlitPixels(rgba, w, FaceWidth, FaceHeight + PartsHeight, PartsWidth, PartsHeight,
                sheet, sheetW, sheetW - PartsWidth, PartsHeight);

            // Mini/map face: seet(96, 16) 32x32
            int miniW = 32, miniH = 32;
            byte[] mini = new byte[miniW * miniH * 4];
            BlitPixels(rgba, w, FaceWidth, PartsHeight, miniW, miniH, mini, miniW, 0, 0);

            // Mouth frames: 6 frames of 32x16 each = 32x96 total
            int mouthW = 32, mouthH = 96;
            byte[] mouth = new byte[mouthW * mouthH * 4];
            // Frame 0: seet(0, 80)
            BlitPixels(rgba, w, 0, FaceHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, 0);
            // Frame 1: seet(32, 80)
            BlitPixels(rgba, w, PartsWidth, FaceHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, PartsHeight);
            // Frame 2: seet(64, 80)
            BlitPixels(rgba, w, PartsWidth * 2, FaceHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, PartsHeight * 2);
            // Frame 3: seet(0, 96)
            BlitPixels(rgba, w, 0, FaceHeight + PartsHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, PartsHeight * 3);
            // Frame 4: seet(32, 96)
            BlitPixels(rgba, w, PartsWidth, FaceHeight + PartsHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, PartsHeight * 4);
            // Frame 5: seet(64, 96)
            BlitPixels(rgba, w, PartsWidth * 2, FaceHeight + PartsHeight, PartsWidth, PartsHeight, mouth, mouthW, 0, PartsHeight * 5);

            return new PortraitSheetParts
            {
                SpriteSheetPixels = sheet,
                SpriteSheetW = sheetW,
                SpriteSheetH = sheetH,
                MiniPixels = mini,
                MiniW = miniW,
                MiniH = miniH,
                MouthPixels = mouth,
                MouthW = mouthW,
                MouthH = mouthH,
            };
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
