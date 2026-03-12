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
    }
}
