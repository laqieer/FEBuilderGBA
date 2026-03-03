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
            /// <summary>GBA palette data (multiple palettes, 32 bytes each).</summary>
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
        /// <param name="maxPalettes">Maximum number of 16-color palettes</param>
        /// <returns>Conversion result or null on error</returns>
        public static MapConvertResult ConvertImage(byte[] rgbaPixels, int width, int height, int maxPalettes = 5)
        {
            if (rgbaPixels == null || width < 8 || height < 8)
                return null;
            if (width % 8 != 0 || height % 8 != 0)
                return null;
            if (rgbaPixels.Length < width * height * 4)
                return null;

            int tilesX = width / 8;
            int tilesY = height / 8;

            // Step 1: Quantize the full image to get a global palette
            var quantized = DecreaseColorCore.Quantize(rgbaPixels, width, height, 16 * maxPalettes);
            if (quantized == null)
                return null;

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
                            tile[py * 8 + px] = quantized.IndexData[srcIdx];
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
                        tileMap[hash] = tileIdx;
                    }

                    // TSA entry: bits 0-9 = tile index, bits 12-15 = palette (0 for now)
                    tsaEntries[ty * tilesX + tx] = (ushort)(tileIdx & 0x3FF);
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
                PaletteData = quantized.GBAPalette,
                TileCount = uniqueTiles.Count,
                PaletteCount = 1,
                WidthTiles = tilesX,
                HeightTiles = tilesY,
            };
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
