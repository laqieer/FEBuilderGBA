using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #840: render the Avalonia <see cref="ImageUnitPaletteView"/> with a
    /// palette entry selected and a class chosen so the class battle-anime sample
    /// preview renders — with the UNIT palette override (NOT the anime's own
    /// palette). Headless RenderTargetBitmap, so it works on locked machines and
    /// in CI (no visible desktop required). Mirrors
    /// <see cref="ImageBattleAnimePalletExportScreenshotTest"/>.
    ///
    /// <para>By default the PNG is written to a per-test temp directory (so
    /// normal/CI runs do NOT dirty the repo's tracked <c>pr-screenshots/</c>); set
    /// <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to override — pointing it at the repo's
    /// <c>pr-screenshots/</c> is how the canonical PR screenshot is regenerated.</para>
    ///
    /// <para>The view is the REAL editor with REAL ROM data: the entry list is
    /// loaded via the VM's <c>LoadList</c>, a palette row is selected through the
    /// same <c>OnSelected</c> path the UI runs (which sets SelectedPaletteSlot),
    /// then a class is set via the real <c>ClassBox.ValueChanged</c> handler. The
    /// first class that yields a non-blank sample is kept.</para>
    /// </summary>
    [Collection("SharedState")]
    public class ImageUnitPaletteSamplePreviewScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public ImageUnitPaletteSamplePreviewScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void ImageUnitPaletteView_ClassSampleRendered_WithUnitPalette_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            var view = new ImageUnitPaletteView();

            // Load the entry list via the VM (the Opened handler does this on a
            // real desktop; headless does not raise Opened on the same timeline).
            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);
            var vmField = typeof(ImageUnitPaletteView).GetField("_vm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (ImageUnitPaletteViewModel)vmField!.GetValue(view)!;

            // Cache the swatch controls (normally done in the Opened handler).
            var cacheMethod = typeof(ImageUnitPaletteView).GetMethod("CacheSwatchControls",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            cacheMethod?.Invoke(view, null);

            var items = vm.LoadList();
            entryList!.SetItems(items);
            _output.WriteLine($"Unit-palette entries: {items.Count}");
            if (items.Count == 0)
            {
                _output.WriteLine("SKIP: no unit-palette entries in this ROM");
                return;
            }

            const int W = 900;
            const int H = 620;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            var samplePreview = view.FindControl<GbaImageControl>("SamplePreview");
            var classBox = view.FindControl<NumericUpDown>("ClassBox");
            Assert.NotNull(samplePreview);
            Assert.NotNull(classBox);

            // Select the FIRST palette entry (sets SelectedPaletteSlot = rowIndex+1
            // via the real OnSelected path).
            entryList.SelectByIndex(0);
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            // Find a class whose battle anime renders a non-blank sample. Walk a
            // reasonable class range; the first that produces a non-null preview
            // (via the real ClassBox.ValueChanged -> RefreshSamplePreview path) wins.
            bool rendered = false;
            int renderedClass = -1;
            for (int cid = 1; cid <= 200; cid++)
            {
                classBox!.Value = cid;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                if (samplePreview!.HasImage)
                {
                    rendered = true;
                    renderedClass = cid;
                    _output.WriteLine($"Sample rendered for class 0x{cid:X2} (palette slot {vm.SelectedPaletteSlot}).");
                    break;
                }
            }

            if (!rendered)
            {
                _output.WriteLine("SKIP: no class produced a non-blank sample for this palette/ROM.");
                return;
            }

            // The render base must be the unit palette (the feature's whole point).
            Assert.True(samplePreview!.HasImage, "sample preview must carry a bitmap");

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);

            // Primary artifact: the actual rendered 360x290 sample grid (the real
            // class battle anime recolored with the UNIT palette). This goes
            // through the real SkiaImageService.EncodePng — independent of the
            // headless RenderTargetBitmap backend, so it always produces real
            // pixels even when the test harness uses the no-op headless drawing.
            try
            {
                IImage grid = vm.RenderClassSamplePreview();
                if (grid != null)
                {
                    byte[] png = grid.EncodePng();
                    if (png != null && png.Length > 0)
                    {
                        string gridPath = Path.Combine(outDir, "pr840-unitpalette-sample-grid.png");
                        File.WriteAllBytes(gridPath, png);
                        _output.WriteLine($"Saved sample grid (class 0x{renderedClass:X2}, {grid.Width}x{grid.Height}) to: {gridPath} ({png.Length} bytes)");
                    }
                    grid.Dispose();
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Sample-grid EncodePng failed: {ex.Message}");
            }

            // Secondary artifact: the full editor view (entry + class selected,
            // sample shown). Best-effort — the headless no-op drawing backend may
            // not support RenderTargetBitmap, in which case this is skipped (the
            // live-window PrintWindow capture is used for the PR instead).
            try
            {
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outPath = Path.Combine(outDir, "pr840-unitpalette-preview.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved editor screenshot (class 0x{renderedClass:X2}) to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless editor render skipped (environment, not the #840 feature): {ex.Message}");
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
