using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Map tile animation type 2 editor (palette animation).
    /// WinForms: MapTileAnimation2Form — block size 8, validated by isPointer(u32(addr+0)).
    /// Fields: PaletteDataPointer (u32@0), AnimInterval (u8@4), DataCount (u8@5),
    /// StartPaletteIndex (u8@6), Unknown7 (u8@7).</summary>
    public class MapTileAnimation2ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _paletteDataPointer;
        uint _animInterval;
        uint _dataCount;
        uint _startPaletteIndex;
        uint _unknown7;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint PaletteDataPointer { get => _paletteDataPointer; set => SetField(ref _paletteDataPointer, value); }
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        public uint StartPaletteIndex { get => _startPaletteIndex; set => SetField(ref _startPaletteIndex, value); }
        public uint Unknown7 { get => _unknown7; set => SetField(ref _unknown7, value); }

        /// <summary>Build list from a given base address (set via filter/JumpTo).</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null || baseAddr == 0) return result;

            const uint blockSize = 8;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Validate: P0 must be a valid pointer
                if (!U.isPointer(rom.u32(addr + 0))) break;

                uint interval = rom.u8(addr + 4);
                uint count = rom.u8(addr + 5);
                string display = $"0x{i:X2} Palette Interval={interval:X2} Count={count:X2}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_tileanime2_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();
            return BuildList(baseAddr);
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            PaletteDataPointer = rom.u32(addr + 0);
            AnimInterval = rom.u8(addr + 4);
            DataCount = rom.u8(addr + 5);
            StartPaletteIndex = rom.u8(addr + 6);
            Unknown7 = rom.u8(addr + 7);
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
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

        public int GetListCount() => LoadList().Count;

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
