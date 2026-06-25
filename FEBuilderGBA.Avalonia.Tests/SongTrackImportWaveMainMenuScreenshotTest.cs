// SPDX-License-Identifier: GPL-3.0-or-later
// #1447 PR proof — render the MainWindow "Audio (Advanced)" expander showing that
// the dead standalone "Import Wave" button is GONE, while the sibling audio
// buttons remain (the real WAV->song flow lives in the "Song Track" editor via
// SongTrackWaveImportCore).
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default output
// is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR
// screenshot into the repo's pr-screenshots/. Mirrors AISubEditorMainMenuScreenshotTest.
using System;
using System.IO;
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
    public class SongTrackImportWaveMainMenuScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public SongTrackImportWaveMainMenuScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void AudioAdvancedExpander_OmitsRemovedImportWaveButton_SavesScreenshot()
        {
            var window = new MainWindow();

            // Data-layer proof of the fix: the removed standalone "Import Wave"
            // button no longer exists as a named control, while the sibling audio
            // editors remain.
            Assert.Null(window.FindControl<Button>("ImportWaveButton"));

            string[] remaining =
            {
                "SongTrackButton", "InstrumentButton", "DirectSoundButton",
                "WaveImportButton", "MIDIImportButton", "SongExchangeButton",
                "ChangeTrackButton", "AllChangeTrackButton", "SelectInstrumentButton",
            };
            foreach (string name in remaining)
                Assert.NotNull(window.FindControl<Button>(name));

            // Expand the Audio (Advanced) panel so the screenshot shows the button row.
            var expander = window.FindControl<Expander>("AudioAdvancedExpander");
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
                string outPath = Path.Combine(outDir, "pr1447-importwave-removed-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1447 fix): {ex.Message}");
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
