using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageUnitPaletteViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        uint _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9, _b10, _b11;
        uint _p12;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // B0
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        // B1
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        // B2
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        // B3
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        // B4
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        // B5
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        // B6
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        // B7
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        // B8
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        // B9
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        // B10
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        // B11
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        // P12: Palette data pointer
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Unit Palette Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            P12 = rom.u32(addr + 12);

            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["P12"] = $"0x{P12:X08}",
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
                ["u8@0"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@3"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@4"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@5"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@6"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@7"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@8"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@9"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@11"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
