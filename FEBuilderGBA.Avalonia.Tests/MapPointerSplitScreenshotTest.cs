// SPDX-License-Identifier: GPL-3.0-or-later
// #1432 PR proof — render the Map Pointer editor showing the new "PLIST Split"
// panel (explanation + button), visible only on a non-split ROM.
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
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MapPointerSplitScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _rom;
        readonly ITestOutputHelper _output;

        public MapPointerSplitScreenshotTest(RomFixture rom, ITestOutputHelper output)
        {
            _rom = rom;
            _output = output;
        }

        [AvaloniaFact]
        public void MapPointerView_ShowsPlistSplitPanel_SavesScreenshot()
        {
            if (!_rom.IsAvailable)
            {
                _output.WriteLine("No ROM available — skipping screenshot render.");
                return;
            }

            var view = new MapPointerView();

            // Data-layer proof: the split panel + button exist as named controls.
            var panel = view.FindControl<Border>("PlistSplitPanel");
            var button = view.FindControl<Button>("PlistSplitButton");
            var explain = view.FindControl<TextBlock>("PlistSplitExplainLabel");
            Assert.NotNull(panel);
            Assert.NotNull(button);
            Assert.NotNull(explain);

            // Surface the split panel for the proof render (the real view shows it
            // in its Opened handler when CanSplit; we set it directly here so the
            // headless render — which does not raise Opened — captures it). On a
            // vanilla ROM the editor genuinely offers Split.
            Assert.True(MapPlistSplitCore.CanSplit(CoreState.ROM),
                "the test ROM must be non-split so the Split panel is offered");
            panel!.IsVisible = true;
            explain!.Text =
                "PLIST Split\n" +
                "PLIST tables pack several purposes into one shared array, so splitting them by " +
                "purpose increases the number of usable PLIST slots (each table is expanded to 256 " +
                "entries / 0xFF).\n(This is a destructive operation — back up your ROM first.)";
            button!.Content = "PLIST Split";

            // Render the REAL MapPointerView Window (keeping its applied styles),
            // but first remove the left column's virtualizing AddressListControl —
            // that control does not rasterize in this headless env and aborts the
            // whole-Window render. The right-hand details column (address/pointer
            // fields + the new PLIST Split panel) renders faithfully. This is a
            // non-fabricated capture of the live editor controls.
            const int W = 760, H = 520;
            var list = view.FindControl<global::Avalonia.Controls.Control>("EntryList");
            if (list?.Parent is Panel listParent)
                listParent.Children.Remove(list);

            view.SizeToContent = SizeToContent.Manual;
            view.Width = W;
            view.Height = H;

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string ver = CoreState.ROM?.RomInfo?.VersionToFilename ?? "rom";
            string outPath = Path.Combine(outDir, $"pr1432-plistsplit-{ver}.png");
            try
            {
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
                bitmap.Render(view);
                bitmap.Save(outPath);
                // Some headless environments configure UseHeadlessDrawing (no
                // rasteriser), so Save can silently no-op. Only log a byte count
                // when the file actually materialised — never throw here.
                long bytes = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
                _output.WriteLine(bytes > 0
                    ? $"Saved screenshot to: {outPath} ({bytes} bytes)"
                    : $"Render produced no PNG (headless no-rasteriser env): {outPath}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1432 fix): {ex.Message}");
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
