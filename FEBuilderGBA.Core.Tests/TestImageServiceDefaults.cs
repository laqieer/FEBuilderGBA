using System;

namespace FEBuilderGBA.Core.Tests
{
    internal static class TestImageServiceDefaults
    {
        public static IImage CreateImage(int width, int height)
            => new BasicTestImage(width, height, indexed: false, gbaPalette: Array.Empty<byte>());

        public static IImage CreateIndexedImage(int width, int height, byte[]? gbaPalette = null)
            => new BasicTestImage(width, height, indexed: true, gbaPalette: gbaPalette ?? Array.Empty<byte>());

        public static byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount)
        {
            int count = Math.Max(0, colorCount);
            var rgba = new byte[count * 4];
            for (int i = 0; i < count; i++)
            {
                int src = i * 2;
                int dst = i * 4;
                if (src + 1 >= gbaPalette.Length) break;
                ushort gba = (ushort)(gbaPalette[src] | (gbaPalette[src + 1] << 8));
                rgba[dst + 0] = (byte)((gba & 0x1F) << 3);
                rgba[dst + 1] = (byte)(((gba >> 5) & 0x1F) << 3);
                rgba[dst + 2] = (byte)(((gba >> 10) & 0x1F) << 3);
                rgba[dst + 3] = 255;
            }
            return rgba;
        }

        public static byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount)
        {
            int count = Math.Max(0, colorCount);
            var gba = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                int src = i * 4;
                if (src + 2 >= rgbaPalette.Length) break;
                ushort color = (ushort)(((rgbaPalette[src + 0] >> 3) & 0x1F)
                    | (((rgbaPalette[src + 1] >> 3) & 0x1F) << 5)
                    | (((rgbaPalette[src + 2] >> 3) & 0x1F) << 10));
                gba[i * 2] = (byte)(color & 0xFF);
                gba[i * 2 + 1] = (byte)(color >> 8);
            }
            return gba;
        }

        private sealed class BasicTestImage : IImage
        {
            private byte[] _pixels;
            private byte[] _palette;

            public BasicTestImage(int width, int height, bool indexed, byte[] gbaPalette)
            {
                Width = width;
                Height = height;
                IsIndexed = indexed;
                _palette = gbaPalette;
                _pixels = new byte[Math.Max(0, width) * Math.Max(0, height) * (indexed ? 1 : 4)];
            }

            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed { get; }

            public byte[] GetPixelData() => _pixels;
            public void SetPixelData(byte[] data) => _pixels = data;
            public byte[] GetPaletteGBA() => _palette;
            public void SetPaletteGBA(byte[] gbaPalette) => _palette = gbaPalette;
            public byte[] GetPaletteRGBA() => GBAPaletteToRGBA(_palette, _palette.Length / 2);
            public void Save(string filePath) { }
            public byte[] EncodePng() => Array.Empty<byte>();
            public void Dispose() { }
        }
    }
}
