// SPDX-License-Identifier: GPL-3.0-or-later
// #1431 PR proof — render the real AOERANGEView with a loaded AoE record so the
// screenshot shows the manual address bar, the 4 header spinners, and the
// dynamic w×h grid of decimal byte cells (0..255; center cell highlighted). The
// previous stub had a single addr-0 list entry and NO grid.
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default
// output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using global::Avalonia;
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
    public class AOERANGEScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _savedRom;

        public AOERANGEScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _savedRom = CoreState.ROM;
        }

        public void Dispose() => CoreState.ROM = _savedRom;

        [AvaloniaFact]
        public void AOERANGEView_ShowsLoadedGrid_SavesScreenshot()
        {
            // Synthetic ROM with a 5×5 AoE record centered at (2,2) — a diamond
            // mask so the grid clearly has populated values + a highlighted center.
            var rom = new ROM();
            rom.LoadLow("aoe-1431-shot.gba", new byte[0x200000], "NAZO");
            CoreState.ROM = rom;

            uint addr = 0x1000;
            const int W = 5, H = 5;
            rom.Data[addr + 0] = W;
            rom.Data[addr + 1] = H;
            rom.Data[addr + 2] = 2; // center X
            rom.Data[addr + 3] = 2; // center Y
            // Diamond pattern (|dx|+|dy| <= 2) → 1, else 0.
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int d = Math.Abs(x - 2) + Math.Abs(y - 2);
                rom.Data[addr + 4 + (uint)(x + y * W)] = (byte)(d <= 2 ? 1 : 0);
            }

            var view = new AOERANGEView();
            view.NavigateTo(addr); // load the record → header + grid populate.

            // Data-layer render coverage (ENFORCED — these assert the real load/grid
            // path the screenshot depicts; a regression here fails CI regardless of
            // whether the headless platform rasterises). The editor is functional,
            // not the addr-0 stub.
            Assert.True(view.IsLoaded);
            var vm = Assert.IsType<AOERANGEViewModel>(view.DataViewModel);
            Assert.Equal((uint)W, vm.Width);
            Assert.Equal((uint)H, vm.Height);
            Assert.Equal(W * H, vm.Cells.Count);          // dynamic grid is populated.
            int centerIdx = -1;
            for (int i = 0; i < vm.Cells.Count; i++)
                if (vm.Cells[i].IsCenter) { centerIdx = i; break; }
            Assert.Equal(2 + 2 * W, centerIdx);           // center @ (2,2) = 2 + 2*5 = 12.

            // Measure/Arrange must not throw on the real visual tree (catches XAML
            // binding/format faults like the hex-FormatString bug). The PNG Save
            // itself is best-effort — the test headless platform uses
            // UseHeadlessDrawing (no rasteriser), so a saved PNG would be blank
            // (see the other *ScreenshotTest siblings).
            const int VW = 720, VH = 620;
            view.Measure(new Size(VW, VH));
            view.Arrange(new Rect(0, 0, VW, VH));
            try
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(VW, VH));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1431-aoerange-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless PNG save no-op (UseHeadlessDrawing, not the #1431 fix): {ex.Message}");
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
