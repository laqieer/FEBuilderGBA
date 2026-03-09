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

        uint _baseAddr;
        byte[] _cachedPalette;

        public List<AddrResult> LoadItemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Use icon_pointer (raw uncompressed icon data, 128 bytes per icon)
            uint ptr = rom.RomInfo.icon_pointer;
            if (ptr == 0) return new List<AddrResult>();

            _baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(_baseAddr)) return new List<AddrResult>();

            // Load palette from icon_palette_pointer
            uint palPtr = rom.RomInfo.icon_palette_pointer;
            if (palPtr != 0)
            {
                uint palGba = rom.u32(palPtr);
                if (U.isPointer(palGba))
                    _cachedPalette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
            }

            // Count icons: 128 bytes each, stop at ROM end
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(_baseAddr + i * 128);
                if (addr + 128 > (uint)rom.Data.Length) break;

                string name = U.ToHexString(i) + " Item Icon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemIcon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.RomInfo.icon_pointer;
            PalettePointer = rom.RomInfo.icon_palette_pointer;
            CanWrite = true;
        }

        /// <summary>
        /// Try to load a single 16x16 item icon from ROM at the selected address.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _cachedPalette == null) return null;
            if (CoreState.ImageService == null) return null;
            try
            {
                uint addr = CurrentAddr;
                if (!U.isSafetyOffset(addr) || addr + 128 > (uint)rom.Data.Length) return null;

                // Read 128 bytes (2x2 tiles at 4bpp) directly from ROM
                return CoreState.ImageService.Decode4bppTiles(rom.Data, (int)addr, 16, 16, _cachedPalette);
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
                ["ImagePointer"] = $"0x{rom.RomInfo.icon_pointer:X08}",
                ["PalettePointer"] = $"0x{rom.RomInfo.icon_palette_pointer:X08}",
            };
        }
    }
}
