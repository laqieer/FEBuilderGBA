using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        uint _d0, _d4, _d8;
        uint _b12, _b13, _b14, _b15;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Portrait image pointer
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        // D4: Mini portrait pointer
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        // D8: Palette pointer
        public uint D8 { get => _d8; set => SetField(ref _d8, value); }
        // B12
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        // B13
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        // B14
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        // B15
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Portrait Editor (FE6)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            D0 = rom.u32(addr + 0);
            D4 = rom.u32(addr + 4);
            D8 = rom.u32(addr + 8);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);

            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0"] = $"0x{D0:X08}",
                ["D4"] = $"0x{D4:X08}",
                ["D8"] = $"0x{D8:X08}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@12"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@13"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@14"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@15"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
