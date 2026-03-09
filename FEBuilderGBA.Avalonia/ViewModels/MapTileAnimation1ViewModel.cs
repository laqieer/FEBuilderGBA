using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTileAnimation1ViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Tile Animation Type 1", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _animInterval;
        uint _dataCount;
        uint _mapTileDataPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Animation interval (W0 / J_0 "Animation Interval").</summary>
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        /// <summary>Data count (W2 / label1 "Data Count").</summary>
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        /// <summary>Pointer to replacement map tile data (P4 / label3 "Map Tile to Replace").</summary>
        public uint MapTileDataPointer { get => _mapTileDataPointer; set => SetField(ref _mapTileDataPointer, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            AnimInterval = rom.u16(addr + 0);
            DataCount = rom.u16(addr + 2);
            MapTileDataPointer = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u16(CurrentAddr + 0, (ushort)AnimInterval);
            rom.write_u16(CurrentAddr + 2, (ushort)DataCount);
            rom.write_u32(CurrentAddr + 4, MapTileDataPointer);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AnimInterval"] = $"0x{AnimInterval:X04}",
                ["DataCount"] = $"0x{DataCount:X04}",
                ["MapTileDataPointer"] = $"0x{MapTileDataPointer:X08}",
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
                ["AnimInterval@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["DataCount@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["MapTileDataPointer@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
