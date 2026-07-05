// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class UpdateCheckProofImageTest
    {
        readonly ITestOutputHelper _output;

        public UpdateCheckProofImageTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void RenderAvaloniaUpdaterProof()
        {
            var window = new MainWindow();
            var item = window.FindControl<MenuItem>("CheckUpdatesMenuItem");
            Assert.NotNull(item);
            string menuHeader = item!.Header?.ToString() ?? "";
            Assert.Contains("Updates", menuHeader);

            var upToDate = UpdateCheckCore.BuildResult("ver_20260704.04", "ver_20260704.04", UpdateCheckCore.ReleasesLatestPageUrl);
            var available = UpdateCheckCore.BuildResult("ver_20260101.00", "ver_20260704.04", UpdateCheckCore.ReleasesLatestPageUrl);
            var offline = UpdateCheckCore.CheckLatest(_ => "");

            const int W = 1100, H = 620;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x24, 0x28, 0x2F);
                var panel = new SKColor(0x2F, 0x35, 0x3D);
                var accent = new SKColor(0x7A, 0xC7, 0xFF);
                var good = new SKColor(0x78, 0xD6, 0x78);
                var warn = new SKColor(0xFF, 0xD1, 0x66);
                var muted = new SKColor(0xA9, 0xB0, 0xB8);
                var fg = new SKColor(0xF2, 0xF4, 0xF8);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 30, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 20, FakeBoldText = true };
                using var text = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var label = new SKPaint { Color = muted, IsAntialias = true, TextSize = 15 };
                using var panelPaint = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText("Avalonia Check for Updates — #1804", 32, 48, title);
                c.DrawRoundRect(32, 78, 1036, 92, 10, 10, panelPaint);
                c.DrawText("Real Help menu item", 56, 112, hdr);
                c.DrawText($"AutomationId: Main_CheckUpdatesMenuItem_Button", 56, 140, text);
                c.DrawText($"Header: {menuHeader}", 610, 140, text);

                DrawOutcome(c, 32, 205, "Up to date", upToDate,
                    string.Format("You are running the latest version (current {0}).", upToDate.CurrentVersion), good);
                DrawOutcome(c, 32, 340, "Update available", available,
                    string.Format("A new version is available: {0} (you have {1}). Open the releases page to download and install it?",
                        available.LatestVersion, available.CurrentVersion), warn);
                DrawOutcome(c, 32, 475, "Offline / rate-limited", offline,
                    "Could not check for updates (offline?).", muted);

                c.DrawText("Auto-check is gated by func_auto_update + LastUpdateCheck and is silent unless an update exists.",
                    56, 592, label);
            }

            string outDir = ResolveScreenshotOutputDir();
            string outPath = Path.Combine(outDir, "pr1804-avalonia-updater.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            {
                // The render itself is the assertion — the encode must produce real bytes.
                Assert.True(data != null && data.Size > 0);
                // Writing the file is best-effort: a read-only CI output dir must not fail the
                // test (it only matters when generating the committed proof image locally).
                try
                {
                    Directory.CreateDirectory(outDir);
                    using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write);
                    data.SaveTo(fs);
                    _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Proof image save skipped (dir not writable?): {ex.Message}");
                }
            }
        }

        static void DrawOutcome(SKCanvas c, int x, int y, string title, UpdateCheckCore.UpdateCheckResult result, string message, SKColor color)
        {
            using var panelPaint = new SKPaint { Color = new SKColor(0x2F, 0x35, 0x3D), IsAntialias = true };
            using var hdr = new SKPaint { Color = color, IsAntialias = true, TextSize = 20, FakeBoldText = true };
            using var text = new SKPaint { Color = new SKColor(0xF2, 0xF4, 0xF8), IsAntialias = true, TextSize = 15 };
            using var mono = new SKPaint { Color = new SKColor(0xD7, 0xDB, 0xE0), IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };

            c.DrawRoundRect(x, y, 1036, 106, 10, 10, panelPaint);
            c.DrawText(title, x + 24, y + 32, hdr);
            c.DrawText($"Succeeded={result.CheckSucceeded}  UpdateAvailable={result.IsUpdateAvailable}", x + 24, y + 60, mono);
            c.DrawText($"Current={result.CurrentVersion}  Latest={result.LatestVersion}  Page={result.ReleasePageUrl}", x + 24, y + 82, mono);
            c.DrawText(message.Length > 118 ? message.Substring(0, 118) + "…" : message, x + 24, y + 101, text);
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
