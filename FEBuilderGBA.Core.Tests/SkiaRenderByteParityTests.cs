// SPDX-License-Identifier: GPL-3.0-or-later
//
// SkiaSharp render byte-parity smoke test (#1125).
//
// These assertions are authored CROSS-PLATFORM (they live in
// FEBuilderGBA.Core.Tests, the net9.0 suite that runs on Linux/macOS/Windows
// and conceptually on Android) so that #1126's Android emulator / instrumented
// CI can run the SAME assertions on EACH of the 4 Android ABIs — arm64-v8a,
// armeabi-v7a, x86, x86_64 — to prove the pinned 2.88.x native libSkiaSharp
// renders byte-identically there too.
//
// Two layers, deliberately different strictness:
//   IMAGE parity is EXACT (zero tolerance): the GBA 4bpp tile -> palette-index
//   decode and the index -> RGBA palette expansion are pure integer math with
//   NO antialiasing, so every byte must match a golden computed by hand from
//   the palette + the deterministic tile pattern.
//   FONT parity is within a DOCUMENTED pixel tolerance: glyph rasterization
//   goes through Skia's antialiased text path, where arm64 vs x64 sub-pixel
//   AA-threshold rounding can flip a handful of edge pixels (see
//   SkiaFontGoldens for the full rationale + observed-drift margins).
//
// The image golden bytes are kept INLINE and explicit so any decode/encode
// divergence is loud; the font goldens are SHARED with the desktop regression
// lock (FEBuilderGBA.Avalonia.Tests/SkiaFontRasterizerTests.cs) via the
// <Compile … Link>-included SkiaFontGoldens, so the two suites can never drift.
using System;
using System.IO;
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.SkiaSharp;
using FEBuilderGBA.SharedTest;

namespace FEBuilderGBA.Core.Tests
{
    public class SkiaRenderByteParityTests
    {
        // ==================================================================
        // IMAGE parity (EXACT, zero tolerance)
        // ==================================================================

        // 16-color GBA palette, 32 bytes little-endian GBA15 (ushort per color).
        // Index 0 is transparent (RenderIndexedToRGBA maps idx 0 -> RGBA
        // (0,0,0,0)); the remaining 15 are distinct spread colors so a wrong
        // palette lookup would change visible bytes. The ushorts (before LE
        // split) are:
        //   0:0x0000 1:0x7FFF 2:0x001F 3:0x03E0 4:0x7C00 5:0x03FF 6:0x7C1F
        //   7:0x7FE0 8:0x0010 9:0x0200 10:0x4000 11:0x0210 12:0x4010 13:0x4200
        //   14:0x294A 15:0x56B5
        static readonly byte[] Palette =
        {
            0x00,0x00,0xFF,0x7F,0x1F,0x00,0xE0,0x03,0x00,0x7C,0xFF,0x03,0x1F,0x7C,0xE0,0x7F,
            0x10,0x00,0x00,0x02,0x00,0x40,0x10,0x02,0x10,0x40,0x00,0x42,0x4A,0x29,0xB5,0x56,
        };

        // One 8x8 4bpp tile, 32 bytes. The chosen pixel pattern is
        // index(x,y) = (x + y*8) % 16, packed exactly as Decode4bppTiles reads
        // it: per byte, low nibble = left (even-x) pixel, high nibble = right
        // (odd-x) pixel. Row 0 = indices 0..7, so bytes (lo|hi<<4) are
        // 0x10,0x32,0x54,0x76,0x98,0xBA,0xDC,0xFE; every subsequent row repeats
        // the same 0..7 then 8..15 progression because (x+y*8)%16 cycles with a
        // period of 16 across the 64-pixel raster.
        static readonly byte[] Tile =
        {
            0x10,0x32,0x54,0x76,0x98,0xBA,0xDC,0xFE,0x10,0x32,0x54,0x76,0x98,0xBA,0xDC,0xFE,
            0x10,0x32,0x54,0x76,0x98,0xBA,0xDC,0xFE,0x10,0x32,0x54,0x76,0x98,0xBA,0xDC,0xFE,
        };

