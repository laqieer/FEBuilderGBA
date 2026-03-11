using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageViewerViewModel : ViewModelBase
    {
        string _title = "Image Viewer";
        int _zoom = 4;
        bool _isLoaded;
        int _imageWidth;
        int _imageHeight;
        string _imageInfo = string.Empty;

        /// <summary>Window title.</summary>
        public string Title { get => _title; set => SetField(ref _title, value); }
        /// <summary>Zoom level for the image display (1x, 2x, 4x, etc.).</summary>
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Width of the loaded image in pixels.</summary>
        public int ImageWidth { get => _imageWidth; set => SetField(ref _imageWidth, value); }
        /// <summary>Height of the loaded image in pixels.</summary>
        public int ImageHeight { get => _imageHeight; set => SetField(ref _imageHeight, value); }
        /// <summary>Descriptive info about the image (size, format, etc.).</summary>
        public string ImageInfo { get => _imageInfo; set => SetField(ref _imageInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
            Title = "Item Icon Viewer";
        }

        /// <summary>
        /// Load item icon list from ROM.
        /// Each icon is 16x16 (2x2 tiles) = 128 bytes of raw 4bpp data.
        /// </summary>
        public List<AddrResult> LoadIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint iconPtr = rom.RomInfo.icon_pointer;
            if (iconPtr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(iconPtr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Each icon: 2x2 tiles of 8x8 at 4bpp = 2*2*32 = 128 bytes
            uint blockSize = 128;
            uint maxCount = rom.RomInfo.icon_orignal_max;
            if (maxCount == 0) maxCount = 0xFF;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                string name = U.ToHexString(i) + " Icon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Draw the item icon at the given ROM address.
        /// </summary>
        public IImage DrawIcon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null) return null;
            if (!U.isSafetyOffset(addr)) return null;

            // Get icon palette
            uint palPtr = rom.u32(rom.RomInfo.icon_palette_pointer);
            if (!U.isPointer(palPtr)) return null;
            uint palOffset = U.toOffset(palPtr);

            byte[] palette = ImageUtilCore.GetPalette(palOffset, 16);
            if (palette == null) return null;

            // Icon is 2x2 tiles (16x16 pixels), raw 4bpp
            return ImageUtilCore.LoadROMTiles4bpp(addr, palette, 2, 2, false);
        }

        /// <summary>Get the GBA palette bytes for export.</summary>
        public byte[] GetPaletteBytes()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint palPtr = rom.u32(rom.RomInfo.icon_palette_pointer);
            if (!U.isPointer(palPtr)) return null;
            return ImageUtilCore.GetPalette(U.toOffset(palPtr), 16);
        }
    }
}
