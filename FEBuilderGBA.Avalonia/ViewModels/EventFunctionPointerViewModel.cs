using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventFunctionPointerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _eventCommandFunctionPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: Event command function pointer (u32 at offset 0) — ASM pointer
        public uint EventCommandFunctionPointer { get => _eventCommandFunctionPointer; set => SetField(ref _eventCommandFunctionPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_function_pointer_table_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint funcPtr = rom.u32(addr);
                if (!U.isPointer(funcPtr)) break;

                string ptrStr = $"0x{funcPtr:X08}";
                result.Add(new AddrResult(addr, $"0x{i:X2} {ptrStr}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EventCommandFunctionPointer = rom.u32(addr + 0);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            rom.write_u32(a + 0, EventCommandFunctionPointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["EventCommandFunctionPointer"] = EventCommandFunctionPointer.ToString("X08"),
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
                ["u32@0x00_EventCommandFunctionPointer"] = $"0x{rom.u32(a + 0):X08}",
            };
        }
    }
}
