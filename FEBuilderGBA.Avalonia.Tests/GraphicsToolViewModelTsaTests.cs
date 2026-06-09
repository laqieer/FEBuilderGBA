// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Graphics Tool TSA-composited preview (#1030). Verifies that
// GraphicsToolViewViewModel.DrawTiles routes the preview through the EXISTING
// ImageTSAEditorCore.TryRenderMainImage when a TSA type other than None is
// selected, that the TSA-type -> (isLZ77TSA, isHeaderTSA) mapping matches the
// shared MapTsaType helper, that TsaModeActive toggles, and that the header-TSA
// min-dimension clamp (>=32x20) mirrors ImageTSAEditorViewModel.Init.
//
// Plants a synthetic FE8U ROM with a 4bpp LZ77 tile sheet + a NON-header raw
// LZ77 TSA + a raw palette, reusing the ImageTSAEditorCellTests fixture pattern.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class GraphicsToolViewModelTsaTests
    {
        // Resolved ROM data offsets for the planted streams.
        const uint IMAGE_OFFSET    = 0x1000;
        const uint IMAGE2_OFFSET   = 0x2000;
        const uint TSA_LZ_OFFSET   = 0x6000;
        const uint PALETTE_OFFSET  = 0x8000;
        const uint COMP_PAL_OFFSET = 0xA000;

        // GBA 5-5-5 colors used in the planted palette.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;

        // -----------------------------------------------------------------
        // MapTsaType — index -> (isLZ77TSA, isHeaderTSA) flag pair.
        // The index space IS the WF TSAOption space the TSA Editor button uses.
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0, false, false)] // None  -> raw (plain path skips it anyway)
        [InlineData(1, true, false)]  // Compressed (LZ77)
        [InlineData(2, true, true)]   // Compressed Header
        [InlineData(3, false, true)]  // Raw Header
        [InlineData(4, false, false)] // Raw
        public void MapTsaType_ReturnsCorrectFlags(int index, bool expLz77, bool expHeader)
        {
            var (isLZ77TSA, isHeaderTSA) = GraphicsToolViewViewModel.MapTsaType(index);
            Assert.Equal(expLz77, isLZ77TSA);
            Assert.Equal(expHeader, isHeaderTSA);
        }

        // -----------------------------------------------------------------
        // TsaModeActive toggles with TsaTypeIndex.
        // -----------------------------------------------------------------

        [Fact]
        public void TsaModeActive_TogglesWithIndex()
        {
            var vm = new GraphicsToolViewViewModel();
            Assert.False(vm.TsaModeActive);          // default index 0
            vm.TsaTypeIndex = 1;
            Assert.True(vm.TsaModeActive);
            vm.TsaTypeIndex = 0;
            Assert.False(vm.TsaModeActive);
        }

        // -----------------------------------------------------------------
        // DrawTiles with TsaTypeIndex > 0 renders a NON-NULL TSA-composited
        // image, and is produced via the TSA path (ImageInfo carries the
        // "TSA" tag, distinct from the plain-tile ImageInfo).
        // -----------------------------------------------------------------

        [Fact]
        public void DrawTiles_TsaMode_RendersNonNullCompositedImage()
        {
            WithRomAndService(rom =>
            {
                // Plant a 2-tile image (tile 0 blank, tile 1 green marker), a
                // LZ77 NON-header TSA (2 cells = 4 bytes — the GBA LZ77 stream
                // needs >= 4 uncompressed bytes to validate), and a palette.
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                ushort[] cells = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(cells)));

                var vm = new GraphicsToolViewViewModel();
                vm.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                vm.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                vm.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                vm.TileCountX = 2;   // 2x1 grid = 2 cells
                vm.TileCountY = 1;
                vm.TsaTypeIndex = 1; // Compressed (LZ77), non-header

                var image = vm.DrawTiles();

                Assert.NotNull(image);
                Assert.Equal(16, image!.Width);  // 2 tiles * 8px
                Assert.Equal(8, image.Height);
                Assert.Contains("TSA", vm.ImageInfo);
            });
        }

        // -----------------------------------------------------------------
        // The plain path (TsaTypeIndex == 0) produces a DIFFERENT result than
        // the TSA path (the TSA composite selects tile 1; the plain path with
        // the SAME image address renders tile 0 first). Proves the TSA branch
        // is actually consumed.
        // -----------------------------------------------------------------

        [Fact]
        public void DrawTiles_TsaMode_DiffersFromPlainPath()
        {
            WithRomAndService(rom =>
            {
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                // 2-cell LZ77 TSA (>= 4 bytes): cell 0 -> tile 1 (green marker),
                // cell 1 -> tile 0 (blank).
                ushort[] cells = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(cells)));

                // Plain path: 2x1 tiles, 4bpp, LZ77 image -> renders tiles 0,1
                // in tilesheet order (tile 0 blank first).
                var plain = new GraphicsToolViewViewModel();
                plain.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                plain.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                plain.TileCountX = 2;
                plain.TileCountY = 1;
                plain.Is4bpp = true;
                plain.IsCompressed = true;
                plain.TsaTypeIndex = 0; // plain path
                var plainImg = plain.DrawTiles();
                Assert.NotNull(plainImg);

                // TSA path: same image, but the TSA composite places tile 1
                // (green marker) FIRST -> different pixel layout than the plain
                // tilesheet order.
                var tsa = new GraphicsToolViewViewModel();
                tsa.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                tsa.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                tsa.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                tsa.TileCountX = 2;
                tsa.TileCountY = 1;
                tsa.TsaTypeIndex = 1;
                var tsaImg = tsa.DrawTiles();
                Assert.NotNull(tsaImg);

                // The two renders differ: plain tilesheet order (0,1) vs TSA
                // composite order (1,0).
                Assert.NotEqual(plainImg!.GetPixelData(), tsaImg!.GetPixelData());
            });
        }

        // -----------------------------------------------------------------
        // A TSA address of 0 (or empty) falls through to the plain path even
        // when a TSA type is selected — guards against a null-pointer read.
        // -----------------------------------------------------------------

        [Fact]
        public void DrawTiles_TsaTypeSet_ButZeroTsaAddress_UsesPlainPath()
        {
            WithRomAndService(rom =>
            {
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                var vm = new GraphicsToolViewViewModel();
                vm.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                vm.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                vm.TsaAddressText = ""; // no TSA address
                vm.TileCountX = 1;
                vm.TileCountY = 1;
                vm.Is4bpp = true;
                vm.IsCompressed = true;
                vm.TsaTypeIndex = 1; // type set, but no address -> plain path

                var image = vm.DrawTiles();
                Assert.NotNull(image);
                // Plain path ImageInfo carries "bpp", not the TSA tag.
                Assert.DoesNotContain("TSA", vm.ImageInfo);
            });
        }

        // -----------------------------------------------------------------
        // Header-TSA clamp: when isHeaderTSA, w8/h8 are raised to >=32/20 (the
        // ImageTSAEditorViewModel.Init clamp). We verify via the rendered image
        // dimensions: a 1x1 request with a header TSA produces a >=256x160 px
        // image (32*8 x 20*8). Plant a raw-header TSA so the header path runs.
        // -----------------------------------------------------------------

        [Fact]
        public void DrawTiles_HeaderTsa_ClampsDimensionsTo32x20()
        {
            WithRomAndService(rom =>
            {
                // A 32x20 tile sheet is needed so the clamped canvas has tiles to
                // index. Plant a 640-tile (32*20) image; all blank is fine.
                byte[] tiles = new byte[32 * 20 * 32]; // 640 blank 4bpp tiles
                PlantBytes(rom, IMAGE_OFFSET, LZ77.compress(tiles));
                PlantPalette(rom, StandardPalette());

                // Raw header TSA: 2 header bytes {w8=32, h8=20} then 32*20 zero cells.
                byte[] headerTsa = new byte[2 + 32 * 20 * 2];
                headerTsa[0] = 32;
                headerTsa[1] = 20;
                PlantBytes(rom, TSA_LZ_OFFSET, headerTsa);

                var vm = new GraphicsToolViewViewModel();
                vm.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                vm.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                vm.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                vm.TileCountX = 1; // intentionally tiny -> must be clamped
                vm.TileCountY = 1;
                vm.TsaTypeIndex = 3; // Raw Header

                var image = vm.DrawTiles();

                Assert.NotNull(image);
                // Clamped to 32x20 tiles = 256x160 px.
                Assert.Equal(256, image!.Width);
                Assert.Equal(160, image.Height);
                Assert.Contains("32x20", vm.ImageInfo);
            });
        }

        // -----------------------------------------------------------------
        // #1074 — image2-join + compressed paletteType behavioral tests.
        // Setting Image2AddressText/IsImage2Join/IsCompressedPalette on the VM
        // and calling DrawTiles must reflect the image2 tile / compressed-palette
        // colors in the rendered CurrentImage.
        // -----------------------------------------------------------------

        /// <summary>image2 = a single tile that is entirely color index 2 (green
        /// in bank 0). Joined after MarkerTiles()'s 2 tiles -> tile index 2.</summary>
        static byte[] Image2GreenTile()
        {
            byte[] tiles = new byte[1 * 32];
            FillTile(tiles, 0, 2);
            return tiles;
        }

        [Fact]
        public void DrawTiles_Image2Join_RendersSecondImageTile()
        {
            WithRomAndService(rom =>
            {
                PlantImage(rom, MarkerTiles());                  // tiles 0,1
                PlantBytes(rom, IMAGE2_OFFSET, LZ77.compress(Image2GreenTile())); // tile 2
                PlantPalette(rom, StandardPalette());
                // TSA cell references tile index 2 -> only reachable via image2.
                ushort[] cells = { Cell(2, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(
                    new ushort[] { cells[0], Cell(0, false, false, 0) }))); // pad to >=4 bytes

                var vm = new GraphicsToolViewViewModel();
                vm.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                vm.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                vm.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                vm.Image2AddressText  = "0x" + U.toPointer(IMAGE2_OFFSET).ToString("X8");
                vm.IsImage2Join = true;
                vm.TileCountX = 2;   // 2 cells
                vm.TileCountY = 1;
                vm.TsaTypeIndex = 1; // Compressed (LZ77), non-header

                var image = vm.DrawTiles();
                Assert.NotNull(image);
                // tile 2 (image2) is all green -> first cell (0,0) is green.
                AssertPixel(image!, 0, 0, 0, 248, 0, 255);
                Assert.Contains("+image2", vm.ImageInfo);

                // WITHOUT the join, the SAME tile index 2 is out of range -> that
                // cell is blank (transparent). Prove the renders differ.
                var noJoin = new GraphicsToolViewViewModel();
                noJoin.ImageAddressText   = vm.ImageAddressText;
                noJoin.PaletteAddressText = vm.PaletteAddressText;
                noJoin.TsaAddressText     = vm.TsaAddressText;
                noJoin.IsImage2Join = false; // join disabled
                noJoin.TileCountX = 2;
                noJoin.TileCountY = 1;
                noJoin.TsaTypeIndex = 1;
                var noJoinImg = noJoin.DrawTiles();
                Assert.NotNull(noJoinImg);
                Assert.NotEqual(image!.GetPixelData(), noJoinImg!.GetPixelData());
            });
        }

        [Fact]
        public void DrawTiles_CompressedPalette_MatchesRawPaletteRender()
        {
            WithRomAndService(rom =>
            {
                PlantImage(rom, MarkerTiles());
                ushort[] cells = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(cells)));

                byte[] rawPal = StandardPalette();
                PlantPalette(rom, rawPal);                                  // raw
                PlantBytes(rom, COMP_PAL_OFFSET, LZ77.compress(rawPal));    // compressed

                // Raw-palette render.
                var raw = new GraphicsToolViewViewModel();
                raw.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                raw.PaletteAddressText = "0x" + U.toPointer(PALETTE_OFFSET).ToString("X8");
                raw.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                raw.TileCountX = 2; raw.TileCountY = 1; raw.TsaTypeIndex = 1;
                raw.IsCompressedPalette = false;
                var rawImg = raw.DrawTiles();
                Assert.NotNull(rawImg);

                // Compressed-palette render of the SAME colors.
                var comp = new GraphicsToolViewViewModel();
                comp.ImageAddressText   = "0x" + U.toPointer(IMAGE_OFFSET).ToString("X8");
                comp.PaletteAddressText = "0x" + U.toPointer(COMP_PAL_OFFSET).ToString("X8");
                comp.TsaAddressText     = "0x" + U.toPointer(TSA_LZ_OFFSET).ToString("X8");
                comp.TileCountX = 2; comp.TileCountY = 1; comp.TsaTypeIndex = 1;
                comp.IsCompressedPalette = true;
                var compImg = comp.DrawTiles();
                Assert.NotNull(compImg);
                Assert.Contains("LZ77 palette", comp.ImageInfo);

                // Identical colors -> identical pixels.
                Assert.Equal(rawImg!.GetPixelData(), compImg!.GetPixelData());
            });
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

        // -----------------------------------------------------------------
        // Helpers (mirror ImageTSAEditorCellTests.cs)
        // -----------------------------------------------------------------

        static void WithRomAndService(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();
                var rom = MakeRom();
                CoreState.ROM = rom;
                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static byte[] MarkerTiles()
        {
            byte[] tiles = new byte[2 * 32]; // tile 0 blank, tile 1 marker
            FillTile(tiles, 1, 1);
            SetPixel(tiles, 1, 0, 0, 2);
            return tiles;
        }

        static byte[] StandardPalette()
        {
            byte[] pal = new byte[512];
            SetColor(pal, 0, 1, RED);
            SetColor(pal, 0, 2, GREEN);
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

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);
    }
}
