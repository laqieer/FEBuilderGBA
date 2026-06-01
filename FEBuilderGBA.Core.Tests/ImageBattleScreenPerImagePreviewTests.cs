// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBattleScreenCore.RenderSingleImagePreview (#816, follow-up to
// #802/#804/#807): the cross-platform per-image (Image1..Image5) previews that
// mirror the WinForms ImageBattleScreenForm.InitLoadChipsetInfo per-image strip
// layout (FEBuilderGBA/ImageBattleScreenForm.cs:82-98).
//
// Per-image dimensions (mirror WF EXACTLY):
//   * Image1 (index 0): NATURAL W x H via the ported CalcLZ77ImageToSize
//     "nice divisor" loop -- NOT an 8px slice of the concatenated ChipCache
//     sheet. A 33-tile strip => liner width 264 => (88, 24), i.e. 11 tiles per
//     row x 3 rows (the genuine multi-row, width != 8 case the reviewer asked
//     for: height > 8 only occurs when the liner width exceeds 256px).
//   * Image2..Image5 (index 1..4): liner-width x 8px (a single horizontal row),
//     via the ported CalcLZ77LinerImageToWidth floor-to-8 formula.
//
// Palette bank 0; palette index 0 OPAQUE (WF BitBlt, transparent_index = 0xFF).
//
// Reuses the #802/#807 synthetic-ROM harness (StubImageService) so the rendered
// IImage carries real RGBA pixel data we can assert on.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageBattleScreenPerImagePreviewTests
    {
        // Storage offsets for the planted battle-screen sources.
        const uint PALETTE_OFFSET = 0x105000;
        const uint IMAGE1_OFFSET  = 0x110000;
        const uint IMAGE2_OFFSET  = 0x114000;
        const uint IMAGE3_OFFSET  = 0x118000;
        const uint IMAGE4_OFFSET  = 0x11C000;
        const uint IMAGE5_OFFSET  = 0x120000;

        // Per-image tile counts planted by MakeRom (each strip decoded from its
        // OWN LZ77 stream at its OWN width):
        //   image1: 33 tiles -> natural (88, 24) = 11x3 (multi-row, width != 8)
        //   image2: 4  tiles -> liner (32, 8)
        //   image3: 16 tiles -> liner (128, 8)
        //   image4: 1  tile  -> liner (8, 8)
        //   image5: 8  tiles -> liner (64, 8)
        const int IMAGE1_TILES = 33;
        const int IMAGE2_TILES = 4;
        const int IMAGE3_TILES = 16;
        const int IMAGE4_TILES = 1;
        const int IMAGE5_TILES = 8;

        // ----------------------------------------------------------------
        // Image1 (index 0) -- natural W x H, multi-row width != 8
        // ----------------------------------------------------------------

        [Fact]
        public void RenderSingleImagePreview_Image1_HasNaturalMultiRowDimensions()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 0);
            Assert.NotNull(img);
            // 33 tiles -> liner width 264 -> nice-divisor (88, 24). 3 rows of 11
            // tiles each: this is the genuine height>8, width!=8 case.
            Assert.Equal(88, img.Width);
            Assert.Equal(24, img.Height);
        }

        [Fact]
        public void RenderSingleImagePreview_Image1_LaysTilesRowMajorAtWidth()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 0);
            Assert.NotNull(img);

            // Layout: 11 tiles per row. Plant scheme (see MakeRom):
            //   tile 0  = SolidTile(5) green  -> row 0, col 0 -> pixel (0, 0)
            //   tile 10 = SolidTile(3) red    -> row 0, col 10 -> pixel (80, 0)
            //   tile 11 = SolidTile(3) red    -> row 1, col 0  -> pixel (0, 8)
            //   tile 22 = SolidTile(5) green  -> row 2, col 0  -> pixel (0, 16)
            //   tile 32 = SolidTile(5) green  -> row 2, col 10 -> pixel (80, 16)
            AssertPixel(img, 0, 0, 0, 248, 0, 255);    // tile 0 green
            AssertPixel(img, 80, 0, 248, 0, 0, 255);   // tile 10 red, same row
            AssertPixel(img, 0, 8, 248, 0, 0, 255);    // tile 11 red, row 1 start
            AssertPixel(img, 0, 16, 0, 248, 0, 255);   // tile 22 green, row 2 start
            AssertPixel(img, 80, 16, 0, 248, 0, 255);  // tile 32 green, last tile
        }

        // ----------------------------------------------------------------
        // Image2..Image5 (index 1..4) -- liner width x 8px
        // ----------------------------------------------------------------

        [Fact]
        public void RenderSingleImagePreview_Image2_LinerWidthTimes8()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 1);
            Assert.NotNull(img);
            // 4 tiles -> liner width 32, height 8 (single row).
            Assert.Equal(32, img.Width);
            Assert.Equal(8, img.Height);
            // tile 0 = green at (0,0); tile 3 = red at (24,0) (row-major).
            AssertPixel(img, 0, 0, 0, 248, 0, 255);
            AssertPixel(img, 24, 0, 248, 0, 0, 255);
        }

        [Fact]
        public void RenderSingleImagePreview_Image3_LinerWidthTimes8()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 2);
            Assert.NotNull(img);
            // 16 tiles -> liner width 128, height 8.
            Assert.Equal(128, img.Width);
            Assert.Equal(8, img.Height);
            AssertPixel(img, 0, 0, 0, 248, 0, 255);     // tile 0 green
            AssertPixel(img, 120, 0, 0, 248, 0, 255);   // tile 15 green (last)
        }

        [Fact]
        public void RenderSingleImagePreview_Image4_SingleTileLinerWidth()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 3);
            Assert.NotNull(img);
            // 1 tile -> liner width 8, height 8.
            Assert.Equal(8, img.Width);
            Assert.Equal(8, img.Height);
            AssertPixel(img, 3, 3, 0, 248, 0, 255); // green fill
        }

        [Fact]
        public void RenderSingleImagePreview_Image5_LinerWidthTimes8()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 4);
            Assert.NotNull(img);
            // 8 tiles -> liner width 64, height 8.
            Assert.Equal(64, img.Width);
            Assert.Equal(8, img.Height);
            AssertPixel(img, 0, 0, 0, 248, 0, 255);    // tile 0 green
            AssertPixel(img, 56, 0, 0, 248, 0, 255);   // tile 7 green (last)
        }

        // ----------------------------------------------------------------
        // Palette bank 0 + index-0 opacity
        // ----------------------------------------------------------------

        [Fact]
        public void RenderSingleImagePreview_UsesPaletteBank0()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image2 tile 0 = SolidTile(5). Bank 0 index 5 = green (0x03E0).
            // Bank 1 index 5 = blue (0x7C00). The per-image preview must use
            // bank 0 -> green (NOT blue), proving it does not reach for a
            // TSA-derived or bank-1 palette.
            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 1);
            Assert.NotNull(img);
            AssertPixel(img, 0, 0, 0, 248, 0, 255); // green = bank 0, not blue
        }

        [Fact]
        public void RenderSingleImagePreview_PaletteIndex0_RendersOpaque()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Plant image4 as a single index-0 tile. Bank 0 index 0 = white
            // (0x7FFF). Per WF BitBlt transparent_index = 0xFF, index 0 must
            // render OPAQUE (alpha 255, not 0).
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image4_pointer, IMAGE4_OFFSET, 1, _ => 0);

            IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 3);
            Assert.NotNull(img);
            AssertPixel(img, 0, 0, 248, 248, 248, 255);
            AssertPixel(img, 7, 7, 248, 248, 248, 255);
        }

        // ----------------------------------------------------------------
        // No-bleed: a planted strip's pixels stay inside its OWN per-image
        // canvas (the factored TryDecodeImageStrip's actual chunk length is the
        // real boundary -- a single strip never reaches its neighbor).
        // ----------------------------------------------------------------

        [Fact]
        public void RenderSingleImagePreview_Image2_DoesNotBleedFromImage3()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image2 = 4 green/red tiles (width 32). image3 = 16 green tiles.
            // The image2 preview must be EXACTLY 32x8 and contain only image2's
            // own tiles -- no image3 tile can bleed in (each strip is decoded
            // from its own LZ77 stream at its own width).
            IImage img2 = ImageBattleScreenCore.RenderSingleImagePreview(rom, 1);
            Assert.NotNull(img2);
            Assert.Equal(32, img2.Width); // exactly image2's width
            Assert.Equal(8, img2.Height);

            // The last pixel column of image2 (tile 3, red) is at x=24..31.
            // There is no x=32 -- proven by the canvas width being 32.
            AssertPixel(img2, 31, 7, 248, 0, 0, 255); // tile 3 red, bottom-right
        }

        [Fact]
        public void RenderSingleImagePreview_PerStripChunkLength_BoundsTheRender()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // The actual per-strip decoded byte count (the factored helper's
            // boundary) is image2_tiles * 32. Its width-derived tile capacity
            // (32px / 8 = 4 tiles per row, 1 row) must equal IMAGE2_TILES so
            // every planted tile lands and nothing past the chunk is read.
            bool ok = ImageBattleScreenCore.TryLoadSingleImageStrip(rom, 1,
                out byte[] tiles, out int widthPx, out int heightPx);
            Assert.True(ok);
            Assert.Equal(IMAGE2_TILES * 32, tiles.Length);
            Assert.Equal(32, widthPx);
            Assert.Equal(8, heightPx);
            int tilesPerRow = widthPx / 8;
            int rows = heightPx / 8;
            Assert.Equal(IMAGE2_TILES, tilesPerRow * rows);
        }

        // ----------------------------------------------------------------
        // Ported width-helper formula parity (via TryLoadSingleImageStrip's
        // computed dims, which call CalcLinerImageToWidth / CalcImageToSize).
        // ----------------------------------------------------------------

        [Theory]
        [InlineData(1, 8, 8)]      // 1 tile  -> liner 8
        [InlineData(4, 32, 8)]     // 4 tiles -> liner 32
        [InlineData(8, 64, 8)]     // 8 tiles -> liner 64
        [InlineData(16, 128, 8)]   // 16 tiles -> liner 128
        [InlineData(32, 256, 8)]   // 32 tiles -> liner 256 (still single row)
        public void TryLoadSingleImageStrip_Liner_MatchesWfFloorToEight(int tiles, int expectedW, int expectedH)
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Re-plant image2 with the parameterized tile count.
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image2_pointer, IMAGE2_OFFSET, tiles, _ => 5);

            bool ok = ImageBattleScreenCore.TryLoadSingleImageStrip(rom, 1,
                out byte[] _, out int widthPx, out int heightPx);
            Assert.True(ok);
            Assert.Equal(expectedW, widthPx); // (uncompSize/2/2/8)*8
            Assert.Equal(expectedH, heightPx); // always 8 for liner images
        }

        [Theory]
        [InlineData(33, 88, 24)]   // 264px liner -> nice-divisor (88, 24), 3 rows
        [InlineData(34, 136, 16)]  // 272px liner -> (136, 16), 2 rows
        [InlineData(40, 160, 16)]  // 320px liner -> (160, 16), 2 rows
        [InlineData(65, 104, 40)]  // 520px liner -> (104, 40), 5 rows
        [InlineData(16, 128, 8)]   // 128px liner -> (128, 8), single row
        public void TryLoadSingleImageStrip_Image1_MatchesWfNiceDivisor(int tiles, int expectedW, int expectedH)
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Re-plant image1 with the parameterized tile count.
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image1_pointer, IMAGE1_OFFSET, tiles, _ => 5);

            bool ok = ImageBattleScreenCore.TryLoadSingleImageStrip(rom, 0,
                out byte[] _, out int widthPx, out int heightPx);
            Assert.True(ok);
            Assert.Equal(expectedW, widthPx);
            Assert.Equal(expectedH, heightPx);
        }

        // ----------------------------------------------------------------
        // Null-safety
        // ----------------------------------------------------------------

        [Fact]
        public void RenderSingleImagePreview_NullRom_ReturnsNull()
        {
            using var _ = EnsureImageService();
            Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(null, 0));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(100)]
        public void RenderSingleImagePreview_BadIndex_ReturnsNull(int badIndex)
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(rom, badIndex));
        }

        [Fact]
        public void RenderSingleImagePreview_NoImageService_ReturnsNull()
        {
            var prev = CoreState.ImageService;
            CoreState.ImageService = null;
            try
            {
                var rom = MakeRom();
                Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(rom, 0));
            }
            finally { CoreState.ImageService = prev; }
        }

        [Fact]
        public void RenderSingleImagePreview_CorruptImagePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Point image3 at an out-of-bounds offset -> isSafetyOffset fails ->
            // the per-strip decode fails -> render returns null (no crash).
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(0x1FFFFFF0));
            Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(rom, 2));

            // The OTHER images still render (single-image contract is per-strip,
            // not all-or-nothing like the composite loader).
            Assert.NotNull(ImageBattleScreenCore.RenderSingleImagePreview(rom, 0));
        }

        [Fact]
        public void RenderSingleImagePreview_CorruptPalettePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Palette pointer out of bounds -> TryLoadRawPalette fails -> null.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(0x1FFFFFF0));
            Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(rom, 0));
        }

        [Fact]
        public void RenderSingleImagePreview_TruncatedImageStream_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Plant a TRUNCATED LZ77 stream for image2 at the end of ROM: a valid
            // header (0x10 + a 0x1000-byte advertised size) but only a few bytes
            // before Data.Length. TryDecodeImageStrip's getCompressedSize guard
            // returns 0 -> render returns null (matching the #802 contract).
            uint imageAddr = (uint)rom.Data.Length - 8;
            rom.Data[imageAddr + 0] = 0x10;
            rom.Data[imageAddr + 1] = 0x00;
            rom.Data[imageAddr + 2] = 0x10;
            rom.Data[imageAddr + 3] = 0x00;
            rom.Data[imageAddr + 4] = 0x00; // control byte
            rom.Data[imageAddr + 5] = 0xAA;
            rom.Data[imageAddr + 6] = 0xBB;
            rom.Data[imageAddr + 7] = 0xCC;
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image2_pointer, U.toPointer(imageAddr));

            // Sanity: confirm the truncation is real at the LZ77 level.
            Assert.Equal(0u, LZ77.getCompressedSize(rom.Data, imageAddr));

            Assert.Null(ImageBattleScreenCore.RenderSingleImagePreview(rom, 1));
        }

        // ----------------------------------------------------------------
        // #804/#807 regression -- the composite battle preview + chipset chip
        // list must stay green after the per-strip extraction (behavior-
        // preserving refactor: TryLoadChipsetAndPalette now reuses
        // TryDecodeImageStrip + TryLoadRawPalette).
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_StillRendersAfterPerStripExtraction()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // tile 0 (image1) green at cell (y=0, x=1) -> pixel (8, 0).
            ushort[] map = new ushort[ImageBattleScreenCore.MAP_SIZE];
            map[0 * ImageBattleScreenCore.MAP_X + 1] = MakeCell(tile: 0, flip: 0, pal: 0);
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);
            Assert.Equal(256, img.Width);
            Assert.Equal(160, img.Height);
            AssertPixel(img, 8, 0, 0, 248, 0, 255); // bank0 idx5 green, opaque
        }

        [Fact]
        public void RenderChipsetPreview_StillRendersAfterPerStripExtraction()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);
            // Canvas width = 8*4*2 = 64. tile 0 (image1) = green at col 0, row 0.
            Assert.Equal(64, img.Width);
            AssertPixel(img, 0, 0, 0, 248, 0, 255); // bank0 idx5 green, opaque
        }

        // ----------------------------------------------------------------
        // Helpers (mirror ImageBattleScreenChipsetPreviewTests)
        // ----------------------------------------------------------------

        static System.IDisposable EnsureImageService() => new ImageServiceScope();

        sealed class ImageServiceScope : System.IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        static ushort MakeCell(uint tile, uint flip, uint pal)
        {
            return (ushort)((tile & 0xff) | ((flip & 0x0f) << 8) | ((pal & 0x0f) << 12));
        }

        static void AssertPixel(IImage img, int x, int y, int r, int g, int b, int a)
        {
            byte[] px = img.GetPixelData();
            int idx = (y * img.Width + x) * 4;
            Assert.True(idx + 3 < px.Length, $"pixel ({x},{y}) out of range");
            Assert.Equal((byte)r, px[idx + 0]);
            Assert.Equal((byte)g, px[idx + 1]);
            Assert.Equal((byte)b, px[idx + 2]);
            Assert.Equal((byte)a, px[idx + 3]);
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            System.Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // ---- Palette: 16 banks x 16 colors x 2 bytes = 512 bytes ----
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(PALETTE_OFFSET));
            for (uint i = 0; i < 512; i++) rom.Data[PALETTE_OFFSET + i] = 0;
            // bank 0 index 0 = white (0x7FFF), index 3 = red (0x001F),
            //         index 5 = green (0x03E0).
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 0) * 2, 0x7FFF);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 3) * 2, 0x001F);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 5) * 2, 0x03E0);
            // bank 1 index 5 = blue (0x7C00) -- the bank-0 proof relies on this.
            U.write_u16(rom.Data, PALETTE_OFFSET + (1 * 16 + 5) * 2, 0x7C00);

            // ---- image1: 33 distinct tiles -> natural (88, 24) = 11x3 ----
            // Row-major plant: tiles 0,11,22 green at column 0 of each row;
            // tiles 10,32 (row 0/2 last) green; tile 11 (row 1 start) red;
            // tile 10 (row 0 last) red. The rest default green so the canvas
            // fills, with the distinctive red corners used by the layout test.
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image1_pointer, IMAGE1_OFFSET, IMAGE1_TILES,
                t => (t == 10 || t == 11) ? 3 : 5); // red at tile 10/11, else green

            // ---- image2: 4 tiles -> liner (32, 8). tile 3 red, rest green. ----
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image2_pointer, IMAGE2_OFFSET, IMAGE2_TILES,
                t => (t == 3) ? 3 : 5);

            // ---- image3: 16 green tiles -> liner (128, 8). ----
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image3_pointer, IMAGE3_OFFSET, IMAGE3_TILES, _ => 5);

            // ---- image4: 1 green tile -> liner (8, 8). ----
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image4_pointer, IMAGE4_OFFSET, IMAGE4_TILES, _ => 5);

            // ---- image5: 8 green tiles -> liner (64, 8). ----
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image5_pointer, IMAGE5_OFFSET, IMAGE5_TILES, _ => 5);

            return rom;
        }

        /// <summary>
        /// Plant an LZ77-compressed strip of <paramref name="tileCount"/> 4bpp
        /// tiles at <paramref name="offset"/>, set <paramref name="slot"/> to
        /// point at it. <paramref name="colorOf"/> maps tile index -> the
        /// 4bpp color index used to solid-fill that tile.
        /// </summary>
        static void PlantImageStripTiles(ROM rom, uint slot, uint offset, int tileCount, Func<int, int> colorOf)
        {
            byte[] raw = new byte[tileCount * 32];
            for (int t = 0; t < tileCount; t++)
            {
                int idx = colorOf(t) & 0x0f;
                byte packed = (byte)((idx << 4) | idx);
                int baseOff = t * 32;
                for (int i = 0; i < 32; i++) raw[baseOff + i] = packed;
            }
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
            U.write_u32(rom.Data, slot, U.toPointer(offset));
        }

        static void PlantMap(ROM rom, ushort[] map)
        {
            uint addr;
            int MAP_X = ImageBattleScreenCore.MAP_X;

            // Allocate + point the 5 TSA regions (the per-image tests don't need
            // them, but the composite #804 regression does).
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(0x100000));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA2_pointer, U.toPointer(0x101000));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(0x102000));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA4_pointer, U.toPointer(0x103000));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA5_pointer, U.toPointer(0x104000));
            for (uint i = 0; i < 0x5000; i++) rom.Data[0x100000 + i] = 0;

            addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            for (int y = 0; y <= 5; y++)
                for (int x = 1; x <= 15; x++)
                { U.write_u16(rom.Data, addr, map[y * MAP_X + x]); addr += 2; }

            addr = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            for (int y = 0; y <= 5; y++)
                for (int x = 16; x <= 30; x++)
                { U.write_u16(rom.Data, addr, map[y * MAP_X + x]); addr += 2; }

            addr = rom.p32(rom.RomInfo.battle_screen_TSA3_pointer);
            for (int y = 13; y <= 19; y++)
                for (int x = 1; x <= 15; x++)
                { U.write_u16(rom.Data, addr, map[y * MAP_X + x]); addr += 2; }

            addr = rom.p32(rom.RomInfo.battle_screen_TSA4_pointer);
            for (int y = 13; y <= 19; y++)
                for (int x = 16; x <= 31; x++)
                { U.write_u16(rom.Data, addr, map[y * MAP_X + x]); addr += 2; }

            addr = rom.p32(rom.RomInfo.battle_screen_TSA5_pointer);
            for (int y = 0; y <= 19; y++)
                for (int x = 31; x <= 32; x++)
                {
                    int xx = x == 32 ? 0 : x;
                    U.write_u16(rom.Data, addr, map[y * MAP_X + xx]); addr += 2;
                }
        }
    }
}