        // EXACT decode golden: GetPixelData() on the indexed image returns the
        // 64 palette INDICES. Because Decode4bppTiles is a faithful unpack of the
        // pattern above, this is simply 0..15 repeated four times.
        static readonly byte[] GoldenIndices =
        {
            0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,
            0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,
            0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,
            0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,
        };

        // EXACT RGBA golden: PNG round-trip (EncodePng -> LoadImageFromBytes ->
        // GetPixelData) returns 4 bytes/pixel = 256 bytes. Computed by expanding
        // each index through GBAColorToRGBA (r=(c&0x1F)<<3, g=((c>>5)&0x1F)<<3,
        // b=((c>>10)&0x1F)<<3, alpha 255) — EXCEPT index 0 -> (0,0,0,0)
        // transparent, matching RenderIndexedToRGBA. Worked example for row 0:
        //   idx0 0x0000 -> 00,00,00,00 (transparent)
        //   idx1 0x7FFF -> F8,F8,F8,FF (white: all channels 0x1F<<3=0xF8)
        //   idx2 0x001F -> F8,00,00,FF (red:   r=0x1F<<3)
        //   idx3 0x03E0 -> 00,F8,00,FF (green: g=0x1F<<3)
        //   idx4 0x7C00 -> 00,00,F8,FF (blue:  b=0x1F<<3)
        //   idx5 0x03FF -> F8,F8,00,FF (r+g)
        //   idx6 0x7C1F -> F8,00,F8,FF (r+b)
        //   idx7 0x7FE0 -> 00,F8,F8,FF (g+b)
        //   idx14 0x294A -> 50,50,50,FF (gray: each 5-bit field == 0x0A<<3=0x50)
        //   idx15 0x56B5 -> A8,A8,A8,FF (gray: each 5-bit field == 0x15<<3=0xA8)
        static readonly byte[] GoldenRgba =
        {
            0x00,0x00,0x00,0x00,0xF8,0xF8,0xF8,0xFF,0xF8,0x00,0x00,0xFF,0x00,0xF8,0x00,0xFF,
            0x00,0x00,0xF8,0xFF,0xF8,0xF8,0x00,0xFF,0xF8,0x00,0xF8,0xFF,0x00,0xF8,0xF8,0xFF,
            0x80,0x00,0x00,0xFF,0x00,0x80,0x00,0xFF,0x00,0x00,0x80,0xFF,0x80,0x80,0x00,0xFF,
            0x80,0x00,0x80,0xFF,0x00,0x80,0x80,0xFF,0x50,0x50,0x50,0xFF,0xA8,0xA8,0xA8,0xFF,
            0x00,0x00,0x00,0x00,0xF8,0xF8,0xF8,0xFF,0xF8,0x00,0x00,0xFF,0x00,0xF8,0x00,0xFF,
            0x00,0x00,0xF8,0xFF,0xF8,0xF8,0x00,0xFF,0xF8,0x00,0xF8,0xFF,0x00,0xF8,0xF8,0xFF,
            0x80,0x00,0x00,0xFF,0x00,0x80,0x00,0xFF,0x00,0x00,0x80,0xFF,0x80,0x80,0x00,0xFF,
            0x80,0x00,0x80,0xFF,0x00,0x80,0x80,0xFF,0x50,0x50,0x50,0xFF,0xA8,0xA8,0xA8,0xFF,
            0x00,0x00,0x00,0x00,0xF8,0xF8,0xF8,0xFF,0xF8,0x00,0x00,0xFF,0x00,0xF8,0x00,0xFF,
            0x00,0x00,0xF8,0xFF,0xF8,0xF8,0x00,0xFF,0xF8,0x00,0xF8,0xFF,0x00,0xF8,0xF8,0xFF,
            0x80,0x00,0x00,0xFF,0x00,0x80,0x00,0xFF,0x00,0x00,0x80,0xFF,0x80,0x80,0x00,0xFF,
            0x80,0x00,0x80,0xFF,0x00,0x80,0x80,0xFF,0x50,0x50,0x50,0xFF,0xA8,0xA8,0xA8,0xFF,
            0x00,0x00,0x00,0x00,0xF8,0xF8,0xF8,0xFF,0xF8,0x00,0x00,0xFF,0x00,0xF8,0x00,0xFF,
            0x00,0x00,0xF8,0xFF,0xF8,0xF8,0x00,0xFF,0xF8,0x00,0xF8,0xFF,0x00,0xF8,0xF8,0xFF,
            0x80,0x00,0x00,0xFF,0x00,0x80,0x00,0xFF,0x00,0x00,0x80,0xFF,0x80,0x80,0x00,0xFF,
            0x80,0x00,0x80,0xFF,0x00,0x80,0x80,0xFF,0x50,0x50,0x50,0xFF,0xA8,0xA8,0xA8,0xFF,
        };

