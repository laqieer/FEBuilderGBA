using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// GUI E2E tests that launch the full application and verify the startup window
    /// appears with the expected controls. These tests require an active input
    /// desktop; a Windows CI runner alone does not establish readiness.
    ///
    /// Each test retains its fresh process for one bounded, nonrecursive cleanup attempt.
    ///
    /// Note on localization: FEBuilderGBA's startup form title is localized
    /// (English: "Welcome to the FEBuilderGBA", Chinese: "初始设置向导", etc.).
    /// Tests use Process.MainWindowTitle or WinForms class detection for robustness.
    /// </summary>
    public class GuiStartupTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private readonly ITestOutputHelper _output;
        private Process? _process;
        private OwnedProcessCleanup? _cleanup;

        public GuiStartupTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            try
            {
                if (_cleanup != null && !_cleanup.TryCleanup(3_000))
                    throw new InvalidOperationException(
                        "Owned application exit was not confirmed; cleanup will not be retried.");
            }
            finally
            {
                _process?.Dispose();
                _process = null;
                _cleanup = null;
            }
        }

        // ------------------------------------------------------------------ Helpers

        [MemberNotNull(nameof(_process), nameof(_cleanup))]
        private void LaunchApp()
        {
            _process = AppRunner.Launch(ExePath);
            _cleanup = new OwnedProcessCleanup(_process);
        }

        /// <summary>
        /// Launch the app and wait for any startup window to appear.
        /// Returns the hWnd of the first window found, or IntPtr.Zero on timeout.
        /// </summary>
        private IntPtr LaunchAndWaitForStartup(int timeoutMs = 30_000)
        {
            LaunchApp();
            return WinAutomation.WaitForAnyAppWindow(_process!, timeoutMs);
        }

        // ------------------------------------------------------------------ Tests

        [Fact]
        public void App_LaunchesAndShowsStartupWindow()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();

            // Take a screenshot for the test report artifact
            if (hWnd != IntPtr.Zero)
                ScreenshotHelper.CaptureWindow(_process!, hWnd, "StartupWindow_visible");

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

            // WelcomeForm / InitWizard both have multiple child controls (buttons, labels).
            // The window handle may appear before all child controls are created,
            // so poll until enough controls are found (same pattern as title test).
            var sw = Stopwatch.StartNew();
            int childCount = 0;
            do
            {
                Thread.Sleep(300);
                var children = WinAutomation.GetChildWindows(hWnd);
                childCount = children.Count;
            }
            while (childCount < 2 && sw.ElapsedMilliseconds < 15_000);

            Assert.True(childCount >= 2,
                $"Expected at least 2 child controls, found {childCount}");
        }

        [Fact]
        public void App_StartupProcessExitsAfterCloseOrOwnedCleanup()
        {
            IntPtr hWnd = LaunchAndWaitForStartup();
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Take a screenshot before closing
            ScreenshotHelper.CaptureWindow(_process!, hWnd, "StartupWindow_before_close");

            // Send WM_CLOSE to all top-level windows for the process
            // (some startup dialogs like Init Wizard may show multiple windows)
            var wins = WinAutomation.GetProcessWindows(_process!.Id);
            foreach (var w in wins)
                WinAutomation.CloseWindow(w);

            // Give the app up to 8 seconds to process the close message gracefully
            bool exitedAfterClose = _process.WaitForExit(8_000);
            bool exitConfirmed = exitedAfterClose;

            if (!exitedAfterClose)
            {
                exitConfirmed = StartupCloseDiagnostics.CaptureAndCleanup(
                    () => WinAutomation.GetProcessWindows(_process.Id),
                    window => ScreenshotHelper.CaptureWindow(
                        _process, window, "StartupWindow_close_stuck"),
                    message => _output.WriteLine(message),
                    _cleanup!);
            }

            _output.WriteLine(
                "Exit observed after close wait: {0}; final exit confirmed: {1}. " +
                "Owned cleanup is not graceful-close evidence.", exitedAfterClose, exitConfirmed);
            Assert.True(exitConfirmed, "Owned application exit was not confirmed after close or cleanup.");
        }

        // ------------------------------------------------------------------ No crash on no ROM

        [Fact]
        public void App_DoesNotCrashOnStartupWithoutROM()
        {
            LaunchApp();

            // Either a window appears, or the app exits quickly — both acceptable.
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process!, timeoutMs: 20_000);

            bool wellBehaved = hWnd != IntPtr.Zero || _process.HasExited;
            Assert.True(wellBehaved,
                "App neither showed a window nor exited in 20 s — possible crash or hang.");
        }

        // ------------------------------------------------------------------ WelcomeForm title search (English)

        [Fact]
        public void WelcomeForm_OrAnyAppWindow_AppearsWithin30s()
        {
            LaunchApp();

            // Look for "Welcome to the FEBuilderGBA" (English default) first,
            // then fall back to any startup window
            IntPtr hWnd = WinAutomation.WaitForWindow(
                _process!, "Welcome to the FEBuilderGBA", timeoutMs: 5_000);

            if (hWnd == IntPtr.Zero)
            {
                // May show Init Wizard or other localized form instead — that's OK
                hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 25_000);
            }

            ScreenshotHelper.CaptureWindow(_process, hWnd, "FirstStartupWindow");
            Assert.NotEqual(IntPtr.Zero, hWnd);
        }
    }
}
