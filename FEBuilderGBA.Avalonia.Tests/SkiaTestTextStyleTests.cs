using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SkiaTestTextStyleTests
    {
        const int Width = 320;
        const int Height = 80;
        const string Sample = "FEBuilderGBA #2067";
        const float ExpectedMeasurement = 164.59277f;
        const float ExpectedBoundsLeft = 0f;
        const float ExpectedBoundsTop = -14f;
        const float ExpectedBoundsRight = 164.6084f;
        const float ExpectedBoundsBottom = 1f;
        const string ExpectedPixelHash = "19CA4B665632E6B06F4D5949EE908CDC358FDD4109D061CEFCEA9889FADE7E84";

        [Fact]
        public void BundledFont_CurrentApiStyle_MatchesLegacyMeasurementBoundsAndPixels()
        {
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "Tuffy-Regular.ttf");
            Assert.True(File.Exists(fontPath), $"bundled font missing at {fontPath}");

            using var typeface = SKTypeface.FromFile(fontPath);
            using var style = SkiaTestTextStyle.Create(
                new SKColor(0x12, 0x34, 0x56),
                18,
                typeface: typeface,
                bold: true);

            float measurement = style.MeasureText(Sample, out SKRect bounds);
            Assert.Equal(ExpectedMeasurement, measurement);
            Assert.Equal(ExpectedBoundsLeft, bounds.Left);
            Assert.Equal(ExpectedBoundsTop, bounds.Top);
            Assert.Equal(ExpectedBoundsRight, bounds.Right);
            Assert.Equal(ExpectedBoundsBottom, bounds.Bottom);

            using var bmp = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(new SKColor(0xEE, 0xEE, 0xEE));
                style.DrawText(canvas, Sample, 17, 43);
                canvas.Flush();
            }

            Assert.Equal(Width, bmp.Width);
            Assert.Equal(Height, bmp.Height);
            Assert.Equal(ExpectedPixelHash, PixelHash(bmp));
        }

        static string PixelHash(SKBitmap bitmap)
        {
            byte[] pixels = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            return Convert.ToHexString(SHA256.HashData(pixels));
        }
    }
}
