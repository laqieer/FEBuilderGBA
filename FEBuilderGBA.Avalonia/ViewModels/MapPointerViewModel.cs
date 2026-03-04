using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _mapDataPointer;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint MapDataPointer { get => _mapDataPointer; set => SetField(ref _mapDataPointer, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadMapPointerList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_map_pointer_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint limit = rom.RomInfo.map_map_pointer_list_default_size;
            if (limit == 0) limit = 256;

            var result = new List<AddrResult>();
            for (uint i = 0; i < limit; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : (pointer == 0 ? "NULL" : "0x" + pointer.ToString("X08"));
                string name = U.ToHexString(i) + " Map " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapPointer(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            MapDataPointer = rom.u32(addr);

            IsLoaded = true;
        }

        public int GetListCount() => LoadMapPointerList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["MapDataPointer"] = $"0x{MapDataPointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }
    }
}
