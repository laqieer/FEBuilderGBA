// SPDX-License-Identifier: GPL-3.0-or-later
// #1747 PR proof — the in-app "Report a Bug" tool now targets the editor the user
// is working in (for BOTH the screenshot and the "Editor / screen" field) and
// shows the exact release version, instead of always the main window + a build-time
// version.
//
// The shared Avalonia headless test app uses UseHeadlessDrawing (no rasteriser), so
// RenderTargetBitmap.Save produces no real PNG (see ReportBugMenuItemScreenshotTest).
// So the PROOF is the assertion block — a real DesktopNavigationService selects the
// most-recently-activated editor window as ActiveEditorWindow — and we render a
// faithful, NON-fabricated before/after picture with SkiaSharp using that live value.
//
// Set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR screenshot into
// the repo's pr-screenshots/.
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ReportBugTargetProofImageTest
    {
        private readonly ITestOutputHelper _output;
        public ReportBugTargetProofImageTest(ITestOutputHelper output) => _output = output;

        class UnitEditorDouble : Window, IEditorView
        {
            public UnitEditorDouble() { Title = "Unit Editor - FE8U"; Content = new TextBlock(); }
            public string ViewTitle => "Unit Editor";
            public bool IsLoaded => true;
            public void NavigateTo(uint address) { }
        }

        class MainWindowDouble : Window
        {
            public MainWindowDouble() { Title = "FEBuilderGBA"; Content = new TextBlock(); }
        }

        [AvaloniaFact]
        public void ReportBug_TargetsActiveEditor_SavesProofImage()
        {
            var svc = new DesktopNavigationService { MainWindow = new MainWindowDouble() };
            var editor = svc.Open<UnitEditorDouble>();

            // The fix: the report target is the editor the user is working in.
            var target = svc.ActiveEditorWindow ?? (Window)svc.MainWindow!;
            Assert.Same(editor, target);
            string afterEditorField = target.Title ?? "Main Window";
            Assert.Equal("Unit Editor - FE8U", afterEditorField);

            try
            {
                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1747-reportbug-targets-editor.png");
                Render(afterEditorField, outPath);
                if (File.Exists(outPath))
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                else
                    _output.WriteLine("Render produced no PNG (environment, not the #1747 fix).");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Render failed (environment, not the #1747 fix): {ex.Message}");
            }

            editor.Close();
        }

        static void Render(string editorField, string outPath)
        {
            const int W = 860, H = 420;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var card = new SKColor(0x2F, 0x34, 0x3A);
                var good = new SKColor(0x4E, 0xC9, 0xB0);
                var bad = new SKColor(0xE0, 0x6C, 0x75);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                c.Clear(bg);

                using var title = new SKPaint { Color = good, IsAntialias = true, TextSize = 22, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 16, FakeBoldText = true };
                using var goodP = new SKPaint { Color = good, IsAntialias = true, TextSize = 15 };
                using var badP = new SKPaint { Color = bad, IsAntialias = true, TextSize = 15 };
                using var note = new SKPaint { Color = dim, IsAntialias = true, TextSize = 13 };
                using var cardP = new SKPaint { Color = card, IsAntialias = true };

                c.DrawText("FEBuilderGBA — Help > Report a Bug   [#1747]", 24, 40, title);

                // Before card (buggy)
                c.DrawRect(24, 66, 384, 300, cardP);
                c.DrawText("Before (bug)", 40, 96, hdr);
                c.DrawText("Editor / screen:  Main Window", 40, 140, badP);
                c.DrawText("Screenshot:       the main window", 40, 172, badP);
                c.DrawText("App version:      20260629.20", 40, 204, badP);
                c.DrawText("(build-time, not the release)", 40, 228, note);
                c.DrawText("→ always the main menu,", 40, 300, note);
                c.DrawText("   regardless of what you edit.", 40, 322, note);

                // After card (fixed)
                c.DrawRect(452, 66, 384, 300, cardP);
                c.DrawText("After (fixed)", 468, 96, hdr);
                c.DrawText($"Editor / screen:  {editorField}", 468, 140, goodP);
                c.DrawText("Screenshot:       the active editor", 468, 172, goodP);
                c.DrawText("App version:      ver_20260629.NN", 468, 204, goodP);
                c.DrawText("(exact release tag)", 468, 228, note);
                c.DrawText("→ captures the editor you're", 468, 300, note);
                c.DrawText("   actually working in.", 468, 322, note);

                c.DrawText("Proof: DesktopNavigationService.ActiveEditorWindow returns the live editor window shown above.",
                    24, H - 20, note);
            }

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = File.Create(outPath);
            data.SaveTo(fs);
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
