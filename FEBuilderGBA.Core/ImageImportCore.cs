using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform image import pipeline for writing graphics data to ROM.
    /// Handles: quantize → encode tiles → encode TSA (with dedup) → LZ77 compress → write to ROM.
    /// </summary>
    public static class ImageImportCore
    {
        /// <summary>Result of TSA encoding (tile deduplication).</summary>
        public class TSAEncodeResult
        {
            /// <summary>Deduplicated tile data (4bpp: 32 bytes/tile).</summary>
            public byte[] TileData { get; set; }
            /// <summary>TSA entries (2 bytes each: bits 0-9=tile index, 10=hflip, 11=vflip, 12-15=palette).</summary>
            public byte[] TSAData { get; set; }
            /// <summary>Number of unique tiles after deduplication.</summary>
            public int UniqueTileCount { get; set; }
        }

        /// <summary>Result of a full image import operation.</summary>
        public class ImportResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            /// <summary>ROM offset where compressed tile data was written.</summary>
            public uint TileDataOffset { get; set; }
            /// <summary>ROM offset where compressed TSA data was written.</summary>
            public uint TSADataOffset { get; set; }
            /// <summary>ROM offset where palette was written.</summary>
            public uint PaletteOffset { get; set; }
        }

        /// <summary>
        /// Encode indexed pixel data into deduplicated 4bpp tiles + TSA map.
        /// Ports WinForms ImageUtil.ImageToBytePackedTSA logic.
        /// </summary>
        /// <param name="indexedPixels">1 byte per pixel (palette indices, max 15 for 4bpp)</param>
        /// <param name="width">Image width (must be multiple of 8)</param>
        /// <param name="height">Image height (must be multiple of 8)</param>
        /// <param name="paletteIndex">Palette index for TSA entries (bits 12-15), typically 0</param>
        public static TSAEncodeResult EncodeTSA(byte[] indexedPixels, int width, int height, int paletteIndex = 0)
        {
            if (indexedPixels == null || width % 8 != 0 || height % 8 != 0)
                return null;

            int tilesX = width / 8;
            int tilesY = height / 8;
            int totalTiles = tilesX * tilesY;

            // Extract all 8x8 tiles as 32-byte 4bpp blocks
            var allTiles = new List<byte[]>();
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    byte[] tile = ExtractTile4bpp(indexedPixels, width, tx * 8, ty * 8);
                    allTiles.Add(tile);
                }
            }

            // Deduplicate tiles with flip search
            var uniqueTiles = new List<byte[]>();
            var tsaEntries = new ushort[totalTiles];

            for (int i = 0; i < totalTiles; i++)
            {
                byte[] tile = allTiles[i];
                int matchIndex = -1;
                int flipFlags = 0;

                // Search for exact match
                matchIndex = FindTileMatch(uniqueTiles, tile);
                if (matchIndex >= 0)
                {
                    flipFlags = 0;
                }
                else
                {
                    // Search H-flip
                    byte[] hFlipped = FlipTileH4bpp(tile);
                    matchIndex = FindTileMatch(uniqueTiles, hFlipped);
                    if (matchIndex >= 0)
                    {
                        flipFlags = 0x0400; // H-flip bit
                    }
                    else
                    {
                        // Search V-flip
                        byte[] vFlipped = FlipTileV4bpp(tile);
                        matchIndex = FindTileMatch(uniqueTiles, vFlipped);
                        if (matchIndex >= 0)
                        {
                            flipFlags = 0x0800; // V-flip bit
                        }
                        else
                        {
                            // Search HV-flip
                            byte[] hvFlipped = FlipTileV4bpp(hFlipped);
                            matchIndex = FindTileMatch(uniqueTiles, hvFlipped);
                            if (matchIndex >= 0)
                            {
                                flipFlags = 0x0C00; // H+V flip bits
                            }
                        }
                    }
                }

                if (matchIndex < 0)
                {
                    // New unique tile
                    matchIndex = uniqueTiles.Count;
                    uniqueTiles.Add(tile);
                    flipFlags = 0;
                }

                // Build TSA entry: tile index | flip flags | palette
                tsaEntries[i] = (ushort)(matchIndex | flipFlags | ((paletteIndex & 0xF) << 12));
            }

            // Flatten unique tiles into contiguous byte array
            byte[] tileData = new byte[uniqueTiles.Count * 32];
            for (int i = 0; i < uniqueTiles.Count; i++)
                Array.Copy(uniqueTiles[i], 0, tileData, i * 32, 32);

            // Convert TSA entries to byte array
            byte[] tsaData = new byte[totalTiles * 2];
            for (int i = 0; i < totalTiles; i++)
            {
                tsaData[i * 2] = (byte)(tsaEntries[i] & 0xFF);
                tsaData[i * 2 + 1] = (byte)((tsaEntries[i] >> 8) & 0xFF);
            }

            return new TSAEncodeResult
            {
                TileData = tileData,
                TSAData = tsaData,
                UniqueTileCount = uniqueTiles.Count,
            };
        }

        /// <summary>
        /// Encode indexed pixels directly to 4bpp tile data (no TSA deduplication).
        /// Suitable for fixed-size images like icons or terrain tiles.
        /// </summary>
        public static byte[] EncodeDirectTiles4bpp(byte[] indexedPixels, int width, int height)
        {
            if (indexedPixels == null || width % 8 != 0 || height % 8 != 0)
                return null;

            int tilesX = width / 8;
            int tilesY = height / 8;
            byte[] result = new byte[tilesX * tilesY * 32];
            int pos = 0;

            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    byte[] tile = ExtractTile4bpp(indexedPixels, width, tx * 8, ty * 8);
                    Array.Copy(tile, 0, result, pos, 32);
                    pos += 32;
                }
            }
            return result;
        }

        /// <summary>
        /// Write LZ77-compressed data to ROM free space and update a pointer.
        /// Returns the ROM offset where data was written, or U.NOT_FOUND on failure.
        /// </summary>
        public static uint WriteCompressedToROM(ROM rom, byte[] rawData, uint pointerAddr)
        {
            if (rom == null || rawData == null) return U.NOT_FOUND;

            byte[] compressed = LZ77.compress(rawData);
            if (compressed == null || compressed.Length == 0) return U.NOT_FOUND;

            uint writeAddr = FindAndWriteData(rom, compressed);
            if (writeAddr == U.NOT_FOUND) return U.NOT_FOUND;

            // Update pointer in ROM (GBA pointer = offset + 0x08000000)
            rom.write_p32(pointerAddr, writeAddr);
            return writeAddr;
        }

        /// <summary>
        /// Write raw (uncompressed) data to ROM free space and update a pointer.
        /// </summary>
        public static uint WriteRawToROM(ROM rom, byte[] data, uint pointerAddr)
        {
            if (rom == null || data == null) return U.NOT_FOUND;

            uint writeAddr = FindAndWriteData(rom, data);
            if (writeAddr == U.NOT_FOUND) return U.NOT_FOUND;

            rom.write_p32(pointerAddr, writeAddr);
            return writeAddr;
        }

        /// <summary>
        /// Write a GBA palette (2 bytes per color) to ROM free space and update a pointer.
        /// </summary>
        public static uint WritePaletteToROM(ROM rom, byte[] gbaPalette, uint pointerAddr)
        {
            return WriteRawToROM(rom, gbaPalette, pointerAddr);
        }

        /// <summary>
        /// Find free space in ROM, write data there, and return the offset.
        /// Searches from halfway through the ROM for free space (0x00 or 0xFF fill).
        /// </summary>
        public static uint FindAndWriteData(ROM rom, byte[] data)
        {
            if (rom == null || data == null || data.Length == 0) return U.NOT_FOUND;

            // Align data size to 4 bytes
            uint needSize = (uint)U.Padding4((uint)data.Length);

            // Search from midpoint of ROM for free space
            uint searchStart = (uint)(rom.Data.Length / 2);
            searchStart = U.Padding4(searchStart);

            uint addr = rom.FindFreeSpace(searchStart, needSize);
            if (addr == U.NOT_FOUND)
            {
                // Try from beginning
                addr = rom.FindFreeSpace(0x100, needSize);
            }
            if (addr == U.NOT_FOUND) return U.NOT_FOUND;

            // Write data to ROM
            WriteBytes(rom, addr, data);
            return addr;
        }

        /// <summary>
        /// Write raw bytes to ROM at a specific address.
        /// </summary>
        public static void WriteBytes(ROM rom, uint addr, byte[] data)
        {
            if (rom == null || data == null) return;
            if (addr + data.Length > (uint)rom.Data.Length) return;

            for (int i = 0; i < data.Length; i++)
                rom.write_u8(addr + (uint)i, data[i]);
        }

        /// <summary>
        /// Full 3-pointer import: image (LZ77) + TSA (LZ77) + palette (raw).
        /// Used by BattleBG, CG, BG editors.
        /// </summary>
        public static ImportResult Import3Pointer(ROM rom, byte[] indexedPixels, byte[] gbaPalette,
            int width, int height, uint imgPointerAddr, uint tsaPointerAddr, uint palPointerAddr,
            int paletteIndex = 0)
        {
            var result = new ImportResult();

            // Encode TSA with deduplication
            var tsaResult = EncodeTSA(indexedPixels, width, height, paletteIndex);
            if (tsaResult == null)
            {
                result.Error = "Failed to encode TSA data";
                return result;
            }

            // Write compressed tile data
            uint tileAddr = WriteCompressedToROM(rom, tsaResult.TileData, imgPointerAddr);
            if (tileAddr == U.NOT_FOUND)
            {
                result.Error = "Failed to write compressed tile data (no free space)";
                return result;
            }

            // Write compressed TSA data
            uint tsaAddr = WriteCompressedToROM(rom, tsaResult.TSAData, tsaPointerAddr);
            if (tsaAddr == U.NOT_FOUND)
            {
                result.Error = "Failed to write compressed TSA data (no free space)";
                return result;
            }

            // Write palette (raw, not compressed)
            uint palAddr = WritePaletteToROM(rom, gbaPalette, palPointerAddr);
            if (palAddr == U.NOT_FOUND)
            {
                result.Error = "Failed to write palette data (no free space)";
                return result;
            }

            result.Success = true;
            result.TileDataOffset = tileAddr;
            result.TSADataOffset = tsaAddr;
            result.PaletteOffset = palAddr;
            return result;
        }

        /// <summary>
        /// Full 2-pointer import: image (LZ77) + palette (raw). No TSA.
        /// Used by BattleTerrain, ChapterTitle editors.
        /// </summary>
        public static ImportResult Import2Pointer(ROM rom, byte[] indexedPixels, byte[] gbaPalette,
            int width, int height, uint imgPointerAddr, uint palPointerAddr)
        {
            var result = new ImportResult();

            byte[] tileData = EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tileData == null)
            {
                result.Error = "Failed to encode tile data";
                return result;
            }

            uint tileAddr = WriteCompressedToROM(rom, tileData, imgPointerAddr);
            if (tileAddr == U.NOT_FOUND)
            {
                result.Error = "Failed to write compressed tile data (no free space)";
                return result;
            }

            uint palAddr = WritePaletteToROM(rom, gbaPalette, palPointerAddr);
            if (palAddr == U.NOT_FOUND)
            {
                result.Error = "Failed to write palette data (no free space)";
                return result;
            }

            result.Success = true;
            result.TileDataOffset = tileAddr;
            result.PaletteOffset = palAddr;
            return result;
        }

        /// <summary>
        /// Import a fixed-size icon (e.g. 16x16) by writing tile data directly at a known address.
        /// No LZ77, no TSA, no pointer update. Overwrites in-place.
        /// </summary>
        public static bool ImportFixedIcon(ROM rom, byte[] indexedPixels, int width, int height, uint destAddr)
        {
            byte[] tileData = EncodeDirectTiles4bpp(indexedPixels, width, height);
            if (tileData == null) return false;

            if (destAddr + tileData.Length > (uint)rom.Data.Length) return false;

            WriteBytes(rom, destAddr, tileData);
            return true;
        }

        /// <summary>
        /// Re-index RGBA pixel data to an existing GBA palette by finding the closest color match.
        /// Used when importing into editors that share a fixed palette (e.g. item icons).
        /// Palette index 0 is treated as transparent (pixels with alpha &lt; 128).
        /// </summary>
        /// <param name="rgbaPixels">RGBA pixel data (4 bytes per pixel)</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="gbaPalette">Existing GBA palette to map to (2 bytes per color)</param>
        /// <param name="colorCount">Number of colors in the palette</param>
        /// <returns>Indexed pixel data (1 byte per pixel) mapped to the existing palette</returns>
        public static byte[] RemapToExistingPalette(byte[] rgbaPixels, int width, int height,
            byte[] gbaPalette, int colorCount)
        {
            if (rgbaPixels == null || gbaPalette == null) return null;
            if (CoreState.ImageService == null) return null;

            int pixelCount = width * height;
            if (rgbaPixels.Length < pixelCount * 4) return null;

            // Convert GBA palette to RGBA for distance calculation
            byte[][] palColors = new byte[colorCount][];
            for (int i = 0; i < colorCount && i * 2 + 1 < gbaPalette.Length; i++)
            {
                ushort gbaColor = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                palColors[i] = new byte[] { r, g, b };
            }

            byte[] indexed = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                byte pr = rgbaPixels[i * 4 + 0];
                byte pg = rgbaPixels[i * 4 + 1];
                byte pb = rgbaPixels[i * 4 + 2];
                byte pa = rgbaPixels[i * 4 + 3];

                // Transparent pixel → index 0
                if (pa < 128)
                {
                    indexed[i] = 0;
                    continue;
                }

                // Find closest palette color (skip index 0 = transparent)
                int bestIndex = 1;
                int bestDist = int.MaxValue;
                for (int c = 1; c < colorCount; c++)
                {
                    if (palColors[c] == null) continue;
                    int dr = pr - palColors[c][0];
                    int dg = pg - palColors[c][1];
                    int db = pb - palColors[c][2];
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIndex = c;
                    }
                }
                indexed[i] = (byte)bestIndex;
            }

            return indexed;
        }

        // ---- Internal tile manipulation ----

        /// <summary>Extract an 8x8 tile from indexed pixel data as 4bpp (32 bytes).</summary>
        internal static byte[] ExtractTile4bpp(byte[] pixels, int stride, int tileX, int tileY)
        {
            byte[] tile = new byte[32]; // 8 rows * 4 bytes/row
            for (int y = 0; y < 8; y++)
            {
                int srcRow = tileY + y;
                for (int x = 0; x < 8; x += 2)
                {
                    int srcIdx1 = srcRow * stride + tileX + x;
                    int srcIdx2 = srcRow * stride + tileX + x + 1;
                    byte lo = (srcIdx1 < pixels.Length) ? (byte)(pixels[srcIdx1] & 0x0F) : (byte)0;
                    byte hi = (srcIdx2 < pixels.Length) ? (byte)(pixels[srcIdx2] & 0x0F) : (byte)0;
                    tile[y * 4 + x / 2] = (byte)(lo | (hi << 4));
                }
            }
            return tile;
        }

        /// <summary>Horizontally flip a 4bpp 8x8 tile (32 bytes).</summary>
        internal static byte[] FlipTileH4bpp(byte[] tile)
        {
            byte[] flipped = new byte[32];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    byte srcByte = tile[y * 4 + (3 - x)];
                    // Swap nibbles within byte AND reverse byte order
                    flipped[y * 4 + x] = (byte)(((srcByte & 0x0F) << 4) | ((srcByte >> 4) & 0x0F));
                }
            }
            return flipped;
        }

        /// <summary>Vertically flip a 4bpp 8x8 tile (32 bytes).</summary>
        internal static byte[] FlipTileV4bpp(byte[] tile)
        {
            byte[] flipped = new byte[32];
            for (int y = 0; y < 8; y++)
            {
                Array.Copy(tile, (7 - y) * 4, flipped, y * 4, 4);
            }
            return flipped;
        }

        /// <summary>Search for an exact tile match in the unique tile list.</summary>
        static int FindTileMatch(List<byte[]> tiles, byte[] target)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (TilesEqual(tiles[i], target))
                    return i;
            }
            return -1;
        }

        /// <summary>Compare two 32-byte tiles for equality.</summary>
        static bool TilesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
