// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform glyph rasterizer (#796): a SkiaSharp implementation of
// IFontRasterizer that reproduces the WinForms ImageUtil.AutoGenerateFont +
// Image4ToByte pipeline byte-for-byte, so translation-font auto-generation
// works on Windows, Linux and macOS.
//
// The algorithm mirrors FEBuilderGBA/ImageUtil.cs:
//   AutoGenerateTextFont  (3732) — text/serif variant
//   AutoGenerateItemFont  (3803) — item variant (glyph + outline ring)
//   IsFontColorFore       (3798) — threshold (R < 0xA0)
//   Image4ToByte          (1579) — 64-byte 2bpp pack (4 px/byte)
// GDI DrawString draws black-on-white at the top-left; we reproduce that with
// SkiaSharp anti-aliased text and the same scale / composite offsets so the
// resulting palette-index tiles match the WF output's shape.
using System;
using System.IO;
using SkiaSharp;

namespace FEBuilderGBA.SkiaSharp
{
    /// <summary>
    /// SkiaSharp <see cref="IFontRasterizer"/>. Renders a character to a 16x16
    /// anti-aliased black-on-white base bitmap (matching GDI
    /// <c>DrawString(Point(0,0))</c>), then applies the exact WF scale /
    /// composite / threshold / pack steps to emit a 64-byte GBA font tile.
    /// </summary>
    public sealed class SkiaFontRasterizer : IFontRasterizer
    {
        // Threshold mirrors WF ImageUtil.IsFontColorFore: a pixel counts as
        // "foreground" (part of the glyph) when its red channel < 0xA0.
        static bool IsFontColorFore(SKColor c) => c.Red < 0xA0;

        /// <inheritdoc/>
        public byte[] RasterizeGlyph(FontSpec font, string character, bool isItemFont,
            int verticalOffset, out int glyphWidth)
        {
            // Render the character black-on-white into a 16x16 base bitmap,
            // matching GDI's DrawString at the top-left corner.
            using SKBitmap baseBitmap = RenderBase(font, character);

            return isItemFont
                ? GenerateItemFont(baseBitmap, verticalOffset, out glyphWidth)
                : GenerateTextFont(baseBitmap, verticalOffset, out glyphWidth);
        }

        // ---------- base render (GDI DrawString equivalent) ----------

        static SKBitmap RenderBase(FontSpec font, string character)
        {
            var bmp = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.White);

                using SKTypeface typeface = ResolveTypeface(font);
                using var skFont = new SKFont(typeface, font.Size <= 0 ? 12f : font.Size)
                {
                    // Pinned-deterministic settings so the same character renders
                    // identically across Windows / Linux / macOS CI.
                    Edging = SKFontEdging.Antialias,
                    Hinting = SKFontHinting.Full,
                    Subpixel = false,
                };
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.Black,
                };

                // Baseline = -Ascent puts the glyph top at y=0, mirroring GDI
                // DrawString(Point(0,0)) which is also top-aligned.
                skFont.GetFontMetrics(out SKFontMetrics metrics);
                float baseline = -metrics.Ascent;

