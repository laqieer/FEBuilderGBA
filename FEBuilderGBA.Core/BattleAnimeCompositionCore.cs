using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// High-level battle animation frame composition and OAM linting.
    /// Builds on BattleAnimeRendererCore for low-level OAM parsing/rendering.
    /// </summary>
    public static class BattleAnimeCompositionCore
    {
        const int TILE_SIZE = 8;
        const int BYTES_PER_TILE_4BPP = 32;
        const int GBA_SCREEN_WIDTH = 240;
        const int GBA_SCREEN_HEIGHT = 160;

        // Section modes
        const int MODE_BODY = 0;
        const int MODE_WEAPON = 1;
        const int MODE_CRIT_BODY = 2;
        const int MODE_CRIT_WEAPON = 3;

        // OAM record size in the FE custom format
        const int OAM_ENTRY_SIZE = 12;

        // Max tile index for 4bpp 256x64 sheet = 32*8 = 256 tiles
        const int MAX_TILE_INDEX = 256;

        // Max palette bank index (0-3 for battle animations)
        const int MAX_PALETTE_INDEX = 3;

        /// <summary>
        /// Describes a section within a battle animation (12-byte record in ROM).
        /// </summary>
        public struct SectionRecord
        {
            /// <summary>Pointer to the frame list for this section (ROM pointer).</summary>
            public uint FrameListPointer;
            /// <summary>Number of frames in this section.</summary>
            public int FrameCount;
            /// <summary>Section mode: 0=body, 1=weapon, 2=crit body, 3=crit weapon.</summary>
            public int Mode;
        }

        /// <summary>
        /// Result of rendering a battle animation frame.
        /// </summary>
        public struct FrameRenderResult
        {
            /// <summary>The rendered image, or null on failure.</summary>
            public IImage Image;
            /// <summary>Error message if rendering failed.</summary>
            public string Error;
        }

        /// <summary>
        /// Render a single battle animation frame by section and frame index.
        /// Reads the section table to find the right section, then reads the frame table
        /// to find the right frame within that section. Decompresses tiles, loads palette,
        /// and calls BattleAnimeRendererCore to render OAM sprites.
        /// </summary>
        /// <param name="rom">ROM to read from.</param>
        /// <param name="sectionDataAddr">ROM address of the 12-entry section offset table (uint32 array).</param>
        /// <param name="frameDataAddr">ROM pointer to compressed frame data.</param>
        /// <param name="paletteAddr">ROM address of the 32-byte GBA palette.</param>
        /// <param name="oamDataAddr">ROM pointer to compressed OAM data.</param>
        /// <param name="sectionIndex">Section index (0-11).</param>
        /// <param name="frameIndex">Frame index within the section.</param>
        /// <returns>A FrameRenderResult with the rendered image or an error.</returns>
        public static FrameRenderResult RenderBattleAnimeFrame(
            ROM rom,
            uint sectionDataAddr,
            uint frameDataAddr,
            uint paletteAddr,
            uint oamDataAddr,
            int sectionIndex,
            int frameIndex)
        {
            if (rom == null)
                return new FrameRenderResult { Error = "ROM is null" };

            IImageService svc = CoreState.ImageService;
            if (svc == null)
                return new FrameRenderResult { Error = "ImageService is null" };

            // Decompress frame data
            byte[] frameData = BattleAnimeRendererCore.DecompressFrameData(rom, frameDataAddr);
            if (frameData == null || frameData.Length == 0)
                return new FrameRenderResult { Error = "Failed to decompress frame data" };

            // Get section range
            BattleAnimeRendererCore.GetSectionRange(sectionIndex, sectionDataAddr,
                (uint)frameData.Length, rom, out uint start, out uint end);

            // Parse frames in this section
            var frames = BattleAnimeRendererCore.ParseFramesInRange(frameData, start, end);
            if (frames.Count == 0)
                return new FrameRenderResult { Error = $"No frames found in section {sectionIndex}" };

            if (frameIndex < 0 || frameIndex >= frames.Count)
                return new FrameRenderResult { Error = $"Frame index {frameIndex} out of range (0-{frames.Count - 1})" };

            var frame = frames[frameIndex];

            // Read palette
            byte[] paletteData = null;
            if (U.isSafetyOffset(paletteAddr, rom))
            {
                paletteData = ImageUtilCore.GetPalette(paletteAddr, 16);
            }
            if (paletteData == null || paletteData.Length < 2)
                return new FrameRenderResult { Error = "Failed to read palette data" };

            // Decompress OAM data
            byte[] oamData = null;
            if (U.isPointer(oamDataAddr))
            {
                uint oamOff = U.toOffset(oamDataAddr);
                if (U.isSafetyOffset(oamOff, rom))
                    oamData = LZ77.decompress(rom.Data, oamOff);
            }
            if (oamData == null || oamData.Length == 0)
                return new FrameRenderResult { Error = "Failed to decompress OAM data" };

            // Use the existing single-frame renderer
            var image = BattleAnimeRendererCore.RenderSingleFrame(frame, oamData, paletteData);
            if (image == null)
                return new FrameRenderResult { Error = "RenderSingleFrame returned null" };

            return new FrameRenderResult { Image = image };
        }

        /// <summary>
        /// Compose a body layer and weapon layer by overlaying the weapon on top of the body.
        /// Uses alpha-aware compositing: weapon pixels with alpha > 0 overwrite body pixels.
        /// Both images must be the same dimensions (typically 240x160).
        /// </summary>
        /// <param name="body">Body layer image (RGBA pixel data).</param>
        /// <param name="weapon">Weapon overlay image (RGBA pixel data).</param>
        /// <returns>A new IImage with the composed result, or null on failure.</returns>
        public static IImage ComposeLayers(IImage body, IImage weapon)
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null || body == null || weapon == null)
                return null;

            int width = body.Width;
            int height = body.Height;

            if (weapon.Width != width || weapon.Height != height)
                return null;

            byte[] bodyPixels = body.GetPixelData();
            byte[] weaponPixels = weapon.GetPixelData();

            if (bodyPixels == null || weaponPixels == null)
                return null;

            int expectedLen = width * height * 4;
            if (bodyPixels.Length < expectedLen || weaponPixels.Length < expectedLen)
                return null;

            // Copy body pixels, then overlay weapon where weapon alpha > 0
            byte[] result = new byte[expectedLen];
            Array.Copy(bodyPixels, result, expectedLen);

            for (int i = 0; i < expectedLen; i += 4)
            {
                byte weaponAlpha = weaponPixels[i + 3];
                if (weaponAlpha > 0)
                {
                    result[i + 0] = weaponPixels[i + 0]; // R
                    result[i + 1] = weaponPixels[i + 1]; // G
                    result[i + 2] = weaponPixels[i + 2]; // B
                    result[i + 3] = weaponAlpha;          // A
                }
            }

            var image = svc.CreateImage(width, height);
            image.SetPixelData(result);
            return image;
        }

        /// <summary>
        /// Compose body and weapon layers from raw RGBA pixel arrays.
        /// Returns a new pixel array with the composed result.
        /// </summary>
        /// <param name="bodyPixels">Body RGBA pixel data.</param>
        /// <param name="weaponPixels">Weapon RGBA pixel data.</param>
        /// <param name="pixelCount">Total number of pixels (width * height).</param>
        /// <returns>Composed RGBA pixel data, or null on failure.</returns>
        public static byte[] ComposeLayerPixels(byte[] bodyPixels, byte[] weaponPixels, int pixelCount)
        {
            if (bodyPixels == null || weaponPixels == null || pixelCount <= 0)
                return null;

            int expectedLen = pixelCount * 4;
            if (bodyPixels.Length < expectedLen || weaponPixels.Length < expectedLen)
                return null;

            byte[] result = new byte[expectedLen];
            Array.Copy(bodyPixels, result, expectedLen);

            for (int i = 0; i < expectedLen; i += 4)
            {
                byte weaponAlpha = weaponPixels[i + 3];
                if (weaponAlpha > 0)
                {
                    result[i + 0] = weaponPixels[i + 0];
                    result[i + 1] = weaponPixels[i + 1];
                    result[i + 2] = weaponPixels[i + 2];
                    result[i + 3] = weaponAlpha;
                }
            }

            return result;
        }

        /// <summary>
        /// Validate OAM data for common errors. Checks each OAM entry for:
        /// - Valid shape/size combinations
        /// - Tile index within range
        /// - Palette index within range (0-3 for battle animations)
        /// - No overlapping sprites (bounding box check)
        /// </summary>
        /// <param name="oamData">The OAM data bytes.</param>
        /// <param name="oamStart">Starting offset within oamData.</param>
        /// <param name="oamLength">Maximum number of bytes to scan (0 = scan to end/terminator).</param>
        /// <returns>List of error/warning messages. Empty list means no issues found.</returns>
        public static List<string> LintOAM(byte[] oamData, int oamStart, int oamLength)
        {
            var errors = new List<string>();

            if (oamData == null)
            {
                errors.Add("OAM data is null");
                return errors;
            }

            if (oamStart < 0 || oamStart >= oamData.Length)
            {
                errors.Add($"OAM start offset {oamStart} is out of range (data length: {oamData.Length})");
                return errors;
            }

            int scanEnd = oamLength > 0
                ? Math.Min(oamStart + oamLength, oamData.Length)
                : oamData.Length;

            // Collect sprite bounding boxes for overlap check
            var boundingBoxes = new List<(int x, int y, int w, int h, int entryIndex)>();
            int entryNum = 0;

            for (int pos = oamStart; pos + OAM_ENTRY_SIZE <= scanEnd; pos += OAM_ENTRY_SIZE)
            {
                byte firstByte = oamData[pos];

                // Check for terminators
                if (firstByte == 0 && pos + 3 < oamData.Length
                    && oamData[pos + 1] == 0xFF && oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF)
                    break;

                if (firstByte == 0x01) break;

                // Affine matrix entry (skip)
                if (pos + 3 < oamData.Length && oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF)
                {
                    entryNum++;
                    continue;
                }

                if (firstByte != 0x00)
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: unexpected first byte 0x{firstByte:X2} (expected 0x00)");
                    break;
                }

                byte align = oamData[pos + 1];
                byte area = oamData[pos + 3];

                // Validate shape/size combination
                int shapeBits = align & 0xC0;
                int sizeBits = area & 0xC0;

                if (!IsValidShapeSizeCombo(shapeBits, sizeBits))
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: invalid shape/size combination (shape=0x{shapeBits:X2}, size=0x{sizeBits:X2})");
                }

                // Get sprite dimensions
                BattleAnimeRendererCore.GetOAMSize(align, area, out int widthTiles, out int heightTiles);

                if (widthTiles == 0 || heightTiles == 0)
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: zero-dimension sprite (w={widthTiles}, h={heightTiles} tiles)");
                    entryNum++;
                    continue;
                }

                // Validate tile index
                int sheetTileX = oamData[pos + 4] & 0x1F;
                int sheetTileY = (oamData[pos + 4] >> 5) & 0x07;
                int tileIndex = sheetTileY * 32 + sheetTileX;

                // Check that the sprite's tile range fits within the sheet
                int endTileX = sheetTileX + widthTiles;
                int endTileY = sheetTileY + heightTiles;

                if (endTileX > 32)
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: tile X range exceeds sheet width (tileX={sheetTileX}, widthTiles={widthTiles}, max=32)");
                }
                if (endTileY > 8)
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: tile Y range exceeds sheet height (tileY={sheetTileY}, heightTiles={heightTiles}, max=8)");
                }

                // Validate palette index
                int paletteBank = (oamData[pos + 5] >> 4) & 0xF;
                if (paletteBank > MAX_PALETTE_INDEX)
                {
                    errors.Add($"Entry {entryNum} at offset {pos}: palette bank {paletteBank} exceeds max ({MAX_PALETTE_INDEX})");
                }

                // Collect bounding box for overlap detection
                if (pos + 9 < oamData.Length)
                {
                    int vramX = (short)(oamData[pos + 6] | (oamData[pos + 7] << 8));
                    int vramY = (short)(oamData[pos + 8] | (oamData[pos + 9] << 8));
                    int sprW = widthTiles * TILE_SIZE;
                    int sprH = heightTiles * TILE_SIZE;
                    boundingBoxes.Add((vramX, vramY, sprW, sprH, entryNum));
                }

                entryNum++;
            }

            // Check for overlapping sprites
            for (int i = 0; i < boundingBoxes.Count; i++)
            {
                for (int j = i + 1; j < boundingBoxes.Count; j++)
                {
                    var a = boundingBoxes[i];
                    var b = boundingBoxes[j];

                    if (RectsOverlap(a.x, a.y, a.w, a.h, b.x, b.y, b.w, b.h))
                    {
                        errors.Add($"Entries {a.entryIndex} and {b.entryIndex} have overlapping bounding boxes");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Check if a shape/size bit combination is valid for GBA OAM.
        /// All 12 combinations (3 shapes x 4 sizes) are valid.
        /// Shape 0xC0 (3 << 6) is reserved/invalid.
        /// </summary>
        static bool IsValidShapeSizeCombo(int shapeBits, int sizeBits)
        {
            // Shape must be 0x00, 0x40, or 0x80 (not 0xC0)
            if (shapeBits != 0x00 && shapeBits != 0x40 && shapeBits != 0x80)
                return false;

            // Size must be 0x00, 0x40, 0x80, or 0xC0
            if (sizeBits != 0x00 && sizeBits != 0x40 && sizeBits != 0x80 && sizeBits != 0xC0)
                return false;

            return true;
        }

        /// <summary>
        /// Check if two axis-aligned rectangles overlap.
        /// </summary>
        static bool RectsOverlap(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2)
        {
            if (w1 <= 0 || h1 <= 0 || w2 <= 0 || h2 <= 0)
                return false;

            return x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2;
        }

        /// <summary>
        /// Parse section records from ROM. Section offsets are stored as a 12-entry uint32 array
        /// where each entry is a byte offset into the decompressed frame data.
        /// </summary>
        /// <param name="rom">ROM to read from.</param>
        /// <param name="sectionDataAddr">Address of the 12-entry section offset table.</param>
        /// <param name="frameData">Decompressed frame data (used to count frames per section).</param>
        /// <returns>Array of 12 section records.</returns>
        public static SectionRecord[] ParseSectionTable(ROM rom, uint sectionDataAddr, byte[] frameData)
        {
            const int SECTION_COUNT = 12;
            var sections = new SectionRecord[SECTION_COUNT];

            if (rom == null || frameData == null)
                return sections;

            uint frameDataLen = (uint)frameData.Length;

            for (int i = 0; i < SECTION_COUNT; i++)
            {
                BattleAnimeRendererCore.GetSectionRange(i, sectionDataAddr,
                    frameDataLen, rom, out uint start, out uint end);

                int frameCount = BattleAnimeRendererCore.CountFramesInRange(frameData, start, end);

                sections[i] = new SectionRecord
                {
                    FrameListPointer = start,
                    FrameCount = frameCount,
                    Mode = GetSectionMode(i),
                };
            }

            return sections;
        }

        /// <summary>
        /// Map section index to its mode.
        /// </summary>
        static int GetSectionMode(int sectionIndex)
        {
            // Sections: 0=attack body, 1=attack weapon, 2=crit body, 3=crit weapon,
            // 4=ranged, 5=ranged crit, 6=dodge melee, 7=dodge ranged,
            // 8=standing melee, 9=standing idle, 10=standing ranged, 11=miss
            switch (sectionIndex)
            {
                case 0: return MODE_BODY;
                case 1: return MODE_WEAPON;
                case 2: return MODE_CRIT_BODY;
                case 3: return MODE_CRIT_WEAPON;
                default: return MODE_BODY; // Sections 4+ are standalone body-type
            }
        }

        /// <summary>
        /// Determine if a section is a weapon overlay section.
        /// </summary>
        public static bool IsWeaponOverlay(int sectionIndex)
        {
            return sectionIndex == 1 || sectionIndex == 3;
        }

        /// <summary>
        /// Get the matching body section index for a weapon overlay section.
        /// Returns -1 if the section is not a weapon overlay.
        /// </summary>
        public static int GetBodySectionForWeapon(int weaponSectionIndex)
        {
            switch (weaponSectionIndex)
            {
                case 1: return 0;  // Attack weapon -> Attack body
                case 3: return 2;  // Crit weapon -> Crit body
                default: return -1;
            }
        }
    }
}
