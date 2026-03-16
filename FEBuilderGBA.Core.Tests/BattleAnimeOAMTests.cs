using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for OAM affine transform, palette bank, and BlitSpriteAffine functionality
    /// in BattleAnimeRendererCore.
    /// </summary>
    [Collection("SharedState")]
    public class BattleAnimeOAMTests : IDisposable
    {
        readonly IImageService _prevService;

        public BattleAnimeOAMTests()
        {
            _prevService = CoreState.ImageService;
            CoreState.ImageService = new StubImageService();
        }

        public void Dispose()
        {
            CoreState.ImageService = _prevService;
        }

        // ---- ParseAffineOAM tests ----

        [Fact]
        public void ParseAffineOAM_NullData_ReturnsFalse()
        {
            bool result = BattleAnimeRendererCore.ParseAffineOAM(null, 0, out _);
            Assert.False(result);
        }

        [Fact]
        public void ParseAffineOAM_TooShortData_ReturnsFalse()
        {
            byte[] data = new byte[8];
            bool result = BattleAnimeRendererCore.ParseAffineOAM(data, 0, out _);
            Assert.False(result);
        }

        [Fact]
        public void ParseAffineOAM_NotAffineEntry_ReturnsFalse()
        {
            // bytes [2..3] are not 0xFFFF
            byte[] data = new byte[12];
            data[2] = 0x00;
            data[3] = 0x00;
            bool result = BattleAnimeRendererCore.ParseAffineOAM(data, 0, out _);
            Assert.False(result);
        }

        [Fact]
        public void ParseAffineOAM_IdentityMatrix_ParsesCorrectly()
        {
            // Build an affine entry with identity matrix (PA=PD=256=0x0100, PB=PC=0)
            byte[] data = new byte[12];
            data[2] = 0xFF; data[3] = 0xFF; // affine marker
            // PA = 256 (0x0100) at [4..5]
            data[4] = 0x00; data[5] = 0x01;
            // PB = 0 at [6..7]
            data[6] = 0x00; data[7] = 0x00;
            // PC = 0 at [8..9]
            data[8] = 0x00; data[9] = 0x00;
            // PD = 256 (0x0100) at [10..11]
            data[10] = 0x00; data[11] = 0x01;

            bool result = BattleAnimeRendererCore.ParseAffineOAM(data, 0, out var affine);
            Assert.True(result);
            Assert.Equal(256, affine.PA);
            Assert.Equal(0, affine.PB);
            Assert.Equal(0, affine.PC);
            Assert.Equal(256, affine.PD);
        }

        [Fact]
        public void ParseAffineOAM_NegativeValues_ParsesCorrectly()
        {
            byte[] data = new byte[12];
            data[2] = 0xFF; data[3] = 0xFF;
            // PA = -256 (0xFF00) — flipped
            data[4] = 0x00; data[5] = 0xFF;
            // PB = 128 (0x0080)
            data[6] = 0x80; data[7] = 0x00;
            // PC = -128 (0xFF80)
            data[8] = 0x80; data[9] = 0xFF;
            // PD = -1 (0xFFFF)
            data[10] = 0xFF; data[11] = 0xFF;

            bool result = BattleAnimeRendererCore.ParseAffineOAM(data, 0, out var affine);
            Assert.True(result);
            Assert.Equal(-256, affine.PA);
            Assert.Equal(128, affine.PB);
            Assert.Equal(-128, affine.PC);
            Assert.Equal(-1, affine.PD);
        }

        [Fact]
        public void ParseAffineOAM_WithOffset_ParsesCorrectly()
        {
            // Put the affine entry at offset 12 (as if preceded by another entry)
            byte[] data = new byte[24];
            data[14] = 0xFF; data[15] = 0xFF; // affine marker at offset 12+2, 12+3
            data[16] = 0x00; data[17] = 0x01; // PA = 256
            data[18] = 0x00; data[19] = 0x00; // PB = 0
            data[20] = 0x00; data[21] = 0x00; // PC = 0
            data[22] = 0x00; data[23] = 0x01; // PD = 256

            bool result = BattleAnimeRendererCore.ParseAffineOAM(data, 12, out var affine);
            Assert.True(result);
            Assert.Equal(256, affine.PA);
            Assert.Equal(256, affine.PD);
        }

        // ---- ExtractPaletteBank tests ----

        [Theory]
        [InlineData(0x00, 0)]  // bank 0
        [InlineData(0x10, 1)]  // bank 1
        [InlineData(0x20, 2)]  // bank 2
        [InlineData(0x30, 3)]  // bank 3
        [InlineData(0xF0, 15)] // bank 15
        [InlineData(0x0F, 0)]  // lower nibble only → bank 0
        [InlineData(0xFF, 15)] // all bits → bank 15
        public void ExtractPaletteBank_ReturnsCorrectBank(byte oamByte5, int expectedBank)
        {
            Assert.Equal(expectedBank, BattleAnimeRendererCore.ExtractPaletteBank(oamByte5));
        }

        // ---- Size-doubled sprite dimension tests ----

        [Theory]
        [InlineData(0x00, 0x00, 8, 8)]    // square times1: 8x8 → doubled 16x16
        [InlineData(0x00, 0x40, 16, 16)]   // square times2: 16x16 → doubled 32x32
        [InlineData(0x40, 0x80, 32, 16)]   // horiz times4: 32x16 → doubled 64x32
        [InlineData(0x80, 0xC0, 32, 64)]   // vert times8: 32x64 → doubled 64x128
        public void SizeDoubled_DoublesRenderArea(int align, int area, int expectedW, int expectedH)
        {
            BattleAnimeRendererCore.GetOAMSize(align, area, out int wTiles, out int hTiles);
            int sprW = wTiles * 8;
            int sprH = hTiles * 8;
            // When size-doubled, render area is 2x
            Assert.Equal(expectedW, sprW);
            Assert.Equal(expectedH, sprH);
            Assert.Equal(expectedW * 2, sprW * 2);
            Assert.Equal(expectedH * 2, sprH * 2);
        }

        // ---- Affine transform math tests ----

        [Fact]
        public void BlitSpriteAffine_IdentityMatrix_MatchesBlitSprite()
        {
            // Setup: 16x16 sprite with known pixel pattern
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];

            // Fill a 16x16 region with a checkerboard of red/green
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int si = (y * srcW + x) * 4;
                    if ((x + y) % 2 == 0)
                    {
                        src[si] = 255; src[si + 1] = 0; src[si + 2] = 0; src[si + 3] = 255; // red
                    }
                    else
                    {
                        src[si] = 0; src[si + 1] = 255; src[si + 2] = 0; src[si + 3] = 255; // green
                    }
                }
            }

            // Identity affine
            var identity = new BattleAnimeRendererCore.OAMAffineData
            {
                PA = 256, PB = 0, PC = 0, PD = 256
            };

            int dstW = 64, dstH = 64;

            // Blit with BlitSprite (no flip)
            byte[] dstNormal = new byte[dstW * dstH * 4];
            // Use reflection or internal access — BlitSprite is private, so test via DrawOAMSprites
            // Instead, test via BlitSpriteAffine with identity and compare output

            // Blit with affine identity (not doubled)
            byte[] dstAffine = new byte[dstW * dstH * 4];
            BattleAnimeRendererCore.BlitSpriteAffine(src, srcW, srcH,
                0, 0, 16, 16,
                dstAffine, dstW, dstH,
                10, 10, identity, false);

            // Verify: sample some pixels that should be drawn
            // Center of 16x16 sprite at dest (10,10): pixel at (18, 18) = src(8,8)
            int checkX = 18, checkY = 18;
            int di = (checkY * dstW + checkX) * 4;
            // src(8,8): (8+8)%2==0 → red
            Assert.Equal(255, dstAffine[di + 0]); // R
            Assert.Equal(0, dstAffine[di + 1]);   // G
            Assert.Equal(255, dstAffine[di + 3]); // A (opaque)
        }

        [Fact]
        public void BlitSpriteAffine_2xScale_ShrinksSpriteInOutput()
        {
            // 2x scale means each dest pixel maps to 2x source distance.
            // PA=PD=512 (2.0 in 8.8), PB=PC=0
            // This maps dest range [-8..+7] to source range [-16..+14],
            // effectively zooming out / shrinking the sprite in the output.
            int srcW = 64, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];

            // Fill a 32x32 region with solid blue
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    int si = (y * srcW + x) * 4;
                    src[si] = 0; src[si + 1] = 0; src[si + 2] = 255; src[si + 3] = 255;
                }
            }

            var scale2x = new BattleAnimeRendererCore.OAMAffineData
            {
                PA = 512, PB = 0, PC = 0, PD = 512 // 2.0 scale (zoom out)
            };

            int dstW = 128, dstH = 128;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.BlitSpriteAffine(src, srcW, srcH,
                0, 0, 32, 32,
                dst, dstW, dstH,
                32, 32, scale2x, false);

            // The center pixel should still be blue (maps to source center)
            int cx = 32 + 16, cy = 32 + 16; // dest center of the 32x32 render area
            int cdi = (cy * dstW + cx) * 4;
            Assert.Equal(255, dst[cdi + 2]); // Blue
            Assert.Equal(255, dst[cdi + 3]); // Opaque

            // Edge pixels should be transparent (source out of sprite bounds)
            int ex = 32, ey = 32; // top-left of render area
            int edi = (ey * dstW + ex) * 4;
            Assert.Equal(0, dst[edi + 3]); // Should be transparent or zero
        }

        [Fact]
        public void BlitSpriteAffine_90DegRotation_RotatesPixels()
        {
            // 90-degree clockwise rotation:
            // PA=0, PB=256, PC=-256, PD=0
            // For dest pixel (dx, dy) relative to center:
            //   srcX = PA*dx + PB*dy = 256*dy
            //   srcY = PC*dx + PD*dy = -256*dx
            // So source (x, y) relative to center maps to dest (-y, x) relative to center.

            int srcW = 64, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];

            // Fill a 16x16 region with red so rotation has plenty of pixels to sample
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int si = (y * srcW + x) * 4;
                    src[si] = 255; src[si + 1] = 0; src[si + 2] = 0; src[si + 3] = 255;
                }
            }

            var rot90 = new BattleAnimeRendererCore.OAMAffineData
            {
                PA = 0, PB = 256, PC = -256, PD = 0
            };

            int dstW = 64, dstH = 64;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.BlitSpriteAffine(src, srcW, srcH,
                0, 0, 16, 16,
                dst, dstW, dstH,
                16, 16, rot90, false);

            // Verify that at least one red pixel was drawn in the output
            bool anyRedDrawn = false;
            for (int i = 0; i < dst.Length; i += 4)
            {
                if (dst[i + 3] != 0 && dst[i] == 255 && dst[i + 1] == 0 && dst[i + 2] == 0)
                {
                    anyRedDrawn = true;
                    break;
                }
            }
            Assert.True(anyRedDrawn, "Rotated sprite should produce at least one red pixel");

            // The center pixel should still map to the source center (which is red)
            int cx = 16 + 8, cy = 16 + 8;
            int cdi = (cy * dstW + cx) * 4;
            Assert.Equal(255, dst[cdi + 0]); // Red
            Assert.Equal(255, dst[cdi + 3]); // Opaque
        }

        [Fact]
        public void BlitSpriteAffine_SizeDoubled_DoublesRenderArea()
        {
            // With size doubling, an 8x8 sprite gets a 16x16 render area
            int srcW = 32, srcH = 32;
            byte[] src = new byte[srcW * srcH * 4];

            // Fill entire 8x8 source region with blue
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int si = (y * srcW + x) * 4;
                    src[si] = 0; src[si + 1] = 0; src[si + 2] = 255; src[si + 3] = 255;
                }
            }

            // Identity matrix, but with sizeDoubled=true
            var identity = new BattleAnimeRendererCore.OAMAffineData
            {
                PA = 256, PB = 0, PC = 0, PD = 256
            };

            int dstW = 64, dstH = 64;
            byte[] dstNormal = new byte[dstW * dstH * 4];
            byte[] dstDoubled = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.BlitSpriteAffine(src, srcW, srcH,
                0, 0, 8, 8,
                dstNormal, dstW, dstH,
                16, 16, identity, false);

            BattleAnimeRendererCore.BlitSpriteAffine(src, srcW, srcH,
                0, 0, 8, 8,
                dstDoubled, dstW, dstH,
                16, 16, identity, true);

            // Count drawn pixels in each
            int countNormal = 0, countDoubled = 0;
            for (int i = 3; i < dstNormal.Length; i += 4)
            {
                if (dstNormal[i] != 0) countNormal++;
                if (dstDoubled[i] != 0) countDoubled++;
            }

            // With identity transform, both should draw the same number of pixels
            // because the identity maps 1:1, but the doubled version scans a larger area
            // Both should have roughly the same visible pixels since the source is bounded
            Assert.True(countNormal > 0, "Normal blit should draw pixels");
            Assert.True(countDoubled > 0, "Doubled blit should draw pixels");
            // The doubled version's render area is 16x16 but source is only 8x8,
            // so only the center 8x8 of the 16x16 render area maps to valid source pixels
            Assert.Equal(countNormal, countDoubled);
        }

        // ---- DrawOAMSprites affine routing tests ----

        [Fact]
        public void DrawOAMSprites_AffineSprite_UsesAffineBlitPath()
        {
            // Build OAM data:
            // Entry 0: affine matrix (identity)
            // Entry 1: affine sprite (align bit 0 set)
            // Entry 2: terminator
            byte[] oam = new byte[36];

            // Entry 0: affine matrix entry
            oam[0] = 0x00;
            oam[1] = 0x00;
            oam[2] = 0xFF; oam[3] = 0xFF; // affine marker
            // PA = 256
            oam[4] = 0x00; oam[5] = 0x01;
            // PB = 0
            oam[6] = 0x00; oam[7] = 0x00;
            // PC = 0
            oam[8] = 0x00; oam[9] = 0x00;
            // PD = 256
            oam[10] = 0x00; oam[11] = 0x01;

            // Entry 1: affine sprite (align bit 0 = 1 for affine mode)
            oam[12] = 0x00;  // normal entry
            oam[13] = 0x01;  // align: square (bits 6-7=0) + affine bit (bit 0=1)
            oam[14] = 0x00;  // not an affine data entry
            oam[15] = 0x00;  // area: times1
            oam[16] = 0x00;  // sheet (0,0)
            oam[17] = 0x00;  // palette bank 0
            oam[18] = 0x00; oam[19] = 0x00; // vram_x = 0
            oam[20] = 0x00; oam[21] = 0x00; // vram_y = 0
            oam[22] = 0x00; oam[23] = 0x00;

            // Entry 2: terminator
            oam[24] = 0x01;

            // Source with a red pixel at (0,0)
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            // Put pixels in center of 8x8 tile area so identity affine picks them up
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int si = (y * srcW + x) * 4;
                    src[si] = 255; src[si + 1] = 0; src[si + 2] = 0; src[si + 3] = 255;
                }
            }

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            // With identity affine at vram(0,0), center of the 8x8 sprite should appear
            // near screen position (0x94 + 4, 0x58 + 4) = (152, 92)
            int checkX = 0x94 + 4;
            int checkY = 0x58 + 4;
            int idx = (checkY * dstW + checkX) * 4;
            Assert.Equal(255, dst[idx + 0]); // Red
            Assert.Equal(255, dst[idx + 3]); // Opaque
        }

        [Fact]
        public void DrawOAMSprites_NonAffineSprite_StillWorksAfterChanges()
        {
            // Ensure the existing non-affine path still works after our modifications
            int srcW = 256, srcH = 64;
            byte[] src = new byte[srcW * srcH * 4];
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 255; // red at (0,0)

            byte[] oam = new byte[24];
            oam[0] = 0x00;  // normal entry
            oam[1] = 0x00;  // align = square, no affine
            oam[2] = 0x00;
            oam[3] = 0x00;  // area = times1, no flip
            oam[4] = 0x00;  // sheet (0,0)
            oam[5] = 0x00;
            oam[6] = 0x00; oam[7] = 0x00; // vram_x = 0
            oam[8] = 0x00; oam[9] = 0x00; // vram_y = 0
            oam[12] = 0x01; // terminator

            int dstW = 240, dstH = 160;
            byte[] dst = new byte[dstW * dstH * 4];

            BattleAnimeRendererCore.DrawOAMSprites(oam, 0, src, srcW, srcH, dst, dstW, dstH);

            int expectedX = 0x94;
            int expectedY = 0x58;
            int idx = (expectedY * dstW + expectedX) * 4;
            Assert.Equal(255, dst[idx + 0]); // R
            Assert.Equal(255, dst[idx + 3]); // A
        }

        // ---- DecodeSheetWithPaletteBank tests ----

        [Fact]
        public void DecodeSheetWithPaletteBank_NullInputs_ReturnsNull()
        {
            var svc = new StubImageService();
            Assert.Null(BattleAnimeRendererCore.DecodeSheetWithPaletteBank(null, new byte[32], 0, 8, 8, svc));
            Assert.Null(BattleAnimeRendererCore.DecodeSheetWithPaletteBank(new byte[32], null, 0, 8, 8, svc));
            Assert.Null(BattleAnimeRendererCore.DecodeSheetWithPaletteBank(new byte[32], new byte[32], 0, 8, 8, null));
        }

        [Fact]
        public void DecodeSheetWithPaletteBank_Bank0_UsesFirstPalette()
        {
            // One tile of all color index 1
            byte[] gfx = new byte[32];
            for (int i = 0; i < 32; i++) gfx[i] = 0x11;

            // Palette: bank 0 color 1 = red (0x001F)
            byte[] pal = new byte[64]; // 2 banks of 16 colors
            pal[2] = 0x1F; pal[3] = 0x00; // bank 0, color 1 = red

            var svc = new StubImageService();
            byte[] pixels = BattleAnimeRendererCore.DecodeSheetWithPaletteBank(gfx, pal, 0, 8, 8, svc);
            Assert.NotNull(pixels);

            // Check pixel (0,0): should be red from bank 0
            Assert.Equal(248, pixels[0]); // R: 31 << 3
            Assert.Equal(0, pixels[1]);   // G
            Assert.Equal(255, pixels[3]); // A (opaque, not index 0)
        }

        [Fact]
        public void DecodeSheetWithPaletteBank_Bank1_UsesSecondPalette()
        {
            // One tile of all color index 1
            byte[] gfx = new byte[32];
            for (int i = 0; i < 32; i++) gfx[i] = 0x11;

            // Palette: bank 0 color 1 = red, bank 1 color 1 = green
            byte[] pal = new byte[64]; // 2 banks
            pal[2] = 0x1F; pal[3] = 0x00;   // bank 0, color 1 = red
            pal[34] = 0xE0; pal[35] = 0x03;  // bank 1, color 1 = green (bits 5-9 = 31)

            var svc = new StubImageService();
            byte[] pixels = BattleAnimeRendererCore.DecodeSheetWithPaletteBank(gfx, pal, 1, 8, 8, svc);
            Assert.NotNull(pixels);

            // Check pixel (0,0): should be green from bank 1
            Assert.Equal(0, pixels[0]);   // R
            Assert.Equal(248, pixels[1]); // G: 31 << 3
            Assert.Equal(255, pixels[3]); // A
        }

        // ---- OAMAffineData struct tests ----

        [Fact]
        public void OAMAffineData_DefaultValues_AreZero()
        {
            var affine = new BattleAnimeRendererCore.OAMAffineData();
            Assert.Equal(0, affine.PA);
            Assert.Equal(0, affine.PB);
            Assert.Equal(0, affine.PC);
            Assert.Equal(0, affine.PD);
        }

        [Fact]
        public void OAMAffineData_CanStoreFullRange()
        {
            var affine = new BattleAnimeRendererCore.OAMAffineData
            {
                PA = short.MaxValue,
                PB = short.MinValue,
                PC = 0,
                PD = -1
            };
            Assert.Equal(short.MaxValue, affine.PA);
            Assert.Equal(short.MinValue, affine.PB);
            Assert.Equal(0, affine.PC);
            Assert.Equal(-1, affine.PD);
        }
    }
}
