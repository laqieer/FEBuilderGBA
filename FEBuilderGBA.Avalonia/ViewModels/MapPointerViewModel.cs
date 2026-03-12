using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _mapDataPointer;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint MapDataPointer { get => _mapDataPointer; set => SetField(ref _mapDataPointer, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>Get the list of PLIST type names for the filter combo.</summary>
        public List<string> GetPlistTypeNames()
        {
            var names = new List<string> { "MAP", "CONFIG", "OBJ/PAL", "CHANGE", "EVENT", "ANIMATION1", "ANIMATION2" };

            // WORLDMAP type is only available for FE6
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo != null && rom.RomInfo.version == 6)
                names.Add("WORLDMAP");

            return names;
        }

        /// <summary>Get ROM pointer for the given PLIST type index.</summary>
        uint GetPlistPointer(int typeIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return typeIndex switch
            {
                1 => rom.RomInfo.map_config_pointer,
                2 => rom.RomInfo.map_obj_pointer,
                3 => rom.RomInfo.map_mapchange_pointer,
                4 => rom.RomInfo.map_event_pointer,
                5 => rom.RomInfo.map_tileanime1_pointer,
                6 => rom.RomInfo.map_tileanime2_pointer,
                7 => rom.RomInfo.worldmap_point_pointer,
                _ => rom.RomInfo.map_map_pointer_pointer,
            };
        }

        public List<AddrResult> LoadMapPointerList(int typeIndex = 0)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = GetPlistPointer(typeIndex);
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint limit = rom.RomInfo.map_map_pointer_list_default_size;
            if (limit == 0) limit = 256;

            string typeName = GetPlistTypeNames()[typeIndex];
            var result = new List<AddrResult>();
            for (uint i = 0; i < limit; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                string ptrStr = U.isPointer(pointer)
                    ? $"0x{pointer:X08}"
                    : (pointer == 0 ? "NULL" : $"0x{pointer:X08}");
                string name = $"{U.ToHexString(i)} {typeName} {ptrStr}";
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

            CanWrite = true;
        }

        public void WriteMapPointer()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, MapDataPointer);
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
