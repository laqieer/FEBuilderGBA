// #1380 Part A — render the FE-Repo Resource Browser in its empty-submodule
// (NotFound) state to a PNG so the actionable "run git submodule update"
// message + the "Copy git command" button are visible as PR proof.
//
// Headless render — works on locked machines and in CI. Default output is a
// per-test temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class FERepoBrowserNotFoundScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public FERepoBrowserNotFoundScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void EmptySubmodule_ShowsActionableMessage_SavesScreenshot()
        {
            // Point BaseDirectory at a dir whose resources/FE-Repo is an EMPTY
            // placeholder (uninitialized submodule), reproducing the #1380 bug.
            string baseDir = Path.Combine(Path.GetTempPath(), "febuilder-ferepo-shot-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(baseDir, "resources", "FE-Repo")); // empty
            string prev = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;

                var window = new FERepoResourceBrowserWindow();
                var vm = (FERepoResourceBrowserViewModel)window.DataContext!;

                // Data-layer proof of the fix (the meaningful assertions).
                Assert.True(vm.NotFound);
                Assert.Contains(FERepoResourceBrowserViewModel.SubmoduleInitCommand, vm.StatusText);

                // Best-effort visual proof (headless render may be a no-op in
                // some environments — we don't assert the PNG, mirroring
                // ItemShopFirstIconScreenshotTest).
                try
                {
                    const int W = 900, H = 600;
                    window.Measure(new Size(W, H));
                    window.Arrange(new Rect(0, 0, W, H));
                    using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                    bitmap.Render(window);

                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, "pr1380-ferepo-empty-message.png");
                    bitmap.Save(outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless render failed (environment, not the #1380 fix): {ex.Message}");
                }
            }
            finally
            {
                CoreState.BaseDirectory = prev;
                Directory.Delete(baseDir, true);
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
