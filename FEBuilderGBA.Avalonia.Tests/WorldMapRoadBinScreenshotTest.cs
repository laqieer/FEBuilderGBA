// SPDX-License-Identifier: GPL-3.0-or-later
// #1458 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no PNG
// locally. This test renders a faithful, NON-fabricated picture of the new
// World Map Road (Path) editor .road.bin Save/Load round-trip directly with
// SkiaSharp, driven by the PRODUCTION WorldMapPathEditorViewModel against the
// REAL FE8U ROM (Path 0): Export the raw ROM stream -> ImportPathBin it back ->
// prove the decoded chip buffer matches byte-for-byte. No fabricated data; if no
// FE8U ROM is available the test SKIPs (no PNG committed from synthetic bytes).
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapRoadBinScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public WorldMapRoadBinScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void RenderRoadBinSaveLoadProof_FE8U()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (_fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: not FE8U (fixture loaded {_fixture.Version ?? "no ROM"}).");
                return;
            }
            ROM rom = CoreState.ROM!;
            if (rom.RomInfo == null || rom.RomInfo.worldmap_road_pointer == 0)
            {
                _output.WriteLine("SKIP: no world-map road pointer.");
                return;
            }
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            // Production VM: load Path 0, then exercise the new Save/Load helpers.
            var vm = new WorldMapPathEditorViewModel();
            var list = vm.LoadList();
            if (list.Count == 0)
            {
                _output.WriteLine("SKIP: empty road list.");
                return;
            }
            vm.LoadEntry((int)list[0].tag);
            int originalCount = vm.Chips.Count;

            // Save: export the RAW ROM stream (WF getBinaryData parity).
            byte[]? bin = vm.ExportPathBin(out string exErr);
            Assert.True(string.IsNullOrEmpty(exErr), exErr);
            Assert.NotNull(bin);

            // Snapshot the original chip buffer for a chip-by-chip comparison.
            var original = new List<PathChip>(vm.Chips);

            // Load: import the exported bytes into a fresh VM buffer.
            var vm2 = new WorldMapPathEditorViewModel();
            vm2.LoadEntry((int)list[0].tag);
            string imErr = vm2.ImportPathBin(bin!);
            Assert.Equal("", imErr);
            Assert.Equal(originalCount, vm2.Chips.Count);

            // Prove the round-trip chip-by-chip (matches the header's claim), not
            // just the count (Copilot PR #1564 review).
            for (int i = 0; i < originalCount; i++)
            {
                Assert.Equal(original[i].WorldX, vm2.Chips[i].WorldX);
                Assert.Equal(original[i].WorldY, vm2.Chips[i].WorldY);
                Assert.Equal(original[i].PathX, vm2.Chips[i].PathX);
                Assert.Equal(original[i].PathY, vm2.Chips[i].PathY);
            }

            RenderProof(_fixture.Version!, vm.CurrentAddr, bin!, originalCount, vm2.Chips);
        }

        static void RenderProof(string ver, uint addr, byte[] bin, int origCount, List<PathChip> reloaded)
        {
            const int W = 1024, H = 600;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 24, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText($"World Map Road (Path) editor — {ver}   Path 0 @ 0x{0x08000000u | addr:X08}   [#1458 .road.bin Save/Load]", 24, 40, title);

                // Left: the raw exported .road.bin bytes (hex dump).
                c.DrawRoundRect(24, 64, 470, 470, 8, 8, panelP);
                c.DrawText($"SAVE — raw ROM stream ({bin.Length} bytes)", 40, 96, hdr);
                float y = 128;
                int max = Math.Min(bin.Length, 16 * 14);
                for (int row = 0; row * 16 < max; row++)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"{row * 16:X4}: ");
                    for (int col = 0; col < 16 && row * 16 + col < bin.Length; col++)
                        sb.Append($"{bin[row * 16 + col]:X2} ");
                    c.DrawText(sb.ToString(), 44, y, mono);
                    y += 22;
                    if (y > 510) break;
                }
                c.DrawText("getBinaryData(addr, CalcPathDataLength) — not re-packed", 44, 525, note);

                // Right: the reloaded chip buffer (LOAD round-trip).
                c.DrawRoundRect(520, 64, 480, 470, 8, 8, panelP);
                c.DrawText($"LOAD — DecodePathBin -> {reloaded.Count} chips", 536, 96, hdr);
                y = 128;
                for (int i = 0; i < reloaded.Count && y < 510; i++)
                {
                    var ch = reloaded[i];
                    c.DrawText($"chip {i,2}: world=({ch.WorldX,3},{ch.WorldY,3}) variant={ch.PathX / 8} row={ch.PathY / 8}", 536, y, mono);
                    y += 22;
                }
                c.DrawText($"round-trip OK: {origCount} chips in == {reloaded.Count} chips out", 536, 525, note);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1458-worldmap-road-savebin-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
