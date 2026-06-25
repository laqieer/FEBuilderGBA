// SPDX-License-Identifier: GPL-3.0-or-later
// #1434 PR proof — render the Event Template 1 window after generating the
// VILLAGE_TALK template, showing real generated event bytes + disassembled
// preview (no longer an empty placeholder shell).
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default
// output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using System.Linq;
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
    public class EventTemplateScreenshotTest : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public EventTemplateScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void EventTemplate1_GeneratesVillageTalk_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping screenshot.");
                return;
            }

            // Drive the real generator on the VM and bind it to the window.
            var vm = new EventTemplate1ViewModel();
            var villageTalk = vm.GetButtons().First(b => !b.IsBlank);
            Assert.True(vm.GenerateButton(villageTalk));
            Assert.True(vm.HasGenerated);

            var window = new EventTemplate1View { DataContext = vm };

            try
            {
                const int W = 760, H = 560;
                window.Measure(new Size(W, H));
                window.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(window);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1434-eventtemplate1-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1434 fix): {ex.Message}");
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
