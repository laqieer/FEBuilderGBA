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
    }
}
