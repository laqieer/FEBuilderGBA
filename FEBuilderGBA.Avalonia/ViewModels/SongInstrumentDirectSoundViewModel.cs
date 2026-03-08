using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongInstrumentDirectSoundViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Direct Sound Instruments", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _d0;
        uint _d4;
        uint _d8;
        uint _d12;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint D0 { get => _d0; set => SetField(ref _d0, value); }
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        public uint D8 { get => _d8; set => SetField(ref _d8, value); }
        public uint D12 { get => _d12; set => SetField(ref _d12, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            D0 = rom.u32(addr + 0);
            D4 = rom.u32(addr + 4);
            D8 = rom.u32(addr + 8);
            D12 = rom.u32(addr + 12);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0"] = $"0x{D0:X08}",
                ["D4"] = $"0x{D4:X08}",
                ["D8"] = $"0x{D8:X08}",
                ["D12"] = $"0x{D12:X08}",
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
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
