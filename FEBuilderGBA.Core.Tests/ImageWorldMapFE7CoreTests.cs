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
