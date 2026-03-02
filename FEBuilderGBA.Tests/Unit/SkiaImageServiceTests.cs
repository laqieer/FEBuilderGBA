using Xunit;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Tests.Unit
{
    public class SkiaImageServiceTests
    {
        [Fact]
        public void GBAColorRoundtrip()
        {
            var svc = new SkiaImageService();
            // Red: 248 >> 3 = 31, so 31 << 3 = 248 roundtrips perfectly
            ushort gba = svc.RGBAToGBAColor(248, 0, 0);
            svc.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
            Assert.Equal(248, r);
            Assert.Equal(0, g);
            Assert.Equal(0, b);
        }

        [Fact]
        public void GBAColorRoundtrip_White()
        {
            var svc = new SkiaImageService();
            ushort gba = svc.RGBAToGBAColor(248, 248, 248);
            svc.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
            Assert.Equal(248, r);
            Assert.Equal(248, g);
            Assert.Equal(248, b);
        }

        [Fact]
        public void GBAColorRoundtrip_Green()
        {
            var svc = new SkiaImageService();
            ushort gba = svc.RGBAToGBAColor(0, 248, 0);
            svc.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
            Assert.Equal(0, r);
            Assert.Equal(248, g);
            Assert.Equal(0, b);
        }

        [Fact]
        public void GBAColorRoundtrip_Blue()
        {
            var svc = new SkiaImageService();
            ushort gba = svc.RGBAToGBAColor(0, 0, 248);
            svc.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
            Assert.Equal(0, r);
            Assert.Equal(0, g);
            Assert.Equal(248, b);
        }

        [Fact]
        public void CreateImage_ReturnsCorrectDimensions()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(16, 16);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);
            Assert.False(img.IsIndexed);
        }

        [Fact]
        public void CreateImage_DifferentSizes()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(240, 160);
            Assert.Equal(240, img.Width);
            Assert.Equal(160, img.Height);
        }

        [Fact]
        public void CreateIndexedImage_ReturnsCorrectDimensions()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32]; // 16 colors * 2 bytes
            using var img = svc.CreateIndexedImage(8, 8, palette, 16);
            Assert.Equal(8, img.Width);
            Assert.Equal(8, img.Height);
            Assert.True(img.IsIndexed);
        }

        [Fact]
        public void CreateIndexedImage_PaletteRoundtrip()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32];
            // Set color 1 to red (GBA format)
            ushort red = svc.RGBAToGBAColor(248, 0, 0);
            palette[2] = (byte)(red & 0xFF);
            palette[3] = (byte)(red >> 8);

            using var img = svc.CreateIndexedImage(8, 8, palette, 16);
            byte[] gotPalette = img.GetPaletteGBA();
            Assert.Equal(palette, gotPalette);
        }

        [Fact]
        public void IndexedImage_SetAndGetPixelData()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32];
            using var img = svc.CreateIndexedImage(8, 8, palette, 16);

            byte[] indices = new byte[64];
            for (int i = 0; i < 64; i++)
                indices[i] = (byte)(i % 16);

            img.SetPixelData(indices);
            byte[] got = img.GetPixelData();
            Assert.Equal(indices, got);
        }

        [Fact]
        public void RGBAImage_SetAndGetPixelData()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(2, 2);

            // Set 4 pixels: red, green, blue, white
            byte[] data = new byte[16];
            // Pixel 0: red
            data[0] = 255; data[1] = 0; data[2] = 0; data[3] = 255;
            // Pixel 1: green
            data[4] = 0; data[5] = 255; data[6] = 0; data[7] = 255;
            // Pixel 2: blue
            data[8] = 0; data[9] = 0; data[10] = 255; data[11] = 255;
            // Pixel 3: white
            data[12] = 255; data[13] = 255; data[14] = 255; data[15] = 255;

            img.SetPixelData(data);
            byte[] got = img.GetPixelData();
            Assert.Equal(data, got);
        }

        [Fact]
        public void Decode4bpp_Roundtrip()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32]; // 16 colors
            // Set color 1 to red
            ushort red = svc.RGBAToGBAColor(248, 0, 0);
            palette[2] = (byte)(red & 0xFF);
            palette[3] = (byte)(red >> 8);

            // Create 8x8 tile with pixel index 1 everywhere
            byte[] tileData = new byte[32]; // 8x8 / 2 = 32 bytes
            for (int i = 0; i < 32; i++)
                tileData[i] = 0x11; // both nibbles = 1

            using var img = svc.Decode4bppTiles(tileData, 0, 8, 8, palette);
            byte[] encoded = svc.Encode4bppTiles(img);
            Assert.Equal(tileData, encoded);
        }

        [Fact]
        public void Decode4bpp_MultiTile_Roundtrip()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32]; // 16 colors

            // Create 16x16 (2x2 tiles) with varying data
            byte[] tileData = new byte[128]; // 4 tiles * 32 bytes
            for (int i = 0; i < 128; i++)
                tileData[i] = (byte)(i % 256);

            using var img = svc.Decode4bppTiles(tileData, 0, 16, 16, palette);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);
            byte[] encoded = svc.Encode4bppTiles(img);
            Assert.Equal(tileData, encoded);
        }

        [Fact]
        public void Decode4bpp_WithOffset()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32];

            // Prepend 8 garbage bytes, then 32 bytes of real tile data
            byte[] tileData = new byte[40];
            for (int i = 0; i < 8; i++)
                tileData[i] = 0xFF; // garbage
            for (int i = 8; i < 40; i++)
                tileData[i] = 0x23; // lo=3, hi=2 pattern

            using var img = svc.Decode4bppTiles(tileData, 8, 8, 8, palette);
            byte[] pixels = img.GetPixelData();
            // Every even pixel should be 3, every odd pixel should be 2
            for (int i = 0; i < 64; i += 2)
            {
                Assert.Equal(3, pixels[i]);
                Assert.Equal(2, pixels[i + 1]);
            }
        }

        [Fact]
        public void Decode8bpp_Roundtrip()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[512]; // 256 colors

            // Create 8x8 tile with increasing indices
            byte[] tileData = new byte[64];
            for (int i = 0; i < 64; i++)
                tileData[i] = (byte)(i % 256);

            using var img = svc.Decode8bppTiles(tileData, 0, 8, 8, palette);
            byte[] encoded = svc.Encode8bppTiles(img);
            Assert.Equal(tileData, encoded);
        }

        [Fact]
        public void Decode8bpp_MultiTile_Roundtrip()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[512];

            // 16x16 = 4 tiles, 64 bytes each
            byte[] tileData = new byte[256];
            for (int i = 0; i < 256; i++)
                tileData[i] = (byte)(i % 256);

            using var img = svc.Decode8bppTiles(tileData, 0, 16, 16, palette);
            byte[] encoded = svc.Encode8bppTiles(img);
            Assert.Equal(tileData, encoded);
        }

        [Fact]
        public void Decode8bppLinear_ReturnsCorrectPixelData()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[512];

            byte[] data = new byte[64]; // 8x8
            for (int i = 0; i < 64; i++)
                data[i] = (byte)(i % 256);

            using var img = svc.Decode8bppLinear(data, 0, 8, 8, palette);
            byte[] pixels = img.GetPixelData();
            Assert.Equal(data, pixels);
        }

        [Fact]
        public void Decode8bppLinear_WithOffset()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[512];

            byte[] data = new byte[74]; // 10 bytes offset + 64 bytes of image
            for (int i = 0; i < 10; i++)
                data[i] = 0xFF; // garbage
            for (int i = 10; i < 74; i++)
                data[i] = (byte)((i - 10) % 256);

            using var img = svc.Decode8bppLinear(data, 10, 8, 8, palette);
            byte[] pixels = img.GetPixelData();
            for (int i = 0; i < 64; i++)
                Assert.Equal((byte)(i % 256), pixels[i]);
        }

        [Fact]
        public void PaletteConversion_Roundtrip()
        {
            var svc = new SkiaImageService();
            byte[] gbaPal = new byte[8]; // 4 colors
            gbaPal[0] = 0; gbaPal[1] = 0;       // black
            gbaPal[2] = 0x1F; gbaPal[3] = 0;    // red (31)
            gbaPal[4] = 0xE0; gbaPal[5] = 0x03; // green (31 << 5)
            gbaPal[6] = 0x00; gbaPal[7] = 0x7C; // blue (31 << 10)

            byte[] rgba = svc.GBAPaletteToRGBA(gbaPal, 4);
            byte[] gbaBack = svc.RGBAPaletteToGBA(rgba, 4);
            Assert.Equal(gbaPal, gbaBack);
        }

        [Fact]
        public void GBAPaletteToRGBA_CorrectValues()
        {
            var svc = new SkiaImageService();
            byte[] gbaPal = new byte[4]; // 2 colors
            // Color 0: black (0x0000)
            gbaPal[0] = 0; gbaPal[1] = 0;
            // Color 1: white (0x7FFF)
            gbaPal[2] = 0xFF; gbaPal[3] = 0x7F;

            byte[] rgba = svc.GBAPaletteToRGBA(gbaPal, 2);
            // Color 0: black
            Assert.Equal(0, rgba[0]);   // R
            Assert.Equal(0, rgba[1]);   // G
            Assert.Equal(0, rgba[2]);   // B
            Assert.Equal(255, rgba[3]); // A
            // Color 1: white (248, 248, 248 due to 5-bit precision)
            Assert.Equal(248, rgba[4]); // R
            Assert.Equal(248, rgba[5]); // G
            Assert.Equal(248, rgba[6]); // B
            Assert.Equal(255, rgba[7]); // A
        }

        [Fact]
        public void EncodePng_ProducesValidData()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(8, 8);
            byte[] png = img.EncodePng();
            Assert.NotNull(png);
            Assert.True(png.Length > 0);
            // PNG magic bytes
            Assert.Equal(0x89, png[0]);
            Assert.Equal(0x50, png[1]); // P
            Assert.Equal(0x4E, png[2]); // N
            Assert.Equal(0x47, png[3]); // G
        }

        [Fact]
        public void LoadImageFromBytes_Roundtrip()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(16, 8);

            // Set some pixels
            byte[] data = new byte[16 * 8 * 4];
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i] = 128;     // R
                data[i + 1] = 64;  // G
                data[i + 2] = 32;  // B
                data[i + 3] = 255; // A
            }
            img.SetPixelData(data);

            byte[] png = img.EncodePng();
            using var loaded = svc.LoadImageFromBytes(png);
            Assert.Equal(16, loaded.Width);
            Assert.Equal(8, loaded.Height);
            Assert.False(loaded.IsIndexed);
        }

        [Fact]
        public void SetPaletteGBA_UpdatesRenderedImage()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[32]; // 16 colors, all black initially
            using var img = svc.CreateIndexedImage(8, 8, palette, 16);

            // Set all pixels to index 1
            byte[] indices = new byte[64];
            for (int i = 0; i < 64; i++)
                indices[i] = 1;
            img.SetPixelData(indices);

            // Now update palette: set color 1 to green
            ushort green = svc.RGBAToGBAColor(0, 248, 0);
            palette[2] = (byte)(green & 0xFF);
            palette[3] = (byte)(green >> 8);
            img.SetPaletteGBA(palette);

            // Verify the palette was updated
            byte[] gotPalette = img.GetPaletteGBA();
            Assert.Equal(palette, gotPalette);
        }

        [Fact]
        public void GetPaletteRGBA_FirstColorTransparent()
        {
            var svc = new SkiaImageService();
            byte[] palette = new byte[4]; // 2 colors
            palette[0] = 0; palette[1] = 0; // color 0
            palette[2] = 0x1F; palette[3] = 0; // color 1 = red

            using var img = svc.CreateIndexedImage(8, 8, palette, 2);
            byte[] rgba = img.GetPaletteRGBA();
            // First color alpha should be 0 (transparent)
            Assert.Equal(0, rgba[3]);
            // Second color alpha should be 255
            Assert.Equal(255, rgba[7]);
        }

        [Fact]
        public void NonIndexedImage_GetPaletteGBA_ReturnsEmpty()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(8, 8);
            byte[] palette = img.GetPaletteGBA();
            Assert.Empty(palette);
        }

        [Fact]
        public void NonIndexedImage_GetPaletteRGBA_ReturnsEmpty()
        {
            var svc = new SkiaImageService();
            using var img = svc.CreateImage(8, 8);
            byte[] palette = img.GetPaletteRGBA();
            Assert.Empty(palette);
        }

        [Fact]
        public void Dispose_DoesNotThrowOnDoubleDispose()
        {
            var svc = new SkiaImageService();
            var img = svc.CreateImage(8, 8);
            img.Dispose();
            // Should not throw
            img.Dispose();
        }
    }
}
