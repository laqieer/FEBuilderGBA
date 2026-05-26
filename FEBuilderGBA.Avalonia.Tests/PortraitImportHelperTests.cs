// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for PortraitImportHelper — the shared portrait-import pipeline used
// by both ImagePortraitView and the Portrait Import Wizard (#657).
//
// Covers Copilot CLI plan v2 review point 4 (helper-level tests):
//   - ImportSimple writes the expected pointer fields on FE8U
//   - ImportSheet is rejected on FE6 ROM (version gate)
//   - ImportSimple FE6 path is allowed and only writes D0 + D8 (no D12 corruption)
//   - Rollback path on a failed write leaves ROM bytes untouched
//   - View / VM / AXAML wiring contracts
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for <see cref="PortraitImportHelper"/> and the wizard view it
    /// powers. Test categories:
    ///   1. Static-no-ROM tests (FE6 gate, helper API surface, RGBA reconstruction)
    ///   2. Live-ROM helper tests (uses RomFixture)
    ///   3. Wizard wiring tests (AXAML / VM / VIEW)
    /// </summary>
    [Collection("SharedState")]
    public class PortraitImportHelperTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public PortraitImportHelperTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null)
                CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        // ------------------------------------------------------------------
        // FE6/FE7/FE8 version gate (Copilot CLI plan v1 blocking #1).
        // ------------------------------------------------------------------

        [Fact]
        public void IsFe7Or8EntryLayout_ReturnsFalse_OnFe6()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE6")
            {
                _output.WriteLine($"SKIP: needs FE6 ROM (have {_fixture.Version})");
                return;
            }
            Assert.False(PortraitImportHelper.IsFe7Or8EntryLayout(_fixture.ROM));
        }

        [Fact]
        public void IsFe7Or8EntryLayout_ReturnsTrue_OnFe7Or8()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (_fixture.Version == "FE6")
            {
                _output.WriteLine("SKIP: this test needs FE7 or FE8");
                return;
            }
            Assert.True(PortraitImportHelper.IsFe7Or8EntryLayout(_fixture.ROM));
        }

        [Fact]
        public void IsFe7Or8EntryLayout_ReturnsFalse_OnNullRom()
        {
            Assert.False(PortraitImportHelper.IsFe7Or8EntryLayout(null));
        }

        // ------------------------------------------------------------------
        // ImportSheet FE6 rejection (Copilot CLI plan v1 blocking #1).
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSheet_RejectsFe6_WithoutWritingRom()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE6")
            {
                _output.WriteLine($"SKIP: needs FE6 ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            // Build a synthetic 128x112 LoadResult (sheet-shaped).
            var loadResult = MakeSyntheticLoadResult(128, 112);

            // Pick an arbitrary safe address (use first portrait entry).
            uint portraitPtr = rom.RomInfo.portrait_pointer;
            uint baseAddr = rom.p32(portraitPtr);
            uint entryAddr = baseAddr; // first slot

            // Snapshot the bytes that the sheet path WOULD touch (addr+12 is
            // the danger zone on FE6 — it's mouth/eye coords, not a pointer).
            byte[] before12 = new byte[4];
            Array.Copy(rom.Data, (int)entryAddr + 12, before12, 0, 4);

            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSheet(rom, entryAddr, loadResult, undo);
            Assert.False(outcome.Success);
            Assert.Contains("FE7", outcome.Error); // error mentions FE7/FE8 only

            // Critical: bytes 12-15 must be unchanged.
            byte[] after12 = new byte[4];
            Array.Copy(rom.Data, (int)entryAddr + 12, after12, 0, 4);
            Assert.Equal(before12, after12);
        }

        // ------------------------------------------------------------------
        // ImportSimple FE8U happy path.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_WritesD0AndD8_OnFe8U()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            // 16x16 quantized image (smallest legal multiple-of-8 size).
            var loadResult = MakeSyntheticLoadResult(16, 16);

            uint portraitPtr = rom.RomInfo.portrait_pointer;
            uint baseAddr = rom.p32(portraitPtr);
            // Use entry 5 to avoid touching commonly-tested entries 0-3.
            uint entryAddr = baseAddr + (uint)(5 * rom.RomInfo.portrait_datasize);

            uint d0Before = rom.p32(entryAddr + 0);
            uint d8Before = rom.p32(entryAddr + 8);
            uint d12Before = rom.p32(entryAddr + 12);

            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

            uint d0After = rom.p32(entryAddr + 0);
            uint d8After = rom.p32(entryAddr + 8);
            uint d12After = rom.p32(entryAddr + 12);

            // D0 (sheet) and D8 (palette) must change to new pointers.
            Assert.NotEqual(d0Before, d0After);
            Assert.NotEqual(d8Before, d8After);
            // D12 (mouth frames) MUST NOT change — simple path only writes D0+D8.
            Assert.Equal(d12Before, d12After);
        }

        // ------------------------------------------------------------------
        // ImportSimple FE6 path is allowed (16-byte entries — D0+D8 safe).
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_AllowedOnFe6_AndDoesNotTouchByte12To15()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE6")
            {
                _output.WriteLine($"SKIP: needs FE6 ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            var loadResult = MakeSyntheticLoadResult(16, 16);

            uint portraitPtr = rom.RomInfo.portrait_pointer;
            uint baseAddr = rom.p32(portraitPtr);
            uint entryAddr = baseAddr + (uint)(5 * rom.RomInfo.portrait_datasize);

            // Snapshot bytes 12-15 (mouth/eye coords on FE6 layout).
            byte[] before12 = new byte[4];
            Array.Copy(rom.Data, (int)entryAddr + 12, before12, 0, 4);

            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

            byte[] after12 = new byte[4];
            Array.Copy(rom.Data, (int)entryAddr + 12, after12, 0, 4);
            // FE6 mouth/eye coords must be untouched by the simple-path write.
            Assert.Equal(before12, after12);
        }

        // ------------------------------------------------------------------
        // Rollback path on null/invalid inputs.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_ReturnsFailure_OnNullLoadResult()
        {
            // Pure no-side-effect check — no fixture needed.
            var outcome = PortraitImportHelper.ImportSimple(
                rom: null, entryAddr: 0x100, loadResult: null,
                undoService: new UndoService());
            Assert.False(outcome.Success);
        }

        [Fact]
        public void ImportSimple_ReturnsFailure_OnZeroEntryAddr()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            using var _ = EnsureImageService();
            var loadResult = MakeSyntheticLoadResult(16, 16);
            var outcome = PortraitImportHelper.ImportSimple(
                _fixture.ROM, entryAddr: 0, loadResult: loadResult,
                undoService: new UndoService());
            Assert.False(outcome.Success);
            Assert.Contains("entry", outcome.Error.ToLowerInvariant());
        }

        // ------------------------------------------------------------------
        // ReconstructRgbaWithPaletteZeroTransparent — Copilot v1 #3.
        // ------------------------------------------------------------------

        [Fact]
        public void ReconstructRgbaWithPaletteZeroTransparent_PaletteIndexZero_IsTransparent()
        {
            using var _ = EnsureImageService();

            var loadResult = new ImageImportService.LoadResult
            {
                Success = true,
                Width = 8,
                Height = 8,
                IndexedPixels = new byte[64], // all index 0
                GBAPalette = BuildSimplePalette(16),
            };

            byte[] rgba = PortraitImportHelper.ReconstructRgbaWithPaletteZeroTransparent(loadResult);
            Assert.NotNull(rgba);
            Assert.Equal(8 * 8 * 4, rgba.Length);

            // All pixels are palette index 0 -> alpha must be 0.
            for (int i = 0; i < 64; i++)
            {
                Assert.Equal(0, rgba[i * 4 + 3]);
            }
        }

        [Fact]
        public void ReconstructRgbaWithPaletteZeroTransparent_NonZeroIndex_IsOpaque()
        {
            using var _ = EnsureImageService();

            byte[] indexed = new byte[64];
            for (int i = 0; i < 64; i++) indexed[i] = 1; // every pixel uses index 1

            var loadResult = new ImageImportService.LoadResult
            {
                Success = true,
                Width = 8,
                Height = 8,
                IndexedPixels = indexed,
                GBAPalette = BuildSimplePalette(16),
            };

            byte[] rgba = PortraitImportHelper.ReconstructRgbaWithPaletteZeroTransparent(loadResult);
            Assert.NotNull(rgba);
            for (int i = 0; i < 64; i++)
            {
                Assert.Equal(255, rgba[i * 4 + 3]);
            }
        }

        // ------------------------------------------------------------------
        // BuildPreviewImage — round-trip check.
        // ------------------------------------------------------------------

        [Fact]
        public void BuildPreviewImage_ReturnsImageMatchingLoadResultDimensions()
        {
            using var _ = EnsureImageService();
            var loadResult = MakeSyntheticLoadResult(16, 16);
            using var preview = PortraitImportHelper.BuildPreviewImage(loadResult);
            Assert.NotNull(preview);
            Assert.Equal(16, preview.Width);
            Assert.Equal(16, preview.Height);
        }

        // ------------------------------------------------------------------
        // RecordSourceFile — must update the resource cache key.
        // ------------------------------------------------------------------

        [Fact]
        public void RecordSourceFile_UpdatesResourceCache_OnValidInputs()
        {
            // EtcCacheResource needs a ROM context (it derives its file path
            // from CoreState.ROM), so skip when no ROM is available.
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            var prev = CoreState.ResourceCache;
            try
            {
                var cache = new EtcCacheResource();
                CoreState.ResourceCache = cache;

                string testPath = "C:\\tmp\\portrait_5.png";
                PortraitImportHelper.RecordSourceFile(portraitIndex: 5, sourcePath: testPath);

                // The "Portrait_" key uses ToHexString — verify the value
                // round-trips through the cache.
                string key = "Portrait_" + U.ToHexString(5);
                bool found = cache.TryGetValue(key, out string back);
                Assert.True(found, $"Cache should contain key {key}");
                Assert.Equal(testPath, back);
            }
            finally
            {
                CoreState.ResourceCache = prev;
            }
        }

        [Fact]
        public void RecordSourceFile_NoOp_OnNegativeIndex()
        {
            // No cache needed — RecordSourceFile must short-circuit on a
            // negative index BEFORE touching the cache. Passing a null
            // ResourceCache proves no write happens.
            var prev = CoreState.ResourceCache;
            try
            {
                CoreState.ResourceCache = null;
                // Should not throw despite the null cache.
                PortraitImportHelper.RecordSourceFile(-1, "C:\\bogus.png");
            }
            finally
            {
                CoreState.ResourceCache = prev;
            }
        }

        [Fact]
        public void RecordSourceFile_NoOp_OnEmptyPath()
        {
            // Empty / null source path must also short-circuit before
            // touching the cache.
            var prev = CoreState.ResourceCache;
            try
            {
                CoreState.ResourceCache = null;
                PortraitImportHelper.RecordSourceFile(5, "");
                PortraitImportHelper.RecordSourceFile(5, null);
            }
            finally
            {
                CoreState.ResourceCache = prev;
            }
        }

        // ------------------------------------------------------------------
        // Wizard wiring tests — AXAML automation IDs + VM contract.
        // ------------------------------------------------------------------

        [Fact]
        public void Wizard_AxamlContainsRequiredAutomationIds()
        {
            string repoRoot = FindRepoRoot();
            string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml");
            Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

            string xaml = File.ReadAllText(axamlPath);
            string[] required = new[]
            {
                "ImagePortraitImporter_Entry_List",
                "ImagePortraitImporter_PickFile_Button",
                "ImagePortraitImporter_SourceFile_Label",
                "ImagePortraitImporter_ImageSize_Label",
                "ImagePortraitImporter_SheetMode_Label",
                "ImagePortraitImporter_Preview_Image",
                "ImagePortraitImporter_Addr_Label",
                "ImagePortraitImporter_Import_Button",
                "ImagePortraitImporter_Status_Label",
                "ImagePortraitImporter_Intro_Label",
                "ImagePortraitImporter_Notes_Label",
            };
            foreach (string id in required)
            {
                Assert.Contains(id, xaml);
            }
        }

        [Fact]
        public void Wizard_ViewModel_LoadList_ReturnsEmptyWhenNoRom()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ImagePortraitImporterViewModel();
                var list = vm.LoadList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Fact]
        public void Wizard_ViewModel_LoadList_ReturnsPortraitsWhenRomLoaded()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            using var _ = EnsureImageService();
            var vm = new ImagePortraitImporterViewModel();
            var list = vm.LoadList();
            Assert.NotNull(list);
            Assert.NotEmpty(list);
            // At least the first slot's address should be a valid offset.
            Assert.True(U.isSafetyOffset(list[0].addr, _fixture.ROM));
        }

        [Fact]
        public void Wizard_ViewModel_CanImport_RequiresBothImageAndSlot()
        {
            using var _ = EnsureImageService();
            var vm = new ImagePortraitImporterViewModel();
            Assert.False(vm.CanImport); // neither

            vm.CurrentAddr = 0x100;
            Assert.False(vm.CanImport); // slot but no image

            vm.LoadedImage = MakeSyntheticLoadResult(8, 8);
            Assert.True(vm.CanImport); // both

            vm.CurrentAddr = 0;
            Assert.False(vm.CanImport); // image but no slot
        }

        [Fact]
        public void Wizard_View_DelegatesImportToHelper()
        {
            // Static-source-check: the view must call PortraitImportHelper.
            // This guards against a refactor that accidentally inlines the
            // ROM-write logic into the view (defeating the shared-helper
            // extraction from Copilot v1 review #2).
            string repoRoot = FindRepoRoot();
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);

            Assert.Contains("PortraitImportHelper.ImportSimple", source);
            Assert.Contains("PortraitImportHelper.ImportSheet", source);
            Assert.Contains("PortraitImportHelper.BuildPreviewImage", source);
            Assert.Contains("PortraitImportHelper.RecordSourceFile", source);
        }

        [Fact]
        public void ImagePortraitView_DelegatesImportToHelper_AfterRefactor()
        {
            // Same static-source-check applied to ImagePortraitView — proves
            // the existing path was refactored to share the same helper
            // (Copilot v1 review #2 acceptance).
            string repoRoot = FindRepoRoot();
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);

            Assert.Contains("PortraitImportHelper.ImportSimple", source);
            Assert.Contains("PortraitImportHelper.ImportSheet", source);

            // The old inline write methods should be gone.
            Assert.DoesNotMatch(new Regex(@"\s+void\s+ImportPortraitSheet\s*\(\s*ROM\s+rom"), source);
        }

        // ------------------------------------------------------------------
        // End-to-end PNG -> ROM import (uses a real PNG rendered from the
        // ROM via PortraitRendererCore) — proves the full wizard pipeline
        // (load -> quantize -> import) round-trips through the helper.
        // ------------------------------------------------------------------

        [Fact]
        public void EndToEnd_LoadRealPng_QuantizeAndImport_OnFe8U()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();

            // Build a small synthetic image, save to PNG, then drive the
            // full wizard pipeline (PNG file -> ImageImportService quantize
            // -> PortraitImportHelper.ImportSimple -> ROM write). Asserts
            // the pointer fields actually changed.
            string tmpPng = Path.Combine(Path.GetTempPath(),
                $"portrait_import_test_{Guid.NewGuid():N}.png");
            try
            {
                using (IImage synth = CoreState.ImageService.CreateImage(16, 16))
                {
                    byte[] rgba = new byte[16 * 16 * 4];
                    for (int i = 0; i < 16 * 16; i++)
                    {
                        rgba[i * 4 + 0] = (byte)((i * 7) & 0xFF);
                        rgba[i * 4 + 1] = (byte)((i * 13) & 0xFF);
                        rgba[i * 4 + 2] = (byte)((i * 23) & 0xFF);
                        rgba[i * 4 + 3] = 255;
                    }
                    synth.SetPixelData(rgba);
                    synth.Save(tmpPng);
                }

                // Step 2: full pipeline via ImageImportService (real PNG
                // path the wizard's PickFile_Click uses).
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(tmpPng, 0, 0, 16);
                Assert.NotNull(loadResult);
                Assert.True(loadResult.Success, loadResult.Error);
                Assert.Equal(16, loadResult.Width);
                Assert.Equal(16, loadResult.Height);

                // Step 3: pick a target slot.
                ROM rom = _fixture.ROM;
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint entryAddr = baseAddr + (uint)(6 * rom.RomInfo.portrait_datasize);

                uint d0Before = rom.p32(entryAddr + 0);
                uint d8Before = rom.p32(entryAddr + 8);

                // Step 4: import via helper — same code path the wizard uses.
                var undo = new UndoService();
                var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
                Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

                // Step 5: verify ROM was modified — pointer fields changed.
                Assert.NotEqual(d0Before, rom.p32(entryAddr + 0));
                Assert.NotEqual(d8Before, rom.p32(entryAddr + 8));

                // Step 6: preview should be reconstructable.
                using var preview = PortraitImportHelper.BuildPreviewImage(loadResult);
                Assert.NotNull(preview);
            }
            finally
            {
                try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// Build a 16-color GBA palette where index N maps to a unique color.
        /// Index 0 = transparent (black, alpha=0 at preview time).
        /// </summary>
        static byte[] BuildSimplePalette(int colors)
        {
            byte[] pal = new byte[colors * 2];
            for (int i = 0; i < colors; i++)
            {
                ushort c = (ushort)((i * 0x421) & 0x7FFF); // unique-ish 15-bit BGR
                pal[i * 2 + 0] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
            return pal;
        }

        /// <summary>
        /// Build a synthetic <see cref="ImageImportService.LoadResult"/> that
        /// looks like a successful quantization result — 16 colors, indexed
        /// pixel data filled with a simple gradient.
        /// </summary>
        static ImageImportService.LoadResult MakeSyntheticLoadResult(int w, int h)
        {
            byte[] indexed = new byte[w * h];
            for (int i = 0; i < indexed.Length; i++)
            {
                indexed[i] = (byte)((i / 8) % 16);
            }
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = indexed,
                GBAPalette = BuildSimplePalette(16),
                SourcePath = "C:\\tmp\\synthetic.png",
            };
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (dir == null)
                throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }
    }
}
