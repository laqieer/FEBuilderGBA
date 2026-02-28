using System;
using System.Collections.Generic;
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

            // The initial hWnd may appear before the title has been set.
            // Poll all process windows until at least one has a non-empty title.
            var titleSw = Stopwatch.StartNew();
            string title = string.Empty;
            do
            {
                Thread.Sleep(300);
                foreach (IntPtr w in WinAutomation.GetProcessWindows(_process.Id))
                {
                    string t = WinAutomation.GetTitle(w);
                    if (!string.IsNullOrWhiteSpace(t)) { title = t; break; }
                }
            }
            while (string.IsNullOrWhiteSpace(title) && titleSw.ElapsedMilliseconds < 30_000);

            Assert.False(string.IsNullOrWhiteSpace(title),
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

            // Poll ALL process windows for the one with the most children.
            // WaitForAnyAppWindow may return a transient splash window; the actual
            // main editor form may be a different top-level window.
            var sw = Stopwatch.StartNew();
            List<(IntPtr hWnd, string Title)> children = new();
            do
            {
                Thread.Sleep(500);
                foreach (IntPtr w in WinAutomation.GetProcessWindows(_process.Id))
                {
                    var c = WinAutomation.GetChildWindows(w);
                    if (c.Count > children.Count)
                        children = c;
                }
            }
            while (children.Count < 10 && sw.ElapsedMilliseconds < 30_000);

            Assert.True(children.Count >= 10,
                $"{romName}: expected at least 10 child controls (main form vs wizard), " +
                $"found {children.Count}");
        }
    }
}
