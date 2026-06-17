// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the FE7 World Map Image editor Core helpers (#1184):
//   * TryRenderFE7BigFieldMap — the 12-split (4x3) 256x256 header-TSA big field
//     map composited into 1024x688. FE7-only.
//   * ImportFE7BigFieldMap — the inverse: validate-all-before-mutate, byte-
//     identical fault restore, RAW in-place write at the 24 piece pointers + palette.
//
// Real-ROM tests (FE7U.gba via FindTestRom). [Collection("SharedState")] because
// they set CoreState.ROM / ImageService / Undo. A StubImageService captures RGBA
// pixels so the render produces a non-null buffer and the import round-trips.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapFE7CoreTests
    {
        const int FE7_BIG_WIDTH = 1024;
        const int FE7_BIG_HEIGHT = 688;

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        [Fact]
        public void Render_NullRom_ReturnsNull()
        {
            Assert.Null(ImageWorldMapCore.TryRenderFE7BigFieldMap(null));
        }

        [Fact]
        public void Render_FE7_ReturnsNonNull_1024x688()
        {
            WithRealRom("FE7U.gba", rom =>
            {
                IImage img = ImageWorldMapCore.TryRenderFE7BigFieldMap(rom);
                Assert.NotNull(img);
                Assert.Equal(FE7_BIG_WIDTH, img.Width);
                Assert.Equal(FE7_BIG_HEIGHT, img.Height);
                img.Dispose();
            });
        }

        [Fact]
        public void Render_FE8_ReturnsNull()
        {
            // FE8's worldmap_big_* slots are resolvable (single image + palette-map),
            // but the FE7 12-split decoder must NOT interpret them — the version
            // gate returns null (Copilot #1184 plan review #1).
            WithRealRom("FE8U.gba", rom =>
            {
                Assert.Null(ImageWorldMapCore.TryRenderFE7BigFieldMap(rom));
            });
        }

        // -----------------------------------------------------------------
        // Regression: tile-0 must render (NOT be skipped) for the FE7 big map
        // (Copilot PR #1223 review #1). DecodeHeaderTSA(skipTile0:false) treats
        // a TSA cell of 0 as a VALID tile-0 reference, unlike the sprite default.
        // -----------------------------------------------------------------

        [Fact]
        public void DecodeHeaderTSA_SkipTile0False_RendersTile0()
        {
            var savedSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();

                // 1 tile of 4bpp data: tile 0 = all pixel-index 1 (32 bytes of 0x11).
                byte[] tiles = new byte[32];
                for (int i = 0; i < 32; i++) tiles[i] = 0x11;
                // Palette: index 1 of bank 0 = a bright color (GBA 0x7FFF = white).
                byte[] palette = new byte[16 * 2];
                palette[2] = 0xFF; palette[3] = 0x7F; // color index 1 = 0x7FFF

                // Header-TSA: 1x1 header (mhx=0,mhy=0), one cell = 0x0000 (tile 0).
                byte[] tsa = new byte[] { 0, 0, 0x00, 0x00 };

                // skipTile0:true (sprite default) -> tile 0 cell skipped -> blank.
                IImage blank = ImageUtilCore.DecodeHeaderTSA(
                    tiles, tsa, palette, 1, 1, true, 0, 0, skipTile0: true);
                // skipTile0:false (FE7 big map) -> tile 0 cell rendered -> non-blank.
                IImage rendered = ImageUtilCore.DecodeHeaderTSA(
                    tiles, tsa, palette, 1, 1, true, 0, 0, skipTile0: false);

                Assert.NotNull(blank);
                Assert.NotNull(rendered);
                byte[] blankPx = blank.GetPixelData();
                byte[] renderedPx = rendered.GetPixelData();

                // The blank decode leaves pixel (0,0) at the zero default (alpha 0);
                // the FE7 decode writes the white tile (alpha 255, white RGB).
                Assert.Equal(0, blankPx[3]);          // skipped -> transparent
                Assert.Equal(255, renderedPx[3]);     // rendered -> opaque
                Assert.True(renderedPx[0] > 200 && renderedPx[1] > 200 && renderedPx[2] > 200,
                    "tile 0 must render its (white) color, not blank");

                blank.Dispose();
                rendered.Dispose();
            }
            finally { CoreState.ImageService = savedSvc; }
        }

        // -----------------------------------------------------------------
        // CanImport gate (lightweight — no render)
        // -----------------------------------------------------------------

        [Fact]
        public void CanImport_NullRom_False()
        {
            Assert.False(ImageWorldMapCore.CanImportFE7BigFieldMap(null));
        }

        [Fact]
        public void CanImport_FE7_True()
        {
            WithRealRom("FE7U.gba", rom =>
                Assert.True(ImageWorldMapCore.CanImportFE7BigFieldMap(rom)));
        }

        [Fact]
        public void CanImport_FE8_False()
        {
            WithRealRom("FE8U.gba", rom =>
                Assert.False(ImageWorldMapCore.CanImportFE7BigFieldMap(rom)));
        }

        // CanImportEvent gates on the EVENT pointers, independent of the big-map
        // pointers (Copilot PR #1223 review) — true on FE7 AND FE8, false on null.
        [Fact]
        public void CanImportEvent_NullRom_False()
        {
            Assert.False(ImageWorldMapCore.CanImportEvent(null));
        }

        [Fact]
        public void CanImportEvent_FE7_True()
        {
            WithRealRom("FE7U.gba", rom =>
                Assert.True(ImageWorldMapCore.CanImportEvent(rom)));
        }

        [Fact]
        public void CanImportEvent_FE8_True()
        {
            // The event image import is FE7+FE8 — the EVENT gate is true on FE8
            // even though the FE7 BIG-map gate is false.
            WithRealRom("FE8U.gba", rom =>
            {
                Assert.True(ImageWorldMapCore.CanImportEvent(rom));
                Assert.False(ImageWorldMapCore.CanImportFE7BigFieldMap(rom));
            });
        }

        // -----------------------------------------------------------------
        // Import
        // -----------------------------------------------------------------

        [Fact]
        public void Import_NullRom_ReturnsError()
        {
            string err = ImageWorldMapCore.ImportFE7BigFieldMap(null, new byte[16], 1024, 688);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void Import_FE8_ReturnsError_NoMutation()
        {
            WithRealRom("FE8U.gba", rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                byte[] rgba = new byte[FE7_BIG_WIDTH * FE7_BIG_HEIGHT * 4];
                string err = ImageWorldMapCore.ImportFE7BigFieldMap(rom, rgba, FE7_BIG_WIDTH, FE7_BIG_HEIGHT);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // ZERO mutation
            });
        }

        [Fact]
        public void Import_WrongDims_ReturnsError_NoMutation()
        {
            WithRealRom("FE7U.gba", rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                // 100x100 source (wrong) — must be rejected with no mutation.
                byte[] rgba = new byte[100 * 100 * 4];
                string err = ImageWorldMapCore.ImportFE7BigFieldMap(rom, rgba, 100, 100);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Import_NullImage_ReturnsError_NoMutation()
        {
            WithRealRom("FE7U.gba", rom =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                string err = ImageWorldMapCore.ImportFE7BigFieldMap(rom, null, FE7_BIG_WIDTH, FE7_BIG_HEIGHT);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Import_RoundTrip_FE7_Succeeds_AndMutates()
        {
            WithRealRom("FE7U.gba", rom =>
            {
                // Render -> export the live big field map RGBA, then import it back.
                IImage img = ImageWorldMapCore.TryRenderFE7BigFieldMap(rom);
                Assert.NotNull(img);
                byte[] rgba = img.GetPixelData();
                img.Dispose();
                Assert.True(rgba.Length >= FE7_BIG_WIDTH * FE7_BIG_HEIGHT * 4);

                byte[] before = (byte[])rom.Data.Clone();

                // The import runs under an ambient undo scope (mirrors the View).
                using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("test")))
                {
                    string err = ImageWorldMapCore.ImportFE7BigFieldMap(
                        rom, rgba, FE7_BIG_WIDTH, FE7_BIG_HEIGHT);
                    Assert.Equal("", err); // success
                }

                // The import writes RAW in-place (24 pieces + palette), so the ROM
                // length is unchanged but the data is re-encoded.
                Assert.Equal(before.Length, rom.Data.Length);
            });
        }

        // -----------------------------------------------------------------
        // Helper: load a real ROM, wire CoreState, run the body, restore.
        // -----------------------------------------------------------------

        static void WithRealRom(string romName, Action<ROM> body)
        {
            string romPath = FindTestRom(romName);
            if (romPath == null) return; // skip if the ROM is unavailable

            var savedRom = CoreState.ROM;
            var savedService = CoreState.ImageService;
            var savedUndo = CoreState.Undo;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip on load failure

                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();

                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedService;
                CoreState.Undo = savedUndo;
            }
        }

        /// <summary>Locate a specific test ROM by name, walking up to the solution
        /// dir's roms/ folder. Returns null when unavailable (test skips).</summary>
        static string FindTestRom(string romName)
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = System.IO.Path.Combine(dir, "roms", romName);
                    return System.IO.File.Exists(path) ? path : null;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