        [Fact]
        public void Decode4bppTiles_IndexBytes_Are_ExactlyTheGolden()
        {
            var svc = new SkiaImageService();
            using IImage img = svc.Decode4bppTiles(Tile, 0, 8, 8, Palette);

            Assert.True(img.IsIndexed, "decoded image should be indexed");
            byte[] indices = img.GetPixelData();

            Assert.Equal(GoldenIndices.Length, indices.Length);
            Assert.Equal(GoldenIndices, indices); // EXACT byte parity
        }

        [Fact]
        public void PngRoundTrip_RgbaBytes_Are_ExactlyTheGolden()
        {
            var svc = new SkiaImageService();
            using IImage img = svc.Decode4bppTiles(Tile, 0, 8, 8, Palette);

            byte[] png = img.EncodePng();
            Assert.True(png.Length > 8, "EncodePng produced no data");

            using IImage decoded = svc.LoadImageFromBytes(png);
            byte[] rgba = decoded.GetPixelData(); // RGBA for a non-indexed image

            Assert.Equal(GoldenRgba.Length, rgba.Length);
            Assert.Equal(GoldenRgba, rgba); // EXACT byte parity
        }

        // ==================================================================
        // FONT parity (documented tolerance, shared goldens)
        // ==================================================================

        static byte[] LoadTuffy()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fonts", "Tuffy-Regular.ttf");
            Assert.True(File.Exists(path), $"bundled font missing at {path}");
            return File.ReadAllBytes(path);
        }

        static FontSpec TuffySpec(float size = 12f) => new FontSpec
        {
            FamilyName = "Tuffy",
            Size = size,
            FontFileData = LoadTuffy(),
        };

        [Fact]
        public void FontParity_TextGlyph_A_WithinSharedTolerance()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: false, 0, out int width);
            int diff = SkiaFontGoldens.CountDifferingPixels(SkiaFontGoldens.GoldenTextA, tile);
            Assert.True(diff <= SkiaFontGoldens.GoldenTextPixelTolerance,
                $"text glyph 'A' differs from baseline golden by {diff} pixels (tolerance {SkiaFontGoldens.GoldenTextPixelTolerance})");
            Assert.Equal(SkiaFontGoldens.GoldenTextAWidth, width);
        }

        [Fact]
        public void FontParity_ItemGlyph_A_WithinSharedTolerance()
        {
            var r = new SkiaFontRasterizer();
            byte[] tile = r.RasterizeGlyph(TuffySpec(), "A", isItemFont: true, 0, out int width);
            int diff = SkiaFontGoldens.CountDifferingPixels(SkiaFontGoldens.GoldenItemA, tile);
            Assert.True(diff <= SkiaFontGoldens.GoldenItemPixelTolerance,
                $"item glyph 'A' differs from baseline golden by {diff} pixels (tolerance {SkiaFontGoldens.GoldenItemPixelTolerance})");
            Assert.Equal(SkiaFontGoldens.GoldenItemAWidth, width);
        }
    }
}
