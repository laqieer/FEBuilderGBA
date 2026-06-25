// SPDX-License-Identifier: GPL-3.0-or-later
// #1414 PR proof — render the MainWindow "AI Scripts" expander showing that the
// five standalone AI sub-editor buttons (AI ASM Call / AI Coordinate / AI Range /
// AI Tiles / AI Units) are GONE, while the safe AI editors remain.
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default output
// is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR
// screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AISubEditorMainMenuScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public AISubEditorMainMenuScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void AIScriptsExpander_OmitsRemovedSubEditorButtons_SavesScreenshot()
        {
            var window = new MainWindow();

            // Data-layer proof of the fix: the five removed standalone buttons no
            // longer exist as named controls, while the safe AI editors remain.
            string[] removed = { "AIASMCallButton", "AICoordinateButton", "AIRangeButton", "AITilesButton", "AIUnitsButton" };
            foreach (string name in removed)
                Assert.Null(window.FindControl<Button>(name));

            string[] remaining = { "AIScriptButton", "AIMapSettingButton", "AITargetButton", "AOERangeButton" };
            foreach (string name in remaining)
                Assert.NotNull(window.FindControl<Button>(name));

            // Expand the AI Scripts panel so the screenshot shows the button row.
            var expander = window.FindControl<Expander>("AIScriptsExpander");
            if (expander != null) expander.IsExpanded = true;

            // Best-effort visual proof (headless render may no-op in some
            // environments — we don't assert the PNG, mirroring the other
            // screenshot tests).
            try
            {
                const int W = 1100, H = 800;
                window.Measure(new Size(W, H));
                window.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(window);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1414-ai-subeditors-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1414 fix): {ex.Message}");
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
