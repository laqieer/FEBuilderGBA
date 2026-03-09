using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SystemIconViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
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
            CanWrite = true;
        }

        /// <summary>
        /// Try to load the full system icon sheet as an IImage.
        /// System icons are stored as one compressed 4bpp block.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
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

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                int totalIcons = tileData.Length / 128;
                if (totalIcons <= 0) return null;

                int iconsPerRow = 8;
                int rows = (totalIcons + iconsPerRow - 1) / iconsPerRow;
                int width = iconsPerRow * 16;
                int height = rows * 16;

                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadSystemIconList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["system_icon_pointer"] = $"0x{ImagePointer:X08}",
                ["system_icon_palette_pointer"] = $"0x{PalettePointer:X08}",
            };
        }
    }
}
