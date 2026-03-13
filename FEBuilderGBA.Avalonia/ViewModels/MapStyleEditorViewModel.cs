using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _objPointer;
        uint _configPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ObjPointer { get => _objPointer; set => SetField(ref _objPointer, value); }
        public uint ConfigPointer { get => _configPointer; set => SetField(ref _configPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Enumerate unique tileset indices from the map_obj_pointer table
            uint objPointer = rom.RomInfo.map_obj_pointer;
            if (objPointer == 0) return new List<AddrResult>();

            uint tableBase = rom.p32(objPointer);
            if (!U.isSafetyOffset(tableBase, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Enumerate entries (PLIST indices 0..255)
            for (int i = 0; i < 256; i++)
            {
                uint entryAddr = (uint)(tableBase + i * 4);
                if (entryAddr + 4 > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(entryAddr);
                if (ptr == 0 || !U.isPointer(ptr))
                    continue;

                string label = $"0x{i:X2} Tileset";
                result.Add(new AddrResult(entryAddr, label, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ObjPointer = rom.u32(addr);

            // Also load config pointer from the parallel config table
            uint configTablePointer = rom.RomInfo.map_config_pointer;
            if (configTablePointer != 0)
            {
                uint objTableBase = rom.p32(rom.RomInfo.map_obj_pointer);
                if (U.isSafetyOffset(objTableBase, rom) && addr >= objTableBase)
                {
                    uint index = (addr - objTableBase) / 4;
                    uint configTableBase = rom.p32(configTablePointer);
                    if (U.isSafetyOffset(configTableBase, rom))
                    {
                        uint configEntryAddr = configTableBase + index * 4;
                        if (configEntryAddr + 4 <= (uint)rom.Data.Length)
                            ConfigPointer = rom.u32(configEntryAddr);
                    }
                }
            }

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ObjPointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ObjPointer"] = $"0x{ObjPointer:X08}",
                ["ConfigPointer"] = $"0x{ConfigPointer:X08}",
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
                ["ObjPointer@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
