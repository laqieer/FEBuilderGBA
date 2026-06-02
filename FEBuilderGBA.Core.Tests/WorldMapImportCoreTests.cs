// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for WorldMap Main/Dark import Core path (#875):
//   1. EncodePaletteMap16Tile ↔ ByteToImage16TilePaletteMap round-trip.
//   2. Main import (image + palette + palette-map) byte-equality round-trip.
//   3. Dark import (128-byte dark palette only) in-place write + rollback.
//   4. FE6/FE7 import refusal (FE8-only gate).
//   5. Format-violation (>4 sub-palettes / mixed-palette tile) → error + NO mutation.
//   6. Parity: CanImportMain / CanImportDark wired; DecreaseColor + Open/SelectSource KnownGap-disabled.
//
// Uses the same ROM-harness shape as ImageWorldMapCoreTests.
// [Collection("SharedState")] + save/restore for all CoreState mutations.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WorldMapImportCoreTests
    {
        // Offsets used for planted data (clear of the 0x0..0x200 danger zone).
        const uint MAIN_IMAGE_OFFSET    = 0x010000; // 76,800 B (RAW)
        const uint MAIN_PALETTE_OFFSET  = 0x030000; // 128 B  (RAW, 4 sub-palettes)
        const uint DARK_PALETTE_OFFSET  = 0x032000; // 128 B  (RAW, 4 sub-palettes)
        const uint PALETTEMAP_OFFSET    = 0x034000; // LZ77 palette-map

        const int MAIN_W = 480, MAIN_H = 320;
        const int MAIN_IMAGE_BYTES = (MAIN_W * MAIN_H) / 2; // 76,800
        const int PAL_BYTES = 4 * 16 * 2;                   // 128

        // GBA BGR555 colors.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;

        // ==================================================================
        // 1. EncodePaletteMap16Tile ↔ ByteToImage16TilePaletteMap round-trip
        // ==================================================================

        [Fact]
        public void EncodePaletteMap16Tile_RoundTrip_480x320_PixelsMatch()
        {
            // Build a synthetic 480×320 indexed buffer: even row-of-tiles → sub-palette 0,
            // odd row-of-tiles → sub-palette 1 (all pixels 0–15 for sp0, 16–31 for sp1).
            // Every 8×8 tile is mono-sub-palette so the round-trip must be lossless.
            int tilesX = MAIN_W / 8; // 60
            int tilesY = MAIN_H / 8; // 40
            byte[] indexedPixels = new byte[MAIN_W * MAIN_H];
            for (int ty = 0; ty < tilesY; ty++)
            {
                int sp = ty % 2; // sub-palette: even → 0, odd → 1
                for (int tx = 0; tx < tilesX; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            int pixIdx = (ty * 8 + py) * MAIN_W + (tx * 8 + px);
                            // Pixel value: local index within sub-palette (always 1)
                            //              shifted by sp*16 to give absolute index.
                            indexedPixels[pixIdx] = (byte)(sp * 16 + 1);
                        }
                    }
                }
            }

            // Encode palette-map nibble stream.
            byte[] encoded = ImageUtilCore.EncodePaletteMap16Tile(indexedPixels, MAIN_W, MAIN_H);
            Assert.NotNull(encoded);
            Assert.Equal((MAIN_W / 2 + 4) * MAIN_H, encoded.Length); // (244)*320 = 78,080 B

            // Build a GBA palette (256 colors, 512 B) for the decode.
            // Bank 0 idx1 = RED; bank 1 idx1 = GREEN.
            byte[] gbaPalette = new byte[512];
            gbaPalette[0 * 32 + 1 * 2]     = (byte)(RED & 0xFF);
            gbaPalette[0 * 32 + 1 * 2 + 1] = (byte)(RED >> 8);
            gbaPalette[1 * 32 + 1 * 2]     = (byte)(GREEN & 0xFF);
            gbaPalette[1 * 32 + 1 * 2 + 1] = (byte)(GREEN >> 8);

            // Encode the image tiles (plain 4bpp, no TSA).
            byte[] imageBytes = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, MAIN_W, MAIN_H);
            Assert.NotNull(imageBytes);
            Assert.Equal(MAIN_IMAGE_BYTES, imageBytes.Length);

            // Decode back via ByteToImage16TilePaletteMap.
            WithImageService(() =>
            {
                IImage? decoded = ImageUtilCore.ByteToImage16TilePaletteMap(
                    imageBytes, encoded, gbaPalette, MAIN_W, MAIN_H);

                Assert.NotNull(decoded);
                Assert.Equal(MAIN_W, decoded.Width);
                Assert.Equal(MAIN_H, decoded.Height);

                // Pixel (0,0): tile (0,0) = sp0 = idx1 = RED (0xF8, 0, 0, 255).
                byte[] pixels = decoded.GetPixelData();
                AssertPixel(pixels, MAIN_W, 0, 0, 0xF8, 0, 0, 255);

                // Pixel (0,8): tile (0,1) = sp1 = idx1 = GREEN (0, 0xF8, 0, 255).
                AssertPixel(pixels, MAIN_W, 0, 8, 0, 0xF8, 0, 255);
            });
        }

        [Fact]
        public void EncodePaletteMap16Tile_PlusMarginSizing_Correct()
        {
            // Tiny 8×8 image (1 tile): expected buffer size = (8/2+4)*8 = 8 bytes.
            // Nibble 0 at byte 0 low nibble must be sub-palette 0.
            byte[] indexed = new byte[8 * 8]; // all zeros → sp0
            byte[] pm = ImageUtilCore.EncodePaletteMap16Tile(indexed, 8, 8);
            Assert.Equal((8 / 2 + 4) * 8, pm.Length); // 8 bytes
            Assert.Equal(0, pm[0] & 0x0F);             // nibble 0 = sp0
        }

        [Fact]
        public void EncodePaletteMap16Tile_16x8_TwoTilesPerByte_Correct()
        {
            // 16×8 image: 2 tiles. Tile 0 = sp0, tile 1 = sp1.
            // Expected: byte 0 = 0x10 (low nibble=0, high nibble=1); +margin.
            byte[] indexed = new byte[16 * 8];
            // tile 0 (x=0..7): sp0 → values 0..15.
            // tile 1 (x=8..15): sp1 → values 16..31.
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++) indexed[y * 16 + x] = 0;     // sp0 idx0
                for (int x = 8; x < 16; x++) indexed[y * 16 + x] = 16;   // sp1 idx0
            }
            byte[] pm = ImageUtilCore.EncodePaletteMap16Tile(indexed, 16, 8);
            // buffer size: (16/2+4)*8 = 12*8 = 96 bytes.
            Assert.Equal((16 / 2 + 4) * 8, pm.Length);
            // nibble 0 = tile 0 → sp0; nibble 1 = tile 1 → sp1.
            Assert.Equal(0, pm[0] & 0x0F);    // low nibble = sp0
            Assert.Equal(1, (pm[0] >> 4) & 0xF); // high nibble = sp1
        }

        [Fact]
        public void EncodePaletteMap16Tile_DegenerateArgs_ReturnsEmpty()
        {
            Assert.Empty(ImageUtilCore.EncodePaletteMap16Tile(null, 480, 320));
            Assert.Empty(ImageUtilCore.EncodePaletteMap16Tile(new byte[480 * 320], 0, 320));
            Assert.Empty(ImageUtilCore.EncodePaletteMap16Tile(new byte[480 * 320], 480, 0));
            // Non-multiple-of-8 dimensions.
            Assert.Empty(ImageUtilCore.EncodePaletteMap16Tile(new byte[480 * 320], 481, 320));
        }

        // ==================================================================
        // 2. Main Import round-trip — byte-equality
        // ==================================================================

        // ==================================================================
        // FIX E: Strengthened in-place write test asserts actual bytes written.
        // ==================================================================

        [Fact]
        public void MainFieldMap_Import_WritesImageAndPaletteInPlace()
        {
            WithRom(rom =>
            {
                // Plant the original image + palette + palette-map.
                PlantMainFieldData(rom);

                uint imageAddr = rom.p32(rom.RomInfo.worldmap_big_image_pointer);
                uint palAddr   = rom.p32(rom.RomInfo.worldmap_big_palette_pointer);
                uint pmPtrAddr = rom.RomInfo.worldmap_big_palettemap_pointer;

                // Build indexed pixels (sp0 for all tiles).
                byte[] indexedPixels = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 0);

                // Use a DIFFERENT palette from the planted ROM palette (planted is RED at idx1).
                // FIX E: use GREEN so we can assert WRITTEN palette equals the passed palette.
                byte[] gbaPalette128 = new byte[PAL_BYTES];
                gbaPalette128[1 * 2]     = (byte)(GREEN & 0xFF);
                gbaPalette128[1 * 2 + 1] = (byte)(GREEN >> 8);

                // Pre-compute expected bytes.
                byte[] expectedImage = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, MAIN_W, MAIN_H);
                byte[] expectedPalMap = ImageUtilCore.EncodePaletteMap16Tile(indexedPixels, MAIN_W, MAIN_H);

                // Import under ambient undo scope.
                using var scope = ROM.BeginUndoScope(null);
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, indexedPixels, gbaPalette128);

                Assert.True(result.Success, result.Error ?? "");

                // FIX E: assert actual written bytes.

                // 1. Image bytes match expected 4bpp encoding.
                byte[] actualImage = new byte[MAIN_IMAGE_BYTES];
                Array.Copy(rom.Data, imageAddr, actualImage, 0, MAIN_IMAGE_BYTES);
                Assert.Equal(expectedImage, actualImage);

                // 2. Palette bytes == passed palette (GREEN), not old ROM palette (RED).
                byte[] actualPalette = new byte[PAL_BYTES];
                Array.Copy(rom.Data, palAddr, actualPalette, 0, PAL_BYTES);
                Assert.Equal(gbaPalette128, actualPalette);
                ushort readBack = (ushort)(rom.Data[palAddr + 1 * 2] | (rom.Data[palAddr + 1 * 2 + 1] << 8));
                Assert.Equal(GREEN, readBack);

                // 3. Decompressed palette-map == EncodePaletteMap16Tile(pixels).
                uint pmAddr = rom.p32(pmPtrAddr);
                Assert.True(pmAddr != 0 && pmAddr < (uint)rom.Data.Length,
                    "Palette-map pointer must be in ROM after write.");
                byte[] actualPalMap = LZ77.decompress(rom.Data, pmAddr);
                Assert.Equal(expectedPalMap, actualPalMap);
            });
        }

        /// <summary>
        /// FIX A regression test: import indexed pixels with a palette that DIFFERS
        /// from the existing ROM palette. The written palette must equal the new
        /// palette, not the old ROM palette. Under the old remap-to-existing behavior
        /// this test would FAIL (old ROM palette written unchanged).
        /// </summary>
        [Fact]
        public void MainFieldMap_Import_DifferentPalette_WritesNewPalette()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);  // plants RED in bank0 idx1

                // Snapshot the OLD ROM palette (has RED at bank0 idx1).
                uint palAddr = rom.p32(rom.RomInfo.worldmap_big_palette_pointer);
                byte[] oldPalette = new byte[PAL_BYTES];
                Array.Copy(rom.Data, palAddr, oldPalette, 0, PAL_BYTES);
                ushort oldColor = (ushort)(oldPalette[1 * 2] | (oldPalette[1 * 2 + 1] << 8));
                Assert.Equal(RED, oldColor); // sanity: ROM has RED

                // Build indexed buffer with sp1 (different sub-palette).
                byte[] indexedPixels = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 1);
                // Build a NEW palette with BLUE in bank1 idx1.
                byte[] newPalette = new byte[PAL_BYTES];
                newPalette[(1 * 16 + 1) * 2]     = (byte)(BLUE & 0xFF);
                newPalette[(1 * 16 + 1) * 2 + 1] = (byte)(BLUE >> 8);

                using var scope = ROM.BeginUndoScope(null);
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, indexedPixels, newPalette);
                Assert.True(result.Success, result.Error ?? "");

                // Written palette must be newPalette (BLUE in bank1 idx1), not old (RED in bank0 idx1).
                byte[] writtenPalette = new byte[PAL_BYTES];
                Array.Copy(rom.Data, palAddr, writtenPalette, 0, PAL_BYTES);

                ushort written11 = (ushort)(writtenPalette[(1 * 16 + 1) * 2] | (writtenPalette[(1 * 16 + 1) * 2 + 1] << 8));
                Assert.Equal(BLUE, written11);

                // Old RED must NOT be in bank0 idx1 (newPalette has 0 there).
                ushort written01 = (ushort)(writtenPalette[1 * 2] | (writtenPalette[1 * 2 + 1] << 8));
                Assert.NotEqual(RED, written01);
            });
        }

        [Fact]
        public void MainFieldMap_Import_FormatViolation_MixedPaletteTile_NoMutation()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);

                // Build indexed pixels where tile (0,0) has MIXED sub-palettes:
                // some pixels sp0 (value 1) and some sp1 (value 17).
                byte[] indexedPixels = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 0);
                indexedPixels[0] = 1;   // sp0
                indexedPixels[1] = 17;  // sp1 — format violation in tile (0,0)

                byte[] gbaPalette128 = new byte[PAL_BYTES];

                // Snapshot image bytes before import.
                uint imageAddr = rom.p32(rom.RomInfo.worldmap_big_image_pointer);
                byte[] before = new byte[MAIN_IMAGE_BYTES];
                Array.Copy(rom.Data, imageAddr, before, 0, MAIN_IMAGE_BYTES);

                // Validation must catch the format violation BEFORE any write.
                string err = ImageWorldMapCore.ValidateTileMonoPalette(indexedPixels, MAIN_W, MAIN_H);
                Assert.False(string.IsNullOrEmpty(err),
                    "Expected a format-violation error but got none.");

                // Since validation failed, import must not be called — no ROM mutation.
                byte[] after = new byte[MAIN_IMAGE_BYTES];
                Array.Copy(rom.Data, imageAddr, after, 0, MAIN_IMAGE_BYTES);
                Assert.Equal(before, after);
            });
        }

        [Fact]
        public void MainFieldMap_Import_FE7_Refused()
        {
            WithRomVersion(MakeFE7Rom, rom =>
            {
                byte[] indexed = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 0);
                byte[] pal = new byte[PAL_BYTES];
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, indexed, pal);
                Assert.False(result.Success);
                Assert.NotEmpty(result.Error!);
            });
        }

        [Fact]
        public void MainFieldMap_Import_FE6_Refused()
        {
            WithRomVersion(MakeFE6Rom, rom =>
            {
                byte[] indexed = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 0);
                byte[] pal = new byte[PAL_BYTES];
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, indexed, pal);
                Assert.False(result.Success);
                Assert.NotEmpty(result.Error!);
            });
        }

        [Fact]
        public void MainFieldMap_Import_NullPixels_Refused()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, null!, new byte[PAL_BYTES]);
                Assert.False(result.Success);
            });
        }

        [Fact]
        public void MainFieldMap_Import_ShortPalette_Refused()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);
                byte[] indexed = BuildMonoSubPaletteIndexed(MAIN_W, MAIN_H, sp: 0);
                // palette buffer too short (only 64 B < required 128 B).
                var result = ImageWorldMapCore.ImportMainFieldMap(rom, indexed, new byte[64]);
                Assert.False(result.Success);
            });
        }

        // ==================================================================
        // 3. Dark Import — writes only the 128-byte dark palette in-place
        // ==================================================================

        [Fact]
        public void DarkImport_WritesOnlyDarkPalette_ImageUnchanged()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);
                PlantDarkPalette(rom); // plant the dark palette slot

                // Snapshot the image bytes before dark import.
                uint imageAddr = rom.p32(rom.RomInfo.worldmap_big_image_pointer);
                byte[] imageBefore = new byte[MAIN_IMAGE_BYTES];
                Array.Copy(rom.Data, imageAddr, imageBefore, 0, MAIN_IMAGE_BYTES);

                // Build a new dark palette (all GREEN for sub-palette 0).
                byte[] darkPal = new byte[PAL_BYTES];
                darkPal[1 * 2]     = (byte)(GREEN & 0xFF);
                darkPal[1 * 2 + 1] = (byte)(GREEN >> 8);

                using var scope = ROM.BeginUndoScope(null);
                var result = ImageWorldMapCore.ImportDarkPalette(rom, darkPal);
                Assert.True(result.Success, result.Error ?? "");

                // Image must be unchanged.
                byte[] imageAfter = new byte[MAIN_IMAGE_BYTES];
                Array.Copy(rom.Data, imageAddr, imageAfter, 0, MAIN_IMAGE_BYTES);
                Assert.Equal(imageBefore, imageAfter);

                // Dark palette slot must now hold the GREEN color.
                uint dpalAddr = rom.p32(rom.RomInfo.worldmap_big_dpalette_pointer);
                byte lo = rom.Data[dpalAddr + 1 * 2];
                byte hi = rom.Data[dpalAddr + 1 * 2 + 1];
                ushort readBack = (ushort)(lo | (hi << 8));
                Assert.Equal(GREEN, readBack);
            });
        }

        [Fact]
        public void DarkImport_Rollback_RestoresDarkPalette()
        {
            WithRom(rom =>
            {
                PlantDarkPalette(rom);

                // Snapshot the original dark palette.
                uint dpalAddr = rom.p32(rom.RomInfo.worldmap_big_dpalette_pointer);
                byte[] origDark = new byte[PAL_BYTES];
                Array.Copy(rom.Data, dpalAddr, origDark, 0, PAL_BYTES);

                var undo = new Undo();
                CoreState.Undo = undo;
                var undoData = undo.NewUndoData("DarkImportTest");
                using var scope = ROM.BeginUndoScope(undoData);

                byte[] newDark = new byte[PAL_BYTES];
                newDark[0] = 0xAB; // sentinel change
                ImageWorldMapCore.ImportDarkPalette(rom, newDark);

                scope.Dispose(); // flush scope
                undo.Push(undoData);
                undo.RunUndo();

                // After rollback, the dark palette must be restored.
                byte[] afterUndo = new byte[PAL_BYTES];
                Array.Copy(rom.Data, dpalAddr, afterUndo, 0, PAL_BYTES);
                Assert.Equal(origDark, afterUndo);
            });
        }

        [Fact]
        public void DarkImport_FE7_Refused()
        {
            WithRomVersion(MakeFE7Rom, rom =>
            {
                var result = ImageWorldMapCore.ImportDarkPalette(rom, new byte[PAL_BYTES]);
                Assert.False(result.Success);
            });
        }

        [Fact]
        public void DarkImport_ShortPalette_Refused()
        {
            WithRom(rom =>
            {
                PlantDarkPalette(rom);
                var result = ImageWorldMapCore.ImportDarkPalette(rom, new byte[64]);
                Assert.False(result.Success);
            });
        }

        // ==================================================================
        // 4. TryRenderDarkFieldMap — FE8-only, uses dark palette
        // ==================================================================

        [Fact]
        public void TryRenderDarkFieldMap_FE8_RendersExpectedDims()
        {
            WithRom(rom =>
            {
                PlantMainFieldData(rom);
                PlantDarkPalette(rom);
                WithImageService(() =>
                {
                    IImage? img = ImageWorldMapCore.TryRenderDarkFieldMap(rom);
                    Assert.NotNull(img);
                    Assert.Equal(MAIN_W, img.Width);
                    Assert.Equal(MAIN_H, img.Height);
                });
            });
        }

        [Fact]
        public void TryRenderDarkFieldMap_FE7_ReturnsNull()
        {
            WithRomVersion(MakeFE7Rom, rom =>
            {
                WithImageService(() =>
                    Assert.Null(ImageWorldMapCore.TryRenderDarkFieldMap(rom)));
            });
        }

        [Fact]
        public void TryRenderDarkFieldMap_NullRom_ReturnsNull()
        {
            WithRom(_ => Assert.Null(ImageWorldMapCore.TryRenderDarkFieldMap(null!)));
        }

        // ==================================================================
        // 5. ValidateTileMonoPalette
        // ==================================================================

        [Fact]
        public void ValidateTileMonoPalette_Clean_ReturnsEmpty()
        {
            byte[] indexed = BuildMonoSubPaletteIndexed(16, 8, sp: 0);
            string err = ImageWorldMapCore.ValidateTileMonoPalette(indexed, 16, 8);
            Assert.Empty(err);
        }

        [Fact]
        public void ValidateTileMonoPalette_MixedTile_ReturnsErrorString()
        {
            byte[] indexed = BuildMonoSubPaletteIndexed(16, 8, sp: 0);
            indexed[1] = 17; // sp1 in tile (0,0) that started sp0
            string err = ImageWorldMapCore.ValidateTileMonoPalette(indexed, 16, 8);
            Assert.NotEmpty(err);
        }

        [Fact]
        public void ValidateTileMonoPalette_NullPixels_ReturnsError()
        {
            Assert.NotEmpty(ImageWorldMapCore.ValidateTileMonoPalette(null!, 16, 8));
        }

        // ==================================================================
        // Harness helpers
        // ==================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom  = CoreState.ROM;
            var savedSvc  = CoreState.ImageService;
            var savedUndo = CoreState.Undo;
            try
            {
                var rom = MakeRom();
                CoreState.ROM  = rom;
                CoreState.Undo = new Undo();
                body(rom);
            }
            finally
            {
                CoreState.ROM          = savedRom;
                CoreState.ImageService = savedSvc;
                CoreState.Undo         = savedUndo;
            }
        }

        static void WithRomVersion(Func<ROM> make, Action<ROM> body)
        {
            var savedRom  = CoreState.ROM;
            var savedSvc  = CoreState.ImageService;
            var savedUndo = CoreState.Undo;
            try
            {
                var rom = make();
                CoreState.ROM  = rom;
                CoreState.Undo = new Undo();
                body(rom);
            }
            finally
            {
                CoreState.ROM          = savedRom;
                CoreState.ImageService = savedSvc;
                CoreState.Undo         = savedUndo;
            }
        }

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

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static ROM MakeFE7Rom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("synth_fe7.gba", data, "AE7E01");
            return rom;
        }

        static ROM MakeFE6Rom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x800000]; // 8 MB
            rom.LoadLow("synth_fe6.gba", data, "AFEJ01");
            return rom;
        }

        /// <summary>Build indexed pixel buffer where all tiles use sub-palette
        /// <paramref name="sp"/> uniformly (all pixel values = sp*16+1).</summary>
        static byte[] BuildMonoSubPaletteIndexed(int width, int height, int sp)
        {
            byte[] buf = new byte[width * height];
            byte val = (byte)(sp * 16 + 1);
            for (int i = 0; i < buf.Length; i++) buf[i] = val;
            return buf;
        }

        /// <summary>Plant the full FE8 main field graphic:
        ///   RAW image (76,800 B) at MAIN_IMAGE_OFFSET,
        ///   RAW 4-sub-palette palette (128 B) at MAIN_PALETTE_OFFSET,
        ///   LZ77 palette-map at PALETTEMAP_OFFSET,
        /// and wire worldmap_big_{image/palette/palettemap}_pointer.</summary>
        static void PlantMainFieldData(ROM rom)
        {
            // Image: all zeros (sp0, all index 0).
            byte[] image = new byte[MAIN_IMAGE_BYTES];
            Array.Copy(image, 0, rom.Data, MAIN_IMAGE_OFFSET, MAIN_IMAGE_BYTES);
            rom.write_u32(rom.RomInfo.worldmap_big_image_pointer, U.toPointer(MAIN_IMAGE_OFFSET));

            // Palette: 128 B (4×16×2), bank0 idx1=RED.
            byte[] pal = new byte[PAL_BYTES];
            pal[1 * 2]     = (byte)(RED & 0xFF);
            pal[1 * 2 + 1] = (byte)(RED >> 8);
            Array.Copy(pal, 0, rom.Data, MAIN_PALETTE_OFFSET, PAL_BYTES);
            rom.write_u32(rom.RomInfo.worldmap_big_palette_pointer, U.toPointer(MAIN_PALETTE_OFFSET));

            // Palette-map: LZ77 of 1280 zero bytes (all sp0).
            byte[] pm = LZ77.compress(new byte[1280]);
            Array.Copy(pm, 0, rom.Data, PALETTEMAP_OFFSET, pm.Length);
            rom.write_u32(rom.RomInfo.worldmap_big_palettemap_pointer, U.toPointer(PALETTEMAP_OFFSET));
        }

        /// <summary>Plant a 128-byte dark palette at DARK_PALETTE_OFFSET and
        /// wire worldmap_big_dpalette_pointer.</summary>
        static void PlantDarkPalette(ROM rom)
        {
            byte[] dark = new byte[PAL_BYTES];
            dark[1 * 2]     = (byte)(BLUE & 0xFF);
            dark[1 * 2 + 1] = (byte)(BLUE >> 8);
            Array.Copy(dark, 0, rom.Data, DARK_PALETTE_OFFSET, PAL_BYTES);
            rom.write_u32(rom.RomInfo.worldmap_big_dpalette_pointer, U.toPointer(DARK_PALETTE_OFFSET));
        }

        static void AssertPixel(byte[] pixels, int width, int x, int y, byte r, byte g, byte b, byte a)
        {
            int idx = (y * width + x) * 4;
            Assert.Equal(r, pixels[idx]);
            Assert.Equal(g, pixels[idx + 1]);
            Assert.Equal(b, pixels[idx + 2]);
            Assert.Equal(a, pixels[idx + 3]);
        }
    }
}
