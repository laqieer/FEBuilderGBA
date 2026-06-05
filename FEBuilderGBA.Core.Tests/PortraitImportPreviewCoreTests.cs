// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Deterministic Core tests for the Portrait Import Wizard per-frame live
    /// preview renderer (#975). Uses a full in-memory <see cref="IImage"/>
    /// service (the <see cref="MinimalImageService"/> in ImageImportCoreTests
    /// returns null from CreateImage, so we need a real one here) and exact-pixel
    /// assertions on a synthetic 128x112 indexed sheet with distinct color bands
    /// per region.
    /// </summary>
    [Collection("SharedState")]
    public class PortraitImportPreviewCoreTests
    {
        // ----- In-memory IImage / IImageService -----
        sealed class MemImage : IImage
        {
            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed => false;
            byte[] _pixels;
            public MemImage(int w, int h) { Width = w; Height = h; _pixels = new byte[w * h * 4]; }
            public byte[] GetPixelData() => _pixels;
            public void SetPixelData(byte[] data) { _pixels = data; }
            public byte[] GetPaletteGBA() => Array.Empty<byte>();
            public void SetPaletteGBA(byte[] p) { }
            public byte[] GetPaletteRGBA() => Array.Empty<byte>();
            public void Save(string f) { }
            public byte[] EncodePng() => Array.Empty<byte>();
            public void Dispose() { }
        }

        sealed class MemImageService : IImageService
        {
            public IImage CreateImage(int w, int h) => new MemImage(w, h);
            public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new MemImage(w, h);
            public IImage LoadImage(string f) => null;
            public IImage LoadImageFromBytes(byte[] d) => null;
            public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
            {
                r = (byte)((gbaColor & 0x1F) << 3);
                g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
            }
            public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;
            public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => new MemImage(w, h);
            public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => new MemImage(w, h);
            public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new MemImage(w, h);
            public byte[] Encode4bppTiles(IImage i) => null;
            public byte[] Encode8bppTiles(IImage i) => null;
            public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
            public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
        }

        const int W = 128, H = 112;

        // 16-color GBA palette. Index i gets the 15-bit color i in the blue
        // channel so each index decodes to a UNIQUE RGBA byte triplet
        // (B = (i & 0x1F) << 3), making per-pixel index comparison trivial.
        static byte[] MakePalette()
        {
            byte[] pal = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)((i & 0x1F) << 10); // put index into the blue 5 bits
                pal[i * 2] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)(c >> 8);
            }
            return pal;
        }

        static void Fill(byte[] buf, int x, int y, int w, int h, byte v)
        {
            for (int yy = 0; yy < h; yy++)
            {
                int row = (y + yy) * W + x;
                for (int xx = 0; xx < w; xx++)
                {
                    int i = row + xx;
                    if (i >= 0 && i < buf.Length) buf[i] = v;
                }
            }
        }

        // Synthetic sheet: face=1, half-eye=2, closed-eye=3, mouth1=4, others base.
        static byte[] MakeSheet()
        {
            byte[] buf = new byte[W * H];
            Fill(buf, 0, 0, 96, 80, 1);    // face band
            Fill(buf, 96, 48, 32, 16, 2);  // half-eye slot
            Fill(buf, 96, 64, 32, 16, 3);  // closed-eye slot
            Fill(buf, 0, 80, 32, 16, 4);   // mouth1 slot
            return buf;
        }

        // Decode the rendered IImage's RGBA pixel at (x,y) back to a palette
        // index via the blue channel encoding from MakePalette.
        static int IndexAt(IImage img, int x, int y)
        {
            byte[] rgba = img.GetPixelData();
            int o = (y * img.Width + x) * 4;
            // Blue is channel 2; we encoded index into blue 5 bits -> B = idx<<3.
            // Alpha 0 means index 0 (transparent).
            if (rgba[o + 3] == 0) return 0;
            return rgba[o + 2] >> 3;
        }

        static IImage Render(byte[] sheet, int frame, bool isFe6 = false)
        {
            // eyeBlock (0,0) -> eye dest px (0,0); mouthBlock (0,8) -> mouth dest px (0,64).
            // Full-slot crop (0,0,32,16) so standardized slots == their source.
            return PortraitImportPreviewCore.RenderFramePreview(
                sheet, W, H, MakePalette(),
                eyeBlockX: 0, eyeBlockY: 0, mouthBlockX: 0, mouthBlockY: 8,
                eyeCropX: 0, eyeCropY: 0, eyeCropW: 32, eyeCropH: 16,
                mouthCropX: 0, mouthCropY: 0, mouthCropW: 32, mouthCropH: 16,
                frameIndex: frame, isFe6: isFe6);
        }

        // ----- Guard tests (no throw, null on bad input) -----

        [Fact]
        public void Render_NullImageService_ReturnsNull()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = null;
            try { Assert.Null(Render(MakeSheet(), 0)); }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Render_NullOrShortInputs_ReturnNullNoThrow()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                Assert.Null(PortraitImportPreviewCore.RenderFramePreview(
                    null, W, H, MakePalette(), 0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false));
                Assert.Null(PortraitImportPreviewCore.RenderFramePreview(
                    MakeSheet(), W, H, null, 0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false));
                // Too small (below 96x80).
                Assert.Null(PortraitImportPreviewCore.RenderFramePreview(
                    new byte[80 * 70], 80, 70, MakePalette(), 0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false));
                // Buffer shorter than width*height.
                Assert.Null(PortraitImportPreviewCore.RenderFramePreview(
                    new byte[10], W, H, MakePalette(), 0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false));
                // Palette < 32 bytes.
                Assert.Null(PortraitImportPreviewCore.RenderFramePreview(
                    MakeSheet(), W, H, new byte[10], 0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Theory]
        // Dimensions whose int product OVERFLOWS to a negative/wrong value, and
        // which also exceed MaxPixelCount. Must return null (no throw, no wrong
        // allocation), honoring the "null on unusable input" contract (#979).
        //   70000 * 70000 = 4,900,000,000 -> overflows int (wraps negative)
        //   46341 * 46341 = 2,147,488,281 -> just past int.MaxValue
        //   50000 * 50000 = 2,500,000,000 -> overflows int
        [InlineData(70000, 70000)]
        [InlineData(46341, 46341)]
        [InlineData(50000, 50000)]
        public void Render_PathologicalLargeDimensions_ReturnsNullNoThrow(int w, int h)
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                // A tiny buffer is fine: the long-arithmetic guard rejects the
                // oversized request BEFORE any allocation/copy or length check
                // against the (overflowed) int product.
                var ex = Record.Exception(() =>
                {
                    IImage img = PortraitImportPreviewCore.RenderFramePreview(
                        new byte[16], w, h, MakePalette(),
                        0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 0, false);
                    Assert.Null(img);
                });
                Assert.Null(ex); // never throws (OverflowException / OOM / OOR)
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Render_ValidSmallInputs_StillRenderAfterOverflowGuard()
        {
            // Regression: the overflow guard must NOT reject legitimate small
            // inputs. The standard 128x112 sheet still renders a 96x80 frame.
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 2);
                Assert.NotNull(img);
                Assert.Equal(96, img.Width);
                Assert.Equal(80, img.Height);
                Assert.Equal(3, IndexAt(img, 4, 4)); // closed-eye overlay
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Render_OutOfRangeFrame_DoesNotThrow_ShowsBase()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 999);
                Assert.NotNull(img);
                Assert.Equal(96, img.Width);
                Assert.Equal(80, img.Height);
                // Frame 999 falls through to base — eye block area shows face index 1.
                Assert.Equal(1, IndexAt(img, 8, 8));
            }
            finally { CoreState.ImageService = saved; }
        }

        // ----- Exact-pixel composite tests -----

        [Fact]
        public void Frame0_Base_EyeAndMouthRegionsShowFace()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 0);
                Assert.NotNull(img);
                // Eye block dest (0,0) region: base face = index 1.
                Assert.Equal(1, IndexAt(img, 4, 4));
                // Mouth block dest (0,64) region: base face = index 1.
                Assert.Equal(1, IndexAt(img, 4, 68));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Frame1_HalfEye_OverlaysEyeBlock()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 1);
                // Eye block dest (0,0): half-eye slot = index 2.
                Assert.Equal(2, IndexAt(img, 4, 4));
                // Mouth area untouched (base face = 1).
                Assert.Equal(1, IndexAt(img, 4, 68));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Frame2_ClosedEye_DiffersFromFrame0AndFrame1()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage f0 = Render(MakeSheet(), 0);
                using IImage f1 = Render(MakeSheet(), 1);
                using IImage f2 = Render(MakeSheet(), 2);
                // Closed-eye slot = index 3 at the eye block dest (0,0).
                Assert.Equal(3, IndexAt(f2, 4, 4));
                // Distinct from base (1) and half-eye (2).
                Assert.NotEqual(IndexAt(f0, 4, 4), IndexAt(f2, 4, 4));
                Assert.NotEqual(IndexAt(f1, 4, 4), IndexAt(f2, 4, 4));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Frame3_Mouth1_OverlaysMouthBlock_NotEyeBlock()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 3);
                // Mouth block dest (0,64): mouth1 slot = index 4.
                Assert.Equal(4, IndexAt(img, 4, 68));
                // Eye block stays base face = 1 (no eye overlay for a mouth frame).
                Assert.Equal(1, IndexAt(img, 4, 4));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Frame10_PositionCheck_OverlaysBothEyeAndMouth()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 10);
                // Eye block: half-eye = index 2.
                Assert.Equal(2, IndexAt(img, 4, 4));
                // Mouth block: mouth1 = index 4.
                Assert.Equal(4, IndexAt(img, 4, 68));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Fe6_SkipsEyeOverlay_MouthStillWorks()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                // FE6 closed-eye frame: eye overlay is skipped -> eye block stays base 1.
                using IImage eyeFe6 = Render(MakeSheet(), 2, isFe6: true);
                Assert.Equal(1, IndexAt(eyeFe6, 4, 4));

                // But mouth frames still composite on FE6.
                using IImage mouthFe6 = Render(MakeSheet(), 3, isFe6: true);
                Assert.Equal(4, IndexAt(mouthFe6, 4, 68));

                // Sanity: on FE7/8 the same eye frame DOES overlay (index 3).
                using IImage eyeFe78 = Render(MakeSheet(), 2, isFe6: false);
                Assert.Equal(3, IndexAt(eyeFe78, 4, 4));
            }
            finally { CoreState.ImageService = saved; }
        }

        // ----- Overlay is transparent-on-index-0 (review fix #979) -----

        [Fact]
        public void Overlay_Index0SlotPixels_PreserveBaseFace_NotPunchHoles()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                // Base face = solid index 1 (non-zero, so we can prove it is
                // PRESERVED under a transparent overlay pixel). The closed-eye
                // slot (sheet 96,64) has a transparent (index 0) TOP-LEFT corner
                // pixel and an index-5 body. The block region under the eye dest
                // is part of the index-1 face.
                byte[] sheet = new byte[W * H];
                Fill(sheet, 0, 0, 96, 80, 1);   // whole face = 1
                Fill(sheet, 96, 64, 32, 16, 5); // closed-eye slot body = 5
                sheet[64 * W + 96] = 0;         // slot top-left pixel = index 0 (transparent)

                // Frame 2 (closed eyes), eyeBlock (0,0) -> overlay dest (0,0).
                // Full crop so the standardized slot mirrors the source slot,
                // keeping the index-0 corner pixel.
                using IImage img = Render(sheet, 2);
                Assert.NotNull(img);

                // The transparent (index 0) corner pixel of the slot must NOT be
                // copied: the base face (index 1) shows through at dest (0,0).
                Assert.Equal(1, IndexAt(img, 0, 0));
                // The non-zero slot body DOES replace the face elsewhere in the
                // overlay region.
                Assert.Equal(5, IndexAt(img, 4, 4));
                Assert.Equal(5, IndexAt(img, 20, 10));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void BlitIndexed_OpaqueDefault_CopiesIndex0()
        {
            // Direct unit test of the blit helper's two modes: the OPAQUE default
            // (transparent_index 0xFF) copies index 0; transparent-on-0 skips it.
            byte[] dst = new byte[4]; // 2x2 all index 7
            for (int i = 0; i < dst.Length; i++) dst[i] = 7;
            byte[] src = { 0, 0, 0, 0 }; // 2x2 all index 0

            // Opaque (default): index 0 IS written -> dst becomes 0.
            byte[] d1 = (byte[])dst.Clone();
            PortraitImportPreviewCore.BlitIndexed(src, 2, 2, 0, 0, 2, 2, d1, 2, 2, 0, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, d1);

            // Transparent-on-0: index 0 is SKIPPED -> dst stays 7.
            byte[] d2 = (byte[])dst.Clone();
            PortraitImportPreviewCore.BlitIndexed(src, 2, 2, 0, 0, 2, 2, d2, 2, 2, 0, 0, transparentIndex: 0);
            Assert.Equal(new byte[] { 7, 7, 7, 7 }, d2);
        }

        // ----- Crop-rect change actually changes the composite (review #4) -----

        [Fact]
        public void MouthCropChange_ChangesStandardizedSlot_AndRenderedFrame()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                // Build a sheet where the mouth BLOCK region of the face (at the
                // mouth-block dest (0,64)) is index 1, but the mouth source slot
                // top-left has a DIFFERENT color band so a partial crop leaves
                // the block-base showing outside the crop rect.
                byte[] sheet = new byte[W * H];
                Fill(sheet, 0, 0, 96, 80, 1);   // whole face = 1 (incl. mouth block region)
                Fill(sheet, 0, 80, 32, 16, 5);  // mouth1 source slot = index 5

                // FULL crop (32x16): the whole standardized mouth slot is rebuilt
                // from the source slot -> the entire 32x16 mouth dest becomes 5.
                using IImage full = PortraitImportPreviewCore.RenderFramePreview(
                    sheet, W, H, MakePalette(),
                    0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 32, 16, 3, false);
                Assert.Equal(5, IndexAt(full, 1, 65));   // inside crop
                Assert.Equal(5, IndexAt(full, 30, 78));  // far corner inside full crop

                // SMALL crop (8x8 at 0,0): only the top-left 8x8 of the slot is
                // overlaid from the source; the rest of the slot keeps the
                // block-base (face index 1). So the far corner differs.
                using IImage small = PortraitImportPreviewCore.RenderFramePreview(
                    sheet, W, H, MakePalette(),
                    0, 0, 0, 8, 0, 0, 32, 16, 0, 0, 8, 8, 3, false);
                Assert.Equal(5, IndexAt(small, 1, 65));   // inside the 8x8 crop
                Assert.Equal(1, IndexAt(small, 30, 78));  // outside crop -> block-base face

                // Proves the crop value genuinely changes the rendered frame.
                Assert.NotEqual(IndexAt(full, 30, 78), IndexAt(small, 30, 78));
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void Render_Returns96x80Image()
        {
            var saved = CoreState.ImageService;
            CoreState.ImageService = new MemImageService();
            try
            {
                using IImage img = Render(MakeSheet(), 0);
                Assert.NotNull(img);
                Assert.Equal(96, img.Width);
                Assert.Equal(80, img.Height);
            }
            finally { CoreState.ImageService = saved; }
        }
    }
}
