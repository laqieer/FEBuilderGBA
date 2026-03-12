using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Minimal IImage stub for testing rendering output dimensions.
    /// </summary>
    internal class StubImage : IImage
    {
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => false;
        byte[] _pixels;

        public StubImage(int w, int h)
        {
            Width = w;
            Height = h;
            _pixels = new byte[w * h * 4];
        }

        public byte[] GetPixelData() => _pixels;
        public void SetPixelData(byte[] data) { _pixels = data; }
        public byte[] GetPaletteGBA() => Array.Empty<byte>();
        public void SetPaletteGBA(byte[] p) { }
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string f) { }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }

    /// <summary>
    /// IImageService stub that creates StubImage and implements GBAColorToRGBA.
    /// </summary>
    internal class StubImageService : IImageService
    {
        public IImage CreateImage(int w, int h) => new StubImage(w, h);
        public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new StubImage(w, h);
        public IImage LoadImage(string f) => null;
        public IImage LoadImageFromBytes(byte[] d) => null;
        public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }
        public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;
        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => new StubImage(w, h);
        public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new StubImage(w, h);
        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new StubImage(w, h);
        public byte[] Encode4bppTiles(IImage i) => null;
        public byte[] Encode8bppTiles(IImage i) => null;
        public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
    }

    /// <summary>
    /// Tests for battle animation data structure parsing logic used by
    /// ImageBattleAnimeViewModel. Validates pointer chain traversal,
    /// LZ77 size detection, and SP type name resolution.
    /// </summary>
    [Collection("SharedState")]
    public class BattleAnimeDetailTests
    {
        /// <summary>
        /// Verify that ResolveSPTypeName returns weapon type names
        /// when Special flag (b1) is 1.
        /// </summary>
        [Theory]
        [InlineData(0u, 1u, "Sword")]
        [InlineData(1u, 1u, "Lance")]
        [InlineData(2u, 1u, "Axe")]
        [InlineData(3u, 1u, "Bow")]
        [InlineData(4u, 1u, "Staff")]
        [InlineData(5u, 1u, "Anima")]
        [InlineData(6u, 1u, "Light")]
        [InlineData(7u, 1u, "Dark")]
        [InlineData(0xFFu, 1u, "Type 0xFF")]
        public void ResolveSPTypeName_WeaponType_ReturnsCorrectName(uint b0, uint b1, string expected)
        {
            // Use the same logic as ImageBattleAnimeViewModel.ResolveSPTypeName
            string result = ResolveSPTypeName(b0, b1);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ResolveSPTypeName_UnknownSpecial_ReturnsHex()
        {
            string result = ResolveSPTypeName(0, 2);
            Assert.Equal("Special=0x02", result);
        }

        /// <summary>
        /// Verify LZ77.getUncompressSize correctly reads the size from a
        /// minimal LZ77 header (type byte 0x10 + 3-byte little-endian size).
        /// </summary>
        [Fact]
        public void LZ77_GetUncompressSize_ReadsHeaderCorrectly()
        {
            // LZ77 header: byte[0]=0x10 (LZ77 marker), bytes[1..3] = size in LE
            // Size = 0x001234 = 4660
            byte[] data = new byte[256];
            data[0] = 0x10; // LZ77 type
            data[1] = 0x34; // size low
            data[2] = 0x12; // size mid
            data[3] = 0x00; // size high

            uint size = LZ77.getUncompressSize(data, 0);
            Assert.Equal(0x001234u, size);
        }

        [Fact]
        public void LZ77_GetUncompressSize_InvalidOffset_ReturnsZero()
        {
            byte[] data = new byte[4];
            // offset beyond data
            uint size = LZ77.getUncompressSize(data, 100);
            Assert.Equal(0u, size);
        }

        /// <summary>
        /// Validate that the animation data record structure has the expected layout:
        /// - Bytes 0-11: Name (12 chars)
        /// - Offset 12: Section pointer (4 bytes)
        /// - Offset 16: Frame pointer (4 bytes)
        /// - Offset 20: OAM R-to-L pointer (4 bytes)
        /// - Offset 24: OAM L-to-R pointer (4 bytes)
        /// - Offset 28: Palette pointer (4 bytes)
        /// Total: 32 bytes per record
        /// </summary>
        [Fact]
        public void AnimationRecord_StructLayout_IsCorrect()
        {
            // Build a fake 32-byte animation record
            byte[] record = new byte[32];

            // Name: "TestAnim" + padding
            byte[] name = System.Text.Encoding.ASCII.GetBytes("TestAnim\0\0\0\0");
            System.Array.Copy(name, 0, record, 0, 12);

            // Section pointer at offset 12: 0x08100000
            record[12] = 0x00; record[13] = 0x00; record[14] = 0x10; record[15] = 0x08;
            // Frame pointer at offset 16: 0x08200000
            record[16] = 0x00; record[17] = 0x00; record[18] = 0x20; record[19] = 0x08;
            // OAM R-to-L at offset 20: 0x08300000
            record[20] = 0x00; record[21] = 0x00; record[22] = 0x30; record[23] = 0x08;
            // OAM L-to-R at offset 24: 0x08400000
            record[24] = 0x00; record[25] = 0x00; record[26] = 0x40; record[27] = 0x08;
            // Palette at offset 28: 0x08500000
            record[28] = 0x00; record[29] = 0x00; record[30] = 0x50; record[31] = 0x08;

            // Verify pointers are valid GBA pointers (bit 27 set = 0x08xxxxxx)
            uint section = (uint)(record[12] | (record[13] << 8) | (record[14] << 16) | (record[15] << 24));
            uint frame = (uint)(record[16] | (record[17] << 8) | (record[18] << 16) | (record[19] << 24));
            uint oamRtL = (uint)(record[20] | (record[21] << 8) | (record[22] << 16) | (record[23] << 24));
            uint oamLtR = (uint)(record[24] | (record[25] << 8) | (record[26] << 16) | (record[27] << 24));
            uint palette = (uint)(record[28] | (record[29] << 8) | (record[30] << 16) | (record[31] << 24));

            Assert.Equal(0x08100000u, section);
            Assert.Equal(0x08200000u, frame);
            Assert.Equal(0x08300000u, oamRtL);
            Assert.Equal(0x08400000u, oamLtR);
            Assert.Equal(0x08500000u, palette);

            Assert.True(U.isPointer(section));
            Assert.True(U.isPointer(frame));
            Assert.True(U.isPointer(oamRtL));
            Assert.True(U.isPointer(oamLtR));
            Assert.True(U.isPointer(palette));
        }

        /// <summary>
        /// Mirror of the SP type name resolution from ImageBattleAnimeViewModel.
        /// </summary>
        static string ResolveSPTypeName(uint b0, uint b1)
        {
            if (b1 == 0)
            {
                return NameResolver.GetItemName(b0);
            }
            if (b1 == 1)
            {
                string[] weaponTypes = { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark" };
                return b0 < (uint)weaponTypes.Length ? weaponTypes[b0] : $"Type 0x{b0:X02}";
            }
            return $"Special=0x{b1:X02}";
        }
    }

    /// <summary>
    /// Tests for BattleAnimeRendererCore tile sheet rendering.
    /// </summary>
    [Collection("SharedState")]
    public class BattleAnimeRendererCoreTests : IDisposable
    {
        readonly IImageService _prevService;

        public BattleAnimeRendererCoreTests()
        {
            _prevService = CoreState.ImageService;
            CoreState.ImageService = new StubImageService();
        }

        public void Dispose()
        {
            CoreState.ImageService = _prevService;
        }

        [Fact]
        public void RenderTileSheet_NullTileData_ReturnsNull()
        {
            var result = BattleAnimeRendererCore.RenderTileSheet(null, new byte[32], 16);
            Assert.Null(result);
        }

        [Fact]
        public void RenderTileSheet_EmptyTileData_ReturnsNull()
        {
            var result = BattleAnimeRendererCore.RenderTileSheet(new byte[0], new byte[32], 16);
            Assert.Null(result);
        }

        [Fact]
        public void RenderTileSheet_NullPalette_ReturnsNull()
        {
            var result = BattleAnimeRendererCore.RenderTileSheet(new byte[32], null, 16);
            Assert.Null(result);
        }

        [Fact]
        public void RenderTileSheet_SingleTile_Returns8x8Image()
        {
            // 1 tile = 32 bytes of 4bpp data
            byte[] tileData = new byte[32];
            byte[] palette = new byte[32]; // 16 colors

            var result = BattleAnimeRendererCore.RenderTileSheet(tileData, palette, 16);
            Assert.NotNull(result);
            Assert.Equal(128, result.Width);  // 16 tiles per row * 8px
            Assert.Equal(8, result.Height);   // 1 row * 8px
        }

        [Fact]
        public void RenderTileSheet_32Tiles_Returns2Rows()
        {
            // 32 tiles = 32 * 32 = 1024 bytes
            byte[] tileData = new byte[32 * 32];
            byte[] palette = new byte[32];

            var result = BattleAnimeRendererCore.RenderTileSheet(tileData, palette, 16);
            Assert.NotNull(result);
            Assert.Equal(128, result.Width);  // 16 * 8
            Assert.Equal(16, result.Height);  // 2 rows * 8
        }

        [Fact]
        public void RenderTileSheet_CustomTilesPerRow_AdjustsDimensions()
        {
            byte[] tileData = new byte[32 * 8]; // 8 tiles
            byte[] palette = new byte[32];

            var result = BattleAnimeRendererCore.RenderTileSheet(tileData, palette, 4);
            Assert.NotNull(result);
            Assert.Equal(32, result.Width);   // 4 * 8
            Assert.Equal(16, result.Height);  // 2 rows * 8
        }

        [Fact]
        public void RenderTileSheet_PixelDataContainsColor()
        {
            // Create a single tile where every pixel is color index 1
            byte[] tileData = new byte[32];
            for (int i = 0; i < 32; i++)
                tileData[i] = 0x11; // Each nibble = 1

            // Palette: color 0 = black, color 1 = red (GBA: R=31, G=0, B=0 = 0x001F)
            byte[] palette = new byte[32];
            palette[2] = 0x1F; palette[3] = 0x00; // color 1 = (31,0,0)

            var result = BattleAnimeRendererCore.RenderTileSheet(tileData, palette, 16);
            Assert.NotNull(result);

            byte[] pixels = result.GetPixelData();
            // Check first non-transparent pixel (index 1 = opaque)
            // Pixel at (0,0): should be color index 1 = red
            Assert.Equal(248, pixels[0]); // R: 31 << 3
            Assert.Equal(0, pixels[1]);   // G
            Assert.Equal(0, pixels[2]);   // B
            Assert.Equal(255, pixels[3]); // A (not index 0, so opaque)
        }

        [Fact]
        public void RenderTileSheet_TransparentForIndex0()
        {
            // Single tile, all zeros = color index 0
            byte[] tileData = new byte[32];
            byte[] palette = new byte[32];

            var result = BattleAnimeRendererCore.RenderTileSheet(tileData, palette, 16);
            Assert.NotNull(result);

            byte[] pixels = result.GetPixelData();
            // Color index 0 should have alpha = 0 (transparent)
            Assert.Equal(0, pixels[3]); // A channel
        }

        [Fact]
        public void RenderTileSheet_NoImageService_ReturnsNull()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var result = BattleAnimeRendererCore.RenderTileSheet(new byte[32], new byte[32], 16);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = saved;
            }
        }

        [Fact]
        public void RenderAnimationTileSheet_NullRom_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var result = BattleAnimeRendererCore.RenderAnimationTileSheet(0, 16);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void CountFramesInRange_NullData_ReturnsZero()
        {
            Assert.Equal(0, BattleAnimeRendererCore.CountFramesInRange(null, 0, 100));
        }

        [Fact]
        public void CountFramesInRange_NoFrames_ReturnsZero()
        {
            // Data with no 0x86 commands
            byte[] data = new byte[16];
            Assert.Equal(0, BattleAnimeRendererCore.CountFramesInRange(data, 0, 16));
        }

        [Fact]
        public void CountFramesInRange_SingleFrame_ReturnsOne()
        {
            // A 0x86 command at byte[3] followed by 8 bytes of gfx+oam data
            byte[] data = new byte[16];
            data[3] = 0x86; // frame command marker
            // bytes 4-7 = graphics pointer, bytes 8-11 = OAM offset
            Assert.Equal(1, BattleAnimeRendererCore.CountFramesInRange(data, 0, 16));
        }

        [Fact]
        public void CountFramesInRange_TwoFrames_ReturnsTwo()
        {
            // Each frame: 4-byte command (byte[3]=0x86) + 8 bytes data = 12 bytes
            byte[] data = new byte[28];
            data[3] = 0x86;   // first frame
            data[15] = 0x86;  // second frame (offset 12 + 3)
            Assert.Equal(2, BattleAnimeRendererCore.CountFramesInRange(data, 0, 28));
        }

        [Fact]
        public void CountFramesInRange_RespectsRange()
        {
            byte[] data = new byte[28];
            data[3] = 0x86;   // frame at offset 0
            data[15] = 0x86;  // frame at offset 12
            // Only count frames in range [12, 28) -- should find 1
            Assert.Equal(1, BattleAnimeRendererCore.CountFramesInRange(data, 12, 28));
        }

        [Fact]
        public void ParseFramesInRange_NullData_ReturnsEmpty()
        {
            var frames = BattleAnimeRendererCore.ParseFramesInRange(null, 0, 100);
            Assert.Empty(frames);
        }

        [Fact]
        public void ParseFramesInRange_SingleFrame_ParsesCorrectly()
        {
            byte[] data = new byte[16];
            data[3] = 0x86;
            // Graphics pointer at offset 4: 0x08100000
            data[4] = 0x00; data[5] = 0x00; data[6] = 0x10; data[7] = 0x08;
            // OAM offset at offset 8: 0x00000040
            data[8] = 0x40; data[9] = 0x00; data[10] = 0x00; data[11] = 0x00;

            var frames = BattleAnimeRendererCore.ParseFramesInRange(data, 0, 16);
            Assert.Single(frames);
            Assert.Equal(0u, frames[0].FrameDataOffset);
            Assert.Equal(0x08100000u, frames[0].GraphicsPointer);
            Assert.Equal(0x00000040u, frames[0].OamOffset);
        }

        [Fact]
        public void SectionNames_Has12Entries()
        {
            Assert.Equal(12, BattleAnimeRendererCore.SectionNames.Length);
        }

        // ---- OAM size table tests ----

        [Theory]
        // Square shape (0x00) with different sizes
        [InlineData(0x00, 0x00, 1, 1)]  // square, times1 → 1x1 tiles = 8x8 px
        [InlineData(0x00, 0x40, 2, 2)]  // square, times2 → 2x2 tiles = 16x16 px
        [InlineData(0x00, 0x80, 4, 4)]  // square, times4 → 4x4 tiles = 32x32 px
        [InlineData(0x00, 0xC0, 8, 8)]  // square, times8 → 8x8 tiles = 64x64 px
        // Horizontal shape (0x40)
        [InlineData(0x40, 0x00, 2, 1)]  // horizontal, times1 → 2x1 tiles = 16x8 px
        [InlineData(0x40, 0x40, 4, 1)]  // horizontal, times2 → 4x1 tiles = 32x8 px
        [InlineData(0x40, 0x80, 4, 2)]  // horizontal, times4 → 4x2 tiles = 32x16 px
        [InlineData(0x40, 0xC0, 8, 4)]  // horizontal, times8 → 8x4 tiles = 64x32 px
        // Vertical shape (0x80)
        [InlineData(0x80, 0x00, 1, 2)]  // vertical, times1 → 1x2 tiles = 8x16 px
        [InlineData(0x80, 0x40, 1, 4)]  // vertical, times2 → 1x4 tiles = 8x32 px
        [InlineData(0x80, 0x80, 2, 4)]  // vertical, times4 → 2x4 tiles = 16x32 px
        [InlineData(0x80, 0xC0, 4, 8)]  // vertical, times8 → 4x8 tiles = 32x64 px
        public void GetOAMSize_ReturnsCorrectTileDimensions(int align, int area, int expectedW, int expectedH)
        {
            BattleAnimeRendererCore.GetOAMSize(align, area, out int w, out int h);
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }

        [Theory]
        // Shape bits are in align bits 6-7; lower bits (rotation flags) should be ignored
        [InlineData(0x03, 0x80, 4, 4)]  // square + rotation bits set → still square times4
        [InlineData(0x43, 0x80, 4, 2)]  // horizontal + rotation bits → horizontal times4
        public void GetOAMSize_IgnoresLowerAlignBits(int align, int area, int expectedW, int expectedH)
        {
            BattleAnimeRendererCore.GetOAMSize(align, area, out int w, out int h);
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }

        [Theory]
        // Area lower bits (flip/affine) should be ignored for size
        [InlineData(0x00, 0x50, 2, 2)]  // times2 + vFlip bit → still times2 square
        [InlineData(0x00, 0x60, 2, 2)]  // times2 + hFlip bit → still times2 square
        [InlineData(0x00, 0x7F, 2, 2)]  // times2 + all lower bits → still times2 square
        public void GetOAMSize_IgnoresLowerAreaBits(int align, int area, int expectedW, int expectedH)
        {
            BattleAnimeRendererCore.GetOAMSize(align, area, out int w, out int h);
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }

        // ---- OAM drawing tests ----

        [Fact]
        public void DrawOAMSprites_NullOamData_DoesNotCrash()
        {
            byte[] dst = new byte[240 * 160 * 4];
            BattleAnimeRendererCore.DrawOAMSprites(null, 0,
                new byte[256 * 64 * 4], 256, 64,
                dst, 240, 160);
            // Should not throw; dst should remain unchanged
            Assert.All(dst, b => Assert.Equal(0, b));
        }

        [Fact]
        public void DrawOAMSprites_Terminator_StopsImmediately()
        {
            // OAM data with immediate terminator (byte[0] = 1)
            byte[] oam = new byte[12];
            oam[0] = 0x01; // terminator
            byte[] dst = new byte[240 * 160 * 4];
            BattleAnimeRendererCore.DrawOAMSprites(oam, 0,
                new byte[256 * 64 * 4], 256, 64,
                dst, 240, 160);
            Assert.All(dst, b => Assert.Equal(0, b));
        }

        [Fact]
        public void DrawOAMSprites_FEditorTerminator_StopsImmediately()
        {
            // FEditor alternate terminator: 0x00, 0xFF, 0xFF, 0xFF
            byte[] oam = new byte[12];
            oam[0] = 0x00;
            oam[1] = 0xFF;
            oam[2] = 0xFF;
            oam[3] = 0xFF;
            byte[] dst = new byte[240 * 160 * 4];
            BattleAnimeRendererCore.DrawOAMSprites(oam, 0,
                new byte[256 * 64 * 4], 256, 64,
                dst, 240, 160);
            Assert.All(dst, b => Assert.Equal(0, b));
        }

        [Fact]
        public void DrawOAMSprites_AffineEntry_Skipped()
        {
            // Affine entry (bytes[2..3] == 0xFFFF) followed by terminator
            byte[] oam = new byte[24];
            // Entry 0: affine marker
            oam[0] = 0x00;
            oam[2] = 0xFF; oam[3] = 0xFF; // affine marker
            // Entry 1: terminator
            oam[12] = 0x01;
            byte[] dst = new byte[240 * 160 * 4];
            BattleAnimeRendererCore.DrawOAMSprites(oam, 0,
                new byte[256 * 64 * 4], 256, 64,
                dst, 240, 160);
            Assert.All(dst, b => Assert.Equal(0, b));
        }

        [Fact]
        public void DrawOAMSprites_SingleSprite_DrawsAtCorrectPosition()
        {
            // Build a 256x64 source sheet with a known pixel at tile (0,0)
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            // Put a red pixel at (0,0) in the source sheet
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 255;

            // Build an OAM entry for: square 1x1 tile, sheet pos (0,0), vram (0,0)
            byte[] oam = new byte[24];
            oam[0] = 0x00;  // normal entry
            oam[1] = 0x00;  // align = square (bits 6-7 = 0)
            oam[2] = 0x00;  // not affine
            oam[3] = 0x00;  // area = times1, no flip, no affine
            oam[4] = 0x00;  // sheet tile: x=0, y=0
            oam[5] = 0x00;  // palette bank = 0
            // vram_x = 0 (signed 16-bit LE)
            oam[6] = 0x00; oam[7] = 0x00;
            // vram_y = 0 (signed 16-bit LE)
            oam[8] = 0x00; oam[9] = 0x00;
            // terminator at entry 1
            oam[12] = 0x01;

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // vram(0,0) + offset(0x94, 0x58) = pixel(148, 88) on the 240x160 screen
            int expectedX = 0x94; // 148
            int expectedY = 0x58; // 88
            int idx = (expectedY * dstW + expectedX) * 4;
            Assert.Equal(255, dst[idx + 0]); // R
            Assert.Equal(0, dst[idx + 1]);   // G
            Assert.Equal(0, dst[idx + 2]);   // B
            Assert.Equal(255, dst[idx + 3]); // A
        }

        [Fact]
        public void DrawOAMSprites_NegativeVramCoords_DrawsCorrectly()
        {
            // Build source sheet with a known pixel
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            src[0] = 0; src[1] = 255; src[2] = 0; src[3] = 255; // green at (0,0)

            // OAM entry with vram_x = -10 (0xFFF6), vram_y = -20 (0xFFEC)
            byte[] oam = new byte[24];
            oam[0] = 0x00;
            oam[1] = 0x00;  // square
            oam[3] = 0x00;  // times1
            oam[4] = 0x00;  // sheet (0,0)
            oam[5] = 0x00;
            // vram_x = -10 → 0xFFF6 LE
            oam[6] = 0xF6; oam[7] = 0xFF;
            // vram_y = -20 → 0xFFEC LE
            oam[8] = 0xEC; oam[9] = 0xFF;
            oam[12] = 0x01; // terminator

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // Expected: (0x94 + (-10), 0x58 + (-20)) = (138, 68)
            int expectedX = 138;
            int expectedY = 68;
            int idx = (expectedY * dstW + expectedX) * 4;
            Assert.Equal(0, dst[idx + 0]);   // R
            Assert.Equal(255, dst[idx + 1]); // G
            Assert.Equal(0, dst[idx + 2]);   // B
            Assert.Equal(255, dst[idx + 3]); // A
        }

        [Fact]
        public void DrawOAMSprites_SheetTilePosition_IsCorrect()
        {
            // Source sheet: put a blue pixel at tile (2, 1) → pixel (16, 8)
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            int srcIdx = (8 * srcW + 16) * 4;
            src[srcIdx + 0] = 0; src[srcIdx + 1] = 0; src[srcIdx + 2] = 255; src[srcIdx + 3] = 255;

            // OAM: sheet tile x=2, y=1 → byte[4] = (1 << 5) | 2 = 0x22
            byte[] oam = new byte[24];
            oam[0] = 0x00;
            oam[1] = 0x00;  // square
            oam[3] = 0x00;  // times1
            oam[4] = 0x22;  // sheet: x=2, y=1
            oam[5] = 0x00;
            oam[6] = 0x00; oam[7] = 0x00; // vram_x = 0
            oam[8] = 0x00; oam[9] = 0x00; // vram_y = 0
            oam[12] = 0x01;

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // First pixel of the 8x8 tile at sheet (2,1) → drawn at screen (0x94, 0x58)
            int idx = (0x58 * dstW + 0x94) * 4;
            Assert.Equal(0, dst[idx + 0]);
            Assert.Equal(0, dst[idx + 1]);
            Assert.Equal(255, dst[idx + 2]); // Blue
            Assert.Equal(255, dst[idx + 3]);
        }

        [Fact]
        public void DrawOAMSprites_HFlip_MirrorsHorizontally()
        {
            // Source: 1x1 tile (8x8), red pixel at (0,0), green at (7,0)
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            // Red at (0,0)
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 255;
            // Green at (7,0)
            int gi = (0 * srcW + 7) * 4;
            src[gi] = 0; src[gi + 1] = 255; src[gi + 2] = 0; src[gi + 3] = 255;

            // OAM: h_flip set (area bit 5 = 0x20)
            byte[] oam = new byte[24];
            oam[0] = 0x00;
            oam[1] = 0x00;
            oam[3] = 0x20;  // times1 + h_flip
            oam[4] = 0x00;
            oam[5] = 0x00;
            oam[6] = 0; oam[7] = 0;
            oam[8] = 0; oam[9] = 0;
            oam[12] = 0x01;

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // With h_flip, src(0,0) red → dst column 7, src(7,0) green → dst column 0
            // At screen (0x94, 0x58): should now be green (was red before flip)
            int dstIdx = (0x58 * dstW + 0x94) * 4;
            Assert.Equal(0, dst[dstIdx + 0]);     // was red, now green
            Assert.Equal(255, dst[dstIdx + 1]);
            Assert.Equal(0, dst[dstIdx + 2]);

            // At screen (0x94+7, 0x58): should now be red
            int dstIdx2 = (0x58 * dstW + 0x94 + 7) * 4;
            Assert.Equal(255, dst[dstIdx2 + 0]);
            Assert.Equal(0, dst[dstIdx2 + 1]);
        }

        [Fact]
        public void DrawOAMSprites_VFlip_MirrorsVertically()
        {
            // Source: 1x1 tile (8x8), red at (0,0), blue at (0,7)
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 255;
            int bi = (7 * srcW + 0) * 4;
            src[bi] = 0; src[bi + 1] = 0; src[bi + 2] = 255; src[bi + 3] = 255;

            // OAM: v_flip set (area bit 4 = 0x10)
            byte[] oam = new byte[24];
            oam[0] = 0x00;
            oam[1] = 0x00;
            oam[3] = 0x10;  // times1 + v_flip
            oam[4] = 0x00;
            oam[5] = 0x00;
            oam[6] = 0; oam[7] = 0;
            oam[8] = 0; oam[9] = 0;
            oam[12] = 0x01;

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // With v_flip, src(0,0) red → dst row 7, src(0,7) blue → dst row 0
            int dstIdx = (0x58 * dstW + 0x94) * 4;
            Assert.Equal(0, dst[dstIdx + 0]);
            Assert.Equal(0, dst[dstIdx + 1]);
            Assert.Equal(255, dst[dstIdx + 2]); // blue (was at bottom, now at top)

            int dstIdx2 = ((0x58 + 7) * dstW + 0x94) * 4;
            Assert.Equal(255, dst[dstIdx2 + 0]); // red (was at top, now at bottom)
        }

        [Fact]
        public void DrawOAMSprites_BugPaletteShift_SkipsEntry()
        {
            // palette bank >= 4 is a bug frame, should be skipped
            byte[] oam = new byte[24];
            oam[0] = 0x00;
            oam[1] = 0x00;
            oam[3] = 0x00;
            oam[4] = 0x00;
            oam[5] = 0x40;  // palette bank = 4 (bits 4-7 = 4) → bug
            oam[6] = 0; oam[7] = 0;
            oam[8] = 0; oam[9] = 0;
            oam[12] = 0x01;

            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 255; // visible pixel

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // Nothing should be drawn
            Assert.All(dst, b => Assert.Equal(0, b));
        }
    }
}
