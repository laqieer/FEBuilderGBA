using System;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the cross-platform multi-palette TSA color reducer
    /// (DecreaseColorConvertCore — port of WinForms DecreaseColor.cs). #998 PR1.
    /// All reduce tests run in-memory on synthetic RGBA arrays (no real ROM,
    /// no SkiaSharp); ReduceColorFile is exercised for guard/error paths plus a
    /// success path via a tiny RGBA-backed fake IImageService.
    /// </summary>
    public class DecreaseColorConvertCoreTests
    {
        // ---- helpers -----------------------------------------------------------

        /// <summary>Build a solid-color WxH RGBA buffer.</summary>
        private static byte[] Solid(int w, int h, byte r, byte g, byte b, byte a)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = a;
            }
            return rgba;
        }

        private static void SetPixel(byte[] rgba, int w, int x, int y, byte r, byte g, byte b, byte a)
        {
            int o = (x + y * w) * 4;
            rgba[o + 0] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = a;
        }

        private static int GbaColorAt(byte[] pal, int colorIndex)
        {
            return pal[colorIndex * 2] | (pal[colorIndex * 2 + 1] << 8);
        }

        // ---- golden bytes: ignoreTSA / maxPalette==1 path ----------------------

        [Fact]
        public void Convert_SingleRedTile_FlatPath_GoldenBytes()
        {
            // 8x8 all opaque red. maxPalette=1 → ConvertIgnoreTSA(maxColor=16, isUseTransparent=true).
            byte[] rgba = Solid(8, 8, 255, 0, 0, 255);

            var result = DecreaseColorConvertCore.Convert(rgba, 8, 8, maxPalette: 1, yohaku: 0, reserve1st: true, ignoreTSA: false);

            Assert.Equal(8, result.Width);
            Assert.Equal(8, result.Height);
            Assert.Equal(1, result.PaletteBankCount);

            // Every pixel maps to palette index 1 (red), index 0 is the reserved empty slot.
            Assert.Equal(64, result.IndexData.Length);
            foreach (byte b in result.IndexData)
            {
                Assert.Equal(1, b);
            }

            // GBA palette: color 0 = (0,0,0)=0x0000; color 1 = (248,0,0)=0x001F (r5=31).
            Assert.Equal(0x0000, GbaColorAt(result.GbaPalette, 0));
            Assert.Equal(0x001F, GbaColorAt(result.GbaPalette, 1));
            // Zero-padded to 256 colors (512 bytes).
            Assert.Equal(256 * 2, result.GbaPalette.Length);
            // Remaining colors zero.
            for (int c = 2; c < 256; c++)
            {
                Assert.Equal(0x0000, GbaColorAt(result.GbaPalette, c));
            }
        }

        [Fact]
        public void Convert_FlatPath_NoReserve_Index0IsRealColor()
        {
            // maxPalette=16 + ignoreTSA → flat path, maxColor=256, isUseTransparent=(16<16)=false.
            // reserve1st=false → startColor=0, index 0 becomes a real color.
            byte[] rgba = Solid(8, 8, 0, 255, 0, 255); // opaque green

            var result = DecreaseColorConvertCore.Convert(rgba, 8, 8, maxPalette: 16, yohaku: 0, reserve1st: false, ignoreTSA: true);

            // Single color → packed at palette slot 0, every pixel = index 0.
            foreach (byte b in result.IndexData)
            {
                Assert.Equal(0, b);
            }
            // color 0 = (0,248,0) → g5=31 → 0x03E0.
            Assert.Equal(0x03E0, GbaColorAt(result.GbaPalette, 0));
        }

        // ---- one-bank-per-tile invariant (multi-palette) -----------------------

        [Fact]
        public void Convert_MultiPalette_EachTileUsesSingleBank()
        {
            // 16x8 = two 8x8 tiles, each a distinct solid color. maxPalette=2.
            int w = 16, h = 8;
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x < 8) SetPixel(rgba, w, x, y, 200, 40, 40, 255);   // tile 0: reddish
                    else SetPixel(rgba, w, x, y, 40, 40, 200, 255);          // tile 1: bluish
                }
            }

            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 2, yohaku: 0, reserve1st: true, ignoreTSA: false);

            Assert.Equal(16, result.Width);
            Assert.Equal(8, result.Height);
            Assert.Equal(2, result.PaletteBankCount);

            // For each 8x8 tile, all non-zero indices share one 16-aligned bank.
            for (int ty = 0; ty < result.Height; ty += 8)
            {
                for (int tx = 0; tx < result.Width; tx += 8)
                {
                    int bank = -1;
                    for (int yy = 0; yy < 8; yy++)
                    {
                        for (int xx = 0; xx < 8; xx++)
                        {
                            int px = tx + xx, py = ty + yy;
                            if (px >= result.Width || py >= result.Height) continue;
                            int idx = result.IndexData[px + py * result.Width];
                            if (idx == 0) continue; // reserved/background slot of any bank
                            int b = idx / 16;
                            if (bank == -1) bank = b;
                            Assert.Equal(bank, b);
                        }
                    }
                }
            }
        }

        // ---- padding & yohaku --------------------------------------------------

        [Fact]
        public void Convert_PaddingAndYohaku_DimensionsAndMarginAreIndex0()
        {
            // 12x10 source (not multiples of 8). maxPalette=1, yohaku=16.
            // Expected output W = Padding8(12)+16 = 16+16 = 32; H = Padding8(10) = 16.
            int srcW = 12, srcH = 10, yohaku = 16;
            byte[] rgba = Solid(srcW, srcH, 100, 150, 200, 255);

            var result = DecreaseColorConvertCore.Convert(rgba, srcW, srcH, maxPalette: 1, yohaku: yohaku, reserve1st: true, ignoreTSA: false);

            Assert.Equal(U.Padding8(srcW) + yohaku, result.Width); // 32
            Assert.Equal(U.Padding8(srcH), result.Height);          // 16
            Assert.Equal(32, result.Width);
            Assert.Equal(16, result.Height);

            // The yohaku right margin (cols [16,32)) must be index 0.
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = U.Padding8(srcW); x < result.Width; x++)
                {
                    Assert.Equal(0, result.IndexData[x + y * result.Width]);
                }
            }
            // Padded rows below the source (y in [10,16)) within the first tile column
            // are part of tile (0,8) which IS processed; those padded pixels read as
            // transparent → index 0.
            for (int y = srcH; y < result.Height; y++)
            {
                for (int x = 0; x < U.Padding8(srcW); x++)
                {
                    Assert.Equal(0, result.IndexData[x + y * result.Width]);
                }
            }
        }

        // ---- color budget ------------------------------------------------------

        [Fact]
        public void Convert_ColorBudget_DistinctColorsWithinLimit()
        {
            // A gradient 24x16 image, maxPalette=2 → budget 32 colors.
            int w = 24, h = 16;
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    SetPixel(rgba, w, x, y, (byte)(x * 10), (byte)(y * 15), (byte)((x + y) * 5), 255);

            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 2, yohaku: 0, reserve1st: true, ignoreTSA: false);

            // Distinct non-zero indices used must each be < maxPalette*16 = 32.
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (byte idx in result.IndexData)
                if (idx != 0) used.Add(idx);
            foreach (int idx in used)
                Assert.True(idx < 2 * 16, $"index {idx} exceeds maxPalette*16");
        }

        [Fact]
        public void Convert_MaxPalette1_BudgetAtMost16()
        {
            int w = 16, h = 16;
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    SetPixel(rgba, w, x, y, (byte)(x * 16), (byte)(y * 16), (byte)(x * y), 255);

            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 1, yohaku: 0, reserve1st: true, ignoreTSA: false);

            var used = new System.Collections.Generic.HashSet<int>();
            foreach (byte idx in result.IndexData) used.Add(idx);
            foreach (int idx in used)
                Assert.True(idx < 16, $"index {idx} exceeds 16 for maxPalette==1");
        }

        // ---- alpha -------------------------------------------------------------

        [Fact]
        public void Convert_AlphaZeroPixels_MapToIndex0()
        {
            // 8x8 tile: top-left half transparent, rest opaque.
            int w = 8, h = 8;
            byte[] rgba = Solid(w, h, 120, 80, 200, 255);
            // Make the first row fully transparent.
            for (int x = 0; x < w; x++) SetPixel(rgba, w, x, 0, 0, 0, 0, 0);

            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 2, yohaku: 0, reserve1st: true, ignoreTSA: false);

            // First row pixels are transparent → index 0 (bank-local reserved slot).
            for (int x = 0; x < w; x++)
                Assert.Equal(0, result.IndexData[x + 0 * result.Width] % 16);
        }

        // ---- reserve-first-color ----------------------------------------------

        [Fact]
        public void Convert_Reserve1stTrue_Slot0IsEmptyBackground()
        {
            // maxPalette=1 flat, reserve1st=true → first=1, center[0] empty.
            byte[] rgba = Solid(8, 8, 90, 160, 240, 255);
            var result = DecreaseColorConvertCore.Convert(rgba, 8, 8, maxPalette: 1, yohaku: 0, reserve1st: true, ignoreTSA: false);

            // Color slot 0 reserved = (0,0,0); the single image color sits at slot 1.
            Assert.Equal(0x0000, GbaColorAt(result.GbaPalette, 0));
            Assert.NotEqual(0x0000, GbaColorAt(result.GbaPalette, 1));
            // All opaque pixels at index 1.
            foreach (byte b in result.IndexData) Assert.Equal(1, b);
        }

        [Fact]
        public void Convert_Reserve1stFalse_Slot0Used()
        {
            // maxPalette=16 + ignoreTSA, reserve1st=false → first=0, color lands in slot 0.
            byte[] rgba = Solid(8, 8, 90, 160, 240, 255);
            var result = DecreaseColorConvertCore.Convert(rgba, 8, 8, maxPalette: 16, yohaku: 0, reserve1st: false, ignoreTSA: true);

            Assert.NotEqual(0x0000, GbaColorAt(result.GbaPalette, 0)); // real color in slot 0
            foreach (byte b in result.IndexData) Assert.Equal(0, b);
        }

        // ---- ignoreTSA / maxPalette<16 transparent path ------------------------

        [Fact]
        public void Convert_IgnoreTSA_ProducesSingleFlatBank()
        {
            int w = 16, h = 16;
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    SetPixel(rgba, w, x, y, (byte)(x * 16), (byte)(y * 16), 64, 255);

            // maxPalette=4 + ignoreTSA → maxColor=64, isUseTransparent=(4<16)=true, single flat bank.
            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 4, yohaku: 0, reserve1st: true, ignoreTSA: true);
            Assert.Equal(1, result.PaletteBankCount);

            // ignoreTSA flat palette: no per-tile banking constraint, indices range 0..63.
            foreach (byte idx in result.IndexData)
                Assert.True(idx < 64);
        }

        [Fact]
        public void Convert_MaxPaletteLessThan16_UsesTransparentPath()
        {
            // maxPalette=8 + ignoreTSA → isUseTransparent=true (8<16). A transparent pixel maps to index 0.
            int w = 8, h = 8;
            byte[] rgba = Solid(w, h, 200, 100, 50, 255);
            SetPixel(rgba, w, 0, 0, 0, 0, 0, 0); // one transparent pixel

            var result = DecreaseColorConvertCore.Convert(rgba, w, h, maxPalette: 8, yohaku: 0, reserve1st: true, ignoreTSA: true);
            Assert.Equal(0, result.IndexData[0]); // transparent → index 0
        }

        // ---- preset table ------------------------------------------------------

        [Fact]
        public void GetMethodPreset_Method1_BgCg()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(1, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(2 * 8, p.Yohaku);
            Assert.Equal(8, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.False(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method2_BattleBg()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(2, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(0, p.Yohaku);
            Assert.Equal(8, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.False(p.IgnoreTSA);
        }

        [Theory]
        [InlineData(8, 480, 320, 0, 4, true, true, false)]
        [InlineData(7, 1024, 688, 0, 4, true, true, false)]
        [InlineData(6, 240, 160, 0, 16, false, true, false)]
        public void GetMethodPreset_Method3_WorldMapLarge(int ver, int w, int h, int yo, int pal, bool reserve, bool scal, bool ignore)
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(3, ver);
            Assert.Equal(w, p.Width);
            Assert.Equal(h, p.Height);
            Assert.Equal(yo, p.Yohaku);
            Assert.Equal(pal, p.PaletteNo);
            Assert.Equal(reserve, p.Reserve1st);
            Assert.Equal(scal, p.Scalable);
            Assert.Equal(ignore, p.IgnoreTSA);
        }

        [Theory]
        [InlineData(8, 30 * 8, 20 * 8, 2 * 8, 4, true, true, false)]
        [InlineData(7, 30 * 8, 20 * 8, 2 * 8, 4, true, true, false)]
        [InlineData(6, 240, 160, 0, 16, false, true, false)]
        public void GetMethodPreset_Method4_WorldMapEvent(int ver, int w, int h, int yo, int pal, bool reserve, bool scal, bool ignore)
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(4, ver);
            Assert.Equal(w, p.Width);
            Assert.Equal(h, p.Height);
            Assert.Equal(yo, p.Yohaku);
            Assert.Equal(pal, p.PaletteNo);
            Assert.Equal(reserve, p.Reserve1st);
            Assert.Equal(scal, p.Scalable);
            Assert.Equal(ignore, p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method5_IgnoreTSA256()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(5, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(0, p.Yohaku);
            Assert.Equal(16, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.True(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method6_StatusBg()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(6, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(0, p.Yohaku);
            Assert.Equal(4, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.False(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method7_MapChips()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(7, 8);
            Assert.Equal(512, p.Width);
            Assert.Equal(512, p.Height);
            Assert.Equal(0, p.Yohaku);
            Assert.Equal(5, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.False(p.Scalable); // ConvertSizeMethod=0
            Assert.False(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method8_MapChips10Color()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(8, 8);
            Assert.Equal(512, p.Width);
            Assert.Equal(512, p.Height);
            Assert.Equal(0, p.Yohaku);
            Assert.Equal(10, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.False(p.Scalable);
            Assert.False(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method9_CutsceneBg256()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(9, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(2 * 8, p.Yohaku);
            Assert.Equal(16, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.True(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_MethodA_TalkBg224()
        {
            var p = DecreaseColorConvertCore.GetMethodPreset(0xA, 8);
            Assert.Equal(30 * 8, p.Width);
            Assert.Equal(20 * 8, p.Height);
            Assert.Equal(2 * 8, p.Yohaku);
            Assert.Equal(14, p.PaletteNo);
            Assert.True(p.Reserve1st);
            Assert.True(p.Scalable);
            Assert.True(p.IgnoreTSA);
        }

        [Fact]
        public void GetMethodPreset_Method0AndDefault_MatchWFInitialState()
        {
            // WF ctor sets Method=1 (BG/CG) and ConvertSizeMethod=1 (scalable).
            var p0 = DecreaseColorConvertCore.GetMethodPreset(0, 8);
            Assert.Equal(30 * 8, p0.Width);
            Assert.Equal(20 * 8, p0.Height);
            Assert.Equal(2 * 8, p0.Yohaku);
            Assert.Equal(8, p0.PaletteNo);
            Assert.True(p0.Reserve1st);
            Assert.True(p0.Scalable);
            Assert.False(p0.IgnoreTSA);

            // Unhandled index falls into the same default.
            var pX = DecreaseColorConvertCore.GetMethodPreset(99, 8);
            Assert.Equal(p0.Width, pX.Width);
            Assert.Equal(p0.PaletteNo, pX.PaletteNo);
            Assert.Equal(p0.IgnoreTSA, pX.IgnoreTSA);
        }

        // ---- scale / resize helpers -------------------------------------------

        [Fact]
        public void ResizeRgba_CropAndPad_TopLeftPreservedRestTransparent()
        {
            // 4x4 solid green → resize to 6x6: top-left 4x4 kept, rest transparent.
            byte[] src = Solid(4, 4, 0, 255, 0, 255);
            byte[] dst = DecreaseColorConvertCore.ResizeRgba(src, 4, 4, 6, 6);
            Assert.Equal(6 * 6 * 4, dst.Length);
            // (0,0) preserved
            Assert.Equal(0, dst[0]); Assert.Equal(255, dst[1]); Assert.Equal(0, dst[2]); Assert.Equal(255, dst[3]);
            // (5,5) new area → transparent
            int o = (5 + 5 * 6) * 4;
            Assert.Equal(0, dst[o + 3]);
        }

        [Fact]
        public void ScaleRgba_SolidColorRemainsSolid()
        {
            // Scaling a solid image must keep it solid in the covered area.
            byte[] src = Solid(4, 4, 10, 20, 30, 255);
            byte[] dst = DecreaseColorConvertCore.ScaleRgba(src, 4, 4, 8, 8);
            Assert.Equal(8 * 8 * 4, dst.Length);
            // Center pixel should be the solid color.
            int o = (4 + 4 * 8) * 4;
            Assert.Equal(10, dst[o + 0]);
            Assert.Equal(20, dst[o + 1]);
            Assert.Equal(30, dst[o + 2]);
            Assert.Equal(255, dst[o + 3]);
        }

        // ---- Convert argument guards ------------------------------------------

        [Fact]
        public void Convert_NullRgba_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                DecreaseColorConvertCore.Convert(null, 8, 8, 1, 0, true, false));
        }

        [Fact]
        public void Convert_TooSmallBuffer_Throws()
        {
            byte[] tooSmall = new byte[10];
            Assert.Throws<ArgumentException>(() =>
                DecreaseColorConvertCore.Convert(tooSmall, 8, 8, 1, 0, true, false));
        }

        // ---- ReduceColorFile error paths (no ImageService mutation) -----------

        [Fact]
        public void ReduceColorFile_MissingInput_ReturnsMinus2()
        {
            // The missing-input guard fires before ImageService is even consulted.
            string missing = Path.Combine(Path.GetTempPath(), "no_such_file_" + Guid.NewGuid() + ".png");
            int rc = DecreaseColorConvertCore.ReduceColorFile(missing, "out.png", 0, 0, 0, 1, true, true, false);
            Assert.Equal(-2, rc);
        }

        [Fact]
        public void ReduceColorFile_EmptyOutPath_ReturnsMinus2()
        {
            // Use a real temp file as input so the missing-input guard does not fire first.
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG magic stub
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, "", 0, 0, 0, 1, true, true, false);
                Assert.Equal(-2, rc);
            }
            finally
            {
                File.Delete(input);
            }
        }
    }

    /// <summary>
    /// ReduceColorFile tests that mutate the shared static CoreState.ImageService.
    /// Serialized via [Collection("SharedState")] to avoid races with other
    /// ImageService-dependent tests.
    /// </summary>
    [Collection("SharedState")]
    public class DecreaseColorConvertCoreFileTests
    {
        private static byte[] Solid(int w, int h, byte r, byte g, byte b, byte a)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = a;
            }
            return rgba;
        }

        // ---- ReduceColorFile success path via a fake IImageService ------------

        [Fact]
        public void ReduceColorFile_SuccessPath_WritesRgbaOutput()
        {
            // Source: an 8x8 solid red "image" served by the fake service.
            byte[] srcRgba = Solid(8, 8, 255, 0, 0, 255);
            var fake = new FakeReduceImageService(srcRgba, 8, 8);
            var prev = CoreState.ImageService;
            CoreState.ImageService = fake;
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            string output = Path.Combine(Path.GetTempPath(), "dc_out_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, output, 0, 0, 0, 1, true, true, false);
                Assert.Equal(0, rc);
                Assert.NotNull(fake.LastSaved);
                Assert.Equal(output, fake.LastSavedPath);
                Assert.Equal(8, fake.LastSaved.Width);
                Assert.Equal(8, fake.LastSaved.Height);

                // Output RGBA: opaque red everywhere (index 1 → (248,0,0), opaque).
                byte[] px = fake.LastSaved.GetPixelData();
                Assert.Equal(248, px[0]);
                Assert.Equal(0, px[1]);
                Assert.Equal(0, px[2]);
                Assert.Equal(255, px[3]);
            }
            finally
            {
                CoreState.ImageService = prev;
                File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Fact]
        public void ReduceColorFile_NoReserve_Index0RenderedOpaque()
        {
            // maxPalette=16 + ignoreTSA, reserve1st=false → index 0 is a real opaque color.
            byte[] srcRgba = Solid(8, 8, 0, 255, 0, 255); // green
            var fake = new FakeReduceImageService(srcRgba, 8, 8);
            var prev = CoreState.ImageService;
            CoreState.ImageService = fake;
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            string output = Path.Combine(Path.GetTempPath(), "dc_out_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, output, 0, 0, 0, 16, isScalable: true, reserve1st: false, ignoreTSA: true);
                Assert.Equal(0, rc);
                byte[] px = fake.LastSaved.GetPixelData();
                // Index 0 must be the real green color, fully opaque (NOT transparent).
                Assert.Equal(0, px[0]);
                Assert.Equal(248, px[1]);
                Assert.Equal(0, px[2]);
                Assert.Equal(255, px[3]);
            }
            finally
            {
                CoreState.ImageService = prev;
                File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Fact]
        public void ReduceColorFile_NoReserveMultiPalette_Index0RenderedOpaque()
        {
            // The FE6 no-reserve world-map presets (method 3/4): paletteNo=16, reserve1st=false,
            // ignoreTSA=false. Convert16Color(first=0) puts the single color in bank slot 0, so
            // index 0 is a REAL color and must save OPAQUE (regression for the index-0 alpha bug).
            byte[] srcRgba = Solid(8, 8, 0, 255, 0, 255); // opaque green
            var fake = new FakeReduceImageService(srcRgba, 8, 8);
            var prev = CoreState.ImageService;
            CoreState.ImageService = fake;
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            string output = Path.Combine(Path.GetTempPath(), "dc_out_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, output, 0, 0, 0, paletteNo: 16, isScalable: true, reserve1st: false, ignoreTSA: false);
                Assert.Equal(0, rc);
                byte[] px = fake.LastSaved.GetPixelData();
                // Pixel (0,0) is bank-slot-0 green; must be the real opaque color, NOT transparent.
                Assert.Equal(0, px[0]);
                Assert.Equal(248, px[1]);
                Assert.Equal(0, px[2]);
                Assert.Equal(255, px[3]);
            }
            finally
            {
                CoreState.ImageService = prev;
                File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Fact]
        public void ReduceColorFile_SaveThrows_ReturnsMinus1_DeletesPartialOutput()
        {
            // A service whose Save() throws an IOException after partially writing the file.
            byte[] srcRgba = Solid(8, 8, 255, 0, 0, 255);
            var fake = new FakeReduceImageService(srcRgba, 8, 8) { ThrowOnSave = true };
            var prev = CoreState.ImageService;
            CoreState.ImageService = fake;
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            string output = Path.Combine(Path.GetTempPath(), "dc_out_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, output, 0, 0, 0, 1, true, true, false);
                Assert.Equal(-1, rc);
                // The partially-written output must have been cleaned up.
                Assert.False(File.Exists(output), "partial output should be deleted on save failure");
            }
            finally
            {
                CoreState.ImageService = prev;
                File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Fact]
        public void ReduceColorFile_NoImageService_ReturnsMinus1()
        {
            var prev = CoreState.ImageService;
            CoreState.ImageService = null;
            string input = Path.Combine(Path.GetTempPath(), "dc_in_" + Guid.NewGuid() + ".png");
            File.WriteAllBytes(input, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                int rc = DecreaseColorConvertCore.ReduceColorFile(input, "out.png", 0, 0, 0, 1, true, true, false);
                Assert.Equal(-1, rc);
            }
            finally
            {
                CoreState.ImageService = prev;
                File.Delete(input);
            }
        }
    }

    // ---- minimal RGBA-backed fake image service for ReduceColorFile tests -----

    internal sealed class FakeReduceImage : IImage
    {
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed { get; }
        private byte[] _pixels;
        public FakeReduceImage(int w, int h, byte[] rgba, bool indexed)
        {
            Width = w; Height = h; IsIndexed = indexed;
            _pixels = rgba ?? new byte[w * h * 4];
        }
        public byte[] GetPixelData()
        {
            byte[] copy = new byte[_pixels.Length];
            Array.Copy(_pixels, copy, _pixels.Length);
            return copy;
        }
        public void SetPixelData(byte[] data) { _pixels = data; }
        public byte[] GetPaletteGBA() => Array.Empty<byte>();
        public void SetPaletteGBA(byte[] p) { }
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string filePath) { }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }

    internal sealed class FakeReduceImageService : IImageService
    {
        private readonly byte[] _srcRgba;
        private readonly int _w, _h;
        public FakeReduceImage LastSaved { get; private set; }
        public string LastSavedPath { get; private set; }
        /// <summary>When true, Save writes a partial file then throws an IOException.</summary>
        public bool ThrowOnSave { get; set; }

        public FakeReduceImageService(byte[] srcRgba, int w, int h)
        {
            _srcRgba = srcRgba; _w = w; _h = h;
        }

        public IImage CreateImage(int width, int height)
        {
            // Track the created output image and its save path.
            var img = new SavingFakeImage(width, height, this);
            return img;
        }
        public IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
            => new SavingFakeImage(width, height, this);
        public IImage LoadImage(string filePath) => new FakeReduceImage(_w, _h, _srcRgba, false);
        public IImage LoadImageFromBytes(byte[] pngData) => null;
        public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }
        public ushort RGBAToGBAColor(byte r, byte g, byte b)
            => (ushort)(((r >> 3) & 0x1F) | (((g >> 3) & 0x1F) << 5) | (((b >> 3) & 0x1F) << 10));
        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => new FakeReduceImage(w, h, null, true);
        public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new FakeReduceImage(w, h, null, true);
        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new FakeReduceImage(w, h, null, true);
        public byte[] Encode4bppTiles(IImage i) => Array.Empty<byte>();
        public byte[] Encode8bppTiles(IImage i) => Array.Empty<byte>();
        public byte[] GBAPaletteToRGBA(byte[] p, int c) => Array.Empty<byte>();
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => Array.Empty<byte>();

        internal void RecordSave(SavingFakeImage img, string path)
        {
            LastSaved = new FakeReduceImage(img.Width, img.Height, img.GetPixelData(), false);
            LastSavedPath = path;
            // Write a real file so the test can verify the (no-)partial-output contract.
            try { File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 }); } catch { }
            if (ThrowOnSave)
            {
                // Simulate a mid-save IO failure AFTER a partial write.
                throw new IOException("simulated save failure");
            }
        }
    }

    internal sealed class SavingFakeImage : IImage
    {
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => false;
        private byte[] _pixels;
        private readonly FakeReduceImageService _owner;
        public SavingFakeImage(int w, int h, FakeReduceImageService owner)
        {
            Width = w; Height = h; _owner = owner;
            _pixels = new byte[w * h * 4];
        }
        public byte[] GetPixelData()
        {
            byte[] copy = new byte[_pixels.Length];
            Array.Copy(_pixels, copy, _pixels.Length);
            return copy;
        }
        public void SetPixelData(byte[] data) { _pixels = data; }
        public byte[] GetPaletteGBA() => Array.Empty<byte>();
        public void SetPaletteGBA(byte[] p) { }
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string filePath) { _owner.RecordSave(this, filePath); }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }
}
