using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIMapSettingViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _trait1;
        uint _trait2;
        uint _trait3;
        uint _trait4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>AI trait flags byte 1 (offset 0, bit flags)</summary>
        public uint Trait1 { get => _trait1; set => SetField(ref _trait1, value); }
        /// <summary>AI trait flags byte 2 (offset 1, bit flags)</summary>
        public uint Trait2 { get => _trait2; set => SetField(ref _trait2, value); }
        /// <summary>AI trait flags byte 3 (offset 2, bit flags)</summary>
        public uint Trait3 { get => _trait3; set => SetField(ref _trait3, value); }
        /// <summary>AI trait flags byte 4 (offset 3, bit flags)</summary>
        public uint Trait4 { get => _trait4; set => SetField(ref _trait4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI Map Settings", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Trait1 = rom.u8(addr + 0);
            Trait2 = rom.u8(addr + 1);
            Trait3 = rom.u8(addr + 2);
            Trait4 = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, Trait1);
            rom.write_u8(addr + 1, Trait2);
            rom.write_u8(addr + 2, Trait3);
            rom.write_u8(addr + 3, Trait4);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Trait1"] = Trait1.ToString("X02"),
                ["Trait2"] = Trait2.ToString("X02"),
                ["Trait3"] = Trait3.ToString("X02"),
                ["Trait4"] = Trait4.ToString("X02"),
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
                ["u8@0x00_Trait1"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Trait2"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Trait3"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Trait4"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
