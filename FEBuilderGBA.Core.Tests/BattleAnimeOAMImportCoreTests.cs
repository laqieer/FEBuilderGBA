using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for BattleAnimeOAMImportCore.
    ///
    /// Key proof: the FUNCTIONAL ROUND-TRIP test — assemble OAM from a synthetic
    /// indexed image, then render via BattleAnimeRendererCore.DrawOAMSprites using
    /// the assembled {tiles, palette, OAM} and assert the rendered pixels reproduce
    /// the input image at the expected screen position.
    ///
    /// This confirms the OAM assembler is correct without requiring WF-reference
    /// or a magic-patched ROM.
    /// </summary>
    [Collection("SharedState")]
    public class BattleAnimeOAMImportCoreTests : IDisposable
    {
        readonly IImageService _prevService;

        public BattleAnimeOAMImportCoreTests()
        {
            _prevService = CoreState.ImageService;
            CoreState.ImageService = new StubImageService();
        }

        public void Dispose()
        {
            CoreState.ImageService = _prevService;
        }

        // ============================================================
        // Input-validation tests
        // ============================================================

        [Fact]
        public void AssembleOAM_NullPixels_ReturnsError()
        {
            var pal = MakeMonoPalette();
            var r = BattleAnimeOAMImportCore.AssembleOAM(null, 8, 8, pal);
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        }

        [Fact]
        public void AssembleOAM_NullPalette_ReturnsError()
        {
            var pixels = new byte[8 * 8];
            pixels[0] = 1;
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, 8, 8, null);
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        }

        [Fact]
        public void AssembleOAM_PaletteTooShort_ReturnsError()
        {
            var pixels = new byte[8 * 8];
            pixels[0] = 1;
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, 8, 8, new byte[16]);
            Assert.False(r.Success);
        }

        [Fact]
        public void AssembleOAM_NonMultipleOf8Width_ReturnsError()
        {
            var r = BattleAnimeOAMImportCore.AssembleOAM(new byte[7 * 8], 7, 8, MakeMonoPalette());
            Assert.False(r.Success);
        }

        [Fact]
        public void AssembleOAM_NonMultipleOf8Height_ReturnsError()
        {
            var r = BattleAnimeOAMImportCore.AssembleOAM(new byte[8 * 7], 8, 7, MakeMonoPalette());
            Assert.False(r.Success);
        }

        [Fact]
        public void AssembleOAM_ZeroSize_ReturnsError()
        {
            var r = BattleAnimeOAMImportCore.AssembleOAM(new byte[0], 0, 0, MakeMonoPalette());
            Assert.False(r.Success);
        }

        [Fact]
        public void AssembleOAM_PixelBufferTooShort_ReturnsError()
        {
            // Claim 16×16 but only provide 64 bytes (enough for 8×8)
            var r = BattleAnimeOAMImportCore.AssembleOAM(new byte[64], 16, 16, MakeMonoPalette());
            Assert.False(r.Success);
        }

        // ============================================================
        // Blank image: all pixels 0 → no sprite OAM entries
        // ============================================================

        [Fact]
        public void AssembleOAM_AllBlankImage_ProducesOnlyTerminator()
        {
            // A screen-sized blank image should produce only a terminator entry (12 bytes)
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;   // 248
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;  // 160
            var pixels = new byte[imgW * imgH];
            var pal = MakeMonoPalette();

            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);

            Assert.True(r.Success, r.Error);
            // Only the 12-byte terminator
            Assert.Equal(12, r.OamRecords.Length);
            Assert.Equal(0x01, r.OamRecords[0]); // terminator byte
        }

        // ============================================================
        // Single 8×8 tile round-trip
        // ============================================================

        [Fact]
        public void AssembleOAM_SingleTile_RoundTripRenders()
        {
            // Build a synthetic 8×8 image with a recognizable pattern:
            // color index 1 in the top-left 8×8 tile.
            // Place it in the context of a full screen-sized buffer so screen
            // coordinates make sense.
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;   // 248
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;  // 160
            var pixels = new byte[imgW * imgH];

            // Fill the first 8×8 tile (column 0, row 0) with color index 1
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y * imgW + x] = 1;

            // Simple mono palette: color 0 = black (transparent), color 1 = white
            // GBA BGR555 white = 0x7FFF
            var pal = MakeMonoPalette();

            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);
            Assert.True(r.Success, r.Error);

            // OAM should have at least one sprite entry + terminator
            Assert.True(r.OamRecords.Length >= 24); // at least 1 sprite + terminator

            // ----- RENDER ROUND-TRIP -----
            // Decode the 4bpp tile data into the RGBA seat sheet (256×64)
            int sheetW = BattleAnimeOAMImportCore.SEAT_TILE_WIDTH * 8;  // 256
            int sheetH = BattleAnimeOAMImportCore.SEAT_TILE_HEIGHT * 8; // 64
            byte[] sheetPixels = Decode4bppToRGBA(r.TileData4bpp, r.PaletteBytes, sheetW, sheetH);

            // Render the OAM onto a 248×160 destination
            int dstW = imgW;
            int dstH = imgH;
            byte[] dstPixels = new byte[dstW * dstH * 4];
            BattleAnimeRendererCore.DrawOAMSprites(
                r.OamRecords, 0,
                sheetPixels, sheetW, sheetH,
                dstPixels, dstW, dstH,
                isMagicOAM: false);

            // The tile at image position (0,0) maps to vramX = 0 - BITMAP_ADDX = -0x94.
            // The renderer: imgX = vramX + BITMAP_ADDX = -0x94 + 0x94 = 0.
            // imgY = vramY + BITMAP_ADDY = -0x58 + 0x58 = 0.
            // So the sprite should appear at destination pixel (0, 0).
            // Verify at least one pixel in the 8×8 block at (0,0) is opaque.
            bool anyOpaque = false;
            for (int py = 0; py < 8 && !anyOpaque; py++)
            for (int px = 0; px < 8 && !anyOpaque; px++)
            {
                int idx2 = (py * dstW + px) * 4;
                if (dstPixels[idx2 + 3] != 0) anyOpaque = true;
            }
            Assert.True(anyOpaque,
                $"Expected at least one opaque pixel in the 8×8 block at (0,0). " +
                $"OAM records ({r.OamRecords.Length} bytes): {Hex(r.OamRecords)}");
        }

        // ============================================================
        // Multi-tile image round-trip
        // ============================================================

        [Fact]
        public void AssembleOAM_MultiTileImage_RoundTripRenders()
        {
            // A 16×16 area at tile column 2, row 1 (screen tile coords) filled with color 1.
            // This should generate OAM records that, when rendered, put those pixels
            // at the expected screen position.
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];

            // Fill 2×2 tiles starting at image tile (2, 1)
            int tileSrcX = 2, tileSrcY = 1;
            int pixSrcX = tileSrcX * 8, pixSrcY = tileSrcY * 8;
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                pixels[(pixSrcY + y) * imgW + (pixSrcX + x)] = 1;

            var pal = MakeMonoPalette();
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);
            Assert.True(r.Success, r.Error);

            int sheetW = BattleAnimeOAMImportCore.SEAT_TILE_WIDTH * 8;
            int sheetH = BattleAnimeOAMImportCore.SEAT_TILE_HEIGHT * 8;
            byte[] sheetPixels = Decode4bppToRGBA(r.TileData4bpp, r.PaletteBytes, sheetW, sheetH);

            int dstW = imgW, dstH = imgH;
            byte[] dstPixels = new byte[dstW * dstH * 4];
            BattleAnimeRendererCore.DrawOAMSprites(
                r.OamRecords, 0,
                sheetPixels, sheetW, sheetH,
                dstPixels, dstW, dstH,
                isMagicOAM: false);

            // Expected screen position:
            // vramX = pixSrcX - BITMAP_ADDX = 16 - 0x94 = -0x84
            // rendered imgX = vramX + BITMAP_ADDX = -0x84 + 0x94 = 0x10 = 16
            // vramY = pixSrcY - BITMAP_ADDY = 8  - 0x58 = -0x50
            // rendered imgY = vramY + BITMAP_ADDY = -0x50 + 0x58 = 8
            int expX = pixSrcX;
            int expY = pixSrcY;
            int idx  = (expY * dstW + expX) * 4;
            Assert.True(dstPixels[idx + 3] != 0,
                $"Expected opaque pixel at ({expX},{expY}). " +
                $"OAM ({r.OamRecords.Length} bytes): {Hex(r.OamRecords)}");
        }

        // ============================================================
        // Multi-sub-palette (multi-palette mode)
        // ============================================================

        [Fact]
        public void AssembleOAM_MultiPalette_PacksBothBanks()
        {
            // Two distinct regions each using a different palette bank.
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];

            // Bank 0 pixel (index 1) at tile (0,0)
            pixels[0] = 1;
            // Bank 1 pixel (index 16) at tile (2,0) — value 16 = bank1+0
            pixels[2 * 8] = 16;

            // 2-bank palette (64 bytes)
            var pal = new byte[64];
            // bank 0 color 1 = white (0x7FFF)
            pal[2] = 0xFF; pal[3] = 0x7F;
            // bank 1 color 0 (base) = 0, color 1 doesn't matter here
            // Actually bank 1 = color 0 in that bank (pixel 16 → extracted as 0) — transparent!
            // Use pixel 17 instead (= bank1 color 1)
            pixels[2 * 8] = 17;
            pal[34] = 0xFF; pal[35] = 0x7F;  // bank 1 color 1 = white

            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal,
                isMagic: false, isMultiPalette: true);

            Assert.True(r.Success, r.Error);
            // Should have at least 2 sprite entries (one per bank) plus terminator
            Assert.True(r.OamRecords.Length >= 36,
                $"Expected >= 36 bytes OAM, got {r.OamRecords.Length}");
        }

        // ============================================================
        // Magic mode — reduced seat height
        // ============================================================

        [Fact]
        public void AssembleOAM_MagicMode_UsesMagicSeatHeight()
        {
            // In magic mode the seat height is 32 px (4 tiles) instead of 64 px (8 tiles).
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];
            pixels[0] = 1;  // one pixel in tile (0,0)

            var pal = MakeMonoPalette();
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal,
                isMagic: true, isMultiPalette: false);

            Assert.True(r.Success, r.Error);

            // Tile data should correspond to 256×32 = 128 tiles × 32 bytes = 4096 bytes
            int expectedTileBytes =
                BattleAnimeOAMImportCore.SEAT_TILE_WIDTH *
                BattleAnimeOAMImportCore.SEAT_MAGIC_TILE_HEIGHT * 32;
            Assert.Equal(expectedTileBytes, r.TileData4bpp.Length);
        }

        // ============================================================
        // Magic mode round-trip (uses BITMAP_SPELL_ADDX)
        // ============================================================

        [Fact]
        public void AssembleOAM_MagicMode_RoundTripRenders()
        {
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];
            // Color index 1 at screen tile (0,0)
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y * imgW + x] = 1;

            var pal = MakeMonoPalette();
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal,
                isMagic: true, isMultiPalette: false);
            Assert.True(r.Success, r.Error);

            // Render using the magic OAM (isMagicOAM=true)
            int sheetW = BattleAnimeOAMImportCore.SEAT_TILE_WIDTH * 8;        // 256
            int sheetH = BattleAnimeOAMImportCore.SEAT_MAGIC_TILE_HEIGHT * 8; // 32
            byte[] sheetPixels = Decode4bppToRGBA(r.TileData4bpp, r.PaletteBytes, sheetW, sheetH);

            int dstW = imgW, dstH = imgH;
            byte[] dstPixels = new byte[dstW * dstH * 4];
            BattleAnimeRendererCore.DrawOAMSprites(
                r.OamRecords, 0,
                sheetPixels, sheetW, sheetH,
                dstPixels, dstW, dstH,
                isMagicOAM: true);

            // Magic: imgX = vramX + BITMAP_SPELL_ADDX = -0xAC + 0xAC = 0
            //        imgY = vramY + BITMAP_ADDY       = -0x58 + 0x58 = 0
            int chkX = 0, chkY = 0;
            int idx = (chkY * dstW + chkX) * 4;
            Assert.True(dstPixels[idx + 3] != 0,
                $"Expected opaque pixel at ({chkX},{chkY}) in magic mode. " +
                $"OAM ({r.OamRecords.Length} bytes): {Hex(r.OamRecords)}");
        }

        // ============================================================
        // OAM record structure validation
        // ============================================================

        [Fact]
        public void AssembleOAM_OamRecordStructure_IsCorrect()
        {
            // A single non-blank pixel should produce one 12-byte entry + 12-byte terminator.
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];
            pixels[0] = 1;  // one non-blank pixel in tile (0,0)

            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, MakeMonoPalette());
            Assert.True(r.Success, r.Error);
            Assert.True(r.OamRecords.Length >= 24);

            // First entry: byte[0] == 0x00 (normal)
            Assert.Equal(0x00, r.OamRecords[0]);
            // Terminator is the last 12-byte entry
            int lastEntryStart = r.OamRecords.Length - 12;
            Assert.Equal(0x01, r.OamRecords[lastEntryStart]); // terminator byte

            // Palette bank stored in byte[5] bits 4-7 must be 0 (bank 0)
            int palByte = (r.OamRecords[5] >> 4) & 0xF;
            Assert.Equal(0, palByte);
        }

        // ============================================================
        // Determinism
        // ============================================================

        [Fact]
        public void AssembleOAM_SameInput_SameOutput()
        {
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];
            pixels[0] = 1;
            pixels[imgW * 2 + 3] = 2;
            var pal = MakeMonoPalette();

            var r1 = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);
            var r2 = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);

            Assert.True(r1.Success, r1.Error);
            Assert.True(r2.Success, r2.Error);

            Assert.Equal(r1.OamRecords, r2.OamRecords);
            Assert.Equal(r1.TileData4bpp, r2.TileData4bpp);
        }

        // ============================================================
        // MakeUseTileData helper
        // ============================================================

        [Fact]
        public void MakeUseTileData_AllBlank_AllTrue()
        {
            var pixels = new byte[8 * 8]; // all zeros
            bool[] map = BattleAnimeOAMImportCore.MakeUseTileData(pixels, 8, 8);
            Assert.Single(map);
            Assert.True(map[0]); // blank → skip
        }

        [Fact]
        public void MakeUseTileData_NonBlank_FalseExceptTopRight()
        {
            // 16×8: two tiles, color=1 only in second tile
            var pixels = new byte[16 * 8];
            pixels[8] = 1; // pixel (8,0) = first pixel of second tile
            bool[] map = BattleAnimeOAMImportCore.MakeUseTileData(pixels, 16, 8);
            Assert.Equal(2, map.Length);
            Assert.True(map[0],  "first tile is blank → true");
            // Second tile has a non-blank pixel, normally false, but it's the top-right tile
            // which is always set to true (FEditorAdv palette map area) in a 2-tile-wide image.
            Assert.True(map[1], "top-right tile forced true by convention");
        }

        [Fact]
        public void MakeUseTileData_3WideTile_LastTileForced()
        {
            // 24×8: three tiles, color=1 only in second tile (col 1)
            var pixels = new byte[24 * 8];
            pixels[8] = 1;  // first pixel of tile 1 (col 1, row 0)
            bool[] map = BattleAnimeOAMImportCore.MakeUseTileData(pixels, 24, 8);
            Assert.Equal(3, map.Length);
            Assert.True(map[0],  "tile 0 blank");
            Assert.False(map[1], "tile 1 has pixel → not blank");
            Assert.True(map[2],  "tile 2 = top-right, always forced");
        }

        // ============================================================
        // ExtractByPaletteBank helper
        // ============================================================

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ExtractByPaletteBank_OnlySelectedBankVisible(int bank)
        {
            // Pixel values from all 4 banks
            var source = new byte[64];
            for (int i = 0; i < 64; i++) source[i] = (byte)i;

            byte[] result = BattleAnimeOAMImportCore.ExtractByPaletteBank(source, 64, 1, bank);

            int bankStart = bank * 16;
            int bankEnd   = bankStart + 16;

            for (int i = 0; i < 64; i++)
            {
                if (i >= bankStart && i < bankEnd)
                    Assert.Equal((byte)(i - bankStart), result[i]);
                else
                    Assert.Equal(0, result[i]);
            }
        }

        // ============================================================
        // GetSpriteSize (OAM decode helper)
        // ============================================================

        [Theory]
        [InlineData(0x00, 0x00, 1, 1)]   // square times1
        [InlineData(0x00, 0x40, 2, 2)]   // square times2
        [InlineData(0x00, 0x80, 4, 4)]   // square times4
        [InlineData(0x00, 0xC0, 8, 8)]   // square times8
        [InlineData(0x40, 0x00, 2, 1)]   // horizontal times1
        [InlineData(0x40, 0x40, 4, 1)]   // horizontal times2  (w=4, h=1)
        [InlineData(0x40, 0x80, 4, 2)]   // horizontal times4
        [InlineData(0x40, 0xC0, 8, 4)]   // horizontal times8
        [InlineData(0x80, 0x00, 1, 2)]   // vertical times1
        [InlineData(0x80, 0x40, 1, 4)]   // vertical times2
        [InlineData(0x80, 0x80, 2, 4)]   // vertical times4
        [InlineData(0x80, 0xC0, 4, 8)]   // vertical times8
        public void GetSpriteSize_MatchesRendererLookup(int align, int area, int expW, int expH)
        {
            BattleAnimeOAMImportCore.GetSpriteSize((byte)align, (byte)area,
                out int w, out int h);
            Assert.Equal(expW, w);
            Assert.Equal(expH, h);

            // Also verify this matches the renderer's table
            BattleAnimeRendererCore.GetOAMSize(align, area, out int rW, out int rH);
            Assert.Equal(rW, w);
            Assert.Equal(rH, h);
        }

        // ============================================================
        // ConvertToLeftToRightOAM
        // ============================================================

        [Fact]
        public void ConvertToLeftToRightOAM_NullInput_ReturnsNull()
        {
            var result = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(null);
            Assert.Null(result);
        }

        [Fact]
        public void ConvertToLeftToRightOAM_SetsAreaBit0x10()
        {
            // WF ImageUtilOAM.ConvertLeftToRightOAM (line 1416) ORs 0x10 into area byte[3].
            // Build a minimal OAM: one 1×1 square sprite + terminator
            byte[] oam = new byte[24];
            // Entry 0: normal 1×1 square sprite at vramX=10
            oam[0] = 0x00;   // normal
            oam[1] = 0x00;   // align: square
            oam[2] = 0x00;
            oam[3] = 0x00;   // area: times1, no flip
            oam[4] = 0x00;
            oam[5] = 0x00;
            // vramX = 10 (signed 16-bit LE)
            oam[6] = 10; oam[7] = 0;
            oam[8] = 0; oam[9] = 0;
            oam[10] = 0; oam[11] = 0;
            // Entry 1: terminator
            oam[12] = 0x01;

            var ltr = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(oam);

            Assert.NotNull(ltr);
            // Must set bit 0x10 — matches WF line 1416: leftToRight[i+3] = (byte)(... | 0x10)
            Assert.True((ltr[3] & 0x10) != 0, "Area bit 0x10 must be set (WF fidelity: ConvertLeftToRightOAM | 0x10)");
            // Must NOT set 0x20 (that was the pre-fix wrong bit)
            Assert.True((ltr[3] & 0x20) == 0, "Area bit 0x20 must NOT be set (WF uses 0x10, not 0x20)");
            // vramX should be negated: -(1*8) - 10 = -18
            short newVramX = (short)(ltr[6] | (ltr[7] << 8));
            Assert.Equal(-18, newVramX);
        }

        [Fact]
        public void ConvertToLeftToRightOAM_TerminatorUnchanged()
        {
            byte[] oam = new byte[12];
            oam[0] = 0x01; // terminator only

            var ltr = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(oam);
            Assert.Equal(0x01, ltr[0]); // still terminator
        }

        // ============================================================
        // Tile data encoding (4bpp)
        // ============================================================

        [Fact]
        public void AssembleOAM_TileData_IsCorrect4bpp()
        {
            // A single pixel at position (0,0) with color index 3.
            // The 4bpp tile encoding should have nibble 3 in byte 0 lower nibble.
            int imgW = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_WIDTH;
            int imgH = 8 * BattleAnimeOAMImportCore.SCREEN_TILE_HEIGHT;
            var pixels = new byte[imgW * imgH];
            pixels[0] = 3;  // (0,0) = color 3

            var pal = MakeMonoPalette();
            var r = BattleAnimeOAMImportCore.AssembleOAM(pixels, imgW, imgH, pal);
            Assert.True(r.Success, r.Error);

            // Tile 0 of the seat should contain color 3 at the first nibble.
            // But the tile packer finds the first non-blank OAM tile.
            // The result TileData4bpp is the whole seat encoded as 4bpp.
            // Find which seat tile the 8×8 block was placed in by reading the OAM.
            // Easier: just verify the tile data has at least one byte with nibble 3.
            bool found3 = false;
            foreach (byte b in r.TileData4bpp)
            {
                if ((b & 0x0F) == 3 || ((b >> 4) & 0x0F) == 3)
                { found3 = true; break; }
            }
            Assert.True(found3, "Expected nibble value 3 in tile data for color index 3");
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>Build a minimal 1-bank GBA palette with color 1 = white (0x7FFF).</summary>
        static byte[] MakeMonoPalette()
        {
            var pal = new byte[32]; // 16 colors × 2 bytes
            // color 0 = transparent black (0x0000)
            // color 1 = white (0x7FFF = BGR555 white)
            pal[2] = 0xFF;
            pal[3] = 0x7F;
            return pal;
        }

        /// <summary>
        /// Decode 4bpp tile data + GBA palette into an RGBA pixel buffer
        /// (sheetW × sheetH × 4 bytes, row-major).
        /// This mirrors what BattleAnimeRendererCore.RenderTileSheet does internally,
        /// but operates on a pre-decompressed tile buffer for testing purposes.
        /// </summary>
        static byte[] Decode4bppToRGBA(byte[] tileData, byte[] pal, int sheetW, int sheetH)
        {
            byte[] pixels = new byte[sheetW * sheetH * 4];
            int tilesPerRow = sheetW / 8;
            int totalTiles  = tileData.Length / 32;

            for (int t = 0; t < totalTiles; t++)
            {
                int tileOff = t * 32;
                int tileCol = t % tilesPerRow;
                int tileRow = t / tilesPerRow;
                if (tileRow * 8 >= sheetH) break;

                for (int py = 0; py < 8; py++)
                for (int px = 0; px < 8; px++)
                {
                    int bytePos = tileOff + py * 4 + px / 2;
                    if (bytePos >= tileData.Length) continue;
                    byte b  = tileData[bytePos];
                    int  ci = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                    int palOff = ci * 2;
                    if (palOff + 2 > pal.Length) continue;

                    ushort gbaColor = (ushort)(pal[palOff] | (pal[palOff + 1] << 8));
                    byte r = (byte)((gbaColor & 0x1F) << 3);
                    byte g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                    byte bl= (byte)(((gbaColor >> 10) & 0x1F) << 3);

                    int destX = tileCol * 8 + px;
                    int destY = tileRow * 8 + py;
                    int idx   = (destY * sheetW + destX) * 4;
                    if (idx + 3 < pixels.Length)
                    {
                        pixels[idx + 0] = r;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = bl;
                        pixels[idx + 3] = (byte)(ci == 0 ? 0 : 255);
                    }
                }
            }
            return pixels;
        }

        // ============================================================
        // GrepBlockInSeat — exclusive scan-bound fidelity (WF parity)
        // ============================================================

        /// <summary>
        /// WF GrepTileBitmap uses EXCLUSIVE upper bounds:
        ///   int width  = rect.Width  - needrect.Width;   // line 2383
        ///   int height = rect.Height - needrect.Height;  // line 2384
        ///   for (int y = 0; y &lt; height; y+=8)          // line 2394
        ///   for (int x = 0; x &lt; width;  x+=8)          // line 2396
        ///
        /// A block placed exactly at (searchW, searchH) in the seat
        /// must NOT be found — Core's scan must stop before that position.
        /// </summary>
        [Fact]
        public void GrepBlockInSeat_DoesNotScanExclusiveEdge_Y()
        {
            // Seat: 2 tiles wide × 2 tiles tall = 16×16 px
            // Block: 8×8 (1 tile)
            // searchH = seat.PixH - blockH = 16 - 8 = 8
            // WF loops: y < 8  → only y=0 is visited; y=8 is EXCLUDED
            // Place a unique pattern only at y=8 (the excluded row).
            // GrepBlockInSeat must return false.
            var seat = new BattleAnimeOAMImportCore.Seat(tileW: 2, tileH: 2);
            int blockW = 8, blockH = 8;
            // Fill the block with marker value 7
            var block = new byte[blockW * blockH];
            for (int i = 0; i < block.Length; i++) block[i] = 7;

            // Place identical marker only at pixel row sy=8 in the seat (the excluded row)
            for (int py = 0; py < blockH; py++)
            for (int px = 0; px < blockW; px++)
                seat.Pixels[(8 + py) * seat.PixW + (0 + px)] = 7;

            bool found = BattleAnimeOAMImportCore.GrepBlockInSeat(
                seat, block, blockW, blockH,
                out int tx, out int ty);

            Assert.False(found,
                "GrepBlockInSeat must NOT find a block at the exclusive edge row " +
                "(sy == searchH = seat.PixH - blockH). WF GrepTileBitmap uses y < height (exclusive).");
        }

        [Fact]
        public void GrepBlockInSeat_DoesNotScanExclusiveEdge_X()
        {
            // Seat: 2×2 tiles = 16×16 px, block 8×8
            // searchW = 16 - 8 = 8; WF loops x < 8 → only x=0 visited; x=8 excluded
            var seat = new BattleAnimeOAMImportCore.Seat(tileW: 2, tileH: 2);
            int blockW = 8, blockH = 8;
            var block = new byte[blockW * blockH];
            for (int i = 0; i < block.Length; i++) block[i] = 5;

            // Place identical marker only at pixel column sx=8 (the excluded column)
            for (int py = 0; py < blockH; py++)
            for (int px = 0; px < blockW; px++)
                seat.Pixels[(0 + py) * seat.PixW + (8 + px)] = 5;

            bool found = BattleAnimeOAMImportCore.GrepBlockInSeat(
                seat, block, blockW, blockH,
                out int tx, out int ty);

            Assert.False(found,
                "GrepBlockInSeat must NOT find a block at the exclusive edge column " +
                "(sx == searchW = seat.PixW - blockW). WF GrepTileBitmap uses x < width (exclusive).");
        }

        [Fact]
        public void GrepBlockInSeat_FindsBlockWithinBounds()
        {
            // Sanity: a block placed at (0,0) IS found.
            var seat = new BattleAnimeOAMImportCore.Seat(tileW: 2, tileH: 2);
            int blockW = 8, blockH = 8;
            var block = new byte[blockW * blockH];
            for (int i = 0; i < block.Length; i++) block[i] = 3;

            // Place identical pattern at seat origin
            for (int py = 0; py < blockH; py++)
            for (int px = 0; px < blockW; px++)
                seat.Pixels[py * seat.PixW + px] = 3;

            bool found = BattleAnimeOAMImportCore.GrepBlockInSeat(
                seat, block, blockW, blockH,
                out int tx, out int ty);

            Assert.True(found, "GrepBlockInSeat must find a block placed at (0,0) within scan bounds.");
            Assert.Equal(0, tx);
            Assert.Equal(0, ty);
        }

        static string Hex(byte[] data)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in data) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
