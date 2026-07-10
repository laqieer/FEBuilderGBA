using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform map tile conversion logic extracted from WinForms MapStyleEditorForm.
    /// Converts a single image into GBA map tile data + TSA (Tile Set Arrangement).
    /// </summary>
    public static class MapConvertCore
    {
        /// <summary>Result of map conversion.</summary>
        public class MapConvertResult
        {
            /// <summary>4bpp tile pixel data.</summary>
            public byte[] TileData { get; set; }
            /// <summary>TSA (map arrangement) data.</summary>
            public byte[] TSAData { get; set; }
            /// <summary>GBA palette data for the single output palette.</summary>
            public byte[] PaletteData { get; set; }
            /// <summary>Number of unique 8x8 tiles.</summary>
            public int TileCount { get; set; }
            /// <summary>Number of palettes used.</summary>
            public int PaletteCount { get; set; }
            /// <summary>Width in tiles.</summary>
            public int WidthTiles { get; set; }
            /// <summary>Height in tiles.</summary>
            public int HeightTiles { get; set; }
        }

        /// <summary>
        /// Extract unique 8x8 tiles from RGBA pixel data and build TSA.
        /// </summary>
        /// <param name="rgbaPixels">RGBA pixel data (4 bytes per pixel)</param>
        /// <param name="width">Image width (must be multiple of 8)</param>
        /// <param name="height">Image height (must be multiple of 8)</param>
        /// <returns>Conversion result or null on error</returns>
        public static MapConvertResult ConvertImage(byte[] rgbaPixels, int width, int height)
        {
            return ConvertImage(rgbaPixels, width, height, out _);
        }

        /// <summary>
        /// Compatibility overload. Map conversion emits exactly one palette; maxPalettes is ignored.
        /// </summary>
        public static MapConvertResult ConvertImage(byte[] rgbaPixels, int width, int height, int maxPalettes)
        {
            return ConvertImage(rgbaPixels, width, height, out _);
        }

        /// <summary>
        /// Extract unique 8x8 tiles from RGBA pixel data and build TSA, returning a specific error.
        /// </summary>
        public static MapConvertResult ConvertImage(byte[] rgbaPixels, int width, int height, out string error)
        {
            error = "";
            if (rgbaPixels == null || width < 8 || height < 8)
            {
                error = "Input image data is invalid.";
                return null;
            }
            if (width % 8 != 0 || height % 8 != 0)
            {
                error = "Image dimensions must be multiples of 8.";
                return null;
            }
            if (rgbaPixels.Length < width * height * 4)
            {
                error = "Input image data is shorter than its dimensions require.";
                return null;
            }

            int tilesX = width / 8;
            int tilesY = height / 8;

            const int TransparentKey = 0x10000;
            var opaquePaletteKeys = new HashSet<int>();
            int pixelCount = width * height;
            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                if (rgbaPixels[offset + 3] < 128)
                    continue;

                int key = (rgbaPixels[offset] >> 3) |
                    ((rgbaPixels[offset + 1] >> 3) << 5) |
                    ((rgbaPixels[offset + 2] >> 3) << 10);
                opaquePaletteKeys.Add(key);
                if (opaquePaletteKeys.Count > 15)
                {
                    error = "Image requires more than 15 opaque colors; map conversion reserves palette index 0 for transparency.";
                    return null;
                }
            }

            // Step 1: Map the already-validated colors directly. Quantizing an exactly
            // representable image can merge rare colors and silently change pixel data.
            var compactPaletteMap = new Dictionary<int, byte>
            {
                [TransparentKey] = 0,
            };
            var compactPalette = new List<ushort> { 0 };

            byte[] compactIndexData = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                int key;
                ushort gbaColor = 0;
                if (rgbaPixels[i * 4 + 3] < 128)
                {
                    key = TransparentKey;
                }
                else
                {
                    int pixelOffset = i * 4;
                    gbaColor = (ushort)((rgbaPixels[pixelOffset] >> 3) |
                        ((rgbaPixels[pixelOffset + 1] >> 3) << 5) |
                        ((rgbaPixels[pixelOffset + 2] >> 3) << 10));
                    key = gbaColor;
                }

                if (!compactPaletteMap.TryGetValue(key, out byte compactIndex))
                {
                    if (compactPalette.Count >= 16)
                    {
                        error = "Image requires more than 15 opaque colors; map conversion reserves palette index 0 for transparency.";
                        return null;
                    }
                    compactIndex = (byte)compactPalette.Count;
                    compactPaletteMap[key] = compactIndex;
                    compactPalette.Add(gbaColor);
                }
                compactIndexData[i] = compactIndex;
            }

            byte[] paletteData = new byte[compactPalette.Count * 2];
            for (int i = 0; i < compactPalette.Count; i++)
            {
                paletteData[i * 2] = (byte)(compactPalette[i] & 0xFF);
                paletteData[i * 2 + 1] = (byte)(compactPalette[i] >> 8);
            }

            // Step 2: Extract unique 8x8 tiles
            var uniqueTiles = new List<byte[]>();
            var tileMap = new Dictionary<string, int>(); // hash -> tile index
            var tsaEntries = new ushort[tilesX * tilesY];

            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    // Extract 8x8 tile pixel indices
                    byte[] tile = new byte[64];
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            int srcIdx = (ty * 8 + py) * width + (tx * 8 + px);
                            tile[py * 8 + px] = compactIndexData[srcIdx];
                        }
                    }

                    string hash = ComputeTileHash(tile);
                    int tileIdx;

                    if (tileMap.TryGetValue(hash, out int existing))
                    {
                        tileIdx = existing;
                    }
                    else
                    {
                        tileIdx = uniqueTiles.Count;
                        uniqueTiles.Add(tile);
                        if (uniqueTiles.Count > 0x400)
                        {
                            error = "Image contains more than 1024 unique tiles; TSA tile indices are limited to 10 bits.";
                            return null;
                        }
                        tileMap[hash] = tileIdx;
                    }

                    // TSA entry: bits 0-9 = tile index, bits 12-15 = palette (0 for now)
                    tsaEntries[ty * tilesX + tx] = (ushort)tileIdx;
                }
            }

            // Step 3: Convert tiles to 4bpp format (2 pixels per byte)
            byte[] tileData = new byte[uniqueTiles.Count * 32]; // 32 bytes per 8x8 4bpp tile
            for (int t = 0; t < uniqueTiles.Count; t++)
            {
                byte[] tile = uniqueTiles[t];
                for (int row = 0; row < 8; row++)
                {
                    for (int col = 0; col < 8; col += 2)
                    {
                        byte lo = (byte)(tile[row * 8 + col] & 0x0F);
                        byte hi = (byte)(tile[row * 8 + col + 1] & 0x0F);
                        tileData[t * 32 + row * 4 + col / 2] = (byte)(lo | (hi << 4));
                    }
                }
            }

            // Step 4: Convert TSA to byte array (2 bytes per entry, little-endian)
            byte[] tsaData = new byte[tsaEntries.Length * 2];
            for (int i = 0; i < tsaEntries.Length; i++)
            {
                tsaData[i * 2 + 0] = (byte)(tsaEntries[i] & 0xFF);
                tsaData[i * 2 + 1] = (byte)((tsaEntries[i] >> 8) & 0xFF);
            }

            return new MapConvertResult
            {
                TileData = tileData,
                TSAData = tsaData,
                PaletteData = paletteData,
                TileCount = uniqueTiles.Count,
                PaletteCount = 1,
                WidthTiles = tilesX,
                HeightTiles = tilesY,
            };
        }

        /// <summary>
        /// Compatibility overload. Map conversion emits exactly one palette; maxPalettes is ignored.
        /// </summary>
        public static MapConvertResult ConvertImage(byte[] rgbaPixels, int width, int height,
            out string error, int maxPalettes)
        {
            return ConvertImage(rgbaPixels, width, height, out error);
        }

        static string ComputeTileHash(byte[] tile)
        {
            // Simple hash: convert to hex string
            var sb = new System.Text.StringBuilder(tile.Length * 2);
            for (int i = 0; i < tile.Length; i++)
                sb.Append(tile[i].ToString("X2"));
            return sb.ToString();
        }
    }
}
