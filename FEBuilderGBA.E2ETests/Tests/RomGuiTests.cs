using System;
using System.Diagnostics;
using System.Threading;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// GUI tests that load each real ROM into the full application and verify
    /// the main editor form appears and is populated.
    ///
    /// Tests are skipped automatically when the ROM file is not available.
    /// Each test launches a fresh process and kills it in Dispose().
    /// </summary>
    public class RomGuiTests : IDisposable
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private Process? _process;

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

        // ------------------------------------------------------------------ window appears

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void App_LoadsRomAndShowsMainForm(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 30_000);

            if (hWnd != IntPtr.Zero)
                ScreenshotHelper.CaptureWindow(hWnd, $"RomLoad_{romName}");

            Assert.NotEqual(IntPtr.Zero, hWnd);
        }

        // ------------------------------------------------------------------ title non-empty

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void MainForm_HasNonEmptyTitle(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 30_000);
            Assert.NotEqual(IntPtr.Zero, hWnd);

            _process!.Refresh();
            Assert.False(string.IsNullOrWhiteSpace(_process.MainWindowTitle),
                $"{romName}: expected non-empty window title after ROM load");
        }

        // ------------------------------------------------------------------ child controls

        [SkippableTheory]
        [MemberData(nameof(RomLocator.AllRoms), MemberType = typeof(RomLocator))]
        public void MainForm_HasManyChildControls(string romName, string? romPath)
        {
            Skip.If(romPath == null, $"{romName} ROM not available");

            _process = AppRunner.Launch(ExePath, $"--rom \"{romPath}\"");
            IntPtr hWnd = WinAutomation.WaitForAnyAppWindow(_process, timeoutMs: 30_000);
            Assert.NotEqual(IntPtr.Zero, hWnd);

            // Give the main form time to fully populate its controls
            Thread.Sleep(2_000);

            var children = WinAutomation.GetChildWindows(hWnd);
            Assert.True(children.Count >= 10,
                $"{romName}: expected at least 10 child controls (main form vs wizard), " +
                $"found {children.Count}");
        }
    }
}
