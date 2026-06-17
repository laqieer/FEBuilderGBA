// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Unit Move Icon render seam (#1177).
//
// Sibling of WaitIconRenderCore (#991): the per-class moving/walk icon sprite
// sheet. This is the single source of truth for decoding + cropping unit
// move-icon map sprites — a faithful port of WinForms
// ImageUnitMoveIconFrom.DrawMoveUnitIcon / LoadMoveUnitIcon. The Avalonia
// ImageUnitMoveIconView reuses it for its full-sheet / per-frame previews +
// PNG/GIF export, and PreviewIconHelper.LoadMoveIcon / ListIconLoaders
// .MoveIconLoader delegate to it for list thumbnails (single source of truth,
// no duplicate decode).
//
// Move-icon table entry layout (8 bytes), mirrors WF
// ImageUnitMoveIconFrom.Init / DrawMoveUnitIconBitmap:
//   +0: sprite pointer (GBA pointer to LZ77-compressed 4bpp sheet)  <-- P0
//   +4: AP (Animated Parts) pointer                                  <-- P4
// NOTE: the image is at +0 here, unlike the WAIT icon's +4.
//
// The sheet is a vertical stack of 32x32 (4*8 x 4*8) walk-step frames. WF
// LoadMoveUnitIcon decodes the whole strip at 32 wide; DrawMoveUnitIcon(...,
// step) crops Rectangle(0, 32*step, 32, 32).
//
// Palette by TYPE (NO lightrune/sepia — only 0..4, unlike the wait icon):
//   0 = self  (unit_icon_palette_address)
//   1 = npc   (unit_icon_npc_palette_address)
//   2 = enemy (unit_icon_enemey_palette_address)
//   3 = gray  (unit_icon_gray_palette_address)
//   4 = four  (unit_icon_four_palette_address)
using FEBuilderGBA; // ROM, U, LZ77, ImageUtilCore, IImage, IImageService live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform decoder + cropper for Unit Move Icon map sprites (#1177).
    /// All methods are READ-ONLY and never throw — bad pointers / short strips /
    /// missing palettes return a blank/null image (WF blank-dummy contract).
    /// </summary>
    public static class UnitMoveIconRenderCore
    {
        /// <summary>Each walk-step frame is 4*8 = 32 pixels square (WF).</summary>
        public const int FRAME = 4 * 8;

        /// <summary>
        /// Resolve the 16-color palette bytes for the given palette TYPE.
        /// Mirrors WF <c>ImageUnitMoveIconFrom.LoadMoveUnitIcon</c>'s palette
        /// selection:
        ///   0 = self  (unit_icon_palette_address)
        ///   1 = npc   (unit_icon_npc_palette_address)
        ///   2 = enemy (unit_icon_enemey_palette_address)
        ///   3 = gray  (unit_icon_gray_palette_address)
        ///   4 = four  (unit_icon_four_palette_address)
        /// Returns null when the ROM is null, the resolved address is 0/unsafe,
        /// or decode fails.
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
                default: palAddr = rom.RomInfo.unit_icon_palette_address; break;
            }
            if (palAddr == 0 || !U.isSafetyOffset(palAddr, rom)) return null;
            // rom-consistent read (mirrors #993): use the explicit-rom
            // GetPalette overload so the palette bytes come from THIS rom, not a
            // different ambient CoreState.ROM.
            return ImageUtilCore.GetPalette(rom, palAddr, 16);
        }

        /// <summary>
        /// Decode the full LZ77 sprite sheet for a move-icon entry (no crop) and
        /// render it as an indexed <see cref="IImage"/> (32 wide). Returns null
        /// on any ROM/service failure (unsafe pointer, empty strip, null
        /// palette).
        /// </summary>
        public static IImage RenderFullSheet(ROM rom, uint moveIconIndex, IImageService svc, int paletteType = 0)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            try
            {
                if (!TryResolveEntry(rom, moveIconIndex, out uint picAddr))
                    return null;
                return RenderFullSheetAt(rom, picAddr, svc, paletteType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Decode the sheet and crop the <paramref name="step"/>'th walk frame —
        /// WF <c>DrawMoveUnitIcon(..., step)</c> = <c>Rectangle(0, 32*step, 32,
        /// 32)</c>. Bounds-checked against the actual strip (short strip /
        /// unsafe ptr / null palette → null, never throws).
        /// </summary>
        public static IImage RenderFrame(ROM rom, uint moveIconIndex, int step, IImageService svc, int paletteType = 0)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            if (step < 0) return null;
            try
            {
                if (!TryResolveEntry(rom, moveIconIndex, out uint picAddr))
                    return null;
                return RenderFrameAt(rom, picAddr, step, svc, paletteType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// picAddress-based full-sheet render — matches the WF
        /// <c>DrawMoveUnitIcon(pic_address, palette_type)</c> /
        /// <c>LoadMoveUnitIcon</c> signature for direct-address callers (the
        /// list-thumbnail / editor paths resolve the pointer separately).
        /// <paramref name="picAddress"/> is a GBA pointer (0x08-based).
        /// </summary>
        public static IImage RenderMoveIcon(ROM rom, uint picAddress, int paletteType, IImageService svc)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            try
            {
                uint picOff = U.toOffset(picAddress);
                if (!U.isSafetyOffset(picOff, rom)) return null;
                return RenderFullSheetAt(rom, picOff, svc, paletteType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// picAddress-based single-frame render — WF
        /// <c>DrawMoveUnitIcon(pic_address, palette_type, step)</c>.
        /// <paramref name="picAddress"/> is a GBA pointer (0x08-based).
        /// </summary>
        public static IImage RenderMoveIcon(ROM rom, uint picAddress, int paletteType, int step, IImageService svc)
        {
            if (rom == null || rom.RomInfo == null || svc == null) return null;
            if (step < 0) return null;
            try
            {
                uint picOff = U.toOffset(picAddress);
                if (!U.isSafetyOffset(picOff, rom)) return null;
                return RenderFrameAt(rom, picOff, step, svc, paletteType);
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------
        // Internal decode at a resolved ROM OFFSET (picOff).
        // -----------------------------------------------------------------

        static IImage RenderFullSheetAt(ROM rom, uint picOff, IImageService svc, int paletteType)
        {
            byte[] palette = GetPaletteColors(rom, paletteType);
            if (palette == null) return null;

            byte[] strip = LZ77.decompress(rom.Data, picOff);
            if (strip == null || strip.Length == 0) return null;

            int height = CalcStripHeight(FRAME, strip.Length);
            if (height <= 0) return null;

            return svc.Decode4bppTiles(strip, 0, FRAME, height, palette);
        }

        static IImage RenderFrameAt(ROM rom, uint picOff, int step, IImageService svc, int paletteType)
        {
            byte[] palette = GetPaletteColors(rom, paletteType);
            if (palette == null) return null;

            byte[] strip = LZ77.decompress(rom.Data, picOff);
            if (strip == null || strip.Length == 0) return null;

            int stripHeight = CalcStripHeight(FRAME, strip.Length);
            if (stripHeight <= 0) return null;

            // WF DrawMoveUnitIcon: Rectangle(0, FRAME*step, FRAME, FRAME).
            int cropY = FRAME * step;
            if (cropY < 0) return null;
            if (cropY + FRAME > stripHeight) return null;

            using IImage full = svc.Decode4bppTiles(strip, 0, FRAME, stripHeight, palette);
            if (full == null) return null;

            return CropIndexedRegion(svc, full, 0, cropY, FRAME, FRAME);
        }

        /// <summary>
        /// Resolve the 8-byte move-icon entry's sprite ROM OFFSET (from the GBA
        /// pointer @ +0). Returns false (caller → blank) on a zero/unsafe table
        /// pointer, out-of-bounds entry, or a non-pointer / unsafe sprite slot.
        /// </summary>
        static bool TryResolveEntry(ROM rom, uint moveIconIndex, out uint spriteAddr)
        {
            spriteAddr = 0;

            uint ptr = rom.RomInfo.unit_move_icon_pointer;
            if (ptr == 0) return false;

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return false;

            // Overflow-safe address arithmetic (#993 lesson): a very large
            // moveIconIndex would wrap `baseAddr + index*8` in uint and could
            // alias onto the WRONG entry. Do the multiply + add + bounds check
            // in ulong and only cast back once the full 8-byte span is proven
            // in-bounds.
            ulong entryAddr64 = (ulong)baseAddr + (ulong)moveIconIndex * 8UL;
            if (entryAddr64 + 8UL > (ulong)rom.Data.Length) return false;
            uint entryAddr = (uint)entryAddr64;

            uint spriteGba = rom.u32(entryAddr + 0);
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
