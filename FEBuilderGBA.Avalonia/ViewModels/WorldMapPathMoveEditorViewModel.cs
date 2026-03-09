using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapPathMoveEditorViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Path Movement Editor", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _elapsedTime;
        uint _coordinateX;
        uint _coordinateY;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>D0: Elapsed time / duration at this node (lower = longer pause, total across all nodes must be &lt;= 4096)</summary>
        public uint ElapsedTime { get => _elapsedTime; set => SetField(ref _elapsedTime, value); }
        /// <summary>W4: X coordinate on world map</summary>
        public uint CoordinateX { get => _coordinateX; set => SetField(ref _coordinateX, value); }
        /// <summary>W6: Y coordinate on world map</summary>
        public uint CoordinateY { get => _coordinateY; set => SetField(ref _coordinateY, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ElapsedTime = rom.u32(addr + 0);
            CoordinateX = rom.u16(addr + 4);
            CoordinateY = rom.u16(addr + 6);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 8 > (uint)rom.Data.Length) return;

            rom.write_u32(addr + 0, ElapsedTime);
            rom.write_u16(addr + 4, (ushort)CoordinateX);
            rom.write_u16(addr + 6, (ushort)CoordinateY);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ElapsedTime"] = $"0x{ElapsedTime:X08}",
                ["CoordinateX"] = $"0x{CoordinateX:X04}",
                ["CoordinateY"] = $"0x{CoordinateY:X04}",
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
                ["ElapsedTime@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["CoordinateX@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["CoordinateY@0x06"] = $"0x{rom.u16(a + 6):X04}",
            };
        }
    }
}
