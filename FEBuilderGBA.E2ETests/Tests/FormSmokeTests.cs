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
    /// Smoke test: for each ROM, load the main form and click every toolbar button,
    /// verifying that at least one button opens a child form.
    ///
    /// WinForms button controls have Win32 class names starting with
    /// "WindowsForms10.BUTTON".  Each button is clicked via BM_CLICK; newly
    /// opened top-level windows are screenshotted and then closed.
    ///
    /// Tests are skipped automatically when the ROM file is not available.
    /// </summary>
    public class FormSmokeTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private readonly ITestOutputHelper _output;
        private Process? _process;

        public FormSmokeTests(ITestOutputHelper output)
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

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void AllToolbarButtons_OpenForms(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 60_000);
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Poll until the main form has rendered its toolbar buttons.
            // Check ALL process windows (not just the initial hWnd) because the OS may
            // set MainWindowHandle to a transient startup dialog before the editor shows.
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

            int opened = 0;
            foreach (var (btnHWnd, btnText) in buttons)
            {
                // Snapshot of top-level windows before the click
                var before = new HashSet<IntPtr>(WinAutomation.GetProcessWindows(_process.Id));

                WinAutomation.ClickButton(btnHWnd);
                Thread.Sleep(2_000);

                // Detect newly opened windows
                var after = WinAutomation.GetProcessWindows(_process.Id);
                var newWindows = after.Where(w => !before.Contains(w)).ToList();

                if (newWindows.Count > 0)
                {
                    opened++;
                    string safeText = string.Join("_",
                        btnText.Split(Path.GetInvalidFileNameChars()));
                    foreach (IntPtr nw in newWindows)
                    {
                        ScreenshotHelper.CaptureWindow(nw, $"Form_{romName}_{safeText}");
                        WinAutomation.CloseWindow(nw);
                    }
                    Thread.Sleep(500);
                }
            }

            _output.WriteLine($"{romName}: {opened}/{buttons.Count} buttons opened a form");

            // At least 1 button must open a form — proves the ROM was loaded into the
            // interactive main form (not the Init Wizard / welcome screen)
            Assert.True(opened >= 1,
                $"{romName}: expected at least 1 toolbar button to open a form, " +
                $"but 0 of {buttons.Count} buttons did so");
        }
    }
}
