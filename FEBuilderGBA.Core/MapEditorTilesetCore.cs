using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for the Visual Map Editor tile-picker / chipset palette
    /// (first slice of #658).
    ///
    /// <para>Naming convention used throughout this file (matches WinForms
    /// <c>MapEditorForm.cs</c>):</para>
    /// <list type="bullet">
    ///   <item><c>marValue</c> — the raw u16 value stored in the decompressed map data
    ///     (the "Map Arrangement Record" entry).</item>
    ///   <item><c>chipsetIndex</c> — the user-facing chipset palette index (0..1023).
    ///     This is the column-major coordinate the user picks from the chipset palette.</item>
    /// </list>
    /// <para>The conversion is fixed: <c>marValue = chipsetIndex &lt;&lt; 2</c> (i.e. *4),
    /// and conversely <c>chipsetIndex = marValue &gt;&gt; 2</c>.</para>
    ///
    /// <para>A chipset is a 16x16 visible tile composed of 4 8x8 sub-tile TSA entries
    /// stored in the decompressed "chipset config" data. The byte offset of a chipset's
    /// 8-byte TSA block in <c>configUZ</c> is <c>marValue &lt;&lt; 1</c> (which equals
    /// <c>chipsetIndex * 8</c>).</para>
    /// </summary>
    public static class MapEditorTilesetCore
    {
        /// <summary>
        /// Separator between TSA-block region and the terrain-data region in
        /// the decompressed chipset config. Bytes [0, CHIPSET_SEP_BYTE) hold
        /// TSA blocks (8 bytes per chipset); bytes from CHIPSET_SEP_BYTE on
        /// hold 2-byte terrain entries.
        /// </summary>
        public const int CHIPSET_SEP_BYTE = 0x2000;

        /// <summary>
        /// Number of chipsets shown per row in the palette grid (matches
        /// WinForms <c>MapEditorForm.cs</c> <c>BuildMapchipSet</c>).
        /// </summary>
        public const int PALETTE_COLUMNS = 32;

        /// <summary>Pixels per chipset side (16x16).</summary>
        public const int CHIPSET_PIXEL_SIZE = 16;

        /// <summary>Total chipsets representable in the TSA region (1024).</summary>
        public const int CHIPSET_COUNT = CHIPSET_SEP_BYTE / 8;

        /// <summary>Convert a chipset palette index to its raw MAR value.</summary>
        public static ushort ChipsetIndexToMar(int chipsetIndex) =>
            (ushort)((chipsetIndex << 2) & 0xFFFF);

        /// <summary>Convert a raw MAR value to its chipset palette index.</summary>
        public static int MarToChipsetIndex(ushort marValue) => marValue >> 2;

        /// <summary>
        /// Look up terrain data for a chipset given its raw MAR value.
        /// Direct port of WinForms <c>ImageUtilMap.GetChipsetID</c>.
        /// Returns <see cref="U.NOT_FOUND"/> if <paramref name="configUZ"/> is too small.
        /// </summary>
        public static uint GetTerrainDataFromMar(int marValue, byte[] configUZ)
        {
            if (configUZ == null) return U.NOT_FOUND;

            int terrainOffset = ((marValue >> 3) * 2) + CHIPSET_SEP_BYTE;
            if (terrainOffset + 1 >= configUZ.Length) return U.NOT_FOUND;

            // 0x4 bit of MAR selects the second terrain byte
            return (marValue & 0x4) > 0
                ? configUZ[terrainOffset + 1]
                : configUZ[terrainOffset];
        }

        /// <summary>
        /// Convenience wrapper that takes a user-facing chipset palette index.
        /// </summary>
        public static uint GetTerrainDataFromChipset(int chipsetIndex, byte[] configUZ) =>
            GetTerrainDataFromMar(ChipsetIndexToMar(chipsetIndex), configUZ);

        /// <summary>
        /// Write the terrain byte for a chipset (user-facing index 0..1023) into
        /// the decompressed chipset-config buffer in place. Mirrors the algebraic
        /// reduction of WF <c>MapStyleEditorForm</c>'s terrain-write block: the
        /// MAR-based even/odd byte path simplifies to a single byte at
        /// <c>CHIPSET_SEP_BYTE + chipsetIndex</c> when a user-facing index is
        /// already in hand.
        ///
        /// <para>Returns <c>false</c> without mutating <paramref name="configUZ"/>
        /// when <paramref name="chipsetIndex"/> falls outside the semantic
        /// chipset range [0, <see cref="CHIPSET_COUNT"/>) OR the buffer is too
        /// small to hold the target byte (<c>CHIPSET_SEP_BYTE + chipsetIndex
        /// &gt;= configUZ.Length</c>). The double bound deliberately rejects
        /// indices like <c>1024</c> that would land in the buffer's terrain
        /// region without first failing the semantic check.</para>
        /// </summary>
        public static bool SetTerrainForChipset(int chipsetIndex, byte terrain, byte[] configUZ)
        {
            if (configUZ == null) return false;
            if (chipsetIndex < 0 || chipsetIndex >= CHIPSET_COUNT) return false;
            int offset = CHIPSET_SEP_BYTE + chipsetIndex;
            if (offset >= configUZ.Length) return false;
            configUZ[offset] = terrain;
            return true;
        }

        /// <summary>
        /// Compute the byte offset into the decompressed map data array for a given
        /// (tileX, tileY) coordinate. Map data format is:
        ///   byte 0 = width, byte 1 = height, then u16 MAR values row-major.
        /// </summary>
        public static int GetMapDataOffset(int width, int tileX, int tileY) =>
            2 + (tileY * width + tileX) * 2;

        /// <summary>
        /// Stage a single-tile MAR edit on a CLONE of <paramref name="mapData"/>.
        /// Returns true and yields the cloned + modified buffer in <paramref name="staged"/>
        /// (with <paramref name="oldMar"/> set to the previous MAR value) when the edit is
        /// a legitimate change. Returns false in all of the following cases:
        ///   - input array is null or too small for the header,
        ///   - <paramref name="tileX"/> / <paramref name="tileY"/> are out of range,
        ///   - the offset computed from (tileX, tileY) falls outside the data buffer,
        ///   - the existing MAR value already equals <paramref name="newMar"/> (no-op).
        ///
        /// On false the original <paramref name="mapData"/> is never mutated, and
        /// <paramref name="staged"/> is set to null. This is the building block both
        /// the manual tile-writer and the click-to-paint flow use; it guarantees the
        /// in-memory cache only ever advances if the entire operation succeeds.
        /// </summary>
        public static bool TryStageMarEdit(
            byte[] mapData,
            int width,
            int height,
            int tileX,
            int tileY,
            ushort newMar,
            out byte[] staged,
            out ushort oldMar)
        {
            staged = null;
            oldMar = 0;

            if (mapData == null || mapData.Length < 2) return false;
            if (width <= 0 || height <= 0) return false;
            if (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height) return false;

            int offset = GetMapDataOffset(width, tileX, tileY);
            if (offset < 0 || offset + 1 >= mapData.Length) return false;

            ushort existing = (ushort)(mapData[offset] | (mapData[offset + 1] << 8));
            if (existing == newMar) return false;

            byte[] clone = (byte[])mapData.Clone();
            clone[offset] = (byte)(newMar & 0xFF);
            clone[offset + 1] = (byte)((newMar >> 8) & 0xFF);

            oldMar = existing;
            staged = clone;
            return true;
        }

        /// <summary>
        /// Stage a full-grid replacement of the decompressed map buffer from a row-major
        /// array of MAR values. Validates that the cache header matches (width, height)
        /// and that mars.Length == width*height. Returns a NEW buffer (header preserved,
        /// all MARs written little-endian row-major); never mutates the input.
        /// Returns false + error on dimension mismatch, null input, or oversize.
        /// PURE — no ROM access.
        /// </summary>
        public static bool TryStageGridEdit(
            byte[] cachedMapData,
            int width,
            int height,
            ushort[] mars,
            out byte[] staged,
            out string error)
        {
            staged = null;
            error = null;

            if (cachedMapData == null)
            {
                error = "cachedMapData is null";
                return false;
            }
            if (width <= 0 || height <= 0)
            {
                error = $"invalid dimensions {width}x{height}";
                return false;
            }
            int needed = 2 + width * height * 2;
            if (cachedMapData.Length < needed)
            {
                error = $"cachedMapData too short ({cachedMapData.Length} < {needed})";
                return false;
            }
            if (cachedMapData[0] != width || cachedMapData[1] != height)
            {
                error = $"map dimension mismatch: header says {cachedMapData[0]}x{cachedMapData[1]}, caller says {width}x{height}";
                return false;
            }
            if (mars == null)
            {
                error = "mars array is null";
                return false;
            }
            if (mars.Length != width * height)
            {
                error = $"mars.Length {mars.Length} != width*height {width * height}";
                return false;
            }

            byte[] clone = (byte[])cachedMapData.Clone();
            for (int i = 0; i < mars.Length; i++)
            {
                int offset = 2 + i * 2;
                clone[offset] = (byte)(mars[i] & 0xFF);
                clone[offset + 1] = (byte)((mars[i] >> 8) & 0xFF);
            }

            staged = clone;
            return true;
        }

        /// <summary>
        /// Read the MAR value at (tileX, tileY) from decompressed map data.
        /// Returns false if coordinates are out of range or the buffer is too small.
        /// </summary>
        public static bool TryReadMar(
            byte[] mapData, int width, int height, int tileX, int tileY, out ushort marValue)
        {
            marValue = 0;
            if (mapData == null || mapData.Length < 2) return false;
            if (width <= 0 || height <= 0) return false;
            if (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height) return false;

            int offset = GetMapDataOffset(width, tileX, tileY);
            if (offset < 0 || offset + 1 >= mapData.Length) return false;

            marValue = (ushort)(mapData[offset] | (mapData[offset + 1] << 8));
            return true;
        }

        // ── Map dimension limits (ported verbatim from WinForms ImageUtilMap.cs) ──
        // Main-map dimensions live only in the decompressed buffer's 2-byte header;
        // the FE engine imposes a minimum playable size and a height-dependent maximum
        // width (taller maps must be narrower to fit the tile budget). These bounds are
        // enforced on resize so the result stays loadable and does not glitch/crash the
        // game. See MapEditorResizeDialogForm.ChangeButton_Click (main-map path).

        /// <summary>Minimum main-map width in 16x16 tiles.</summary>
        public const int MAP_MIN_WIDTH = 15;
        /// <summary>Minimum main-map height in 16x16 tiles.</summary>
        public const int MAP_MIN_HEIGHT = 10;
        /// <summary>Maximum main-map height in 16x16 tiles.</summary>
        public const int MAP_MAX_HEIGHT = 63;

        // Height-indexed maximum width table (index 0 == height 10 .. index 53 == height 63).
        static readonly uint[] MapWidthLimit = new uint[]{
            63, 63, 63, 63, 63, 63, 63, 63, 63, 63, // h=10..19
            63, 63, 63, 63, 63,                     // h=20..24
            62, 60, 58, 56, 54, 52, 49, 48, 47, 46, // h=25..34
            44, 43, 42, 41, 39, 38, 37, 36, 35, 34, // h=35..44
            34, 33, 32, 32, 30, 30, 29, 28, 28, 27, // h=45..54
            27, 26, 26, 25, 25, 24, 24, 23, 23,     // h=55..63
        };

        /// <summary>
        /// Maximum permitted main-map width for a given height (in 16x16 tiles), or 0
        /// when the height itself is out of the valid 10..63 range. Mirrors WinForms
        /// <c>ImageUtilMap.GetLimitMapWidth</c>.
        /// </summary>
        public static uint GetLimitMapWidth(int height)
        {
            if (height < MAP_MIN_HEIGHT) return 0;
            if (height > MAP_MAX_HEIGHT) return 0;
            return MapWidthLimit[height - MAP_MIN_HEIGHT];
        }

        /// <summary>
        /// Build a NEW decompressed map buffer resized from <paramref name="old"/> by the
        /// given per-edge padding (positive grows, negative crops), mirroring WinForms
        /// <c>MapEditorForm.MapSizeChange</c>. The new size is
        /// <c>newW = oldW + left + right</c>, <c>newH = oldH + top + bottom</c>; each old
        /// tile <c>(x,y)</c> is copied to <c>(x+left, y+top)</c> where it lands in-bounds
        /// (out-of-bounds tiles are cropped), and every uncopied cell is set to
        /// <paramref name="fillTile"/> (WinForms fills new rows/cols with tile 0).
        ///
        /// <para>Validates the result against the FE main-map limits exactly like WinForms
        /// (<see cref="MAP_MIN_WIDTH"/>x<see cref="MAP_MIN_HEIGHT"/> minimum and
        /// <see cref="GetLimitMapWidth"/> maximum); on an invalid request it returns false
        /// with an explanatory <paramref name="error"/> and never clamps.</para>
        ///
        /// PURE — no ROM access, never mutates <paramref name="old"/>.
        /// </summary>
        public static bool BuildResizedMapData(
            byte[] old, int oldW, int oldH,
            int top, int left, int right, int bottom,
            ushort fillTile,
            out byte[] resized, out int newW, out int newH, out string error)
        {
            resized = null;
            newW = 0;
            newH = 0;
            error = null;

            if (old == null)
            {
                error = "map data is null";
                return false;
            }
            if (oldW <= 0 || oldH <= 0)
            {
                error = $"invalid source dimensions {oldW}x{oldH}";
                return false;
            }
            int srcNeeded = 2 + oldW * oldH * 2;
            if (old.Length < srcNeeded)
            {
                error = $"map data too short ({old.Length} < {srcNeeded})";
                return false;
            }

            int w = oldW + left + right;
            int h = oldH + top + bottom;

            if (w < MAP_MIN_WIDTH || h < MAP_MIN_HEIGHT)
            {
                error = $"map cannot be smaller than {MAP_MIN_WIDTH}x{MAP_MIN_HEIGHT} tiles (requested {w}x{h})";
                return false;
            }
            uint limitWidth = GetLimitMapWidth(h);
            if (limitWidth == 0)
            {
                error = $"map height {h} is out of range ({MAP_MIN_HEIGHT}..{MAP_MAX_HEIGHT})";
                return false;
            }
            if (w > limitWidth)
            {
                error = $"map is too wide: width {w} exceeds the maximum {limitWidth} for height {h}";
                return false;
            }

            byte[] dst = new byte[2 + w * h * 2];
            dst[0] = (byte)w;
            dst[1] = (byte)h;

            // Pre-fill every tile with fillTile, then overwrite cells covered by the copy.
            if (fillTile != 0)
            {
                for (int i = 0; i < w * h; i++)
                {
                    int d = 2 + i * 2;
                    dst[d] = (byte)(fillTile & 0xFF);
                    dst[d + 1] = (byte)((fillTile >> 8) & 0xFF);
                }
            }

            for (int oy = 0; oy < oldH; oy++)
            {
                int ny = oy + top;
                if (ny < 0 || ny >= h) continue;
                for (int ox = 0; ox < oldW; ox++)
                {
                    int nx = ox + left;
                    if (nx < 0 || nx >= w) continue;
                    int so = 2 + (oy * oldW + ox) * 2;
                    int dOff = 2 + (ny * w + nx) * 2;
                    dst[dOff] = old[so];
                    dst[dOff + 1] = old[so + 1];
                }
            }

            resized = dst;
            newW = w;
            newH = h;
            return true;
        }

        /// <summary>
        /// Render a single 4bpp 8x8 tile from the OBJ tile graphics into an
        /// RGBA8888 destination buffer at (destX, destY) inside an image of
        /// <paramref name="destWidth"/> pixels wide. Mirrors the inner loop of
        /// <c>MapEditorViewModel.RenderTile4bpp</c>; kept here so the palette
        /// renderer doesn't need a reference to the VM.
        /// </summary>
        public static void RenderTile4bpp(
            byte[] tileData, int tileIndex,
            byte[] palette, int palIndex,
            byte[] dest, int destWidth,
            int destX, int destY,
            bool hFlip, bool vFlip)
        {
            const int bytesPerTile = 32; // 8x8 4bpp
            int tileOffset = tileIndex * bytesPerTile;
            if (tileData == null || palette == null || dest == null) return;
            if (tileOffset + bytesPerTile > tileData.Length) return;

            int palOffset = palIndex * 16 * 2;

            for (int py = 0; py < 8; py++)
            {
                int srcY = vFlip ? (7 - py) : py;
                for (int px = 0; px < 8; px++)
                {
                    int srcX = hFlip ? (7 - px) : px;

                    int bytePos = tileOffset + srcY * 4 + srcX / 2;
                    if (bytePos >= tileData.Length) continue;

                    byte b = tileData[bytePos];
                    int colorIndex = (srcX % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                    int palByteOffset = palOffset + colorIndex * 2;
                    if (palByteOffset + 2 > palette.Length) continue;

                    ushort gbaColor = (ushort)(palette[palByteOffset] | (palette[palByteOffset + 1] << 8));

                    byte r = (byte)(((gbaColor >> 0) & 0x1F) << 3);
                    byte g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                    byte bl = (byte)(((gbaColor >> 10) & 0x1F) << 3);
                    byte a = (byte)(colorIndex == 0 ? 0 : 255);

                    int dx = destX + px;
                    int dy = destY + py;
                    if (dx < 0 || dy < 0 || dx >= destWidth) continue;

                    int idx = (dy * destWidth + dx) * 4;
                    if (idx + 3 >= dest.Length) continue;

                    dest[idx + 0] = r;
                    dest[idx + 1] = g;
                    dest[idx + 2] = bl;
                    dest[idx + 3] = a;
                }
            }
        }

        /// <summary>
        /// Render a strip of 4bpp 8x8 tiles into an RGBA8888 image, <paramref name="columns"/>
        /// tiles wide and <c>ceil(tileCount / columns)</c> tiles tall. Uses
        /// <paramref name="paletteIndex"/> from a flat <paramref name="palette"/> block
        /// (16 colors x 2 bytes per palette).
        ///
        /// <para>Returns null + zero dims when:</para>
        /// <list type="bullet">
        ///   <item><paramref name="tileData"/> is null or smaller than one tile (32 bytes),</item>
        ///   <item><paramref name="palette"/> is null,</item>
        ///   <item>palette block for <paramref name="paletteIndex"/> is out of range
        ///       (i.e. <c>palette.Length &lt; (paletteIndex + 1) * 32</c>).</item>
        /// </list>
        /// </summary>
        public static byte[] RenderTileSheet4bpp(
            byte[] tileData,
            byte[] palette,
            int paletteIndex,
            int columns,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            const int bytesPerTile = 32;
            const int tileSize = 8;
            if (tileData == null || tileData.Length < bytesPerTile) return null;
            if (palette == null) return null;
            if (paletteIndex < 0) return null;
            if (palette.Length < (paletteIndex + 1) * 16 * 2) return null;
            if (columns <= 0) columns = 1;

            int tileCount = tileData.Length / bytesPerTile;
            int rows = (tileCount + columns - 1) / columns;
            width = columns * tileSize;
            height = rows * tileSize;

            byte[] dest = new byte[width * height * 4];
            for (int t = 0; t < tileCount; t++)
            {
                int destX = (t % columns) * tileSize;
                int destY = (t / columns) * tileSize;
                RenderTile4bpp(tileData, t, palette, paletteIndex, dest, width, destX, destY, false, false);
            }
            return dest;
        }

        /// <summary>
        /// Render one chipset (16x16 pixels = four 8x8 sub-tiles) into a
        /// destination RGBA buffer at (destX, destY).
        /// </summary>
        /// <param name="marValue">The raw MAR value for this chipset (use
        /// <see cref="ChipsetIndexToMar"/> if you have a palette index).</param>
        public static void RenderChipsetIntoBuffer(
            int marValue,
            byte[] configUZ,
            byte[] objData,
            byte[] palette,
            byte[] dest, int destWidth,
            int destX, int destY)
        {
            int tsaBase = marValue << 1; // bytes
            if (configUZ == null || tsaBase + 7 >= configUZ.Length)
                return; // leave blank (transparent)

            for (int sub = 0; sub < 4; sub++)
            {
                int tsaOff = tsaBase + sub * 2;
                ushort tsa = (ushort)(configUZ[tsaOff] | (configUZ[tsaOff + 1] << 8));
                int tileIdx = tsa & 0x3FF;
                bool hF = (tsa & 0x400) != 0;
                bool vF = (tsa & 0x800) != 0;
                int palIdx = (tsa >> 12) & 0xF;

                int sx = destX + (sub % 2) * 8;
                int sy = destY + (sub / 2) * 8;
                RenderTile4bpp(objData, tileIdx, palette, palIdx, dest, destWidth, sx, sy, hF, vF);
            }
        }

        /// <summary>
        /// Render the full chipset palette as an RGBA grid <paramref name="columns"/>
        /// chipsets wide × <c>ceil(CHIPSET_COUNT / columns)</c> rows tall. Each chipset
        /// is 16x16 pixels.
        /// </summary>
        /// <returns>RGBA8888 pixel buffer of size width*height*4 bytes.</returns>
        public static byte[] RenderChipsetPalette(
            byte[] objData,
            byte[] configUZ,
            byte[] palette,
            out int paletteWidth,
            out int paletteHeight,
            int columns = PALETTE_COLUMNS,
            int chipsetCount = CHIPSET_COUNT)
        {
            if (columns <= 0) columns = PALETTE_COLUMNS;
            if (chipsetCount <= 0) chipsetCount = CHIPSET_COUNT;

            int rows = (chipsetCount + columns - 1) / columns;
            paletteWidth = columns * CHIPSET_PIXEL_SIZE;
            paletteHeight = rows * CHIPSET_PIXEL_SIZE;

            byte[] pixels = new byte[paletteWidth * paletteHeight * 4];

            for (int i = 0; i < chipsetCount; i++)
            {
                int cx = (i % columns) * CHIPSET_PIXEL_SIZE;
                int cy = (i / columns) * CHIPSET_PIXEL_SIZE;
                int marValue = ChipsetIndexToMar(i);
                RenderChipsetIntoBuffer(marValue, configUZ, objData, palette, pixels, paletteWidth, cx, cy);
            }

            return pixels;
        }

        /// <summary>
        /// Convert pixel coordinates inside the palette grid into a chipset
        /// palette index. Returns -1 if the coordinates are outside the grid
        /// or beyond <paramref name="chipsetCount"/> chipsets.
        /// </summary>
        public static int PixelToChipsetIndex(
            int px, int py, int columns = PALETTE_COLUMNS, int chipsetCount = CHIPSET_COUNT)
        {
            if (px < 0 || py < 0) return -1;
            int cx = px / CHIPSET_PIXEL_SIZE;
            int cy = py / CHIPSET_PIXEL_SIZE;
            if (cx < 0 || cx >= columns) return -1;
            int idx = cy * columns + cx;
            if (idx < 0 || idx >= chipsetCount) return -1;
            return idx;
        }

        /// <summary>Pixel column of a chipset in the palette grid.</summary>
        public static int ChipsetIndexToPixelX(int chipsetIndex, int columns = PALETTE_COLUMNS) =>
            (chipsetIndex % columns) * CHIPSET_PIXEL_SIZE;

        /// <summary>Pixel row of a chipset in the palette grid.</summary>
        public static int ChipsetIndexToPixelY(int chipsetIndex, int columns = PALETTE_COLUMNS) =>
            (chipsetIndex / columns) * CHIPSET_PIXEL_SIZE;
    }
}
