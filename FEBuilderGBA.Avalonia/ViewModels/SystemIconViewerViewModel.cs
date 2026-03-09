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

        IImage _cachedSheet;

        public List<AddrResult> LoadSystemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint imgPtr = rom.RomInfo.system_icon_pointer;
            if (imgPtr == 0) return new List<AddrResult>();

            uint imgAddr = rom.p32(imgPtr);
            if (!U.isSafetyOffset(imgAddr)) return new List<AddrResult>();

            byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
            if (tileData == null || tileData.Length == 0) return new List<AddrResult>();

            uint palPtr = rom.RomInfo.system_icon_palette_pointer;
            if (palPtr == 0) return new List<AddrResult>();
            uint palAddr = rom.p32(palPtr);
            if (!U.isSafetyOffset(palAddr)) return new List<AddrResult>();

            byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
            if (palette == null) return new List<AddrResult>();

            // Render full sheet: 16 tiles wide (128px), height from data
            int totalTiles = tileData.Length / 32;
            int tilesX = 16;
            int tilesY = (totalTiles + tilesX - 1) / tilesX;
            if (tilesY <= 0) tilesY = 1;

            if (CoreState.ImageService != null)
                _cachedSheet = CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);

            // Create entries for icons (16x16 = 2x2 tiles each, 8 icons per row)
            int iconsPerRow = tilesX / 2;
            int totalIcons = (totalTiles / 2) / 2; // approximate
            var result = new List<AddrResult>();
            for (uint i = 0; i < (uint)totalIcons; i++)
            {
                string name = U.ToHexString(i) + " System Icon";
                result.Add(new AddrResult(i, name, i));
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
        /// Display the full system icon sheet.
        /// Individual icon extraction requires 2D tile cropping not yet implemented.
        /// </summary>
        public IImage TryLoadImage()
        {
            return _cachedSheet;
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
