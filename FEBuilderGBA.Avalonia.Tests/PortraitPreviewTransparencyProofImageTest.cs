using System;
using System.IO;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PortraitPreviewTransparencyProofImageTest
    {
        readonly ITestOutputHelper _output;

        public PortraitPreviewTransparencyProofImageTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RenderPortraitPreviewTransparencyProofImage()
        {
            IDisposable? restore = null;
            try
            {
                restore = EnsureImageService();
                var loadResult = MakeSyntheticPortraitLoadResult(96, 80);
                byte[] oldRgba = PortraitImportHelper.ReconstructRgbaWithPaletteZeroTransparent(loadResult);

                using IImage newPreview = PortraitImportHelper.BuildPreviewImage(loadResult);
                Assert.NotNull(newPreview);
                byte[] newRgba = newPreview.GetPixelData();

                byte[] pngBytes = RenderProofPng(oldRgba, newRgba, loadResult.Width, loadResult.Height);
                Assert.NotNull(pngBytes);
                Assert.True(pngBytes.Length > 0);

                try
                {
                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, "pr-portrait-preview-1847.png");
                    File.WriteAllBytes(outPath, pngBytes);
                    _output.WriteLine($"Saved proof image to: {outPath} ({pngBytes.Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Proof image not saved to disk (best-effort): {ex.Message}");
                }
            }
            catch (TypeInitializationException ex)
                when (ex.InnerException is InvalidOperationException ioe
                      && ioe.Message.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"SKIP: native libSkiaSharp incompatible on this host: {ioe.Message}");
                return;
            }
            finally
            {
                restore?.Dispose();
            }
        }

        static IDisposable EnsureImageService()
        {
            IImageService? prev = CoreState.ImageService;
            if (prev == null)
                CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService? _prev;
            public RestoreImageService(IImageService? prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        static ImageImportService.LoadResult MakeSyntheticPortraitLoadResult(int w, int h)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = 0;
                rgba[i * 4 + 1] = 220;
                rgba[i * 4 + 2] = 80;
                rgba[i * 4 + 3] = 255;
            }
            for (int y = 16; y < 64; y++)
            {
                for (int x = 16; x < 64; x++)
                {
                    int off = (y * w + x) * 4;
                    rgba[off + 0] = 210;
                    rgba[off + 1] = 80;
                    rgba[off + 2] = 40;
                    rgba[off + 3] = 255;
                }
            }

            var qr = DecreaseColorCore.Quantize(rgba, w, h, 16);
            Assert.NotNull(qr);
            Assert.DoesNotContain((byte)0, qr.IndexData);

            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = qr.IndexData,
                GBAPalette = qr.GBAPalette,
                RGBAPixels = rgba,
                SourcePath = "C:\\synthetic\\portrait-preview-1847.png",
            };
        }

        static byte[] RenderProofPng(byte[] oldRgba, byte[] newRgba, int imageW, int imageH)
        {
            const int scale = 3;
            int panelW = imageW * scale;
            int panelH = imageH * scale;
            int canvasW = panelW * 2 + 96;
            int canvasH = panelH + 96;

            using var bmp = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(new SKColor(0x24, 0x26, 0x2B));

            using var title = new SKPaint { Color = SKColors.White, TextSize = 22, IsAntialias = true, FakeBoldText = true };
            using var label = new SKPaint { Color = new SKColor(0xD8, 0xDE, 0xE9), TextSize = 16, IsAntialias = true };
            canvas.DrawText("Portrait Import Wizard quantized preview transparency (#1847)", 24, 34, title);
            canvas.DrawText("Before: raw-quantized index 0 rule", 24, 66, label);
            canvas.DrawText("After: import color-key -> quantize -> index 0 transparent", panelW + 72, 66, label);

            DrawChecker(canvas, 24, 80, panelW, panelH);
            DrawChecker(canvas, panelW + 72, 80, panelW, panelH);
            DrawScaledRgba(canvas, oldRgba, imageW, imageH, 24, 80, scale);
            DrawScaledRgba(canvas, newRgba, imageW, imageH, panelW + 72, 80, scale);

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        static void DrawChecker(SKCanvas canvas, int x0, int y0, int w, int h)
        {
            using var light = new SKPaint { Color = new SKColor(0xF0, 0xF0, 0xF0) };
            using var dark = new SKPaint { Color = new SKColor(0xB0, 0xB0, 0xB0) };
            const int cell = 12;
            for (int y = 0; y < h; y += cell)
            {
                for (int x = 0; x < w; x += cell)
                {
                    var paint = ((x / cell + y / cell) & 1) == 0 ? light : dark;
                    canvas.DrawRect(x0 + x, y0 + y, Math.Min(cell, w - x), Math.Min(cell, h - y), paint);
                }
            }
        }

        static void DrawScaledRgba(SKCanvas canvas, byte[] rgba, int w, int h, int x0, int y0, int scale)
        {
            using var paint = new SKPaint { IsAntialias = false };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int off = (y * w + x) * 4;
                    byte a = rgba[off + 3];
                    if (a == 0) continue;
                    paint.Color = new SKColor(rgba[off], rgba[off + 1], rgba[off + 2], a);
                    canvas.DrawRect(x0 + x * scale, y0 + y * scale, scale, scale, paint);
                }
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
