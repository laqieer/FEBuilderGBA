using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageSystemAreaViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 2;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _gbaColor;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // W0: GBA color value (15-bit RGB555)
        public uint GBAColor { get => _gbaColor; set => SetField(ref _gbaColor, value); }

        int _filterIndex;
        public int FilterIndex { get => _filterIndex; set => SetField(ref _filterIndex, value); }
        public string[] FilterNames => new[] { "Move Range", "Attack Range", "Staff Range" };

        uint GetFilterPointer(int index)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return index switch
            {
                0 => rom.RomInfo.systemarea_move_gradation_palette_pointer,
                1 => rom.RomInfo.systemarea_attack_gradation_palette_pointer,
                2 => rom.RomInfo.systemarea_staff_gradation_palette_pointer,
                _ => 0
            };
        }

        public List<AddrResult> LoadList()
        {
            return LoadColorList(FilterIndex);
        }

        public List<AddrResult> LoadColorList(int filterIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = GetFilterPointer(filterIndex);
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // 30 u16 colors = 60 bytes
            int colorCount = 30;
            var result = new List<AddrResult>();
            for (int i = 0; i < colorCount; i++)
            {
                uint addr = baseAddr + (uint)(i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint color = rom.u16(addr);
                int r = (int)(color & 0x1F) * 8;
                int g = (int)((color >> 5) & 0x1F) * 8;
                int b = (int)((color >> 10) & 0x1F) * 8;
                result.Add(new AddrResult(addr, $"0x{i:X2} #{r:X2}{g:X2}{b:X2}", color));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            GBAColor = rom.u16(addr + 0);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u16(addr + 0, GBAColor);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["GBAColor"] = $"0x{GBAColor:X04}",
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
                ["u16@0"] = $"0x{rom.u16(a + 0):X04}",
            };
        }
    }
}
