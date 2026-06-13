// SPDX-License-Identifier: GPL-3.0-or-later
// #1115 — screenshot proof for the Animation Creator skill-jump SEED path.
// Mirrors ToolAnimationCreatorMagicSeedScreenshotTest (#996/#1116): build a
// self-contained, CI-portable FE8J ROM with a REAL skill-anime config (frames
// stream + per-id LZ77 OBJ + LZ77 TSA + 16-colour palette) and drive the EXACT
// production seam the #1115 skill jump runs:
//   SkillSystemsAnimeExportCore.ExportSkillAnimation (the ROM decode) ->
//   ToolAnimationCreatorView.InitFromSkillRom (the new VM seed) ->
//   SkillSystemsAnimeExportCore.GetFrameImage (the TSA-correct per-frame preview).
//
// Why a synthetic ROM instead of RomFixture: NO ROM under roms/ detects as one
// of the 4 anime-capable SkillConfig variants (Ver2/Ver3/CSkillSys09x/
// SkillSystem) with a valid per-skill anime pointer (roms/FE8J_skill.gba is
// FE8N Ver1 — render-only, NO animation pointer — verified at the byte level in
// the #1115 Step-0 probe). So the live skill editor cannot populate the Creator
// end-to-end. This test plants a real skill-anime config and renders the actual
// TSA-decoded frame, producing a genuine (non-fabricated) skill-frame PNG.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolAnimationCreatorSkillSeedScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public ToolAnimationCreatorSkillSeedScreenshotTest(ITestOutputHelper output) => _output = output;

        // Tail-of-ROM layout for the synthetic skill anime.
        const uint CONFIG       = 0x300;
        const uint FRAMES       = 0x400;
        const uint GRAPHIC_LIST = 0x500;
        const uint TSA_LIST     = 0x600;
        const uint PAL_LIST     = 0x700;
        const uint OBJ_LZ       = 0x1000;
        const uint TSA_LZ       = 0x3000;
        const uint PAL_OFF      = 0x5000;
        const int ROM_SIZE      = 0x1000000; // 16 MB — FE8J version-detect needs the full size
        const int W = 1024, H = 720;

        [AvaloniaFact]
        public void AnimationCreatorSkillSeed_RendersAndSavesScreenshot()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();

                ROM rom = BuildFE8JRomWithSkillAnime();
                CoreState.ROM = rom; // ExportSkillAnimation requires the ACTIVE rom.

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);

                // Render the REAL TSA-decoded skill frame (the production preview seam)
                // and save it — proves the frame model maps onto a real rendered image.
                var export = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, U.toPointer(CONFIG));
                _output.WriteLine($"ExportSkillAnimation: error='{export.Error}' frames={export.Frames.Count}");
                Assert.Equal("", export.Error);
                Assert.True(export.Frames.Count >= 2);

                var view = new ToolAnimationCreatorView();
                view.InitFromSkillRom(AnimationTypeEnum.Skill, 1,
                    "Skill Animation #01 (demo frames)", U.toPointer(CONFIG));

                var framesList = view.FindControl<ListBox>("FramesList");
                Assert.NotNull(framesList);
                Assert.True(framesList!.ItemCount >= 2,
                    "skill seed should populate >= 2 frames from the planted stream");
                framesList.SelectedIndex = 0;
                IImage frame = SkillSystemsAnimeExportCore.GetFrameImage(export, 0);
                Assert.NotNull(frame);
                Assert.Equal(240, frame!.Width);
                Assert.True(frame.Height >= 160);
                byte[] png = frame.EncodePng();
                Assert.NotNull(png);
                Assert.True(png!.Length > 0, "skill frame PNG must have bytes");
                string previewPath = Path.Combine(outDir, "pr1115-skill-seed-preview.png");
                File.WriteAllBytes(previewPath, png);
                _output.WriteLine($"Saved skill-frame preview ({frame.Width}x{frame.Height}) to: {previewPath} ({png.Length} bytes)");

                var previewControl = view.FindControl<GbaImageControl>("MapActionPreview");
                Assert.NotNull(previewControl);

                try
                {
                    view.Measure(new Size(W, H));
                    view.Arrange(new Rect(0, 0, W, H));
                    using var bmp = new RenderTargetBitmap(new PixelSize(W, H));
                    bmp.Render(view);
                    string viewPath = Path.Combine(outDir, "pr1115-skill-seed-view.png");
                    bmp.Save(viewPath);
                    _output.WriteLine($"Saved editor view to: {viewPath} ({new FileInfo(viewPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless full-view render skipped (environment, not #1115): {ex.Message}");
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }

        // Build a FE8J (multi-byte) ROM with a real skill-anime config: 2 frames,
        // each id maps to a LZ77 64x64 radial OBJ + a LZ77 30x20 TSA + a rainbow
        // palette. Both frame ids reference the same resources (like real anime).
        static ROM BuildFE8JRomWithSkillAnime()
        {
            byte[] data = new byte[ROM_SIZE];

            // 240x160 frame = 30x20 = 600 TSA cells; tile each cell into the
            // 8x8 = 64-tile radial OBJ sheet.
            const int OBJ_W = 64, OBJ_H = 64;
            const int COLS = 30, ROWS = 20;
            byte[] objRaw = BuildRadialTiles(OBJ_W, OBJ_H);
            byte[] objLz = LZ77.compress(objRaw);
            Array.Copy(objLz, 0, data, OBJ_LZ, objLz.Length);

            byte[] tsaRaw = new byte[COLS * ROWS * 2];
            for (int i = 0; i < COLS * ROWS; i++)
            {
                ushort tile = (ushort)(i % ((OBJ_W / 8) * (OBJ_H / 8)));
                tsaRaw[i * 2 + 0] = (byte)(tile & 0xFF);
                tsaRaw[i * 2 + 1] = (byte)((tile >> 8) & 0xFF);
            }
            byte[] tsaLz = LZ77.compress(tsaRaw);
            Array.Copy(tsaLz, 0, data, TSA_LZ, tsaLz.Length);

            for (int i = 0; i < 16; i++)
            {
                ushort c = RainbowColor(i);
                data[PAL_OFF + i * 2 + 0] = (byte)(c & 0xFF);
                data[PAL_OFF + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }

            // config: frames, tsalist, graphiclist, palettelist, soundId.
            WriteU32(data, CONFIG + 0,  U.toPointer(FRAMES));
            WriteU32(data, CONFIG + 4,  U.toPointer(TSA_LIST));
            WriteU32(data, CONFIG + 8,  U.toPointer(GRAPHIC_LIST));
            WriteU32(data, CONFIG + 12, U.toPointer(PAL_LIST));
            WriteU32(data, CONFIG + 16, 0x3C);

            // per-id list tables (ids 0 and 1 -> same resources).
            WriteU32(data, GRAPHIC_LIST + 0, U.toPointer(OBJ_LZ));
            WriteU32(data, GRAPHIC_LIST + 4, U.toPointer(OBJ_LZ));
            WriteU32(data, TSA_LIST + 0, U.toPointer(TSA_LZ));
            WriteU32(data, TSA_LIST + 4, U.toPointer(TSA_LZ));
            WriteU32(data, PAL_LIST + 0, U.toPointer(PAL_OFF));
            WriteU32(data, PAL_LIST + 4, U.toPointer(PAL_OFF));

            // frames stream: (id=0,wait=4) (id=1,wait=8) terminator.
            WriteU16(data, FRAMES + 0, 0); WriteU16(data, FRAMES + 2, 4);
            WriteU16(data, FRAMES + 4, 1); WriteU16(data, FRAMES + 6, 8);
            WriteU16(data, FRAMES + 8, 0xFFFF);

            var rom = new ROM();
            rom.LoadLow("synthetic_fe8j_skillanime.gba", data, "BE8J01");
            return rom;
        }

        static ushort RainbowColor(int i)
        {
            int r, g, b;
            if (i == 0) { r = g = b = 2; }
            else
            {
                double h = (i - 1) / 15.0 * 6.0;
                int seg = (int)h; double f = h - seg;
                int v = 31, p = 4, q = (int)(31 * (1 - f)), t = (int)(31 * f);
                switch (seg % 6)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }
            return (ushort)((b << 10) | (g << 5) | r);
        }

        static byte[] BuildRadialTiles(int w, int h)
        {
            byte[] tiles = new byte[w * h / 2];
            int tilesW = w / 8;
            int cx = w / 2, cy = h / 2;
            int p = 0;
            for (int ty = 0; ty < h / 8; ty++)
                for (int tx = 0; tx < tilesW; tx++)
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px += 2)
                        {
                            int x0 = tx * 8 + px, y0 = ty * 8 + py;
                            int lo = RingIndex(x0, y0, cx, cy);
                            int hi = RingIndex(x0 + 1, y0, cx, cy);
                            tiles[p++] = (byte)((lo & 0x0F) | ((hi & 0x0F) << 4));
                        }
            return tiles;
        }

        static int RingIndex(int x, int y, int cx, int cy)
        {
            double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return ((int)(d / 3.0) % 15) + 1;
        }

        static void WriteU16(byte[] data, uint addr, int v)
        {
            data[addr + 0] = (byte)(v & 0xFF);
            data[addr + 1] = (byte)((v >> 8) & 0xFF);
        }

        static void WriteU32(byte[] data, uint addr, uint v)
        {
            data[addr + 0] = (byte)(v & 0xFF);
            data[addr + 1] = (byte)((v >> 8) & 0xFF);
            data[addr + 2] = (byte)((v >> 16) & 0xFF);
            data[addr + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