                if (!string.IsNullOrEmpty(character))
                {
                    canvas.DrawText(character, 0, baseline, skFont, paint);
                }
                canvas.Flush();
            }
            return bmp;
        }

        static SKTypeface ResolveTypeface(FontSpec font)
        {
            try
            {
                if (font.FontFileData != null && font.FontFileData.Length > 0)
                {
                    var tf = SKTypeface.FromStream(new MemoryStream(font.FontFileData));
                    if (tf != null) return tf;
                }
                if (!string.IsNullOrEmpty(font.FontFilePath) && File.Exists(font.FontFilePath))
                {
                    var tf = SKTypeface.FromFile(font.FontFilePath);
                    if (tf != null) return tf;
                }
                if (!string.IsNullOrEmpty(font.FamilyName))
                {
                    var weight = font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
                    var slant = font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                    var tf = SKTypeface.FromFamilyName(font.FamilyName, weight,
                        SKFontStyleWidth.Normal, slant);
                    // FromFamilyName never returns null (falls back to default),
                    // but guard anyway.
                    if (tf != null) return tf;
                }
            }
            catch
            {
                // fall through to the default typeface — never crash.
            }
            return SKTypeface.Default;
        }

        // ---------- text/serif font (mirrors AutoGenerateTextFont) ----------

        static byte[] GenerateTextFont(SKBitmap baseBitmap, int verticalOffset, out int glyphWidth)
        {
            // Step 1: scale base 16x16 -> drawn into a 16x16 surface at dest
            // rect (0,0,16,22). The bottom 6 px are clipped off the surface
            // (WF draws a 16x22 image into a 16-tall bitmap).
            using SKBitmap scaleBitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(scaleBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(baseBitmap, new SKRect(0, 0, 16, 22));
                canvas.Flush();
            }

            // Step 2: composite onto a fresh white 16x16 at (-2, verticalOffset).
            using SKBitmap fontBitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(fontBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(scaleBitmap, -2, verticalOffset);
                canvas.Flush();
            }

            // Step 3: threshold to palette indices (0 = bg, 3 = fore) and pack.
            byte[] idx = new byte[16 * 16];
            int maxWidth = 0;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte a = IsFontColorFore(fontBitmap.GetPixel(x, y)) ? (byte)3 : (byte)0;
                    if (a == 3 && x > maxWidth) maxWidth = x;
                    idx[x + y * 16] = a;
                }
            }

            glyphWidth = Math.Min(maxWidth + 1, 16);
            return Image4ToByte(idx);
        }

        // ---------- item font (mirrors AutoGenerateItemFont) ----------

        static byte[] GenerateItemFont(SKBitmap baseBitmap, int verticalOffset, out int glyphWidth)
        {
            // Step 1: scale base 16x16 -> 16x16 (dest rect 0,0,16,16).
            using SKBitmap scaleBitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(scaleBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(baseBitmap, new SKRect(0, 0, 16, 16));
                canvas.Flush();
            }

            // Step 2: composite onto a fresh white 16x16 at (-1, 2 + verticalOffset).
            using SKBitmap fontBitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(fontBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(scaleBitmap, -1, 2 + verticalOffset);
                canvas.Flush();
            }

            // Step 3: glyph fill -> idx 2; background with a foreground 4-neighbour
            // -> idx 3 (the outline ring); otherwise idx 0.
            byte[] idx = new byte[16 * 16];
            int maxWidth = 0;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte a;
                    if (IsFontColorFore(fontBitmap.GetPixel(x, y)))
                    {
                        a = 2; // glyph fill
                    }
                    else
                    {
                        a = 0; // background
                        if (x > 1 && IsFontColorFore(fontBitmap.GetPixel(x - 1, y))) a = 3;
                        if (x < 15 && IsFontColorFore(fontBitmap.GetPixel(x + 1, y))) a = 3;
                        if (y > 0 && IsFontColorFore(fontBitmap.GetPixel(x, y - 1))) a = 3;
                        if (y < 10 && IsFontColorFore(fontBitmap.GetPixel(x, y + 1))) a = 3;
                    }

                    if (a == 2 && x > maxWidth) maxWidth = x;
                    idx[x + y * 16] = a;
                }
            }

            glyphWidth = Math.Min(maxWidth + 1, 16);
            return Image4ToByte(idx);
        }

        // ---------- pack (mirrors ImageUtil.Image4ToByte) ----------

        /// <summary>
        /// Pack a 16x16 palette-index buffer (row-major, one byte per pixel)
        /// into the GBA 2bpp font tile: 256 pixels at 2 bits/pixel packed 4
        /// pixels per byte (low two bits = left-most pixel) = 64 bytes. Mirrors
        /// WF <c>ImageUtil.Image4ToByte</c> exactly.
        /// </summary>
        static byte[] Image4ToByte(byte[] idx)
        {
            byte[] data = new byte[16 / 4 * 16]; // 64 bytes
            int nn = 0;
            for (int y = 0; y < 16; y++)
            {
                int n = 0;
                byte one = 0;
                for (int x = 0; x < 16; x++)
                {
                    byte a = idx[x + y * 16];
                    one = (byte)(one | ((a & 0x03) << (n * 2)));
                    n++;
                    if (n >= 4)
                    {
                        data[nn++] = one;
                        n = 0;
                        one = 0;
                    }
                }
            }
            return data;
        }
    }
}
