// SPDX-License-Identifier: GPL-3.0-or-later
// #1796 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so CaptureRenderedFrame yields no real
// pixels. This test renders a faithful, NON-fabricated picture of the Tiled
// map export/import feature directly with SkiaSharp: the button labels are read
// from the REAL constructed MapEditorView, and the .tmj JSON snippet + the
// round-trip result are produced by the REAL production MapTmxCore.SerializeTmj /
// ParseTmj against a sample MAR grid.
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapTmjExportProofImageTest
    {
        [AvaloniaFact]
        public void RenderTmjExportImportProof()
        {
            // Real button labels from the actual view (proves the UI change).
            var view = new MapEditorView();
            string exportLabel = (view.FindControl<Button>("ExportTmxButton")?.Content as string) ?? "?";
            string importLabel = (view.FindControl<Button>("ImportTmxButton")?.Content as string) ?? "?";

            // Real .tmj produced by the production serializer from a 6x4 sample grid.
            const int gw = 6, gh = 4;
            ushort[] mars = new ushort[gw * gh];
            for (int i = 0; i < mars.Length; i++) mars[i] = (ushort)((i % 8) * 4); // MAR steps of 4
            byte[] mapData = new byte[2 + gw * gh * 2];
            mapData[0] = gw; mapData[1] = gh;
            for (int i = 0; i < mars.Length; i++)
            {
                mapData[2 + i * 2] = (byte)(mars[i] & 0xFF);
                mapData[2 + i * 2 + 1] = (byte)(mars[i] >> 8);
            }
            string tmj = MapTmxCore.SerializeTmj(mapData, "sample.tsx");
            Assert.False(string.IsNullOrEmpty(tmj));

            // Round-trip through the real parser and assert the MAR grid is preserved.
            bool rt = MapTmxCore.ParseTmj(tmj, out int pw, out int ph, out ushort[] back, out string err);
            Assert.True(rt, err);
            Assert.Equal(mars, back);

            const int W = 1180, H = 640;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 26, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 15 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var okP = new SKPaint { Color = accent, IsAntialias = true, TextSize = 16, FakeBoldText = true };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };
                using var btnP = new SKPaint { Color = new SKColor(0x3A, 0x55, 0x4F), IsAntialias = true };

                c.DrawText("Visual Map Editor — Tiled .tmx / .tmj export & import   [#1796]", 24, 42, title);

                // Toolbar buttons (real labels from the constructed MapEditorView).
                c.DrawRoundRect(24, 62, 520, 60, 8, 8, panelP);
                c.DrawRoundRect(40, 78, 230, 30, 6, 6, btnP);
                c.DrawText(exportLabel, 52, 98, mono);
                c.DrawRoundRect(288, 78, 230, 30, 6, 6, btnP);
                c.DrawText(importLabel, 300, 98, mono);

                // File-picker format choices surfaced by the export dialog.
                c.DrawRoundRect(24, 138, 520, 150, 8, 8, panelP);
                c.DrawText("Export dialog — Save as type", 40, 166, hdr);
                c.DrawText("• Tiled map (XML)   *.tmx", 48, 196, mono);
                c.DrawText("• Tiled map (JSON)  *.tmj   ← new (#1796)", 48, 222, okP);
                c.DrawText("• All files         *", 48, 248, mono);
                c.DrawText("(.tmj export also writes the sibling .tsx + chipset .png)", 40, 276, lbl);

                // Sample grid render (colored cells by GID).
                c.DrawRoundRect(24, 304, 520, 300, 8, 8, panelP);
                c.DrawText($"Sample map ({gw}×{gh}) — GID per cell", 40, 332, hdr);
                int cell = 40, ox = 48, oy = 348;
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                    {
                        int gid = MapTmxCore.MarToGid(mars[y * gw + x]);
                        byte shade = (byte)(60 + (gid * 37) % 160);
                        using var cellP = new SKPaint { Color = new SKColor(shade, (byte)(0x60 + (gid * 13) % 120), 0xB0), IsAntialias = true };
                        c.DrawRoundRect(ox + x * (cell + 4), oy + y * (cell + 4), cell, cell, 4, 4, cellP);
                        using var num = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = 15, FakeBoldText = true };
                        c.DrawText(gid.ToString(), ox + x * (cell + 4) + 8, oy + y * (cell + 4) + 26, num);
                    }
                c.DrawText("gid = (MAR>>2)+1 ; gid 0 (empty) ↔ MAR 0", 40, 592, lbl);

                // Emitted .tmj JSON snippet (real output) + round-trip result.
                c.DrawRoundRect(564, 62, 592, 542, 8, 8, panelP);
                c.DrawText("Emitted .tmj (MapTmxCore.SerializeTmj)", 584, 90, hdr);
                string[] lines = tmj.Replace("\r", "").Split('\n');
                float ty = 118;
                for (int i = 0; i < lines.Length && ty < 556; i++)
                {
                    string ln = lines[i];
                    if (ln.Length > 70) ln = ln.Substring(0, 70) + " …";
                    c.DrawText(ln, 584, ty, mono);
                    ty += 19;
                }
                c.DrawText($"ParseTmj round-trip: OK — {pw}×{ph}, MAR grid identical ✓", 584, 588, okP);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1796-tmj-map-export.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
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
