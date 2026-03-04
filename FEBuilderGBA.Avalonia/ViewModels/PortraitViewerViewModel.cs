using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PortraitViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        uint _portraitIndex;
        uint _imagePointer, _mapPointer, _palettePointer;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint PortraitIndex { get => _portraitIndex; set => SetField(ref _portraitIndex, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint MapPointer { get => _mapPointer; set => SetField(ref _mapPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadPortraitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 28;

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (uint i = 0; i < 0x400; i++) // generous max
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Check validity: at least one of the first 3 pointers should be valid
                uint u0 = rom.u32(addr + 0);
                uint u4 = rom.u32(addr + 4);
                uint u8 = rom.u32(addr + 8);

                if (i > 0)
                {
                    if (!U.isPointerOrNULL(u0) || !U.isPointerOrNULL(u4) || !U.isPointerOrNULL(u8))
                        break;
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {
                        nullCount++;
                        if (nullCount >= 100) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadPortrait(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 28;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ImagePointer = rom.u32(addr + 0);
            MapPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);
            Name = $"Portrait at 0x{addr:X08}";
            IsLoaded = true;
        }

        /// <summary>
        /// Try to load portrait image data as RGBA pixels using Core image utils.
        /// Returns null if the portrait cannot be decoded.
        /// </summary>
        public byte[] TryLoadPortraitImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;

            try
            {
                uint imgPtr = rom.u32(CurrentAddr + 0);
                uint palPtr = rom.u32(CurrentAddr + 8);

                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = imgPtr - 0x08000000;
                uint palAddr = palPtr - 0x08000000;

                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                // Load palette (16 colors * 2 bytes = 32 bytes)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Load compressed image tiles (typically 4bpp, 32x32 = 4 tiles x 4 tiles)
                var image = ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, true);
                if (image == null) return null;

                return image.GetPixelData();
            }
            catch
            {
                return null;
            }
        }

        public int GetListCount() => LoadPortraitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["MapPointer"] = $"0x{MapPointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
