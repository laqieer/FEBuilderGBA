using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMRangeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _x1;
        uint _y1;
        uint _x2;
        uint _y2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Range start X coordinate (offset 0)</summary>
        public uint X1 { get => _x1; set => SetField(ref _x1, value); }
        /// <summary>Range start Y coordinate (offset 1)</summary>
        public uint Y1 { get => _y1; set => SetField(ref _y1, value); }
        /// <summary>Range end X coordinate (offset 2)</summary>
        public uint X2 { get => _x2; set => SetField(ref _x2, value); }
        /// <summary>Range end Y coordinate (offset 3)</summary>
        public uint Y2 { get => _y2; set => SetField(ref _y2, value); }

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
            X1 = rom.u8(addr + 0);
            Y1 = rom.u8(addr + 1);
            X2 = rom.u8(addr + 2);
            Y2 = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, X1);
            rom.write_u8(addr + 1, Y1);
            rom.write_u8(addr + 2, X2);
            rom.write_u8(addr + 3, Y2);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["X1"] = X1.ToString("X02"),
                ["Y1"] = Y1.ToString("X02"),
                ["X2"] = X2.ToString("X02"),
                ["Y2"] = Y2.ToString("X02"),
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
                ["u8@0x00_X1"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Y1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_X2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Y2"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
