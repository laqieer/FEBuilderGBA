// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform per-frame live-preview renderer for the Avalonia Portrait
    /// Import Wizard (#975, follow-up to #717 / #707 Slice A).
    ///
    /// Ports WinForms <c>ImagePortraitImporterForm.GenPreviewMainChar</c>
    /// (<c>:251-319</c>) PLUS the sheet-reorganization half of
    /// <c>DecreaseColor16</c> (<c>:166-228</c>) onto an in-memory indexed-pixel
    /// buffer — NO System.Drawing dependency (Core stays platform-independent).
    ///
    /// The wizard hands us its already-16-color-quantized SOURCE image
    /// (<c>ImageImportService.LoadResult.IndexedPixels</c> + <c>GBAPalette</c>),
    /// which is a 128x112 portrait composite sheet for sheet imports. WF works
    /// on the same 128x112 sheet layout:
    ///   - Base face:        sheet (0,0) 96x80
    ///   - Half-closed eyes: sheet (96,48) 32x16      (FE7/8 only)
    ///   - Closed eyes:      sheet (96,64) 32x16      (FE7/8 only)
    ///   - Mouth 1..7:       sheet (0,80)(32,80)(64,80)(96,80)(0,96)(32,96)(64,96)
    ///
    /// Two stages, mirroring WF exactly:
    ///   STAGE A (reorganize) — like <c>DecreaseColor16</c>: for each eye/mouth
    ///     standard slot, copy the destination BLOCK region of the face
    ///     (<c>blockX*8, blockY*8, 32, 16</c>) then overlay the user's CROP
    ///     rectangle pulled from that slot's own source area, then write the
    ///     rebuilt 32x16 tile back into the standard slot. This is WHY the crop
    ///     NumericUpDowns matter — without it, the crop values would be inert
    ///     (Copilot CLI plan review blocking #1).
    ///   STAGE B (composite) — like <c>GenPreviewMainChar</c>: blit the base
    ///     96x80 face, then overlay the standard slot selected by
    ///     <c>frameIndex</c> at the eye-block / mouth-block destination.
    ///
    /// Transparency, matching WF <c>ImageUtil.BitBlt</c>:
    ///   - The base-face copy and the STAGE-A slot rebuild are OPAQUE
    ///     (<c>transparent_index=0xff</c> never matches a 0..15 index, so
    ///     nothing is skipped — WF <c>DecreaseColor16</c> uses the default).
    ///   - The STAGE-B eye/mouth OVERLAY is TRANSPARENT-on-index-0: a slot
    ///     pixel whose index == 0 is SKIPPED so the base face shows through
    ///     instead of leaving a transparent hole (WF <c>GenPreviewMainChar</c>
    ///     overlays pass <c>transparent_index: 0</c>; #979 review fix).
    /// All blits use full bounds clamping.
    ///
    /// SCOPE NOTES (Copilot CLI plan review):
    ///   - This previews the AUTO-QUANTIZED source only. Share-palette /
    ///     custom-palette / Fuchidori change only the FINAL written pixels, not
    ///     this source composite — the pane is a layout aid, not an exact
    ///     import-result preview (#975 plan review point 3).
    ///   - FE6 (<paramref name="isFe6"/>): the wizard's crop/frame NUDs are
    ///     disabled on FE6 (<c>ImagePortraitImporterView.UpdateDetailPanel</c>),
    ///     so FE6 never drives a composite interactively. The flag's only effect
    ///     here is to SKIP the eye-state frames (FE6 has no eye states — mirrors
    ///     WF <c>IsFE6Image</c> disabling the eye inputs). We do NOT import
    ///     <c>PortraitRendererCoreFE6</c>'s 256x40 ROM-tile coordinates — those
    ///     are a different coordinate space (decoded ROM graphics, not the
    ///     importer sheet) (#975 plan review point 2).
    /// </summary>
    public static class PortraitImportPreviewCore
    {
        // Importer composite-sheet geometry (pixels). These are the 128x112
        // SHEET coordinates, NOT the 256x40 ROM sprite-sheet coordinates used
        // by PortraitRendererCore.
        const int FaceWidth = 96;
        const int FaceHeight = 80;
        const int PartWidth = 32;
        const int PartHeight = 16;

        // Upper bound on the source pixel count (width * height) accepted by
        // RenderFramePreview. A real 16-color portrait import sheet is 128x112
        // (~14 K px); 64 Mpx is ~4500x larger — far above any legitimate input
        // while staying well inside int range so the later (int)pixelCount cast
        // and int-based allocation/copy cannot overflow (#979 review).
        const long MaxPixelCount = 64L * 1024 * 1024; // 67,108,864 px

        // Standard slot source coordinates inside the 128x112 sheet.
        const int EyeHalfSrcX = 96, EyeHalfSrcY = 48;   // sheet (96, 16*3)
        const int EyeClosedSrcX = 96, EyeClosedSrcY = 64; // sheet (96, 16*4)

        // Six mouth slots: (0,80)(32,80)(64,80)(96,80)(0,96)(32,96) and mouth7 (64,96).
        // Indexed 0..6 = WF frames 3..9.
        static readonly (int x, int y)[] MouthSlots =
        {
            (0, 80),   // mouth1  (frame 3)
            (32, 80),  // mouth2  (frame 4)
            (64, 80),  // mouth3  (frame 5)
            (96, 80),  // mouth4  (frame 6 — "status screen mouth 4" in WF)
            (0, 96),   // mouth5  (frame 7)
            (32, 96),  // mouth6  (frame 8)
            (64, 96),  // mouth7  (frame 9)
        };

        // Frame index constants (match PortraitFrameStrings / WF GenPreviewMainChar).
        public const int FrameNormal = 0;
        public const int FrameEyeHalf = 1;
        public const int FrameEyeClosed = 2;
        public const int FrameMouth1 = 3;
        public const int FrameMouth7 = 9;
        public const int FramePositionCheck = 10;
        public const int MaxFrame = 10;

        /// <summary>
        /// Render the per-frame live preview as a 96x80 <see cref="IImage"/>.
        /// Returns <c>null</c> (never throws) on any unusable input: null/short
        /// indexed buffer, null/short palette, source smaller than 96x80, or a
        /// missing <see cref="CoreState.ImageService"/>.
        /// </summary>
        /// <param name="indexedPixels">Quantized source: 1 byte/pixel, row-major, width*height.</param>
        /// <param name="width">Source width (px). Must be &gt;= 96.</param>
        /// <param name="height">Source height (px). Must be &gt;= 80.</param>
        /// <param name="palette">GBA palette bytes (2 bytes/color, &gt;= 32 bytes for 16 colors).</param>
        /// <param name="eyeBlockX">Eye destination block X (tile units; *8 = px).</param>
        /// <param name="eyeBlockY">Eye destination block Y (tile units).</param>
        /// <param name="mouthBlockX">Mouth destination block X (tile units).</param>
        /// <param name="mouthBlockY">Mouth destination block Y (tile units).</param>
        /// <param name="eyeCropX">Eye crop rect X within the eye source area (px).</param>
        /// <param name="eyeCropY">Eye crop rect Y (px).</param>
        /// <param name="eyeCropW">Eye crop rect width (px).</param>
        /// <param name="eyeCropH">Eye crop rect height (px).</param>
        /// <param name="mouthCropX">Mouth crop rect X (px).</param>
        /// <param name="mouthCropY">Mouth crop rect Y (px).</param>
        /// <param name="mouthCropW">Mouth crop rect width (px).</param>
        /// <param name="mouthCropH">Mouth crop rect height (px).</param>
        /// <param name="frameIndex">0=base, 1=half-eye, 2=closed-eye, 3-9=mouth1-7, 10=position check.</param>
        /// <param name="isFe6">When true, skip eye-state overlays (FE6 has no eye states).</param>
        public static IImage RenderFramePreview(
            byte[] indexedPixels, int width, int height, byte[] palette,
            int eyeBlockX, int eyeBlockY, int mouthBlockX, int mouthBlockY,
            int eyeCropX, int eyeCropY, int eyeCropW, int eyeCropH,
            int mouthCropX, int mouthCropY, int mouthCropW, int mouthCropH,
            int frameIndex, bool isFe6)
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;
            if (indexedPixels == null || palette == null) return null;
            if (width < FaceWidth || height < FaceHeight) return null;
            if (palette.Length < 32) return null;

            // Compute the pixel count in LONG arithmetic and bound it BEFORE any
            // int-based allocation/copy: `width * height` in `int` can overflow
            // to a negative/wrong value for pathologically large dimensions,
            // making the `indexedPixels.Length < ...` guard unreliable and the
            // `new byte[...]` allocation wrong — both violations of the
            // "return null on unusable input, never throw" contract (#979
            // review). `width`/`height` are already known >= 96/80 here (so
            // they are positive). MaxPixelCount caps a single 16-color portrait
            // sheet well above any real source while staying inside int range.
            long pixelCount = (long)width * height;
            if (pixelCount > MaxPixelCount) return null;
            if (indexedPixels.Length < pixelCount) return null;

            // pixelCount is now proven <= MaxPixelCount (< int.MaxValue), so the
            // int cast and int-based allocation/copy below cannot overflow.
            int pixelCountInt = (int)pixelCount;

            // Work on a private copy of the indexed buffer so the wizard's
            // LoadResult.IndexedPixels is never mutated.
            byte[] sheet = new byte[pixelCountInt];
            Array.Copy(indexedPixels, sheet, pixelCountInt);

            // STAGE A — reorganize the standard eye/mouth slots from the crop
            // rectangles (port of DecreaseColor16 :166-228). FE6 skips the eye
            // slots entirely (no eye states).
            if (!isFe6)
            {
                RebuildSlot(sheet, width, height,
                    eyeBlockX * 8, eyeBlockY * 8,
                    eyeCropX, eyeCropY, eyeCropW, eyeCropH,
                    EyeHalfSrcX, EyeHalfSrcY);     // half-eye standard slot
                RebuildSlot(sheet, width, height,
                    eyeBlockX * 8, eyeBlockY * 8,
                    eyeCropX, eyeCropY, eyeCropW, eyeCropH,
                    EyeClosedSrcX, EyeClosedSrcY); // closed-eye standard slot
            }
            for (int m = 0; m < MouthSlots.Length; m++)
            {
                RebuildSlot(sheet, width, height,
                    mouthBlockX * 8, mouthBlockY * 8,
                    mouthCropX, mouthCropY, mouthCropW, mouthCropH,
                    MouthSlots[m].x, MouthSlots[m].y);
            }

            // STAGE B — composite the selected frame onto a fresh 96x80 face.
            byte[] face = new byte[FaceWidth * FaceHeight];
            BlitIndexed(sheet, width, height, 0, 0,
                FaceWidth, FaceHeight, face, FaceWidth, FaceHeight, 0, 0);

            int eyeDX = eyeBlockX * 8, eyeDY = eyeBlockY * 8;
            int mouthDX = mouthBlockX * 8, mouthDY = mouthBlockY * 8;

            switch (frameIndex)
            {
                case FrameEyeHalf:
                    if (!isFe6) OverlaySlot(sheet, width, height, EyeHalfSrcX, EyeHalfSrcY, face, eyeDX, eyeDY);
                    break;
                case FrameEyeClosed:
                    if (!isFe6) OverlaySlot(sheet, width, height, EyeClosedSrcX, EyeClosedSrcY, face, eyeDX, eyeDY);
                    break;
                case FramePositionCheck:
                    // WF frame 10: overlay BOTH the half-eye and mouth1.
                    if (!isFe6) OverlaySlot(sheet, width, height, EyeHalfSrcX, EyeHalfSrcY, face, eyeDX, eyeDY);
                    OverlaySlot(sheet, width, height, MouthSlots[0].x, MouthSlots[0].y, face, mouthDX, mouthDY);
                    break;
                default:
                    if (frameIndex >= FrameMouth1 && frameIndex <= FrameMouth7)
                    {
                        var slot = MouthSlots[frameIndex - FrameMouth1];
                        OverlaySlot(sheet, width, height, slot.x, slot.y, face, mouthDX, mouthDY);
                    }
                    // frameIndex 0 (or any other value) = base face only.
                    break;
            }

            // Decode the 96x80 indexed face to RGBA via the palette and wrap it
            // in an IImage. Index 0 is treated transparent to match the wizard's
            // BuildPreviewImage / WF transparent-color-0 convention.
            byte[] rgba = DecodeIndexedToRgba(face, FaceWidth, FaceHeight, palette, svc);
            IImage img = svc.CreateImage(FaceWidth, FaceHeight);
            if (img == null) return null;
            img.SetPixelData(rgba);
            return img;
        }

        /// <summary>
        /// Rebuild one standard 32x16 slot at (<paramref name="slotSrcX"/>,
        /// <paramref name="slotSrcY"/>): seed it with the destination block
        /// region of the face, overlay the user's crop rectangle (read from the
        /// slot's own source area), then write the result back into the slot.
        /// Exact port of the DecreaseColor16 per-slot pattern (WF :178-211).
        /// </summary>
        static void RebuildSlot(byte[] sheet, int sheetW, int sheetH,
            int blockSrcX, int blockSrcY,
            int cropX, int cropY, int cropW, int cropH,
            int slotSrcX, int slotSrcY)
        {
            // temp = Copy(sheet, blockSrcX, blockSrcY, 32, 16)
            byte[] temp = new byte[PartWidth * PartHeight];
            BlitIndexed(sheet, sheetW, sheetH, blockSrcX, blockSrcY,
                PartWidth, PartHeight, temp, PartWidth, PartHeight, 0, 0);

            // BitBlt(temp, cropX, cropY, cropW, cropH, sheet, cropX + slotSrcX, cropY + slotSrcY)
            // i.e. overlay the crop rect from the slot's source area onto temp at
            // the same (cropX, cropY) offset.
            BlitIndexed(sheet, sheetW, sheetH, cropX + slotSrcX, cropY + slotSrcY,
                cropW, cropH, temp, PartWidth, PartHeight, cropX, cropY);

            // BitBlt(sheet, slotSrcX, slotSrcY, 32, 16, temp, 0, 0) — write back.
            BlitIndexed(temp, PartWidth, PartHeight, 0, 0,
                PartWidth, PartHeight, sheet, sheetW, sheetH, slotSrcX, slotSrcY);
        }

        /// <summary>
        /// Overlay a standard 32x16 slot from the sheet onto the 96x80 face at
        /// (<paramref name="dstX"/>, <paramref name="dstY"/>). Mirrors
        /// GenPreviewMainChar's per-frame BitBlt, which passes
        /// <c>transparent_index: 0</c> — so the slot's index-0 (transparent)
        /// pixels do NOT overwrite the underlying face; the base face shows
        /// through instead of leaving transparent holes (#979 review fix).
        /// </summary>
        static void OverlaySlot(byte[] sheet, int sheetW, int sheetH,
            int slotSrcX, int slotSrcY, byte[] face, int dstX, int dstY)
        {
            BlitIndexed(sheet, sheetW, sheetH, slotSrcX, slotSrcY,
                PartWidth, PartHeight, face, FaceWidth, FaceHeight, dstX, dstY,
                transparentIndex: 0);
        }

        /// <summary>
        /// Indexed-pixel block copy with full bounds clamping, matching WF
        /// <see cref="ImageUtil"/>.BitBlt. <paramref name="transparentIndex"/>
        /// mirrors WF's <c>transparent_index</c> argument:
        ///   - <c>0xFF</c> (default) = OPAQUE copy. No 0..15 index matches 0xFF,
        ///     so every source pixel (incl. index 0) is copied — used for the
        ///     base-face assembly and the STAGE-A slot rebuild (WF
        ///     <c>DecreaseColor16</c> uses the default).
        ///   - <c>0</c> = TRANSPARENT-on-0. Source pixels whose index == 0 are
        ///     SKIPPED, leaving the destination (the base face) intact — used
        ///     for the STAGE-B eye/mouth overlay so transparent slot borders
        ///     don't punch holes in the face (WF <c>GenPreviewMainChar</c>
        ///     overlays pass <c>transparent_index: 0</c>; #979 review fix).
        /// Negative or out-of-range source/destination regions are clamped.
        /// </summary>
        internal static void BlitIndexed(
            byte[] src, int srcW, int srcH, int srcX, int srcY,
            int w, int h, byte[] dst, int dstW, int dstH, int dstX, int dstY,
            int transparentIndex = 0xFF)
        {
            if (src == null || dst == null || w <= 0 || h <= 0) return;

            // Clamp Y (dest first, then src) like WF BitBlt.
            if (dstY < 0) { srcY += -dstY; h -= -dstY; dstY = 0; }
            if (srcY < 0) { h -= -srcY; srcY = 0; }
            if (srcY + h > srcH) h -= (srcY + h) - srcH;
            if (dstY + h > dstH) h -= (dstY + h) - dstH;

            // Clamp X.
            if (dstX < 0) { srcX += -dstX; w -= -dstX; dstX = 0; }
            if (srcX < 0) { w -= -srcX; srcX = 0; }
            if (srcX + w > srcW) w -= (srcX + w) - srcW;
            if (dstX + w > dstW) w -= (dstX + w) - dstW;

            if (w <= 0 || h <= 0) return;

            for (int y = 0; y < h; y++)
            {
                int sRow = (srcY + y) * srcW + srcX;
                int dRow = (dstY + y) * dstW + dstX;
                for (int x = 0; x < w; x++)
                {
                    int si = sRow + x;
                    int di = dRow + x;
                    if (si < 0 || si >= src.Length || di < 0 || di >= dst.Length) continue;
                    // Transparent-on-N: skip source pixels equal to the
                    // transparent index, leaving the destination pixel intact.
                    if (src[si] == transparentIndex) continue;
                    dst[di] = src[si];
                }
            }
        }

        /// <summary>
        /// RGBA sibling of the indexed STAGE-A (<see cref="RebuildSlot"/>):
        /// reconstruct the standard eye/mouth animation cells IN PLACE on a
        /// colour-keyed 128x112 RGBA sheet, BEFORE the import splits/remaps it
        /// (#1917). Each cell is reseeded from the destination face block region
        /// then overlaid with only the cropped feature, so the cell's original
        /// (possibly differently-coloured, e.g. white hackbox) background is
        /// discarded and replaced by the face background — which the import's
        /// colour key already marked transparent (alpha 0) → index 0 after remap
        /// → no in-game smear. Mirrors WinForms <c>DecreaseColor16</c> (which
        /// reconstructs before the ROM write); the Avalonia import previously
        /// skipped this, so the raw opaque cell backgrounds were blitted as solid
        /// rectangles over the face. A feature whose crop W/H &lt;= 0 is skipped
        /// (left untouched), so callers without crop info must gate this off.
        /// </summary>
        public static void ReconstructSheetCellsRgba(
            byte[] rgba, int width, int height,
            int eyeBlockX, int eyeBlockY, int mouthBlockX, int mouthBlockY,
            int eyeCropX, int eyeCropY, int eyeCropW, int eyeCropH,
            int mouthCropX, int mouthCropY, int mouthCropW, int mouthCropH,
            bool isFe6)
        {
            if (rgba == null || width <= 0 || height <= 0) return;
            if ((long)rgba.Length < (long)width * height * 4) return;

            if (!isFe6 && eyeCropW > 0 && eyeCropH > 0)
            {
                RebuildSlotRgba(rgba, width, height, eyeBlockX * 8, eyeBlockY * 8,
                    eyeCropX, eyeCropY, eyeCropW, eyeCropH, EyeHalfSrcX, EyeHalfSrcY);
                RebuildSlotRgba(rgba, width, height, eyeBlockX * 8, eyeBlockY * 8,
                    eyeCropX, eyeCropY, eyeCropW, eyeCropH, EyeClosedSrcX, EyeClosedSrcY);
            }
            if (mouthCropW > 0 && mouthCropH > 0)
            {
                for (int m = 0; m < MouthSlots.Length; m++)
                    RebuildSlotRgba(rgba, width, height, mouthBlockX * 8, mouthBlockY * 8,
                        mouthCropX, mouthCropY, mouthCropW, mouthCropH,
                        MouthSlots[m].x, MouthSlots[m].y);
            }
        }

        /// <summary>
        /// RGBA port of <see cref="RebuildSlot"/>: seed the 32x16 slot at
        /// (<paramref name="slotSrcX"/>, <paramref name="slotSrcY"/>) with the
        /// face block region, overlay the crop rect read from the slot's own
        /// source area, then write it back — all opaque 4-byte-per-pixel copies
        /// (matches WF <c>DecreaseColor16</c>'s default-opaque BitBlt).
        /// </summary>
        static void RebuildSlotRgba(byte[] rgba, int sheetW, int sheetH,
            int blockSrcX, int blockSrcY, int cropX, int cropY, int cropW, int cropH,
            int slotSrcX, int slotSrcY)
        {
            byte[] temp = new byte[PartWidth * PartHeight * 4];
            BlitRgba(rgba, sheetW, sheetH, blockSrcX, blockSrcY,
                PartWidth, PartHeight, temp, PartWidth, PartHeight, 0, 0);
            BlitRgba(rgba, sheetW, sheetH, cropX + slotSrcX, cropY + slotSrcY,
                cropW, cropH, temp, PartWidth, PartHeight, cropX, cropY);
            BlitRgba(temp, PartWidth, PartHeight, 0, 0,
                PartWidth, PartHeight, rgba, sheetW, sheetH, slotSrcX, slotSrcY);
        }

        /// <summary>
        /// Opaque RGBA (4 byte/pixel) block copy with WF-BitBlt-style region
        /// clamping. Unlike <see cref="BlitIndexed"/> there is no transparent
        /// index — every source pixel (incl. alpha 0) is copied, matching the
        /// reconstruction's opaque reseed+overlay semantics.
        /// </summary>
        internal static void BlitRgba(
            byte[] src, int srcW, int srcH, int srcX, int srcY,
            int w, int h, byte[] dst, int dstW, int dstH, int dstX, int dstY)
        {
            if (src == null || dst == null || w <= 0 || h <= 0) return;

            if (dstY < 0) { srcY += -dstY; h -= -dstY; dstY = 0; }
            if (srcY < 0) { h -= -srcY; srcY = 0; }
            if (srcY + h > srcH) h -= (srcY + h) - srcH;
            if (dstY + h > dstH) h -= (dstY + h) - dstH;

            if (dstX < 0) { srcX += -dstX; w -= -dstX; dstX = 0; }
            if (srcX < 0) { w -= -srcX; srcX = 0; }
            if (srcX + w > srcW) w -= (srcX + w) - srcW;
            if (dstX + w > dstW) w -= (dstX + w) - dstW;

            if (w <= 0 || h <= 0) return;

            for (int y = 0; y < h; y++)
            {
                int sRow = ((srcY + y) * srcW + srcX) * 4;
                int dRow = ((dstY + y) * dstW + dstX) * 4;
                for (int x = 0; x < w; x++)
                {
                    int si = sRow + x * 4;
                    int di = dRow + x * 4;
                    if (si < 0 || si + 3 >= src.Length || di < 0 || di + 3 >= dst.Length) continue;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        /// <summary>
        /// Decode an indexed (1 byte/pixel, 0..15) buffer to RGBA via a GBA
        /// palette. Palette index 0 is rendered fully transparent (matches the
        /// wizard's <c>ReconstructRgbaWithPaletteZeroTransparent</c> convention).
        /// </summary>
        internal static byte[] DecodeIndexedToRgba(byte[] indexed, int w, int h, byte[] palette, IImageService svc)
        {
            // Guard the allocation in LONG arithmetic: `w * h * 4` in `int` can
            // overflow for large dimensions. RenderFramePreview only ever calls
            // this with the 96x80 face constants, but the helper is internal and
            // hardened defensively (#979 review) — return an empty buffer on a
            // non-positive or oversized request instead of throwing/overflowing.
            if (w <= 0 || h <= 0) return Array.Empty<byte>();
            long pixels = (long)w * h;
            if (pixels > MaxPixelCount) return Array.Empty<byte>();
            byte[] rgba = new byte[(int)pixels * 4];
            int n = (int)Math.Min(indexed.Length, pixels);
            for (int i = 0; i < n; i++)
            {
                int palIdx = indexed[i] & 0x0F;
                int palOff = palIdx * 2;
                if (palOff + 2 > palette.Length) continue;
                ushort gba = (ushort)(palette[palOff] | (palette[palOff + 1] << 8));
                svc.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
                int o = i * 4;
                rgba[o + 0] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = (byte)(palIdx == 0 ? 0 : 255);
            }
            return rgba;
        }
    }
}
