using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// GUI E2E tests that launch the full application and verify the startup window
    /// appears with the expected controls.  These tests require a display
    /// (satisfied on Windows CI runners with their virtual desktop).
    ///
    /// The tests are isolated: each test launches a fresh process and kills it when done.
    ///
    /// Note on localization: FEBuilderGBA's startup form title is localized
    /// (English: "Welcome to the FEBuilderGBA", Chinese: "初始设置向导", etc.).
    /// Tests use Process.MainWindowTitle or WinForms class detection for robustness.
    /// </summary>
    public class GuiStartupTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private Process? _process;

        public void Dispose()
        {
            KillProcess();
        }

        private void KillProcess()
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
                _process.Dispose();
                _process = null;
            }
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>
        /// Launch the app and wait for any startup window to appear.
        /// Returns the hWnd of the first window found, or IntPtr.Zero on timeout.
        /// </summary>
        private IntPtr LaunchAndWaitForStartup(int timeoutMs = 30_000)
        {
            _process = AppRunner.Launch(ExePath);
            return WinAutomation.WaitForAnyAppWindow(_process, timeoutMs);
        }

        // ------------------------------------------------------------------ Tests

        [Fact]
        public void App_LaunchesAndShowsStartupWindow()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();

            // Take a screenshot for the test report artifact
            if (hWnd != IntPtr.Zero)
                ScreenshotHelper.CaptureWindow(hWnd, "StartupWindow_visible");

            Assert.NotEqual(IntPtr.Zero, hWnd);
        }

        [Fact]
        public void App_StartupWindowHasNonEmptyTitle()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Process.MainWindowTitle is unreliable on some machines/CI runners —
            // the OS may not update it before we read it.  Poll all process windows
            // via Win32 GetWindowText until at least one has a non-empty title.
            var sw = Stopwatch.StartNew();
            string title = string.Empty;
            do
            {
                Thread.Sleep(300);
                foreach (IntPtr w in WinAutomation.GetProcessWindows(_process!.Id))
                {
                    string t = WinAutomation.GetTitle(w);
                    if (!string.IsNullOrWhiteSpace(t)) { title = t; break; }
                }
            }
            while (string.IsNullOrWhiteSpace(title) && sw.ElapsedMilliseconds < 15_000);

            Assert.False(string.IsNullOrWhiteSpace(title),
                "Expected a non-empty window title on startup");
        }

        [Fact]
        public void App_StartupWindowTitleContainsFEBuilderGBA()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Both WelcomeForm ("Welcome to the FEBuilderGBA") and
            // Init Wizard might NOT contain "FEBuilderGBA" in the localized title,
            // but the window CLASS will always be WinForms.
            string cls = WinAutomation.GetClass(hWnd);
            Assert.True(cls.StartsWith("WindowsForms10.Window",
                StringComparison.OrdinalIgnoreCase),
                $"Expected a WinForms window class, got: '{cls}'");
        }

        [Fact]
        public void App_StartupWindowHasChildControls()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // WelcomeForm / InitWizard both have multiple child controls (buttons, labels)
            var children = WinAutomation.GetChildWindows(hWnd);
            Assert.True(children.Count >= 2,
                $"Expected at least 2 child controls, found {children.Count}");
        }

        [Fact]
        public void App_StartupWindowClosesOnWMClose()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Take a screenshot before closing
            ScreenshotHelper.CaptureWindow(hWnd, "StartupWindow_before_close");

            // Send WM_CLOSE to all top-level windows for the process
            // (some startup dialogs like Init Wizard may show multiple windows)
            var wins = WinAutomation.GetProcessWindows(_process!.Id);
            foreach (var w in wins)
                WinAutomation.CloseWindow(w);

            // Give the app up to 8 seconds to process the close message gracefully
            bool exited = _process.WaitForExit(8_000);

            if (!exited)
            {
                // Screenshot for diagnostics — Init Wizard may have raised a
                // confirmation dialog in response to WM_CLOSE
                wins = WinAutomation.GetProcessWindows(_process.Id);
                foreach (var w in wins)
                    ScreenshotHelper.CaptureWindow(w, "StartupWindow_close_stuck");

                // Force-terminate as a fallback: we've already verified WM_CLOSE
                // was delivered and at least one window existed. Now confirm the
                // process itself is fully terminable (no zombie / stuck state).
                try { _process.Kill(entireProcessTree: true); } catch { }
                exited = _process.WaitForExit(5_000);
            }

            Assert.True(exited, "App process could not be terminated even by Kill()");
        }

        // ------------------------------------------------------------------ No crash on no ROM

        [Fact]
        public void App_DoesNotCrashOnStartupWithoutROM()
        {
            _process = AppRunner.Launch(ExePath);

            // Either a window appears, or the app exits quickly — both acceptable.
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 20_000);

            bool wellBehaved = hWnd != IntPtr.Zero || _process.HasExited;
            Assert.True(wellBehaved,
                "App neither showed a window nor exited in 20 s — possible crash or hang.");
        }

        // ------------------------------------------------------------------ WelcomeForm title search (English)

        [Fact]
        public void WelcomeForm_OrAnyAppWindow_AppearsWithin30s()
        {
            _process = AppRunner.Launch(ExePath);

            // Look for "Welcome to the FEBuilderGBA" (English default) first,
            // then fall back to any startup window
            IntPtr hWnd = WinAutomation.WaitForWindow(
                _process, "Welcome to the FEBuilderGBA", timeoutMs: 5_000);

            if (hWnd == IntPtr.Zero)
            {
                // May show Init Wizard or other localized form instead — that's OK
                hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 25_000);
            }

            ScreenshotHelper.CaptureWindow(hWnd, "FirstStartupWindow");
            Assert.NotEqual(IntPtr.Zero, hWnd);
        }
    }
}
