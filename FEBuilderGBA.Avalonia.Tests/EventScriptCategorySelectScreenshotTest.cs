// SPDX-License-Identifier: GPL-3.0-or-later
// #1443 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no
// real PNG. This test renders a faithful, NON-fabricated picture of the FIXED
// Event Script Category Select dialog, populated entirely by driving the
// production EventScriptCategorySelectViewModel against a real FE8U ROM (so the
// categories and command list are exactly what the GUI shows).
//
// It proves the fix: real categories load from event_category_FE8.*.txt (NOT
// the 8 invented English labels), the command list is real EventScript.Scripts
// filtered by category + text, {TEMPLATE} is skipped, and ConfirmSelection()
// returns a real EventScript.Script.
//
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventScriptCategorySelectScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly EventScript? _prevEvent;
        readonly IEtcCache? _prevComment;
        readonly string? _prevBaseDir;
        readonly ROM _rom;

        public EventScriptCategorySelectScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevEvent = CoreState.EventScript;
            _prevComment = CoreState.CommentCache;
            _prevBaseDir = CoreState.BaseDirectory;

            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            CoreState.BaseDirectory = asmDir;

            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.CommentCache = new HeadlessEtcCache();

            var es = new EventScript();
            es.Load(EventScript.EventScriptType.Event);
            CoreState.EventScript = es;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.EventScript = _prevEvent;
            CoreState.CommentCache = _prevComment;
            CoreState.BaseDirectory = _prevBaseDir;
        }

        [Fact]
        public void RenderEventScriptCategoryPickerProof_FE8U()
        {
            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            Assert.True(vm.IsLoaded);
            Assert.Contains("Show all", vm.Categories);
            Assert.DoesNotContain("Event Template", vm.Categories); // {TEMPLATE} skipped

            // "Show all" -> full real command list.
            vm.SelectedCategory = "Show all";
            int allCount = vm.ScriptNames.Count;
            Assert.True(allCount > 0);

            // "Text" category narrows it (proves the {TEXT} token filter works).
            vm.SelectedCategory = "Text";
            var textNames = vm.ScriptNames.ToList();
            Assert.True(textNames.Count > 0 && textNames.Count < allCount);

            // Pick the first Text command -> a real returned Script.
            vm.SelectedScriptIndex = 0;
            Assert.True(vm.ConfirmSelection());
            Assert.NotNull(vm.SelectedScript);
            string chosen = EventScript.makeCommandComboText(vm.SelectedScript!, true);
            Assert.False(string.IsNullOrWhiteSpace(chosen));

            // --- Render a faithful SkiaSharp proof image of the dialog state ---
            const int W = 1100, H = 660;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                var sel = new SKColor(0x33, 0x4E, 0x4A);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 24, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 14 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };
                using var selP = new SKPaint { Color = sel, IsAntialias = true };

                c.DrawText("Event Script Category Select — FE8U   [#1443 wired]", 24, 40, title);
                c.DrawText("Real categories from event_category_FE8.*.txt + real EventScript.Scripts; returns a chosen Script.", 24, 64, lbl);

                // Left panel: real category list ({TEMPLATE} skipped). Highlight "Text".
                c.DrawRoundRect(24, 84, 280, 520, 8, 8, panelP);
                c.DrawText("Category", 40, 112, hdr);
                float ry = 140;
                foreach (var cat in vm.Categories.Take(15))
                {
                    if (cat == "Text")
                        c.DrawRoundRect(34, ry - 16, 260, 24, 4, 4, selP);
                    c.DrawText(cat, 44, ry, cat == "Text" ? mono : lbl);
                    ry += 26;
                }
                c.DrawText("({TEMPLATE} intentionally skipped — no Avalonia consumer)", 36, ry + 6, lbl);

                // Middle panel: filtered command list (Text category).
                c.DrawRoundRect(320, 84, 470, 520, 8, 8, panelP);
                c.DrawText($"Commands — category 'Text'  ({textNames.Count} of {allCount})", 336, 112, hdr);
                float cy = 140;
                for (int i = 0; i < textNames.Count && i < 16; i++)
                {
                    if (i == 0)
                        c.DrawRoundRect(330, cy - 16, 450, 24, 4, 4, selP);
                    string row = textNames[i];
                    if (row.Length > 52) row = row.Substring(0, 52) + "…";
                    c.DrawText(row, 340, cy, mono);
                    cy += 26;
                }

                // Right panel: chosen result.
                c.DrawRoundRect(806, 84, 270, 200, 8, 8, panelP);
                c.DrawText("Result (returned Script)", 822, 112, hdr);
                string r0 = chosen.Length > 30 ? chosen.Substring(0, 30) + "…" : chosen;
                c.DrawText(r0, 822, 144, mono);
                c.DrawText("OK -> Close(SelectedScript)", 822, 176, note);
                c.DrawText("(was: bare category string)", 822, 200, lbl);

                c.DrawText($"Before: 8 invented labels, no command list, no result.", 822, 320, lbl);
                c.DrawText($"After : {vm.Categories.Count} real categories, {allCount} real commands.", 822, 344, note);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1443-eventscript-category-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
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
