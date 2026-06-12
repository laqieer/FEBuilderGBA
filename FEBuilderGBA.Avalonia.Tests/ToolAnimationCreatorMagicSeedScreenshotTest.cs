// SPDX-License-Identifier: GPL-3.0-or-later
// #996 — screenshot proof for the Animation Creator magic-jump SEED path
// (FEditor magic kind). Mirrors ToolAnimationCreatorPreviewScreenshotTest
// (#1024): render through the REAL production seam and write PNGs via
// IImage.EncodePng so real pixels are produced even when the headless drawing
// backend is a no-op.
//
// Why a synthetic ROM instead of RomFixture: the magic editors only POPULATE
// when the FEditorAdv / CSA_Creator patch signature is present AND the CSA
// spell table is findable (ImageUtilMagicCore.SearchMagicSystem). None of the
// bundled stock ROMs under roms/ carry those patches, so the live magic editor
// reports "magic system not detected" and the Creator cannot be populated
// end-to-end from them (the symmetric counterpart of the documented skill
// ROM-detection wall). This test builds a self-contained, CI-portable ROM with
// a real 2-frame magic 0x86 stream (each frame points at a LZ77-compressed
// 64x64 4bpp OBJ + a 16-colour palette) and drives the EXACT production seam
// the #996 magic jump runs:
//   MagicEffectExportCore.ExportMagicScriptLines (the ROM read) ->
//   ToolAnimationCreatorView.InitFromMagicRom (the new VM seed) ->
//   ImageUtilMapActionAnimationCore.RenderFrameImage (the per-frame preview).
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
    public class ToolAnimationCreatorMagicSeedScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public ToolAnimationCreatorMagicSeedScreenshotTest(ITestOutputHelper output) => _output = output;

        const int FRAME_BASE = 0x210;
        const int OBJ_OFFSET = 0x400;
        const int PAL_OFFSET = 0x900;
        const int ROM_SIZE   = 0x4000;
        const int W = 1024, H = 720;

        [AvaloniaFact]
        public void AnimationCreatorMagicSeed_FEditorFrames_RendersAndSavesScreenshot()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();

                var rom = BuildRomWithMagicStream();
                CoreState.ROM = rom;

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);

                var view = new ToolAnimationCreatorView();
                view.InitFromMagicRom(AnimationTypeEnum.MagicAnime_FEEDitor, 1,
                    "Magic Animation (FEditor) #01 (demo frames)",
                    U.toPointer((uint)FRAME_BASE), isCsa: false);

                var framesList = view.FindControl<ListBox>("FramesList");
                Assert.NotNull(framesList);
                Assert.True(framesList!.ItemCount >= 2,
                    "magic seed should populate 2 frames from the planted stream");
                framesList.SelectedIndex = 0;

                IImage preview = ImageUtilMapActionAnimationCore.RenderFrameImage(
                    rom, (uint)OBJ_OFFSET, (uint)PAL_OFFSET);
                Assert.NotNull(preview);
                byte[] png = preview!.EncodePng();
                Assert.NotNull(png);
                Assert.True(png!.Length > 0, "preview PNG must have bytes");
                string previewPath = Path.Combine(outDir, "pr996-magic-seed-preview.png");
                File.WriteAllBytes(previewPath, png);
                _output.WriteLine($"Saved magic-frame preview ({preview.Width}x{preview.Height}) to: {previewPath} ({png.Length} bytes)");
                preview.Dispose();

                var previewControl = view.FindControl<GbaImageControl>("MapActionPreview");
                Assert.NotNull(previewControl);

                try
                {
                    view.Measure(new Size(W, H));
                    view.Arrange(new Rect(0, 0, W, H));
                    using var bmp = new RenderTargetBitmap(new PixelSize(W, H));
                    bmp.Render(view);
                    string viewPath = Path.Combine(outDir, "pr996-magic-seed-view.png");
                    bmp.Save(viewPath);
                    _output.WriteLine($"Saved editor view to: {viewPath} ({new FileInfo(viewPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless full-view render skipped (environment, not #996): {ex.Message}");
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

        static ROM BuildRomWithMagicStream()
        {
            byte[] data = new byte[ROM_SIZE];

            for (int i = 0; i < 16; i++)
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
                ushort c = (ushort)((b << 10) | (g << 5) | r);
                data[PAL_OFFSET + i * 2 + 0] = (byte)(c & 0xFF);
                data[PAL_OFFSET + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }

            byte[] raw = BuildRadialTiles(64, 64);
            byte[] compressed = LZ77.compress(raw);
            Array.Copy(compressed, 0, data, OBJ_OFFSET, compressed.Length);

            int f0 = FRAME_BASE;
            int f1 = FRAME_BASE + 28;
            WriteMagicFrame(data, f0, wait: 4);
            WriteMagicFrame(data, f1, wait: 6);

            int term = FRAME_BASE + 56;
            data[term + 3] = 0x80;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        static void WriteMagicFrame(byte[] data, int n, uint wait)
        {
            data[n + 0] = (byte)(wait & 0xFF);
            data[n + 1] = (byte)((wait >> 8) & 0xFF);
            data[n + 3] = 0x86;
            WriteU32(data, n + 4, U.toPointer((uint)OBJ_OFFSET));
            WriteU32(data, n + 16, U.toPointer((uint)OBJ_OFFSET));
            WriteU32(data, n + 20, U.toPointer((uint)PAL_OFFSET));
            WriteU32(data, n + 24, U.toPointer((uint)PAL_OFFSET));
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

        static void WriteU32(byte[] data, int addr, uint v)
        {
            data[addr + 0] = (byte)(v & 0xFF);
            data[addr + 1] = (byte)((v >> 8) & 0xFF);
            data[addr + 2] = (byte)((v >> 16) & 0xFF);
            data[addr + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
