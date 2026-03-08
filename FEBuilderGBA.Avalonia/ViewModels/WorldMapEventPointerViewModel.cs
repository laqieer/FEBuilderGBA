using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapEventPointerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _eventPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }

        public List<AddrResult> LoadWorldMapEventList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_worldmapevent_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint eventPtr = rom.u32(addr);
                // Stop at null entries
                if (eventPtr == 0) break;

                string name = U.ToHexString(i) + " World Map Event";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadWorldMapEvent(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EventPointer = rom.u32(addr);
            CanWrite = true;
        }

        public void WriteWorldMapEvent()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;

            rom.write_u32(CurrentAddr, EventPointer);
        }

        public int GetListCount() => LoadWorldMapEventList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EventPointer"] = $"0x{EventPointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
