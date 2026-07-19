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
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
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

        sealed class UndoServiceSpy : UndoService
        {
            public int BeginCount { get; private set; }
            public override void Begin(string name) { BeginCount++; }
            public override void Commit() { }
            public override void Rollback() { }
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

            uint d0Before = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET);
            uint d8Before = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D8_PALETTE);

            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

            uint d0After  = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET);
            uint d4After  = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D4_MINI_FACE);
            uint d8After  = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D8_PALETTE);
            uint d12After = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D12_MOUTH_FRAMES);

            // D0 (sheet) and D8 (palette) must change to new pointers.
            Assert.NotEqual(d0Before, d0After);
            Assert.NotEqual(d8Before, d8After);
            // #657: D4 (mini face) and D12 (mouth frames) MUST be zeroed
            // on FE7/FE8 so the editor renders blank for those sub-views
            // instead of decoding stale tile blocks under the new palette.
            Assert.Equal((uint)0, d4After);
            Assert.Equal((uint)0, d12After);
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
            // FE6 mouth/eye coords must be untouched by the simple-path write
            // — FE6 reuses bytes +12..+15 for u8 mouth-X/mouth-Y/two unused,
            // so the D12 invalidate that #657 applies on FE7/FE8 is skipped
            // on FE6 to avoid silently relocating the mouth to (0,0).
            Assert.Equal(before12, after12);
        }

        // ------------------------------------------------------------------
        // #657: FE6 D4 invalidate. FE6 layout has +4 u32 Mini/Map face
        // pointer, which would render under the new D8 palette as garbled
        // garbage just like FE7/FE8. Zero D4 on FE6 too (Copilot CLI PR
        // review blocking #1).
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_FE6_ZerosD4Issue657()
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

            // Seed D4 with a sentinel pointer so we can prove it was zeroed.
            rom.write_p32(entryAddr + PortraitImportHelper.OFFSET_D4_MINI_FACE, 0x08AAAAAAu);

            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

            // D4 (Mini/Map face pointer on FE6) MUST be zeroed.
            Assert.Equal((uint)0, rom.p32(entryAddr + PortraitImportHelper.OFFSET_D4_MINI_FACE));
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

            // On Linux CI runners the bundled libSkiaSharp.so may report an
            // incompatible native-library version (88.x vs the managed
            // SkiaSharp 3.116.x expected range). When that happens, every
            // SKBitmap constructor throws TypeInitializationException; the
            // managed code under test is fine but cannot be exercised without
            // a usable native runtime. Skip in that case rather than failing
            // the whole CI matrix — coverage on Windows/macOS still validates
            // BuildPreviewImage end-to-end. The Linux ubuntu-latest job hits
            // this; PR #684 CI logs show the underlying
            // SkiaSharp.SkiaSharpVersion.CheckNativeLibraryCompatible failure.
            IImage? preview;
            try
            {
                preview = PortraitImportHelper.BuildPreviewImage(loadResult);
            }
            catch (TypeInitializationException ex)
                when (ex.InnerException is InvalidOperationException ioe
                      && ioe.Message.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"SKIP: native libSkiaSharp incompatible on this host: {ioe.Message}");
                return;
            }

            using (preview)
            {
                Assert.NotNull(preview);
                Assert.Equal(16, preview!.Width);
                Assert.Equal(16, preview.Height);
            }
        }

        [Fact]
        public void BuildPreviewImage_OpaqueRawBackground_UsesImportColorKeyTransparency()
        {
            using var _ = EnsureImageService();
            var loadResult = MakeOpaqueBackgroundLoadResult(96, 80);

            Assert.DoesNotContain((byte)0, loadResult.IndexedPixels);
            byte[] keyed = PortraitImportHelper.BuildColorKeyedRgba(loadResult);

            // On Linux CI runners the bundled libSkiaSharp.so may report an
            // incompatible native-library version (88.x vs the managed
            // SkiaSharp 3.116.x expected range). When that happens, every
            // SKBitmap constructor throws TypeInitializationException; the
            // managed code under test is fine but cannot be exercised without
            // a usable native runtime. Skip in that case rather than failing
            // the whole CI matrix — coverage on Windows/macOS still validates
            // BuildPreviewImage end-to-end. The Linux ubuntu-latest job hits
            // this; PR #684 CI logs show the underlying
            // SkiaSharp.SkiaSharpVersion.CheckNativeLibraryCompatible failure.
            IImage? preview;
            try
            {
                preview = PortraitImportHelper.BuildPreviewImage(loadResult);
            }
            catch (TypeInitializationException ex)
                when (ex.InnerException is InvalidOperationException ioe
                      && ioe.Message.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"SKIP: native libSkiaSharp incompatible on this host: {ioe.Message}");
                return;
            }

            using (preview)
            {
                Assert.NotNull(preview);
                byte[] rgba = preview!.GetPixelData();
                Assert.Equal(keyed.Length, rgba.Length);

                for (int i = 0; i < loadResult.Width * loadResult.Height; i++)
                {
                    Assert.Equal(keyed[i * 4 + 3], rgba[i * 4 + 3]);
                }

                Assert.Equal(0, GetAlpha(rgba, loadResult.Width, 0, 0));
                Assert.Equal(0, GetAlpha(rgba, loadResult.Width, loadResult.Width - 1, 0));
                Assert.Equal(0, GetAlpha(rgba, loadResult.Width, loadResult.Width - 1, loadResult.Height - 1));
                Assert.Equal(255, GetAlpha(rgba, loadResult.Width, 16, 16));
            }
        }

        // ------------------------------------------------------------------
        // #1980 — BuildPreparedPreviewLoadResult: the shared cache both the
        // Step-2 source preview AND the per-frame live preview now read from.
        // ------------------------------------------------------------------

        [Fact]
        public void BuildPreparedPreviewLoadResult_OpaqueRawBackground_MapsBackgroundToIndex0_AndDoesNotMutateSource()
        {
            using var _ = EnsureImageService();
            var loadResult = MakeOpaqueBackgroundLoadResult(96, 80);

            // Snapshot the source buffers BEFORE calling the method under
            // test, so we can prove afterwards that nothing was mutated
            // in place (the plan explicitly requires "do not mutate source
            // buffers").
            byte[] origIndexed = (byte[])loadResult.IndexedPixels.Clone();
            byte[] origPalette = (byte[])loadResult.GBAPalette.Clone();
            byte[] origRgba = (byte[])loadResult.RGBAPixels.Clone();

            var prepared = PortraitImportHelper.BuildPreparedPreviewLoadResult(loadResult);

            Assert.NotNull(prepared);
            Assert.True(prepared!.Success);
            Assert.Equal(loadResult.Width, prepared.Width);
            Assert.Equal(loadResult.Height, prepared.Height);

            // Source buffers must be byte-for-byte unchanged.
            Assert.Equal(origIndexed, loadResult.IndexedPixels);
            Assert.Equal(origPalette, loadResult.GBAPalette);
            Assert.Equal(origRgba, loadResult.RGBAPixels);

            // The prepared result must be freshly allocated, not aliasing the
            // source arrays (belt-and-suspenders against accidental in-place
            // quantization writing back into the caller's buffers).
            Assert.NotSame(loadResult.IndexedPixels, prepared.IndexedPixels);
            Assert.NotSame(loadResult.GBAPalette, prepared.GBAPalette);

            // The color-keyed background (the corner pixel, which
            // MakeOpaqueBackgroundLoadResult fills solid green) must have been
            // remapped to palette index 0 (transparent) by quantization —
            // this is exactly the correctness bug #1980 reports: the raw,
            // un-keyed IndexedPixels/GBAPalette do NOT have this property.
            Assert.Equal(0, prepared.IndexedPixels[0]);

            byte[] rgba = PortraitImportHelper.ReconstructRgbaWithPaletteZeroTransparent(prepared);
            Assert.NotNull(rgba);
            Assert.Equal(0, GetAlpha(rgba, prepared.Width, 0, 0));
            Assert.Equal(0, GetAlpha(rgba, prepared.Width, prepared.Width - 1, 0));
            Assert.Equal(0, GetAlpha(rgba, prepared.Width, prepared.Width - 1, prepared.Height - 1));
            // The foreground square (rows/cols 16..64) must still be opaque —
            // it must not have been swallowed by the background color-key.
            Assert.Equal(255, GetAlpha(rgba, prepared.Width, 32, 32));
        }

        [Fact]
        public void BuildPreparedPreviewLoadResult_UnusableInput_ReturnsNull_ForCacheClear()
        {
            // #1980: RebuildPreparedPreview relies on this returning null (not
            // throwing) so the view can safely clear its cache when the
            // source is missing/unusable — e.g. a failed load.
            Assert.Null(PortraitImportHelper.BuildPreparedPreviewLoadResult(null));

            var failed = new ImageImportService.LoadResult { Success = false, Width = 96, Height = 80 };
            Assert.Null(PortraitImportHelper.BuildPreparedPreviewLoadResult(failed));
        }

        // ------------------------------------------------------------------
        // #1980 — behavioral end-to-end: prepared result -> RenderFramePreview
        // must produce alpha=0 at the color-keyed background. A source-text
        // assertion alone would not catch a regression where the prepared
        // buffers are computed correctly but never actually reach the
        // frame-preview renderer.
        // ------------------------------------------------------------------

        [Fact]
        public void RenderFramePreview_FromPreparedResult_OpaqueRawBackground_ProducesTransparentBackground()
        {
            using var _ = EnsureImageService();
            // Needs 16 distinct colors (not just background+foreground) so the
            // quantizer fills all 16 palette slots — RenderFramePreview
            // requires palette.Length >= 32 (16 colors) and rejects the
            // smaller palette a 2-color fixture would quantize to.
            var loadResult = MakeOpaqueBackgroundManyColorsLoadResult(96, 80);
            var prepared = PortraitImportHelper.BuildPreparedPreviewLoadResult(loadResult);
            Assert.NotNull(prepared);
            Assert.True(prepared!.GBAPalette.Length >= 32);

            IImage preview;
            try
            {
                // Same block/crop shape as ImagePortraitImporterView's default
                // NUD values / FEBuilderGBA.Core.Tests'
                // PortraitImportPreviewCoreTests.Render helper: eyeBlock(0,0),
                // mouthBlock(0,8) -> dest px (0,64); full-slot crops so the
                // eye/mouth STAGE-A rebuild reads/writes are no-ops against a
                // 96x80 source (the eye/mouth slot source coordinates, e.g.
                // x=96, sit outside a 96-wide image and are safely clamped
                // away by BlitIndexed's bounds clamping).
                preview = PortraitImportPreviewCore.RenderFramePreview(
                    prepared.IndexedPixels, prepared.Width, prepared.Height, prepared.GBAPalette,
                    eyeBlockX: 0, eyeBlockY: 0, mouthBlockX: 0, mouthBlockY: 8,
                    eyeCropX: 0, eyeCropY: 0, eyeCropW: 32, eyeCropH: 16,
                    mouthCropX: 0, mouthCropY: 0, mouthCropW: 32, mouthCropH: 16,
                    frameIndex: 0, isFe6: false);
            }
            catch (TypeInitializationException ex)
                when (ex.InnerException is InvalidOperationException ioe
                      && ioe.Message.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"SKIP: native libSkiaSharp incompatible on this host: {ioe.Message}");
                return;
            }

            Assert.NotNull(preview);
            using (preview)
            {
                byte[] rgba = preview!.GetPixelData();
                Assert.Equal(prepared.Width, preview.Width);
                Assert.Equal(prepared.Height, preview.Height);

                Assert.Equal(0, GetAlpha(rgba, preview.Width, 0, 0));
                Assert.Equal(0, GetAlpha(rgba, preview.Width, preview.Width - 1, 0));
                Assert.Equal(0, GetAlpha(rgba, preview.Width, preview.Width - 1, preview.Height - 1));
                Assert.Equal(255, GetAlpha(rgba, preview.Width, 32, 32));
            }
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
                "ImagePortraitImporter_FERepo_Button",
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

        // ------------------------------------------------------------------
        // #664: FE-Repo browser + drag-and-drop wiring — static source +
        // AXAML structure checks. We avoid trying to synthesize Avalonia
        // DragEventArgs in headless tests (brittle), per Copilot CLI plan
        // review on issue #664.
        // ------------------------------------------------------------------

        [Fact]
        public void Wizard_View_HasFERepoButton()
        {
            string repoRoot = FindRepoRoot();
            string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml");
            string xaml = File.ReadAllText(axamlPath);

            // AXAML carries the FE-Repo button declaration.
            Assert.Contains("ImagePortraitImporter_FERepo_Button", xaml);
            Assert.Contains("Name=\"FERepoButton\"", xaml);
            Assert.Contains("Click=\"FERepo_Click\"", xaml);

            // Code-behind has the handler that opens the embeddable FERepoResourceBrowserWindow.
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);
            Assert.Contains("void FERepo_Click", source);
            Assert.Contains("OpenModal<FERepoResourceBrowserWindow, string>", source);
            // FE-Repo path must funnel through the shared loader, not import
            // directly — so the user still sees the preview and hits Import.
            Assert.Matches(new Regex(@"FERepo_Click[\s\S]+?LoadImageFromPath\("), source);
        }

        [Fact]
        public void PortraitColorKey_WithOpaqueCornerColor_MapsBackgroundToTransparentIndexZero()
        {
            using var _ = EnsureImageService();
            var lr = MakeOpaqueBackgroundLoadResult(128, 112);

            byte[] keyed = PortraitImportHelper.BuildColorKeyedRgba(lr);
            var qr = DecreaseColorCore.Quantize(keyed, lr.Width, lr.Height, 16);

            Assert.NotNull(qr);
            Assert.Equal(0, qr.IndexData[0 * lr.Width + (lr.Width - 1)]);
            Assert.Equal(0, qr.IndexData[(lr.Height - 1) * lr.Width + (lr.Width - 1)]);
            Assert.Equal(0, qr.IndexData[0]);
            Assert.NotEqual(0, qr.IndexData[(16 * lr.Width) + 16]);
        }

        [Fact]
        public void PortraitColorKey_UsesWinFormsCornerOrder_AndRunsBeforeQuantize()
        {
            byte[] rgba = SolidRgba(16, 16, 1, 2, 3, 255);
            SetPixel(rgba, 16, 15, 0, 9, 9, 9, 255);      // top-right wins
            SetPixel(rgba, 16, 15, 15, 8, 8, 8, 255);    // bottom-right ignored
            SetPixel(rgba, 16, 0, 0, 7, 7, 7, 255);      // top-left ignored
            SetPixel(rgba, 16, 4, 4, 9, 9, 9, 255);

            Assert.True(PortraitImportHelper.ApplyPortraitBackgroundColorKey(rgba, 16, 16));

            Assert.Equal(0, GetAlpha(rgba, 16, 15, 0));
            Assert.Equal(0, GetAlpha(rgba, 16, 4, 4));
            Assert.Equal(255, GetAlpha(rgba, 16, 15, 15));
            Assert.Equal(255, GetAlpha(rgba, 16, 0, 0));
        }

        [Fact]
        public void PortraitColorKey_AllCornersTransparent_PreservesImage()
        {
            byte[] rgba = SolidRgba(16, 16, 10, 20, 30, 255);
            SetPixel(rgba, 16, 15, 0, 0, 0, 0, 0);
            SetPixel(rgba, 16, 15, 15, 0, 0, 0, 0);
            SetPixel(rgba, 16, 0, 0, 0, 0, 0, 0);

            Assert.False(PortraitImportHelper.ApplyPortraitBackgroundColorKey(rgba, 16, 16));
            Assert.Equal(255, GetAlpha(rgba, 16, 4, 4));
        }

        [Fact]
        public void ImportPortrait_UnsupportedSize_ReturnsGuidanceBeforeUndoScope()
        {
            var undo = new UndoServiceSpy();
            var outcome = PortraitImportHelper.ImportPortrait(
                new ROM(), 0x100, MakeSyntheticLoadResult(16, 16), undo);

            Assert.False(outcome.Success);
            Assert.Contains("Unsupported portrait image size", outcome.Error);
            Assert.Equal(0, undo.BeginCount);
        }

        [Fact]
        public void PortraitImportHelper_ImportSheet_OpaqueBackdrop_AutoQuantize_IsTransparentAfterImport()
        {
            using var _ = EnsureImageService();
            var rom = MakeSyntheticFe8URom();
            var lr = MakeOpaqueBackgroundLoadResult(128, 112);
            uint entryAddr = 0x1000;
            var outcome = PortraitImportHelper.ImportPortrait(
                rom, entryAddr, lr, new UndoServiceSpy(), "Import sheet test");
            Assert.True(outcome.Success, outcome.Error);

            byte[] tileData = ReadRawD0TileData(rom, entryAddr);
            byte bg = GetTilePixel(tileData, 256, 0, 0);
            byte skin = GetTilePixel(tileData, 256, 0, 16);

            Assert.Equal(0, bg);
            Assert.NotEqual(0, skin);
        }

        [Fact]
        public void PortraitImportHelper_Import96x80Face_UsesSheetReverseAssembly()
        {
            using var _ = EnsureImageService();
            var lr = MakeOpaqueBackgroundLoadResult(96, 80);

            byte[] keyed = PortraitImportHelper.BuildColorKeyedRgba(lr);
            byte[] sheet = PortraitRendererCore.PromoteFaceToPortraitSheet(keyed, 96, 80);
            var parts = PortraitRendererCore.SplitPortraitSheet(sheet, 128, 112);

            Assert.Equal(0, parts.SpriteSheetPixels[3]);
            int skinOff = (16 * parts.SpriteSheetW + 0) * 4;
            Assert.Equal(200, parts.SpriteSheetPixels[skinOff + 0]);
            Assert.Equal(80, parts.SpriteSheetPixels[skinOff + 1]);
            Assert.Equal(40, parts.SpriteSheetPixels[skinOff + 2]);
            Assert.Equal(255, parts.SpriteSheetPixels[skinOff + 3]);
        }



        [Fact]
        public void ImportPortrait_TwoparterHackbox_ReturnsExplicitUnsupportedBeforeUndoScope()
        {
            var undo = new UndoServiceSpy();
            var outcome = PortraitImportHelper.ImportPortrait(
                new ROM(), 0x100, MakeSyntheticLoadResult(144, 304), undo);

            Assert.False(outcome.Success);
            Assert.Contains("Twoparter Hackbox", outcome.Error);
            Assert.Contains("not yet supported", outcome.Error);
            Assert.Equal(0, undo.BeginCount);
        }

        [Fact]
        public void ImportPortrait_HalfBodyHackbox_RequiresHalfBodyPatchBeforeUndoScope()
        {
            using var _ = EnsureImageService();
            var rom = MakeSyntheticFe8URom();
            var undo = new UndoServiceSpy();

            var outcome = PortraitImportHelper.ImportPortrait(
                rom, 0x1000, MakeHalfBodyLoadResult(), undo);

            Assert.False(outcome.Success);
            Assert.Contains("HALFBODY", outcome.Error);
            Assert.Equal(0, undo.BeginCount);
        }

        [Fact]
        public void ImportPortrait_HalfBodyHackbox_CustomPaletteRequires64Bytes()
        {
            using var _ = EnsureImageService();
            var rom = MakeSyntheticFe8URom();
            InstallHalfBodyPatch(rom);
            var undo = new UndoServiceSpy();

            var outcome = PortraitImportHelper.ImportPortrait(
                rom, 0x1000, MakeHalfBodyLoadResult(), undo,
                PortraitPaletteMode.CustomPalette, BuildSimplePalette(16), false,
                "Halfbody custom palette test");

            Assert.False(outcome.Success);
            Assert.Contains("64 bytes", outcome.Error);
            Assert.Equal(0, undo.BeginCount);
        }

        [Fact]
        public void ImportPortrait_HalfBodyHackbox_AutoQuantizeWritesHalfBodyHeaderAnd64BytePalette()
        {
            using var _ = EnsureImageService();
            var rom = MakeSyntheticFe8URom();
            InstallHalfBodyPatch(rom);
            uint entryAddr = 0x1000;

            var outcome = PortraitImportHelper.ImportPortrait(
                rom, entryAddr, MakeHalfBodyLoadResult(), new UndoServiceSpy(),
                "Halfbody import test");

            Assert.True(outcome.Success, outcome.Error);
            uint d0 = U.toOffset(rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET));
            uint d8 = U.toOffset(rom.p32(entryAddr + PortraitImportHelper.OFFSET_D8_PALETTE));
            Assert.True(U.isSafetyOffset(d0, rom));
            Assert.True(U.isSafetyOffset(d8, rom));
            Assert.Equal((byte)0x00, rom.u8(d0 + 0));
            Assert.Equal((byte)0x04, rom.u8(d0 + 1));
            Assert.Equal((byte)0x20, rom.u8(d0 + 2));
            Assert.Equal((byte)0x00, rom.u8(d0 + 3));

            byte[] palette64 = rom.getBinaryData(d8, 64);
            Assert.Equal(64, palette64.Length);
            Assert.Contains(palette64[32..], b => b != 0);
            Assert.Equal((uint)0, rom.p32(entryAddr + PortraitImportHelper.OFFSET_D16_CLASS_CARD));

            byte[] tileData = rom.getBinaryData(d0 + 4, 256 / 8 * 64 / 8 * 32);
            Assert.NotEqual(0, GetTilePixel(tileData, 256, 0, 32));
        }

        [Fact]
        public void Wizard_View_HasDragAndDropWiring()
        {
            string repoRoot = FindRepoRoot();
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);

            // AllowDrop enabled in the constructor.
            Assert.Contains("DragDrop.SetAllowDrop(this, true)", source);
            // Both drag handlers registered.
            Assert.Contains("AddHandler(DragDrop.DragOverEvent, OnDragOver)", source);
            Assert.Contains("AddHandler(DragDrop.DropEvent, OnDrop)", source);
            // OnDrop delegates to the shared loader, not directly to import.
            Assert.Contains("void OnDrop(", source);
            Assert.Matches(new Regex(@"OnDrop\([\s\S]+?LoadImageFromPath\("), source);
        }

        [Fact]
        public void Wizard_View_SharedLoadImageFromPath_UsedByAllEntryPoints()
        {
            // Copilot CLI plan review #2 acceptance: Pick PNG/BMP, FE-Repo,
            // and drag-drop all funnel through LoadImageFromPath so preview
            // behavior is identical.
            string repoRoot = FindRepoRoot();
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);

            // The shared helper exists.
            Assert.Contains("void LoadImageFromPath(string", source);

            // PickFile_Click delegates to it (no longer inlines the load + preview).
            Assert.Matches(new Regex(@"PickFile_Click[\s\S]+?LoadImageFromPath\("), source);

            // BuildPreviewImageFromPrepared must be inside SetQuantizedPreview
            // (called from LoadImageFromPath, not duplicated in PickFile_Click),
            // so all entry points share the single preview-render call site.
            // (#1980: the View now shares one cached, color-keyed + quantized
            // prepared result — see RebuildPreparedPreview — between the
            // Step-2 source preview and the per-frame preview, instead of
            // calling BuildPreviewImage directly and re-deriving it per call.)
            // Strip line comments first so the doc-header reference doesn't
            // count as an extra call site.
            string codeOnly = Regex.Replace(source, @"//.*", string.Empty);
            var callMatches = Regex.Matches(codeOnly, @"PortraitImportHelper\.BuildPreviewImageFromPrepared\s*\(");
            Assert.Single(callMatches);

            // The shared prepared-preview cache must be rebuilt exactly once
            // per image load — LoadImageFromPath (Pick/FE-Repo/drag-drop) and
            // the SeedFramePreviewForScreenshot harness seed — never inside a
            // per-frame/crop NUD handler (which would re-quantize on every tweak).
            var rebuildMatches = Regex.Matches(codeOnly, @"RebuildPreparedPreview\s*\(\s*\)\s*;");
            Assert.Equal(2, rebuildMatches.Count);

            // BuildPreviewImage itself (the raw, un-cached convenience API) must
            // NOT be called directly by the view anymore — only the prepared
            // overload, so a new image load can't silently bypass the cache.
            var rawCallMatches = Regex.Matches(codeOnly, @"PortraitImportHelper\.BuildPreviewImage\s*\(");
            Assert.Empty(rawCallMatches);
        }

        [AvaloniaFact]
        public void Wizard_View_AllowDropIsTrue_AfterConstruction()
        {
            // Construct the view in the Avalonia test app and verify the
            // drag-drop attached property was set in the constructor.
            var view = new ImagePortraitImporterView();
            try
            {
                Assert.True(DragDrop.GetAllowDrop(view),
                    "AllowDrop must be true so the window accepts file drops.");
            }
            finally
            {
                view.Close();
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

            Assert.Contains("PortraitImportHelper.ImportPortrait", source);
            Assert.DoesNotMatch(new Regex(@"PortraitImportHelper\.ImportSimple\s*\("), source);
            Assert.DoesNotMatch(new Regex(@"PortraitImportHelper\.ImportSheet\s*\("), source);
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

            Assert.Contains("PortraitImportHelper.ImportPortrait", source);
            Assert.DoesNotMatch(new Regex(@"PortraitImportHelper\.ImportSimple\s*\("), source);
            Assert.DoesNotMatch(new Regex(@"PortraitImportHelper\.ImportSheet\s*\("), source);

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

                uint d0Before = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET);
                uint d8Before = rom.p32(entryAddr + PortraitImportHelper.OFFSET_D8_PALETTE);

                // Step 4: import via helper — same code path the wizard uses.
                var undo = new UndoService();
                var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
                Assert.True(outcome.Success, $"Import failed: {outcome.Error}");

                // Step 5: verify ROM was modified — pointer fields changed.
                Assert.NotEqual(d0Before, rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET));
                Assert.NotEqual(d8Before, rom.p32(entryAddr + PortraitImportHelper.OFFSET_D8_PALETTE));

                // Step 6: preview should be reconstructable.
                using var preview = PortraitImportHelper.BuildPreviewImage(loadResult);
                Assert.NotNull(preview);
            }
            finally
            {
                try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
            }
        }


        [Fact]
        public void PortraitImport_ProofPng_SavesDeterministicImage()
        {
            using var _ = EnsureImageService();
            var lr = MakeOpaqueBackgroundLoadResult(96, 80);
            byte[] keyed = PortraitImportHelper.BuildColorKeyedRgba(lr);
            byte[] sheet = PortraitRendererCore.PromoteFaceToPortraitSheet(keyed, 96, 80);
            var parts = PortraitRendererCore.SplitPortraitSheet(sheet, 128, 112);

            const int outW = 96 + 8 + 256;
            const int outH = 80;
            byte[] canvas = SolidRgba(outW, outH, 32, 32, 32, 255);
            BlitOpaque(lr.RGBAPixels, 96, 80, canvas, outW, 0, 0);
            BlitAlpha(keyed, 96, 80, canvas, outW, 104, 0);
            BlitAlpha(parts.SpriteSheetPixels, 256, 32, canvas, outW, 104, 48);

            Assert.Contains((byte)200, canvas);
            Assert.Contains((byte)32, canvas);

            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(overrideDir))
                return;

            string repoRoot = FindRepoRoot();
            string outDir = Path.GetFullPath(overrideDir);
            string relativeOutDir = Path.GetRelativePath(repoRoot, outDir);
            if (Path.IsPathRooted(relativeOutDir)
                || relativeOutDir == ".."
                || relativeOutDir.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Screenshot output must stay inside the repository.");
            }
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "portrait-import-1750-1751-proof.png");
            using IImage img = CoreState.ImageService.CreateImage(outW, outH);
            img.SetPixelData(canvas);
            img.Save(outPath);

            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 0);
        }



        [Fact]
        public void PortraitImport_HalfBodyProofPng_SavesDeterministicImage()
        {
            using var _ = EnsureImageService();
            var lr = MakeHalfBodyLoadResult();
            byte[] keyed = PortraitImportHelper.BuildColorKeyedRgba(lr);
            var parts = PortraitRendererCore.SplitHalfBodyPortraitSheet(keyed, 160, 160);
            Assert.NotNull(parts);

            const int outW = 160 + 8 + 256;
            const int outH = 160;
            byte[] canvas = SolidRgba(outW, outH, 32, 32, 32, 255);
            BlitOpaque(lr.RGBAPixels, 160, 160, canvas, outW, 0, 0);
            BlitAlpha(parts.SpriteSheetPixels, 256, 64, canvas, outW, 168, 0);
            BlitAlpha(parts.MiniPixels, 32, 32, canvas, outW, 168, 72);
            BlitAlpha(parts.MouthPixels, 32, 96, canvas, outW, 208, 64);

            Assert.Contains((byte)220, canvas);
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(overrideDir))
                return;

            string repoRoot = FindRepoRoot();
            string outDir = Path.GetFullPath(overrideDir);
            string relativeOutDir = Path.GetRelativePath(repoRoot, outDir);
            if (Path.IsPathRooted(relativeOutDir)
                || relativeOutDir == ".."
                || relativeOutDir.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Screenshot output must stay inside the repository.");
            }
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "portrait-import-halfbody-1752-proof.png");
            using IImage img = CoreState.ImageService.CreateImage(outW, outH);
            img.SetPixelData(canvas);
            img.Save(outPath);

            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 0);
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
            var result = new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = indexed,
                GBAPalette = BuildSimplePalette(16),
                SourcePath = "C:\\tmp\\synthetic.png",
            };
            byte[] rgba = new byte[w * h * 4];
            var svc = CoreState.ImageService;
            if (svc != null)
            {
                for (int i = 0; i < indexed.Length; i++)
                {
                    ushort color = (ushort)(result.GBAPalette[indexed[i] * 2]
                        | (result.GBAPalette[indexed[i] * 2 + 1] << 8));
                    svc.GBAColorToRGBA(color, out byte r, out byte g, out byte b);
                    rgba[i * 4 + 0] = r;
                    rgba[i * 4 + 1] = g;
                    rgba[i * 4 + 2] = b;
                    rgba[i * 4 + 3] = 255;
                }
            }
            result.RGBAPixels = rgba;
            return result;
        }

        static ImageImportService.LoadResult MakeOpaqueBackgroundLoadResult(int w, int h)
        {
            byte[] rgba = SolidRgba(w, h, 0, 255, 0, 255);
            for (int y = 16; y < Math.Min(h, 64); y++)
            {
                for (int x = 16; x < Math.Min(w, 64); x++)
                {
                    SetPixel(rgba, w, x, y, 200, 80, 40, 255);
                }
            }
            var qr = DecreaseColorCore.Quantize(rgba, w, h, 16);
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = qr.IndexData,
                GBAPalette = qr.GBAPalette,
                RGBAPixels = rgba,
                SourcePath = "C:\\tmp\\opaque.png",
            };
        }

        /// <summary>
        /// Like <see cref="MakeOpaqueBackgroundLoadResult"/> (solid opaque
        /// background + a foreground square), but the foreground is a
        /// multi-band gradient with 15+ distinct colors so the median-cut
        /// quantizer fills all 16 palette slots — matching a real 16-color
        /// portrait sheet. Needed for
        /// <see cref="PortraitImportPreviewCore.RenderFramePreview"/>, which
        /// requires <c>palette.Length &gt;= 32</c> (16 colors); the plain
        /// 2-color <see cref="MakeOpaqueBackgroundLoadResult"/> fixture only
        /// quantizes to 2 colors and is rejected by that guard.
        /// </summary>
        static ImageImportService.LoadResult MakeOpaqueBackgroundManyColorsLoadResult(int w, int h)
        {
            byte[] rgba = SolidRgba(w, h, 0, 255, 0, 255);
            const int bands = 15;
            int y0 = 16, y1 = Math.Min(h, 64);
            int bandHeight = Math.Max(1, (y1 - y0) / bands);
            for (int band = 0; band < bands; band++)
            {
                byte r = (byte)(16 + band * 15);
                byte g = (byte)(40 + band * 10);
                byte b = (byte)(220 - band * 12);
                int yStart = y0 + band * bandHeight;
                int yEnd = band == bands - 1 ? y1 : Math.Min(yStart + bandHeight, y1);
                for (int y = yStart; y < yEnd; y++)
                {
                    for (int x = 16; x < Math.Min(w, 64); x++)
                    {
                        SetPixel(rgba, w, x, y, r, g, b, 255);
                    }
                }
            }
            var qr = DecreaseColorCore.Quantize(rgba, w, h, 16);
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = qr.IndexData,
                GBAPalette = qr.GBAPalette,
                RGBAPixels = rgba,
                SourcePath = "C:\\tmp\\opaque_manycolors.png",
            };
        }

        static byte[] SolidRgba(int w, int h, byte r, byte g, byte b, byte a)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = a;
            }
            return rgba;
        }

        static void SetPixel(byte[] rgba, int w, int x, int y, byte r, byte g, byte b, byte a)
        {
            int off = (y * w + x) * 4;
            rgba[off + 0] = r;
            rgba[off + 1] = g;
            rgba[off + 2] = b;
            rgba[off + 3] = a;
        }

        static byte GetAlpha(byte[] rgba, int w, int x, int y)
            => rgba[(y * w + x) * 4 + 3];


        static void BlitOpaque(byte[] src, int srcW, int srcH, byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    int si = (y * srcW + x) * 4;
                    int di = ((dstY + y) * dstW + (dstX + x)) * 4;
                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                }
            }
        }

        static void BlitAlpha(byte[] src, int srcW, int srcH, byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    int si = (y * srcW + x) * 4;
                    int di = ((dstY + y) * dstW + (dstX + x)) * 4;
                    if (src[si + 3] < 128) continue;
                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                }
            }
        }



        static ImageImportService.LoadResult MakeHalfBodyLoadResult()
        {
            byte[] rgba = new byte[160 * 160 * 4];
            SetRect(rgba, 160, 32, 0, 64, 32, 220, 40, 40, 255);
            SetRect(rgba, 160, 0, 80, 128, 32, 40, 40, 220, 255);
            SetRect(rgba, 160, 128, 64, 32, 32, 40, 220, 40, 255);
            SetRect(rgba, 160, 128, 96, 32, 16, 220, 220, 40, 255);
            SetRect(rgba, 160, 0, 128, 96, 32, 220, 40, 220, 255);
            var qr = DecreaseColorCore.Quantize(rgba, 160, 160, 16);
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = 160,
                Height = 160,
                IndexedPixels = qr.IndexData,
                GBAPalette = qr.GBAPalette,
                RGBAPixels = rgba,
                SourcePath = "C:\\repo\\halfbody.png",
            };
        }

        static void SetRect(byte[] rgba, int w, int x, int y, int rw, int rh, byte r, byte g, byte b, byte a)
        {
            for (int yy = 0; yy < rh; yy++)
            {
                for (int xx = 0; xx < rw; xx++)
                {
                    SetPixel(rgba, w, x + xx, y + yy, r, g, b, a);
                }
            }
        }

        static void InstallHalfBodyPatch(ROM rom)
        {
            rom.write_u8(0x8540, 0x0A);
            rom.write_u8(0x8541, 0x1C);
        }

        static ROM MakeSyntheticFe8URom()
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-fe8u.gba", new byte[0x1000000], "BE8E01"));
            return rom;
        }

        static byte[] ReadRawD0TileData(ROM rom, uint entryAddr)
        {
            uint d0 = U.toOffset(rom.p32(entryAddr + PortraitImportHelper.OFFSET_D0_TILE_SHEET));
            Assert.True(U.isSafetyOffset(d0, rom));
            Assert.Equal((byte)0x00, rom.u8(d0 + 0));
            Assert.Equal((byte)0x04, rom.u8(d0 + 1));
            Assert.Equal((byte)0x10, rom.u8(d0 + 2));
            Assert.Equal((byte)0x00, rom.u8(d0 + 3));
            return rom.getBinaryData(d0 + 4, 256 / 8 * 32 / 8 * 32);
        }

        static byte GetTilePixel(byte[] tileData, int imageWidth, int x, int y)
        {
            int tilesX = imageWidth / 8;
            int tileIndex = (y / 8) * tilesX + (x / 8);
            int inTileX = x % 8;
            int inTileY = y % 8;
            byte packed = tileData[tileIndex * 32 + inTileY * 4 + inTileX / 2];
            return (byte)((inTileX & 1) == 0 ? packed & 0x0F : packed >> 4);
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
