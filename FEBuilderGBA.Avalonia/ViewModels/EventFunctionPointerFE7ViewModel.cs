using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventFunctionPointerFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        uint _d4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: pointer at offset 0
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        // D4: u32 field at offset 4
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Function Pointer (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);
            D4 = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["P0"] = P0.ToString("X08"),
                ["D4"] = D4.ToString("X08"),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            uint addr = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["P0"] = rom.u32(addr + 0).ToString("X08"),
                ["D4"] = rom.u32(addr + 4).ToString("X08"),
            };
        }
    }
}
