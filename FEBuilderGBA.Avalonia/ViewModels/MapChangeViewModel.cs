using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapChangeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _changePointer;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ChangePointer { get => _changePointer; set => SetField(ref _changePointer, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadMapChangeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_mapchange_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                // Stop if we hit clearly invalid data
                if (pointer == 0xFFFFFFFF) break;

                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : (pointer == 0 ? "NULL" : "0x" + pointer.ToString("X08"));
                string name = U.ToHexString(i) + " Change " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapChange(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ChangePointer = rom.u32(addr);

            CanWrite = true;
        }

        public void WriteMapChange()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ChangePointer);
        }

        public int GetListCount() => LoadMapChangeList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ChangePointer"] = $"0x{ChangePointer:X08}",
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
