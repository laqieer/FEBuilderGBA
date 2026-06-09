// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageTSAEditorCore.TryRenderMainImage (#808, #769 item 1).
//
// Verifies the read-only TSA-composited main-image render used by the Avalonia
// ImageTSAEditorView: dimensions (tile-count vs pixel convention), planted-tile
// placement, H/V flip, palette-bank selection, index-0 transparency, the
// raw-non-header byte-offset path, the header-TSA path, the deliberate 10-bit
// (vs WF 8-bit) tile-index mask, and null/out-of-bounds safety.
//
// Uses the in-assembly StubImageService/StubImage (defined in
// BattleAnimeDetailTests.cs) which capture RGBA pixel data so we can assert the
// exact composed output.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageTSAEditorCoreTests
    {
        const uint IMAGE_OFFSET   = 0x1000;
        const uint TSA_OFFSET     = 0x4000;
        const uint PALETTE_OFFSET = 0x8000;

        // GBA 5-5-5 colors used in the planted palette.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;
        const ushort WHITE = 0x7FFF;

        // -----------------------------------------------------------------
        // Dimensions + planted-tile placement (raw, non-header TSA)
        // -----------------------------------------------------------------

        [Fact]
        public void RawTSA_RendersTileCountTimesEightDimensions()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[]
                {
                    Cell(1, false, false, 0), Cell(1, false, false, 0),
                    Cell(1, false, false, 0), Cell(1, false, false, 0),
                });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 2, 2, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                Assert.Equal(16, img.Width);   // width8(2) * 8
                Assert.Equal(16, img.Height);  // height8(2) * 8
            });
        }

        [Fact]
        public void RawTSA_PlantsMarkerTileAtExpectedPixels()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Marker pixel (0,0) is color index 2 = green; neighbour (1,0) is
                // color index 1 = red. (Bank 0.)
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
                AssertPixel(img, 1, 0, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RawTSA_Index0_IsTransparent()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());     // tile 0 is all index 0
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(0, false, false, 0) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // tile 0 is entirely color index 0 -> alpha 0 everywhere.
                byte[] px = img.GetPixelData();
                Assert.Equal(0, px[(0 * img.Width + 0) * 4 + 3]);
                Assert.Equal(0, px[(7 * img.Width + 7) * 4 + 3]);
            });
        }

        [Fact]
        public void RawTSA_HFlip_MapsMarkerToRightEdge()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // cell0 normal, cell1 H-flipped.
                PlantRawCells(rom, TSA_OFFSET, new ushort[]
                {
                    Cell(1, false, false, 0), Cell(1, true, false, 0),
                });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 2, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // cell1 occupies x=[8..15]; H-flip moves the (0,0) marker to local
                // x=7 -> global x=15, y=0 (green). Local (0,0) of cell1 -> red.
                AssertPixel(img, 15, 0, 0, 248, 0, 255);
                AssertPixel(img, 8, 0, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RawTSA_VFlip_MapsMarkerToBottomEdge()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // cell0 normal (top), cell1 V-flipped (bottom).
                PlantRawCells(rom, TSA_OFFSET, new ushort[]
                {
                    Cell(1, false, false, 0), Cell(1, false, true, 0),
                });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 2, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // cell1 occupies y=[8..15]; V-flip moves the (0,0) marker to local
                // y=7 -> global y=15, x=0 (green).
                AssertPixel(img, 0, 15, 0, 248, 0, 255);
            });
        }

        [Fact]
        public void RawTSA_PaletteBank_SelectsCorrectBank()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // Use palette bank 1: color1 = blue, color2 = white.
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 1) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Marker pixel (0,0) = color2 of bank1 = white; (1,0) = color1 = blue.
                AssertPixel(img, 0, 0, 248, 248, 248, 255);
                AssertPixel(img, 1, 0, 0, 0, 248, 255);
            });
        }

        [Fact]
        public void RawNonHeader_HonoursByteOffset()
        {
            // Review #7: a positive nonzero TSA offset must decode from tsaAddr,
            // not from offset 0. Plant a DIFFERENT (red) cell at ROM offset 0x200
            // and the real (green-marker) cell at TSA_OFFSET; the render must
            // reflect TSA_OFFSET.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // Decoy cell at a low offset (would render tile 1 too, but if the
                // offset were ignored we'd read this instead).
                PlantRawCells(rom, 0x200, new ushort[] { Cell(0, false, false, 0) });
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // The marker (green) proves the decode started at TSA_OFFSET.
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
            });
        }

        // -----------------------------------------------------------------
        // LZ77-compressed TSA + header TSA
        // -----------------------------------------------------------------

        [Fact]
        public void Lz77TSA_RendersMarkerTile()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // Two cells (4 bytes) so the uncompressed LZ77 size is >= 3
                // (getCompressedSize rejects payloads smaller than 3 bytes).
                byte[] tsaRaw = CellsToBytes(new ushort[]
                {
                    Cell(1, false, false, 0), Cell(0, false, false, 0),
                });
                PlantBytes(rom, TSA_OFFSET, LZ77.compress(tsaRaw));

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 2, 1, IMAGE_OFFSET, false, true, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
            });
        }

        [Fact]
        public void HeaderTSA_Renders()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // Header {x=0,y=0} + one cell -> tile[0] at (0,0).
                byte[] tsa = new byte[] { 0x00, 0x00,
                    (byte)(Cell(1, false, false, 0) & 0xFF),
                    (byte)(Cell(1, false, false, 0) >> 8) };
                PlantBytes(rom, TSA_OFFSET, tsa);

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, true, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                Assert.Equal(8, img.Width);
                Assert.Equal(8, img.Height);
                // Header path renders tile[0] = marker tile.
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
            });
        }

        // -----------------------------------------------------------------
        // 10-bit tile index (documented divergence vs WF 8-bit canvas mask)
        // -----------------------------------------------------------------

        [Fact]
        public void TileIndex0x100_UsesFull10BitIndex()
        {
            // Review #6: the Core decoder masks the TSA tile index to 10 bits
            // (& 0x3FF), unlike the WF TSA canvas (& 0xff). Tile 0x100 must be
            // read as tile 0x100, NOT tile 0 (which an 8-bit mask would produce).
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] tiles = new byte[0x101 * 32];
                FillTile(tiles, 0, 1);              // tile 0   -> all red
                FillTile(tiles, 0x100, 1);          // tile 256 -> all green-marker
                SetPixel(tiles, 0x100, 0, 0, 2);
                PlantImage(rom, tiles);
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(0x100, false, false, 0) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Pixel (0,0) is the tile-0x100 marker (green). If the index were
                // 8-bit masked to 0 we'd see tile 0 here (red at (0,0)).
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
            });
        }

        // -----------------------------------------------------------------
        // Graceful / bounded edge cases
        // -----------------------------------------------------------------

        [Fact]
        public void PaletteBankAbovePlantedBanks_DoesNotThrow()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                // Only 2 banks planted; request bank 5.
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 5) });

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img); // out-of-range bank bytes are bounds-skipped
            });
        }

        [Fact]
        public void PaletteNearRomEnd_ClampsWithoutThrowing()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                uint paletteNearEnd = (uint)rom.Data.Length - 10; // < 512 bytes left

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, paletteNearEnd);

                Assert.NotNull(img); // clamped palette read, no throw
            });
        }

        [Fact]
        public void RawTSA_NearRomEnd_NoThrow_BoundedRead()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                uint tsaNearEnd = (uint)rom.Data.Length - 4;

                IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 32, 20, IMAGE_OFFSET, false, false, tsaNearEnd, PALETTE_OFFSET);

                Assert.NotNull(img); // reads at most the bytes available, no throw
            });
        }

        // -----------------------------------------------------------------
        // Null / out-of-bounds safety
        // -----------------------------------------------------------------

        [Fact]
        public void NullRom_ReturnsNull()
        {
            WithImageService(() =>
            {
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    null, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void NoImageService_ReturnsNull()
        {
            var rom = MakeRom();
            PlantImage(rom, MarkerTiles());
            PlantPalette(rom, StandardPalette());
            PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void ZeroDimensions_ReturnNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 0, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET));
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 0, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void CorruptImagePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                // imageAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, 0, false, false, TSA_OFFSET, PALETTE_OFFSET));
                // imageAddr at a zero-filled region -> not a valid LZ77 stream.
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void TruncatedLz77Image_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                // Valid 0x10 header claiming 0x100 bytes, planted 4 bytes from the
                // ROM end so the stream is truncated -> getCompressedSize == 0.
                uint addr = (uint)rom.Data.Length - 4;
                rom.Data[addr + 0] = 0x10;
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                rom.Data[addr + 3] = 0x00;
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, addr, false, false, TSA_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void CorruptPalettePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                // paletteAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, 0));
            });
        }

        [Fact]
        public void CorruptTsaPointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // tsaAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, 0, PALETTE_OFFSET));
            });
        }

        // =================================================================
        // RenderChipList (#819) — the 4-column single-bank chip-list strip
        // mirroring WF ImageTSAEditorForm.MakeCHIPLIST:
        //   * Output = 32 x (tileCount*8): 4 columns of 8px (orig / Hflip /
        //     Vflip / HVflip), one 8x8 cell per tile-row, SINGLE palette bank 0.
        //   * index 0 renders OPAQUE.
        // Reuses the #810 synthetic-ROM harness (MarkerTiles + StandardPalette).
        // MarkerTiles() = 2 tiles: tile 0 all index 0, tile 1 index-1 fill with
        // an index-2 marker at (0,0). So tile 1 sits on the SECOND row (tileY=8).
        // bank0 idx1=red, idx2=green; bank1 idx1=blue, idx2=white; color 0 =
        // black (unset) but OPAQUE.
        // =================================================================

        [Fact]
        public void RenderChipList_RendersThirtyTwoByTileCountTimesEightDimensions()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());          // 2 tiles
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // 4 columns * 8px = 32 wide.
                Assert.Equal(32, img.Width);
                // 2 tiles * 8px = 16 tall.
                Assert.Equal(16, img.Height);
            });
        }

        [Fact]
        public void RenderChipList_SingleTile_RendersThirtyTwoByEight()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, SingleMarkerTile());      // exactly 1 tile
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                Assert.Equal(32, img.Width);   // 4 cols * 8
                Assert.Equal(8, img.Height);   // 1 tile * 8
            });
        }

        [Fact]
        public void RenderChipList_OriginalColumn_HasMarkerTopLeft()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // tile 1 is on the 2nd row -> tileY = 8. col 0 (orig, x base 0):
                // marker src(0,0) stays at (0, 8) = green; neighbour (1, 8) = red.
                AssertPixel(img, 0, 8, 0, 248, 0, 255);
                AssertPixel(img, 1, 8, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RenderChipList_HFlipColumn_MovesMarkerToTopRight()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // col 1 (Hflip, x base 8): src(0,0) -> local x=7 -> global (15, 8).
                AssertPixel(img, 15, 8, 0, 248, 0, 255);
                // The column's local (0,0) is now an index-1 (red) pixel.
                AssertPixel(img, 8, 8, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RenderChipList_VFlipColumn_MovesMarkerToBottomLeft()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // col 2 (Vflip, x base 16): src(0,0) -> local y=7 -> global (16, 15).
                AssertPixel(img, 16, 15, 0, 248, 0, 255);
            });
        }

        [Fact]
        public void RenderChipList_HVFlipColumn_MovesMarkerToBottomRight()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // col 3 (HVflip, x base 24): src(0,0) -> local (7,7) -> global (31, 15).
                AssertPixel(img, 31, 15, 0, 248, 0, 255);
            });
        }

        [Fact]
        public void RenderChipList_UsesPaletteBank0_NotBank1()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // The marker (index 2) renders as GREEN (bank 0 idx2 = 0x03E0),
                // NOT white (bank 1 idx2 = 0x7FFF). This proves bank 0 is used.
                AssertPixel(img, 0, 8, 0, 248, 0, 255);
                // And the index-1 fill is RED (bank 0), not blue (bank 1).
                AssertPixel(img, 1, 8, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RenderChipList_SingleBank_NoFifthThroughEighthColumns()
        {
            // The structural difference from #807's 8-col/2-bank chip list: this
            // chip list is SINGLE bank, exactly 4 columns wide (32px). There is
            // no bank-1 5th-8th column, so the canvas width must be 32 (not 64).
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Width is exactly the 4 single-bank columns -> 32. Were a 5th-8th
                // bank-1 column present (the #807 layout) the width would be 64.
                Assert.Equal(32, img.Width);
                // Every RGBA pixel lives within the 32-wide buffer; the pixel data
                // length equals exactly 32 * height * 4 (no extra bank-1 columns).
                byte[] px = img.GetPixelData();
                Assert.Equal(32 * img.Height * 4, px.Length);
            });
        }

        [Fact]
        public void RenderChipList_Index0_RendersOpaque()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());          // tile 0 = all index 0
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // tile 0 is on the FIRST row (tileY=0), entirely index 0. Per WF
                // MakeCHIPLIST (transparent_index 0xFF + Blank color-0 bg), index 0
                // must render OPAQUE (alpha 255). color 0 = 0x0000 = black.
                byte[] px = img.GetPixelData();
                Assert.Equal(255, px[(0 * img.Width + 0) * 4 + 3]);   // (0,0) alpha
                Assert.Equal(255, px[(7 * img.Width + 7) * 4 + 3]);   // (7,7) alpha
                AssertPixel(img, 0, 0, 0, 0, 0, 255);                 // opaque black
            });
        }

        // ----- RenderChipList null / out-of-bounds safety -----

        [Fact]
        public void RenderChipList_NullRom_ReturnsNull()
        {
            WithImageService(() =>
            {
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    null, IMAGE_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderChipList_NoImageService_ReturnsNull()
        {
            var rom = MakeRom();
            PlantImage(rom, MarkerTiles());
            PlantPalette(rom, StandardPalette());

            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void RenderChipList_CorruptImagePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                // imageAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, 0, PALETTE_OFFSET));
                // imageAddr at a zero-filled region -> not a valid LZ77 stream.
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderChipList_CorruptPalettePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                // paletteAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, 0));
            });
        }

        [Fact]
        public void RenderChipList_TruncatedLz77Image_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                // Valid 0x10 header claiming 0x100 bytes, planted 4 bytes from the
                // ROM end so the stream is truncated -> getCompressedSize == 0.
                // (addr is int for idiomatic array indexing; cast to uint at the
                // LZ77 / RenderChipList call sites.)
                int addr = rom.Data.Length - 4;
                rom.Data[addr + 0] = 0x10;
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                rom.Data[addr + 3] = 0x00;
                Assert.Equal(0u, LZ77.getCompressedSize(rom.Data, (uint)addr));
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, (uint)addr, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderChipList_ImagePointerAtLastBytes_NoThrow_ReturnsNull()
        {
            // #818 last-bytes-pointer no-throw case: an image address within the
            // final 4 bytes of ROM must not throw inside LZ77.getCompressedSize /
            // the end-of-ROM bound guard -- it returns null gracefully.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                uint imageNearEnd = (uint)rom.Data.Length - 2;
                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, imageNearEnd, PALETTE_OFFSET);
                Assert.Null(img); // no throw, bounded read
            });
        }

        [Fact]
        public void ImagePointerThreeBytesFromEnd_WithMagic_NoThrow_BothReturnNull()
        {
            // PR #821 review (item 1): the LZ77 header is 4 bytes and
            // getCompressedSize reads input[addr+3]. An image pointer 3 bytes
            // from the ROM end (addr+3 == Data.Length, out of range) passes
            // isSafetyOffset but would throw IndexOutOfRangeException inside
            // getCompressedSize WITHOUT the explicit 4-byte-header guard. With
            // the guard, BOTH RenderChipList and TryRenderMainImage must return
            // null (a throw fails the test -- there is no try/catch here).
            // Plant the 0x10 magic so the throw would otherwise be reached.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

                int addr = rom.Data.Length - 3;          // only 3 header bytes in-bounds
                rom.Data[addr + 0] = 0x10;               // LZ77 magic
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                // addr+3 is out of range -- the 4-byte-header guard must reject
                // BEFORE getCompressedSize touches input[addr+3].

                // Chip-list path (no TSA): guard short-circuits -> null, no throw.
                Assert.Null(ImageTSAEditorCore.RenderChipList(
                    rom, (uint)addr, PALETTE_OFFSET));

                // Main-image path (delegates to the SAME loader): also null, no throw.
                Assert.Null(ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, (uint)addr, false, false, TSA_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderChipList_PaletteNearRomEnd_ClampsWithoutThrowing()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                uint paletteNearEnd = (uint)rom.Data.Length - 10; // < 512 bytes left
                IImage img = ImageTSAEditorCore.RenderChipList(
                    rom, IMAGE_OFFSET, paletteNearEnd);
                Assert.NotNull(img); // clamped palette read, no throw
            });
        }

        // =================================================================
        // RenderRawTilesheet (#974) — the raw 4bpp tilesheet PNG export:
        //   * Output = 64 x (ceil(tileCount/8)*8): an 8-tile-wide strip,
        //     left-to-right / top-to-bottom, SINGLE palette bank 0,
        //     index 0 OPAQUE. NOT the TSA-composited canvas (#808), NOT the
        //     4-column flip strip (#819).
        // Reuses the same MarkerTiles + StandardPalette synthetic harness.
        // =================================================================

        [Fact]
        public void RenderRawTilesheet_TwoTiles_RendersSixtyFourByEight()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());          // 2 tiles
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // 8 tiles wide * 8px = 64 wide.
                Assert.Equal(64, img.Width);
                // 2 tiles fit in 1 row -> ceil(2/8)=1 row * 8px = 8 tall.
                Assert.Equal(8, img.Height);
            });
        }

        [Fact]
        public void RenderRawTilesheet_NineTiles_WrapsToSecondRow()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                // 9 tiles: 8 fill the first row, the 9th wraps to the 2nd row.
                byte[] tiles = new byte[9 * 32];
                FillTile(tiles, 0, 1);                   // tile 0 -> red
                FillTile(tiles, 8, 1);                   // tile 8 -> red fill
                SetPixel(tiles, 8, 0, 0, 2);             // tile 8 (0,0) -> green marker
                PlantImage(rom, tiles);
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                Assert.Equal(64, img.Width);
                // ceil(9/8) = 2 rows * 8px = 16 tall.
                Assert.Equal(16, img.Height);
                // Tile 8 is the first tile of the 2nd row (col 0, y base 8); its
                // (0,0) marker -> global (0, 8) = green.
                AssertPixel(img, 0, 8, 0, 248, 0, 255);
            });
        }

        [Fact]
        public void RenderRawTilesheet_LaysTilesLeftToRight()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                // 2 tiles: tile 0 all index 0 (black/opaque), tile 1 red fill with
                // a green marker at (0,0). The raw strip lays tile 0 at col 0 and
                // tile 1 at col 1 (x base 8) — UNLIKE RenderChipList, which stacks
                // tiles vertically. This pins the horizontal layout.
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Tile 1 sits at col 1 (x base 8) on the SAME row (y=0). Its (0,0)
                // marker -> global (8, 0) = green; neighbour (9,0) = red.
                AssertPixel(img, 8, 0, 0, 248, 0, 255);
                AssertPixel(img, 9, 0, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void RenderRawTilesheet_Index0_RendersOpaque()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());          // tile 0 = all index 0
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Tile 0 sits at col 0 (x base 0), entirely index 0 -> OPAQUE black.
                AssertPixel(img, 0, 0, 0, 0, 0, 255);
                byte[] px = img.GetPixelData();
                Assert.Equal(255, px[(7 * img.Width + 7) * 4 + 3]);
            });
        }

        [Fact]
        public void RenderRawTilesheet_UsesPaletteBank0()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                IImage img = ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET);

                Assert.NotNull(img);
                // Tile 1 (col 1) marker (index 2) is GREEN (bank 0), NOT white
                // (bank 1) — proving bank 0 is used.
                AssertPixel(img, 8, 0, 0, 248, 0, 255);
            });
        }

        // ----- RenderRawTilesheet null / out-of-bounds safety -----

        [Fact]
        public void RenderRawTilesheet_NullRom_ReturnsNull()
        {
            WithImageService(() =>
            {
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    null, IMAGE_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderRawTilesheet_NoImageService_ReturnsNull()
        {
            var rom = MakeRom();
            PlantImage(rom, MarkerTiles());
            PlantPalette(rom, StandardPalette());

            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void RenderRawTilesheet_CorruptImagePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                // imageAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    rom, 0, PALETTE_OFFSET));
                // imageAddr at a zero-filled region -> not a valid LZ77 stream.
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, PALETTE_OFFSET));
            });
        }

        [Fact]
        public void RenderRawTilesheet_CorruptPalettePointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                // paletteAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    rom, IMAGE_OFFSET, 0));
            });
        }

        [Fact]
        public void RenderRawTilesheet_TruncatedLz77Image_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantPalette(rom, StandardPalette());
                int addr = rom.Data.Length - 4;
                rom.Data[addr + 0] = 0x10;
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                rom.Data[addr + 3] = 0x00;
                Assert.Null(ImageTSAEditorCore.RenderRawTilesheet(
                    rom, (uint)addr, PALETTE_OFFSET));
            });
        }

        // =================================================================
        // image2-join + compressed paletteType (#1074) — the 10-arg
        // TryRenderMainImage overload.
        //   * image2 join: a 2nd LZ77 tile image is concatenated AFTER the
        //     first (order image ++ image2), so a TSA cell with a tile index
        //     >= image1's tile count reaches the joined image2 tiles.
        //   * compressed palette: the palette address is an LZ77 stream
        //     (paletteType == 1) clamped to <=512 bytes.
        //   * backward-compat: the 8-arg wrapper == the 10-arg with
        //     image2Addr:0, isCompressedPalette:false.
        //   * no-throw: corrupt/near-EOF compressed palette -> null (NOT a raw
        //     fallback); invalid/zero image2 -> single image; near-EOF LZ77-TSA
        //     pointer -> null.
        // Reuses the MarkerTiles + StandardPalette synthetic harness.
        // =================================================================

        // Image2 lives at a separate offset from IMAGE_OFFSET (0x1000) and the
        // TSA (0x4000) / palette (0x8000) so the planted streams never overlap.
        const uint IMAGE2_OFFSET = 0x2000;

        /// <summary>image2 = a single tile that is entirely color index 2
        /// (green in bank 0). Joined after MarkerTiles()'s 2 tiles, this becomes
        /// tile index 2.</summary>
        static byte[] Image2GreenTile()
        {
            byte[] tiles = new byte[1 * 32];
            FillTile(tiles, 0, 2); // all index 2
            return tiles;
        }

        [Fact]
        public void Image2Join_TsaCellReachesSecondImageTile()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());                  // 2 tiles (idx 0,1)
                PlantBytes(rom, IMAGE2_OFFSET, LZ77.compress(Image2GreenTile())); // tile idx 2
                PlantPalette(rom, StandardPalette());
                // TSA cell references tile index 2 -> ONLY reachable via image2.
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(2, false, false, 0) });

                using IImage joined = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: IMAGE2_OFFSET, isCompressedPalette: false);

                Assert.NotNull(joined);
                // image2 tile 2 is all green -> pixel (0,0) is green.
                AssertPixel(joined, 0, 0, 0, 248, 0, 255);

                // WITHOUT image2 the same tile index 2 is out of range -> that
                // cell renders blank (alpha 0). Prove the two renders differ.
                using IImage single = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);

                Assert.NotNull(single);
                byte[] singlePx = single.GetPixelData();
                Assert.Equal(0, singlePx[(0 * single.Width + 0) * 4 + 3]); // (0,0) transparent
                Assert.NotEqual(joined.GetPixelData(), single.GetPixelData());
            });
        }

        [Fact]
        public void Image2Join_ZeroOrInvalid_RendersSingleImage()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

                // image2Addr 0 -> no-op (single image), no throw.
                using IImage zero = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);
                Assert.NotNull(zero);
                AssertPixel(zero, 0, 0, 0, 248, 0, 255); // tile 1 marker

                // image2Addr pointing at a zero-filled (non-LZ77) region -> no-op
                // (single image), no throw, byte-identical to the zero case.
                using IImage invalid = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0xC000u, isCompressedPalette: false);
                Assert.NotNull(invalid);
                Assert.Equal(zero.GetPixelData(), invalid.GetPixelData());
            });
        }

        [Fact]
        public void CompressedPalette_Bank0_EqualsRawPaletteRender()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });

                byte[] rawPal = StandardPalette();           // 512-byte raw palette

                // Render A: RAW palette at PALETTE_OFFSET (isCompressedPalette:false).
                PlantPalette(rom, rawPal);
                using IImage rawRender = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);
                Assert.NotNull(rawRender);

                // Render B: the SAME colors, LZ77-compressed, decoded with
                // isCompressedPalette:true at a separate offset.
                const uint COMP_PAL_OFFSET = 0xA000;
                PlantBytes(rom, COMP_PAL_OFFSET, LZ77.compress(rawPal));
                using IImage compRender = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, COMP_PAL_OFFSET,
                    image2Addr: 0u, isCompressedPalette: true);
                Assert.NotNull(compRender);

                // Identical colors -> identical PNG.
                Assert.Equal(rawRender.EncodePng(), compRender.EncodePng());
            });
        }

        [Fact]
        public void CompressedPalette_NonzeroBank512Bytes_EqualsRawPaletteRender()
        {
            // Refinement #3: exercise a FULL 512-byte palette with a NONZERO bank
            // (bank 3), not only bank 0, so the compressed decode covers the
            // whole 16-bank block.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                // Cell uses palette bank 3.
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 3) });

                // 512-byte palette: bank 3 idx1 = blue, idx2 = white (so the
                // bank-3 colors are nonzero and distinct from bank 0).
                byte[] rawPal = new byte[512];
                SetColor(rawPal, 3, 1, BLUE);
                SetColor(rawPal, 3, 2, WHITE);
                Assert.Equal(512, rawPal.Length);

                PlantPalette(rom, rawPal);
                using IImage rawRender = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);
                Assert.NotNull(rawRender);
                // bank 3 idx2 (marker) = white; idx1 = blue.
                AssertPixel(rawRender, 0, 0, 248, 248, 248, 255);
                AssertPixel(rawRender, 1, 0, 0, 0, 248, 255);

                const uint COMP_PAL_OFFSET = 0xA000;
                PlantBytes(rom, COMP_PAL_OFFSET, LZ77.compress(rawPal));
                using IImage compRender = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, COMP_PAL_OFFSET,
                    image2Addr: 0u, isCompressedPalette: true);
                Assert.NotNull(compRender);

                Assert.Equal(rawRender.EncodePng(), compRender.EncodePng());
            });
        }

        [Fact]
        public void EightArgWrapper_EqualsTenArgWithDefaults()
        {
            // Refinement #4: the 8-arg back-compat wrapper must be byte-identical
            // to the 10-arg with image2Addr:0, isCompressedPalette:false.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                PlantRawCells(rom, TSA_OFFSET, new ushort[]
                {
                    Cell(1, false, false, 0), Cell(0, false, false, 0),
                    Cell(1, true, false, 0),  Cell(1, false, true, 0),
                });

                using IImage eight = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 2, 2, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET);
                using IImage ten = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 2, 2, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);

                Assert.NotNull(eight);
                Assert.NotNull(ten);
                Assert.Equal(eight.EncodePng(), ten.EncodePng());
            });
        }

        [Fact]
        public void CompressedPalette_CorruptStream_ReturnsNull_NoRawFallback()
        {
            // Refinement #2: a bad compressed-palette stream returns null (NOT a
            // silent raw read), so a paletteType==1 failure is visible.
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                // PALETTE_OFFSET holds a valid RAW palette, but we ask for a
                // compressed decode -> the (non-LZ77) bytes there are not a valid
                // stream -> getCompressedSize == 0 -> null (no raw fallback).
                PlantPalette(rom, StandardPalette());

                using IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: true);
                Assert.Null(img);
            });
        }

        [Fact]
        public void CompressedPalette_NearRomEnd_ReturnsNull_NoThrow()
        {
            // Refinement #2: a truncated LZ77 palette near the ROM end must return
            // null (no throw, no raw fallback).
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantRawCells(rom, TSA_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                // Valid 0x10 header claiming 0x100 bytes, planted 4 bytes from the
                // ROM end -> getCompressedSize == 0.
                uint addr = (uint)rom.Data.Length - 4;
                rom.Data[addr + 0] = 0x10;
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                rom.Data[addr + 3] = 0x00;

                using IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, false, TSA_OFFSET, addr,
                    image2Addr: 0u, isCompressedPalette: true);
                Assert.Null(img);
            });
        }

        [Fact]
        public void Lz77Tsa_PointerNearRomEnd_ReturnsNull_NoThrow()
        {
            // Refinement #1: an LZ77-TSA pointer in the last 1-3 ROM bytes passes
            // isSafetyOffset but would throw inside getCompressedSize without the
            // 4-byte-header guard. With the guard it returns null (no throw — there
            // is no try/catch here).
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                int addr = rom.Data.Length - 3;       // only 3 header bytes in-bounds
                rom.Data[addr + 0] = 0x10;            // LZ77 magic
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                // addr+3 is out of range -- the guard must reject BEFORE
                // getCompressedSize touches input[addr+3].

                using IImage img = ImageTSAEditorCore.TryRenderMainImage(
                    rom, 1, 1, IMAGE_OFFSET, false, true /*isLZ77TSA*/, (uint)addr, PALETTE_OFFSET,
                    image2Addr: 0u, isCompressedPalette: false);
                Assert.Null(img);
            });
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

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
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>2 tiles: tile 0 = all index 0, tile 1 = mostly index 1 with a
        /// single index-2 marker pixel at (0,0).</summary>
        static byte[] MarkerTiles()
        {
            byte[] tiles = new byte[2 * 32];
            FillTile(tiles, 1, 1);
            SetPixel(tiles, 1, 0, 0, 2);
            return tiles;
        }

        /// <summary>Exactly 1 tile: index-1 fill with an index-2 marker at (0,0)
        /// (the same shape as MarkerTiles()' tile 1, but as the sole tile so the
        /// chip-list tileCount is 1).</summary>
        static byte[] SingleMarkerTile()
        {
            byte[] tiles = new byte[1 * 32];
            FillTile(tiles, 0, 1);
            SetPixel(tiles, 0, 0, 0, 2);
            return tiles;
        }

        static byte[] StandardPalette()
        {
            byte[] pal = new byte[512];
            SetColor(pal, 0, 1, RED);
            SetColor(pal, 0, 2, GREEN);
            SetColor(pal, 1, 1, BLUE);
            SetColor(pal, 1, 2, WHITE);
            return pal;
        }

        static void SetColor(byte[] pal, int bank, int index, ushort c)
        {
            int off = bank * 32 + index * 2;
            pal[off] = (byte)(c & 0xFF);
            pal[off + 1] = (byte)(c >> 8);
        }

        static void FillTile(byte[] tiles, int tile, int colorIndex)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    SetPixel(tiles, tile, x, y, colorIndex);
        }

        static void SetPixel(byte[] tiles, int tile, int x, int y, int colorIndex)
        {
            int pos = tile * 32 + y * 4 + x / 2;
            byte b = tiles[pos];
            if (x % 2 == 0) b = (byte)((b & 0xF0) | (colorIndex & 0x0F));
            else b = (byte)((b & 0x0F) | ((colorIndex & 0x0F) << 4));
            tiles[pos] = b;
        }

        static ushort Cell(int tileIndex, bool h, bool v, int bank)
            => (ushort)((tileIndex & 0x3FF) | (h ? 0x400 : 0) | (v ? 0x800 : 0) | ((bank & 0xF) << 12));

        static byte[] CellsToBytes(ushort[] cells)
        {
            byte[] b = new byte[cells.Length * 2];
            for (int i = 0; i < cells.Length; i++)
            {
                b[i * 2] = (byte)(cells[i] & 0xFF);
                b[i * 2 + 1] = (byte)(cells[i] >> 8);
            }
            return b;
        }

        static void PlantImage(ROM rom, byte[] tiles)
            => PlantBytes(rom, IMAGE_OFFSET, LZ77.compress(tiles));

        static void PlantPalette(ROM rom, byte[] palette)
            => PlantBytes(rom, PALETTE_OFFSET, palette);

        static void PlantRawCells(ROM rom, uint addr, ushort[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
                U.write_u16(rom.Data, addr + (uint)(i * 2), cells[i]);
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

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
