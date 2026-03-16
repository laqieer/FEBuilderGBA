using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class BattleAnimeCompositionTests
    {
        // Helper: build a minimal valid OAM entry (12 bytes)
        // firstByte=0x00, align, unused, area, tileRef, palBank, vramX(2), vramY(2), unused(2)
        static byte[] MakeOAMEntry(byte align, byte area, byte tileRef, byte palBankNibble,
                                    short vramX, short vramY)
        {
            return new byte[]
            {
                0x00,       // [0] normal entry
                align,      // [1] align (shape in bits 6-7)
                0x00,       // [2] (not 0xFF, so not affine)
                area,       // [3] area (size in bits 6-7, flip flags)
                tileRef,    // [4] tile ref: bits 0-4=X, bits 5-7=Y
                (byte)(palBankNibble << 4), // [5] palette bank in high nibble
                (byte)(vramX & 0xFF),       // [6] vramX low
                (byte)((vramX >> 8) & 0xFF),// [7] vramX high
                (byte)(vramY & 0xFF),       // [8] vramY low
                (byte)((vramY >> 8) & 0xFF),// [9] vramY high
                0x00,       // [10] unused
                0x00,       // [11] unused
            };
        }

        // Helper: build OAM terminator (0x01 in first byte)
        static byte[] MakeOAMTerminator()
        {
            return new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        }

        // Concatenate multiple byte arrays
        static byte[] Concat(params byte[][] arrays)
        {
            int total = 0;
            foreach (var a in arrays) total += a.Length;
            var result = new byte[total];
            int offset = 0;
            foreach (var a in arrays)
            {
                Array.Copy(a, 0, result, offset, a.Length);
                offset += a.Length;
            }
            return result;
        }

        [Fact]
        public void LintOAM_ValidData_ReturnsNoErrors()
        {
            // A single valid OAM entry: square shape (0x00), size=times1 (0x00),
            // tileRef at (0,0), palette bank 0, at position (0,0)
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x00, 0x00, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.Empty(errors);
        }

        [Fact]
        public void LintOAM_ValidMultipleEntries_ReturnsNoErrors()
        {
            // Two non-overlapping sprites
            // Entry 0: square 1x1 tile at vram (0, 0)
            // Entry 1: square 1x1 tile at vram (16, 0) — no overlap with 8px wide sprite
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x00, 0x00, 0, 0, 0),
                MakeOAMEntry(0x00, 0x00, 0x01, 0, 16, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.Empty(errors);
        }

        [Fact]
        public void LintOAM_InvalidShape_ReturnsError()
        {
            // Shape = 0xC0 (reserved, invalid)
            byte[] data = Concat(
                MakeOAMEntry(0xC0, 0x00, 0x00, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("invalid shape/size"));
        }

        [Fact]
        public void LintOAM_TileIndexOutOfRange_ReturnsError()
        {
            // A 4x4 tile sprite (SIZE_TIMES4=0x80, SHAPE_SQUARE=0x00) starting at tileX=30
            // would extend to tileX 34, exceeding the 32-tile width of the sheet
            // tileRef: bits 0-4 = 30 (0x1E), bits 5-7 = 0 => byte = 0x1E
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x80, 0x1E, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("tile X range exceeds"));
        }

        [Fact]
        public void LintOAM_TileYOutOfRange_ReturnsError()
        {
            // A 4x4 tile sprite at tileY=6 would extend to tileY=10, exceeding max of 8
            // tileRef: bits 0-4 = 0, bits 5-7 = 6 => byte = 6 << 5 = 0xC0
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x80, 0xC0, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("tile Y range exceeds"));
        }

        [Fact]
        public void LintOAM_PaletteIndexOutOfRange_ReturnsError()
        {
            // Palette bank 5 (exceeds max of 3)
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x00, 0x00, 5, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("palette bank"));
        }

        [Fact]
        public void LintOAM_OverlappingSprites_ReturnsError()
        {
            // Two sprites at the same position (both 1x1 tile = 8x8 pixels at vram 0,0)
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0x00, 0x00, 0, 0, 0),
                MakeOAMEntry(0x00, 0x00, 0x01, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("overlapping"));
        }

        [Fact]
        public void LintOAM_NullData_ReturnsError()
        {
            var errors = BattleAnimeCompositionCore.LintOAM(null, 0, 0);
            Assert.Single(errors);
            Assert.Contains("null", errors[0]);
        }

        [Fact]
        public void LintOAM_OutOfRangeStart_ReturnsError()
        {
            byte[] data = new byte[12];
            var errors = BattleAnimeCompositionCore.LintOAM(data, 100, 0);
            Assert.Single(errors);
            Assert.Contains("out of range", errors[0]);
        }

        [Fact]
        public void LintOAM_AffineEntrySkipped()
        {
            // An affine matrix entry (bytes 2-3 = 0xFFFF) should be skipped without error
            byte[] affine = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            byte[] data = Concat(affine, MakeOAMTerminator());

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.Empty(errors);
        }

        [Fact]
        public void LintOAM_WithLengthLimit()
        {
            // Only scan first 12 bytes (one entry), ignoring the second (even if invalid)
            byte[] valid = MakeOAMEntry(0x00, 0x00, 0x00, 0, 0, 0);
            byte[] invalid = MakeOAMEntry(0xC0, 0x00, 0x00, 0, 16, 0);
            byte[] data = Concat(valid, invalid, MakeOAMTerminator());

            // With length=12, only the first entry should be scanned
            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 12);
            Assert.Empty(errors);
        }

        [Fact]
        public void ComposeLayerPixels_WeaponOverlaysBody()
        {
            // 2 pixels: body is red, weapon pixel 0 is transparent, pixel 1 is blue
            byte[] body = new byte[]
            {
                255, 0, 0, 255,   // pixel 0: red, opaque
                255, 0, 0, 255,   // pixel 1: red, opaque
            };
            byte[] weapon = new byte[]
            {
                0, 0, 0, 0,       // pixel 0: transparent
                0, 0, 255, 255,   // pixel 1: blue, opaque
            };

            var result = BattleAnimeCompositionCore.ComposeLayerPixels(body, weapon, 2);

            Assert.NotNull(result);
            // Pixel 0: should stay red (weapon was transparent)
            Assert.Equal(255, result[0]); // R
            Assert.Equal(0,   result[1]); // G
            Assert.Equal(0,   result[2]); // B
            Assert.Equal(255, result[3]); // A

            // Pixel 1: should be blue (weapon overwrites)
            Assert.Equal(0,   result[4]); // R
            Assert.Equal(0,   result[5]); // G
            Assert.Equal(255, result[6]); // B
            Assert.Equal(255, result[7]); // A
        }

        [Fact]
        public void ComposeLayerPixels_NullInputs_ReturnsNull()
        {
            Assert.Null(BattleAnimeCompositionCore.ComposeLayerPixels(null, new byte[8], 2));
            Assert.Null(BattleAnimeCompositionCore.ComposeLayerPixels(new byte[8], null, 2));
            Assert.Null(BattleAnimeCompositionCore.ComposeLayerPixels(new byte[8], new byte[8], 0));
        }

        [Fact]
        public void ComposeLayerPixels_MismatchedLength_ReturnsNull()
        {
            // Body has enough data for 2 pixels, weapon only for 1
            Assert.Null(BattleAnimeCompositionCore.ComposeLayerPixels(new byte[8], new byte[4], 2));
        }

        [Fact]
        public void ComposeLayerPixels_AllTransparentWeapon_ReturnsCopyOfBody()
        {
            byte[] body = new byte[] { 100, 150, 200, 255, 50, 60, 70, 128 };
            byte[] weapon = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };

            var result = BattleAnimeCompositionCore.ComposeLayerPixels(body, weapon, 2);
            Assert.NotNull(result);
            for (int i = 0; i < body.Length; i++)
                Assert.Equal(body[i], result[i]);
        }

        [Fact]
        public void IsWeaponOverlay_CorrectForKnownSections()
        {
            Assert.False(BattleAnimeCompositionCore.IsWeaponOverlay(0)); // attack body
            Assert.True(BattleAnimeCompositionCore.IsWeaponOverlay(1));  // attack weapon
            Assert.False(BattleAnimeCompositionCore.IsWeaponOverlay(2)); // crit body
            Assert.True(BattleAnimeCompositionCore.IsWeaponOverlay(3));  // crit weapon
            Assert.False(BattleAnimeCompositionCore.IsWeaponOverlay(4)); // ranged
        }

        [Fact]
        public void GetBodySectionForWeapon_ReturnsCorrectMapping()
        {
            Assert.Equal(0, BattleAnimeCompositionCore.GetBodySectionForWeapon(1));
            Assert.Equal(2, BattleAnimeCompositionCore.GetBodySectionForWeapon(3));
            Assert.Equal(-1, BattleAnimeCompositionCore.GetBodySectionForWeapon(0));
            Assert.Equal(-1, BattleAnimeCompositionCore.GetBodySectionForWeapon(5));
        }

        [Fact]
        public void LintOAM_ValidLargeSprite_NoErrors()
        {
            // 8x8 tile sprite (SIZE_TIMES8=0xC0, SHAPE_SQUARE=0x00) at tile (0,0)
            // This is a 64x64 pixel sprite, the largest valid square
            byte[] data = Concat(
                MakeOAMEntry(0x00, 0xC0, 0x00, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.Empty(errors);
        }

        [Fact]
        public void LintOAM_HorizontalSprite_ValidDimensions()
        {
            // SHAPE_HORIZONTAL (0x40) + SIZE_TIMES2 (0x40) = 4x1 tiles = 32x8 pixels
            // At tile (0,0), this fits in the 32-wide sheet
            byte[] data = Concat(
                MakeOAMEntry(0x40, 0x40, 0x00, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.Empty(errors);
        }

        [Fact]
        public void LintOAM_VerticalSprite_ExceedsHeight()
        {
            // SHAPE_VERTICAL (0x80) + SIZE_TIMES8 (0xC0) = 4x8 tiles
            // At tileY=1 (byte = 1 << 5 = 0x20), extends to tileY=9, exceeds max of 8
            byte[] data = Concat(
                MakeOAMEntry(0x80, 0xC0, 0x20, 0, 0, 0),
                MakeOAMTerminator()
            );

            var errors = BattleAnimeCompositionCore.LintOAM(data, 0, 0);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("tile Y range exceeds"));
        }
    }
}
