// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBattleScreenCore.WritePerImageStrip +
// TryLoadRawPalettePublic (issue #872: per-image Import/Export).
//
// Pipeline under test (mirrors the Avalonia ImageImport_Click path):
//   indexedPixels -> EncodeDirectTiles4bpp -> LZ77.compress
//   -> free-space write + pointer update (WriteCompressedToROM)
//
// Round-trip proof: import tiles, then decompress at the new pointer and
// compare raw 4bpp bytes. Export proof: RenderSingleImagePreview returns
// a non-null IImage with the correct pixel color after import.
// Rollback: RunUndo() restores the original pointer + ROM bytes.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageBattleScreenPerImageImportTests
    {
        // Storage offsets for the planted battle-screen sources.
        const uint PALETTE_OFFSET = 0x105000;
        const uint IMAGE1_OFFSET  = 0x110000;
        const uint IMAGE2_OFFSET  = 0x114000;
        const uint IMAGE3_OFFSET  = 0x118000;
        const uint IMAGE4_OFFSET  = 0x11C000;
        const uint IMAGE5_OFFSET  = 0x120000;

        // Free-space region planted at 0x800000 for WriteCompressedToROM to find.
        const uint FREE_SPACE_OFFSET = 0x800000;

        // ---------------------------------------------------------------------------
        // TryLoadRawPalettePublic
        // ---------------------------------------------------------------------------

        [Fact]
        public void TryLoadRawPalettePublic_ValidRom_ReturnsTrueAndPaletteBytes()
        {
            ROM rom = MakeRom();
            bool ok = ImageBattleScreenCore.TryLoadRawPalettePublic(rom, out byte[] pal);
            Assert.True(ok);
            Assert.NotNull(pal);
            Assert.Equal(512, pal.Length); // 16 banks x 16 colors x 2 bytes
        }

        [Fact]
        public void TryLoadRawPalettePublic_NullRom_ReturnsFalse()
        {
            bool ok = ImageBattleScreenCore.TryLoadRawPalettePublic(null, out byte[] pal);
            Assert.False(ok);
            Assert.Null(pal);
        }

        [Fact]
        public void TryLoadRawPalettePublic_CorruptPalettePointer_ReturnsFalse()
        {
            ROM rom = MakeRom();
            // Point palette at an out-of-bounds offset.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(0x1FFFFFF0));
            bool ok = ImageBattleScreenCore.TryLoadRawPalettePublic(rom, out byte[] pal);
            Assert.False(ok);
            Assert.Null(pal);
        }

        // ---------------------------------------------------------------------------
        // WritePerImageStrip — argument validation
        // ---------------------------------------------------------------------------

        [Fact]
        public void WritePerImageStrip_NullRom_ReturnsFalse()
        {
            byte[] pixels = new byte[8 * 8]; // 8x8 = 1 tile
            bool ok = ImageBattleScreenCore.WritePerImageStrip(null, 0, pixels, 8, 8);
            Assert.False(ok);
        }

        [Fact]
        public void WritePerImageStrip_NullPixels_ReturnsFalse()
        {
            ROM rom = MakeRom();
            bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 0, null, 8, 8);
            Assert.False(ok);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(100)]
        public void WritePerImageStrip_BadIndex_ReturnsFalse(int badIndex)
        {
            ROM rom = MakeRom();
            byte[] pixels = new byte[8 * 8];
            bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, badIndex, pixels, 8, 8);
            Assert.False(ok);
        }

        [Fact]
        public void WritePerImageStrip_DimensionNotMultipleOf8_ReturnsFalse()
        {
            ROM rom = MakeRom();
            byte[] pixels = new byte[7 * 8]; // width 7 is not a multiple of 8
            bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 0, pixels, 7, 8);
            Assert.False(ok);
        }

        [Fact]
        public void WritePerImageStrip_PixelArrayWrongSize_ReturnsFalse()
        {
            ROM rom = MakeRom();
            // Width x Height = 16x8 = 128 but we pass only 64 bytes.
            byte[] pixels = new byte[64];
            bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 0, pixels, 16, 8);
            Assert.False(ok);
        }

        // ---------------------------------------------------------------------------
        // WritePerImageStrip — round-trip: import -> decompress at new pointer ->
        // compare raw 4bpp bytes (representative slots: image2 and image4).
        // ---------------------------------------------------------------------------

        [Fact]
        public void WritePerImageStrip_Image2_RoundTrip_DecompressedBytesMatch()
        {
            ROM rom = MakeRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                // image2 is liner-width x 8px. Use 32x8 = 4 tiles.
                int w = 32, h = 8;
                byte[] indexedPixels = MakeSolidIndexedPixels(w, h, palIndex: 3); // red

                Undo.UndoData undoData = CoreState.Undo.NewUndoData("test import image2");
                using (ROM.BeginUndoScope(undoData))
                {
                    bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 1, indexedPixels, w, h);
                    Assert.True(ok, "WritePerImageStrip(image2) should succeed");
                }
                if (undoData.list.Count > 0) CoreState.Undo.Push(undoData);

                // Verify: decompress at the NEW pointer for image2 slot.
                uint newAddr = rom.p32(rom.RomInfo.battle_screen_image2_pointer);
                Assert.NotEqual(IMAGE2_OFFSET, newAddr); // pointer was repointed
                byte[] decompressed = LZ77.decompress(rom.Data, newAddr);
                Assert.NotNull(decompressed);

                // Encode the same pixels ourselves and compare.
                byte[] expected = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, w, h);
                Assert.NotNull(expected);
                Assert.Equal(expected.Length, decompressed.Length);
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], decompressed[i]);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        [Fact]
        public void WritePerImageStrip_Image4_RoundTrip_DecompressedBytesMatch()
        {
            ROM rom = MakeRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                // image4 is liner-width x 8px. Use 16x8 = 2 tiles.
                int w = 16, h = 8;
                byte[] indexedPixels = MakeSolidIndexedPixels(w, h, palIndex: 5); // green

                Undo.UndoData undoData = CoreState.Undo.NewUndoData("test import image4");
                using (ROM.BeginUndoScope(undoData))
                {
                    bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 3, indexedPixels, w, h);
                    Assert.True(ok, "WritePerImageStrip(image4) should succeed");
                }
                if (undoData.list.Count > 0) CoreState.Undo.Push(undoData);

                uint newAddr = rom.p32(rom.RomInfo.battle_screen_image4_pointer);
                Assert.NotEqual(IMAGE4_OFFSET, newAddr);
                byte[] decompressed = LZ77.decompress(rom.Data, newAddr);
                Assert.NotNull(decompressed);

                byte[] expected = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, w, h);
                Assert.Equal(expected.Length, decompressed.Length);
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], decompressed[i]);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        // ---------------------------------------------------------------------------
        // WritePerImageStrip — export after import: RenderSingleImagePreview returns
        // valid PNG-magic-equivalent (the IImage has non-zero pixel data and the
        // correct per-pixel color from the imported tiles).
        // ---------------------------------------------------------------------------

        [Fact]
        public void WritePerImageStrip_Image2_ExportAfterImport_RendersCorrectColor()
        {
            using var _ = EnsureImageService();
            ROM rom = MakeRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                // Import: solid-red (index 3) 32x8 strip into image2 slot.
                int w = 32, h = 8;
                byte[] indexedPixels = MakeSolidIndexedPixels(w, h, palIndex: 3); // red

                Undo.UndoData undoData = CoreState.Undo.NewUndoData("test export after import image2");
                bool ok;
                using (ROM.BeginUndoScope(undoData))
                {
                    ok = ImageBattleScreenCore.WritePerImageStrip(rom, 1, indexedPixels, w, h);
                }
                if (undoData.list.Count > 0) CoreState.Undo.Push(undoData);
                Assert.True(ok);

                // Export: render the strip and check the pixel color.
                IImage img = ImageBattleScreenCore.RenderSingleImagePreview(rom, 1);
                Assert.NotNull(img);
                // palette bank 0 index 3 = GBA 0x001F = BGR15(R=31,G=0,B=0)
                // -> RGB(248, 0, 0) opaque. GBA BGR15: bits0-4=R, bits5-9=G, bits10-14=B.
                AssertPixel(img, 0, 0, 248, 0, 0, 255);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        // ---------------------------------------------------------------------------
        // WritePerImageStrip — rollback restores original pointer + ROM bytes
        // (per the #871 lesson: a failed write leaves no residue).
        // ---------------------------------------------------------------------------

        [Fact]
        public void WritePerImageStrip_Rollback_RestoresOriginalPointerAndBytes()
        {
            ROM rom = MakeRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                // Snapshot the original image3 pointer.
                uint originalAddr = rom.p32(rom.RomInfo.battle_screen_image3_pointer);
                // Snapshot 16 bytes of ROM at the original location.
                byte[] before = new byte[16];
                Array.Copy(rom.Data, originalAddr, before, 0, 16);

                int w = 16, h = 8;
                byte[] indexedPixels = MakeSolidIndexedPixels(w, h, palIndex: 5);

                // Write, then roll back via Undo.RunUndo().
                Undo.UndoData undoData = CoreState.Undo.NewUndoData("test rollback image3");
                using (ROM.BeginUndoScope(undoData))
                {
                    bool ok = ImageBattleScreenCore.WritePerImageStrip(rom, 2, indexedPixels, w, h);
                    Assert.True(ok); // write succeeded before rollback
                }
                if (undoData.list.Count > 0)
                {
                    CoreState.Undo.Push(undoData);
                    CoreState.Undo.RunUndo(); // equivalent to UndoService.Rollback()
                }

                // Pointer must be restored to its original value.
                uint afterRollback = rom.p32(rom.RomInfo.battle_screen_image3_pointer);
                Assert.Equal(originalAddr, afterRollback);

                // ROM bytes at the original pointer must be unchanged.
                for (int i = 0; i < 16; i++)
                {
                    Assert.Equal(before[i], rom.Data[originalAddr + i]);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        // ---------------------------------------------------------------------------
        // Parity: the 10 per-image buttons are wired (not stubs) in the AXAML.
        // Static AXAML scan — no runtime dependency.
        // ---------------------------------------------------------------------------

        [Fact]
        public void AxamlView_AllPerImageButtons_AreWiredAndEnabled()
        {
            string axaml = ReadAxaml();

            for (int i = 1; i <= 5; i++)
            {
                // Import button must have a Click handler (not be a stub).
                Assert.Matches(
                    new System.Text.RegularExpressions.Regex(
                        $@"AutomationId=""ImageBattleScreen_Image{i}_Import_Button""[\s\S]{{0,200}}Click=""Image{i}Import_Click"""),
                    axaml);
                // Export button must have a Click handler (not be a stub).
                Assert.Matches(
                    new System.Text.RegularExpressions.Regex(
                        $@"AutomationId=""ImageBattleScreen_Image{i}_Export_Button""[\s\S]{{0,200}}Click=""Image{i}Export_Click"""),
                    axaml);
                // The KnownGap stub IsEnabled=False must be gone.
                Assert.DoesNotMatch(
                    new System.Text.RegularExpressions.Regex(
                        $@"AutomationId=""ImageBattleScreen_Image{i}_Import_Button""[\s\S]{{0,400}}IsEnabled=""False"""),
                    axaml);
                Assert.DoesNotMatch(
                    new System.Text.RegularExpressions.Regex(
                        $@"AutomationId=""ImageBattleScreen_Image{i}_Export_Button""[\s\S]{{0,400}}IsEnabled=""False"""),
                    axaml);
            }
        }

        [Fact]
        public void CodeBehind_ImageImport_Click_WrapsInUndoService()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // The shared ImageImport_Click handler (internal, injectable) must
            // wrap WritePerImageStrip in _undoService.Begin / Commit / Rollback.
            Assert.Matches(new System.Text.RegularExpressions.Regex(
                @"_undoService\.Begin\([^)]*\)[\s\S]*?WritePerImageStrip[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
                System.Text.RegularExpressions.RegexOptions.Singleline), code);
        }

        [Fact]
        public void CodeBehind_ImageImport_Click_RollsBackOnWriteFailure()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // The handler must call Rollback on the failure path.
            Assert.Matches(new System.Text.RegularExpressions.Regex(
                @"_undoService\.Rollback\(\)",
                System.Text.RegularExpressions.RegexOptions.Singleline), code);
        }

        [Fact]
        public void CodeBehind_ImageImport_Click_RestoresVmPointersOnFailure()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // The handler must call RestoreVmPointers on failure paths.
            Assert.Matches(new System.Text.RegularExpressions.Regex(
                @"RestoreVmPointers\(",
                System.Text.RegularExpressions.RegexOptions.Singleline), code);
        }

        // ---------------------------------------------------------------------------
        // FIX 1 (#874 review): ImageExport_Click must be async + awaited so
        // File.Create / bitmap.Save exceptions are never swallowed.
        // ---------------------------------------------------------------------------

        [Fact]
        public void CodeBehind_ImageExport_Click_IsAsyncAndAwaited()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // The shared ImageExport_Click helper (internal Task) must be async.
            Assert.Matches(new System.Text.RegularExpressions.Regex(
                @"async\s+.*?Task\s+ImageExport_Click\(",
                System.Text.RegularExpressions.RegexOptions.Singleline), code);
        }

        [Fact]
        public void CodeBehind_ImageExportWrappers_AwaitImageExport_Click()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // All five thin wrappers (Image1..Image5Export_Click) must await
            // ImageExport_Click rather than fire-and-forget.
            for (int i = 1; i <= 5; i++)
            {
                Assert.Matches(new System.Text.RegularExpressions.Regex(
                    $@"async\s+void\s+Image{i}Export_Click[\s\S]{{0,200}}await\s+ImageExport_Click\(",
                    System.Text.RegularExpressions.RegexOptions.Singleline), code);
            }
        }

        [Fact]
        public void CodeBehind_ImageExport_Click_HasTryCatch()
        {
            string code = System.IO.File.ReadAllText(CodeBehindPath());
            // The export handler must wrap ExportPng in a try/catch that logs
            // failures (mirrors BattleExportPng_Click error-handling pattern).
            Assert.Matches(new System.Text.RegularExpressions.Regex(
                @"await\s+previewControl\.ExportPng[\s\S]{0,200}catch\s*\(\s*Exception",
                System.Text.RegularExpressions.RegexOptions.Singleline), code);
        }

        // ---------------------------------------------------------------------------
        // FIX 2 (#874 review): WritePerImageStrip with an unsafe pointerSlot
        // (zero / out-of-bounds) must return false and NOT throw or corrupt.
        //
        // Strategy: use a ROMFE0 ("NAZO") ROM whose battle_screen_image1_pointer
        // defaults to 0 (uninitialized). isSafetyOffset(0, rom) is false because
        // 0 < 0x200, so IsRegionSafe(rom, 0, 4) returns false — the guard fires
        // before any data is encoded or written.
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Build a NAZO ROM (ROMFE0) whose pointer properties are zero-initialized
        /// so the image pointer slots are 0, which is below the isSafetyOffset
        /// lower-bound (0x200) and therefore rejected by IsRegionSafe.
        /// </summary>
        static ROM MakeUnsafeSlotRom()
        {
            var rom = new ROM();
            // NAZO version requires any size (no minimum). Use 1 MB.
            byte[] data = new byte[0x100000];
            // Fill with 0xFF so there are no false free-space hits.
            Array.Fill(data, (byte)0xFF);
            rom.LoadLow("nazo.gba", data, "NAZO");
            // ROMFE0.battle_screen_image1_pointer == 0 (default for unset uint).
            return rom;
        }

        [Fact]
        public void WritePerImageStrip_UnsafePointerSlot_ReturnsFalseNoThrow()
        {
            // The NAZO ROM has battle_screen_image1_pointer == 0, which fails
            // IsRegionSafe (0 < 0x200). WritePerImageStrip must return false
            // without throwing and without calling WriteCompressedToROM.
            ROM rom = MakeUnsafeSlotRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                int w = 8, h = 8;
                byte[] pixels = MakeSolidIndexedPixels(w, h, palIndex: 1);

                bool result = true; // set to true so we can detect it going false
                var ex = Record.Exception(() =>
                {
                    Undo.UndoData ud = CoreState.Undo.NewUndoData("test unsafe slot");
                    using (ROM.BeginUndoScope(ud))
                    {
                        result = ImageBattleScreenCore.WritePerImageStrip(rom, 0, pixels, w, h);
                    }
                });
                Assert.Null(ex);
                Assert.False(result);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        [Fact]
        public void WritePerImageStrip_UnsafePointerSlot_DoesNotCorruptROM()
        {
            // Same NAZO ROM. Verifies that NO ROM bytes change when the guard
            // fires (early return before EncodeDirectTiles4bpp means
            // WriteCompressedToROM is never reached).
            ROM rom = MakeUnsafeSlotRom();
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                // Snapshot ALL bytes.
                byte[] before = (byte[])rom.Data.Clone();

                int w = 8, h = 8;
                byte[] pixels = MakeSolidIndexedPixels(w, h, palIndex: 2);

                Undo.UndoData ud = CoreState.Undo.NewUndoData("test corrupt guard");
                using (ROM.BeginUndoScope(ud))
                {
                    ImageBattleScreenCore.WritePerImageStrip(rom, 0, pixels, w, h);
                }

                // Every byte must be byte-for-byte identical — no partial write.
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        static System.IDisposable EnsureImageService() => new ImageServiceScope();

        sealed class ImageServiceScope : System.IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
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

        /// <summary>Build a flat indexed-pixel array filled with a single palette index.</summary>
        static byte[] MakeSolidIndexedPixels(int width, int height, int palIndex)
        {
            byte[] pixels = new byte[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(palIndex & 0x0f);
            return pixels;
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // Fill a large free-space region (0x800000+) with 0x00 so
            // FindFreeSpace can locate it for WriteCompressedToROM.
            for (uint i = 0; i < 0x100000; i++) rom.Data[FREE_SPACE_OFFSET + i] = 0x00;

            // Palette: 16 banks x 16 colors x 2 bytes = 512 bytes.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(PALETTE_OFFSET));
            for (uint i = 0; i < 512; i++) rom.Data[PALETTE_OFFSET + i] = 0;
            // bank 0 index 0 = white (0x7FFF), index 3 = red (0x001F), index 5 = green (0x03E0).
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 0) * 2, 0x7FFF);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 3) * 2, 0x001F);
            U.write_u16(rom.Data, PALETTE_OFFSET + (0 * 16 + 5) * 2, 0x03E0);

            // image1: 33 green tiles -> natural (88, 24) = 11x3.
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image1_pointer, IMAGE1_OFFSET, 33, _ => 5);

            // image2: 4 green tiles -> liner (32, 8).
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image2_pointer, IMAGE2_OFFSET, 4, _ => 5);

            // image3: 16 green tiles -> liner (128, 8).
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image3_pointer, IMAGE3_OFFSET, 16, _ => 5);

            // image4: 1 green tile -> liner (8, 8).
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image4_pointer, IMAGE4_OFFSET, 1, _ => 5);

            // image5: 8 green tiles -> liner (64, 8).
            PlantImageStripTiles(rom, rom.RomInfo.battle_screen_image5_pointer, IMAGE5_OFFSET, 8, _ => 5);

            return rom;
        }

        static void PlantImageStripTiles(ROM rom, uint slot, uint offset, int tileCount, Func<int, int> colorOf)
        {
            byte[] raw = new byte[tileCount * 32];
            for (int t = 0; t < tileCount; t++)
            {
                int idx = colorOf(t) & 0x0f;
                byte packed = (byte)((idx << 4) | idx);
                int baseOff = t * 32;
                for (int i = 0; i < 32; i++) raw[baseOff + i] = packed;
            }
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
            U.write_u32(rom.Data, slot, U.toPointer(offset));
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null)
                throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }

        static string AxamlPath()
            => System.IO.Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "ImageBattleScreenView.axaml");

        static string CodeBehindPath()
            => System.IO.Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "ImageBattleScreenView.axaml.cs");

        static string ReadAxaml() => System.IO.File.ReadAllText(AxamlPath());
    }
}
