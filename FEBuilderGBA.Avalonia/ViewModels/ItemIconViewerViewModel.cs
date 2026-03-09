using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemIconViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadItemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint imgPtr = rom.RomInfo.system_weapon_icon_pointer;
            if (imgPtr == 0) return new List<AddrResult>();

            uint imgAddr = rom.p32(imgPtr);
            if (!U.isSafetyOffset(imgAddr)) return new List<AddrResult>();

            // Weapon icons are a single compressed block; create virtual entries
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x40; i++)
            {
                string name = U.ToHexString(i) + " Weapon Icon";
                result.Add(new AddrResult(imgAddr + i * 4, name, i));
            }
            return result;
        }

        public void LoadItemIcon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.RomInfo.system_weapon_icon_pointer;
            PalettePointer = rom.RomInfo.system_weapon_icon_palette_pointer;
            CanWrite = true;
        }

        /// <summary>
        /// Try to load the weapon icon sheet as an IImage.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            try
            {
                uint imgPtr = rom.RomInfo.system_weapon_icon_pointer;
                uint palPtr = rom.RomInfo.system_weapon_icon_palette_pointer;
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

        public int GetListCount() => LoadItemIconList().Count;

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
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["ImagePointer"] = $"0x{rom.RomInfo.system_weapon_icon_pointer:X08}",
                ["PalettePointer"] = $"0x{rom.RomInfo.system_weapon_icon_palette_pointer:X08}",
            };
        }
    }
}
