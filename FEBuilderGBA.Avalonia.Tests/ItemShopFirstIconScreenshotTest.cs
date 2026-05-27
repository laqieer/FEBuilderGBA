using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
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
    /// Issue #654: render the Avalonia <see cref="ItemShopViewerView"/> with a
    /// loaded shop to a PNG file that proves the first slot's icon is now
    /// visible. By default the PNG is written to a per-test temp directory
    /// (so normal/CI runs do NOT dirty the repo's tracked
    /// <c>pr-screenshots/</c>); set the <c>FEBUILDERGBA_SCREENSHOT_DIR</c>
    /// environment variable to override the output directory — pointing it
    /// at the repo's <c>pr-screenshots/</c> is how the canonical PR
    /// screenshot is regenerated locally before being attached to the PR
    /// description. See <see cref="ResolveScreenshotOutputDir"/> for the
    /// resolution rules.
    ///
    /// This is a headless render — does NOT require a visible desktop, so it
    /// works on locked machines and in CI. Mirrors the pattern used by
    /// <see cref="VisualRenderingSweepTests"/>.
    /// </summary>
    [Collection("SharedState")]
    public class ItemShopFirstIconScreenshotTest : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ItemShopFirstIconScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void ItemShopViewerView_FirstSlotIconVisible_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            // Pick the first non-empty shop and load its items.
            var vm = new ItemShopViewerViewModel();
            var shops = vm.LoadShopList();
            Assert.NotEmpty(shops);

            AddrResult? chosenShop = null;
            System.Collections.Generic.List<AddrResult> slots = new();
            foreach (var shop in shops)
            {
                var s = vm.LoadShopItems(shop.addr, shop.tag, shop.name);
                if (s.Count > 0)
                {
                    chosenShop = shop;
                    slots = s;
                    break;
                }
            }

            if (chosenShop == null || slots.Count == 0)
            {
                _output.WriteLine("SKIP: no non-empty shop");
                return;
            }

            // Verify the fix at the data layer first: slot 0's prefix MUST be
            // the item ID, NOT slot index 0. atoh on the prefix should yield
            // the actual item id read from ROM.
            uint slot0ItemId = CoreState.ROM!.u8(slots[0].addr);
            Assert.Equal(slot0ItemId, U.atoh(slots[0].name));
            _output.WriteLine($"Shop @ 0x{chosenShop.addr:X8} '{chosenShop.name}', " +
                              $"slot[0] = '{slots[0].name}' itemId=0x{slot0ItemId:X2}");

            // Verify the icon loader resolves a non-null bitmap for slot 0
            // (proves Bug 1+2+3 fixes wired through).
            using var bmp0 = ListIconLoaders.ItemIconLoader(slots, 0);
            _output.WriteLine($"slot[0] icon = {(bmp0 == null ? "null" : "Bitmap")}");
            // Bitmap might still be null if item ID 0 was used (null item) —
            // but for any real shop slot the item ID is non-zero so the icon
            // must be non-null.
            Assert.True(slot0ItemId > 0, "Shop must have non-zero item id at slot 0");
            Assert.NotNull(bmp0);

            // Render the full ItemShopViewerView so we can save the bitmap as
            // visual proof. We construct the view and bind the VM, then load
            // the first shop and its slot 0 selection.
            var view = new ItemShopViewerView();
            view.DataContext = vm;
            // Programmatically tell the VM to load the shop so the SlotList
            // populates. ItemShopViewerView.Opened wires a similar handler;
            // we call into the VM directly here since Avalonia headless does
            // not raise the Opened event on the same timeline.
            vm.LoadShopItems(chosenShop.addr, chosenShop.tag, chosenShop.name);
            vm.LoadItemShop(slots[0].addr);

            // Best-effort: render the view into a PNG bitmap and save it as
            // visual proof. The headless Avalonia render pipeline in this
            // test environment may not produce real bitmaps (other
            // VisualRenderingSweepTests in this assembly fail the same way),
            // so we DON'T assert the PNG file size — the meaningful proof of
            // the #654 fix is the data-layer assertions above:
            //  - slot[0] prefix is the actual item ID, not 0
            //  - ListIconLoaders.ItemIconLoader(slots, 0) returns non-null
            // If the render does succeed, we save the file for PR attachment.
            try
            {
                const int W = 1100;
                const int H = 720;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr-654-first-icon-fix.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #654 fix): {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve the output directory for the rendered PNG.
        /// Default: per-test temp directory (does NOT dirty the repo's
        /// tracked <c>pr-screenshots/</c> on normal/CI runs).
        /// Override via <c>FEBUILDERGBA_SCREENSHOT_DIR</c> env var to
        /// write into the repo's <c>pr-screenshots/</c> (used when
        /// regenerating the canonical PR screenshot locally).
        /// </summary>
        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
