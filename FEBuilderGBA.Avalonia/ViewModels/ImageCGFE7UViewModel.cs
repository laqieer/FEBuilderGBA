using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageCGFE7UViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        uint _imageType, _reserved1, _reserved2, _reserved3;
        uint _splitImagePtr, _tsaPtr, _palettePtr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // B0: Image type (0=Single, 1=10-Split)
        public uint ImageType { get => _imageType; set => SetField(ref _imageType, value); }
        // B1: Reserved
        public uint Reserved1 { get => _reserved1; set => SetField(ref _reserved1, value); }
        // B2: Reserved
        public uint Reserved2 { get => _reserved2; set => SetField(ref _reserved2, value); }
        // B3: Reserved
        public uint Reserved3 { get => _reserved3; set => SetField(ref _reserved3, value); }
        // P4: 10-Split image data pointer
        public uint SplitImagePtr { get => _splitImagePtr; set => SetField(ref _splitImagePtr, value); }
        // P8: TSA (Tile Screen Arrangement) pointer
        public uint TSAPtr { get => _tsaPtr; set => SetField(ref _tsaPtr, value); }
        // P12: Palette data pointer
        public uint PalettePtr { get => _palettePtr; set => SetField(ref _palettePtr, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "CG Editor (FE7U)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            ImageType = rom.u8(addr + 0);
            Reserved1 = rom.u8(addr + 1);
            Reserved2 = rom.u8(addr + 2);
            Reserved3 = rom.u8(addr + 3);
            SplitImagePtr = rom.u32(addr + 4);
            TSAPtr = rom.u32(addr + 8);
            PalettePtr = rom.u32(addr + 12);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, ImageType);
            rom.write_u8(addr + 1, Reserved1);
            rom.write_u8(addr + 2, Reserved2);
            rom.write_u8(addr + 3, Reserved3);
            rom.write_u32(addr + 4, SplitImagePtr);
            rom.write_u32(addr + 8, TSAPtr);
            rom.write_u32(addr + 12, PalettePtr);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImageType"] = $"0x{ImageType:X02}",
                ["Reserved1"] = $"0x{Reserved1:X02}",
                ["Reserved2"] = $"0x{Reserved2:X02}",
                ["Reserved3"] = $"0x{Reserved3:X02}",
                ["SplitImagePtr"] = $"0x{SplitImagePtr:X08}",
                ["TSAPtr"] = $"0x{TSAPtr:X08}",
                ["PalettePtr"] = $"0x{PalettePtr:X08}",
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
                ["u8@0_ImageType"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1_Reserved1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@2_Reserved2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@3_Reserved3"] = $"0x{rom.u8(a + 3):X02}",
                ["u32@4_SplitImagePtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8_TSAPtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@12_PalettePtr"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
