// SPDX-License-Identifier: GPL-3.0-or-later
// #1913 PR proof — render the Event Unit Placement editor at a WIDE width with the
// FE8 After-Coordinate move-path panel (1094px grid) visible, so the detail panel
// is shown AND its content overflows the detail column, exercising the new
// HorizontalScrollBarVisibility="Auto" on the detail ScrollViewer. The render/save
// is best-effort on UseHeadlessDrawing (wrapped in try/catch, per the repo's other
// *ScreenshotTest files); the layout assertions outside the catch are the real gate.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventUnitDetailScrollScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public EventUnitDetailScrollScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void EventUnitView_WideWidth_DetailScrollEngages_SavesScreenshot()
        {
            var v = new EventUnitView();
            var panel = v.FindControl<Control>("AfterCoordsPanel");
            Assert.NotNull(panel);

            const int VW = 1400, VH = 950;
            // Set the window size explicitly (overrides the view's SizeToContent) so
            // the detail column (~VW-700 list columns) is narrower than the visible
            // 1094px move-path grid, forcing the detail ScrollViewer to scroll.
            v.Width = VW;
            v.Height = VH;
            v.Show();
            try
            {
                // Force the FE8 move-path panel visible AFTER Show so the view's
                // Show-time UI sync (which can re-hide it when a ROM is loaded) can't
                // flip it back — keeps the overflow measurement deterministic (#1913 review).
                panel!.IsVisible = true;
                v.UpdateLayout();

                // Enforced (outside the render try/catch): the detail ScrollViewer
                // allows horizontal scrolling and its content overflows the viewport.
                var sv = v.FindControl<ScrollViewer>("DetailScrollViewer");
                Assert.NotNull(sv);
                Assert.Equal(ScrollBarVisibility.Auto, sv!.HorizontalScrollBarVisibility);
                Assert.True(sv.Extent.Width > sv.Viewport.Width,
                    $"detail content ({sv.Extent.Width:F0}) should overflow the viewport ({sv.Viewport.Width:F0})");

                // Best-effort PNG (no-op under UseHeadlessDrawing) — PR proof artifact.
                try
                {
                    using var bmp = new RenderTargetBitmap(new PixelSize(VW, VH));
                    bmp.Render(v);
                    string outDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR")
                        ?? Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, "pr1913-eventunit-hscroll.png");
                    bmp.Save(outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless PNG save no-op (UseHeadlessDrawing): {ex.Message}");
                }
            }
            finally
            {
                v.Close();
            }
        }
    }
}
