using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapPointViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _x;
        uint _y;
        uint _nameTextId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint X { get => _x; set => SetField(ref _x, value); }
        public uint Y { get => _y; set => SetField(ref _y, value); }
        public uint NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }

        public List<AddrResult> LoadWorldMapPointList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                // Termination: pointers at +12, +16, +20 must be pointer-or-null
                if (!U.isPointerOrNULL(rom.u32(addr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 16))) break;
                if (!U.isPointerOrNULL(rom.u32(addr + 20))) break;

                string name = U.ToHexString(i) + " World Map Point";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadWorldMapPoint(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            X = rom.u16(addr + 0);
            Y = rom.u16(addr + 2);
            NameTextId = rom.u16(addr + 28);
            IsLoaded = true;
        }

        public int GetListCount() => LoadWorldMapPointList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["X"] = $"0x{X:X04}",
                ["Y"] = $"0x{Y:X04}",
                ["NameTextId"] = $"0x{NameTextId:X04}",
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
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@0x1C"] = $"0x{rom.u16(a + 28):X04}",
            };
        }
    }
}
