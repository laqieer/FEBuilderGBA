// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1917 — deterministic tests for <see cref="PortraitImportPreviewCore.ReconstructSheetCellsRgba"/>,
    /// the RGBA eye/mouth cell reconstruction the 128x112 sheet import now runs
    /// before splitting (mirroring WinForms <c>DecreaseColor16</c>).
    ///
    /// The bug: a hackbox sheet's eye/mouth cells sit on a background color
    /// (white) DIFFERENT from the face's keyed background (teal), so the cell
    /// background stayed opaque and the game blitted it as a solid rectangle over
    /// the face. The fix reseeds each cell from the destination face block (whose
    /// background is already transparent after the import's color key) and
    /// overlays only the cropped feature — so the opaque cell background is
    /// replaced by the transparent face background.
    ///
    /// These tests build a synthetic 128x112 RGBA sheet with a WHITE OPAQUE
    /// eye/mouth cell background + a TRANSPARENT face block + a distinct feature
    /// in the crop area, and assert the reconstruction replaces the cell
    /// background with the transparent face block while preserving the feature.
    /// Pure RGBA — no ROM, no IImageService — so it runs everywhere in CI.
    /// </summary>
    public class PortraitSheetReconstructTests
    {
        const int W = 128, H = 112;

        static byte[] NewSheet() => new byte[W * H * 4];

        static void SetPixel(byte[] rgba, int x, int y, byte r, byte g, byte b, byte a)
        {
            int i = (y * W + x) * 4;
            rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
        }

        static void FillRect(byte[] rgba, int x0, int y0, int w, int h, byte r, byte g, byte b, byte a)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    SetPixel(rgba, x, y, r, g, b, a);
        }

        static (byte r, byte g, byte b, byte a) GetPixel(byte[] rgba, int x, int y)
        {
            int i = (y * W + x) * 4;
            return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        }

        // Eye standard slots inside the 128x112 sheet.
        const int EyeHalfX = 96, EyeHalfY = 48;
        const int EyeClosedX = 96, EyeClosedY = 64;
        // First mouth slot.
        const int Mouth0X = 0, Mouth0Y = 80;

        [Fact]
        public void ReconstructSheetCellsRgba_ReplacesEyeCellWhiteBackground_WithTransparentFaceBlock()
        {
            byte[] sheet = NewSheet();
            // Whole sheet = WHITE OPAQUE (the hackbox cell background).
            FillRect(sheet, 0, 0, W, H, 255, 255, 255, 255);

            // Eye destination block = (2,2) tiles -> (16,16), 32x16. Make it
            // TRANSPARENT (the face's keyed background around the eyes).
            int eyeBlockX = 2, eyeBlockY = 2;
            FillRect(sheet, eyeBlockX * 8, eyeBlockY * 8, 32, 16, 0, 0, 0, 0);

            // Eye crop = (4,4) 8x8. The crop SOURCE is (cropX+slotX, cropY+slotY);
            // paint a distinct FEATURE there for both eye slots.
            int cx = 4, cy = 4, cw = 8, ch = 8;
            FillRect(sheet, cx + EyeHalfX,   cy + EyeHalfY,   cw, ch, 200, 40, 40, 255);
            FillRect(sheet, cx + EyeClosedX, cy + EyeClosedY, cw, ch, 200, 40, 40, 255);

            // Sanity: before reconstruction the eye-cell background is WHITE OPAQUE.
            Assert.Equal((byte)255, GetPixel(sheet, EyeHalfX + 20, EyeHalfY + 2).a);

            PortraitImportPreviewCore.ReconstructSheetCellsRgba(
                sheet, W, H,
                eyeBlockX, eyeBlockY, /*mouthBlock*/ 1, 5,
                cx, cy, cw, ch,          // eye crop
                0, 0, 0, 0,              // mouth crop 0 -> mouth slots untouched
                isFe6: false);

            // Background OUTSIDE the crop: now the TRANSPARENT face block, not white.
            var halfBg = GetPixel(sheet, EyeHalfX + 20, EyeHalfY + 2);
            Assert.Equal((byte)0, halfBg.a);
            var closedBg = GetPixel(sheet, EyeClosedX + 20, EyeClosedY + 2);
            Assert.Equal((byte)0, closedBg.a);

            // Feature INSIDE the crop survives (would be transparent if the crop
            // overlay were broken).
            var halfFeat = GetPixel(sheet, EyeHalfX + cx, EyeHalfY + cy);
            Assert.Equal(((byte)200, (byte)40, (byte)40, (byte)255), halfFeat);
            var closedFeat = GetPixel(sheet, EyeClosedX + cx, EyeClosedY + cy);
            Assert.Equal(((byte)200, (byte)40, (byte)40, (byte)255), closedFeat);
        }

        [Fact]
        public void ReconstructSheetCellsRgba_ReplacesMouthCellWhiteBackground_WithTransparentFaceBlock()
        {
            byte[] sheet = NewSheet();
            FillRect(sheet, 0, 0, W, H, 255, 255, 255, 255);

            // Mouth block = (1,5) tiles -> (8,40), 32x16, transparent.
            int mouthBlockX = 1, mouthBlockY = 5;
            FillRect(sheet, mouthBlockX * 8, mouthBlockY * 8, 32, 16, 0, 0, 0, 0);

            int cx = 6, cy = 2, cw = 10, ch = 8;
            FillRect(sheet, cx + Mouth0X, cy + Mouth0Y, cw, ch, 30, 30, 220, 255);

            PortraitImportPreviewCore.ReconstructSheetCellsRgba(
                sheet, W, H,
                /*eyeBlock*/ 2, 2, mouthBlockX, mouthBlockY,
                0, 0, 0, 0,              // eye crop 0 -> eye slots untouched
                cx, cy, cw, ch,          // mouth crop
                isFe6: false);

            var mouthBg = GetPixel(sheet, Mouth0X + 24, Mouth0Y + 1);
            Assert.Equal((byte)0, mouthBg.a);
            var mouthFeat = GetPixel(sheet, Mouth0X + cx, Mouth0Y + cy);
            Assert.Equal(((byte)30, (byte)30, (byte)220, (byte)255), mouthFeat);
        }

        [Fact]
        public void ReconstructSheetCellsRgba_SkipsFeature_WhenCropSizeZero()
        {
            byte[] sheet = NewSheet();
            FillRect(sheet, 0, 0, W, H, 255, 255, 255, 255);
            FillRect(sheet, 16, 16, 32, 16, 0, 0, 0, 0); // eye block transparent

            // Both crops zero-sized -> nothing reconstructed; the eye cell stays
            // WHITE OPAQUE (guards the per-feature gate the non-wizard paths rely on).
            PortraitImportPreviewCore.ReconstructSheetCellsRgba(
                sheet, W, H, 2, 2, 1, 5,
                0, 0, 0, 0,
                0, 0, 0, 0,
                isFe6: false);

            var bg = GetPixel(sheet, EyeHalfX + 20, EyeHalfY + 2);
            Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), bg);
        }

        [Fact]
        public void ReconstructSheetCellsRgba_Fe6_SkipsEyeSlots()
        {
            byte[] sheet = NewSheet();
            FillRect(sheet, 0, 0, W, H, 255, 255, 255, 255);
            FillRect(sheet, 16, 16, 32, 16, 0, 0, 0, 0);

            // isFe6 = true: eye slots must NOT be reconstructed even with a valid
            // eye crop (FE6 has no eye states).
            PortraitImportPreviewCore.ReconstructSheetCellsRgba(
                sheet, W, H, 2, 2, 1, 5,
                4, 4, 8, 8,   // eye crop provided
                0, 0, 0, 0,
                isFe6: true);

            var bg = GetPixel(sheet, EyeHalfX + 20, EyeHalfY + 2);
            Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), bg);
        }
    }
}
