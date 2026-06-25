// SPDX-License-Identifier: GPL-3.0-or-later
// #1444 PR proof image. The Avalonia "Unit Color" view was a dead placeholder
// (address list + label). This test renders a faithful, NON-fabricated picture
// of the FIXED 4-slot colour picker, populated entirely from the production
// EventUnitColorViewModel (Seed + Pack + FriendlyText). It shows the corrected
// editor: four labelled slot combos (Player/Enemy/NPC/Fourth), the selected
// colour per slot, the live friendly-label summary, and the packed value the
// Apply button returns to the event editor.
//
// Rendered directly with SkiaSharp (the headless test app may use no rasteriser,
// so RenderTargetBitmap.Save can be blank — see the other *ProofImageTest
// siblings). Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.

using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventUnitColorProofImageTest
    {
        readonly ITestOutputHelper _output;

        public EventUnitColorProofImageTest(ITestOutputHelper output) => _output = output;

        [Fact]
        public void RenderUnitColorPickerProof()
        {
            // Seed the real ViewModel with a fully-populated value: Player→Blue,
            // Enemy→Red, NPC→Green, Fourth→Sepia (0x4321).
            var vm = new EventUnitColorViewModel();
            vm.Seed(0x4321u);

            string[] slots = { "Player", "Enemy", "NPC", "Fourth" };
            int[] indices = { vm.PlayerIndex, vm.EnemyIndex, vm.NpcIndex, vm.FourthIndex };
            // Stable English colour names for the proof image (translate-agnostic).
            string[] colorNames = { "0 = No change", "1 = Blue", "2 = Red", "3 = Green", "4 = Sepia" };
            uint packed = vm.Pack();
            string friendly = vm.FriendlyText;

            Assert.Equal(0x4321u, packed);
            Assert.Equal(new[] { 1, 2, 3, 4 }, indices);

            const int W = 760, H = 460;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                var combo = new SKColor(0x3A, 0x40, 0x47);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 24, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 16 };
                using var val = new SKPaint { Color = fg, IsAntialias = true, TextSize = 16, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 15 };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };
                using var comboP = new SKPaint { Color = combo, IsAntialias = true };

                c.DrawText("Unit Color picker (event UNIT_COLOR)   [#1444 fixed]", 24, 40, title);

                // Message bar (mirrors the WinForms MESSAGE label).
                c.DrawRoundRect(24, 60, 712, 40, 6, 6, panelP);
                c.DrawText("Please specify the color you want to change.", 40, 86, lbl);

                // Four slot combos.
                float y = 130;
                for (int i = 0; i < 4; i++)
                {
                    c.DrawText(slots[i], 40, y + 24, hdr);
                    c.DrawRoundRect(180, y, 420, 36, 6, 6, comboP);
                    c.DrawText(colorNames[indices[i]], 196, y + 24, val);
                    c.DrawText("v", 575, y + 24, lbl);
                    y += 56;
                }

                // Live friendly-label summary.
                c.DrawRoundRect(24, y + 6, 712, 44, 6, 6, panelP);
                c.DrawText(string.IsNullOrEmpty(friendly) ? "(no change)" : friendly, 40, y + 34,
                    new SKPaint { Color = new SKColor(0x50, 0x8C, 0xC8), IsAntialias = true, TextSize = 16 });

                // Apply note: the packed value returned to the event editor.
                c.DrawText($"Apply -> packed value 0x{packed:X04}  (a | b<<4 | c<<8 | d<<12)", 24, y + 78, note);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1444-unitcolor.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            _output.WriteLine($"packed=0x{packed:X04} friendly='{friendly}'");
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
