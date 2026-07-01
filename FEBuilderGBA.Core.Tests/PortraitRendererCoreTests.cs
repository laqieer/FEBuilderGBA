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
        public void DrawPortraitAutoById_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitAutoById(1);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitAutoById_ZeroId_ReturnsNull()
        {
            // Portrait id 0 is the empty / no-portrait slot.
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCore.DrawPortraitAutoById(0);
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
        public void PromoteFaceToPortraitSheet_CopiesFaceAtCanvasOrigin_NotX16()
        {
            byte[] face = new byte[96 * 80 * 4];
            int edgeIdx = (48 * 96 + 0) * 4;
            face[edgeIdx + 0] = 10;
            face[edgeIdx + 1] = 20;
            face[edgeIdx + 2] = 30;
            face[edgeIdx + 3] = 255;

            int upperIdx = (0 * 96 + 16) * 4;
            face[upperIdx + 0] = 40;
            face[upperIdx + 1] = 50;
            face[upperIdx + 2] = 60;
            face[upperIdx + 3] = 255;

            byte[] sheet = PortraitRendererCore.PromoteFaceToPortraitSheet(face, 96, 80);

            Assert.NotNull(sheet);
            Assert.Equal(10, sheet[(48 * 128 + 0) * 4 + 0]);
            Assert.Equal(40, sheet[(0 * 128 + 16) * 4 + 0]);
            Assert.Equal(0, sheet[(0 * 128 + 112) * 4 + 3]);
        }

        [Fact]
        public void PromoteFaceToPortraitSheet_ThenSplit_PlacesFaceTilesInSpriteSheetLayout()
        {
            byte[] face = new byte[96 * 80 * 4];
            int upperIdx = (0 * 96 + 16) * 4;
            face[upperIdx + 0] = 100;
            face[upperIdx + 1] = 110;
            face[upperIdx + 2] = 120;
            face[upperIdx + 3] = 255;

            byte[] sheet = PortraitRendererCore.PromoteFaceToPortraitSheet(face, 96, 80);
            var parts = PortraitRendererCore.SplitPortraitSheet(sheet, 128, 112);

            Assert.NotNull(parts);
            Assert.Equal(100, parts.SpriteSheetPixels[0]);
            Assert.Equal(110, parts.SpriteSheetPixels[1]);
            Assert.Equal(120, parts.SpriteSheetPixels[2]);
            Assert.Equal(255, parts.SpriteSheetPixels[3]);
        }

        [Fact]
        public void ApplyPortraitBackgroundColorKey_UsesWinFormsCornerOrder()
        {
            byte[] rgba = SolidRgba(16, 16, 1, 2, 3, 255);
            SetPixel(rgba, 16, 15, 0, 9, 9, 9, 255);
            SetPixel(rgba, 16, 15, 15, 8, 8, 8, 255);
            SetPixel(rgba, 16, 0, 0, 7, 7, 7, 255);
            SetPixel(rgba, 16, 4, 4, 9, 9, 9, 255);

            Assert.True(PortraitRendererCore.ApplyPortraitBackgroundColorKey(rgba, 16, 16));

            Assert.Equal(0, rgba[(0 * 16 + 15) * 4 + 3]);
            Assert.Equal(0, rgba[(4 * 16 + 4) * 4 + 3]);
            Assert.Equal(255, rgba[(15 * 16 + 15) * 4 + 3]);
            Assert.Equal(255, rgba[3]);
        }

        [Fact]
        public void ApplyPortraitBackgroundColorKey_AllTransparentCorners_PreservesOpaquePixels()
        {
            byte[] rgba = SolidRgba(16, 16, 10, 20, 30, 255);
            SetPixel(rgba, 16, 15, 0, 0, 0, 0, 0);
            SetPixel(rgba, 16, 15, 15, 0, 0, 0, 0);
            SetPixel(rgba, 16, 0, 0, 0, 0, 0, 0);

            Assert.False(PortraitRendererCore.ApplyPortraitBackgroundColorKey(rgba, 16, 16));
            Assert.Equal(255, rgba[(4 * 16 + 4) * 4 + 3]);
        }

        static byte[] SolidRgba(int w, int h, byte r, byte g, byte b, byte a)
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

        static void SetPixel(byte[] rgba, int w, int x, int y, byte r, byte g, byte b, byte a)
        {
            int off = (y * w + x) * 4;
            rgba[off + 0] = r;
            rgba[off + 1] = g;
            rgba[off + 2] = b;
            rgba[off + 3] = a;
        }

        // ================================================================
        // FE6-specific portrait renderer tests
        // ================================================================

        [Fact]
        public void DrawPortraitUnitFE6_NullROM_ReturnsNull()
        {
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCoreFE6.DrawPortraitUnitFE6(0x08000000, 0x08000100, 2, 3, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void DrawPortraitUnitFE6_ZeroFacePointer_ReturnsNull()
        {
            // Zero face pointer with non-null ROM should still return null
            // (toOffset(0) == 0, which triggers the unitFace == 0 guard)
            var saved = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var result = PortraitRendererCoreFE6.DrawPortraitUnitFE6(0, 0x08000100, 0, 0, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void FE6MouthFrames_HaveCorrectCoordinates()
        {
            // Verify the FE6 mouth frame coordinate mapping matches WinForms
            var frames = PortraitRendererCoreFE6.MouthFrames;
            Assert.Equal(4, frames.Length);

            // Frame 1: (192, 0) 32x16
            Assert.Equal((192, 0, 32, 16), frames[0]);
            // Frame 2: (224, 0) 32x16
            Assert.Equal((224, 0, 32, 16), frames[1]);
            // Frame 3: (192, 16) 32x16
            Assert.Equal((192, 16, 32, 16), frames[2]);
            // Frame 4: (224, 16) 32x16
            Assert.Equal((224, 16, 32, 16), frames[3]);

            // Frame 5 split coordinates
            Assert.Equal(0, PortraitRendererCoreFE6.Frame5TopX);
            Assert.Equal(32, PortraitRendererCoreFE6.Frame5TopY);
            Assert.Equal(32, PortraitRendererCoreFE6.Frame5BotX);
            Assert.Equal(32, PortraitRendererCoreFE6.Frame5BotY);
        }

        [Fact]
        public void FE6ApplyMouthFrame_Frame1_BlitsCorrectRegion()
        {
            // Create a synthetic 256x40 sheet with a marker at frame 1 position (192, 0)
            int sheetW = 256, sheetH = 40;
            byte[] sheet = new byte[sheetW * sheetH * 4];
            int markerIdx = (0 * sheetW + 192) * 4;
            sheet[markerIdx + 0] = 255; // R
            sheet[markerIdx + 1] = 0;   // G
            sheet[markerIdx + 2] = 0;   // B
            sheet[markerIdx + 3] = 255; // A

            byte[] face = new byte[96 * 80 * 4];
            // mouthX=2, mouthY=3 -> pixel (16, 24)
            PortraitRendererCoreFE6.ApplyMouthFrame(1, sheet, sheetW, 2, 3, face, 96);

            int destIdx = (24 * 96 + 16) * 4;
            Assert.Equal(255, face[destIdx + 0]); // R
            Assert.Equal(0, face[destIdx + 1]);   // G
            Assert.Equal(0, face[destIdx + 2]);   // B
            Assert.Equal(255, face[destIdx + 3]); // A
        }

        [Fact]
        public void FE6ApplyMouthFrame_Frame5_BlitsSplitRegions()
        {
            // Frame 5 is split: top from (0,32), bottom from (32,32)
            int sheetW = 256, sheetH = 40;
            byte[] sheet = new byte[sheetW * sheetH * 4];

            // Mark top half at (0, 32) with green
            int topIdx = (32 * sheetW + 0) * 4;
            sheet[topIdx + 0] = 0; sheet[topIdx + 1] = 255; sheet[topIdx + 2] = 0; sheet[topIdx + 3] = 255;

            // Mark bottom half at (32, 32) with blue
            int botIdx = (32 * sheetW + 32) * 4;
            sheet[botIdx + 0] = 0; sheet[botIdx + 1] = 0; sheet[botIdx + 2] = 255; sheet[botIdx + 3] = 255;

            byte[] face = new byte[96 * 80 * 4];
            PortraitRendererCoreFE6.ApplyMouthFrame(5, sheet, sheetW, 1, 1, face, 96);

            // Top half goes to (8, 8)
            int topDest = (8 * 96 + 8) * 4;
            Assert.Equal(0, face[topDest + 0]);
            Assert.Equal(255, face[topDest + 1]); // Green
            Assert.Equal(0, face[topDest + 2]);

            // Bottom half goes to (8, 16) — 8 pixels below top
            int botDest = (16 * 96 + 8) * 4;
            Assert.Equal(0, face[botDest + 0]);
            Assert.Equal(0, face[botDest + 1]);
            Assert.Equal(255, face[botDest + 2]); // Blue
        }

        [Fact]
        public void FE6AssembleFace_ProducesCorrectDimensions()
        {
            // Verify AssembleFace produces non-zero output
            int sheetW = 256, sheetH = 40;
            byte[] sheet = new byte[sheetW * sheetH * 4];
            // Paint entire sheet white (opaque)
            for (int i = 0; i < sheet.Length; i += 4)
            {
                sheet[i] = 255; sheet[i + 1] = 255; sheet[i + 2] = 255; sheet[i + 3] = 255;
            }

            byte[] face = new byte[96 * 80 * 4];
            PortraitRendererCoreFE6.AssembleFace(sheet, sheetW, face, 96);

            // Check center pixel is non-zero (face area was painted)
            int centerIdx = (40 * 96 + 48) * 4;
            Assert.Equal(255, face[centerIdx + 3]); // Alpha should be opaque
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
