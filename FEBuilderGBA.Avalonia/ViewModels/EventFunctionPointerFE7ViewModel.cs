using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventFunctionPointerFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _eventCommandFunctionPointer;
        uint _unknown4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: Event command function pointer (u32 at offset 0) — ASM pointer
        public uint EventCommandFunctionPointer { get => _eventCommandFunctionPointer; set => SetField(ref _eventCommandFunctionPointer, value); }
        // D4: Unknown value (u32 at offset 4) — possibly size or category
        public uint Unknown4 { get => _unknown4; set => SetField(ref _unknown4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Function Pointer (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EventCommandFunctionPointer = rom.u32(addr + 0);
            Unknown4 = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 8 > (uint)rom.Data.Length) return;

            rom.write_u32(a + 0, EventCommandFunctionPointer);
            rom.write_u32(a + 4, Unknown4);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["EventCommandFunctionPointer"] = EventCommandFunctionPointer.ToString("X08"),
                ["Unknown4"] = Unknown4.ToString("X08"),
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
                ["u32@0x04_Unknown4"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
