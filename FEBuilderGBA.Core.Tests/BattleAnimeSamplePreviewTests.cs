// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the #822 battle-animation sample-preview render in
// BattleAnimeRendererCore: CropImage (WF SCALE_90 crop at source (100,30)),
// IsBlankImage (WF IsBlankBitmap mirror, threshold 10), and
// RenderSampleBattleAnime (the 12-frame 360x290 grid that mirrors WF
// ImageBattleAnimePalletForm.DrawSample — decompressed palette + paletteIndex
// sub-palette + per-cell SCALE_90 crop + blank-advance + persistent cursor).
//
// Reuses the shared StubImageService/StubImage (BattleAnimeDetailTests) so the
// rendered IImage carries real RGBA pixel data we can assert on, and the same
// synthetic-ROM pattern (rom.LoadLow + U.write_u32 + LZ77.compress) as the
// ImageBattleScreen preview tests (#802/#805).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BattleAnimeSamplePreviewTests : IDisposable
    {
        readonly IImageService _prevService;
        readonly ROM _prevRom;

        public BattleAnimeSamplePreviewTests()
        {
            _prevService = CoreState.ImageService;
            _prevRom = CoreState.ROM;
            CoreState.ImageService = new StubImageService();
        }

        public void Dispose()
        {
            CoreState.ImageService = _prevService;
            CoreState.ROM = _prevRom;
        }

        // ================================================================
        // CropImage — WF SCALE_90 BitBlt(trim, 0,0, 90,90, bitmap, 100, 30)
        // ================================================================

        [Fact]
        public void CropImage_ProducesExact90x90Dimensions()
        {
            IImage src = MakeRgbaImage(240, 160);
            IImage crop = BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90);
            Assert.NotNull(crop);
            Assert.Equal(90, crop.Width);
            Assert.Equal(90, crop.Height);
        }

        [Fact]
        public void CropImage_SentinelAtScreen100_30_LandsAtCrop0_0()
        {
            // A distinctive pixel at SOURCE (100,30) must appear at CROP (0,0).
            IImage src = MakeRgbaImage(240, 160);
            SetPixel(src, 100, 30, 10, 20, 30, 255);

            IImage crop = BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90);
            Assert.NotNull(crop);
            AssertPixel(crop, 0, 0, 10, 20, 30, 255);
        }

        [Fact]
        public void CropImage_SentinelInsideWindow_MapsByOffset()
        {
            // Source (100+5, 30+7) maps to crop (5,7).
            IImage src = MakeRgbaImage(240, 160);
            SetPixel(src, 105, 37, 99, 88, 77, 255);

            IImage crop = BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90);
            Assert.NotNull(crop);
            AssertPixel(crop, 5, 7, 99, 88, 77, 255);
        }

        [Fact]
        public void CropImage_PixelOutsideWindow_NotInCrop()
        {
            // A pixel at source (50,20) is OUTSIDE the (100,30)..(190,120) window.
            // The crop's corresponding location stays transparent.
            IImage src = MakeRgbaImage(240, 160);
            SetPixel(src, 50, 20, 200, 100, 50, 255);

            IImage crop = BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90);
            Assert.NotNull(crop);
            // (0,0) in crop = source (100,30), which we never set => transparent.
            AssertPixel(crop, 0, 0, 0, 0, 0, 0);
        }

        [Fact]
        public void CropImage_WindowPartlyOutOfBounds_FillsTransparent()
        {
            // Crop window extends past the right/bottom edge of a small source.
            IImage src = MakeRgbaImage(120, 60);
            SetPixel(src, 100, 30, 1, 2, 3, 255);

            IImage crop = BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90);
            Assert.NotNull(crop);
            Assert.Equal(90, crop.Width);
            Assert.Equal(90, crop.Height);
            // In-bounds corner present...
            AssertPixel(crop, 0, 0, 1, 2, 3, 255);
            // ...out-of-bounds far corner transparent (no crash).
            AssertPixel(crop, 89, 89, 0, 0, 0, 0);
        }

        [Fact]
        public void CropImage_NullSource_ReturnsNull()
        {
            Assert.Null(BattleAnimeRendererCore.CropImage(null, 100, 30, 90, 90));
        }

        [Fact]
        public void CropImage_NoImageService_ReturnsNull()
        {
            IImage src = MakeRgbaImage(240, 160);
            var prev = CoreState.ImageService;
            CoreState.ImageService = null;
            try
            {
                Assert.Null(BattleAnimeRendererCore.CropImage(src, 100, 30, 90, 90));
            }
            finally { CoreState.ImageService = prev; }
        }

        // ================================================================
        // IsBlankImage — WF IsBlankBitmap(bmp, 10) mirror
        // ================================================================

        [Fact]
        public void IsBlankImage_FullyTransparent_ReturnsTrue()
        {
            IImage img = MakeRgbaImage(90, 90); // all zero (alpha 0)
            Assert.True(BattleAnimeRendererCore.IsBlankImage(img, 10));
        }

        [Fact]
        public void IsBlankImage_BelowThreshold_ReturnsTrue()
        {
            // Exactly 10 non-transparent pixels => still blank (dotCount <= 10).
            IImage img = MakeRgbaImage(90, 90);
            for (int i = 0; i < 10; i++) SetPixel(img, i, 0, 255, 255, 255, 255);
            Assert.True(BattleAnimeRendererCore.IsBlankImage(img, 10));
        }

        [Fact]
        public void IsBlankImage_AboveThreshold_ReturnsFalse()
        {
            // 11 non-transparent pixels => not blank.
            IImage img = MakeRgbaImage(90, 90);
            for (int i = 0; i < 11; i++) SetPixel(img, i, 0, 255, 255, 255, 255);
            Assert.False(BattleAnimeRendererCore.IsBlankImage(img, 10));
        }

        [Fact]
        public void IsBlankImage_OnlyAlphaCounts_RgbIgnored()
        {
            // Pixels with RGB set but alpha 0 are still "transparent" => blank.
            IImage img = MakeRgbaImage(90, 90);
            for (int i = 0; i < 50; i++) SetPixel(img, i, 0, 200, 100, 50, 0);
            Assert.True(BattleAnimeRendererCore.IsBlankImage(img, 10));
        }

        [Fact]
        public void IsBlankImage_Null_ReturnsTrue()
        {
            Assert.True(BattleAnimeRendererCore.IsBlankImage(null, 10));
        }

        // ================================================================
        // RenderSampleBattleAnime — the 12-frame grid (mirror DrawSample)
        // ================================================================

        [Fact]
        public void RenderSample_ProducesExpected360x290Grid()
        {
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
            Assert.NotNull(grid);
            Assert.Equal(360, grid.Width);
            Assert.Equal(290, grid.Height);
        }

        [Fact]
        public void RenderSample_UsesDecompressedPalette_NotRaw()
        {
            // The planted sprite pixel uses color index 5. Block 0 index 5 is
            // GREEN (0x03E0). The palette block is LZ77-COMPRESSED, so a render
            // that read the palette RAW (without decompression) would get the
            // LZ77 header bytes as colors and NOT produce this exact green.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
            Assert.NotNull(grid);

            // The sprite's index-5 pixel is centered to land at crop (0,0),
            // which is grid (0,0) for the first (top-left) cell. Green = (0,248,0).
            AssertPixel(grid, 0, 0, 0, 248, 0, 255);
        }

        [Fact]
        public void RenderSample_PaletteIndex0_vs_1_DiffersBySubPalette()
        {
            // Same sprite/frame, different palette type: block 0 index 5 = GREEN,
            // block 1 index 5 = BLUE. The cross-platform equivalent of WF
            // SwapPalette. Pixel (0,0) must differ between the two renders.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage grid0 = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
            IImage grid1 = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 1);
            Assert.NotNull(grid0);
            Assert.NotNull(grid1);

            AssertPixel(grid0, 0, 0, 0, 248, 0, 255); // green (block 0)
            AssertPixel(grid1, 0, 0, 0, 0, 248, 255); // blue  (block 1)

            byte[] p0 = grid0.GetPixelData();
            byte[] p1 = grid1.GetPixelData();
            // The two grids must not be byte-identical (sub-palette selection works).
            Assert.False(ByteArraysEqual(p0, p1));
        }

        [Fact]
        public void RenderSample_OutOfRangePaletteIndex_FallsBackToBlock0()
        {
            // paletteIndex 9 is beyond the planted 2 blocks => fall back to
            // block 0 (green) rather than returning null/garbage.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 9);
            Assert.NotNull(grid);
            AssertPixel(grid, 0, 0, 0, 248, 0, 255); // green = block 0 fallback
        }

        [Fact]
        public void RenderSample_BlankAdvance_FillsLaterCellsFromNextSection()
        {
            // Section 0 has ONE frame (lands in cell 0). Cells 1..11 find section 0
            // frame index 2,4,... missing => the blank-advance must roll to
            // section 1 (which we plant with a RED sprite). So a later cell shows
            // red — proving the persistent cursor advanced sections across cells
            // and the blank-check ran on the cropped cells.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
            Assert.NotNull(grid);

            // Cell 0 (top-left) = section 0 green at grid (0,0).
            AssertPixel(grid, 0, 0, 0, 248, 0, 255);
            // Cell 1 (second column, x base 90) = section 1 red at grid (90,0).
            // Section 1's red sprite is centered to the same crop origin (0,0),
            // i.e. grid (90,0) for the second cell.
            AssertPixel(grid, 90, 0, 248, 0, 0, 255);
        }

        // ================================================================
        // #840 — palette-override overload: render with a UNIT-palette block
        // INSTEAD of the record's own rec+0x1C palette, still applying the
        // paletteIndex sub-palette slice.
        // ================================================================

        [Fact]
        public void RenderSample_OverrideAddr0_IsIdenticalToTwoArgPath()
        {
            // The override overload with paletteOverrideAddr == 0 must be
            // byte-identical to the existing two-arg #822 path (the record's own
            // palette). This proves existing callers are unaffected.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            IImage twoArg = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
            IImage overrideZero = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0);
            Assert.NotNull(twoArg);
            Assert.NotNull(overrideZero);
            Assert.True(ByteArraysEqual(twoArg.GetPixelData(), overrideZero.GetPixelData()),
                "override-addr-0 must match the two-arg (record-own-palette) path exactly");
        }

        [Fact]
        public void RenderSample_PaletteOverride_UsesOverrideBlock_NotRecordOwn()
        {
            // PRIMARY deterministic override-effect assertion (reviewer pref):
            // the record's own palette (rec+0x1C) block 0 index 5 = GREEN.
            // The OVERRIDE palette block 0 index 5 = MAGENTA. Rendering with the
            // override address must yield MAGENTA at grid (0,0) — i.e. the
            // unit-palette override block was used, NOT the anime's own palette.
            ROM rom = MakeAnimeRom();
            // Plant an override palette block (distinct colors) at OVERRIDE_PAL_OFFSET.
            byte[] overridePal = new byte[64];
            U.write_u16(overridePal, (0 * 16 + 5) * 2, 0x7C1F); // block0 idx5 = magenta (R31 B31)
            U.write_u16(overridePal, (1 * 16 + 5) * 2, 0x03FF); // block1 idx5 = yellow  (R31 G31)
            PlantCompressed(rom, OVERRIDE_PAL_OFFSET, overridePal);
            CoreState.ROM = rom;

            uint overrideAddr = U.toPointer(OVERRIDE_PAL_OFFSET);

            // Sanity: the override pointer differs from the record's own palette
            // pointer at rec+0x1C — so a render that used the record's own palette
            // would NOT produce the override color (== GetUnitPaletteAddr ≠ rec+0x1C).
            uint recordOwnPalettePtr = rom.u32(RECORD_OFFSET + 28);
            Assert.NotEqual(recordOwnPalettePtr, overrideAddr);

            IImage withOverride = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, overrideAddr);
            Assert.NotNull(withOverride);
            // Magenta (0x7C1F) -> R=248, G=0, B=248. Proves the UNIT-palette
            // override block was applied, not the record's own green.
            AssertPixel(withOverride, 0, 0, 248, 0, 248, 255);
        }

        [Fact]
        public void RenderSample_PaletteOverride_DiffersFromRecordOwnPalette()
        {
            // Secondary pixel-diff: the same class/anime rendered with the unit
            // palette override vs the record's own palette must differ.
            ROM rom = MakeAnimeRom();
            byte[] overridePal = new byte[64];
            U.write_u16(overridePal, (0 * 16 + 5) * 2, 0x7C1F); // magenta (vs green rec-own)
            PlantCompressed(rom, OVERRIDE_PAL_OFFSET, overridePal);
            CoreState.ROM = rom;

            IImage recordOwn    = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0);
            IImage withOverride = BattleAnimeRendererCore.RenderSampleBattleAnime(
                RECORD_OFFSET, 0, U.toPointer(OVERRIDE_PAL_OFFSET));
            Assert.NotNull(recordOwn);
            Assert.NotNull(withOverride);
            Assert.False(ByteArraysEqual(recordOwn.GetPixelData(), withOverride.GetPixelData()),
                "unit-palette override must change the rendered pixels vs the anime's own palette");
        }

        [Fact]
        public void RenderSample_PaletteOverride_StillAppliesPaletteIndexSubPalette()
        {
            // The override picks the unit-palette BLOCK; paletteIndex still picks
            // the SUB-palette within it. Override block 0 idx5 = magenta;
            // block 1 idx5 = yellow. paletteIndex 0 -> magenta; 1 -> yellow.
            ROM rom = MakeAnimeRom();
            byte[] overridePal = new byte[64];
            U.write_u16(overridePal, (0 * 16 + 5) * 2, 0x7C1F); // block0 idx5 = magenta
            U.write_u16(overridePal, (1 * 16 + 5) * 2, 0x03FF); // block1 idx5 = yellow
            PlantCompressed(rom, OVERRIDE_PAL_OFFSET, overridePal);
            CoreState.ROM = rom;

            uint overrideAddr = U.toPointer(OVERRIDE_PAL_OFFSET);
            IImage idx0 = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, overrideAddr);
            IImage idx1 = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 1, overrideAddr);
            Assert.NotNull(idx0);
            Assert.NotNull(idx1);
            AssertPixel(idx0, 0, 0, 248, 0, 248, 255); // magenta = override block 0
            AssertPixel(idx1, 0, 0, 248, 248, 0, 255); // yellow  = override block 1
        }

        [Fact]
        public void RenderSample_PaletteOverride_UnsafeAddr_FallsBackToRecordOwn()
        {
            // A non-pointer / out-of-range override address is ignored (WF's
            // `if (U.isSafetyOffset(addr)) palettes = p` guard) -> the record's
            // own palette (green) is used.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            // 0x12345678 is not a valid GBA pointer (top byte 0x12, not 0x08/0x09).
            IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0x12345678);
            Assert.NotNull(grid);
            AssertPixel(grid, 0, 0, 0, 248, 0, 255); // green = record's own palette
        }

        // ================================================================
        // #1022 — live-recolor: EXACT 32-byte in-memory palette-block override
        // (the 4th param). When supplied AND exactly 32 bytes long it is used as
        // the palette DIRECTLY (bypassing ROM resolution); null / non-32 falls
        // back byte-identically to the saved (ResolveSamplePaletteBlock) path.
        // ================================================================

        // Build a 32-byte (16-color RGB555) block whose index-5 color is `gbaColor`.
        // The section-0 synthetic sprite draws color index 5, so grid pixel (0,0)
        // reflects index 5 of the rendered palette block.
        static byte[] MakeEditedBlock(ushort idx5Color)
        {
            byte[] block = new byte[32];
            U.write_u16(block, 5 * 2, idx5Color);
            return block;
        }

        [Fact]
        public void RenderSample_EditedBlockOverride_TwoDistinctBlocks_DifferInPixels()
        {
            // Two DISTINCT 32-byte override blocks (block A idx5 = magenta, block B
            // idx5 = yellow) must produce DIFFERENT rendered pixels — the override
            // recolors the preview live.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            byte[] blockA = MakeEditedBlock(0x7C1F); // magenta (R31 B31)
            byte[] blockB = MakeEditedBlock(0x03FF); // yellow  (R31 G31)

            using IImage gridA = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, blockA);
            using IImage gridB = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, blockB);
            Assert.NotNull(gridA);
            Assert.NotNull(gridB);
            Assert.False(ByteArraysEqual(gridA.GetPixelData(), gridB.GetPixelData()),
                "two distinct edited palette blocks must render different pixels");
            // Deterministic: each block's idx5 color shows at grid (0,0).
            AssertPixel(gridA, 0, 0, 248, 0, 248, 255); // magenta
            AssertPixel(gridB, 0, 0, 248, 248, 0, 255); // yellow
        }

        [Fact]
        public void RenderSample_EditedBlockOverride_TakesPriorityOverSavedPalette()
        {
            // Even with a valid record-own (green) palette, an EXACT 32-byte edited
            // block (idx5 = magenta) wins — proves it bypasses ROM resolution.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            using IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(
                RECORD_OFFSET, 0, 0, MakeEditedBlock(0x7C1F)); // magenta
            Assert.NotNull(grid);
            AssertPixel(grid, 0, 0, 248, 0, 248, 255); // magenta override, not green
        }

        [Fact]
        public void RenderSample_EditedBlockNull_IsIdenticalToThreeArgPath()
        {
            // overridePaletteBlock == null must be byte-identical to the existing
            // 3-arg saved-palette path (record's own palette, green).
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            using IImage threeArg = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0);
            using IImage nullBlock = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, null);
            Assert.NotNull(threeArg);
            Assert.NotNull(nullBlock);
            Assert.True(ByteArraysEqual(threeArg.GetPixelData(), nullBlock.GetPixelData()),
                "null edited block must match the 3-arg saved-palette path exactly");
        }

        [Fact]
        public void RenderSample_EditedBlockUndersized_FallsBackToSavedPalette()
        {
            // A non-32-byte block (e.g. 16 bytes) is IGNORED — the saved palette
            // (record's own green) is used, identical to the null path.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            byte[] undersized = new byte[16];           // NOT 32 bytes
            U.write_u16(undersized, 5 * 2, 0x7C1F);     // would be magenta if honored

            using IImage savedPath = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, null);
            using IImage withUnder = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, undersized);
            Assert.NotNull(savedPath);
            Assert.NotNull(withUnder);
            Assert.True(ByteArraysEqual(savedPath.GetPixelData(), withUnder.GetPixelData()),
                "an undersized (non-32-byte) block must fall back to the saved palette");
            AssertPixel(withUnder, 0, 0, 0, 248, 0, 255); // green = saved palette, not magenta
        }

        [Fact]
        public void RenderSample_EditedBlockOversized_FallsBackToSavedPalette()
        {
            // A block LONGER than 32 bytes (e.g. 64) is also IGNORED (the check is
            // EXACT 32, not >= 32) — the saved palette is used.
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            byte[] oversized = new byte[64];            // NOT exactly 32 bytes
            U.write_u16(oversized, 5 * 2, 0x7C1F);      // would be magenta if honored

            using IImage savedPath = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, null);
            using IImage withOver  = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0, 0, oversized);
            Assert.NotNull(savedPath);
            Assert.NotNull(withOver);
            Assert.True(ByteArraysEqual(savedPath.GetPixelData(), withOver.GetPixelData()),
                "an oversized (non-exactly-32-byte) block must fall back to the saved palette");
            AssertPixel(withOver, 0, 0, 0, 248, 0, 255); // green = saved palette
        }

        [Fact]
        public void RenderSample_NullRom_ReturnsNull()
        {
            CoreState.ROM = null;
            Assert.Null(BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0));
        }

        [Fact]
        public void RenderSample_NoImageService_ReturnsNull()
        {
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;
            var prev = CoreState.ImageService;
            CoreState.ImageService = null;
            try
            {
                Assert.Null(BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0));
            }
            finally { CoreState.ImageService = prev; }
        }

        [Fact]
        public void RenderSample_UnresolvableRecord_AllZeroPointers_ReturnsNull()
        {
            // A record whose section pointer is 0 (not a GBA pointer) is
            // unresolvable => null, no crash.
            ROM rom = MakeAnimeRom();
            // Wipe the section pointer of the record.
            U.write_u32(rom.Data, RECORD_OFFSET + 12, 0);
            CoreState.ROM = rom;

            Assert.Null(BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0));
        }

        [Fact]
        public void RenderSample_RecordPastEndOfRom_ReturnsNull()
        {
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;
            uint badOffset = (uint)rom.Data.Length - 4; // record would overrun
            Assert.Null(BattleAnimeRendererCore.RenderSampleBattleAnime(badOffset, 0));
        }

        // ================================================================
        // Resource-disposal regression (#824 Copilot review) — IImage is
        // IDisposable (Skia-backed); every intermediate cell/frame must be
        // disposed and the RETURNED grid must NOT be.
        // ================================================================

        [Fact]
        public void RenderSample_DisposesAllIntermediates_ButNotReturnedGrid()
        {
            ROM rom = MakeAnimeRom();
            CoreState.ROM = rom;

            var tracking = new TrackingImageService();
            var prevSvc = CoreState.ImageService;
            CoreState.ImageService = tracking;
            try
            {
                IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
                Assert.NotNull(grid);
                Assert.Equal(360, grid.Width);
                Assert.Equal(290, grid.Height);

                // The returned grid must still be usable (pixels intact) and
                // NOT disposed by the Core method — the caller owns it.
                Assert.False(((TrackingImage)grid).Disposed, "returned grid must NOT be disposed");
                AssertPixel(grid, 0, 0, 0, 248, 0, 255); // green still readable

                // Every OTHER image the method created (full frames + cropped
                // cells + blank-fallback cells) must be disposed — no leaks.
                int leaked = 0;
                foreach (var img in tracking.Created)
                {
                    if (ReferenceEquals(img, grid)) continue;
                    if (!img.Disposed) leaked++;
                }
                Assert.Equal(0, leaked);

                // Sanity: the method really did create intermediates (so the
                // assertion above is meaningful, not vacuously true).
                Assert.True(tracking.Created.Count > 1,
                    $"expected multiple intermediates, got {tracking.Created.Count}");

                grid.Dispose(); // caller cleans up afterwards
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderSample_NoContentPath_DisposesAllCells_ReturnsNull()
        {
            // A record with a valid palette/section/frame/OAM but a graphics
            // tile that is fully transparent (index 0) => every cropped cell is
            // blank => anyContent false => null. All generated cells must be
            // disposed (the no-content early-return path).
            ROM rom = MakeBlankGraphicsAnimeRom();
            CoreState.ROM = rom;

            var tracking = new TrackingImageService();
            var prevSvc = CoreState.ImageService;
            CoreState.ImageService = tracking;
            try
            {
                IImage grid = BattleAnimeRendererCore.RenderSampleBattleAnime(RECORD_OFFSET, 0);
                Assert.Null(grid);

                // Whatever images were created on this path must all be disposed.
                foreach (var img in tracking.Created)
                {
                    Assert.True(img.Disposed,
                        "no-content path must dispose every generated cell/frame");
                }
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        // ================================================================
        // Real-ROM integration — exercises the full path against actual
        // FE game data (skips when no ROM is present, like the existing
        // RenderAnimationTileSheet_RealRom test).
        // ================================================================

        [Fact]
        public void RenderSample_RealRom_ProducesValid360x290Grid()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip if no ROM available

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return; // skip
            CoreState.ROM = rom;

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return; // skip

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return; // skip

            // Walk the animation table; render the sample grid for the first
            // record that yields a non-null preview (mirrors WF DrawSample).
            const uint recordSize = 32;
            IImage grid = null;
            int validRecords = 0;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * recordSize);
                if (addr + recordSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr + 12))
                    || !U.isPointer(rom.u32(addr + 20))
                    || !U.isPointer(rom.u32(addr + 24)))
                    break;

                validRecords++;
                grid = BattleAnimeRendererCore.RenderSampleBattleAnime(addr, 0);
                if (grid != null) break;
            }

            Assert.True(validRecords > 0, "ROM should have at least one valid animation record");
            Assert.NotNull(grid);
            Assert.Equal(BattleAnimeRendererCore.SampleGridWidth, grid.Width);   // 360
            Assert.Equal(BattleAnimeRendererCore.SampleGridHeight, grid.Height); // 290
            // The grid must carry at least some non-transparent content.
            Assert.False(BattleAnimeRendererCore.IsBlankImage(grid, 10));
        }

        [Fact]
        public void RenderSample_RealRom_PaletteIndex0_vs_1_MayDiffer()
        {
            // On a real ROM, palette-type 0 vs 1 (Player vs Enemy) usually use
            // different sub-palettes. We can't assert they ALWAYS differ (some
            // anims are single-bank), but the call must succeed for both and the
            // grids must have matching dimensions. (Smoke-tests the paletteIndex
            // slice path against real LZ77 palette blocks.)
            string romPath = FindTestRom();
            if (romPath == null) return;

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            const uint recordSize = 32;
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * recordSize);
                if (addr + recordSize > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr + 12))
                    || !U.isPointer(rom.u32(addr + 20))
                    || !U.isPointer(rom.u32(addr + 24)))
                    break;

                IImage g0 = BattleAnimeRendererCore.RenderSampleBattleAnime(addr, 0);
                if (g0 == null) continue;
                IImage g1 = BattleAnimeRendererCore.RenderSampleBattleAnime(addr, 1);
                Assert.NotNull(g1);
                Assert.Equal(g0.Width, g1.Width);
                Assert.Equal(g0.Height, g1.Height);
                return; // one valid record is enough to smoke-test the path
            }
        }

        /// <summary>
        /// Locate a test ROM by walking up from the test assembly directory.
        /// Returns the first preferred *.gba found in roms/, or null.
        /// </summary>
        static string FindTestRom()
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string romsDir = System.IO.Path.Combine(dir, "roms");
                    if (System.IO.Directory.Exists(romsDir))
                    {
                        string[] preferred = { "FE8U.gba", "FE7U.gba", "FE8J.gba", "FE7J.gba", "FE6.gba" };
                        foreach (string name in preferred)
                        {
                            string path = System.IO.Path.Combine(romsDir, name);
                            if (System.IO.File.Exists(path)) return path;
                        }
                        string[] gbaFiles = System.IO.Directory.GetFiles(romsDir, "*.gba");
                        if (gbaFiles.Length > 0) return gbaFiles[0];
                    }
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        // ================================================================
        // Synthetic anime ROM construction
        // ================================================================
        //
        // Layout (offsets are arbitrary free-space slots, mirroring the #802
        // battle-screen test pattern):
        //   RECORD_OFFSET (0x200000): the 32-byte animation record.
        //     +12 -> SECTION_OFFSET   (pointer)
        //     +16 -> FRAME_OFFSET     (pointer to LZ77 frame stream)
        //     +20 -> OAM_OFFSET       (pointer to LZ77 OAM data)
        //     +24 -> OAM_LTR (unused) (pointer, just needs to be valid)
        //     +28 -> PALETTE_OFFSET   (pointer to LZ77 multi-block palette)
        //   SECTION_OFFSET: 12 uint32 section starts (raw, not compressed).
        //   FRAME stream: section 0 = one frame (green sprite); section 1 = one
        //                 frame (red sprite). Each frame is 12 bytes.
        //   OAM data: section 0's sprite centers index-5 at crop (0,0);
        //             section 1's sprite likewise (red).

        const uint RECORD_OFFSET   = 0x200000;
        const uint SECTION_OFFSET  = 0x201000;
        const uint FRAME_OFFSET    = 0x202000; // LZ77-compressed frame stream
        const uint OAM_OFFSET      = 0x203000; // LZ77-compressed OAM
        const uint PALETTE_OFFSET  = 0x204000; // LZ77-compressed palette (record's own)
        const uint OVERRIDE_PAL_OFFSET = 0x205000; // LZ77-compressed UNIT-palette override (#840)
        const uint GFX_GREEN       = 0x210000; // LZ77 graphics: index 5 at (0,0)
        const uint GFX_RED         = 0x211000; // LZ77 graphics: index 5 at (0,0)

        // OAM byte offsets inside the (uncompressed) OAM data we author.
        const uint OAM_SEC0_FRAME0 = 0;   // section 0 frame's OAM
        const uint OAM_SEC1_FRAME0 = 24;  // section 1 frame's OAM (after sec0's entry+term)

        static ROM MakeAnimeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            Array.Fill(data, (byte)0x00); // zero free space (0xFF would look like pointers)
            rom.LoadLow("synth.gba", data, "BE8E01");

            // ---- 32-byte animation record ----
            // Name (12 bytes) left as zeros.
            U.write_u32(rom.Data, RECORD_OFFSET + 12, U.toPointer(SECTION_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 16, U.toPointer(FRAME_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 20, U.toPointer(OAM_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 24, U.toPointer(OAM_OFFSET)); // L-to-R (unused here)
            U.write_u32(rom.Data, RECORD_OFFSET + 28, U.toPointer(PALETTE_OFFSET));

            // ---- Frame stream (UNCOMPRESSED authoring, then LZ77-compress) ----
            // Section 0: 1 frame (12 bytes) -> GFX_GREEN, OAM offset SEC0.
            // Section 1: 1 frame (12 bytes) -> GFX_RED,   OAM offset SEC1.
            byte[] frameStream = new byte[24];
            // frame 0 (section 0): [00 00 00 86][gfxPtr][oamOffset]
            frameStream[3] = 0x86;
            U.write_u32(frameStream, 4, U.toPointer(GFX_GREEN));
            U.write_u32(frameStream, 8, OAM_SEC0_FRAME0);
            // frame 1 (section 1): [00 00 00 86][gfxPtr][oamOffset]
            frameStream[15] = 0x86;
            U.write_u32(frameStream, 16, U.toPointer(GFX_RED));
            U.write_u32(frameStream, 20, OAM_SEC1_FRAME0);
            PlantCompressed(rom, FRAME_OFFSET, frameStream);

            // ---- Section array: 12 uint32 starts ----
            // Section 0 = [0, 12), Section 1 = [12, 24). Sections 2..11 = 24 (empty,
            // start==frameLen so ParseFramesInRange yields nothing => blank).
            for (int s = 0; s < 12; s++)
            {
                uint start = s == 0 ? 0u : (s == 1 ? 12u : 24u);
                U.write_u32(rom.Data, SECTION_OFFSET + (uint)(s * 4), start);
            }

            // ---- OAM data (UNCOMPRESSED authoring, then LZ77-compress) ----
            // Two 12-byte sprite entries + terminators. Each sprite: square 1x1
            // tile from sheet (0,0), palette bank 0, positioned so the sprite's
            // top-left (its index-5 pixel) lands at SCREEN (100, 30) = crop (0,0).
            //   imgX = vramX + 0x94 (148) = 100 => vramX = -48 (0xFFD0)
            //   imgY = vramY + 0x58 (88)  = 30  => vramY = -58 (0xFFC6)
            byte[] oam = new byte[48];
            WriteSpriteOAM(oam, 0, vramX: -48, vramY: -58);   // section 0 sprite
            // terminator for section 0's OAM list at entry index 1
            oam[12] = 0x01;
            WriteSpriteOAM(oam, 24, vramX: -48, vramY: -58);  // section 1 sprite
            oam[36] = 0x01; // terminator
            PlantCompressed(rom, OAM_OFFSET, oam);

            // ---- Graphics: one 4bpp tile, SOLID fill of color index 5 ----
            // A solid 8x8 tile => 64 opaque pixels in the crop, which is well
            // above WF's IsBlankBitmap threshold of 10 (a 1-pixel corner tile
            // would be counted as BLANK and wrongly advance the cursor).
            PlantCompressed(rom, GFX_GREEN, SolidTileIndex(5));
            PlantCompressed(rom, GFX_RED,   SolidTileIndex(5));

            // ---- Palette: 2 blocks x 16 colors x 2 bytes = 64 bytes ----
            // block 0 index 5 = green (0x03E0); block 1 index 5 = blue (0x7C00).
            // Also set index 5 of block 0 differently from block 1 to make the
            // red-vs-green test meaningful: but the RED sprite uses index 5 too,
            // so for section 1's red cell we instead rely on a separate palette?
            // No — both sprites use index 5. To get red for section 1 we plant a
            // *graphics* whose pixel maps to a RED index. Simpler: keep index 5
            // = green/blue for blocks 0/1, and make GFX_RED use color index 3,
            // with block 0 index 3 = red. (Re-author GFX_RED below.)
            byte[] pal = new byte[64];
            U.write_u16(pal, (0 * 16 + 5) * 2, 0x03E0); // block0 idx5 = green
            U.write_u16(pal, (0 * 16 + 3) * 2, 0x001F); // block0 idx3 = red
            U.write_u16(pal, (1 * 16 + 5) * 2, 0x7C00); // block1 idx5 = blue
            U.write_u16(pal, (1 * 16 + 3) * 2, 0x001F); // block1 idx3 = red
            PlantCompressed(rom, PALETTE_OFFSET, pal);

            // Re-author GFX_RED to use color index 3 (red) so section 1's cell is
            // visibly RED regardless of palette block.
            PlantCompressed(rom, GFX_RED, SolidTileIndex(3));

            return rom;
        }

        // Write a minimal "square 1x1 tile" OAM sprite entry at byte offset `at`.
        static void WriteSpriteOAM(byte[] oam, int at, int vramX, int vramY)
        {
            oam[at + 0] = 0x00;  // normal entry
            oam[at + 1] = 0x00;  // align = square (bits 6-7 = 0)
            oam[at + 2] = 0x00;  // not affine
            oam[at + 3] = 0x00;  // area = times1, no flip, no affine
            oam[at + 4] = 0x00;  // sheet tile: x=0, y=0
            oam[at + 5] = 0x00;  // palette bank 0
            oam[at + 6] = (byte)(vramX & 0xFF);
            oam[at + 7] = (byte)((vramX >> 8) & 0xFF);
            oam[at + 8] = (byte)(vramY & 0xFF);
            oam[at + 9] = (byte)((vramY >> 8) & 0xFF);
            oam[at + 10] = 0x00;
            oam[at + 11] = 0x00;
        }

        // One 4bpp tile (32 bytes) SOLID-filled with color `index` (all 64 px).
        static byte[] SolidTileIndex(int index)
        {
            byte packed = (byte)(((index & 0x0F) << 4) | (index & 0x0F));
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++) tile[i] = packed;
            return tile;
        }

        static void PlantCompressed(ROM rom, uint offset, byte[] raw)
        {
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
        }

        // ================================================================
        // RGBA image helpers (operate on the StubImage backing buffer)
        // ================================================================

        static IImage MakeRgbaImage(int w, int h)
        {
            IImage img = CoreState.ImageService.CreateImage(w, h);
            img.SetPixelData(new byte[w * h * 4]); // all transparent
            return img;
        }

        static void SetPixel(IImage img, int x, int y, int r, int g, int b, int a)
        {
            byte[] px = img.GetPixelData();
            int idx = (y * img.Width + x) * 4;
            px[idx + 0] = (byte)r;
            px[idx + 1] = (byte)g;
            px[idx + 2] = (byte)b;
            px[idx + 3] = (byte)a;
            img.SetPixelData(px);
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

        static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ================================================================
        // Disposal-tracking image service (for the #824 leak regression).
        // Records every image it creates and whether each was disposed.
        // ================================================================

        sealed class TrackingImage : IImage
        {
            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed => false;
            public bool Disposed { get; private set; }
            byte[] _pixels;

            public TrackingImage(int w, int h)
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
            public void Dispose() { Disposed = true; }
        }

        sealed class TrackingImageService : IImageService
        {
            public System.Collections.Generic.List<TrackingImage> Created { get; } = new();

            public IImage CreateImage(int w, int h)
            {
                var img = new TrackingImage(w, h);
                Created.Add(img);
                return img;
            }
            public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => CreateImage(w, h);
            public IImage LoadImage(string f) => null;
            public IImage LoadImageFromBytes(byte[] d) => null;
            public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
            {
                r = (byte)((gbaColor & 0x1F) << 3);
                g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
            }
            public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;
            public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => CreateImage(w, h);
            public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => CreateImage(w, h);
            public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => CreateImage(w, h);
            public byte[] Encode4bppTiles(IImage i) => null;
            public byte[] Encode8bppTiles(IImage i) => null;
            public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
            public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
        }

        /// <summary>
        /// Like <see cref="MakeAnimeRom"/> but the graphics tile is fully
        /// transparent (color index 0), so every rendered frame's crop is blank
        /// and RenderSampleBattleAnime takes the no-content (null) path.
        /// </summary>
        static ROM MakeBlankGraphicsAnimeRom()
        {
            ROM rom = MakeAnimeRom();
            // Overwrite both graphics with an all-index-0 (transparent) tile.
            PlantCompressed(rom, GFX_GREEN, SolidTileIndex(0));
            PlantCompressed(rom, GFX_RED,   SolidTileIndex(0));
            return rom;
        }
    }
}
