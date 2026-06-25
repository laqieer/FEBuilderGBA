// SPDX-License-Identifier: GPL-3.0-or-later
// #1424 PR proof — render the Avalonia SummonsDemonKingViewerView with a
// CORRUPTED count byte (0xFF) to a PNG that proves the editor list now shows
// 0 fabricated rows (WinForms behavior) instead of the old 21 rows.
//
// The bug only triggers for an abnormal count byte (vanilla FE8 count == 11 →
// both editors already agree at 12 rows), so the proof loads a private copy of
// FE8U, sets summons_demon_king_count_address (0x7B2BC) to 0xFF, and renders.
//
// Headless RenderTargetBitmap — works on locked machines and CI. Default output
// is a per-test temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
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
    public class SummonsDemonKingCountScreenshotTest : IClassFixture<RomFixture>
    {
        // FE8U RomInfo addresses (ROMFE8U.cs:321-322).
        const uint CountAddr = 0x7B2BCu; // summons_demon_king_count_address

        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SummonsDemonKingCountScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void CorruptCountByte_ListIsEmpty_NotTwentyOne_SavesScreenshot()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U" || _fixture.RomPath == null)
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (got {_fixture.Version ?? "none"})");
                return;
            }

            ROM? savedRom = CoreState.ROM;
            try
            {
                // Load a private copy of FE8U and corrupt the count byte to 0xFF
                // (>=100 → corrupt). This is the #1424 trigger.
                byte[] bytes = File.ReadAllBytes(_fixture.RomPath);
                bytes[(int)CountAddr] = 0xFF;
                var rom = new ROM();
                bool ok = rom.LoadLow("synth-fe8u-1424-corrupt.gba", bytes, "BE8E01");
                Assert.True(ok, "FE8U copy must load");
                CoreState.ROM = rom;

                // Data-layer proof of the fix: corrupt count → EMPTY list (0 rows),
                // matching WinForms (SummonsDemonKingForm.cs: max>=100 → 0 rows),
                // NOT the old 21 fabricated rows.
                var vm = new SummonsDemonKingViewerViewModel();
                var list = vm.LoadSummonsDemonKingList();
                _output.WriteLine($"Corrupt count byte 0xFF → list rows = {list.Count} (was 21 before #1424)");
                Assert.Empty(list);
                Assert.NotEqual(21, list.Count);

                // Render the view as visual proof (empty list).
                var view = new SummonsDemonKingViewerView { DataContext = vm };
                try
                {
                    const int W = 1000, H = 640;
                    view.Measure(new Size(W, H));
                    view.Arrange(new Rect(0, 0, W, H));
                    using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                    bitmap.Render(view);

                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, "pr1424-demonking-count-fe8u.png");
                    bitmap.Save(outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless render failed (environment, not the #1424 fix): {ex.Message}");
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
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
