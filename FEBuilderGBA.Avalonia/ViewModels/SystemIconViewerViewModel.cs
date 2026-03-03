using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SystemIconViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _imagePointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadSystemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint imgPtr = rom.RomInfo.system_icon_pointer;
            if (imgPtr == 0) return new List<AddrResult>();

            uint imgAddr = rom.p32(imgPtr);
            if (!U.isSafetyOffset(imgAddr)) return new List<AddrResult>();

            // System icons are stored as a single compressed block.
            // We create virtual entries for each 16x16 icon.
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x80; i++)
            {
                string name = U.ToHexString(i) + " System Icon";
                result.Add(new AddrResult(imgAddr + i * 4, name, i));
            }
            return result;
        }

        public void LoadSystemIcon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.RomInfo.system_icon_pointer;
            PalettePointer = rom.RomInfo.system_icon_palette_pointer;
            IsLoaded = true;
        }

        /// <summary>
        /// Try to load the full system icon sheet as RGBA pixels.
        /// System icons are stored as one compressed 4bpp block.
        /// Returns null on failure.
        /// </summary>
        public byte[] TryLoadImage(uint selectedIndex, out int width, out int height)
        {
            width = 0; height = 0;
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            try
            {
                uint imgPtr = rom.RomInfo.system_icon_pointer;
                uint palPtr = rom.RomInfo.system_icon_palette_pointer;
                if (imgPtr == 0 || palPtr == 0) return null;

                uint imgAddr = rom.p32(imgPtr);
                uint palAddr = rom.p32(palPtr);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Decompress and render a portion: 2x2 tiles (16x16) per icon
                // Render entire icon sheet as wide strip
                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Each icon is 2x2 tiles = 4 tiles = 128 bytes at 4bpp
                int totalIcons = tileData.Length / 128;
                if (totalIcons <= 0) return null;

                // Render as grid: 8 icons per row
                int iconsPerRow = 8;
                int rows = (totalIcons + iconsPerRow - 1) / iconsPerRow;
                width = iconsPerRow * 16;
                height = rows * 16;

                if (CoreState.ImageService == null) return null;
                var image = CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
                if (image == null) return null;
                return image.GetPixelData();
            }
            catch { return null; }
        }
    }
}
