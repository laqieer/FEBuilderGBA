using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleDataFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _w0;
        uint _b2;
        uint _b3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // W0: u16 field at offset 0
        public uint W0 { get => _w0; set => SetField(ref _w0, value); }
        // B2: u8 field at offset 2
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        // B3: u8 field at offset 3
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Battle Data (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            W0 = rom.u16(addr + 0);
            B2 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["W0"] = W0.ToString("X04"),
                ["B2"] = B2.ToString("X02"),
                ["B3"] = B3.ToString("X02"),
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
