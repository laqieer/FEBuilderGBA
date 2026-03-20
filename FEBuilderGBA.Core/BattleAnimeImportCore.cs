using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform battle animation import from .txt script + PNG sprite frames.
    /// Ported from WinForms ImageUtilOAM.ImportBattleAnime() to work with byte[] RGBA pixels.
    /// </summary>
    public static class BattleAnimeImportCore
    {
        // Sprite sheet dimensions (in 8×8 tiles)
        const int SEAT_TILE_WIDTH = 32;   // 256 pixels
        const int SEAT_TILE_HEIGHT = 8;   // 64 pixels
        const int SCREEN_TILE_WIDTH = 31; // 248 pixels

        // Sprite position offsets
        const int BITMAP_ADDX = 0x94;  // 148
        const int BITMAP_ADDY = 0x58;  // 88

        // OAM shape bytes
        const byte SQUARE = 0x00;
        const byte HORIZONTAL = 0x40;
        const byte VERTICAL = 0x80;

        // OAM area/size bytes
        const byte TIMES1 = 0x00;
        const byte TIMES2 = 0x40;
        const byte TIMES4 = 0x80;
        const byte TIMES8 = 0xC0;

        // Number of animation sections
        const int SECTION_COUNT = 0xC;

        /// <summary>Resolve battle animation record address from 0-based ID.</summary>
        public static uint ResolveBattleAnimeAddr(ROM rom, uint animId)
        {
            var (tableBase, tableEnd) = GetTableBounds(rom);
            if (tableBase == 0) return U.NOT_FOUND;
            uint entryCount = (tableEnd - tableBase) / 32;
            if (animId >= entryCount) return U.NOT_FOUND;
            return tableBase + (animId * 32);
        }

        /// <summary>Get table base and end addresses for recycling safety.</summary>
        public static (uint baseAddr, uint endAddr) GetTableBounds(ROM rom)
        {
            if (rom?.RomInfo == null) return (0, 0);
            uint ptr = rom.RomInfo.image_battle_animelist_pointer;
            if (ptr == 0) return (0, 0);
            uint tableBase = rom.p32(U.toOffset(ptr));
            if (tableBase == 0) return (0, 0);

            // Count valid entries
            uint count = 0;
            for (uint i = 0; i < 512; i++) // safety limit
            {
                uint addr = tableBase + (i * 32);
                if (!U.isSafetyOffset(addr + 31, rom)) break;
                uint p12 = rom.u32(addr + 12);
                uint p20 = rom.u32(addr + 20);
                uint p24 = rom.u32(addr + 24);
                if (!U.isPointer(p12) || !U.isPointer(p20) || !U.isPointer(p24))
                    break;
                count++;
            }
            return (tableBase, tableBase + (count * 32));
        }

        /// <summary>
        /// Import a battle animation from a .txt script file.
        /// </summary>
        /// <param name="scriptPath">Path to the .txt animation script.</param>
        /// <param name="animRecordAddr">ROM address of the 32-byte animation record.</param>
        /// <param name="imageLoader">Callback: filename → (rgba, width, height) or null if not found.</param>
        /// <returns>Error message (empty = success).</returns>
        public static string ImportBattleAnime(
            string scriptPath,
            uint animRecordAddr,
            Func<string, (byte[] rgba, int w, int h)?> imageLoader)
        {
            if (!File.Exists(scriptPath))
                return $"Script file not found: {scriptPath}";

            ROM rom = CoreState.ROM;
            if (rom == null) return "No ROM loaded.";

            string baseDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
            string[] lines = File.ReadAllLines(scriptPath);

            // Parse script and build animation data
            var result = ParseAndBuild(lines, baseDir, imageLoader);
            if (result.Error != null) return result.Error;

            // Write to ROM
            return WriteToRom(rom, animRecordAddr, result);
        }

        #region Script Parsing

        class BuildResult
        {
            public string Error;
            public byte[] SectionData;
            public byte[] FrameData;
            public byte[] OamData;       // Right-to-left OAM (LZ77 compressed)
            public byte[] PaletteData;   // 4-team palette (LZ77 compressed)
            public List<byte[]> SheetImages;  // Tile sheet data (4bpp, LZ77 compressed each)
        }

        class AnimeData
        {
            public int SheetIndex;   // Which sheet this frame uses
            public uint OamPos;      // OAM offset for this frame
            public uint ImageNumber; // Frame reference index
        }

        static BuildResult ParseAndBuild(string[] lines, string baseDir,
            Func<string, (byte[] rgba, int w, int h)?> imageLoader)
        {
            byte[] sectionData = new byte[SECTION_COUNT * 4];
            var frameData = new List<byte>();
            var frameDataMode2 = new List<byte>();
            var oamData = new List<byte>();
            var sheets = new List<byte[]>(); // Compressed tile sheet data
            var animeDic = new Dictionary<string, AnimeData>();

            // Tile sheet state
            byte[] seatPixels = new byte[SEAT_TILE_WIDTH * 8 * SEAT_TILE_HEIGHT * 8]; // indexed
            bool[] seatUsed = new bool[SEAT_TILE_WIDTH * SEAT_TILE_HEIGHT];
            byte[] sharedPalette = null; // GBA 555 format, 32 bytes
            byte[] sharedRgbaPalette = null; // RGBA format for remapping
            bool isMultiPalette = false;
            int seatSheetCount = 0;

            int mode = 0;
            bool isMode1 = true; // Attack body (mode 0) generates both body + weapon overlay
            uint countLoopFrame = U.NOT_FOUND;
            uint imageNumber = 0;

            // Auto-patch: ensure sections end properly
            // (headless: skip C26/C27/C47 dialogs — these are non-critical warnings)

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;

                // Strip inline comments
                int commentPos = line.IndexOf("//");
                if (commentPos >= 0) line = line.Substring(0, commentPos).Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Section terminator
                if (line[0] == '~')
                {
                    AppendU32(frameData, 0x80000000);
                    if (isMode1) AppendU32(frameDataMode2, 0x80000000);

                    if (countLoopFrame != U.NOT_FOUND)
                        return new BuildResult { Error = $"Loop not terminated with C01 before section end (line {i + 1})" };

                    mode++;
                    if (mode >= SECTION_COUNT) break;
                    WriteU32(sectionData, (uint)(mode * 4), (uint)frameData.Count);

                    if (isMode1)
                    {
                        frameData.AddRange(frameDataMode2);
                        frameDataMode2.Clear();
                        mode++;
                        WriteU32(sectionData, (uint)(mode * 4), (uint)frameData.Count);
                    }

                    isMode1 = (mode == 0x3 - 1); // Mode 1 = critical body/weapon
                    continue;
                }

                // 85 command
                if (line[0] == 'C' || line[0] == 'c')
                {
                    uint command = ParseHex(line.Substring(1));

                    if (command == 0x01 && countLoopFrame != U.NOT_FOUND)
                    {
                        if (countLoopFrame > 0xFF)
                            return new BuildResult { Error = $"Loop too long (max 0xFF) at line {i + 1}" };
                        if (countLoopFrame < 0x3)
                            return new BuildResult { Error = $"Loop too short (min 3 frames) at line {i + 1}" };
                        command = (countLoopFrame << 8) | 0x01;
                        countLoopFrame = U.NOT_FOUND;
                    }

                    uint a = (command & 0x00FFFFFF) | 0x85000000;
                    AppendU32(frameData, a);
                    if (isMode1) AppendU32(frameDataMode2, a);

                    if (command == 0x0D)
                    {
                        AppendU32(frameData, 0x80000000);
                        if (isMode1) AppendU32(frameDataMode2, 0x80000000);
                    }
                    continue;
                }

                // Loop start
                if (line[0] == 'L' || line[0] == 'l')
                {
                    if (countLoopFrame != U.NOT_FOUND)
                        return new BuildResult { Error = $"Nested loop at line {i + 1}" };
                    countLoopFrame = 0;
                    continue;
                }

                // Sound command
                if (line[0] == 'S' || line[0] == 's')
                {
                    uint music = ParseHex(line.Substring(1));
                    uint a = ((music & 0xFFFF) << 8) | 0x85000048;
                    AppendU32(frameData, a);
                    if (isMode1) AppendU32(frameDataMode2, a);
                    continue;
                }

                // 86 frame command: Np-filename.png
                int pIdx = line.IndexOf("p-");
                if (pIdx > 0 && char.IsDigit(line[0]))
                {
                    uint frameSec = ParseDecimal(line);
                    if (countLoopFrame != U.NOT_FOUND)
                        countLoopFrame += 3;

                    string imageFilename = line.Substring(pIdx + 2).Trim();
                    if (string.IsNullOrEmpty(imageFilename))
                        return new BuildResult { Error = $"Missing image filename at line {i + 1}" };

                    AnimeData anime;
                    if (animeDic.TryGetValue(imageFilename, out anime))
                    {
                        // Reuse cached frame
                    }
                    else
                    {
                        // Load and process new image
                        string fullPath = Path.Combine(baseDir, imageFilename);
                        var loaded = imageLoader(fullPath);
                        if (loaded == null)
                            return new BuildResult { Error = $"Image not found: {imageFilename} (line {i + 1})" };

                        var (rgba, w, h) = loaded.Value;

                        // Validate image dimensions
                        if (w % 8 != 0 || h % 8 != 0)
                            return new BuildResult { Error = $"Image dimensions must be multiples of 8: {imageFilename} ({w}x{h})" };
                        if (w > SCREEN_TILE_WIDTH * 8 || h > 160)
                            return new BuildResult { Error = $"Image too large (max {SCREEN_TILE_WIDTH * 8}x160): {imageFilename} ({w}x{h})" };

                        // Quantize to 16 colors
                        var qr = DecreaseColorCore.Quantize(rgba, w, h, 16);
                        if (qr == null)
                            return new BuildResult { Error = $"Failed to quantize image: {imageFilename}" };

                        if (sharedPalette == null)
                        {
                            sharedPalette = qr.GBAPalette;
                            sharedRgbaPalette = qr.RGBAPalette;
                            isMultiPalette = (qr.ColorCount > 16);
                        }
                        else
                        {
                            // Remap this frame's pixels to the shared palette
                            RemapToSharedPalette(qr.IndexData, qr.RGBAPalette, sharedRgbaPalette, w, h);
                        }

                        // Build OAM for this frame
                        uint oamPos = (uint)oamData.Count;
                        bool ok = PackTiles(qr.IndexData, w, h,
                            seatPixels, seatUsed, oamData, 0);

                        if (!ok)
                        {
                            // Sheet full — finalize current sheet and start new
                            byte[] sheetTiles = EncodeSeatTo4bpp(seatPixels,
                                SEAT_TILE_WIDTH * 8, SEAT_TILE_HEIGHT * 8);
                            sheets.Add(LZ77.compress(sheetTiles));
                            seatSheetCount++;

                            // Reset sheet
                            Array.Clear(seatPixels, 0, seatPixels.Length);
                            Array.Clear(seatUsed, 0, seatUsed.Length);

                            oamPos = (uint)oamData.Count;
                            ok = PackTiles(qr.IndexData, w, h,
                                seatPixels, seatUsed, oamData, 0);
                            if (!ok)
                                return new BuildResult { Error = $"Image too large for sprite sheet: {imageFilename}" };
                        }

                        AppendTermOAM(oamData);

                        anime = new AnimeData
                        {
                            SheetIndex = seatSheetCount,
                            OamPos = oamPos,
                            ImageNumber = imageNumber++
                        };
                        animeDic[imageFilename] = anime;
                    }

                    uint cmd = (frameSec & 0xFFFF)
                        | ((anime.ImageNumber & 0xFF) << 16)
                        | 0x86000000;
                    AppendU32(frameData, cmd);
                    AppendU32(frameData, (uint)anime.SheetIndex); // placeholder for sheet addr
                    AppendU32(frameData, anime.OamPos);

                    if (isMode1)
                    {
                        AppendU32(frameDataMode2, cmd);
                        AppendU32(frameDataMode2, (uint)anime.SheetIndex);
                        AppendU32(frameDataMode2, anime.OamPos);
                    }
                    continue;
                }
            }

            if (mode < SECTION_COUNT)
            {
                return new BuildResult { Error = $"Script has only {mode} sections, but battle animations require all {SECTION_COUNT} sections (use ~ to separate)." };
            }

            // Finalize last sheet
            if (seatUsed != null)
            {
                bool hasData = false;
                foreach (bool b in seatUsed) { if (b) { hasData = true; break; } }
                if (hasData)
                {
                    byte[] sheetTiles = EncodeSeatTo4bpp(seatPixels,
                        SEAT_TILE_WIDTH * 8, SEAT_TILE_HEIGHT * 8);
                    sheets.Add(LZ77.compress(sheetTiles));
                }
            }

            // Build 4-team palette
            byte[] palData;
            if (sharedPalette != null)
            {
                // Simple: player palette only (no recolor map in CLI)
                byte[] fourPal = new byte[16 * 2 * 4]; // 4 teams × 16 colors × 2 bytes
                for (int p = 0; p < 4; p++)
                    Array.Copy(sharedPalette, 0, fourPal, p * 32,
                        Math.Min(sharedPalette.Length, 32));
                palData = LZ77.compress(fourPal);
            }
            else
            {
                palData = LZ77.compress(new byte[128]);
            }

            // Compress OAM
            byte[] oamCompressed = LZ77.compress(oamData.ToArray());

            return new BuildResult
            {
                SectionData = sectionData,
                FrameData = frameData.ToArray(),
                OamData = oamCompressed,
                PaletteData = palData,
                SheetImages = sheets
            };
        }

        #endregion

        #region Tile Packing

        static bool PackTiles(byte[] indexedPixels, int width, int height,
            byte[] seatPixels, bool[] seatUsed, List<byte> oam, int oamPalette)
        {
            int tilesW = width / 8;
            int tilesH = height / 8;
            bool[] useTileData = new bool[tilesW * tilesH];

            // Mark tiles that have non-zero pixels (transparent = 0)
            for (int ty = 0; ty < tilesH; ty++)
            {
                for (int tx = 0; tx < tilesW; tx++)
                {
                    bool empty = true;
                    for (int py = 0; py < 8 && empty; py++)
                    {
                        for (int px = 0; px < 8 && empty; px++)
                        {
                            int idx = (ty * 8 + py) * width + (tx * 8 + px);
                            if (idx < indexedPixels.Length && indexedPixels[idx] != 0)
                                empty = false;
                        }
                    }
                    useTileData[ty * tilesW + tx] = empty; // true = skip (empty)
                }
            }

            for (int i = 0; i < useTileData.Length; i++)
            {
                if (useTileData[i]) continue;

                int tileX = i % tilesW;
                int tileY = i / tilesW;
                int vramX = tileX * 8 - BITMAP_ADDX;
                int vramY = tileY * 8 - BITMAP_ADDY;

                int seatX, seatY;

                // Tile size cascade: 8×8 → 8×4 → 4×8 → 4×4 → ... → 1×1
                if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 8, 8, out seatX, out seatY))
                    AppendOAM(SQUARE, TIMES8, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 8, 4, out seatX, out seatY))
                    AppendOAM(HORIZONTAL, TIMES8, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 4, 8, out seatX, out seatY))
                    AppendOAM(VERTICAL, TIMES8, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 4, 4, out seatX, out seatY))
                    AppendOAM(SQUARE, TIMES4, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 4, 2, out seatX, out seatY))
                    AppendOAM(HORIZONTAL, TIMES4, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 2, 4, out seatX, out seatY))
                    AppendOAM(VERTICAL, TIMES4, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 2, 2, out seatX, out seatY))
                    AppendOAM(SQUARE, TIMES2, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 4, 1, out seatX, out seatY))
                    AppendOAM(HORIZONTAL, TIMES2, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 1, 4, out seatX, out seatY))
                    AppendOAM(VERTICAL, TIMES2, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 2, 1, out seatX, out seatY))
                    AppendOAM(HORIZONTAL, TIMES1, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 1, 2, out seatX, out seatY))
                    AppendOAM(VERTICAL, TIMES1, oam, seatX, seatY, vramX, vramY, oamPalette);
                else if (TryCopy(indexedPixels, width, useTileData, tilesW,
                    seatPixels, seatUsed, i, 1, 1, out seatX, out seatY))
                    AppendOAM(SQUARE, TIMES1, oam, seatX, seatY, vramX, vramY, oamPalette);
                else
                    return false; // Sheet full
            }
            return true;
        }

        static bool TryCopy(byte[] srcPixels, int srcWidth,
            bool[] useTileData, int tilesW,
            byte[] seatPixels, bool[] seatUsed,
            int tileIdx, int tw, int th,
            out int seatX, out int seatY)
        {
            seatX = 0;
            seatY = 0;

            int srcTileX = tileIdx % tilesW;
            int srcTileY = tileIdx / tilesW;
            int tilesH = useTileData.Length / tilesW;

            // Check if block fits in source
            if (srcTileX + tw > tilesW || srcTileY + th > tilesH)
                return false;

            // Check all tiles in block are non-empty
            for (int dy = 0; dy < th; dy++)
                for (int dx = 0; dx < tw; dx++)
                    if (useTileData[(srcTileY + dy) * tilesW + (srcTileX + dx)])
                        return false;

            // Find empty space in seat
            for (int sy = 0; sy <= SEAT_TILE_HEIGHT - th; sy++)
            {
                for (int sx = 0; sx <= SEAT_TILE_WIDTH - tw; sx++)
                {
                    bool fits = true;
                    for (int dy = 0; dy < th && fits; dy++)
                        for (int dx = 0; dx < tw && fits; dx++)
                            if (seatUsed[(sy + dy) * SEAT_TILE_WIDTH + (sx + dx)])
                                fits = false;

                    if (fits)
                    {
                        // Copy tile data to seat
                        for (int dy = 0; dy < th; dy++)
                        {
                            for (int dx = 0; dx < tw; dx++)
                            {
                                CopyTile(srcPixels, srcWidth,
                                    (srcTileX + dx) * 8, (srcTileY + dy) * 8,
                                    seatPixels, SEAT_TILE_WIDTH * 8,
                                    (sx + dx) * 8, (sy + dy) * 8);
                                seatUsed[(sy + dy) * SEAT_TILE_WIDTH + (sx + dx)] = true;
                            }
                        }

                        // Mark source tiles as used
                        for (int dy = 0; dy < th; dy++)
                            for (int dx = 0; dx < tw; dx++)
                                useTileData[(srcTileY + dy) * tilesW + (srcTileX + dx)] = true;

                        seatX = sx;
                        seatY = sy;
                        return true;
                    }
                }
            }
            return false;
        }

        static void CopyTile(byte[] src, int srcW, int srcX, int srcY,
            byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int si = (srcY + y) * srcW + (srcX + x);
                    int di = (dstY + y) * dstW + (dstX + x);
                    if (si < src.Length && di < dst.Length)
                        dst[di] = src[si];
                }
        }

        #endregion

        #region OAM Assembly

        static void AppendOAM(byte align, byte area, List<byte> oam,
            int seatX, int seatY, int vramX, int vramY, int oamPalette)
        {
            oam.Add(0);
            oam.Add(align);
            oam.Add(0);
            oam.Add(area);

            oam.Add((byte)((seatX & 0x1F) | ((seatY << 5) & 0xE0)));
            oam.Add((byte)((oamPalette & 0xF) << 4));

            oam.Add((byte)(vramX & 0xFF));
            oam.Add((byte)((vramX >> 8) & 0xFF));

            oam.Add((byte)(vramY & 0xFF));
            oam.Add((byte)((vramY >> 8) & 0xFF));

            oam.Add(0);
            oam.Add(0);
        }

        static void AppendTermOAM(List<byte> oam)
        {
            oam.Add(1); oam.Add(0); oam.Add(0); oam.Add(0);
            oam.Add(0); oam.Add(0);
            oam.Add(0); oam.Add(0);
            oam.Add(0); oam.Add(0);
            oam.Add(0); oam.Add(0);
        }

        #endregion

        #region Tile Encoding

        static byte[] EncodeSeatTo4bpp(byte[] indexedPixels, int width, int height)
        {
            int tilesW = width / 8;
            int tilesH = height / 8;
            byte[] result = new byte[tilesW * tilesH * 32]; // 32 bytes per 8×8 tile

            for (int ty = 0; ty < tilesH; ty++)
            {
                for (int tx = 0; tx < tilesW; tx++)
                {
                    int tileOffset = (ty * tilesW + tx) * 32;
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x += 2)
                        {
                            int px1 = (ty * 8 + y) * width + (tx * 8 + x);
                            int px2 = px1 + 1;
                            byte lo = (px1 < indexedPixels.Length) ? (byte)(indexedPixels[px1] & 0x0F) : (byte)0;
                            byte hi = (px2 < indexedPixels.Length) ? (byte)(indexedPixels[px2] & 0x0F) : (byte)0;
                            result[tileOffset + y * 4 + x / 2] = (byte)(lo | (hi << 4));
                        }
                    }
                }
            }
            return result;
        }

        #endregion

        #region ROM Writing

        static string WriteToRom(ROM rom, uint animRecordAddr, BuildResult data)
        {
            // Search from ROM midpoint to avoid low-address table regions
            uint searchStart = (uint)(rom.Data.Length / 2);

            // Write sheet images to ROM
            var sheetAddrs = new List<uint>();
            foreach (var sheet in data.SheetImages)
            {
                uint addr = rom.FindFreeSpace(searchStart, (uint)sheet.Length);
                if (addr == U.NOT_FOUND)
                    addr = rom.FindFreeSpace(0x100, (uint)sheet.Length); // fallback
                if (addr == U.NOT_FOUND)
                    return $"Cannot find {sheet.Length} bytes of free space for tile sheet.";
                rom.write_range(addr, sheet);
                sheetAddrs.Add(addr);
            }

            // Write OAM data (offset +20: right-to-left)
            uint oamAddr = rom.FindFreeSpace(searchStart, (uint)data.OamData.Length);
            if (oamAddr == U.NOT_FOUND)
                oamAddr = rom.FindFreeSpace(0x100, (uint)data.OamData.Length);
            if (oamAddr == U.NOT_FOUND)
                return $"Cannot find {data.OamData.Length} bytes of free space for OAM data.";
            rom.write_range(oamAddr, data.OamData);
            rom.write_p32(animRecordAddr + 20, oamAddr);
            // Left-to-right reuses same OAM (matches AutoGenLeftOAM patch behavior)
            rom.write_p32(animRecordAddr + 24, oamAddr);

            // Write palette (offset +28)
            uint palAddr = rom.FindFreeSpace(searchStart, (uint)data.PaletteData.Length);
            if (palAddr == U.NOT_FOUND)
                palAddr = rom.FindFreeSpace(0x100, (uint)data.PaletteData.Length);
            if (palAddr == U.NOT_FOUND)
                return $"Cannot find {data.PaletteData.Length} bytes of free space for palette.";
            rom.write_range(palAddr, data.PaletteData);
            rom.write_p32(animRecordAddr + 28, palAddr);

            // Update frame data with actual sheet addresses
            byte[] frameBytes = data.FrameData;
            int pos = 0;
            while (pos + 11 < frameBytes.Length)
            {
                uint cmd = ReadU32(frameBytes, pos);
                if ((cmd & 0xFF000000) == 0x86000000)
                {
                    // Frame command: next 4 bytes = sheet index, then 4 bytes = OAM pos
                    uint sheetIdx = ReadU32(frameBytes, pos + 4);
                    if (sheetIdx < sheetAddrs.Count)
                    {
                        uint gbaPtr = U.toPointer(sheetAddrs[(int)sheetIdx]);
                        WriteU32(frameBytes, (uint)(pos + 4), gbaPtr);
                    }
                    pos += 12;
                }
                else if ((cmd & 0xFF000000) == 0x85000000)
                {
                    pos += 4;
                }
                else if (cmd == 0x80000000)
                {
                    pos += 4;
                }
                else
                {
                    pos += 4;
                }
            }

            // Write frame data compressed (offset +16)
            byte[] frameCompressed = LZ77.compress(frameBytes);
            uint frameAddr = rom.FindFreeSpace(searchStart, (uint)frameCompressed.Length);
            if (frameAddr == U.NOT_FOUND)
                frameAddr = rom.FindFreeSpace(0x100, (uint)frameCompressed.Length);
            if (frameAddr == U.NOT_FOUND)
                return $"Cannot find {frameCompressed.Length} bytes of free space for frame data.";
            rom.write_range(frameAddr, frameCompressed);
            rom.write_p32(animRecordAddr + 16, frameAddr);

            // Write section data (offset +12, uncompressed 48 bytes)
            uint sectionAddr = rom.FindFreeSpace(searchStart, (uint)data.SectionData.Length);
            if (sectionAddr == U.NOT_FOUND)
                sectionAddr = rom.FindFreeSpace(0x100, (uint)data.SectionData.Length);
            if (sectionAddr == U.NOT_FOUND)
                return $"Cannot find {data.SectionData.Length} bytes of free space for section data.";
            rom.write_range(sectionAddr, data.SectionData);
            rom.write_p32(animRecordAddr + 12, sectionAddr);

            return string.Empty; // success
        }

        #endregion

        #region Palette Remapping

        /// <summary>Remap indexed pixels to the shared palette by nearest-color matching.</summary>
        static void RemapToSharedPalette(byte[] indexData, byte[] framePalette, byte[] sharedPalette, int w, int h)
        {
            if (framePalette == null || sharedPalette == null) return;

            // Build remap table: for each frame palette entry, find nearest in shared palette
            int frameColors = framePalette.Length / 4;
            int sharedColors = sharedPalette.Length / 4;
            byte[] remap = new byte[Math.Max(frameColors, 256)];

            for (int i = 0; i < frameColors; i++)
            {
                if (i == 0) { remap[i] = 0; continue; } // Index 0 = transparent

                int fr = framePalette[i * 4];
                int fg = framePalette[i * 4 + 1];
                int fb = framePalette[i * 4 + 2];

                int bestIdx = 0;
                int bestDist = int.MaxValue;
                for (int j = 1; j < sharedColors; j++) // skip transparent
                {
                    int sr = sharedPalette[j * 4];
                    int sg = sharedPalette[j * 4 + 1];
                    int sb = sharedPalette[j * 4 + 2];
                    int dist = (fr - sr) * (fr - sr) + (fg - sg) * (fg - sg) + (fb - sb) * (fb - sb);
                    if (dist < bestDist) { bestDist = dist; bestIdx = j; }
                }
                remap[i] = (byte)bestIdx;
            }

            // Apply remap
            for (int p = 0; p < indexData.Length; p++)
            {
                byte idx = indexData[p];
                if (idx < frameColors)
                    indexData[p] = remap[idx];
            }
        }

        #endregion

        #region Helpers

        static void AppendU32(List<byte> list, uint value)
        {
            list.Add((byte)(value & 0xFF));
            list.Add((byte)((value >> 8) & 0xFF));
            list.Add((byte)((value >> 16) & 0xFF));
            list.Add((byte)((value >> 24) & 0xFF));
        }

        static void WriteU32(byte[] data, uint offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static uint ReadU32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        static uint ParseHex(string s)
        {
            s = s.Trim().Replace("0x", "").Replace("0X", "");
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        static uint ParseDecimal(string s)
        {
            uint val = 0;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9')
                    val = val * 10 + (uint)(c - '0');
                else
                    break;
            }
            return val;
        }

        #endregion

        #region FEditor .bin Import

        static readonly byte[] FEDITOR_HEADER_1 = { 0x5C, 0x78, 0x78, 0x75, 0x72 };
        static readonly byte[] FEDITOR_HEADER_2 = { 0x5C, 0x78, 0x70 };
        static readonly byte[] FEDITOR_FOOTER = { 0x75, 0x71, 0x00, 0x7E, 0x00 };
        const int FEDITOR_HEADER_SKIP = 0x38;

        /// <summary>
        /// Import battle animation from FEditor serialized .bin format.
        /// Requires companion files: {basename} Frame Data.dmp, {basename} Sheet N.png
        /// </summary>
        public static string ImportFEditorBin(string binPath, uint animRecordAddr,
            Func<string, (byte[] rgba, int w, int h)?> imageLoader)
        {
            if (!File.Exists(binPath))
                return $"File not found: {binPath}";

            ROM rom = CoreState.ROM;
            if (rom == null) return "No ROM loaded.";

            byte[] binData = File.ReadAllBytes(binPath);
            string baseName = Path.GetFileNameWithoutExtension(binPath);
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(binPath));

            // Locate header
            uint headerPos = GrepBytes(binData, FEDITOR_HEADER_1, 0);
            int headerSkip = FEDITOR_HEADER_SKIP + FEDITOR_HEADER_1.Length;

            if (headerPos == U.NOT_FOUND)
            {
                // Try variant header
                headerPos = GrepBytes(binData, FEDITOR_HEADER_2, 0);
                if (headerPos == U.NOT_FOUND)
                    return "Invalid FEditor .bin file: header not found.";

                // For variant header, find the next occurrence of footer to determine skip
                uint footerAfterHeader = GrepBytes(binData, new byte[] { 0x70, 0x78, 0x75, 0x72 },
                    headerPos + (uint)FEDITOR_HEADER_2.Length);
                if (footerAfterHeader == U.NOT_FOUND)
                    return "Invalid FEditor .bin file: cannot determine header variant.";
                headerSkip = (int)(footerAfterHeader - headerPos) + 4 + FEDITOR_HEADER_SKIP;
            }

            uint pos = headerPos + (uint)headerSkip;
            if (pos + SECTION_COUNT * 4 > binData.Length)
                return "FEditor .bin file too short for section data.";

            // Extract section data (48 bytes)
            byte[] sectionData = new byte[SECTION_COUNT * 4];
            Array.Copy(binData, pos, sectionData, 0, SECTION_COUNT * 4);
            pos += SECTION_COUNT * 4;

            // Skip to RightToLeftOAM (skip footer + 5 bytes)
            pos += (uint)FEDITOR_FOOTER.Length + 5;
            uint oamEnd = GrepBytes(binData, FEDITOR_FOOTER, pos);
            if (oamEnd == U.NOT_FOUND)
                return "FEditor .bin: cannot find RightToLeftOAM boundary.";

            int rtlLen = (int)(oamEnd - pos);
            if (rtlLen < 0 || pos + rtlLen > binData.Length)
                return "FEditor .bin: RightToLeftOAM bounds invalid.";
            byte[] rightToLeftOAM = new byte[rtlLen];
            Array.Copy(binData, (int)pos, rightToLeftOAM, 0, rtlLen);

            // Skip to LeftToRightOAM
            pos = oamEnd + (uint)FEDITOR_FOOTER.Length + 5;
            oamEnd = GrepBytes(binData, FEDITOR_FOOTER, pos);
            if (oamEnd == U.NOT_FOUND)
                return "FEditor .bin: cannot find LeftToRightOAM boundary.";

            int ltrLen = (int)(oamEnd - pos);
            if (ltrLen < 0 || pos + ltrLen > binData.Length)
                return "FEditor .bin: LeftToRightOAM bounds invalid.";
            byte[] leftToRightOAM = new byte[ltrLen];
            Array.Copy(binData, (int)pos, leftToRightOAM, 0, ltrLen);

            // Extract palette (remaining data after footer)
            pos = oamEnd + (uint)FEDITOR_FOOTER.Length + 5;
            int palLen = (int)(binData.Length - pos);
            if (palLen < 0) palLen = 0;
            byte[] palette = new byte[palLen];
            if (palLen > 0)
                Array.Copy(binData, (int)pos, palette, 0, palLen);

            // Read frame data from companion .dmp file
            string dmpPath = Path.Combine(baseDir, baseName + " Frame Data.dmp");
            if (!File.Exists(dmpPath))
                return $"Companion file not found: {baseName} Frame Data.dmp";
            byte[] frameDataRaw = File.ReadAllBytes(dmpPath);

            // Load sheet PNGs
            var sheetDatas = new List<byte[]>();
            for (int i = 1; i <= 254; i++)
            {
                string sheetName = $"{baseName} Sheet {i}.png";
                string sheetPath = Path.Combine(baseDir, sheetName);
                if (!File.Exists(sheetPath)) break;

                var loaded = imageLoader(sheetPath);
                if (loaded == null)
                    return $"Failed to load sheet image: {sheetName}";

                var (rgba, w, h) = loaded.Value;
                // Quantize and encode as 4bpp tiles
                var qr = DecreaseColorCore.Quantize(rgba, w, h, 16);
                if (qr == null)
                    return $"Failed to quantize sheet: {sheetName}";

                byte[] tiles = EncodeSeatTo4bpp(qr.IndexData, w, h);
                sheetDatas.Add(LZ77.compress(tiles));
            }

            if (sheetDatas.Count == 0)
                return $"No sheet images found: {baseName} Sheet 1.png";

            // Write to ROM
            uint searchStart = (uint)(rom.Data.Length / 2);

            // Write sheet images
            var sheetAddrs = new List<uint>();
            foreach (var sheet in sheetDatas)
            {
                uint addr = rom.FindFreeSpace(searchStart, (uint)sheet.Length);
                if (addr == U.NOT_FOUND)
                    addr = rom.FindFreeSpace(0x100, (uint)sheet.Length);
                if (addr == U.NOT_FOUND)
                    return $"No free space for sheet ({sheet.Length} bytes).";
                rom.write_range(addr, sheet);
                sheetAddrs.Add(addr);
            }

            // Write SEPARATE OAM data (distinct +20 and +24)
            byte[] rtlCompressed = LZ77.compress(rightToLeftOAM);
            uint rtlAddr = rom.FindFreeSpace(searchStart, (uint)rtlCompressed.Length);
            if (rtlAddr == U.NOT_FOUND)
                rtlAddr = rom.FindFreeSpace(0x100, (uint)rtlCompressed.Length);
            if (rtlAddr == U.NOT_FOUND)
                return "No free space for RightToLeftOAM.";
            rom.write_range(rtlAddr, rtlCompressed);
            rom.write_p32(animRecordAddr + 20, rtlAddr);

            byte[] ltrCompressed = LZ77.compress(leftToRightOAM);
            uint ltrAddr = rom.FindFreeSpace(searchStart, (uint)ltrCompressed.Length);
            if (ltrAddr == U.NOT_FOUND)
                ltrAddr = rom.FindFreeSpace(0x100, (uint)ltrCompressed.Length);
            if (ltrAddr == U.NOT_FOUND)
                return "No free space for LeftToRightOAM.";
            rom.write_range(ltrAddr, ltrCompressed);
            rom.write_p32(animRecordAddr + 24, ltrAddr);

            // Write palette
            byte[] palCompressed = (palette.Length >= 0x80)
                ? LZ77.compress(palette)
                : LZ77.compress(new byte[0x80]); // empty 4-team palette
            uint palAddr = rom.FindFreeSpace(searchStart, (uint)palCompressed.Length);
            if (palAddr == U.NOT_FOUND)
                palAddr = rom.FindFreeSpace(0x100, (uint)palCompressed.Length);
            if (palAddr == U.NOT_FOUND)
                return "No free space for palette.";
            rom.write_range(palAddr, palCompressed);
            rom.write_p32(animRecordAddr + 28, palAddr);

            // Update frame data with sheet addresses
            UpdateFrameDataAddresses(frameDataRaw, sheetAddrs);

            // Write frame data compressed
            byte[] frameCompressed = LZ77.compress(frameDataRaw);
            uint frameAddr = rom.FindFreeSpace(searchStart, (uint)frameCompressed.Length);
            if (frameAddr == U.NOT_FOUND)
                frameAddr = rom.FindFreeSpace(0x100, (uint)frameCompressed.Length);
            if (frameAddr == U.NOT_FOUND)
                return "No free space for frame data.";
            rom.write_range(frameAddr, frameCompressed);
            rom.write_p32(animRecordAddr + 16, frameAddr);

            // Write section data
            uint secAddr = rom.FindFreeSpace(searchStart, (uint)sectionData.Length);
            if (secAddr == U.NOT_FOUND)
                secAddr = rom.FindFreeSpace(0x100, (uint)sectionData.Length);
            if (secAddr == U.NOT_FOUND)
                return "No free space for section data.";
            rom.write_range(secAddr, sectionData);
            rom.write_p32(animRecordAddr + 12, secAddr);

            return string.Empty;
        }

        internal static void UpdateFrameDataAddresses(byte[] frameData, List<uint> sheetAddrs)
        {
            // First pass: collect unique graphics pointers in order of first appearance.
            // Each unique pointer maps to a sequential sheet index (Sheet 1, Sheet 2, ...).
            var uniquePtrs = new List<uint>();
            var ptrToIndex = new Dictionary<uint, int>();

            for (int i = 0; i + 11 < frameData.Length; )
            {
                byte cmdType = frameData[i + 3];
                if (cmdType == 0x86)
                {
                    uint gfxPtr = (uint)(frameData[i + 4] | (frameData[i + 5] << 8) |
                        (frameData[i + 6] << 16) | (frameData[i + 7] << 24));
                    if (!ptrToIndex.ContainsKey(gfxPtr))
                    {
                        ptrToIndex[gfxPtr] = uniquePtrs.Count;
                        uniquePtrs.Add(gfxPtr);
                    }
                    i += 12;
                }
                else
                {
                    i += 4;
                }
            }

            // Second pass: replace each graphics pointer with the new ROM address
            for (int i = 0; i + 11 < frameData.Length; )
            {
                byte cmdType = frameData[i + 3];
                if (cmdType == 0x86)
                {
                    uint gfxPtr = (uint)(frameData[i + 4] | (frameData[i + 5] << 8) |
                        (frameData[i + 6] << 16) | (frameData[i + 7] << 24));
                    int sheetIdx = ptrToIndex.ContainsKey(gfxPtr) ? ptrToIndex[gfxPtr] : 0;
                    if (sheetIdx < sheetAddrs.Count)
                    {
                        uint newPtr = U.toPointer(sheetAddrs[sheetIdx]);
                        frameData[i + 4] = (byte)(newPtr & 0xFF);
                        frameData[i + 5] = (byte)((newPtr >> 8) & 0xFF);
                        frameData[i + 6] = (byte)((newPtr >> 16) & 0xFF);
                        frameData[i + 7] = (byte)((newPtr >> 24) & 0xFF);
                    }
                    i += 12;
                }
                else
                {
                    i += 4;
                }
            }
        }

        static uint GrepBytes(byte[] data, byte[] pattern, uint startPos)
        {
            for (uint i = startPos; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return U.NOT_FOUND;
        }

        #endregion
    }
}
