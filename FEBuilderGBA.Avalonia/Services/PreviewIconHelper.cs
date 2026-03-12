using System;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Helper to render small preview icons for unit portraits, item icons, and class wait icons.
    /// Used by editor views to show a sidebar preview next to the address list.
    /// </summary>
    public static class PreviewIconHelper
    {
        /// <summary>
        /// Load a mini portrait (32x32) for the given portrait ID.
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadPortraitMini(uint portraitId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || portraitId == 0) return null;

            try
            {
                uint ptr = rom.RomInfo.portrait_pointer;
                if (ptr == 0) return null;

                uint portraitBase = rom.p32(ptr);
                if (!U.isSafetyOffset(portraitBase)) return null;

                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0) dataSize = 28;

                uint portraitAddr = portraitBase + portraitId * dataSize;
                if (portraitAddr + dataSize > (uint)rom.Data.Length) return null;

                uint imgPtr = rom.u32(portraitAddr + 4);  // offset 4 = map/mini face
                uint palPtr = rom.u32(portraitAddr + 8);

                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = imgPtr - 0x08000000;
                uint palAddr = palPtr - 0x08000000;

                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                return ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, true);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a 16x16 item icon for the given icon index (B29 field).
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadItemIcon(uint iconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint ptr = rom.RomInfo.icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Load palette
                uint palPtr = rom.RomInfo.icon_palette_pointer;
                if (palPtr == 0) return null;

                uint palGba = rom.u32(palPtr);
                if (!U.isPointer(palGba)) return null;

                byte[] palette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
                if (palette == null) return null;

                // Each icon is 128 bytes (2x2 tiles at 4bpp = 16x16 pixels)
                uint iconAddr = baseAddr + iconIndex * 128;
                if (iconAddr + 128 > (uint)rom.Data.Length) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a class wait icon (map sprite) for the given wait icon index (B6 field).
        /// The wait icon table stores compressed 16x16 (2x2 tile) sprites per entry.
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadClassWaitIcon(uint waitIconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || waitIconIndex == 0) return null;

            try
            {
                uint ptr = rom.RomInfo.unit_wait_icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Each entry in the unit wait icon table is a pointer to data
                // The table format: each entry is 4 bytes (GBA pointer to sprite data)
                // Plus a palette pointer
                uint entryAddr = baseAddr + waitIconIndex * 4;
                if (entryAddr + 4 > (uint)rom.Data.Length) return null;

                uint spriteGba = rom.u32(entryAddr);
                if (!U.isPointer(spriteGba)) return null;

                uint spriteAddr = U.toOffset(spriteGba);
                if (!U.isSafetyOffset(spriteAddr)) return null;

                // Palette: use the unit icon palette address
                uint palAddr = rom.RomInfo.unit_icon_palette_address;
                if (palAddr == 0 || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Sprites are LZ77-compressed 4bpp tile data
                return ImageUtilCore.LoadROMTiles4bpp(spriteAddr, palette, 4, 4, true);
            }
            catch
            {
                return null;
            }
        }
    }
}
