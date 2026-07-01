using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="MapEditorTilesetCore"/> — the cross-platform helpers
    /// powering the Visual Map Editor tile-picker (first slice of #658).
    /// </summary>
    public class MapEditorTilesetCoreTests
    {
        // --------- Chipset/MAR conversion --------------------------------------

        [Theory]
        [InlineData(0, 0x0000)]
        [InlineData(1, 0x0004)]
        [InlineData(5, 0x0014)]
        [InlineData(7, 0x001C)]
        [InlineData(0x3FF, 0x0FFC)]
        public void ChipsetIndexToMar_KnownValues(int chipsetIndex, ushort expectedMar)
        {
            Assert.Equal(expectedMar, MapEditorTilesetCore.ChipsetIndexToMar(chipsetIndex));
        }

        [Theory]
        [InlineData((ushort)0x0000, 0)]
        [InlineData((ushort)0x0004, 1)]
        [InlineData((ushort)0x0014, 5)]
        [InlineData((ushort)0x03FC, 0xFF)]
        [InlineData((ushort)0x0FFC, 0x3FF)]
        public void MarToChipsetIndex_KnownValues(ushort marValue, int expectedIndex)
        {
            Assert.Equal(expectedIndex, MapEditorTilesetCore.MarToChipsetIndex(marValue));
        }

        [Fact]
        public void Chipset_ToMar_RoundTrip()
        {
            foreach (int i in new[] { 0, 1, 7, 31, 32, 256, 511, 512, 0x3FE, 0x3FF })
            {
                ushort mar = MapEditorTilesetCore.ChipsetIndexToMar(i);
                int back = MapEditorTilesetCore.MarToChipsetIndex(mar);
                Assert.Equal(i, back);
            }
        }

        // --------- Terrain lookup ----------------------------------------------

        static byte[] BuildConfigUZ()
        {
            // Build a configUZ buffer with TSA region (zeroed for these terrain tests)
            // followed by terrain bytes from offset CHIPSET_SEP_BYTE on.
            byte[] cfg = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 16];
            // At terrain offset 0 from CHIPSET_SEP_BYTE: terrain_data1=0x11, terrain_data2=0x22
            cfg[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 0] = 0x11;
            cfg[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 1] = 0x22;
            // At terrain offset +2: terrain_data1=0x33, terrain_data2=0x44
            cfg[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 2] = 0x33;
            cfg[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 3] = 0x44;
            return cfg;
        }

        [Fact]
        public void GetTerrainData_FromMar_FirstBlock_BothBytes()
        {
            byte[] cfg = BuildConfigUZ();
            // MAR values 0..3 (i.e. chipsetIndex 0): (m>>3)*2 == 0 → terrain block 0,
            // (m & 0x4) == 0 → first byte (0x11).
            Assert.Equal(0x11u, MapEditorTilesetCore.GetTerrainDataFromMar(0x00, cfg));
            Assert.Equal(0x11u, MapEditorTilesetCore.GetTerrainDataFromMar(0x03, cfg));
            // MAR values 4..7 (i.e. chipsetIndex 1): (m>>3)*2 == 0 → same block,
            // (m & 0x4) != 0 → second byte (0x22).
            Assert.Equal(0x22u, MapEditorTilesetCore.GetTerrainDataFromMar(0x04, cfg));
            Assert.Equal(0x22u, MapEditorTilesetCore.GetTerrainDataFromMar(0x07, cfg));
        }

        [Fact]
        public void GetTerrainData_ChipsetIndex1_SelectsSecondTerrainByte()
        {
            // Direct port of the v3 review case: chipsetIndex=1 → marValue=4 → second byte.
            byte[] cfg = BuildConfigUZ();
            Assert.Equal(0x22u, MapEditorTilesetCore.GetTerrainDataFromChipset(1, cfg));
            Assert.Equal(0x11u, MapEditorTilesetCore.GetTerrainDataFromChipset(0, cfg));
        }

        [Fact]
        public void GetTerrainData_OOR_ReturnsNotFound()
        {
            // Empty configUZ → OOR
            Assert.Equal(U.NOT_FOUND, MapEditorTilesetCore.GetTerrainDataFromMar(0, new byte[0]));
            // null configUZ → NOT_FOUND
            Assert.Equal(U.NOT_FOUND, MapEditorTilesetCore.GetTerrainDataFromMar(0, null));
        }

        [Fact]
        public void GetTerrainData_PalettiseFromIndexVsMar_Consistent()
        {
            byte[] cfg = BuildConfigUZ();
            foreach (int i in new[] { 0, 1, 2, 3, 4, 5 })
            {
                uint fromIdx = MapEditorTilesetCore.GetTerrainDataFromChipset(i, cfg);
                uint fromMar = MapEditorTilesetCore.GetTerrainDataFromMar(MapEditorTilesetCore.ChipsetIndexToMar(i), cfg);
                Assert.Equal(fromMar, fromIdx);
            }
        }

        // --------- Map-data offset / read --------------------------------------

        [Fact]
        public void GetMapDataOffset_KnownValues()
        {
            // width=3: offset(2,1) = 2 + (1*3+2)*2 = 2 + 10 = 12
            Assert.Equal(12, MapEditorTilesetCore.GetMapDataOffset(3, 2, 1));
            // width=2: offset(0,0) = 2
            Assert.Equal(2, MapEditorTilesetCore.GetMapDataOffset(2, 0, 0));
        }

        [Fact]
        public void TryReadMar_RoundTrip()
        {
            byte[] mapData = new byte[2 + 2 * 2 * 2];
            mapData[0] = 2; mapData[1] = 2;
            // Tile (1,1) = 0x1234
            int off = MapEditorTilesetCore.GetMapDataOffset(2, 1, 1);
            mapData[off] = 0x34; mapData[off + 1] = 0x12;
            Assert.True(MapEditorTilesetCore.TryReadMar(mapData, 2, 2, 1, 1, out ushort mar));
            Assert.Equal(0x1234, mar);
        }

        [Fact]
        public void TryReadMar_OutOfRange_ReturnsFalse()
        {
            byte[] mapData = new byte[2 + 4];
            mapData[0] = 1; mapData[1] = 1;
            Assert.False(MapEditorTilesetCore.TryReadMar(mapData, 1, 1, 5, 5, out _));
            Assert.False(MapEditorTilesetCore.TryReadMar(mapData, 1, 1, -1, 0, out _));
            Assert.False(MapEditorTilesetCore.TryReadMar(null, 1, 1, 0, 0, out _));
        }

        // --------- TryStageMarEdit — staged-clone semantics --------------------

        static byte[] Build3x2Map()
        {
            // width=3, height=2; tile values 0x0001..0x0006 row-major.
            byte[] map = new byte[2 + 3 * 2 * 2];
            map[0] = 3; map[1] = 2;
            for (int i = 0; i < 6; i++)
            {
                int off = 2 + i * 2;
                ushort v = (ushort)(i + 1);
                map[off] = (byte)(v & 0xFF);
                map[off + 1] = (byte)((v >> 8) & 0xFF);
            }
            return map;
        }

        [Fact]
        public void TryStageMarEdit_Success_ReturnsClone_InputUnchanged()
        {
            byte[] map = Build3x2Map();
            byte[] before = (byte[])map.Clone();

            bool ok = MapEditorTilesetCore.TryStageMarEdit(map, 3, 2, 1, 0, 0x00FF,
                out byte[] staged, out ushort oldMar);

            Assert.True(ok);
            Assert.NotNull(staged);
            Assert.NotSame(map, staged);
            Assert.Equal((ushort)0x0002, oldMar);

            // Input map untouched
            Assert.Equal(before, map);

            // Staged differs ONLY at the target offset
            int off = MapEditorTilesetCore.GetMapDataOffset(3, 1, 0);
            Assert.Equal(0xFF, staged[off]);
            Assert.Equal(0x00, staged[off + 1]);
            for (int i = 0; i < map.Length; i++)
            {
                if (i == off || i == off + 1) continue;
                Assert.Equal(map[i], staged[i]);
            }
        }

        [Fact]
        public void TryStageMarEdit_SameValue_IsNoOp()
        {
            byte[] map = Build3x2Map();
            // Tile (0,0) is already 0x0001 — write same value.
            bool ok = MapEditorTilesetCore.TryStageMarEdit(map, 3, 2, 0, 0, 0x0001,
                out byte[] staged, out ushort oldMar);
            Assert.False(ok);
            Assert.Null(staged);
            Assert.Equal((ushort)0, oldMar);
        }

        [Fact]
        public void TryStageMarEdit_OOR_ReturnsFalse()
        {
            byte[] map = Build3x2Map();
            Assert.False(MapEditorTilesetCore.TryStageMarEdit(map, 3, 2, 10, 0, 0,
                out byte[] s1, out _));
            Assert.Null(s1);
            Assert.False(MapEditorTilesetCore.TryStageMarEdit(map, 3, 2, 0, -1, 0,
                out byte[] s2, out _));
            Assert.Null(s2);
            Assert.False(MapEditorTilesetCore.TryStageMarEdit(null, 3, 2, 0, 0, 0,
                out byte[] s3, out _));
            Assert.Null(s3);
            // Width/height invalid
            Assert.False(MapEditorTilesetCore.TryStageMarEdit(map, 0, 2, 0, 0, 0,
                out byte[] s4, out _));
            Assert.Null(s4);
        }

        // --------- Palette geometry --------------------------------------------

        [Fact]
        public void PixelToChipsetIndex_KnownValues()
        {
            // Default columns=32, chipsetCount=1024.
            // (0,0) → index 0
            Assert.Equal(0, MapEditorTilesetCore.PixelToChipsetIndex(0, 0));
            // (15,15) → still chipset 0 (within first 16x16 cell)
            Assert.Equal(0, MapEditorTilesetCore.PixelToChipsetIndex(15, 15));
            // (16,0) → index 1
            Assert.Equal(1, MapEditorTilesetCore.PixelToChipsetIndex(16, 0));
            // (0,16) → index 32 (next row)
            Assert.Equal(32, MapEditorTilesetCore.PixelToChipsetIndex(0, 16));
            // Right of grid → -1
            Assert.Equal(-1, MapEditorTilesetCore.PixelToChipsetIndex(32 * 16, 0));
            // Negative → -1
            Assert.Equal(-1, MapEditorTilesetCore.PixelToChipsetIndex(-1, 0));
        }

        [Fact]
        public void ChipsetIndexToPixel_KnownValues()
        {
            Assert.Equal(0, MapEditorTilesetCore.ChipsetIndexToPixelX(0));
            Assert.Equal(0, MapEditorTilesetCore.ChipsetIndexToPixelY(0));
            Assert.Equal(16, MapEditorTilesetCore.ChipsetIndexToPixelX(1));
            Assert.Equal(0, MapEditorTilesetCore.ChipsetIndexToPixelY(1));
            Assert.Equal(0, MapEditorTilesetCore.ChipsetIndexToPixelX(32));
            Assert.Equal(16, MapEditorTilesetCore.ChipsetIndexToPixelY(32));
        }

        [Fact]
        public void RenderChipsetPalette_Dimensions()
        {
            // Use minimal buffers — we only care about output dimensions here.
            byte[] obj = new byte[32 * 16]; // some 4bpp tiles
            byte[] cfg = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE + 256];
            byte[] pal = new byte[512];

            byte[] rgba = MapEditorTilesetCore.RenderChipsetPalette(obj, cfg, pal,
                out int pw, out int ph);
            Assert.NotNull(rgba);
            // 32 cols * 16 px = 512 wide; 1024/32 = 32 rows * 16 px = 512 tall.
            Assert.Equal(32 * 16, pw);
            Assert.Equal(32 * 16, ph);
            Assert.Equal(pw * ph * 4, rgba.Length);
        }

        [Fact]
        public void RenderChipsetPalette_RoundsUpRowCount()
        {
            byte[] obj = new byte[64];
            byte[] cfg = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE];
            byte[] pal = new byte[512];

            // Custom: 5 columns, 11 chipsets → 3 rows (ceil(11/5))
            byte[] rgba = MapEditorTilesetCore.RenderChipsetPalette(obj, cfg, pal,
                out int pw, out int ph, columns: 5, chipsetCount: 11);
            Assert.Equal(5 * 16, pw);
            Assert.Equal(3 * 16, ph);
            Assert.NotNull(rgba);
        }

        // --------- RenderTileSheet4bpp (Map Chip Preview #670) -----------------

        [Fact]
        public void RenderTileSheet4bpp_NullForEmptyTileData()
        {
            var result = MapEditorTilesetCore.RenderTileSheet4bpp(null, new byte[32], 0, 32, out int w, out int h);
            Assert.Null(result);
            Assert.Equal(0, w);
            Assert.Equal(0, h);

            result = MapEditorTilesetCore.RenderTileSheet4bpp(new byte[10], new byte[32], 0, 32, out w, out h);
            Assert.Null(result);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
        }

        [Fact]
        public void RenderTileSheet4bpp_NullForNullPalette()
        {
            var result = MapEditorTilesetCore.RenderTileSheet4bpp(new byte[32], null, 0, 32, out int w, out int h);
            Assert.Null(result);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
        }

        [Fact]
        public void RenderTileSheet4bpp_UsesSelectedPaletteIndex()
        {
            // One 4bpp tile where pixel (0,0) uses color index 1.
            // 4bpp layout: each row = 4 bytes (8 nibbles); the LOW nibble of
            // the first byte is the color index of pixel (0,0).
            byte[] tileData = new byte[32];
            tileData[0] = 0x01; // pixel(0,0)=1, pixel(1,0)=0

            // 16 palettes × 16 colors × 2 bytes = 512 bytes
            byte[] palette = new byte[16 * 16 * 2];
            ushort red = (ushort)(31);
            ushort green = (ushort)(31 << 5);
            ushort blue = (ushort)(31 << 10);
            ushort white = (ushort)(31 | (31 << 5) | (31 << 10));

            void SetPalette(int palIdx, int colorIdx, ushort c)
            {
                int off = palIdx * 16 * 2 + colorIdx * 2;
                palette[off] = (byte)(c & 0xFF);
                palette[off + 1] = (byte)((c >> 8) & 0xFF);
            }
            SetPalette(0, 1, red);
            SetPalette(1, 1, green);
            SetPalette(2, 1, blue);
            SetPalette(3, 1, white);

            // paletteIndex=2 → pixel(0,0) should be blue.
            var result = MapEditorTilesetCore.RenderTileSheet4bpp(tileData, palette, 2, 32, out int w, out int h);
            Assert.NotNull(result);
            Assert.True(w >= 8);
            Assert.True(h >= 8);
            Assert.Equal((byte)0, result[0]);          // R
            Assert.Equal((byte)0, result[1]);          // G
            Assert.Equal((byte)(31 << 3), result[2]);  // B = 248
            Assert.Equal((byte)255, result[3]);        // alpha (index != 0)

            // paletteIndex=1 → pixel(0,0) should be green.
            var resultG = MapEditorTilesetCore.RenderTileSheet4bpp(tileData, palette, 1, 32, out _, out _);
            Assert.Equal((byte)0, resultG[0]);
            Assert.Equal((byte)(31 << 3), resultG[1]);
            Assert.Equal((byte)0, resultG[2]);
            Assert.Equal((byte)255, resultG[3]);
        }

        [Fact]
        public void RenderTileSheet4bpp_NullForOutOfRangePaletteIndex()
        {
            // 32-byte palette = only palette index 0 fits. Index 1 must be rejected.
            var result = MapEditorTilesetCore.RenderTileSheet4bpp(new byte[32], new byte[32], 1, 32, out int w, out int h);
            Assert.Null(result);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
        }

        // --------- SetTerrainForChipset (#671) ---------------------------------

        /// <summary>
        /// Round-trip: writing terrain via SetTerrainForChipset must be
        /// readable by <see cref="MapEditorTilesetCore.GetTerrainDataFromChipset"/>
        /// for the same chipset index. Verifies the byte-offset reduction
        /// (CHIPSET_SEP_BYTE + chipsetIndex) matches WF's MAR-based read.
        /// </summary>
        [Theory]
        [InlineData(0, 0x7F)]
        [InlineData(1, 0x42)]
        [InlineData(2, 0x21)]
        [InlineData(7, 0x80)]
        [InlineData(8, 0xAA)]
        [InlineData(1023, 0xFF)]
        public void SetTerrainForChipset_RoundTripsThroughGetTerrain(int chipsetIndex, byte terrain)
        {
            byte[] buf = new byte[0x2400];
            Assert.True(MapEditorTilesetCore.SetTerrainForChipset(chipsetIndex, terrain, buf));
            Assert.Equal((uint)terrain, MapEditorTilesetCore.GetTerrainDataFromChipset(chipsetIndex, buf));
            // Confirm the actual byte offset matches the algebraic reduction.
            Assert.Equal(terrain, buf[MapEditorTilesetCore.CHIPSET_SEP_BYTE + chipsetIndex]);
        }

        /// <summary>
        /// Semantic bound: chipset index must be in [0, CHIPSET_COUNT).
        /// 1024 (one past the end) lands inside the terrain region by raw
        /// byte arithmetic if the buffer is long enough, so a buffer-only
        /// bound would silently accept it. Confirm we reject it.
        /// </summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(1024)]
        [InlineData(2048)]
        public void SetTerrainForChipset_RejectsOutOfRangeIndex(int chipsetIndex)
        {
            byte[] buf = new byte[0x4000]; // big enough that raw byte math could "succeed"
            byte original = buf[MapEditorTilesetCore.CHIPSET_SEP_BYTE + Math.Max(0, chipsetIndex)];
            Assert.False(MapEditorTilesetCore.SetTerrainForChipset(chipsetIndex, 0x55, buf));
            // The buffer must NOT have been mutated.
            if (chipsetIndex >= 0)
                Assert.Equal(original, buf[MapEditorTilesetCore.CHIPSET_SEP_BYTE + chipsetIndex]);
        }

        /// <summary>
        /// Buffer-size bound: even an in-range index must be rejected when
        /// the buffer is too small to hold the terrain byte at that offset.
        /// </summary>
        [Fact]
        public void SetTerrainForChipset_RejectsTooSmallBuffer()
        {
            byte[] buf = new byte[MapEditorTilesetCore.CHIPSET_SEP_BYTE]; // no room for terrain[0]
            Assert.False(MapEditorTilesetCore.SetTerrainForChipset(0, 0x55, buf));
        }

        // =====================================================================
        // TryStageGridEdit tests (#1382)
        // =====================================================================

        /// <summary>Helper to build a minimal 2x2 map buffer.</summary>
        static byte[] Make2x2Buffer(ushort v00 = 0x0001, ushort v10 = 0x0002,
                                     ushort v01 = 0x0003, ushort v11 = 0x0004)
        {
            byte[] buf = new byte[2 + 4 * 2];
            buf[0] = 2; buf[1] = 2;
            buf[2] = (byte)(v00 & 0xFF); buf[3] = (byte)(v00 >> 8);
            buf[4] = (byte)(v10 & 0xFF); buf[5] = (byte)(v10 >> 8);
            buf[6] = (byte)(v01 & 0xFF); buf[7] = (byte)(v01 >> 8);
            buf[8] = (byte)(v11 & 0xFF); buf[9] = (byte)(v11 >> 8);
            return buf;
        }

        [Fact]
        public void TryStageGridEdit_Success_HeaderPreservedAndMarsWritten()
        {
            byte[] original = Make2x2Buffer();
            ushort[] newMars = new ushort[] { 0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD };

            bool ok = MapEditorTilesetCore.TryStageGridEdit(original, 2, 2, newMars, out byte[] staged, out string err);

            Assert.True(ok, err);
            Assert.NotNull(staged);
            // Header preserved
            Assert.Equal(2, staged[0]);
            Assert.Equal(2, staged[1]);
            // MARs written little-endian
            Assert.Equal(0xAA, staged[2]); Assert.Equal(0xAA, staged[3]); // 0xAAAA
            Assert.Equal(0xBB, staged[4]); Assert.Equal(0xBB, staged[5]); // 0xBBBB
            Assert.Equal(0xCC, staged[6]); Assert.Equal(0xCC, staged[7]); // 0xCCCC
            Assert.Equal(0xDD, staged[8]); Assert.Equal(0xDD, staged[9]); // 0xDDDD
        }

        [Fact]
        public void TryStageGridEdit_InputNotMutated()
        {
            byte[] original = Make2x2Buffer();
            byte[] originalCopy = (byte[])original.Clone();
            ushort[] newMars = new ushort[] { 0x1234, 0x5678, 0xABCD, 0xEF01 };

            MapEditorTilesetCore.TryStageGridEdit(original, 2, 2, newMars, out _, out _);

            // Original must be byte-identical after the call.
            Assert.Equal(originalCopy, original);
        }

        [Fact]
        public void TryStageGridEdit_DimensionMismatch_ReturnsError()
        {
            // Build a buffer that is large enough to hold 3x3 MAR data (needed bytes = 2+9*2=20)
            // but set the header to say 2x2 — caller passes width=3 so the header check fires.
            byte[] buf = new byte[2 + 9 * 2];
            buf[0] = 2; buf[1] = 2; // header says 2x2
            ushort[] mars = new ushort[9]; // 3x3 grid

            bool ok = MapEditorTilesetCore.TryStageGridEdit(buf, 3, 3, mars, out byte[] staged, out string err);

            Assert.False(ok);
            Assert.Null(staged);
            Assert.NotNull(err);
            Assert.Contains("mismatch", err, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryStageGridEdit_MarsWrongLength_ReturnsError()
        {
            byte[] buf = Make2x2Buffer();
            ushort[] mars = new ushort[3]; // should be 4

            bool ok = MapEditorTilesetCore.TryStageGridEdit(buf, 2, 2, mars, out byte[] staged, out string err);

            Assert.False(ok);
            Assert.Null(staged);
            Assert.NotNull(err);
        }

        [Fact]
        public void TryStageGridEdit_NullInput_ReturnsError()
        {
            bool ok = MapEditorTilesetCore.TryStageGridEdit(null, 2, 2, new ushort[4], out byte[] staged, out string err);

            Assert.False(ok);
            Assert.Null(staged);
            Assert.NotNull(err);
        }

        [Fact]
        public void TryStageGridEdit_NullMars_ReturnsError()
        {
            byte[] buf = Make2x2Buffer();

            bool ok = MapEditorTilesetCore.TryStageGridEdit(buf, 2, 2, null, out byte[] staged, out string err);

            Assert.False(ok);
            Assert.Null(staged);
            Assert.NotNull(err);
        }

        [Fact]
        public void TryStageGridEdit_TrailingBytesPreserved()
        {
            // Buffer with extra trailing bytes beyond the grid area — they should remain intact.
            byte[] buf = new byte[2 + 4 * 2 + 4]; // 4 extra bytes
            buf[0] = 2; buf[1] = 2;
            buf[10] = 0xDE; buf[11] = 0xAD; buf[12] = 0xBE; buf[13] = 0xEF;
            ushort[] mars = new ushort[4];

            bool ok = MapEditorTilesetCore.TryStageGridEdit(buf, 2, 2, mars, out byte[] staged, out string err);

            Assert.True(ok, err);
            Assert.Equal(buf.Length, staged.Length);
            Assert.Equal(0xDE, staged[10]);
            Assert.Equal(0xAD, staged[11]);
            Assert.Equal(0xBE, staged[12]);
            Assert.Equal(0xEF, staged[13]);
        }

        // --------- BuildResizedMapData / GetLimitMapWidth (#1735) ----------------

        [Theory]
        [InlineData(10, 63u)]
        [InlineData(24, 63u)]
        [InlineData(25, 62u)]
        [InlineData(30, 52u)]
        [InlineData(44, 34u)]
        [InlineData(63, 23u)]
        public void GetLimitMapWidth_KnownValues(int height, uint expected)
        {
            Assert.Equal(expected, MapEditorTilesetCore.GetLimitMapWidth(height));
        }

        [Theory]
        [InlineData(9)]
        [InlineData(64)]
        [InlineData(0)]
        [InlineData(-1)]
        public void GetLimitMapWidth_OutOfRange_ReturnsZero(int height)
        {
            Assert.Equal(0u, MapEditorTilesetCore.GetLimitMapWidth(height));
        }

        // Build a map whose every tile encodes its own row-major index (0x1000 | idx)
        // so a resize can be verified positionally. idx stays well below 0x1000 for all
        // test sizes used here, so the OR never collides.
        static byte[] BuildUniqueMap(int w, int h)
        {
            byte[] map = new byte[2 + w * h * 2];
            map[0] = (byte)w; map[1] = (byte)h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int off = 2 + (y * w + x) * 2;
                    ushort v = (ushort)(0x1000 | (y * w + x));
                    map[off] = (byte)(v & 0xFF);
                    map[off + 1] = (byte)((v >> 8) & 0xFF);
                }
            return map;
        }

        static ushort ReadTileAt(byte[] map, int w, int x, int y)
        {
            int off = 2 + (y * w + x) * 2;
            return (ushort)(map[off] | (map[off + 1] << 8));
        }

        [Fact]
        public void BuildResizedMapData_GrowRight_CopiesAndFills()
        {
            byte[] src = BuildUniqueMap(15, 10);
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 0, 0, 5, 0, 0,
                out byte[] dst, out int nw, out int nh, out string err);
            Assert.True(ok, err);
            Assert.Equal(20, nw);
            Assert.Equal(10, nh);
            Assert.Equal(20, dst[0]);
            Assert.Equal(10, dst[1]);
            Assert.Equal(2 + 20 * 10 * 2, dst.Length);
            // Original tiles preserved in place
            Assert.Equal(ReadTileAt(src, 15, 14, 9), ReadTileAt(dst, 20, 14, 9));
            Assert.Equal(ReadTileAt(src, 15, 0, 0), ReadTileAt(dst, 20, 0, 0));
            // New right columns are fill (0)
            Assert.Equal(0, ReadTileAt(dst, 20, 15, 0));
            Assert.Equal(0, ReadTileAt(dst, 20, 19, 9));
        }

        [Fact]
        public void BuildResizedMapData_GrowTopLeft_ShiftsContent()
        {
            byte[] src = BuildUniqueMap(15, 10);
            // add 3 rows on top, 2 cols on left → old (0,0) lands at (2,3)
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 3, 2, 0, 0, 0,
                out byte[] dst, out int nw, out int nh, out string err);
            Assert.True(ok, err);
            Assert.Equal(17, nw);
            Assert.Equal(13, nh);
            Assert.Equal(ReadTileAt(src, 15, 0, 0), ReadTileAt(dst, 17, 2, 3));
            Assert.Equal(ReadTileAt(src, 15, 14, 9), ReadTileAt(dst, 17, 16, 12));
            // top-left new area is fill
            Assert.Equal(0, ReadTileAt(dst, 17, 0, 0));
            Assert.Equal(0, ReadTileAt(dst, 17, 1, 2));
        }

        [Fact]
        public void BuildResizedMapData_FillTileNonZero_UsedForNewCells()
        {
            byte[] src = BuildUniqueMap(15, 10);
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 0, 0, 1, 0, 0x0ABC,
                out byte[] dst, out int nw, out int nh, out string err);
            Assert.True(ok, err);
            Assert.Equal(0x0ABC, ReadTileAt(dst, 16, 15, 0));
            Assert.Equal(0x0ABC, ReadTileAt(dst, 16, 15, 9));
            Assert.Equal(ReadTileAt(src, 15, 0, 0), ReadTileAt(dst, 16, 0, 0));
        }

        [Fact]
        public void BuildResizedMapData_Crop_DropsOutOfBoundsTiles()
        {
            // 20x15 map, crop 5 off right and 5 off bottom → 15x10
            byte[] src = BuildUniqueMap(20, 15);
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 20, 15, 0, 0, -5, -5, 0,
                out byte[] dst, out int nw, out int nh, out string err);
            Assert.True(ok, err);
            Assert.Equal(15, nw);
            Assert.Equal(10, nh);
            Assert.Equal(ReadTileAt(src, 20, 0, 0), ReadTileAt(dst, 15, 0, 0));
            Assert.Equal(ReadTileAt(src, 20, 14, 9), ReadTileAt(dst, 15, 14, 9));
        }

        [Fact]
        public void BuildResizedMapData_TooSmall_Rejected()
        {
            byte[] src = BuildUniqueMap(15, 10);
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 0, -1, 0, 0, 0,
                out byte[] dst, out _, out _, out string err);
            Assert.False(ok);
            Assert.Null(dst);
            Assert.Contains("smaller", err);
        }

        [Fact]
        public void BuildResizedMapData_TooTall_Rejected()
        {
            byte[] src = BuildUniqueMap(15, 10);
            // grow height to 64 (>63) → rejected
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 54, 0, 0, 0, 0,
                out byte[] dst, out _, out _, out _);
            Assert.False(ok);
            Assert.Null(dst);
        }

        [Fact]
        public void BuildResizedMapData_TooWideForHeight_Rejected_LimitAllowed()
        {
            // At height 30 the max width is 52. 15 + 38 = 53 > 52 → reject.
            byte[] src = BuildUniqueMap(15, 30);
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 30, 0, 0, 38, 0, 0,
                out byte[] dst, out _, out _, out string err);
            Assert.False(ok);
            Assert.Null(dst);
            Assert.Contains("wide", err);

            // 15 + 37 = 52 == limit → allowed
            byte[] src2 = BuildUniqueMap(15, 30);
            bool ok2 = MapEditorTilesetCore.BuildResizedMapData(
                src2, 15, 30, 0, 0, 37, 0, 0,
                out byte[] dst2, out int nw2, out _, out string err2);
            Assert.True(ok2, err2);
            Assert.Equal(52, nw2);
        }

        [Fact]
        public void BuildResizedMapData_DoesNotMutateInput()
        {
            byte[] src = BuildUniqueMap(15, 10);
            byte[] before = (byte[])src.Clone();
            MapEditorTilesetCore.BuildResizedMapData(
                src, 15, 10, 1, 1, 1, 1, 0,
                out _, out _, out _, out _);
            Assert.Equal(before, src);
        }

        [Fact]
        public void BuildResizedMapData_OversizedSourceDims_Rejected()
        {
            // Dimensions come from a u8 header, so anything >255 is corrupt input. The
            // old `int` length math (oldW*oldH*2) would overflow and could pass the
            // short-buffer check; the guard must reject before that.
            byte[] tiny = new byte[2];
            bool ok = MapEditorTilesetCore.BuildResizedMapData(
                tiny, 100000, 100000, 0, 0, 0, 0, 0,
                out byte[] dst, out _, out _, out string err);
            Assert.False(ok);
            Assert.Null(dst);
            Assert.Contains("invalid source dimensions", err);
        }
    }
}
