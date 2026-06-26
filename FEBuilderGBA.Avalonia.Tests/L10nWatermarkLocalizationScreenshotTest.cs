// SPDX-License-Identifier: GPL-3.0-or-later
// #1468 PR proof + regression — load the real Japanese translation table and
// apply it to the RAM Rewrite Tool (MAP) view, proving the three hex watermark
// example literals (`e.g. 0x01`, `e.g. 0x02000000`, `e.g. 0xFF`) now localize to
// `例: 0x…` instead of staying hardcoded English. The same ja.txt/zh.txt entries
// cover the other 5-view watermarks flagged by the localisation sweep
// (MoveToFreeSpace / Options / RAMRewriteTool / PointerTool).
//
// The FUNCTIONAL assertions (watermark text == translated value) are the
// authoritative proof; the PNG capture is wrapped in try/catch because the
// UseHeadlessDrawing CI environment can yield a blank/null frame. Set
// FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR screenshot.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class L10nWatermarkLocalizationScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public L10nWatermarkLocalizationScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void RAMRewriteToolMAP_HexWatermarks_LocalizeToJapanese_SavesScreenshot()
        {
            string repoRoot = FindRepoRoot();
            string jaPath = Path.Combine(repoRoot, "config", "translate", "ja.txt");
            Assert.True(File.Exists(jaPath), $"ja.txt not found at {jaPath}");

            // Load the REAL shipped Japanese translation table (the file this PR
            // edits) so the lookup exercises the new dictionary entries.
            MyTranslateResource.LoadResource(jaPath);
            try
            {
                // Sanity: the new entries resolve through the runtime translator.
                Assert.Equal("例: 0x01", R._("e.g. 0x01"));
                Assert.Equal("例: 0x02000000", R._("e.g. 0x02000000"));
                Assert.Equal("例: 0xFF", R._("e.g. 0xFF"));

                var view = new RAMRewriteToolMAPView();

                var mapIdBox = view.FindControl<TextBox>("MapIdTextBox");
                var addressBox = view.FindControl<TextBox>("AddressTextBox");
                var valueBox = view.FindControl<TextBox>("ValueTextBox");
                Assert.NotNull(mapIdBox);
                Assert.NotNull(addressBox);
                Assert.NotNull(valueBox);

                // Apply the same translation pass the runtime TranslatedWindow does.
                new ViewTranslationHelper(view).TranslateAll();

                // The three hex watermarks must now read the Japanese form.
                Assert.Equal("例: 0x01", mapIdBox!.Watermark);
                Assert.Equal("例: 0x02000000", addressBox!.Watermark);
                Assert.Equal("例: 0xFF", valueBox!.Watermark);

                const int W = 720;
                const int H = 360;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1468-ramrewritemap-ja.png");

                try
                {
                    view.Show();
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    using var frame = view.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    HeadlessScreenshotHelper.SaveFramePng(frame!, outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless capture no-op (environment, not the #1468 fix): {ex.Message}");
                }
            }
            finally
            {
                // Restore shared translator state for other tests in the collection.
                MyTranslateResource.Clear();
            }
        }

        static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
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
