using System;
using System.Collections.Generic;

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
        const int SECTION_COUNT = 12;     // 0x0C section modes
        const int SCREEN_TILE_WIDTH = 30; // 240px / 8
        const int SCREEN_TILE_HEIGHT = 20; // 160px / 8

        /// <summary>
        /// Describes a single animation frame within a section.
        /// </summary>
        public struct FrameInfo
        {
            /// <summary>Byte offset within decompressed frame data where this frame's 0x86 command is.</summary>
            public uint FrameDataOffset;
            /// <summary>Pointer to LZ77-compressed graphics tile data for this frame.</summary>
            public uint GraphicsPointer;
            /// <summary>Absolute offset into decompressed OAM data.</summary>
            public uint OamOffset;
        }

        /// <summary>
        /// Section mode names (English). Index 0..11 maps to sections 0x00..0x0B.
        /// </summary>
        public static readonly string[] SectionNames = new string[]
        {
            "0x00 Attack (body)",
            "0x01 Attack (weapon overlay)",
            "0x02 Critical (body)",
            "0x03 Critical (weapon overlay)",
            "0x04 Ranged",
            "0x05 Ranged Critical",
            "0x06 Dodge (melee)",
            "0x07 Dodge (ranged)",
            "0x08 Standing (melee)",
            "0x09 Standing (idle)",
            "0x0A Standing (ranged)",
            "0x0B Miss"
        };

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

        /// <summary>
        /// Decompress frame data from ROM, handling the special uncompressed-frame-pointer case.
        /// </summary>
        public static byte[] DecompressFrameData(ROM rom, uint frameRaw)
        {
            if (rom == null) return null;
            if (FETextEncode.IsUnHuffmanPatchPointer(frameRaw))
            {
                uint converted = FETextEncode.ConvertUnHuffmanPatchToPointer(frameRaw);
                uint off = U.toOffset(converted);
                if (!U.isSafetyOffset(off, rom)) return null;
                // Uncompressed frame data: scan to find length
                uint length = CalcUncompressedFrameLength(rom, off);
                if (length == 0) return null;
                return rom.getBinaryData(off, length);
            }
            else if (U.isPointer(frameRaw))
            {
                uint off = U.toOffset(frameRaw);
                if (!U.isSafetyOffset(off, rom)) return null;
                return LZ77.decompress(rom.Data, off);
            }
            return null;
        }

        /// <summary>
        /// Calculate the length of uncompressed frame data by scanning for the end marker.
        /// </summary>
        static uint CalcUncompressedFrameLength(ROM rom, uint offset)
        {
            // Scan 4 bytes at a time looking for termination (all zeros or past end)
            uint maxLen = 0x100000; // safety cap at 1MB
            for (uint n = 0; n < maxLen; n += 4)
            {
                if (offset + n + 4 > (uint)rom.Data.Length) return n;
                uint b3 = rom.u8(offset + n + 3);
                if (b3 == 0x80) // terminator command
                    return n + 4;
            }
            return 0;
        }

        /// <summary>
        /// Get the section start/end byte offsets within decompressed frame data
        /// for a given section index (0..11).
        /// </summary>
        /// <param name="sectionIndex">Section index (0-11).</param>
        /// <param name="sectionDataOffset">ROM offset of the 12-entry uint32 section array.</param>
        /// <param name="frameDataLength">Total length of decompressed frame data.</param>
        /// <param name="rom">ROM to read from.</param>
        /// <param name="start">Output: start byte offset in frame data.</param>
        /// <param name="end">Output: end byte offset in frame data.</param>
        public static void GetSectionRange(int sectionIndex, uint sectionDataOffset,
            uint frameDataLength, ROM rom, out uint start, out uint end)
        {
            start = 0;
            end = frameDataLength;

            if (rom == null || sectionIndex < 0 || sectionIndex >= SECTION_COUNT)
                return;

            uint readAddr = sectionDataOffset + (uint)(sectionIndex * 4);
            if (U.isSafetyOffset(readAddr, rom))
            {
                uint val = rom.u32(readAddr);
                if (val <= frameDataLength) start = val;
            }

            // End = next section's start, or frameDataLength
            if (sectionIndex + 1 < SECTION_COUNT)
            {
                uint nextAddr = sectionDataOffset + (uint)((sectionIndex + 1) * 4);
                if (U.isSafetyOffset(nextAddr, rom))
                {
                    uint val = rom.u32(nextAddr);
                    if (val > 0 && val <= frameDataLength) end = val;
                    else end = frameDataLength;
                }
            }

            // Fix: if start is 0 but shouldn't be, use section 1's start as end
            if (start == 0 && sectionIndex == 0)
            {
                uint sec1Addr = sectionDataOffset + 4;
                if (U.isSafetyOffset(sec1Addr, rom))
                {
                    uint sec1Val = rom.u32(sec1Addr);
                    if (sec1Val > 0 && sec1Val <= frameDataLength) end = sec1Val;
                }
            }
            if (end == 0) end = frameDataLength;
        }

        /// <summary>
        /// Count the number of displayable frames (0x86 commands) in a given
        /// range of decompressed frame data.
        /// </summary>
        public static int CountFramesInRange(byte[] frameData, uint start, uint end)
        {
            if (frameData == null) return 0;
            int count = 0;
            for (uint n = start; n < end && n + 3 < (uint)frameData.Length; n += 4)
            {
                if (frameData[n + 3] == 0x86)
                {
                    count++;
                    n += 8; // 0x86 command is followed by 4-byte gfx pointer + 4-byte OAM offset = 12 total
                }
            }
            return count;
        }

        /// <summary>
        /// Parse all frames (0x86 commands) in the given range of frame data.
        /// </summary>
        public static List<FrameInfo> ParseFramesInRange(byte[] frameData, uint start, uint end)
        {
            var frames = new List<FrameInfo>();
            if (frameData == null) return frames;

            for (uint n = start; n < end && n + 3 < (uint)frameData.Length; n += 4)
            {
                if (frameData[n + 3] == 0x86)
                {
                    if (n + 12 <= (uint)frameData.Length)
                    {
                        var fi = new FrameInfo
                        {
                            FrameDataOffset = n,
                            GraphicsPointer = U.u32(frameData, n + 4),
                            OamOffset = U.u32(frameData, n + 8),
                        };
                        frames.Add(fi);
                    }
                    n += 8; // skip the 8 extra bytes (gfx ptr + oam offset)
                }
            }
            return frames;
        }

        /// <summary>
        /// Render a single animation frame as an image.
        /// Uses the frame's graphics pointer to get tile data, the OAM data for sprite layout,
        /// and the palette for coloring.
        /// </summary>
        /// <param name="frame">Frame info from ParseFramesInRange.</param>
        /// <param name="oamData">Decompressed OAM data (right-to-left).</param>
        /// <param name="paletteData">Decompressed palette data.</param>
        /// <returns>An IImage of the rendered frame, or null on failure.</returns>
        public static IImage RenderSingleFrame(FrameInfo frame, byte[] oamData, byte[] paletteData)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null || oamData == null || paletteData == null)
                return null;

            // Decompress the graphics tile data for this frame
            uint gfxPtr = frame.GraphicsPointer;
            byte[] gfxData = null;
            if (U.isPointer(gfxPtr))
            {
                uint gfxOff = U.toOffset(gfxPtr);
                if (U.isSafetyOffset(gfxOff, rom))
                    gfxData = LZ77.decompress(rom.Data, gfxOff);
            }
            if (gfxData == null || gfxData.Length == 0) return null;

            // Create the output image (240x160, GBA screen size)
            int width = SCREEN_TILE_WIDTH * TILE_SIZE;   // 240
            int height = SCREEN_TILE_HEIGHT * TILE_SIZE;  // 160
            byte[] pixels = new byte[width * height * 4]; // RGBA

            // Decode graphics as a 256x64 tile sheet (standard battle animation format)
            // 256 / 8 = 32 tiles per row, 64 / 8 = 8 rows = 256 tiles
            int sheetWidthTiles = 32;
            int sheetHeightTiles = 8;
            int sheetWidth = sheetWidthTiles * TILE_SIZE;  // 256
            int sheetHeight = sheetHeightTiles * TILE_SIZE; // 64

            // Decode the 4bpp graphics into an RGBA pixel buffer for the source sheet
            int totalGfxTiles = gfxData.Length / BYTES_PER_TILE_4BPP;
            byte[] sheetPixels = new byte[sheetWidth * sheetHeight * 4];

            for (int t = 0; t < totalGfxTiles && t < sheetWidthTiles * sheetHeightTiles; t++)
            {
                int tileOff = t * BYTES_PER_TILE_4BPP;
                int tileCol = t % sheetWidthTiles;
                int tileRow = t / sheetWidthTiles;

                for (int py = 0; py < TILE_SIZE; py++)
                {
                    for (int px = 0; px < TILE_SIZE; px++)
                    {
                        int bytePos = tileOff + py * 4 + px / 2;
                        if (bytePos >= gfxData.Length) continue;

                        byte b = gfxData[bytePos];
                        int ci = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                        int palOff = ci * 2;
                        if (palOff + 2 > paletteData.Length) continue;

                        ushort gbaColor = (ushort)(paletteData[palOff] | (paletteData[palOff + 1] << 8));
                        svc.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte bl);

                        int sx = tileCol * TILE_SIZE + px;
                        int sy = tileRow * TILE_SIZE + py;
                        int si = (sy * sheetWidth + sx) * 4;
                        if (si + 3 < sheetPixels.Length)
                        {
                            sheetPixels[si + 0] = r;
                            sheetPixels[si + 1] = g;
                            sheetPixels[si + 2] = bl;
                            sheetPixels[si + 3] = (byte)(ci == 0 ? 0 : 255);
                        }
                    }
                }
            }

            // Parse OAM entries starting at frame.OamOffset and draw sprites
            uint oamStart = frame.OamOffset;
            DrawOAMSprites(oamData, oamStart, sheetPixels, sheetWidth, sheetHeight,
                           pixels, width, height);

            var image = svc.CreateImage(width, height);
            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Parse OAM entries and blit sprites from the source sheet onto the destination.
        /// OAM format: 12 bytes per entry, terminates when first byte is 1.
        /// </summary>
        static void DrawOAMSprites(byte[] oamData, uint oamStart,
            byte[] srcPixels, int srcWidth, int srcHeight,
            byte[] dstPixels, int dstWidth, int dstHeight)
        {
            if (oamData == null || oamStart >= (uint)oamData.Length) return;

            // Collect all OAM entries first, then draw in reverse order (back to front)
            var entries = new List<(int imgX, int imgY, int sheetX, int sheetY,
                                    int w, int h, bool hFlip, bool vFlip)>();

            for (uint pos = oamStart; pos + 5 < (uint)oamData.Length; )
            {
                byte terminator = oamData[pos];
                if (terminator == 0x01) break; // end of OAM list

                // GBA OAM attr0, attr1, attr2 in 12 bytes (parsed as 3 uint32 or 6 uint16)
                if (pos + 12 > (uint)oamData.Length) break;

                ushort attr0 = (ushort)(oamData[pos] | (oamData[pos + 1] << 8));
                ushort attr1 = (ushort)(oamData[pos + 2] | (oamData[pos + 3] << 8));
                ushort attr2 = (ushort)(oamData[pos + 4] | (oamData[pos + 5] << 8));

                // Y coordinate (signed 8-bit from attr0 bits 0-7)
                int imgY = (int)(sbyte)(attr0 & 0xFF);
                // Shape from attr0 bits 14-15
                int shape = (attr0 >> 14) & 3;

                // X coordinate (signed 9-bit from attr1 bits 0-8)
                int imgX = (int)(attr1 & 0x1FF);
                if (imgX >= 256) imgX -= 512;
                // Size from attr1 bits 14-15
                int sizeCode = (attr1 >> 14) & 3;
                bool hFlip = ((attr1 >> 12) & 1) == 1;
                bool vFlip = ((attr1 >> 13) & 1) == 1;

                // Tile number from attr2 bits 0-9
                int tileNum = attr2 & 0x3FF;

                // Determine width/height from shape + size
                GetOAMSize(shape, sizeCode, out int sprW, out int sprH);

                // Calculate sheet position from tile number
                // In 1D mapping mode, tiles are contiguous
                int tilesPerRow = srcWidth / TILE_SIZE; // 32 for 256-wide
                int sheetTileX = tileNum % tilesPerRow;
                int sheetTileY = tileNum / tilesPerRow;
                int sheetX = sheetTileX * TILE_SIZE;
                int sheetY = sheetTileY * TILE_SIZE;

                // Offset to center on the 240x160 screen
                int drawX = imgX + 120;
                int drawY = imgY + 80;

                entries.Add((drawX, drawY, sheetX, sheetY, sprW, sprH, hFlip, vFlip));

                pos += 12;
            }

            // Draw in reverse order (first entries are drawn on top)
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                BlitSprite(srcPixels, srcWidth, srcHeight,
                           e.sheetX, e.sheetY, e.w, e.h,
                           dstPixels, dstWidth, dstHeight,
                           e.imgX, e.imgY, e.hFlip, e.vFlip);
            }
        }

        /// <summary>
        /// Get OAM sprite dimensions from shape and size codes.
        /// </summary>
        static void GetOAMSize(int shape, int size, out int width, out int height)
        {
            // GBA OAM size table
            // shape 0 = square, 1 = wide, 2 = tall
            switch (shape)
            {
                case 0: // Square
                    switch (size)
                    {
                        case 0: width = 8; height = 8; return;
                        case 1: width = 16; height = 16; return;
                        case 2: width = 32; height = 32; return;
                        default: width = 64; height = 64; return;
                    }
                case 1: // Wide
                    switch (size)
                    {
                        case 0: width = 16; height = 8; return;
                        case 1: width = 32; height = 8; return;
                        case 2: width = 32; height = 16; return;
                        default: width = 64; height = 32; return;
                    }
                case 2: // Tall
                    switch (size)
                    {
                        case 0: width = 8; height = 16; return;
                        case 1: width = 8; height = 32; return;
                        case 2: width = 16; height = 32; return;
                        default: width = 32; height = 64; return;
                    }
                default:
                    width = 8; height = 8; return;
            }
        }

        /// <summary>
        /// Blit a rectangular region from source to destination with optional flipping.
        /// Uses alpha-aware compositing (skip transparent pixels).
        /// </summary>
        static void BlitSprite(byte[] src, int srcStride, int srcH,
                               int srcX, int srcY, int w, int h,
                               byte[] dst, int dstStride, int dstH,
                               int dstX, int dstY, bool hFlip, bool vFlip)
        {
            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    int sx = srcX + (hFlip ? (w - 1 - px) : px);
                    int sy = srcY + (vFlip ? (h - 1 - py) : py);
                    if (sx < 0 || sx >= srcStride || sy < 0 || sy >= srcH) continue;

                    int si = (sy * srcStride + sx) * 4;
                    if (si + 3 >= src.Length) continue;
                    if (src[si + 3] == 0) continue; // transparent

                    int dx = dstX + px;
                    int dy = dstY + py;
                    if (dx < 0 || dx >= dstStride || dy < 0 || dy >= dstH) continue;

                    int di = (dy * dstStride + dx) * 4;
                    if (di + 3 >= dst.Length) continue;

                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }
    }
}
