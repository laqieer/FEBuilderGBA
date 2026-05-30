// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBattleScreenCore.RenderChipsetPreview (#805, follow-up to #802):
// the cross-platform chip-list render that mirrors the WinForms
// ImageBattleScreenForm.MakeCHIPLIST() flip/palette-bank permutation grid.
//
// MakeCHIPLIST geometry (WF ImageBattleScreenForm.cs:187-227):
//   * Canvas = ChipCache.Width * 4 * 2 (= 8*4*2 = 64) wide x ChipCache.Height tall,
//     where ChipCache is the 8px-wide tile sheet (one 8x8 tile per 8px row).
//   * For each tile row y, 8 adjacent 8px columns at x = 8*0 .. 8*7:
//       col 0: original  (palette bank 0)    col 4: original  (palette bank 1)
//       col 1: H-flip    (palette bank 0)    col 5: H-flip    (palette bank 1)
//       col 2: V-flip    (palette bank 0)    col 6: V-flip    (palette bank 1)
//       col 3: HV-flip   (palette bank 0)    col 7: HV-flip   (palette bank 1)
//   * index 0 renders OPAQUE -- WF blits with transparent_index = 0xFF
//     (never matches a 0..15 index), same as the battle-screen preview.
//
// Reuses the #802 synthetic-ROM harness (same StubImageService/StubImage as the
// battle anime tests) so the rendered IImage carries real RGBA pixel data we can
// assert on. The synthetic ROM plants image1..image5 each as exactly ONE 4bpp
// tile, so the concatenated tileset is 5 tiles (tile indices 0..4):
//   tile 0 (image1) = SolidTile(5)  -> green fill   (bank0 idx5 = 0x03E0)
//   tile 1 (image2) = SolidTile(3)  -> red fill     (bank0 idx3 = 0x001F)
//   tile 2 (image3) = CornerTile(5) -> green ONLY at src (0,0); else index 0
//   tile 3 (image4) = SolidTile(0)  -> index-0 fill (bank0 idx0 = white 0x7FFF)
//   tile 4 (image5) = SolidTile(5)  -> green fill
// bank1 idx5 = blue (0x7C00).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageBattleScreenChipsetPreviewTests
    {
        // Storage offsets for the planted battle-screen sources (mirror #802 test).
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

        // 5 image strips x 1 tile each -> 5 tiles in the concatenated sheet.
        const int TILE_COUNT = 5;

        // ----------------------------------------------------------------
        // Dimensions -- Width*4*2 (= 64) x Height (= tileCount*8 = 40)
        // ----------------------------------------------------------------

        [Fact]
        public void RenderChipsetPreview_ProducesExpectedDimensions()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);
            // ChipCache.Width=8 -> canvas width = 8*4*2 = 64.
            Assert.Equal(8 * 4 * 2, img.Width);
            // 5 tiles -> 5 * 8 = 40 px tall.
            Assert.Equal(TILE_COUNT * 8, img.Height);
        }

        // ----------------------------------------------------------------
        // Column order parity -- bank 0 {orig, Hflip, Vflip, HVflip}
        // ----------------------------------------------------------------

        // Use the asymmetric corner tile (tile 2 = image3) so each flip moves the
        // single distinctive green pixel to a column-specific corner. Row y for
        // tile 2 is tileY = 2*8 = 16.
        [Fact]
        public void RenderChipsetPreview_Bank0_OriginalColumn_HasCornerTopLeft()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // col 0 (orig, bank 0): corner pixel src(0,0) stays at (0, 16). Green.
            AssertPixel(img, 0, 16, 0, 248, 0, 255);
        }

        [Fact]
        public void RenderChipsetPreview_Bank0_HFlipColumn_HasCornerTopRight()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // col 1 (Hflip, bank 0) at x base 8: corner moves to x=8+7=15, y=16.
            AssertPixel(img, 15, 16, 0, 248, 0, 255);
            // And the un-flipped top-left of that column is now index 0 (blank).
            // SolidTile? No -- CornerTile's other pixels are index 0 -> bank0 idx0
            // is white & opaque. So just verify the green landed at the corner.
        }

        [Fact]
        public void RenderChipsetPreview_Bank0_VFlipColumn_HasCornerBottomLeft()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // col 2 (Vflip, bank 0) at x base 16: corner moves to x=16, y=16+7=23.
            AssertPixel(img, 16, 23, 0, 248, 0, 255);
        }

        [Fact]
        public void RenderChipsetPreview_Bank0_HVFlipColumn_HasCornerBottomRight()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // col 3 (HVflip, bank 0) at x base 24: corner moves to x=24+7=31, y=23.
            AssertPixel(img, 31, 23, 0, 248, 0, 255);
        }

        // ----------------------------------------------------------------
        // Palette-bank selection -- columns 4..7 use bank 1
        // ----------------------------------------------------------------

        [Fact]
        public void RenderChipsetPreview_Bank1_OriginalColumn_UsesBank1Palette()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // col 4 (orig, bank 1) at x base 32. Tile 2 corner index 5 -> bank 1
            // index 5 = blue (0x7C00). Corner stays top-left at (32, 16).
            AssertPixel(img, 32, 16, 0, 0, 248, 255);
        }

        [Fact]
        public void RenderChipsetPreview_Bank0VsBank1_SolidTile_DiffersByPalette()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // Tile 0 (image1) = SolidTile(5). Row y = 0.
            // col 0 (bank 0) -> green; col 4 (bank 1) -> blue. Same source tile,
            // different palette bank, proving the bank selection works.
            AssertPixel(img, 0, 0, 0, 248, 0, 255);    // bank0 idx5 green
            AssertPixel(img, 32, 0, 0, 0, 248, 255);   // bank1 idx5 blue
        }

        // ----------------------------------------------------------------
        // Distinct tile per row -- red (image2) sits on row 1
        // ----------------------------------------------------------------

        [Fact]
        public void RenderChipsetPreview_SecondTileRow_RendersImage2Red()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // Tile 1 (image2) = SolidTile(3) red, row y = 8. col 0 (orig, bank 0).
            AssertPixel(img, 0, 8, 248, 0, 0, 255);
            AssertPixel(img, 4, 8 + 4, 248, 0, 0, 255); // anywhere in the cell
        }

        // ----------------------------------------------------------------
        // Alpha -- palette index 0 must be OPAQUE
        // ----------------------------------------------------------------

        [Fact]
        public void RenderChipsetPreview_PaletteIndex0_RendersOpaque()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            IImage img = ImageBattleScreenCore.RenderChipsetPreview(rom);
            Assert.NotNull(img);

            // Tile 3 (image4) = SolidTile(0): index 0 fill. Row y = 24.
            // bank0 idx0 = white (0x7FFF). Per WF MakeCHIPLIST transparent_index
            // = 0xFF, so index 0 must render OPAQUE (alpha 255, not 0).
            AssertPixel(img, 0, 24, 248, 248, 248, 255);
            // Several pixels across the cell to confirm a full opaque fill.
            AssertPixel(img, 7, 24 + 7, 248, 248, 248, 255);
            AssertPixel(img, 3, 24 + 2, 248, 248, 248, 255);
        }

        // ----------------------------------------------------------------
        // Null-safety
        // ----------------------------------------------------------------

        [Fact]
        public void RenderChipsetPreview_NullRom_ReturnsNull()
        {
            using var _ = EnsureImageService();
            Assert.Null(ImageBattleScreenCore.RenderChipsetPreview(null));
        }

        [Fact]
        public void RenderChipsetPreview_CorruptImagePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Point image3 at an out-of-bounds offset -> isSafetyOffset fails ->
            // the shared loader fails -> render returns null (no crash).
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(0x1FFFFFF0));

            Assert.Null(ImageBattleScreenCore.RenderChipsetPreview(rom));
        }

        [Fact]
        public void RenderChipsetPreview_CorruptPalettePointer_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Palette pointer out of bounds -> loader fails -> null.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(0x1FFFFFF0));

            Assert.Null(ImageBattleScreenCore.RenderChipsetPreview(rom));
        }

        [Fact]
        public void RenderChipsetPreview_TruncatedImageStream_ReturnsNull()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // Plant a TRUNCATED LZ77 stream for image3 at the end of ROM: a valid
            // header (0x10 + a 0x1000-byte advertised size) but only a few bytes
            // before Data.Length. The shared loader's getCompressedSize guard
            // returns 0 -> render returns null (matching the #802 contract).
            uint imageAddr = (uint)rom.Data.Length - 8;
            rom.Data[imageAddr + 0] = 0x10;
            rom.Data[imageAddr + 1] = 0x00;
            rom.Data[imageAddr + 2] = 0x10;
            rom.Data[imageAddr + 3] = 0x00;
            rom.Data[imageAddr + 4] = 0x00; // control: next 8 blocks are literals
            rom.Data[imageAddr + 5] = 0xAA;
            rom.Data[imageAddr + 6] = 0xBB;
            rom.Data[imageAddr + 7] = 0xCC;
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(imageAddr));

            // Sanity: confirm the truncation is real at the LZ77 level.
            Assert.Equal(0u, LZ77.getCompressedSize(rom.Data, imageAddr));

            Assert.Null(ImageBattleScreenCore.RenderChipsetPreview(rom));
        }

        [Fact]
        public void RenderChipsetPreview_NoImageService_ReturnsNull()
        {
            // No ImageService scope -> RenderChipsetPreview must return null.
            var prev = CoreState.ImageService;
            CoreState.ImageService = null;
            try
            {
                var rom = MakeRom();
                Assert.Null(ImageBattleScreenCore.RenderChipsetPreview(rom));
            }
            finally { CoreState.ImageService = prev; }
        }

        // ----------------------------------------------------------------
        // #802 regression -- the battle preview must stay green after the
        // shared-loader extraction (behavior-preserving refactor).
        // ----------------------------------------------------------------

        [Fact]
        public void RenderBattleScreenPreview_StillRendersAfterLoaderExtraction()
        {
            using var _ = EnsureImageService();
            var rom = MakeRom();

            // tile 0 (image1) green at cell (y=0, x=1) -> pixel (8, 0). This is the
            // same assertion as ImageBattleScreenPreviewTests
            // RenderBattleScreenPreview_TileFromImage1_RendersExpectedColor, kept
            // here to prove TryLoadChipsetAndPalette is behavior-preserving.
            ushort[] map = new ushort[ImageBattleScreenCore.MAP_SIZE];
            map[0 * ImageBattleScreenCore.MAP_X + 1] = MakeCell(tile: 0, flip: 0, pal: 0);
            PlantMap(rom, map);

            IImage img = ImageBattleScreenCore.RenderBattleScreenPreview(rom);
            Assert.NotNull(img);
            Assert.Equal(256, img.Width);
            Assert.Equal(160, img.Height);
            AssertPixel(img, 8, 0, 0, 248, 0, 255); // bank0 idx5 green, opaque
        }

        // ----------------------------------------------------------------
        // Helpers (mirror ImageBattleScreenPreviewTests)
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

        static void PlantImageStrip(ROM rom, uint slot, uint offset, byte[] rawTile)
        {
            byte[] comp = LZ77.compress(rawTile);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
            U.write_u32(rom.Data, slot, U.toPointer(offset));
        }

        static byte[] SolidTile(int index)
        {
            byte packed = (byte)(((index & 0x0f) << 4) | (index & 0x0f));
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++) tile[i] = packed;
            return tile;
        }

        static byte[] CornerTile(int index)
        {
            byte[] tile = new byte[32];
            // src (0,0): row 0, byte 0, low nibble (even x => low nibble).
            tile[0] = (byte)(index & 0x0f);
            return tile;
        }

        static void PlantMap(ROM rom, ushort[] map)
        {
            uint addr;
            int MAP_X = ImageBattleScreenCore.MAP_X;

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
