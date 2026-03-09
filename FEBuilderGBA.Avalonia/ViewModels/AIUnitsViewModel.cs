using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIUnitsViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _unknown1;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Unit ID (u8 at offset 0)</summary>
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        /// <summary>Unknown byte (u8 at offset 1)</summary>
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI Units Evaluation", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Unit = rom.u8(addr + 0);
            Unknown1 = rom.u8(addr + 1);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, Unit);
            rom.write_u8(addr + 1, Unknown1);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                { "Unit", Unit.ToString("X02") },
                { "Unknown1", Unknown1.ToString("X02") },
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
                ["u8@0x00_Unit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Unknown1"] = $"0x{rom.u8(a + 1):X02}",
            };
        }
    }
}
