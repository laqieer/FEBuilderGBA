// SPDX-License-Identifier: GPL-3.0-or-later
// #1418 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no PNG
// locally. This test renders a faithful, NON-fabricated picture of the FIXED
// World Map Path Move editor list directly with SkiaSharp, populated from the
// REAL FE8U ROM via the production WorldMapPathMoveEditorViewModel.BuildList.
//
// It shows the corrected terminator in action: FE8U Path 0's move data (base
// 0x082064BC) ends at the u32 0xFFFFFFFF sentinel (0x2064CC), so BuildList returns
// exactly the 2 real nodes. The old all-zeros terminator read the sentinel as a
// node and over-read into adjacent ROM (the right panel lists those over-read
// rows for contrast). Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathMoveScreenshotTest : IClassFixture<RomFixture>
    {
        // FE8U Path 0 real move data (file offset). 2 nodes then sentinel @ 0x2064CC.
        const uint FE8U_PATH0_MOVE_OFFSET = 0x2064BC;

        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public WorldMapPathMoveScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void RenderPathMoveTerminatorProof_FE8U()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            // This proof asserts FE8U-specific data (Path 0 = exactly 2 nodes, the
            // 0x2064BC fallback offset), so gate strictly on FE8U for determinism
            // across whichever ROM the fixture happened to load.
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

            uint baseOff = ResolvePath0MoveOffset(rom);

            // Production VM with the FIX: BuildList stops at the 0xFFFFFFFF sentinel.
            var vm = new WorldMapPathMoveEditorViewModel();
            List<AddrResult> rows = vm.BuildList(baseOff);

            Assert.Equal(2, rows.Count); // exactly 2 real nodes; no over-read.

            // Capture what the OLD all-zeros terminator would have over-read (for the
            // contrast panel) — walk forward ignoring the sentinel until an all-zeros
            // entry or a cap, exactly like the buggy code.
            var overread = SimulateOldAllZerosWalk(rom, baseOff, cap: 24);

            RenderProof(_fixture.Version ?? "FE8U", baseOff, rows, overread);
        }

        /// <summary>Resolve FE8U Path 0's move-data file offset from the road table.
        /// Each 12-byte road entry is {point_ptr @ +0, point_count @ +4, move_ptr @ +8};
        /// Path 0's move data is therefore p32(roadBase + 8). Falls back to the verified
        /// 0x2064BC.</summary>
        static uint ResolvePath0MoveOffset(ROM rom)
        {
            try
            {
                uint roadBase = rom.p32(rom.RomInfo.worldmap_road_pointer);
                if (U.isSafetyOffset(roadBase, rom))
                {
                    uint move0 = rom.p32(roadBase + 8); // Path 0 move-data pointer
                    if (U.isSafetyOffset(move0, rom)) return move0;
                }
            }
            catch { /* fall through */ }
            return FE8U_PATH0_MOVE_OFFSET;
        }

        /// <summary>Reproduce the removed all-zeros terminator to show the rows it
        /// would have surfaced past the real sentinel.</summary>
        static List<(uint addr, uint t, uint x, uint y)> SimulateOldAllZerosWalk(
            ROM rom, uint baseOff, int cap)
        {
            var rows = new List<(uint, uint, uint, uint)>();
            for (int i = 0; i < cap; i++)
            {
                uint a = baseOff + (uint)(i * 8);
                if (a + 8 > (uint)rom.Data.Length) break;
                uint t = rom.u32(a); uint x = rom.u16(a + 4); uint y = rom.u16(a + 6);
                if (t == 0 && x == 0 && y == 0 && i > 0) break; // old terminator
                rows.Add((a, t, x, y));
            }
            return rows;
        }

        static void RenderProof(string ver, uint baseOff,
            List<AddrResult> rows, List<(uint addr, uint t, uint x, uint y)> overread)
        {
            const int W = 1024, H = 600;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var bad = new SKColor(0xE0, 0x6C, 0x6C);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 24, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var monoBad = new SKPaint { Color = bad, IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var noteBad = new SKPaint { Color = bad, IsAntialias = true, TextSize = 14 };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText($"World Map Path Move — {ver}  Path 0 @ 0x{0x08000000u | baseOff:X08}   [#1418 fixed]", 24, 40, title);

                // Left: FIXED list (exactly the real nodes, terminated at sentinel).
                c.DrawRoundRect(24, 64, 470, 470, 8, 8, panelP);
                c.DrawText("FIXED — BuildList stops at 0xFFFFFFFF sentinel", 40, 96, hdr);
                float y = 128;
                for (int i = 0; i < rows.Count; i++)
                {
                    c.DrawText(rows[i].name, 44, y, mono);
                    y += 26;
                }
                c.DrawText($"sentinel  @ 0x{0x08000000u | (baseOff + (uint)(rows.Count * 8)):X08}  T=0xFFFFFFFF (end)", 44, y, note); y += 30;
                c.DrawText($"{rows.Count} editable rows (was 55+ on FE8U)", 44, y, note);

                // Right: OLD over-read rows past the sentinel (garbage / adjacent ROM).
                c.DrawRoundRect(520, 64, 480, 470, 8, 8, panelP);
                c.DrawText("OLD all-zeros terminator — over-read", 536, 96, hdr);
                y = 128;
                for (int i = 0; i < overread.Count && y < 520; i++)
                {
                    var r = overread[i];
                    bool past = i >= rows.Count; // rows at/after the real sentinel = over-read
                    string line = $"Node {i} @0x{0x08000000u | r.addr:X08} T={r.t}";
                    c.DrawText(line, 536, y, past ? monoBad : mono);
                    y += 24;
                }
                c.DrawText("rows at/after the sentinel are garbage the user", 536, 548, noteBad);
                c.DrawText("could select and Write -> ROM corruption.", 536, 566, noteBad);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1418-worldmap-pathmove-fe8u.png");
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
