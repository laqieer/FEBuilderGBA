using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapExitPointViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _exitPointer;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ExitPointer { get => _exitPointer; set => SetField(ref _exitPointer, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadMapExitPointList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_exit_point_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint npcBlockAdd = rom.RomInfo.map_exit_point_npc_blockadd;
            uint maxEntries = npcBlockAdd > 0 ? npcBlockAdd : 0x100;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxEntries; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                if (!U.isPointerOrNULL(pointer)) break;

                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : "NULL";
                string name = U.ToHexString(i) + " ExitPoint " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapExitPoint(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ExitPointer = rom.u32(addr);

            IsLoaded = true;
        }

        public int GetListCount() => LoadMapExitPointList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ExitPointer"] = $"0x{ExitPointer:X08}",
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
