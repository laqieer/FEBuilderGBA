using System;
using System.IO;
using System.Linq;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests for the WinForms --screenshot-all CLI flag.
    /// Captures all WinForms editors via DrawToBitmap with filenames matching
    /// the Avalonia convention (WinForms_{ViewName}_{RomVersion}.png)
    /// for side-by-side comparison.
    /// </summary>
    public class WinFormsScreenshotAllCliTests
    {
        /// <summary>
        /// Captures screenshots of all WinForms editors via --screenshot-all.
        /// Verifies at least 50 PNG files are created.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void WinForms_ScreenshotAllCli_CapturesEditors(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            string exePath = AppRunner.FindExePath();
            string screenshotDir = Path.Combine(
                ScreenshotHelper.OutputDirectory, $"winforms_cli_{romName}");
            if (Directory.Exists(screenshotDir))
                Directory.Delete(screenshotDir, true);

            var (exitCode, stdout, stderr) = AppRunner.Run(
                exePath,
                $"--rom \"{romPath}\" --screenshot-all --screenshot-dir=\"{screenshotDir}\"",
                timeoutMs: 300_000);

            // Verify screenshots were created
            Assert.True(Directory.Exists(screenshotDir),
                $"Screenshot directory was not created: {screenshotDir}\nStdout: {stdout}\nStderr: {stderr}");

            var pngFiles = Directory.GetFiles(screenshotDir, "WinForms_*.png");
            Assert.True(pngFiles.Length >= 50,
                $"{romName}: Expected at least 50 WinForms screenshots but got {pngFiles.Length}.\n" +
                $"Stdout: {stdout}\nStderr: {stderr}");
        }

        /// <summary>
        /// Verifies stdout contains the SCREENSHOT: Results summary line.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void WinForms_ScreenshotAllCli_OutputContainsResults(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            string exePath = AppRunner.FindExePath();
            string screenshotDir = Path.Combine(
                ScreenshotHelper.OutputDirectory, $"winforms_cli_results_{romName}");

            var (exitCode, stdout, stderr) = AppRunner.Run(
                exePath,
                $"--rom \"{romPath}\" --screenshot-all --screenshot-dir=\"{screenshotDir}\"",
                timeoutMs: 300_000);

            Assert.Contains("SCREENSHOT: Results:", stdout);
            Assert.Contains("SCREENSHOT: Capturing", stdout);
        }
    }
}
