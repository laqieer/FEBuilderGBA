using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTileAnimation2ViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Tile Animation Type 2", 0));
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _paletteDataPointer;
        uint _animInterval;
        uint _dataCount;
        uint _startPaletteIndex;
        uint _unknown7;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Pointer to palette data to replace (P0 / J_0 "Palette Data to Replace").</summary>
        public uint PaletteDataPointer { get => _paletteDataPointer; set => SetField(ref _paletteDataPointer, value); }
        /// <summary>Animation interval (B4 / J_4).</summary>
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        /// <summary>Data count (B5 / J_5).</summary>
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        /// <summary>Start palette index (B6 / J_6).</summary>
        public uint StartPaletteIndex { get => _startPaletteIndex; set => SetField(ref _startPaletteIndex, value); }
        /// <summary>Unknown field at offset 7 (B7 / J_7).</summary>
        public uint Unknown7 { get => _unknown7; set => SetField(ref _unknown7, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            PaletteDataPointer = rom.u32(addr + 0);
            AnimInterval = rom.u8(addr + 4);
            DataCount = rom.u8(addr + 5);
            StartPaletteIndex = rom.u8(addr + 6);
            Unknown7 = rom.u8(addr + 7);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr + 0, PaletteDataPointer);
            rom.write_u8(CurrentAddr + 4, (byte)AnimInterval);
            rom.write_u8(CurrentAddr + 5, (byte)DataCount);
            rom.write_u8(CurrentAddr + 6, (byte)StartPaletteIndex);
            rom.write_u8(CurrentAddr + 7, (byte)Unknown7);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PaletteDataPointer"] = $"0x{PaletteDataPointer:X08}",
                ["AnimInterval"] = $"0x{AnimInterval:X02}",
                ["DataCount"] = $"0x{DataCount:X02}",
                ["StartPaletteIndex"] = $"0x{StartPaletteIndex:X02}",
                ["Unknown7"] = $"0x{Unknown7:X02}",
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
                ["PaletteDataPointer@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["AnimInterval@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["DataCount@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["StartPaletteIndex@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["Unknown7@0x07"] = $"0x{rom.u8(a + 7):X02}",
            };
        }
    }
}
