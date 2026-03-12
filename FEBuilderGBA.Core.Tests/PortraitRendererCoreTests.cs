using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PortraitRendererCoreTests
    {
        [Fact]
        public void DrawPortraitUnit_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitUnit(0x08000000, 0x08000100, 0, 0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitMap_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitMap(0x08000000, 0x08000100);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitClass_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitClass(0x08000000, 0x08000100);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitUnit_InvalidPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                // Zero pointer should return null
                var result = PortraitRendererCore.DrawPortraitUnit(0, 0, 0, 0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitMap_ZeroPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitMap(0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitClass_ZeroPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitClass(0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void BlitPixels_CopiesCorrectRegion()
        {
            // 4x4 source image, RGBA
            byte[] src = new byte[4 * 4 * 4];
            // Set pixel at (1,1) to red
            int idx = (1 * 4 + 1) * 4;
            src[idx + 0] = 255; // R
            src[idx + 1] = 0;   // G
            src[idx + 2] = 0;   // B
            src[idx + 3] = 255; // A

            // 4x4 destination
            byte[] dst = new byte[4 * 4 * 4];

            // Blit 2x2 region from (1,1) to dest (0,0)
            PortraitRendererCore.BlitPixels(src, 4, 1, 1, 2, 2, dst, 4, 0, 0);

            // The source pixel (1,1) should now be at dest (0,0)
            Assert.Equal(255, dst[0]); // R
            Assert.Equal(0, dst[1]);   // G
            Assert.Equal(0, dst[2]);   // B
            Assert.Equal(255, dst[3]); // A
        }

        [Fact]
        public void BlitPixelsWithTransparency_SkipsTransparent()
        {
            // 2x1 source: pixel 0 transparent, pixel 1 opaque green
            byte[] src = new byte[2 * 1 * 4];
            src[0] = 0; src[1] = 0; src[2] = 0; src[3] = 0;       // transparent
            src[4] = 0; src[5] = 255; src[6] = 0; src[7] = 255;    // green, opaque

            // 2x1 destination: pre-fill with blue
            byte[] dst = new byte[2 * 1 * 4];
            dst[0] = 0; dst[1] = 0; dst[2] = 255; dst[3] = 255;   // blue
            dst[4] = 0; dst[5] = 0; dst[6] = 255; dst[7] = 255;   // blue

            PortraitRendererCore.BlitPixelsWithTransparency(src, 2, 0, 0, 2, 1, dst, 2, 0, 0);

            // Pixel 0: should remain blue (source was transparent)
            Assert.Equal(0, dst[0]);
            Assert.Equal(0, dst[1]);
            Assert.Equal(255, dst[2]);
            Assert.Equal(255, dst[3]);

            // Pixel 1: should be green (source was opaque)
            Assert.Equal(0, dst[4]);
            Assert.Equal(255, dst[5]);
            Assert.Equal(0, dst[6]);
            Assert.Equal(255, dst[7]);
        }

        [Fact]
        public void DrawPortraitUnitWithFrame_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitUnitWithFrame(
                    0x08000000, 0x08000100, 0x08000200, 2, 3, 4, 5, 0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitUnitWithFrame_ZeroPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitUnitWithFrame(0, 0, 0, 0, 0, 0, 0, 0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawMouthFrameStrip_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawMouthFrameStrip(0x08000200, 0x08000100);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawEyeFrameStrip_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawEyeFrameStrip(0x08000000, 0x08000100);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawMouthFrameStrip_ZeroPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawMouthFrameStrip(0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawEyeFrameStrip_ZeroPointer_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawEyeFrameStrip(0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void FrameConstants_HaveExpectedValues()
        {
            Assert.Equal(0, PortraitRendererCore.FrameNormal);
            Assert.Equal(1, PortraitRendererCore.FrameEyeHalf);
            Assert.Equal(2, PortraitRendererCore.FrameEyeClosed);
            Assert.Equal(3, PortraitRendererCore.FrameMouth1);
            Assert.Equal(8, PortraitRendererCore.FrameMouth6);
            Assert.Equal(9, PortraitRendererCore.FrameMouth7);
            Assert.Equal(9, PortraitRendererCore.MaxShowFrame);
        }

        [Fact]
        public void SplitPortraitSheet_WrongSize_ReturnsNull()
        {
            // Non-128x112 should return null
            byte[] rgba = new byte[100 * 100 * 4];
            Assert.Null(PortraitRendererCore.SplitPortraitSheet(rgba, 100, 100));
        }

        [Fact]
        public void SplitPortraitSheet_NullInput_ReturnsNull()
        {
            Assert.Null(PortraitRendererCore.SplitPortraitSheet(null, 128, 112));
        }

        [Fact]
        public void SplitPortraitSheet_TooSmallBuffer_ReturnsNull()
        {
            byte[] rgba = new byte[10]; // way too small
            Assert.Null(PortraitRendererCore.SplitPortraitSheet(rgba, 128, 112));
        }

        [Fact]
        public void SplitPortraitSheet_ValidSize_ReturnsParts()
        {
            byte[] rgba = new byte[128 * 112 * 4];
            // Paint a distinctive pixel at the mini face location (96, 16)
            int miniPixelIdx = (16 * 128 + 96) * 4;
            rgba[miniPixelIdx + 0] = 255; // R
            rgba[miniPixelIdx + 1] = 128; // G
            rgba[miniPixelIdx + 2] = 64;  // B
            rgba[miniPixelIdx + 3] = 255; // A

            var parts = PortraitRendererCore.SplitPortraitSheet(rgba, 128, 112);
            Assert.NotNull(parts);

            // Verify dimensions
            Assert.Equal(256, parts.SpriteSheetW);
            Assert.Equal(32, parts.SpriteSheetH);
            Assert.Equal(256 * 32 * 4, parts.SpriteSheetPixels.Length);

            Assert.Equal(32, parts.MiniW);
            Assert.Equal(32, parts.MiniH);
            Assert.Equal(32 * 32 * 4, parts.MiniPixels.Length);

            Assert.Equal(32, parts.MouthW);
            Assert.Equal(96, parts.MouthH);
            Assert.Equal(32 * 96 * 4, parts.MouthPixels.Length);

            // The pixel at sheet (96, 16) = mini face (0, 0)
            Assert.Equal(255, parts.MiniPixels[0]); // R
            Assert.Equal(128, parts.MiniPixels[1]); // G
            Assert.Equal(64, parts.MiniPixels[2]);   // B
            Assert.Equal(255, parts.MiniPixels[3]); // A
        }

        [Fact]
        public void SplitPortraitSheet_FaceRegion_MapsToSpriteSheet()
        {
            byte[] rgba = new byte[128 * 112 * 4];
            // Paint a pixel in the upper face region at sheet (16, 0) = face upper-left
            // This should map to sprite sheet (0, 0)
            int facePixelIdx = (0 * 128 + 16) * 4;
            rgba[facePixelIdx + 0] = 100;
            rgba[facePixelIdx + 1] = 200;
            rgba[facePixelIdx + 2] = 50;
            rgba[facePixelIdx + 3] = 255;

            var parts = PortraitRendererCore.SplitPortraitSheet(rgba, 128, 112);
            Assert.NotNull(parts);

            // Sheet face (16, 0) -> sprite sheet (0, 0)
            Assert.Equal(100, parts.SpriteSheetPixels[0]);
            Assert.Equal(200, parts.SpriteSheetPixels[1]);
            Assert.Equal(50, parts.SpriteSheetPixels[2]);
            Assert.Equal(255, parts.SpriteSheetPixels[3]);
        }

        [Fact]
        public void SplitPortraitSheet_MouthFrames_MappedCorrectly()
        {
            byte[] rgba = new byte[128 * 112 * 4];
            // Paint a pixel at mouth frame 0 location: sheet (0, 80)
            int mouthPixelIdx = (80 * 128 + 0) * 4;
            rgba[mouthPixelIdx + 0] = 10;
            rgba[mouthPixelIdx + 1] = 20;
            rgba[mouthPixelIdx + 2] = 30;
            rgba[mouthPixelIdx + 3] = 255;

            var parts = PortraitRendererCore.SplitPortraitSheet(rgba, 128, 112);
            Assert.NotNull(parts);

            // Mouth frame 0 at (0, 0) in mouth pixels
            Assert.Equal(10, parts.MouthPixels[0]);
            Assert.Equal(20, parts.MouthPixels[1]);
            Assert.Equal(30, parts.MouthPixels[2]);
            Assert.Equal(255, parts.MouthPixels[3]);
        }
    }
}
