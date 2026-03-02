using System;
using System.IO;
using SkiaSharp;

namespace FEBuilderGBA.SkiaSharp
{
    /// <summary>
    /// SkiaSharp-backed implementation of IImage.
    /// Since SkiaSharp doesn't natively support indexed/palettized images,
    /// we store palette and index data separately and render to RGBA SKBitmap.
    /// </summary>
    public class SkiaImage : IImage
    {
        private SKBitmap _bitmap;
        private byte[] _indexData;   // For indexed images: palette indices per pixel
        private byte[] _gbaPalette;  // For indexed images: GBA-format palette
        private readonly bool _isIndexed;

        /// <summary>Create a blank RGBA image.</summary>
        public SkiaImage(int width, int height)
        {
            _bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _bitmap.Erase(SKColors.Transparent);
            _isIndexed = false;
            _indexData = null;
            _gbaPalette = Array.Empty<byte>();
        }

        /// <summary>Create an indexed image with GBA palette.</summary>
        public SkiaImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
        {
            _bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _bitmap.Erase(SKColors.Transparent);
            _isIndexed = true;
            _indexData = new byte[width * height];
            _gbaPalette = new byte[gbaPalette.Length];
            Array.Copy(gbaPalette, _gbaPalette, gbaPalette.Length);
        }

        /// <summary>Wrap an existing SKBitmap.</summary>
        internal SkiaImage(SKBitmap bitmap)
        {
            _bitmap = bitmap;
            _isIndexed = false;
            _indexData = null;
            _gbaPalette = Array.Empty<byte>();
        }

        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;
        public bool IsIndexed => _isIndexed;

        public byte[] GetPixelData()
        {
            if (_isIndexed && _indexData != null)
            {
                byte[] copy = new byte[_indexData.Length];
                Array.Copy(_indexData, copy, _indexData.Length);
                return copy;
            }
            // Return RGBA data
            var pixels = _bitmap.Pixels;
            byte[] data = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                data[i * 4] = pixels[i].Red;
                data[i * 4 + 1] = pixels[i].Green;
                data[i * 4 + 2] = pixels[i].Blue;
                data[i * 4 + 3] = pixels[i].Alpha;
            }
            return data;
        }

        public void SetPixelData(byte[] data)
        {
            if (_isIndexed && _indexData != null)
            {
                int len = Math.Min(data.Length, _indexData.Length);
                Array.Copy(data, _indexData, len);
                RenderIndexedToRGBA();
                return;
            }
            // Set RGBA data
            var pixels = new SKColor[_bitmap.Width * _bitmap.Height];
            for (int i = 0; i < pixels.Length && i * 4 + 3 < data.Length; i++)
            {
                pixels[i] = new SKColor(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
            }
            _bitmap.Pixels = pixels;
        }

        public byte[] GetPaletteGBA()
        {
            if (_gbaPalette == null) return Array.Empty<byte>();
            byte[] copy = new byte[_gbaPalette.Length];
            Array.Copy(_gbaPalette, copy, _gbaPalette.Length);
            return copy;
        }

        public void SetPaletteGBA(byte[] gbaPalette)
        {
            _gbaPalette = new byte[gbaPalette.Length];
            Array.Copy(gbaPalette, _gbaPalette, gbaPalette.Length);
            if (_isIndexed)
                RenderIndexedToRGBA();
        }

        public byte[] GetPaletteRGBA()
        {
            if (_gbaPalette == null || _gbaPalette.Length == 0)
                return Array.Empty<byte>();
            int colorCount = _gbaPalette.Length / 2;
            byte[] rgba = new byte[colorCount * 4];
            for (int i = 0; i < colorCount; i++)
            {
                ushort gbaColor = (ushort)(_gbaPalette[i * 2] | (_gbaPalette[i * 2 + 1] << 8));
                GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
            // First color is transparent
            if (colorCount > 0)
                rgba[3] = 0;
            return rgba;
        }

        public void Save(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            SKEncodedImageFormat format = ext switch
            {
                ".bmp" => SKEncodedImageFormat.Bmp,
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };
            using var image = SKImage.FromBitmap(_bitmap);
            using var data = image.Encode(format, 100);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        public byte[] EncodePng()
        {
            using var image = SKImage.FromBitmap(_bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }

        /// <summary>
        /// Re-render the indexed pixel data to the RGBA SKBitmap using the current palette.
        /// </summary>
        internal void RenderIndexedToRGBA()
        {
            if (_indexData == null || _gbaPalette == null) return;
            int colorCount = _gbaPalette.Length / 2;
            var pixels = new SKColor[_bitmap.Width * _bitmap.Height];
            for (int i = 0; i < pixels.Length && i < _indexData.Length; i++)
            {
                int idx = _indexData[i];
                if (idx < colorCount && idx > 0)
                {
                    ushort gbaColor = (ushort)(_gbaPalette[idx * 2] | (_gbaPalette[idx * 2 + 1] << 8));
                    GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                    pixels[i] = new SKColor(r, g, b, 255);
                }
                else
                {
                    pixels[i] = SKColors.Transparent; // index 0 = transparent
                }
            }
            _bitmap.Pixels = pixels;
        }

        private static void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }
    }
}
