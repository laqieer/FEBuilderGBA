using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AOERANGEViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _width;
        uint _height;
        uint _centerX;
        uint _centerY;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>AoE grid width (offset 0)</summary>
        public uint Width { get => _width; set => SetField(ref _width, value); }
        /// <summary>AoE grid height (offset 1)</summary>
        public uint Height { get => _height; set => SetField(ref _height, value); }
        /// <summary>Center point X coordinate (offset 2)</summary>
        public uint CenterX { get => _centerX; set => SetField(ref _centerX, value); }
        /// <summary>Center point Y coordinate (offset 3)</summary>
        public uint CenterY { get => _centerY; set => SetField(ref _centerY, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Area of Effect Range", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Width = rom.u8(addr + 0);
            Height = rom.u8(addr + 1);
            CenterX = rom.u8(addr + 2);
            CenterY = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, Width);
            rom.write_u8(addr + 1, Height);
            rom.write_u8(addr + 2, CenterX);
            rom.write_u8(addr + 3, CenterY);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Width"] = Width.ToString("X02"),
                ["Height"] = Height.ToString("X02"),
                ["CenterX"] = CenterX.ToString("X02"),
                ["CenterY"] = CenterY.ToString("X02"),
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
                ["u8@0x00_Width"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Height"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_CenterX"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_CenterY"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
