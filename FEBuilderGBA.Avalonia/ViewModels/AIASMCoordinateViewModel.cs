using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMCoordinateViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _x;
        uint _y;
        uint _unused2;
        uint _unused3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>X coordinate (offset 0)</summary>
        public uint X { get => _x; set => SetField(ref _x, value); }
        /// <summary>Y coordinate (offset 1)</summary>
        public uint Y { get => _y; set => SetField(ref _y, value); }
        /// <summary>Unused byte (offset 2)</summary>
        public uint Unused2 { get => _unused2; set => SetField(ref _unused2, value); }
        /// <summary>Unused byte (offset 3)</summary>
        public uint Unused3 { get => _unused3; set => SetField(ref _unused3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI ASM Coordinate", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            X = rom.u8(addr + 0);
            Y = rom.u8(addr + 1);
            Unused2 = rom.u8(addr + 2);
            Unused3 = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, X);
            rom.write_u8(addr + 1, Y);
            rom.write_u8(addr + 2, Unused2);
            rom.write_u8(addr + 3, Unused3);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["X"] = X.ToString("X02"),
                ["Y"] = Y.ToString("X02"),
                ["Unused2"] = Unused2.ToString("X02"),
                ["Unused3"] = Unused3.ToString("X02"),
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
                ["u8@0x00_X"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Y"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unused2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unused3"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
