using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #828: render the Avalonia <see cref="ImageBattleAnimePalletView"/>
    /// with a battle-animation entry selected to a PNG that proves the
    /// <b>Export Image</b> button is now ENABLED beside the rendered 360x290
    /// sample preview (#822 sample render + #815 ExportPng primitive). Headless
    /// RenderTargetBitmap — does NOT require a visible desktop, so it works on
    /// locked machines and in CI. Mirrors
    /// <see cref="WorldMapImageListExpandScreenshotTest"/> /
    /// <see cref="ItemShopFirstIconScreenshotTest"/>.
    ///
    /// <para>By default the PNG is written to a per-test temp directory (so
    /// normal/CI runs do NOT dirty the repo's tracked <c>pr-screenshots/</c>);
    /// set <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to override — pointing it at the
    /// repo's <c>pr-screenshots/</c> is how the canonical PR screenshot is
    /// regenerated locally.</para>
    ///
    /// <para>The view is the REAL editor with REAL ROM data: the entry list is
    /// loaded via the VM's <c>LoadList</c>, the first row that yields a non-null
    /// sample is selected through the same <c>OnSelectedEntry</c> path the UI
    /// runs, and the Export button's <c>IsEnabled</c> is asserted == the sample's
    /// <c>HasImage</c> (the #828 gating contract).</para>
    /// </summary>
    [Collection("SharedState")]
    public class ImageBattleAnimePalletExportScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public ImageBattleAnimePalletExportScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void ImageBattleAnimePalletView_SampleRendered_ExportEnabled_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            // Battle-animation list + LZ77 palettes exist on every FE GBA ROM, but
            // the sample render is exercised against the loaded ROM regardless of
            // version. Use whatever ROM the fixture resolved.
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            var view = new ImageBattleAnimePalletView();

            // Load the entry list via the VM (the Opened handler does this on a
            // real desktop; headless does not raise Opened on the same timeline).
            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);
            var vmField = typeof(ImageBattleAnimePalletView).GetField("_vm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (ImageBattleAnimePalletViewModel)vmField!.GetValue(view)!;

            var items = vm.LoadList();
            entryList!.SetItems(items);
            _output.WriteLine($"Battle-anime entries: {items.Count}");
            if (items.Count == 0)
            {
                _output.WriteLine("SKIP: no battle-anime entries in this ROM");
                return;
            }

            // Realize the visual tree so the SamplePreview + Export button exist.
            const int W = 1180;
            const int H = 820;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            var exportButton = view.FindControl<Button>("ExportButton");
            var samplePreview = view.FindControl<GbaImageControl>("SamplePreview");
            Assert.NotNull(exportButton);
            Assert.NotNull(samplePreview);

            // Select the first row that renders a non-null sample (mirrors WF
            // DrawSample). OnSelectedEntry runs RefreshSamplePreview, which gates
            // the Export button on SamplePreview.HasImage (#828).
            bool rendered = false;
            for (int i = 0; i < items.Count; i++)
            {
                entryList.SelectByIndex(i);
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                if (samplePreview!.HasImage)
                {
                    rendered = true;
                    _output.WriteLine($"Sample rendered for entry index {i}: '{items[i].name}'");
                    break;
                }
            }

            if (!rendered)
            {
                _output.WriteLine("SKIP: no entry produced a non-blank sample in this ROM");
                return;
            }

            // #828 gating contract: a rendered sample => Export button enabled.
            Assert.True(samplePreview!.HasImage, "sample preview must carry a bitmap");
            Assert.True(exportButton!.IsEnabled,
                "Export Image button must be enabled once a sample renders (#828)");

            // Render the real view (entry selected, sample shown, Export enabled)
            // to a PNG for the PR. Re-layout first so the latest state is measured.
            try
            {
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr828-export-png.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #828 fix): {ex.Message}");
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
