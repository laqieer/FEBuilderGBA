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
        /// Reads LZ77-compressed palette from offset 28, decompresses frame command
        /// stream from offset 16, then extracts the actual graphics tile data from
        /// the first 0x86 frame command's GraphicsPointer.
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

            // Decompress palette (LZ77-compressed)
            byte[] gbaPalette;
            if (U.isPointer(paletteRaw))
            {
                uint palOff = U.toOffset(paletteRaw);
                if (!U.isSafetyOffset(palOff, rom)) return null;
                gbaPalette = ImageUtilCore.GetCompressedPalette(palOff, 16);
            }
            else
            {
                return null;
            }
            if (gbaPalette == null) return null;

            // Decompress frame command stream
            byte[] frameData = DecompressFrameData(rom, frameRaw);
            if (frameData == null || frameData.Length == 0) return null;

            // Find the first 0x86 frame command with a valid graphics pointer
            byte[] tileData = null;
            for (uint n = 0; n + 11 < (uint)frameData.Length; n += 4)
            {
                if (frameData[n + 3] == 0x86)
                {
                    uint gfxPtr = U.u32(frameData, n + 4);
                    if (U.isPointer(gfxPtr))
                    {
                        uint gfxOff = U.toOffset(gfxPtr);
                        if (U.isSafetyOffset(gfxOff, rom))
                        {
                            tileData = LZ77.decompress(rom.Data, gfxOff);
                            if (tileData != null && tileData.Length > 0)
                                break;
                        }
                    }
                    n += 8; // skip the 8 extra bytes (gfx ptr + oam offset)
                }
            }

            if (tileData == null || tileData.Length == 0) return null;

            return RenderTileSheet(tileData, gbaPalette, tilesPerRow);
        }

        /// <summary>
        /// Render a tile sheet from a frame's GraphicsPointer and palette data.
        /// Decompresses the tile data from the graphics pointer and renders using the first 16 palette colors.
        /// </summary>
        public static IImage RenderFrameTileSheet(uint graphicsPointer, byte[] paletteData, int tilesPerRow = 32)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null || paletteData == null) return null;
            if (!U.isPointer(graphicsPointer)) return null;

            uint gfxOff = U.toOffset(graphicsPointer);
            if (!U.isSafetyOffset(gfxOff, rom)) return null;

            byte[] tileData = LZ77.decompress(rom.Data, gfxOff);
            if (tileData == null || tileData.Length == 0) return null;

            // Use first 16 colors (32 bytes) from palette
            byte[] pal16;
            if (paletteData.Length >= 32)
            {
                pal16 = new byte[32];
                Array.Copy(paletteData, pal16, 32);
            }
            else
            {
                pal16 = paletteData;
            }

            return RenderTileSheet(tileData, pal16, tilesPerRow);
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

        // -----------------------------------------------------------------
        // Sample-preview helpers (#822) — the cross-platform port of the
        // WinForms ImageBattleAnimePalletForm.DrawSample 12-frame grid +
        // SCALE_90 crop + IsBlankBitmap blank-check + section/frame advance.
        // -----------------------------------------------------------------

        /// <summary>Cropped sample cell size — mirrors WF SCALE_90 (90x90).</summary>
        public const int SampleCellSize = 90;
        /// <summary>Crop window source X — mirrors WF BitBlt(... , bitmap, 100, 30).</summary>
        public const int SampleCropSrcX = 100;
        /// <summary>Crop window source Y — mirrors WF BitBlt(... , bitmap, 100, 30).</summary>
        public const int SampleCropSrcY = 30;
        /// <summary>Number of sample cells — mirrors WF DrawSample's Bitmap[12].</summary>
        public const int SampleFrameCount = 12;
        /// <summary>Composite grid width — mirrors WF Blank(360, 290).</summary>
        public const int SampleGridWidth = 360;
        /// <summary>Composite grid height — mirrors WF Blank(360, 290).</summary>
        public const int SampleGridHeight = 290;

        /// <summary>
        /// Copy a rectangular <paramref name="w"/>x<paramref name="h"/> window
        /// starting at source pixel (<paramref name="srcX"/>, <paramref name="srcY"/>)
        /// out of <paramref name="src"/> into a fresh RGBA image. Out-of-bounds
        /// source pixels are left transparent (RGBA 0,0,0,0).
        ///
        /// <para>This is the cross-platform equivalent of WinForms
        /// <c>ImageBattleAnimeForm.DrawBattleAnime</c>'s SCALE_90 branch:
        /// <c>Blank(90,90)</c> + <c>BitBlt(trim, 0,0, 90,90, bitmap, 100, 30)</c>
        /// — i.e. a CROP of the (100,30)..(190,120) window, NOT a scale.</para>
        /// </summary>
        /// <param name="src">Source RGBA image.</param>
        /// <param name="srcX">Source window X origin.</param>
        /// <param name="srcY">Source window Y origin.</param>
        /// <param name="w">Crop width.</param>
        /// <param name="h">Crop height.</param>
        /// <returns>A fresh <paramref name="w"/>x<paramref name="h"/> RGBA image, or null on failure.</returns>
        public static IImage CropImage(IImage src, int srcX, int srcY, int w, int h)
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null || src == null || w <= 0 || h <= 0)
                return null;

            byte[] srcPixels = src.GetPixelData();
            if (srcPixels == null)
                return null;

            int srcW = src.Width;
            int srcH = src.Height;
            byte[] outPixels = new byte[w * h * 4]; // RGBA, zero-filled = transparent

            for (int y = 0; y < h; y++)
            {
                int sy = srcY + y;
                if (sy < 0 || sy >= srcH) continue;
                for (int x = 0; x < w; x++)
                {
                    int sx = srcX + x;
                    if (sx < 0 || sx >= srcW) continue;

                    int si = (sy * srcW + sx) * 4;
                    if (si + 3 >= srcPixels.Length) continue;

                    int di = (y * w + x) * 4;
                    outPixels[di + 0] = srcPixels[si + 0];
                    outPixels[di + 1] = srcPixels[si + 1];
                    outPixels[di + 2] = srcPixels[si + 2];
                    outPixels[di + 3] = srcPixels[si + 3];
                }
            }

            var image = svc.CreateImage(w, h);
            image.SetPixelData(outPixels);
            return image;
        }

        /// <summary>
        /// Return true when <paramref name="img"/> has at most
        /// <paramref name="threshold"/> non-transparent pixels.
        ///
        /// <para>Cross-platform mirror of WinForms
        /// <c>ImageUtil.IsBlankBitmap(bmp, emptySize)</c>: WF iterates the
        /// 8bpp indexed bitmap and counts pixels whose palette index byte is
        /// <c>&gt; 0</c> (index 0 = transparent), returning
        /// <c>dotCount &lt;= emptySize</c>. Here the equivalent is the RGBA
        /// alpha channel: <c>RenderSingleFrame</c> writes alpha 0 for color
        /// index 0 and alpha 255 otherwise, so "alpha &gt; 0" == "index &gt; 0".
        /// The default threshold of 10 matches WF's <c>DrawSample</c> call
        /// (<c>IsBlankBitmap(animeframe[index], 10)</c>).</para>
        /// </summary>
        /// <param name="img">Image to test (RGBA).</param>
        /// <param name="threshold">Max non-transparent pixel count to still count as blank.</param>
        /// <returns>True if the image is effectively blank.</returns>
        public static bool IsBlankImage(IImage img, int threshold = 10)
        {
            if (img == null) return true;
            byte[] pixels = img.GetPixelData();
            if (pixels == null) return true;

            int dotCount = 0;
            // RGBA: alpha at index 3 of every 4-byte group.
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    dotCount++;
                    if (dotCount > threshold) return false;
                }
            }
            return dotCount <= threshold;
        }

        /// <summary>
        /// Render the 12-frame battle-animation sample-preview grid for the
        /// 32-byte animation record at <paramref name="animeRecordAddr"/>,
        /// recolored with the <paramref name="paletteIndex"/>-th 16-color
        /// sub-palette. Mirrors WinForms
        /// <c>ImageBattleAnimePalletForm.DrawSample(battleAnimeID, paletteIndex)</c>.
        ///
        /// <para><b>Record resolution.</b> <paramref name="animeRecordAddr"/>
        /// is the ROM offset of the record itself (NOT a 1-based anime id — the
        /// caller already resolved <c>baseAddr + i*0x20</c>, so there is no WF
        /// <c>id-1</c> conversion here). Pointer/offset conventions mirror
        /// <c>ImageBattleAnimeViewModel.InitFrameNavigation</c> exactly:
        /// section <c>u32(rec+12)</c> → OFFSET (<c>U.toOffset</c>, fed to
        /// <see cref="GetSectionRange"/>); frame <c>u32(rec+16)</c> → POINTER
        /// (passed raw to <see cref="DecompressFrameData"/>, which
        /// <c>toOffset</c>s internally); OAM <c>u32(rec+20)</c> → POINTER
        /// (→ offset → LZ77); palette <c>u32(rec+0x1C)</c> → POINTER
        /// (→ offset → LZ77).</para>
        ///
        /// <para><b>Palette.</b> The palette block is LZ77-compressed; this
        /// decompresses it and slices the <c>paletteIndex*0x20</c> 16-color
        /// block (guarding length, falling back to block 0) — the
        /// cross-platform equivalent of WF <c>ImageUtil.SwapPalette</c>.</para>
        ///
        /// <para><b>Per-cell.</b> Each frame is rendered via
        /// <see cref="RenderSingleFrame"/> (240x160), then cropped to 90x90 at
        /// source (100,30) via <see cref="CropImage"/> (WF SCALE_90), then
        /// blank-checked via <see cref="IsBlankImage"/> on the CROP. A single
        /// section/frame cursor PERSISTS across all 12 cells (advanced section
        /// carries forward — mirrors WF declaring <c>showsecstion</c>/
        /// <c>showframe</c> outside the loop). On a blank crop the advance order
        /// is WF's exact one: <c>frame += 2</c>, then on a still-blank crop
        /// <c>section += 1; frame = 0</c> (twice).</para>
        ///
        /// <para>Null-safe: returns null on null ROM / null ImageService /
        /// unresolvable record / no non-blank frame.</para>
        /// </summary>
        /// <param name="animeRecordAddr">ROM offset of the 32-byte animation record.</param>
        /// <param name="paletteIndex">Sub-palette (palette-type) index: 0=Player, 1=Enemy, 2=Other, 3=4th.</param>
        /// <returns>A 360x290 composite IImage, or null on failure.</returns>
        public static IImage RenderSampleBattleAnime(uint animeRecordAddr, int paletteIndex)
            => RenderSampleBattleAnime(animeRecordAddr, paletteIndex, 0);

        /// <summary>
        /// Palette-override overload of <see cref="RenderSampleBattleAnime(uint,int)"/>.
        /// When <paramref name="paletteOverrideAddr"/> is non-zero, the 12-cell
        /// grid is rendered with the palette block at THAT address (a GBA pointer
        /// to the LZ77-compressed UNIT palette) instead of the animation record's
        /// own palette at <c>rec+0x1C</c> — the cross-platform mirror of WinForms
        /// <c>ImageBattleAnimeForm.DrawBattleAnime</c>'s
        /// <c>custompalette&gt;0 → palettes = ImageUnitPaletteForm.GetPaletteAddr(custompalette)</c>
        /// override (<c>ImageBattleAnimeForm.cs:285-293</c>). The
        /// <paramref name="paletteIndex"/> sub-palette slice is still applied on
        /// top (the two are independent: the override picks the unit-palette
        /// BLOCK / custompalette slot; <paramref name="paletteIndex"/> picks the
        /// enemy/ally SUB-palette within that block, like WF <c>SwapPalette</c>).
        /// When <paramref name="paletteOverrideAddr"/> is 0 the behaviour is
        /// identical to the existing #822 path (the record's own palette).
        /// </summary>
        /// <param name="animeRecordAddr">ROM offset of the 32-byte animation record.</param>
        /// <param name="paletteIndex">Sub-palette (palette-type) index: 0=Player, 1=Enemy, 2=Other, 3=4th.</param>
        /// <param name="paletteOverrideAddr">GBA pointer to the override palette
        /// block (the unit palette). 0 = use the record's own <c>rec+0x1C</c> palette.</param>
        /// <returns>A 360x290 composite IImage, or null on failure.</returns>
        public static IImage RenderSampleBattleAnime(uint animeRecordAddr, int paletteIndex, uint paletteOverrideAddr)
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;
            if (animeRecordAddr + 32 > (uint)rom.Data.Length) return null;
            if (paletteIndex < 0) paletteIndex = 0;

            // --- Resolve the record's pointers (mirror InitFrameNavigation) ---
            uint sectionRaw = rom.u32(animeRecordAddr + 12);
            uint frameRaw   = rom.u32(animeRecordAddr + 16);
            uint oamRtLRaw  = rom.u32(animeRecordAddr + 20);
            uint paletteRaw = rom.u32(animeRecordAddr + 28);

            // Palette source: the UNIT-palette override (custompalette) when
            // supplied AND it points somewhere safe, else the record's own
            // palette. Mirrors WF: `if (custompalette>0) { p = GetPaletteAddr(...);
            // if (U.isSafetyOffset(addr)) palettes = p; }` — the override only
            // takes effect for a safety-valid record (which the early bound check
            // above already guaranteed) and a safety-valid override block.
            //
            // The override may arrive as either a raw GBA POINTER (0x08...) or an
            // OFFSET (as GetUnitPaletteAddr/p32 returns, < 0x08000000). Normalize
            // to an offset for the safety check, then feed it to
            // ResolveSamplePaletteBlock in POINTER form (which the helper toOffsets
            // again — same convention as the record's own rec+0x1C pointer).
            uint effectivePaletteRaw = paletteRaw;
            if (paletteOverrideAddr != 0)
            {
                uint overrideOffset = U.toOffset(paletteOverrideAddr);
                if (U.isSafetyOffset(overrideOffset, rom))
                {
                    effectivePaletteRaw = U.toPointer(overrideOffset);
                }
            }

            // Section data is raw (not compressed) at the pointer address.
            if (!U.isPointer(sectionRaw)) return null;
            uint sectionOffset = U.toOffset(sectionRaw);
            if (!U.isSafetyOffset(sectionOffset, rom)) return null;

            // Frame command stream (LZ77 or un-Huffman patch pointer).
            byte[] frameData = DecompressFrameData(rom, frameRaw);
            if (frameData == null || frameData.Length == 0) return null;

            // OAM data (LZ77-compressed).
            byte[] oamData = null;
            if (U.isPointer(oamRtLRaw))
            {
                uint oamOff = U.toOffset(oamRtLRaw);
                if (U.isSafetyOffset(oamOff, rom))
                    oamData = LZ77.decompress(rom.Data, oamOff);
            }
            if (oamData == null) return null;

            // Palette (LZ77-compressed) → slice the paletteIndex-th 16-color block.
            // `effectivePaletteRaw` is the unit-palette override when one was
            // supplied, otherwise the record's own palette.
            byte[] paletteSubBytes = ResolveSamplePaletteBlock(rom, effectivePaletteRaw, paletteIndex);
            if (paletteSubBytes == null) return null;

            // --- Collect 12 cropped 90x90 cells (mirror DrawSample) ---
            // The cursor PERSISTS across cells: an advanced section/frame carries
            // forward into the next cell (WF declares these outside the loop).
            //
            // IImage is IDisposable (Skia-backed native bitmaps), so every
            // intermediate cell MUST be disposed: a blank-retry overwrite
            // disposes the previous crop, and after the grid is composed (or on
            // the no-content path) all cells are disposed in `finally`. The
            // full 240x160 frame each cell came from is disposed inside
            // RenderSampleCell right after the crop. Only the RETURNED grid
            // survives (the caller owns it).
            var cells = new IImage[SampleFrameCount];
            try
            {
                int section = 0;
                int frame = 0;
                for (int index = 0; index < SampleFrameCount; index++, frame += 2)
                {
                    SetCell(cells, index, RenderSampleCell(rom, frameData, oamData,
                        paletteSubBytes, sectionOffset, section, frame));
                    if (!IsBlankImage(cells[index], 10))
                    {
                        continue;
                    }
                    // Blank: advance the frame a bit and retry (disposes the
                    // previous blank crop before overwriting).
                    frame += 2;
                    SetCell(cells, index, RenderSampleCell(rom, frameData, oamData,
                        paletteSubBytes, sectionOffset, section, frame));
                    if (!IsBlankImage(cells[index], 10))
                    {
                        continue;
                    }
                    // Still blank: switch to the next section (reset frame).
                    section += 1;
                    frame = 0;
                    SetCell(cells, index, RenderSampleCell(rom, frameData, oamData,
                        paletteSubBytes, sectionOffset, section, frame));
                    if (!IsBlankImage(cells[index], 10))
                    {
                        continue;
                    }
                    // Still blank: advance one more section, then give up for this cell.
                    section += 1;
                    frame = 0;
                    SetCell(cells, index, RenderSampleCell(rom, frameData, oamData,
                        paletteSubBytes, sectionOffset, section, frame));
                }

                // If every cell came back null/blank, there is nothing to
                // preview. The `finally` disposes the (blank) cells.
                bool anyContent = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] != null && !IsBlankImage(cells[i], 10)) { anyContent = true; break; }
                }
                if (!anyContent) return null;

                // --- Compose the cells into the 4-col x 3-row 360x290 grid ---
                byte[] gridPixels = new byte[SampleGridWidth * SampleGridHeight * 4]; // RGBA
                int gx = 0;
                int gy = 0;
                for (int index = 0; index < cells.Length; index++)
                {
                    BlitCellIntoGrid(cells[index], gridPixels, SampleGridWidth, SampleGridHeight, gx, gy);
                    gx += SampleCellSize;
                    if (gx >= SampleGridWidth)
                    {
                        gx = 0;
                        gy += SampleCellSize;
                    }
                }

                var grid = svc.CreateImage(SampleGridWidth, SampleGridHeight);
                grid.SetPixelData(gridPixels);
                return grid; // caller owns the grid — NOT disposed here.
            }
            finally
            {
                // BlitCellIntoGrid copied each cell's pixels into the grid, so
                // the cell intermediates are safe to dispose now (also covers
                // the no-content early return). The returned grid is a separate
                // image created above and is never placed in `cells`.
                for (int i = 0; i < cells.Length; i++)
                {
                    DisposeImage(cells[i]);
                    cells[i] = null;
                }
            }
        }

        /// <summary>
        /// Assign <paramref name="value"/> to <c>cells[index]</c>, disposing any
        /// image already there first (a blank-retry overwrite). Null-safe.
        /// </summary>
        static void SetCell(IImage[] cells, int index, IImage value)
        {
            if (!ReferenceEquals(cells[index], value))
            {
                DisposeImage(cells[index]);
            }
            cells[index] = value;
        }

        /// <summary>Null-safe, exception-swallowing dispose for an IImage.</summary>
        static void DisposeImage(IImage img)
        {
            if (img is IDisposable d)
            {
                try { d.Dispose(); } catch { /* double-dispose / already-disposed: ignore */ }
            }
        }

        /// <summary>
        /// Resolve the UNIT-palette address for unit-palette slot
        /// <paramref name="paletteno"/> (1-based). Cross-platform mirror of
        /// WinForms <c>ImageUnitPaletteForm.GetPaletteAddr(paletteid)</c>
        /// (<c>ImageUnitPaletteForm.cs:115-130</c>):
        /// <c>p32(IDToAddr(paletteno-1) + 12)</c>, where the unit-palette table
        /// base is <c>p32(RomInfo.image_unit_palette_pointer)</c> and each entry
        /// is 16 bytes (<c>IDToAddr(id) = base + id*16</c>). This is resolved via
        /// Core <c>RomInfo</c>, NOT the WinForms-coupled <c>InputFormRef.IDToAddr</c>.
        /// Returns <see cref="U.NOT_FOUND"/> for <paramref name="paletteno"/> &lt;= 0
        /// or an unsafe resolved address (the WF <c>U.isSafetyOffset</c> guard).
        /// </summary>
        /// <param name="rom">The active ROM.</param>
        /// <param name="paletteno">1-based unit-palette slot (WF
        /// <c>AddressList.SelectedIndex + 1</c>).</param>
        /// <returns>The unit-palette block's ROM OFFSET (the read goes through
        /// <c>rom.p32</c>, which applies <c>U.toOffset</c> — callers must NOT
        /// double-convert), or <see cref="U.NOT_FOUND"/>.</returns>
        public static uint GetUnitPaletteAddr(ROM rom, int paletteno)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (paletteno <= 0) return U.NOT_FOUND;

            uint tablePointer = rom.RomInfo.image_unit_palette_pointer;
            if (tablePointer == 0) return U.NOT_FOUND;
            // rom.p32 reads 4 bytes — guard the full span before the read so a
            // table pointer near EOF cannot throw. image_unit_palette_pointer is
            // a fixed RomInfo header-region location (may be < 0x200), so we do
            // NOT apply the isSafetyOffset lower-bound here — only the EOF bound,
            // matching the original direct rom.p32(tablePointer) read.
            if ((ulong)tablePointer + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint baseAddr = rom.p32(tablePointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            // IDToAddr(paletteno - 1): table base + (paletteno-1) * 16-byte stride.
            const uint EntrySize = 16;
            uint entryAddr = baseAddr + (uint)(paletteno - 1) * EntrySize;
            // The +12 pointer slot must itself stay in-bounds + safety-valid.
            uint slot = entryAddr + 12;
            if (!U.isSafetyOffset(slot, rom)) return U.NOT_FOUND;
            if ((ulong)slot + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            return rom.p32(slot);
        }

        /// <summary>
        /// Decompress the anime palette block and slice the
        /// <paramref name="paletteIndex"/>-th 16-color (32-byte) sub-palette.
        /// Falls back to block 0 when the requested block is out of range.
        /// Returns null on null/invalid palette pointer or decompress failure.
        /// </summary>
        static byte[] ResolveSamplePaletteBlock(ROM rom, uint paletteRaw, int paletteIndex)
        {
            if (!U.isPointer(paletteRaw)) return null;
            uint palOff = U.toOffset(paletteRaw);
            if (!U.isSafetyOffset(palOff, rom)) return null;

            byte[] decompressed = LZ77.decompress(rom.Data, palOff);
            if (decompressed == null || decompressed.Length < 32) return null;

            const int blockBytes = 32; // 16 colors * 2 bytes
            int start = paletteIndex * blockBytes;
            if (start < 0 || start + blockBytes > decompressed.Length)
            {
                start = 0; // length guard → fall back to block 0
            }

            byte[] sub = new byte[blockBytes];
            Array.Copy(decompressed, start, sub, 0, blockBytes);
            return sub;
        }

        /// <summary>
        /// Render one sample cell: resolve the (section, frameIndex) frame in the
        /// decompressed frame stream, render it via <see cref="RenderSingleFrame"/>,
        /// then crop to 90x90 at source (100,30) (WF SCALE_90). Returns a blank
        /// 90x90 cell when the frame index does not exist in the section (WF
        /// returns a blank bitmap in that case, which the caller's blank-check
        /// then advances past).
        /// </summary>
        static IImage RenderSampleCell(ROM rom, byte[] frameData, byte[] oamData,
            byte[] paletteSubBytes, uint sectionOffset, int section, int frameIndex)
        {
            // Out-of-range section → blank cell (WF FindFrame would return NOT_FOUND).
            if (section < 0 || section >= SECTION_COUNT)
                return BlankSampleCell();

            GetSectionRange(section, sectionOffset, (uint)frameData.Length, rom,
                out uint start, out uint end);

            List<FrameInfo> frames = ParseFramesInRange(frameData, start, end);
            if (frames == null || frameIndex < 0 || frameIndex >= frames.Count)
                return BlankSampleCell();

            IImage full = RenderSingleFrame(frames[frameIndex], oamData, paletteSubBytes);
            if (full == null)
                return BlankSampleCell();

            // Crop to the 90x90 SCALE_90 window, then dispose the 240x160 full
            // frame intermediate (its pixels were copied into the crop).
            IImage crop = CropImage(full, SampleCropSrcX, SampleCropSrcY,
                SampleCellSize, SampleCellSize);
            DisposeImage(full);
            return crop ?? BlankSampleCell();
        }

        /// <summary>A fresh transparent 90x90 cell (for missing/failed frames).</summary>
        static IImage BlankSampleCell()
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;
            var img = svc.CreateImage(SampleCellSize, SampleCellSize);
            img.SetPixelData(new byte[SampleCellSize * SampleCellSize * 4]);
            return img;
        }

        /// <summary>
        /// Blit a (possibly null) 90x90 cell into the composite grid at
        /// (<paramref name="gx"/>, <paramref name="gy"/>). Null/short cells are
        /// skipped (the grid stays transparent there).
        /// </summary>
        static void BlitCellIntoGrid(IImage cell, byte[] gridPixels, int gridW, int gridH,
            int gx, int gy)
        {
            if (cell == null) return;
            byte[] cellPixels = cell.GetPixelData();
            if (cellPixels == null) return;

            int cw = cell.Width;
            int ch = cell.Height;
            for (int y = 0; y < ch; y++)
            {
                int dy = gy + y;
                if (dy < 0 || dy >= gridH) continue;
                for (int x = 0; x < cw; x++)
                {
                    int dx = gx + x;
                    if (dx < 0 || dx >= gridW) continue;

                    int si = (y * cw + x) * 4;
                    if (si + 3 >= cellPixels.Length) continue;

                    int di = (dy * gridW + dx) * 4;
                    gridPixels[di + 0] = cellPixels[si + 0];
                    gridPixels[di + 1] = cellPixels[si + 1];
                    gridPixels[di + 2] = cellPixels[si + 2];
                    gridPixels[di + 3] = cellPixels[si + 3];
                }
            }
        }

        /// <summary>
        /// GBA OAM affine transform matrix (PA/PB/PC/PD).
        /// Represents the 2x2 fixed-point matrix used for rotation/scaling.
        /// Values are 8.8 fixed-point (256 = 1.0).
        /// Transform: srcX = PA * dx + PB * dy, srcY = PC * dx + PD * dy
        /// where (dx, dy) is relative to the sprite center.
        /// </summary>
        public struct OAMAffineData
        {
            /// <summary>Matrix element [0,0] — X scale / cos component. 8.8 fixed-point.</summary>
            public short PA;
            /// <summary>Matrix element [0,1] — X shear / -sin component. 8.8 fixed-point.</summary>
            public short PB;
            /// <summary>Matrix element [1,0] — Y shear / sin component. 8.8 fixed-point.</summary>
            public short PC;
            /// <summary>Matrix element [1,1] — Y scale / cos component. 8.8 fixed-point.</summary>
            public short PD;
        }

        /// <summary>
        /// Parse affine parameters from an FE custom OAM affine matrix entry.
        /// Affine matrix entries are identified by bytes [2..3] == 0xFFFF.
        /// The PA/PB/PC/PD values are stored at offsets [4..5], [6..7], [8..9], [10..11]
        /// as signed 16-bit 8.8 fixed-point values.
        /// </summary>
        /// <param name="oamData">Raw OAM data bytes.</param>
        /// <param name="entryOffset">Byte offset of the 12-byte OAM entry.</param>
        /// <param name="affine">Parsed affine data output.</param>
        /// <returns>True if this is a valid affine entry and was parsed successfully.</returns>
        public static bool ParseAffineOAM(byte[] oamData, uint entryOffset, out OAMAffineData affine)
        {
            affine = default;
            if (oamData == null || entryOffset + 12 > (uint)oamData.Length)
                return false;

            // Check the affine marker: bytes [2..3] must be 0xFFFF
            if (oamData[entryOffset + 2] != 0xFF || oamData[entryOffset + 3] != 0xFF)
                return false;

            affine.PA = (short)(oamData[entryOffset + 4] | (oamData[entryOffset + 5] << 8));
            affine.PB = (short)(oamData[entryOffset + 6] | (oamData[entryOffset + 7] << 8));
            affine.PC = (short)(oamData[entryOffset + 8] | (oamData[entryOffset + 9] << 8));
            affine.PD = (short)(oamData[entryOffset + 10] | (oamData[entryOffset + 11] << 8));
            return true;
        }

        /// <summary>
        /// Extract palette bank index from OAM attr2 byte.
        /// In 4bpp (16-color) mode, bits 12-15 of attr2 select which 16-color
        /// palette bank to use. In the FE custom format, this is stored in
        /// byte[5] bits 4-7.
        /// </summary>
        /// <param name="oamByte5">The byte at offset [5] of the OAM entry.</param>
        /// <returns>Palette bank index (0-15).</returns>
        public static int ExtractPaletteBank(byte oamByte5)
        {
            return (oamByte5 >> 4) & 0xF;
        }

        /// <summary>
        /// Blit a sprite with affine transformation (rotation/scaling).
        /// For each destination pixel, computes the source coordinate using the inverse
        /// affine matrix (PA/PB/PC/PD are treated as the GBA's texture-to-screen mapping).
        /// </summary>
        /// <param name="src">Source pixel data (RGBA).</param>
        /// <param name="srcStride">Source image width in pixels.</param>
        /// <param name="srcH">Source image height in pixels.</param>
        /// <param name="srcX">Source region X (top-left, pixels).</param>
        /// <param name="srcY">Source region Y (top-left, pixels).</param>
        /// <param name="sprW">Original sprite width in pixels (before doubling).</param>
        /// <param name="sprH">Original sprite height in pixels (before doubling).</param>
        /// <param name="dst">Destination pixel data (RGBA).</param>
        /// <param name="dstStride">Destination image width in pixels.</param>
        /// <param name="dstH">Destination image height in pixels.</param>
        /// <param name="dstX">Destination X position.</param>
        /// <param name="dstY">Destination Y position.</param>
        /// <param name="affine">Affine transform parameters (PA/PB/PC/PD in 8.8 fixed-point).</param>
        /// <param name="sizeDoubled">True if the GBA double-size flag is set (doubles rendering area).</param>
        internal static void BlitSpriteAffine(byte[] src, int srcStride, int srcH,
                                   int srcX, int srcY, int sprW, int sprH,
                                   byte[] dst, int dstStride, int dstH,
                                   int dstX, int dstY,
                                   OAMAffineData affine, bool sizeDoubled)
        {
            // The rendering area: if size-doubled, the output area is 2x the sprite size
            int renderW = sizeDoubled ? sprW * 2 : sprW;
            int renderH = sizeDoubled ? sprH * 2 : sprH;

            // Center of the rendering area (in destination-local coords)
            int halfRenderW = renderW / 2;
            int halfRenderH = renderH / 2;

            // Center of the source sprite region
            int halfSprW = sprW / 2;
            int halfSprH = sprH / 2;

            // GBA affine: for each dest pixel (dx, dy) relative to center,
            // source pixel = (PA * dx + PB * dy, PC * dx + PD * dy) relative to sprite center.
            // PA/PB/PC/PD are 8.8 fixed-point, so we work in fixed-point then shift.

            for (int py = 0; py < renderH; py++)
            {
                // dy relative to render center
                int dy = py - halfRenderH;

                for (int px = 0; px < renderW; px++)
                {
                    // dx relative to render center
                    int dx = px - halfRenderW;

                    // Apply affine transform (8.8 fixed-point math)
                    // Source coords relative to sprite center
                    int texX = (affine.PA * dx + affine.PB * dy + 128) >> 8;
                    int texY = (affine.PC * dx + affine.PD * dy + 128) >> 8;

                    // Convert to absolute source coordinates
                    int sx = srcX + halfSprW + texX;
                    int sy = srcY + halfSprH + texY;

                    // Bounds check on source
                    if (sx < srcX || sx >= srcX + sprW || sy < srcY || sy >= srcY + sprH)
                        continue;
                    if (sx < 0 || sx >= srcStride || sy < 0 || sy >= srcH)
                        continue;

                    int si = (sy * srcStride + sx) * 4;
                    if (si + 3 >= src.Length) continue;
                    if (src[si + 3] == 0) continue; // transparent

                    int destPx = dstX + px;
                    int destPy = dstY + py;
                    if (destPx < 0 || destPx >= dstStride || destPy < 0 || destPy >= dstH)
                        continue;

                    int di = (destPy * dstStride + destPx) * 4;
                    if (di + 3 >= dst.Length) continue;

                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        /// <summary>
        /// Decode source sheet pixels from 4bpp tile data with palette bank support.
        /// Each palette bank shifts the color index lookup by paletteBank * 16 colors
        /// (paletteBank * 32 bytes) into the palette data.
        /// </summary>
        /// <param name="gfxData">4bpp tile graphics data.</param>
        /// <param name="paletteData">Full palette data (may contain multiple 16-color banks).</param>
        /// <param name="paletteBank">Palette bank index (0 = first 16 colors, 1 = next 16, etc.).</param>
        /// <param name="sheetWidth">Sheet width in pixels.</param>
        /// <param name="sheetHeight">Sheet height in pixels.</param>
        /// <param name="svc">Image service for color conversion.</param>
        /// <returns>RGBA pixel buffer, or null on failure.</returns>
        internal static byte[] DecodeSheetWithPaletteBank(byte[] gfxData, byte[] paletteData,
            int paletteBank, int sheetWidth, int sheetHeight, IImageService svc)
        {
            if (gfxData == null || paletteData == null || svc == null)
                return null;

            int sheetWidthTiles = sheetWidth / TILE_SIZE;
            int totalGfxTiles = gfxData.Length / BYTES_PER_TILE_4BPP;
            byte[] pixels = new byte[sheetWidth * sheetHeight * 4];

            int palByteBase = paletteBank * 32; // 16 colors * 2 bytes each

            for (int t = 0; t < totalGfxTiles && t < (sheetWidth / TILE_SIZE) * (sheetHeight / TILE_SIZE); t++)
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

                        int palOff = palByteBase + ci * 2;
                        if (palOff + 2 > paletteData.Length) continue;

                        ushort gbaColor = (ushort)(paletteData[palOff] | (paletteData[palOff + 1] << 8));
                        svc.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte bl);

                        int sx = tileCol * TILE_SIZE + px;
                        int sy = tileRow * TILE_SIZE + py;
                        int si = (sy * sheetWidth + sx) * 4;
                        if (si + 3 < pixels.Length)
                        {
                            pixels[si + 0] = r;
                            pixels[si + 1] = g;
                            pixels[si + 2] = bl;
                            pixels[si + 3] = (byte)(ci == 0 ? 0 : 255);
                        }
                    }
                }
            }

            return pixels;
        }

        // FE battle animation OAM centering offsets (matches WinForms ImageUtilOAM)
        const int BITMAP_ADDX = 0x94;         // 148 — X offset to center sprites on 240px screen
        const int BITMAP_ADDY = 0x58;         // 88  — Y offset to center sprites on 160px screen
        const int BITMAP_SPELL_ADDX = 0xAC;   // 172 — X offset for magic spell OAM

        // Shape constants for the align byte (bits 6-7)
        const byte SHAPE_SQUARE     = 0x00;       // 0 << 6
        const byte SHAPE_HORIZONTAL = 0x40;       // 1 << 6
        const byte SHAPE_VERTICAL   = 0x80;       // 2 << 6

        // Size constants for the area byte (bits 6-7)
        const byte SIZE_TIMES1 = 0x00;            // 0 << 6
        const byte SIZE_TIMES2 = 0x40;            // 1 << 6
        const byte SIZE_TIMES4 = 0x80;            // 2 << 6
        const byte SIZE_TIMES8 = 0xC0;            // 3 << 6

        /// <summary>
        /// Parse FE battle animation OAM entries and blit sprites from the source sheet
        /// onto the destination.
        ///
        /// FE custom OAM format (12 bytes per entry):
        ///   [0]    = terminator flag: 0x00=normal entry, 0x01=end of list
        ///   [1]    = align: bits 6-7=shape (square/horizontal/vertical), bits 0-1=rotation flags
        ///   [2..3] = if 0xFFFF → affine matrix entry (skip for rendering)
        ///   [3]    = area: bits 6-7=size, bit 4=v_flip, bit 5=h_flip, bits 0-4=affine num
        ///   [4]    = sheet tile ref: bits 0-4=tile X, bits 5-7=tile Y
        ///   [5]    = bits 4-7=palette bank (0-3)
        ///   [6..7] = vram_x (signed 16-bit, screen-space X relative to center)
        ///   [8..9] = vram_y (signed 16-bit, screen-space Y relative to center)
        ///   [10..11] = unused
        /// </summary>
        /// <param name="isMagicOAM">True to use the magic spell X offset instead of normal.</param>
        internal static void DrawOAMSprites(byte[] oamData, uint oamStart,
            byte[] srcPixels, int srcWidth, int srcHeight,
            byte[] dstPixels, int dstWidth, int dstHeight,
            bool isMagicOAM = false)
        {
            if (oamData == null || oamStart >= (uint)oamData.Length) return;

            // Collect all OAM entries first, then draw in reverse order (back to front)
            var entries = new List<(int imgX, int imgY, int sheetX, int sheetY,
                                    int w, int h, bool hFlip, bool vFlip,
                                    bool isAffine, OAMAffineData affine, bool sizeDoubled)>();

            // First pass: collect affine matrix data from affine entries
            // In FE custom OAM, affine entries have bytes [2..3] == 0xFFFF
            // and store the matrix at bytes [4..11].
            // We store the last parsed affine matrix to apply to subsequent affine sprites.
            OAMAffineData currentAffine = new OAMAffineData { PA = 256, PB = 0, PC = 0, PD = 256 }; // identity

            for (uint pos = oamStart; ; pos += 12)
            {
                if (pos + 12 > (uint)oamData.Length) break;

                byte firstByte = oamData[pos];

                // FEditor serialized alternate terminator
                if (firstByte == 0 && oamData[pos + 1] == 0xFF
                    && oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF)
                    break;

                // Affine matrix entry: bytes [2..3] == 0xFFFF → parse affine data, not a sprite
                if (oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF)
                {
                    ParseAffineOAM(oamData, pos, out currentAffine);
                    continue;
                }

                // Normal terminator
                if (firstByte == 0x01) break;

                // First byte must be 0x00 for a normal OAM entry
                if (firstByte != 0x00) break;

                byte align = oamData[pos + 1];
                byte area  = oamData[pos + 3];

                // Detect affine sprite: align bit 0 set indicates affine mode
                bool isAffineSprite = (align & 0x01) != 0;
                // Size-doubled: align bit 1 set when affine is enabled
                bool sizeDoubled = isAffineSprite && (align & 0x02) != 0;

                bool vFlip = (area & 0x10) != 0;
                bool hFlip = (area & 0x20) != 0;

                int paletteShift = (oamData[pos + 5] >> 4) & 0xF;
                if (paletteShift >= 4) continue; // bug frame, skip

                // Width/height in tiles from shape+size, then multiply by 8 for pixels
                GetOAMSize(align, area, out int widthTiles, out int heightTiles);
                int sprW = widthTiles * TILE_SIZE;
                int sprH = heightTiles * TILE_SIZE;

                // Sheet source position (tile coordinates → pixel coordinates)
                int sheetTileX = oamData[pos + 4] & 0x1F;
                int sheetTileY = (oamData[pos + 4] >> 5) & 0x07;
                int sheetX = sheetTileX * TILE_SIZE;
                int sheetY = sheetTileY * TILE_SIZE;

                // Screen destination position (signed 16-bit vram coords + centering offset)
                int vramX = (short)(oamData[pos + 6] | (oamData[pos + 7] << 8));
                int vramY = (short)(oamData[pos + 8] | (oamData[pos + 9] << 8));

                int addX = isMagicOAM ? BITMAP_SPELL_ADDX : BITMAP_ADDX;
                int imgX = vramX + addX;
                int imgY = vramY + BITMAP_ADDY;

                if (imgX >= 256) imgX &= 0xFF;

                entries.Add((imgX, imgY, sheetX, sheetY, sprW, sprH, hFlip, vFlip,
                             isAffineSprite, currentAffine, sizeDoubled));
            }

            // Draw in reverse order (first entries are drawn on top in GBA)
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.isAffine)
                {
                    BlitSpriteAffine(srcPixels, srcWidth, srcHeight,
                                     e.sheetX, e.sheetY, e.w, e.h,
                                     dstPixels, dstWidth, dstHeight,
                                     e.imgX, e.imgY, e.affine, e.sizeDoubled);
                }
                else
                {
                    BlitSprite(srcPixels, srcWidth, srcHeight,
                               e.sheetX, e.sheetY, e.w, e.h,
                               dstPixels, dstWidth, dstHeight,
                               e.imgX, e.imgY, e.hFlip, e.vFlip);
                }
            }
        }

        /// <summary>
        /// Get OAM sprite dimensions (in tiles, not pixels) from align and area bytes.
        /// align bits 6-7 = shape (square/horizontal/vertical)
        /// area bits 6-7 = size multiplier (times1/times2/times4/times8)
        /// </summary>
        internal static void GetOAMSize(int align, int area, out int widthTiles, out int heightTiles)
        {
            int shapeBits = align & 0xC0;
            int sizeBits  = area  & 0xC0;

            widthTiles = 0;
            heightTiles = 0;

            if (sizeBits == SIZE_TIMES8)
            {
                if (shapeBits == SHAPE_VERTICAL)        { widthTiles = 4; heightTiles = 8; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 8; heightTiles = 4; }
                else /* SHAPE_SQUARE */                  { widthTiles = 8; heightTiles = 8; }
            }
            else if (sizeBits == SIZE_TIMES4)
            {
                if (shapeBits == SHAPE_VERTICAL)        { widthTiles = 2; heightTiles = 4; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 4; heightTiles = 2; }
                else /* SHAPE_SQUARE */                  { widthTiles = 4; heightTiles = 4; }
            }
            else if (sizeBits == SIZE_TIMES2)
            {
                if (shapeBits == SHAPE_VERTICAL)        { widthTiles = 1; heightTiles = 4; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 4; heightTiles = 1; }
                else /* SHAPE_SQUARE */                  { widthTiles = 2; heightTiles = 2; }
            }
            else /* SIZE_TIMES1 */
            {
                if (shapeBits == SHAPE_VERTICAL)        { widthTiles = 1; heightTiles = 2; }
                else if (shapeBits == SHAPE_HORIZONTAL)  { widthTiles = 2; heightTiles = 1; }
                else /* SHAPE_SQUARE */                  { widthTiles = 1; heightTiles = 1; }
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
