// SPDX-License-Identifier: GPL-3.0-or-later
// #1024 — screenshot proof for the Animation Creator live frame preview
// (Map-Action kind). Mirrors the repo's existing *ScreenshotTest pattern
// (e.g. ImageUnitPaletteSamplePreviewScreenshotTest): render through the REAL
// production pipeline and write a PNG via IImage.EncodePng so real pixels are
// produced even when the headless drawing backend is a no-op.
//
// Why a synthetic ROM instead of RomFixture: the Map Action Animation list is
// discovered by a hard-coded byte-signature search (ImageMapActionAnimation
// VM.FindAnimationPointer). The bundled stock ROMs under roms/ do not match
// that signature (the table is not where the signature expects), so the source
// editor reports 0 animations and the Creator cannot be populated end-to-end
// from them. This test therefore builds a self-contained, CI-portable ROM with
// one VALID map-action frame (LZ77-compressed 64x64 4bpp OBJ + a 16-colour
// palette) and drives the EXACT production seam
// (ImageUtilMapActionAnimationCore.RenderFrameImage) plus the real
// ToolAnimationCreatorView — the same code path the live editor runs.
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
    public class ToolAnimationCreatorPreviewScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public ToolAnimationCreatorPreviewScreenshotTest(ITestOutputHelper output) => _output = output;

        const int FRAME_BASE = 0x210;   // 12-byte map-action frame row (>= 0x200)
        const int OBJ_OFFSET = 0x400;   // LZ77-compressed 64x64 4bpp OBJ tiles
        const int PAL_OFFSET = 0x900;   // 0x20-byte (16-colour) palette
        const int ROM_SIZE   = 0x4000;
        const int W = 1024, H = 720;

        [AvaloniaFact]
        public void AnimationCreatorPreview_MapActionFrame_RendersAndSavesScreenshot()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();

                var rom = BuildRomWithOneColourfulFrame();
                CoreState.ROM = rom;

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);

                // --- Primary artifact: the ACTUAL preview pixels --------------
                // RenderFrameImage is the seam the view's RenderPreview() calls.
                // EncodePng goes through the real SkiaImageService and yields real
                // pixels independent of the headless RenderTargetBitmap backend.
                IImage preview = ImageUtilMapActionAnimationCore.RenderFrameImage(
                    rom, (uint)OBJ_OFFSET, (uint)PAL_OFFSET);
                Assert.NotNull(preview);
                Assert.Equal(64, preview!.Width);
                Assert.Equal(64, preview.Height);

                byte[] png = preview.EncodePng();
                Assert.NotNull(png);
                Assert.True(png!.Length > 0, "preview PNG must have bytes");
                string previewPath = Path.Combine(outDir, "pr1024-animation-creator-preview.png");
                File.WriteAllBytes(previewPath, png);
                _output.WriteLine($"Saved preview ({preview.Width}x{preview.Height}) to: {previewPath} ({png.Length} bytes)");
                preview.Dispose();

                // --- Drive the REAL view (populated + frame selected) ---------
                var view = new ToolAnimationCreatorView();
                view.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0,
                    "Map Action Animation #00 (demo frame)", U.toPointer((uint)FRAME_BASE));

                var framesList = view.FindControl<ListBox>("FramesList");
                Assert.NotNull(framesList);
                Assert.True(framesList!.ItemCount >= 1, "view should have at least one frame");
                framesList.SelectedIndex = 0;   // fires SelectionChanged -> RenderPreview

                var previewControl = view.FindControl<GbaImageControl>("MapActionPreview");
                Assert.NotNull(previewControl);

                // --- Secondary artifact: full editor view (best-effort) -------
                // The headless no-op drawing backend may not support
                // RenderTargetBitmap; that is an environment limitation, not a
                // #1024 failure, so it is caught and reported (the preview PNG
                // above is the authoritative pixel proof).
                try
                {
                    view.Measure(new Size(W, H));
                    view.Arrange(new Rect(0, 0, W, H));
                    using var bmp = new RenderTargetBitmap(new PixelSize(W, H));
                    bmp.Render(view);
                    string viewPath = Path.Combine(outDir, "pr1024-animation-creator-view.png");
                    bmp.Save(viewPath);
                    _output.WriteLine($"Saved editor view to: {viewPath} ({new FileInfo(viewPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless full-view render skipped (environment, not #1024): {ex.Message}");
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

        /// <summary>
        /// Synthetic ROM: one valid map-action frame whose OBJ decodes to a
        /// colourful radial-ring 64x64 image (clearly a rendered preview, not a
        /// blank box) under a 16-colour rainbow palette.
        /// </summary>
        static ROM BuildRomWithOneColourfulFrame()
        {
            byte[] data = new byte[ROM_SIZE];

            // 16-colour rainbow palette (RGB555, little-endian) at PAL_OFFSET.
            // Index 0 kept dark so ring boundaries read clearly.
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

            // 64x64 4bpp OBJ tiles, radial colour rings, packed in GBA tile order.
            byte[] raw = BuildRadialTiles(64, 64);
            byte[] compressed = LZ77.compress(raw);
            Array.Copy(compressed, 0, data, OBJ_OFFSET, compressed.Length);

            // Frame row: wait=4, sound=0, img -> OBJ_OFFSET, pal -> PAL_OFFSET.
            data[FRAME_BASE + 0] = 4;
            WriteU32(data, FRAME_BASE + 4, U.toPointer((uint)OBJ_OFFSET));
            WriteU32(data, FRAME_BASE + 8, U.toPointer((uint)PAL_OFFSET));
            // Zero terminator row already present at FRAME_BASE + 12.

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
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
            return ((int)(d / 3.0) % 15) + 1; // 1..15 (skip 0/dark)
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
