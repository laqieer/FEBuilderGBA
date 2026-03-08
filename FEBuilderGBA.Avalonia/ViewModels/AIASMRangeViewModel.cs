using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMRangeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _b0;
        uint _b1;
        uint _b2;
        uint _b3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // B0: u8 field at offset 0
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        // B1: u8 field at offset 1
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        // B2: u8 field at offset 2
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        // B3: u8 field at offset 3
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI ASM Range", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["B0"] = B0.ToString("X02"),
                ["B1"] = B1.ToString("X02"),
                ["B2"] = B2.ToString("X02"),
                ["B3"] = B3.ToString("X02"),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            uint addr = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["B0"] = rom.u8(addr + 0).ToString("X02"),
                ["B1"] = rom.u8(addr + 1).ToString("X02"),
                ["B2"] = rom.u8(addr + 2).ToString("X02"),
                ["B3"] = rom.u8(addr + 3).ToString("X02"),
            };
        }
    }
}
