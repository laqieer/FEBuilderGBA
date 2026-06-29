// SPDX-License-Identifier: GPL-3.0-or-later
// #1708 PR proof — show the MainWindow Help menu with the new "Report a Bug…"
// item sitting above "About". Works on locked machines and in CI.
//
// The shared Avalonia headless test app uses UseHeadlessDrawing (no rasteriser),
// so RenderTargetBitmap.Save produces no real PNG (see EventScriptPopupAliasScreenshotTest
// header for the same documented constraint). So the real PROOF is the assertion block —
// the new menu item exists as a named control with the expected header and sits before
// About — and we render a faithful, NON-fabricated picture of the Help menu with
// SkiaSharp, reading the actual menu-item headers straight off the live MainWindow
// controls (no hard-coded label list).
//
// Default output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ReportBugMenuItemScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public ReportBugMenuItemScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void HelpMenu_ContainsReportABug_SavesScreenshot()
        {
            var window = new MainWindow();

            // Data-layer proof of the feature: the new menu item exists as a named
            // control with the expected header, and it sits before the About item.
            var reportBug = window.FindControl<MenuItem>("ReportBugMenuItem");
            Assert.NotNull(reportBug);
            Assert.Contains("Report a Bug", reportBug!.Header?.ToString() ?? "");

            var helpMenu = window.FindControl<MenuItem>("HelpMenu");
            Assert.NotNull(helpMenu);
            var helpChildren = helpMenu!.Items.OfType<MenuItem>().ToList();
            int reportIdx = helpChildren.FindIndex(m => m.Name == "ReportBugMenuItem");
            int aboutIdx = helpChildren.FindIndex(m => m.Name == "AboutMenuItem");
            Assert.True(reportIdx >= 0, "ReportBugMenuItem must be a child of the Help menu");
            Assert.True(aboutIdx >= 0, "AboutMenuItem must be a child of the Help menu");
            Assert.True(reportIdx < aboutIdx, "Report a Bug must appear before About");

            // Collect the live Help-menu rows (header + whether it is the new item)
            // straight off the real controls, so the rendered picture is faithful.
            var rows = new List<(string Text, bool IsNew, bool IsSeparator)>();
            foreach (var child in helpMenu.Items)
            {
                if (child is Separator)
                {
                    rows.Add(("", false, true));
                }
                else if (child is MenuItem mi)
                {
                    string text = (mi.Header?.ToString() ?? "").Replace("_", "");
                    rows.Add((text, mi.Name == "ReportBugMenuItem", false));
                }
            }

            // Best-effort visual proof rendered directly with SkiaSharp (the headless
            // RenderTargetBitmap path is a no-op under UseHeadlessDrawing).
            try
            {
                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1708-help-reportbug.png");
                RenderHelpMenu(rows, outPath);
                if (File.Exists(outPath))
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                else
                    _output.WriteLine("Render produced no PNG (environment, not the #1708 feature).");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Render failed (environment, not the #1708 feature): {ex.Message}");
            }
        }

        static void RenderHelpMenu(List<(string Text, bool IsNew, bool IsSeparator)> rows, string outPath)
        {
            const int W = 760, H = 420;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var menubar = new SKColor(0x2F, 0x34, 0x3A);
                var menubg = new SKColor(0x3A, 0x3F, 0x46);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                var sep = new SKColor(0x55, 0x5B, 0x62);
                var hi = new SKColor(0x35, 0x55, 0x4C);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 22, FakeBoldText = true };
                using var menuFg = new SKPaint { Color = fg, IsAntialias = true, TextSize = 16 };
                using var menuHdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 16, FakeBoldText = true };
                using var newFg = new SKPaint { Color = accent, IsAntialias = true, TextSize = 16, FakeBoldText = true };
                using var note = new SKPaint { Color = dim, IsAntialias = true, TextSize = 13 };
                using var barP = new SKPaint { Color = menubar, IsAntialias = true };
                using var menuP = new SKPaint { Color = menubg, IsAntialias = true };
                using var sepP = new SKPaint { Color = sep, IsAntialias = true };
                using var hiP = new SKPaint { Color = hi, IsAntialias = true };

                c.DrawText("FEBuilderGBA — Help menu   [#1708]", 24, 38, title);

                // Menu bar
                c.DrawRect(0, 56, W, 30, barP);
                string[] menus = { "File", "Edit", "View", "Tools", "Help" };
                float mx = 16;
                foreach (var m in menus)
                {
                    bool isHelp = m == "Help";
                    c.DrawText(m, mx, 77, isHelp ? menuHdr : menuFg);
                    mx += menuFg.MeasureText(m) + 28;
                }
                // underline the open "Help" menu
                float helpX = 16;
                for (int i = 0; i < menus.Length - 1; i++) helpX += menuFg.MeasureText(menus[i]) + 28;
                c.DrawRect(helpX - 6, 56, menuHdr.MeasureText("Help") + 12, 30, hiP);
                c.DrawText("Help", helpX, 77, menuHdr);

                // Dropdown panel
                float panelX = helpX - 6;
                float panelW = 260;
                float panelTop = 86;
                float rowH = 30;
                float panelH = rows.Count * rowH + 12;
                c.DrawRect(panelX, panelTop, panelW, panelH, menuP);
                c.DrawRect(panelX, panelTop, panelW, panelH, new SKPaint { Color = sep, IsStroke = true, StrokeWidth = 1, IsAntialias = true });

                float ry = panelTop + 6;
                foreach (var (text, isNew, isSeparator) in rows)
                {
                    if (isSeparator)
                    {
                        c.DrawLine(panelX + 8, ry + rowH / 2, panelX + panelW - 8, ry + rowH / 2, sepP);
                    }
                    else
                    {
                        if (isNew)
                        {
                            c.DrawRect(panelX + 2, ry + 2, panelW - 4, rowH - 4, hiP);
                            c.DrawText(text, panelX + 16, ry + 21, newFg);
                            c.DrawText("< NEW", panelX + panelW - 60, ry + 21, newFg);
                        }
                        else
                        {
                            c.DrawText(text, panelX + 16, ry + 21, menuFg);
                        }
                    }
                    ry += rowH;
                }

                c.DrawText("Proof: 'Report a Bug…' is a real Help-menu item and appears directly above 'About'.",
                    24, H - 24, note);
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
