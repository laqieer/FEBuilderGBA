using System;
using System.IO;
using SkiaSharp;

namespace FEBuilderGBA.SkiaSharp
{
    /// <summary>
    /// SkiaSharp implementation of IImageService for cross-platform GBA image operations.
    /// </summary>
    public class SkiaImageService : IImageService
    {
        public IImage CreateImage(int width, int height)
        {
            return new SkiaImage(width, height);
        }

        public IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
        {
            return new SkiaImage(width, height, gbaPalette, paletteColorCount);
        }

        public IImage LoadImage(string filePath)
        {
            var bitmap = SKBitmap.Decode(filePath);
            if (bitmap == null)
                throw new IOException($"Failed to load image: {filePath}");
            return new SkiaImage(bitmap);
        }

        public IImage LoadImageFromBytes(byte[] pngData)
        {
            var bitmap = SKBitmap.Decode(pngData);
            if (bitmap == null)
                throw new InvalidOperationException("Failed to decode image from byte data.");
            return new SkiaImage(bitmap);
        }

        public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }

        public ushort RGBAToGBAColor(byte r, byte g, byte b)
        {
            return (ushort)(
                ((r >> 3) & 0x1F) |
                (((g >> 3) & 0x1F) << 5) |
                (((b >> 3) & 0x1F) << 10)
            );
        }

        public IImage Decode4bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
        {
            var image = new SkiaImage(width, height, gbaPalette, gbaPalette.Length / 2);
            byte[] pixels = new byte[width * height];

            int tileW = width / 8;
            int tileH = height / 8;
            int pos = offset;

            for (int ty = 0; ty < tileH; ty++)
            {
                for (int tx = 0; tx < tileW; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px += 2)
                        {
                            if (pos >= tileData.Length) goto done;
                            byte b = tileData[pos++];
                            int x = tx * 8 + px;
                            int y = ty * 8 + py;
                            if (x < width && y < height)
                                pixels[y * width + x] = (byte)(b & 0x0F);
                            if (x + 1 < width && y < height)
                                pixels[y * width + x + 1] = (byte)((b >> 4) & 0x0F);
                        }
                    }
                }
            }
            done:
            image.SetPixelData(pixels);
            return image;
        }

        public IImage Decode8bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
        {
            var image = new SkiaImage(width, height, gbaPalette, gbaPalette.Length / 2);
            byte[] pixels = new byte[width * height];

            int tileW = width / 8;
            int tileH = height / 8;
            int pos = offset;

            for (int ty = 0; ty < tileH; ty++)
            {
                for (int tx = 0; tx < tileW; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            if (pos >= tileData.Length) goto done;
                            int x = tx * 8 + px;
                            int y = ty * 8 + py;
                            if (x < width && y < height)
                                pixels[y * width + x] = tileData[pos];
                            pos++;
                        }
                    }
                }
            }
            done:
            image.SetPixelData(pixels);
            return image;
        }

        public IImage Decode8bppLinear(byte[] data, int offset, int width, int height, byte[] gbaPalette)
        {
            var image = new SkiaImage(width, height, gbaPalette, gbaPalette.Length / 2);
            byte[] pixels = new byte[width * height];
            int len = Math.Min(pixels.Length, data.Length - offset);
            if (len > 0)
                Array.Copy(data, offset, pixels, 0, len);
            image.SetPixelData(pixels);
            return image;
        }

        public byte[] Encode4bppTiles(IImage image)
        {
            byte[] pixels = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;
            int tileW = width / 8;
            int tileH = height / 8;
            byte[] result = new byte[tileW * tileH * 32]; // 32 bytes per 8x8 4bpp tile
            int pos = 0;

            for (int ty = 0; ty < tileH; ty++)
            {
                for (int tx = 0; tx < tileW; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px += 2)
                        {
                            int x = tx * 8 + px;
                            int y = ty * 8 + py;
                            byte lo = (x < width && y < height) ? (byte)(pixels[y * width + x] & 0x0F) : (byte)0;
                            byte hi = (x + 1 < width && y < height) ? (byte)(pixels[y * width + x + 1] & 0x0F) : (byte)0;
                            if (pos < result.Length)
                                result[pos++] = (byte)(lo | (hi << 4));
                        }
                    }
                }
            }
            return result;
        }

        public byte[] Encode8bppTiles(IImage image)
        {
            byte[] pixels = image.GetPixelData();
            int width = image.Width;
            int height = image.Height;
            int tileW = width / 8;
            int tileH = height / 8;
            byte[] result = new byte[tileW * tileH * 64]; // 64 bytes per 8x8 8bpp tile
            int pos = 0;

            for (int ty = 0; ty < tileH; ty++)
            {
                for (int tx = 0; tx < tileW; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            int x = tx * 8 + px;
                            int y = ty * 8 + py;
                            if (pos < result.Length)
                                result[pos++] = (x < width && y < height) ? pixels[y * width + x] : (byte)0;
                        }
                    }
                }
            }
            return result;
        }

        public byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount)
        {
            byte[] rgba = new byte[colorCount * 4];
            for (int i = 0; i < colorCount && i * 2 + 1 < gbaPalette.Length; i++)
            {
                ushort gbaColor = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }

        public byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount)
        {
            byte[] gba = new byte[colorCount * 2];
            for (int i = 0; i < colorCount && i * 4 + 2 < rgbaPalette.Length; i++)
            {
                ushort color = RGBAToGBAColor(rgbaPalette[i * 4], rgbaPalette[i * 4 + 1], rgbaPalette[i * 4 + 2]);
                gba[i * 2] = (byte)(color & 0xFF);
                gba[i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
            return gba;
        }
    }
}
