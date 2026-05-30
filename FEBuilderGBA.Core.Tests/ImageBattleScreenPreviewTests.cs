// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for ImageBattleScreenCore.RenderBattleScreenPreview (#802):
// the cross-platform live battle-screen preview render that mirrors the WinForms
// ImageBattleScreenForm.GetChipImage + MakeBattleScreen pipeline.
//
// The preview composites:
//   - Blocker 1: a RAW 16-bank GBA palette (512 bytes, NOT LZ77) at the
//     battle_screen_palette_pointer.
//   - Blocker 2: image1..image5 LZ77 strips (each 8px wide) concatenated
//     vertically into one tileset.
//   - Blocker 3: the 32x20 TSA grid from LoadBattleScreen, normalized from the
//     WF cell layout (tile=m&0xff, flip=(m>>8)&0x0f, pal=m>>12; flip 0=none /
//     4=h / 8=v / other-nonzero=both) into DecodeTSA's GBA-standard layout.
//   - Blocker 4: palette index 0 renders OPAQUE (alpha 255) to match WF BitBlt
//     (transparent_index=0xFF never matches a 0..15 index).
//
// These tests use the same internal StubImageService/StubImage as the battle
// anime tests (defined in BattleAnimeDetailTests.cs) so the rendered IImage
// carries real RGBA pixel data we can assert on.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageBattleScreenPreviewTests
    {
        // Storage offsets for the planted battle-screen sources. Chosen well
        // past the header, with room between each for the small blocks.
        const uint TSA1_OFFSET    = 0x100000;
        const uint TSA2_OFFSET    = 0x101000;
        const uint TSA3_OFFSET    = 0x102000;
        const uint TSA4_OFFSET    = 0x103000;
        const uint TSA5_OFFSET    = 0x104000;
        const uint PALETTE_OFFSET = 0x105000;
        const uint IMAGE1_OFFSET  = 0x110000;
        const uint IMAGE2_OFFSET  = 0x111000;
        const uint IMAGE3_OFFSET  = 0x112000;
        const uint IMAGE4_OFFSET  = 0x113000;
        const uint IMAGE5_OFFSET  = 0x114000;

        const int MAP_X = 32;
        const int MAP_Y = 20;

        // ----------------------------------------------------------------
        // Dimensions
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_Produces256x160Image()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            ushort[] map = new ushort[MAP_X * MAP_Y];
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);
            Assert.Equal(256, img.Width);
            Assert.Equal(160, img.Height);
        }

        // ----------------------------------------------------------------
        // Tile pixel parity -- a tile from image1 AND a tile from image2..5
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_TileFromImage1_RendersExpectedColor()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // tile 0 lives in image1 (image1 = exactly 1 tile = 32 bytes).
            // image1 tile 0 is filled with color index 5; bank 0 index 5 = green.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            // Cell (y=0, x=1) -> renders at pixel (8, 0). tile=0, pal=0, no flip.
            map[0 * MAP_X + 1] = MakeCell(tile: 0, flip: 0, pal: 0);
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // bank 0 index 5 = 0x03E0 -> R=0 G=248 B=0, opaque.
            AssertPixel(img, 8, 0, 0, 248, 0, 255);
        }

        [Fact]
        public void RenderBattleScreenPreview_TileFromImage2_RendersExpectedColor()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image1 = 1 tile (32 bytes) so tile index 1 is image2's first tile.
            // image2 tile is filled with color index 3; bank 0 index 3 = red.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            // Cell (y=0, x=2) -> pixel (16, 0). tile=1 (image2), pal=0.
            map[0 * MAP_X + 2] = MakeCell(tile: 1, flip: 0, pal: 0);
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // bank 0 index 3 = 0x001F -> R=248 G=0 B=0, opaque.
            AssertPixel(img, 16, 0, 248, 0, 0, 255);
        }

        // ----------------------------------------------------------------
        // Palette-bank selection (pal nibble) parity
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_PaletteBankSelectsColor()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image1 tile 0 = color index 5. Using palette bank 1, index 5 = blue.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            map[0 * MAP_X + 3] = MakeCell(tile: 0, flip: 0, pal: 1); // pixel (24,0)
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // bank 1 index 5 = 0x7C00 -> R=0 G=0 B=248, opaque.
            AssertPixel(img, 24, 0, 0, 0, 248, 255);
        }

        // ----------------------------------------------------------------
        // Flip parity -- nibble 0 / 4 / 8 / other
        // ----------------------------------------------------------------

        [Theory]
        [InlineData(0)]   // none
        [InlineData(4)]   // horizontal
        [InlineData(8)]   // vertical
        [InlineData(12)]  // other-nonzero -> both
        [InlineData(1)]   // other-nonzero -> both (WF default branch)
        public void RenderBattleScreenPreview_FlipNibble_RendersWithoutCorruption(int flip)
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Use the asymmetric corner tile in image3 so flips actually move a
            // distinctive pixel. image3 = tile index 2 (image1=1 tile,
            // image2=1 tile). Tile has color index 5 only at its top-left
            // source pixel (0,0); everything else is index 0.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            map[0 * MAP_X + 4] = MakeCell(tile: 2, flip: (uint)flip, pal: 0); // pixel base (32,0)
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // Determine where the corner pixel (source 0,0 = green index 5) lands.
            bool h, v;
            if (flip == 0) { h = false; v = false; }
            else if (flip == 4) { h = true; v = false; }
            else if (flip == 8) { h = false; v = true; }
            else { h = true; v = true; }

            int baseX = 4 * 8;
            int baseY = 0;
            int gx = baseX + (h ? 7 : 0);
            int gy = baseY + (v ? 7 : 0);

            // The corner pixel is green (index 5, bank 0 = 0x03E0).
            AssertPixel(img, gx, gy, 0, 248, 0, 255);
        }

        // ----------------------------------------------------------------
        // TSA normalization -- bit8/bit9 set must NOT pollute the tile index
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_HighBitsInFlipNibble_TileIndexStaysLow8Bits()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Raw WF cell with bit8 (0x100) AND bit9 (0x200) set in the flip
            // nibble plus tile=1 (image2 red). WF reads tile = m & 0xff = 1,
            // flip = (m>>8)&0x0f = 0x3 (-> both flips). The tile must resolve to
            // image2's tile (red), NOT some out-of-range index from the raw 16-bit
            // value 0x0301.
            ushort raw = 0x0301; // tile=0x01, flip nibble=0x3, pal=0
            ushort[] map = new ushort[MAP_X * MAP_Y];
            map[0 * MAP_X + 5] = raw; // pixel base (40,0)
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // image2 tile is a SOLID red fill, so flip does not matter -- any of
            // its 8x8 pixels is red. Check the cell center.
            AssertPixel(img, 40 + 3, 0 + 3, 248, 0, 0, 255);
        }

        // ----------------------------------------------------------------
        // Alpha -- palette index 0 must be OPAQUE (Blocker 4)
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_PaletteIndex0_RendersOpaque()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image4 = tile index 3: SOLID color index 0 fill. Bank 0 index 0 =
            // white (0x7FFF). Per WF BitBlt the battle screen has no transparent
            // index, so index 0 must render OPAQUE.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            map[0 * MAP_X + 6] = MakeCell(tile: 3, flip: 0, pal: 0); // pixel base (48,0)
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // White, fully opaque -- alpha MUST be 255 (not 0).
            AssertPixel(img, 48, 0, 248, 248, 248, 255);
        }

        [Fact]
        public void RenderBattleScreenPreview_NonzeroIndex_RGBAParity()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // image5 = tile index 4: SOLID color index 5 fill -> green at full
            // alpha across the whole 8x8 cell.
            ushort[] map = new ushort[MAP_X * MAP_Y];
            map[0 * MAP_X + 7] = MakeCell(tile: 4, flip: 0, pal: 0); // pixel base (56,0)
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);

            // Check several pixels across the 8x8 cell -- all green, opaque.
            AssertPixel(img, 56 + 0, 0 + 0, 0, 248, 0, 255);
            AssertPixel(img, 56 + 7, 0 + 7, 0, 248, 0, 255);
            AssertPixel(img, 56 + 4, 0 + 2, 0, 248, 0, 255);
        }

        // ----------------------------------------------------------------
        // Null-safety
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_NullRom_ReturnsNull()
        {
            using var _ = EnsureImageService();
            Assert.Null(ImageBattleScreenCore.RenderBattleScreenPreview(null));
        }

        // NOTE on the LoadBattleScreen==null guard: LoadBattleScreen only
        // returns null when rom==null OR rom.RomInfo==null. A real synthetic ROM
        // always has a non-null RomInfo, so the rom==null branch (covered by
        // RenderBattleScreenPreview_NullRom_ReturnsNull) is the reachable case.
        // This test instead pins the adjacent behavior: corrupt TSA pointers
        // produce a zero (but non-null) map, and the render must still succeed
        // (no crash, no null) because the REQUIRED sources (images + palette)
        // are intact -- a zero TSA grid is a valid, blank layout.
        [Fact]
        public void RenderBattleScreenPreview_CorruptTsaButValidSources_StillRenders()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(0));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA2_pointer, U.toPointer(0));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(0));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA4_pointer, U.toPointer(0));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA5_pointer, U.toPointer(0));

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img); // zero TSA map is valid -> blank render
            Assert.Equal(256, img.Width);
        }

        [Fact]
        public void RenderBattleScreenPreview_CorruptImagePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            ushort[] map = new ushort[MAP_X * MAP_Y];
            PlantMap(rom, map);

            // Point image3 at an out-of-bounds offset -> isSafetyOffset fails.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(0x1FFFFFF0));

            Assert.Null(ImageBattleScreenCore.RenderBattleScreenPreview(rom));
        }

        [Fact]
        public void RenderBattleScreenPreview_ImagePointerLZ77Garbage_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            ushort[] map = new ushort[MAP_X * MAP_Y];
            PlantMap(rom, map);

            // Point image2 at a valid-but-non-LZ77 region (first byte != 0x10)
            // -> LZ77.decompress returns empty -> render fails.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image2_pointer, U.toPointer(0x120000));
            for (uint i = 0; i < 64; i++) rom.Data[0x120000 + i] = 0xAB; // not an LZ77 stream

            Assert.Null(ImageBattleScreenCore.RenderBattleScreenPreview(rom));
        }

        [Fact]
        public void RenderBattleScreenPreview_CorruptPalettePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            ushort[] map = new ushort[MAP_X * MAP_Y];
            PlantMap(rom, map);

            // Palette pointer out of bounds.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(0x1FFFFFF0));

            Assert.Null(ImageBattleScreenCore.RenderBattleScreenPreview(rom));
        }

        [Fact]
        public void RenderBattleScreenPreview_ShortPalette_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();
            ushort[] map = new ushort[MAP_X * MAP_Y];
            PlantMap(rom, map);

            // Point the palette at the very end of ROM so a 512-byte read
            // overflows -> render must return null (does not partial-render).
            uint nearEnd = (uint)rom.Data.Length - 256;
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(nearEnd));

            Assert.Null(ImageBattleScreenCore.RenderBattleScreenPreview(rom));
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static System.IDisposable EnsureImageService()
        {
            return new ImageServiceScope();
        }

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

        /// <summary>Pack a raw WF battle-screen TSA cell.</summary>
        static ushort MakeCell(uint tile, uint flip, uint pal)
        {
            return (ushort)((tile & 0xff) | ((flip & 0x0f) << 8) | ((pal & 0x0f) << 12));
        }

        /// <summary>
        /// Assert the RGBA pixel at (x,y) in a non-indexed IImage.
        /// </summary>
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

        /// <summary>
        /// Build a synthetic FE8U ROM with planted image1..5 LZ77 strips, a
        /// 512-byte RAW palette, and TSA pointer slots wired to our storage.
        /// </summary>
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            System.Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // TSA pointer slots -> our TSA storage offsets, regions zero-filled.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(TSA1_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA2_pointer, U.toPointer(TSA2_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(TSA3_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA4_pointer, U.toPointer(TSA4_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA5_pointer, U.toPointer(TSA5_OFFSET));
            for (uint i = 0; i < 0x1000; i++)
            {
                rom.Data[TSA1_OFFSET + i] = 0;
                rom.Data[TSA2_OFFSET + i] = 0;
                rom.Data[TSA3_OFFSET + i] = 0;
                rom.Data[TSA4_OFFSET + i] = 0;
                rom.Data[TSA5_OFFSET + i] = 0;
            }

            // ---- Palette: 16 banks x 16 colors x 2 bytes = 512 bytes ----
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(PALETTE_OFFSET));
            for (uint i = 0; i < 512; i++) rom.Data[PALETTE_OFFSET + i] = 0;
            // bank 0 index 0 = white (0x7FFF), index 3 = red (0x001F),
            //         index 5 = green (0x03E0).
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 0) * 2, 0x7FFF);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 3) * 2, 0x001F);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 5) * 2, 0x03E0);
            // bank 1 index 5 = blue (0x7C00).
            U.write_u16(rom.Data, PALETTE_OFFSET + (1 * 16 + 5) * 2, 0x7C00);

            // ---- image1..5: each exactly ONE 4bpp tile (32 bytes) ----
            // tile indices after concatenation:
            //   image1 -> tile 0 (color index 5 fill, green)
            //   image2 -> tile 1 (color index 3 fill, red)
            //   image3 -> tile 2 (corner-only color index 5 at src (0,0))
            //   image4 -> tile 3 (color index 0 fill, white/opaque test)
            //   image5 -> tile 4 (color index 5 fill, green)
            PlantImageStrip(rom, rom.RomInfo.battle_screen_image1_pointer, IMAGE1_OFFSET, SolidTile(5));
            PlantImageStrip(rom, rom.RomInfo.battle_screen_image2_pointer, IMAGE2_OFFSET, SolidTile(3));
            PlantImageStrip(rom, rom.RomInfo.battle_screen_image3_pointer, IMAGE3_OFFSET, CornerTile(5));
            PlantImageStrip(rom, rom.RomInfo.battle_screen_image4_pointer, IMAGE4_OFFSET, SolidTile(0));
            PlantImageStrip(rom, rom.RomInfo.battle_screen_image5_pointer, IMAGE5_OFFSET, SolidTile(5));

            return rom;
        }

        /// <summary>
        /// LZ77-compress a raw tile and plant it at <paramref name="offset"/>,
        /// wiring the pointer <paramref name="slot"/> to point at it.
        /// </summary>
        static void PlantImageStrip(ROM rom, uint slot, uint offset, byte[] rawTile)
        {
            byte[] comp = LZ77.compress(rawTile);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
            U.write_u32(rom.Data, slot, U.toPointer(offset));
        }

        /// <summary>A 4bpp 8x8 tile (32 bytes) with every pixel = index.</summary>
        static byte[] SolidTile(int index)
        {
            byte packed = (byte)(((index & 0x0f) << 4) | (index & 0x0f));
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++) tile[i] = packed;
            return tile;
        }

        /// <summary>
        /// A 4bpp 8x8 tile where ONLY source pixel (0,0) has the given color
        /// index; all other pixels are index 0. Lets flip tests detect where the
        /// distinctive pixel lands after h/v flips.
        /// </summary>
        static byte[] CornerTile(int index)
        {
            byte[] tile = new byte[32];
            // src (0,0): row 0, byte 0, low nibble (even x => low nibble).
            tile[0] = (byte)(index & 0x0f);
            return tile;
        }

        /// <summary>
        /// Inverse of LoadBattleScreen: write a full 32x20 map back into the 5
        /// TSA regions so RenderBattleScreenPreview reads exactly these cells.
        /// </summary>
        static void PlantMap(ROM rom, ushort[] map)
        {
            uint addr;

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
