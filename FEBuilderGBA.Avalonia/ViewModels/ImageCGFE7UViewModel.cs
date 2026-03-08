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
        uint _b0, _b1, _b2, _b3;
        uint _p4, _p8, _p12;

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
        // P4: Image data pointer
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        // P8: Palette pointer
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        // P12: TSA pointer
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }

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

            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            P4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);
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
                ["P4"] = $"0x{P4:X08}",
                ["P8"] = $"0x{P8:X08}",
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
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
