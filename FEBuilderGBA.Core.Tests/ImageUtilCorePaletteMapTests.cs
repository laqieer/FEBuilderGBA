// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageUtilCore.ByteToImage16TilePaletteMap (#846, NV5b) — the PURE
// FE8 World Map "big field map" decode primitive.
//
// Verifies, against a synthetic image + palette-map + 256-color palette:
//   * the default 480x320 IImage dims (60x40 tiles),
//   * two tiles whose PALETTE-MAP nibble selects DIFFERENT sub-palettes render
//     their correct colors (catches a wrong palette-map decode — a 16-color /
//     ignore-palette-map read would render both tiles from the same bank),
//   * a MULTI-ROW case exercising the per-row `+4` off-screen-margin quirk
//     (if the +4 were missing, row 1 would read the WRONG palette-map nibbles),
//   * a SHORT image renders a PARTIAL image (NOT a throw) — mirrors WF's
//     U.at-safe / clamp-to-image.Length behaviour (ImageUtil.cs:411-412,442-446),
//   * degenerate args (null buffers / non-positive dims / no ImageService) -> null.
//
// Pure primitive: it builds the RGBA buffer directly via CreateImage +
// SetPixelData (like DecodeHeaderTSA), so StubImage round-trips the pixels and
// AssertPixel can verify exact colors (StubImageService.Decode4bppTiles returns
// a blank StubImage, so the primitive must NOT delegate to it — it doesn't).
// [Collection("SharedState")] + CoreState.ImageService save/restore because the
// primitive reads CoreState.ImageService.GBAColorToRGBA.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageUtilCorePaletteMapTests
    {
        // GBA 5-5-5 colors and their StubImageService RGBA expansion (<<3).
        const ushort RED   = 0x001F; //   (248,   0,   0)
        const ushort GREEN = 0x03E0; //   (  0, 248,   0)
        const ushort BLUE  = 0x7C00; //   (  0,   0, 248)
        const ushort WHITE = 0x7FFF; //   (248, 248, 248)

        // =================================================================
        // Dimensions (default 480x320 = 60x40 tiles)
        // =================================================================

        [Fact]
        public void RendersDefaultDims_480x320()
        {
            WithImageService(() =>
            {
                // Full image (76,800 B) + a 256-color palette + a palette-map of
                // all-zero nibbles (sub-palette 0 everywhere).
                byte[] image = new byte[(480 * 320) / 2];
                byte[] palette = Build256Palette();
                byte[] paletteMap = new byte[2048]; // > 40*64 nibbles, all zero

                IImage img = ImageUtilCore.ByteToImage16TilePaletteMap(image, paletteMap, palette);

                Assert.NotNull(img);
                Assert.Equal(480, img.Width);
                Assert.Equal(320, img.Height);
            });
        }

        // =================================================================
        // Two tiles -> DIFFERENT sub-palettes via the palette-map nibble
        // =================================================================

        [Fact]
        public void TwoTiles_SelectDifferentSubPalettes_RenderCorrectColors()
        {
            // A 16x8 canvas = 2 tiles wide, 1 tile tall. Tile 0 and tile 1 each
            // carry pixel index 1 at (0,0) and pixel index 2 at (1,0). The
            // palette-map nibble for tile 0 = 0 (sub-palette 0) and for tile 1 =
            // 3 (sub-palette 3). In sub-palette 0: idx1=RED, idx2=GREEN. In
            // sub-palette 3 (color base 48): idx1=BLUE, idx2=WHITE. So:
            //   tile 0 pixel (0,0)=RED   (1,0)=GREEN
            //   tile 1 pixel (8,0)=BLUE  (9,0)=WHITE
            // A wrong/ignored palette-map decode would render tile 1 from
            // sub-palette 0 (RED/GREEN) instead of sub-palette 3 (BLUE/WHITE).
            WithImageService(() =>
            {
                int width = 16, height = 8;
                byte[] image = new byte[(width * height) / 2];
                SetTilePixel(image, 0, 0, 0, 1); // tile 0 (0,0) = idx 1
                SetTilePixel(image, 0, 1, 0, 2); // tile 0 (1,0) = idx 2
                SetTilePixel(image, 1, 0, 0, 1); // tile 1 (0,0) = idx 1
                SetTilePixel(image, 1, 1, 0, 2); // tile 1 (1,0) = idx 2

                byte[] palette = Build256Palette();
                // palette-map: nibble 0 (tile 0) = 0, nibble 1 (tile 1) = 3.
                byte[] paletteMap = new byte[8];
                SetNibble(paletteMap, 0, 0);
                SetNibble(paletteMap, 1, 3);

                IImage img = ImageUtilCore.ByteToImage16TilePaletteMap(
                    image, paletteMap, palette, width, height);

                Assert.NotNull(img);
                Assert.Equal(16, img.Width);
                Assert.Equal(8, img.Height);
                // tile 0 (sub-palette 0)
                AssertPixel(img, 0, 0, 248, 0, 0, 255);   // RED
                AssertPixel(img, 1, 0, 0, 248, 0, 255);   // GREEN
                // tile 1 (sub-palette 3) — proves the palette-map nibble select
                AssertPixel(img, 8, 0, 0, 0, 248, 255);   // BLUE
                AssertPixel(img, 9, 0, 248, 248, 248, 255); // WHITE
            });
        }

        // =================================================================
        // Multi-row -> exercises the per-row `+4` off-screen-margin quirk
        // =================================================================

        [Fact]
        public void MultiRow_PlusFourQuirk_SelectsCorrectRow1SubPalette()
        {
            // 16x16 = 2 tiles wide, 2 tiles tall. paletteMapIndex walk:
            //   row 0: tile@x0 -> nibble 0, tile@x8 -> nibble 1; then +4 quirk
            //          (index becomes 2, then +4 = 6).
            //   row 1: tile@x0 -> nibble 6, tile@x8 -> nibble 7.
            // So the off-screen margin consumes nibbles 2,3,4,5. We plant:
            //   nibble 0 = 0 (sub-palette 0: idx1=RED)
            //   nibble 6 = 3 (sub-palette 3: idx1=BLUE)
            // and a DECOY at nibble 2 = 1 (sub-palette 1: idx1=GREEN) — the
            // value row 1 col 0 would WRONGLY read if the +4 quirk were missing.
            // Row 0 col 0 must be RED; row 1 col 0 must be BLUE (NOT GREEN).
            WithImageService(() =>
            {
                int width = 16, height = 16;
                byte[] image = new byte[(width * height) / 2];
                SetTilePixelAbs(image, width, 0, 0, 1); // (0,0) row0 col0 = idx1
                SetTilePixelAbs(image, width, 0, 8, 1); // (0,8) row1 col0 = idx1

                byte[] palette = Build256Palette();
                byte[] paletteMap = new byte[8];
                SetNibble(paletteMap, 0, 0); // row0 col0 -> sub-palette 0 (RED)
                SetNibble(paletteMap, 2, 1); // DECOY off-screen margin -> sub-palette 1 (GREEN)
                SetNibble(paletteMap, 6, 3); // row1 col0 -> sub-palette 3 (BLUE)

                IImage img = ImageUtilCore.ByteToImage16TilePaletteMap(
                    image, paletteMap, palette, width, height);

                Assert.NotNull(img);
                AssertPixel(img, 0, 0, 248, 0, 0, 255); // row 0 col 0 = RED  (nibble 0)
                AssertPixel(img, 0, 8, 0, 0, 248, 255); // row 1 col 0 = BLUE (nibble 6, NOT decoy nibble 2 GREEN)
            });
        }

        // =================================================================
        // Short image -> PARTIAL render, NOT a throw (mirrors WF)
        // =================================================================

        [Fact]
        public void ShortImage_RendersPartial_DoesNotThrow()
        {
            // 16x8 = 2 tiles wide. Provide only ONE tile's worth of image bytes
            // (32) for a 2-tile row. WF clamps length to image.Length and returns
            // the partially-filled bitmap (ImageUtil.cs:411-412,442-446) — it does
            // NOT throw. Tile 0 (sub-palette 0) renders; tile 1 (no bytes) stays
            // at the zero/transparent default.
            WithImageService(() =>
            {
                int width = 16, height = 8;
                byte[] image = new byte[32]; // only 1 of the 2 tiles in the row
                SetTilePixel(image, 0, 0, 0, 1); // tile 0 (0,0) = idx 1 -> RED

                byte[] palette = Build256Palette();
                byte[] paletteMap = new byte[8];
                SetNibble(paletteMap, 0, 0);

                IImage img = ImageUtilCore.ByteToImage16TilePaletteMap(
                    image, paletteMap, palette, width, height);

                Assert.NotNull(img); // partial, NOT null, NOT a throw
                Assert.Equal(16, img.Width);
                Assert.Equal(8, img.Height);
                AssertPixel(img, 0, 0, 248, 0, 0, 255); // tile 0 rendered = RED
                // tile 1 (x=8) had no image bytes -> default (0,0,0,0).
                AssertPixel(img, 8, 0, 0, 0, 0, 0);
            });
        }

        [Fact]
        public void ShortPaletteMap_DoesNotThrow_UsesSubPaletteZero()
        {
            // The palette-map read is U.at-safe (WF :419): an out-of-range nibble
            // returns 0 -> sub-palette 0. A 16x8 canvas (2 tiles) with a ZERO-
            // length palette-map must NOT throw; both tiles fall back to
            // sub-palette 0. (Locks the U.at-safe palette-map access.)
            WithImageService(() =>
            {
                int width = 16, height = 8;
                byte[] image = new byte[(width * height) / 2];
                SetTilePixel(image, 0, 0, 0, 1); // tile 0 (0,0) = idx 1
                SetTilePixel(image, 1, 0, 0, 2); // tile 1 (0,0) = idx 2

                byte[] palette = Build256Palette();
                byte[] paletteMap = Array.Empty<byte>(); // all reads -> 0

                IImage img = ImageUtilCore.ByteToImage16TilePaletteMap(
                    image, paletteMap, palette, width, height);

                Assert.NotNull(img);
                AssertPixel(img, 0, 0, 248, 0, 0, 255); // tile 0 idx1 sub0 = RED
                AssertPixel(img, 8, 0, 0, 248, 0, 255); // tile 1 idx2 sub0 = GREEN
            });
        }

        // =================================================================
        // Degenerate args -> null (no throw)
        // =================================================================

        [Fact]
        public void NullImage_ReturnsNull()
        {
            WithImageService(() =>
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(
                    null, new byte[8], Build256Palette())));
        }

        [Fact]
        public void NullPaletteMap_ReturnsNull()
        {
            WithImageService(() =>
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(
                    new byte[16], null, Build256Palette())));
        }

        [Fact]
        public void NullPalette_ReturnsNull()
        {
            WithImageService(() =>
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(
                    new byte[16], new byte[8], null)));
        }

        [Fact]
        public void NonPositiveDims_ReturnNull()
        {
            WithImageService(() =>
            {
                byte[] image = new byte[16];
                byte[] palette = Build256Palette();
                byte[] paletteMap = new byte[8];
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(image, paletteMap, palette, 0, 8));
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(image, paletteMap, palette, 16, 0));
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(image, paletteMap, palette, -8, 8));
            });
        }

        [Fact]
        public void NoImageService_ReturnsNull()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                Assert.Null(ImageUtilCore.ByteToImage16TilePaletteMap(
                    new byte[16], new byte[8], Build256Palette(), 16, 8));
            }
            finally { CoreState.ImageService = saved; }
        }

        // =================================================================
        // Harness / helpers
        // =================================================================

        static void WithImageService(Action body)
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                body();
            }
            finally { CoreState.ImageService = saved; }
        }

        /// <summary>256-color palette where, for EVERY sub-palette bank b (0..15),
        /// index 1 = RED, index 2 = GREEN, and additionally bank 3's index 1 =
        /// BLUE, index 2 = WHITE (the distinctive bank the tests select). Bank
        /// base color = b*16; byte offset = (b*16 + idx) * 2.</summary>
        static byte[] Build256Palette()
        {
            byte[] pal = new byte[256 * 2];
            for (int bank = 0; bank < 16; bank++)
            {
                SetColor(pal, bank, 1, RED);
                SetColor(pal, bank, 2, GREEN);
            }
            // Override bank 3 so a correct sub-palette-3 select is distinguishable
            // from sub-palette 0 (RED/GREEN).
            SetColor(pal, 3, 1, BLUE);
            SetColor(pal, 3, 2, WHITE);
            return pal;
        }

        /// <summary>Write a 256-palette color: bank*16 + index color slot.</summary>
        static void SetColor(byte[] pal, int bank, int index, ushort c)
        {
            int colorSlot = bank * 16 + index;
            int off = colorSlot * 2;
            pal[off] = (byte)(c & 0xFF);
            pal[off + 1] = (byte)(c >> 8);
        }

        /// <summary>Set a 4bpp pixel inside tile <paramref name="tile"/> of a
        /// linear tile stream (tile = 32 bytes; row-major y*4 + x/2; low nibble =
        /// even x). Same layout WF / DecodeTileToPixels read.</summary>
        static void SetTilePixel(byte[] image, int tile, int x, int y, int colorIndex)
        {
            int pos = tile * 32 + y * 4 + x / 2;
            byte b = image[pos];
            if (x % 2 == 0) b = (byte)((b & 0xF0) | (colorIndex & 0x0F));
            else b = (byte)((b & 0x0F) | ((colorIndex & 0x0F) << 4));
            image[pos] = b;
        }

        /// <summary>Set a 4bpp pixel by ABSOLUTE canvas (absX, absY) for a stream
        /// of <paramref name="width"/>-pixel-wide tile rows. Maps the absolute
        /// pixel back to its sequential tile index + in-tile (x,y).</summary>
        static void SetTilePixelAbs(byte[] image, int width, int absX, int absY, int colorIndex)
        {
            int tilesPerRow = width / 8;
            int tileCol = absX / 8;
            int tileRow = absY / 8;
            int tile = tileRow * tilesPerRow + tileCol;
            SetTilePixel(image, tile, absX % 8, absY % 8, colorIndex);
        }

        /// <summary>Set the <paramref name="nibbleIndex"/>-th nibble of a packed
        /// nibble stream (two nibbles/byte; even index = low nibble).</summary>
        static void SetNibble(byte[] buf, int nibbleIndex, int value)
        {
            int bytePos = nibbleIndex / 2;
            byte b = buf[bytePos];
            if ((nibbleIndex & 1) == 0) b = (byte)((b & 0xF0) | (value & 0x0F));
            else b = (byte)((b & 0x0F) | ((value & 0x0F) << 4));
            buf[bytePos] = b;
        }

        static void AssertPixel(IImage img, int x, int y, byte r, byte g, byte b, byte a)
        {
            byte[] px = img.GetPixelData();
            int idx = (y * img.Width + x) * 4;
            Assert.Equal(r, px[idx + 0]);
            Assert.Equal(g, px[idx + 1]);
            Assert.Equal(b, px[idx + 2]);
            Assert.Equal(a, px[idx + 3]);
        }
    }
}
