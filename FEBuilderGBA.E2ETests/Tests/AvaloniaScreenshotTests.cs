using System;
using System.IO;
using System.Linq;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests that capture PNG screenshots of all Avalonia GUI editor forms.
    /// Uses --screenshot-all flag to iterate through every editor and save screenshots
    /// via RenderTargetBitmap. Screenshots are saved as CI artifacts for visual regression.
    /// </summary>
    public class AvaloniaScreenshotTests
    {
        private static readonly string? ExePath = AvaloniaAppRunner.FindExePath();

        /// <summary>
        /// Captures screenshots of all editors via --screenshot-all.
        /// Verifies at least 100 PNG files are created (of 323 total editors).
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_ScreenshotAll_CapturesEditors(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            string screenshotDir = Path.Combine(
                ScreenshotHelper.OutputDirectory, $"avalonia_{romName}");
            if (Directory.Exists(screenshotDir))
                Directory.Delete(screenshotDir, true);

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!,
                $"--rom \"{romPath}\" --screenshot-all --screenshot-dir=\"{screenshotDir}\"",
                timeoutMs: 300_000);

            // Verify screenshots were created
            Assert.True(Directory.Exists(screenshotDir),
                $"Screenshot directory was not created: {screenshotDir}\nStdout: {stdout}\nStderr: {stderr}");

            var pngFiles = Directory.GetFiles(screenshotDir, "*.png");
            Assert.True(pngFiles.Length >= 100,
                $"{romName}: Expected at least 100 screenshots but got {pngFiles.Length}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");

            Assert.True(exitCode == 0,
                $"{romName}: Screenshot capture failed with exit code {exitCode}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }

        /// <summary>
        /// Verifies stdout contains the SCREENSHOT: Results summary line.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void Avalonia_ScreenshotAll_OutputContainsResults(string romName, string? romPath)
        {
            Skip.If(ExePath == null, "Avalonia exe not found — build FEBuilderGBA.Avalonia first");
            Skip.If(romPath == null, $"{romName} ROM not available");

            string screenshotDir = Path.Combine(
                ScreenshotHelper.OutputDirectory, $"avalonia_results_{romName}");

            var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                ExePath!,
                $"--rom \"{romPath}\" --screenshot-all --screenshot-dir=\"{screenshotDir}\"",
                timeoutMs: 300_000);

            Assert.Contains("SCREENSHOT: Results:", stdout);
            Assert.Contains("SCREENSHOT: Capturing", stdout);
        }
    }
}
