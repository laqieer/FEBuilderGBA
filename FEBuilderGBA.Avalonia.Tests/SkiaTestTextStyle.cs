using System;
using SkiaSharp;

namespace FEBuilderGBA.Avalonia.Tests
{
    internal sealed class SkiaTestTextStyle : IDisposable
    {
        readonly SKTypeface? _ownedTypeface;

        SkiaTestTextStyle(
            SKColor color,
            float size,
            SKTypeface typeface,
            bool bold,
            bool ownsTypeface)
        {
            if (ownsTypeface)
                _ownedTypeface = typeface;

            Font = new SKFont(typeface, size)
            {
                Edging = SKFontEdging.Antialias,
                Embolden = bold,
            };
            Paint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
            };
        }

        public SKFont Font { get; }

        public SKPaint Paint { get; }

        public float TextSize => Font.Size;

        public SKTypeface? Typeface => Font.Typeface;

        public static SkiaTestTextStyle Create(
            SKColor color,
            float size,
            bool bold = false,
            string? familyName = null,
            SKTypeface? typeface = null)
        {
            if (typeface != null)
                return new SkiaTestTextStyle(color, size, typeface, bold, ownsTypeface: false);

            if (!string.IsNullOrEmpty(familyName))
            {
                SKTypeface createdTypeface = SKTypeface.FromFamilyName(familyName);
                return new SkiaTestTextStyle(color, size, createdTypeface, bold, ownsTypeface: true);
            }

            return new SkiaTestTextStyle(color, size, SKTypeface.Default, bold, ownsTypeface: false);
        }

        public float MeasureText(string text)
            => Font.MeasureText(text, Paint);

        public float MeasureText(string text, out SKRect bounds)
            => Font.MeasureText(text, out bounds, Paint);

        public void DrawText(SKCanvas canvas, string text, float x, float y)
            => canvas.DrawText(text, x, y, Font, Paint);

        public void Dispose()
        {
            Font.Dispose();
            Paint.Dispose();
            _ownedTypeface?.Dispose();
        }
    }

    internal static class SkiaTestTextStyleCanvasExtensions
    {
        public static void DrawText(
            this SKCanvas canvas,
            string text,
            float x,
            float y,
            SkiaTestTextStyle style)
            => style.DrawText(canvas, text, x, y);
    }
}
