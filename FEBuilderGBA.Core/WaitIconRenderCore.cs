// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Unit Wait Icon render seam (#991).
//
// This is the single source of truth for decoding + cropping unit wait-icon
// map sprites. It is a VERBATIM extraction of the decode pipeline that used to
// live inside the Avalonia-side PreviewIconHelper.LoadClassWaitIcon (strip
// LZ77-decompress + CalcStripHeight + per-animType crop + CropIndexedRegion).
// PreviewIconHelper.LoadClassWaitIcon now delegates to RenderClassWaitIcon so
// the WinForms-parity list-preview behavior (#342/#667: step-0 16x24@Y=8) is
// preserved with zero drift, and the Avalonia ImageUnitWaitIconView reuses the
// same seam for its full-sheet / per-frame previews + PNG/GIF export.
//
// Wait-icon table entry layout (8 bytes), mirrors WF
// ImageUnitWaitIconFrom.DrawWaitUnitIcon / LoadWaitUnitIcon:
//   +0: flags (4 bytes); byte at +2 = animation type (b2)
//   +4: sprite pointer (GBA pointer to LZ77-compressed 4bpp tile strip)
// animType 0 -> 16-wide strip, 16x16 per frame
// animType 1 -> 16-wide strip, 16x24 per frame, first frame begins at Y=8
// animType 2 -> 32-wide strip, 32x32 per frame
using FEBuilderGBA; // ROM, U, LZ77, ImageUtilCore, IImage, IImageService live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform decoder + cropper for Unit Wait Icon map sprites (#991).
    /// All methods are READ-ONLY and never throw — bad pointers / short strips /
    /// missing palettes return a blank/null image (WF blank-dummy contract).
    /// </summary>
    public static class WaitIconRenderCore
    {
        /// <summary>
        /// Resolve the 16-color palette bytes for the given palette TYPE.
        /// Mirrors WF <c>ImageUnitWaitIconFrom.LoadWaitUnitIcon</c>'s palette
        /// selection:
        ///   0 = self  (unit_icon_palette_address)
        ///   1 = npc   (unit_icon_npc_palette_address)
        ///   2 = enemy (unit_icon_enemey_palette_address)
        ///   3 = gray  (unit_icon_gray_palette_address)
        ///   4 = four  (unit_icon_four_palette_address)
        ///   5 = lightrune (unit_icon_lightrune_palette_address)
        ///   6 = sepia (unit_icon_sepia_palette_address)
        /// Returns null when the ROM is null, the resolved address is 0/unsafe
        /// (FE6 has no lightrune/sepia → 0 → blank), or decode fails.
        /// </summary>
        public static byte[] GetPaletteColors(ROM rom, int paletteType)
        {
            if (rom == null || rom.RomInfo == null) return null;
            uint palAddr;
            switch (paletteType)
            {
                case 1: palAddr = rom.RomInfo.unit_icon_npc_palette_address; break;
                case 2: palAddr = rom.RomInfo.unit_icon_enemey_palette_address; break;
                case 3: palAddr = rom.RomInfo.unit_icon_gray_palette_address; break;
                case 4: palAddr = rom.RomInfo.unit_icon_four_palette_address; break;
                case 5: palAddr = rom.RomInfo.unit_icon_lightrune_palette_address; break;
                case 6: palAddr = rom.RomInfo.unit_icon_sepia_palette_address; break;
                default: palAddr = rom.RomInfo.unit_icon_palette_address; break;
            }
            if (palAddr == 0 || !U.isSafetyOffset(palAddr, rom)) return null;
            // rom-consistent read (#993 Copilot review): use the explicit-rom
            // GetPalette overload so the palette bytes come from THIS rom, not a
            // different ambient CoreState.ROM. The safety guard above already
            // validated palAddr against `rom`.
            return ImageUtilCore.GetPalette(rom, palAddr, 16);
        }

        /// <summary>
        /// Decode the full LZ77 sprite strip for a wait-icon entry and render it
        /// as an indexed <see cref="IImage"/> (no crop). Returns null on any
        /// ROM/service failure (unsafe pointer, empty strip, null palette).
        /// </summary>
        public static IImage RenderFullSheet(ROM rom, uint waitIconIndex, IImageService svc, int paletteType = 0)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            try
            {
                if (!TryResolveEntry(rom, waitIconIndex, out byte animType, out uint spriteAddr))
                    return null;

                byte[] palette = GetPaletteColors(rom, paletteType);
                if (palette == null) return null;

                int stripWidth = (animType == 2) ? 32 : 16;

                byte[] stripData = LZ77.decompress(rom.Data, spriteAddr);
                if (stripData == null || stripData.Length == 0) return null;

                int stripHeight = CalcStripHeight(stripWidth, stripData.Length);
                if (stripHeight <= 0) return null;

                return svc.Decode4bppTiles(stripData, 0, stripWidth, stripHeight, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Decode the strip and crop the <paramref name="step"/>'th frame per WF
        /// <c>DrawWaitUnitIcon</c> (height16_limit=false):
        ///   animType 0: Rectangle(0, 16*step,       16, 16)
        ///   animType 1: Rectangle(0, 32*step + 8,   16, 24)  (Y offset = 8)
        ///   animType 2: Rectangle(0, 32*step,       32, 32)
        /// Bounds-checked against the actual strip (short strip / unsafe ptr /
        /// null palette → null, never throws). animType is read as the raw byte
        /// at <c>entry + 2</c> (NOT the u16 W2 field).
        /// </summary>
        public static IImage RenderFrame(ROM rom, uint waitIconIndex, int step, IImageService svc, int paletteType = 0)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            if (step < 0) return null;
            try
            {
                if (!TryResolveEntry(rom, waitIconIndex, out byte animType, out uint spriteAddr))
                    return null;

                byte[] palette = GetPaletteColors(rom, paletteType);
                if (palette == null) return null;

                // Per-animType strip width + per-frame crop rectangle. Matches WF
                // LoadWaitUnitIcon (width=32 for animType 2, else 16) and
                // DrawWaitUnitIcon's full-size (height16_limit=false) crop:
                //   b2==1 frame height = ((2*8)+16)*step + 8  =>  32*step + 8, 16x24
                //   b2==2 frame height = 32*step               =>  32*step, 32x32
                //   else   frame height = (2*8)*step           =>  16*step, 16x16
                int stripWidth;
                int cropX, cropY, cropW, cropH;
                if (animType == 2)
                {
                    stripWidth = 32;
                    cropX = 0; cropY = 32 * step; cropW = 32; cropH = 32;
                }
                else if (animType == 1)
                {
                    stripWidth = 16;
                    cropX = 0; cropY = 32 * step + 8; cropW = 16; cropH = 24;
                }
                else
                {
                    stripWidth = 16;
                    cropX = 0; cropY = 16 * step; cropW = 16; cropH = 16;
                }

                byte[] stripData = LZ77.decompress(rom.Data, spriteAddr);
                if (stripData == null || stripData.Length == 0) return null;

                int stripHeight = CalcStripHeight(stripWidth, stripData.Length);
                if (stripHeight <= 0) return null;

                if (cropX + cropW > stripWidth) return null;
                if (cropY + cropH > stripHeight) return null;

                using IImage strip = svc.Decode4bppTiles(stripData, 0, stripWidth, stripHeight, palette);
                if (strip == null) return null;

                return CropIndexedRegion(svc, strip, cropX, cropY, cropW, cropH);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Render the class wait-icon preview (step 0, self palette). Identical
        /// to <c>RenderFrame(rom, waitIconIndex, step:0, svc, paletteType:0)</c>;
        /// preserves the #342/#667 step-0 16x24@Y=8 parity that the WinForms list
        /// preview relies on. <see cref="FEBuilderGBA.Avalonia.Services.PreviewIconHelper"/>
        /// (or its WinForms equivalent) delegates here.
        /// </summary>
        public static IImage RenderClassWaitIcon(ROM rom, uint waitIconIndex, IImageService svc, int paletteType = 0)
        {
            return RenderFrame(rom, waitIconIndex, 0, svc, paletteType);
        }

        /// <summary>
        /// Resolve the 8-byte wait-icon entry: animType (byte @ +2) and the
        /// sprite ROM OFFSET (from the GBA pointer @ +4). Returns false (caller
        /// → blank) on a zero/unsafe table pointer, out-of-bounds entry, or a
        /// non-pointer / unsafe sprite slot.
        /// </summary>
        static bool TryResolveEntry(ROM rom, uint waitIconIndex, out byte animType, out uint spriteAddr)
        {
            animType = 0;
            spriteAddr = 0;

            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return false;

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return false;

            uint entryAddr = baseAddr + waitIconIndex * 8;
            if (entryAddr + 8 > (uint)rom.Data.Length) return false;

            animType = (byte)rom.u8(entryAddr + 2);

            uint spriteGba = rom.u32(entryAddr + 4);
            if (!U.isPointer(spriteGba)) return false;

            uint addr = U.toOffset(spriteGba);
            if (!U.isSafetyOffset(addr, rom)) return false;

            spriteAddr = addr;
            return true;
        }

        /// <summary>
        /// Compute the rendered strip height from an LZ77-decompressed 4bpp byte
        /// length. Mirrors WF <c>ImageUtil.CalcHeight(width, image_size,
        /// align=8)</c>: ceil(image_size / (width/2)) aligned UP to a multiple of
        /// 8 rows.
        /// </summary>
        static int CalcStripHeight(int width, int imageSize)
        {
            if (width <= 0 || imageSize <= 0) return 0;
            int half = width / 2;          // 4bpp = 2 pixels/byte
            if (half <= 0) return 0;
            int height = imageSize / half;
            if (imageSize % half != 0) height++;
            const int align = 8;
            int remainder = height % align;
            if (remainder != 0) height += (align - remainder);
            return height;
        }

        /// <summary>
        /// Crop a sub-rectangle out of an indexed strip image, returning a fresh
        /// indexed <see cref="IImage"/> sharing the strip's palette. Returns null
        /// on any geometry / service failure.
        /// </summary>
        static IImage CropIndexedRegion(IImageService svc, IImage src, int x, int y, int w, int h)
        {
            if (svc == null || src == null) return null;
            if (!src.IsIndexed) return null;

            byte[] srcIdx = src.GetPixelData();
            if (srcIdx == null) return null;
            int srcW = src.Width;
            int srcH = src.Height;
            if (x < 0 || y < 0 || w <= 0 || h <= 0) return null;
            if (x + w > srcW || y + h > srcH) return null;
            if ((long)srcW * srcH > srcIdx.Length) return null;

            byte[] dstIdx = new byte[w * h];
            for (int row = 0; row < h; row++)
            {
                int srcOff = (y + row) * srcW + x;
                int dstOff = row * w;
                System.Buffer.BlockCopy(srcIdx, srcOff, dstIdx, dstOff, w);
            }

            IImage outImg = svc.CreateIndexedImage(w, h, src.GetPaletteGBA(), 16);
            if (outImg == null) return null;
            outImg.SetPixelData(dstIdx);
            return outImg;
        }
    }
}
