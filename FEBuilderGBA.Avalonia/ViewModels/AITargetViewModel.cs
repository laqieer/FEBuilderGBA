using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AITargetViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _b0;
        uint _b1;
        uint _b2;
        uint _b3;
        uint _b4;
        uint _b5;
        uint _b6;
        uint _b7;
        uint _b8;
        uint _b9;
        uint _b10;
        uint _b11;
        uint _b12;
        uint _b13;
        uint _b14;
        uint _b15;
        uint _b16;
        uint _b17;
        uint _b18;
        uint _b19;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI Targeting", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

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
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B18 = rom.u8(addr + 18);
            B19 = rom.u8(addr + 19);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                { "B0", B0.ToString("X02") },
                { "B1", B1.ToString("X02") },
                { "B2", B2.ToString("X02") },
                { "B3", B3.ToString("X02") },
                { "B4", B4.ToString("X02") },
                { "B5", B5.ToString("X02") },
                { "B6", B6.ToString("X02") },
                { "B7", B7.ToString("X02") },
                { "B8", B8.ToString("X02") },
                { "B9", B9.ToString("X02") },
                { "B10", B10.ToString("X02") },
                { "B11", B11.ToString("X02") },
                { "B12", B12.ToString("X02") },
                { "B13", B13.ToString("X02") },
                { "B14", B14.ToString("X02") },
                { "B15", B15.ToString("X02") },
                { "B16", B16.ToString("X02") },
                { "B17", B17.ToString("X02") },
                { "B18", B18.ToString("X02") },
                { "B19", B19.ToString("X02") },
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();
            uint addr = CurrentAddr;
            return new Dictionary<string, string>
            {
                { "B0", rom.u8(addr + 0).ToString("X02") },
                { "B1", rom.u8(addr + 1).ToString("X02") },
                { "B2", rom.u8(addr + 2).ToString("X02") },
                { "B3", rom.u8(addr + 3).ToString("X02") },
                { "B4", rom.u8(addr + 4).ToString("X02") },
                { "B5", rom.u8(addr + 5).ToString("X02") },
                { "B6", rom.u8(addr + 6).ToString("X02") },
                { "B7", rom.u8(addr + 7).ToString("X02") },
                { "B8", rom.u8(addr + 8).ToString("X02") },
                { "B9", rom.u8(addr + 9).ToString("X02") },
                { "B10", rom.u8(addr + 10).ToString("X02") },
                { "B11", rom.u8(addr + 11).ToString("X02") },
                { "B12", rom.u8(addr + 12).ToString("X02") },
                { "B13", rom.u8(addr + 13).ToString("X02") },
                { "B14", rom.u8(addr + 14).ToString("X02") },
                { "B15", rom.u8(addr + 15).ToString("X02") },
                { "B16", rom.u8(addr + 16).ToString("X02") },
                { "B17", rom.u8(addr + 17).ToString("X02") },
                { "B18", rom.u8(addr + 18).ToString("X02") },
                { "B19", rom.u8(addr + 19).ToString("X02") },
            };
        }
    }
}
