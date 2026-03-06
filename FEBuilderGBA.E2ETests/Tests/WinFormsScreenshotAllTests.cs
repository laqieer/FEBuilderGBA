using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Comprehensive screenshot test: for each ROM, load the main form and click every
    /// toolbar button, capturing screenshots of the main window and all child forms.
    ///
    /// Screenshots are saved as PNG artifacts for visual regression tracking.
    /// Follows the same Win32 automation pattern as FormSmokeTests.cs.
    /// </summary>
    public class WinFormsScreenshotAllTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private readonly ITestOutputHelper _output;
        private Process? _process;

        private const int ButtonLoopTimeoutMs = 120_000; // 2 minutes per ROM

        public WinFormsScreenshotAllTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3_000);
            }
            catch { }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
        }

        /// <summary>
        /// Captures screenshots of the main form and all editor forms opened via toolbar buttons.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void WinForms_ScreenshotAll_CapturesMainAndEditorForms(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 60_000);
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Wait for main form to fully load, then close unexpected dialogs
            Thread.Sleep(2_000);
            var mainWindows = new HashSet<IntPtr>(WinAutomation.GetProcessWindows(_process.Id));
            WinAutomation.CloseUnexpectedWindows(_process.Id, mainWindows);
            Thread.Sleep(1_000);

            // Screenshot the main window
            int screenshotCount = 0;
            string? mainScreenshot = ScreenshotHelper.CaptureWindow(hWnd, $"WinForms_{romName}_MainForm");
            if (mainScreenshot != null)
            {
                screenshotCount++;
                _output.WriteLine($"{romName}: Main form screenshot: {mainScreenshot}");
            }

            // Poll for toolbar buttons
            var sw = Stopwatch.StartNew();
            List<(IntPtr hWnd, string Title)> buttons = new();
            do
            {
                Thread.Sleep(500);
                foreach (IntPtr w in WinAutomation.GetProcessWindows(_process.Id))
                {
                    var candidates = WinAutomation.GetChildWindows(w)
                        .Where(c => WinAutomation.GetClass(c.hWnd)
                            .StartsWith("WindowsForms10.BUTTON", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(c.Title))
                        .ToList();
                    if (candidates.Count > buttons.Count)
                        buttons = candidates;
                }
            }
            while (buttons.Count == 0 && sw.ElapsedMilliseconds < 30_000);

            _output.WriteLine($"{romName}: found {buttons.Count} toolbar buttons");

            // Click each button and capture screenshots of opened forms
            var loopSw = Stopwatch.StartNew();
            foreach (var (btnHWnd, btnText) in buttons)
            {
                if (loopSw.ElapsedMilliseconds > ButtonLoopTimeoutMs)
                {
                    _output.WriteLine($"{romName}: button loop timeout ({ButtonLoopTimeoutMs}ms) — " +
                        $"stopping after {screenshotCount} screenshots");
                    break;
                }

                var before = new HashSet<IntPtr>(WinAutomation.GetProcessWindows(_process.Id));

                WinAutomation.ClickButton(btnHWnd);
                Thread.Sleep(500);

                var after = WinAutomation.GetProcessWindows(_process.Id);
                var newWindows = after.Where(w => !before.Contains(w)).ToList();

                if (newWindows.Count > 0)
                {
                    string safeText = string.Join("_",
                        btnText.Split(Path.GetInvalidFileNameChars()));
                    foreach (IntPtr nw in newWindows)
                    {
                        string? path = ScreenshotHelper.CaptureWindow(nw, $"WinForms_{romName}_{safeText}");
                        if (path != null)
                        {
                            screenshotCount++;
                            _output.WriteLine($"{romName}: {safeText} screenshot: {path}");
                        }
                        WinAutomation.CloseWindow(nw);
                    }
                    Thread.Sleep(500);
                }

                WinAutomation.CloseUnexpectedWindows(_process.Id, before);
            }

            _output.WriteLine($"{romName}: total screenshots captured: {screenshotCount}");

            // We should capture at least the main form + a few editor forms
            Assert.True(screenshotCount >= 10,
                $"{romName}: expected at least 10 screenshots but got {screenshotCount}");
        }

        /// <summary>
        /// Verifies the main form screenshot is captured successfully.
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void WinForms_ScreenshotAll_MainFormCaptured(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 60_000);
            Assert.NotEqual(IntPtr.Zero, hWnd);

            Thread.Sleep(2_000);

            string? screenshot = ScreenshotHelper.CaptureWindow(hWnd, $"WinForms_{romName}_MainForm_verify");
            Assert.NotNull(screenshot);
            Assert.True(File.Exists(screenshot), $"Screenshot file not found: {screenshot}");

            // Verify it's a valid PNG (at least a few KB)
            var fileInfo = new FileInfo(screenshot);
            Assert.True(fileInfo.Length > 1024,
                $"Screenshot file too small ({fileInfo.Length} bytes), likely empty: {screenshot}");
        }
    }
}
