// SPDX-License-Identifier: GPL-3.0-or-later
// #1467 PR proof — render the real LogViewerView showing actual application-log
// lines instead of the old dummy address-list placeholder (a single addr-0 list
// entry + an "Address: 0x00000000" label and nothing else). The fixed viewer
// surfaces Log.LogToString() in a read-only multiline TextBox with Refresh /
// Save / Copy / Open-log-folder buttons.
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default
// output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class LogViewerScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly string _savedBaseDir;
        readonly string _tempDir;

        public LogViewerScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _savedBaseDir = CoreState.BaseDirectory;
            _tempDir = Path.Combine(Path.GetTempPath(), "febuilder_logview_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            CoreState.BaseDirectory = _tempDir;
        }

        public void Dispose()
        {
            CoreState.BaseDirectory = _savedBaseDir;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [AvaloniaFact]
        public void LogViewerView_SurfacesRealLogContent_SavesScreenshot()
        {
            // Seed the log with known lines the viewer must surface.
            string marker = "#1467 Log Viewer wired to the real application log " + Guid.NewGuid().ToString("N");
            Log.Notify(marker);
            Log.Notify("N: ROM loaded OK");
            Log.Error("E: example error line for the screenshot");
            Log.SyncLog();

            var view = new LogViewerView();

            // Drive the same load path the View's Opened handler uses so the
            // bound TextBox is populated (headless windows do not raise Opened).
            var vm = Assert.IsType<LogViewerViewModel>(view.DataViewModel);
            vm.Refresh();

            // Data-layer assertions — the editor is functional, NOT the stub.
            Assert.True(view.IsLoaded);
            Assert.False(string.IsNullOrEmpty(vm.LogText), "LogText is empty — the viewer is not reading the log.");
            Assert.Contains(marker, vm.LogText);
            Assert.EndsWith(Path.Combine("config", "log", "log.txt"), vm.LogFilePath);

            // The bound TextBox actually shows the log text (proves the binding,
            // not just the VM).
            var logBox = view.FindControl<TextBox>("LogTextBox");
            Assert.NotNull(logBox);

            // Measure/Arrange the real visual tree (catches XAML binding/format
            // faults). The PNG Save is best-effort under UseHeadlessDrawing.
            const int VW = 800, VH = 560;
            view.Measure(new Size(VW, VH));
            view.Arrange(new Rect(0, 0, VW, VH));
            Assert.Contains(marker, logBox!.Text ?? string.Empty);

            try
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(VW, VH));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1467-logviewer-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless PNG save no-op (UseHeadlessDrawing, not the #1467 fix): {ex.Message}");
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
