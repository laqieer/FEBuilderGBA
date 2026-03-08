using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusOptionViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _d0;
        uint _w4;
        uint _w6;
        uint _w8;
        uint _w10;
        uint _w12;
        uint _w14;
        uint _w16;
        uint _w18;
        uint _w20;
        uint _w22;
        uint _w24;
        uint _w26;
        uint _w28;
        uint _w30;
        uint _w32;
        uint _w34;
        uint _d36;
        uint _p40;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }
        public uint W6 { get => _w6; set => SetField(ref _w6, value); }
        public uint W8 { get => _w8; set => SetField(ref _w8, value); }
        public uint W10 { get => _w10; set => SetField(ref _w10, value); }
        public uint W12 { get => _w12; set => SetField(ref _w12, value); }
        public uint W14 { get => _w14; set => SetField(ref _w14, value); }
        public uint W16 { get => _w16; set => SetField(ref _w16, value); }
        public uint W18 { get => _w18; set => SetField(ref _w18, value); }
        public uint W20 { get => _w20; set => SetField(ref _w20, value); }
        public uint W22 { get => _w22; set => SetField(ref _w22, value); }
        public uint W24 { get => _w24; set => SetField(ref _w24, value); }
        public uint W26 { get => _w26; set => SetField(ref _w26, value); }
        public uint W28 { get => _w28; set => SetField(ref _w28, value); }
        public uint W30 { get => _w30; set => SetField(ref _w30, value); }
        public uint W32 { get => _w32; set => SetField(ref _w32, value); }
        public uint W34 { get => _w34; set => SetField(ref _w34, value); }
        public uint D36 { get => _d36; set => SetField(ref _d36, value); }
        public uint P40 { get => _p40; set => SetField(ref _p40, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Status Screen Options", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 44 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            D0 = rom.u32(addr + 0);
            W4 = rom.u16(addr + 4);
            W6 = rom.u16(addr + 6);
            W8 = rom.u16(addr + 8);
            W10 = rom.u16(addr + 10);
            W12 = rom.u16(addr + 12);
            W14 = rom.u16(addr + 14);
            W16 = rom.u16(addr + 16);
            W18 = rom.u16(addr + 18);
            W20 = rom.u16(addr + 20);
            W22 = rom.u16(addr + 22);
            W24 = rom.u16(addr + 24);
            W26 = rom.u16(addr + 26);
            W28 = rom.u16(addr + 28);
            W30 = rom.u16(addr + 30);
            W32 = rom.u16(addr + 32);
            W34 = rom.u16(addr + 34);
            D36 = rom.u32(addr + 36);
            P40 = rom.u32(addr + 40);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                { "D0", D0.ToString("X08") },
                { "W4", W4.ToString("X04") },
                { "W6", W6.ToString("X04") },
                { "W8", W8.ToString("X04") },
                { "W10", W10.ToString("X04") },
                { "W12", W12.ToString("X04") },
                { "W14", W14.ToString("X04") },
                { "W16", W16.ToString("X04") },
                { "W18", W18.ToString("X04") },
                { "W20", W20.ToString("X04") },
                { "W22", W22.ToString("X04") },
                { "W24", W24.ToString("X04") },
                { "W26", W26.ToString("X04") },
                { "W28", W28.ToString("X04") },
                { "W30", W30.ToString("X04") },
                { "W32", W32.ToString("X04") },
                { "W34", W34.ToString("X04") },
                { "D36", D36.ToString("X08") },
                { "P40", P40.ToString("X08") },
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
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u16@0x0A"] = $"0x{rom.u16(a + 10):X04}",
                ["u16@0x0C"] = $"0x{rom.u16(a + 12):X04}",
                ["u16@0x0E"] = $"0x{rom.u16(a + 14):X04}",
                ["u16@0x10"] = $"0x{rom.u16(a + 16):X04}",
                ["u16@0x12"] = $"0x{rom.u16(a + 18):X04}",
                ["u16@0x14"] = $"0x{rom.u16(a + 20):X04}",
                ["u16@0x16"] = $"0x{rom.u16(a + 22):X04}",
                ["u16@0x18"] = $"0x{rom.u16(a + 24):X04}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u16@0x1C"] = $"0x{rom.u16(a + 28):X04}",
                ["u16@0x1E"] = $"0x{rom.u16(a + 30):X04}",
                ["u16@0x20"] = $"0x{rom.u16(a + 32):X04}",
                ["u16@0x22"] = $"0x{rom.u16(a + 34):X04}",
                ["u32@0x24"] = $"0x{rom.u32(a + 36):X08}",
                ["u32@0x28"] = $"0x{rom.u32(a + 40):X08}",
            };
        }
    }
}
