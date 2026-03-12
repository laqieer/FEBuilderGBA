using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Map load function pointer list (FE8 only).
    /// WinForms: MapLoadFunctionForm — block size 4, count from switch1 address.
    /// Each entry maps a chapter/map to a function pointer called on map load.</summary>
    public class MapLoadFunctionViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        string _pointerInfo = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public string PointerInfo { get => _pointerInfo; set => SetField(ref _pointerInfo, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.map_load_function_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint switchAddr = rom.RomInfo.map_load_function_switch1_address;
            if (switchAddr == 0) return new List<AddrResult>();

            // Check switch1 is enabled
            if (switchAddr + 4 > (uint)rom.Data.Length) return new List<AddrResult>();
            // Switch1 check: first byte at switch address should be non-zero enable marker
            // Count is at switch1_address + 0
            uint count = rom.u8(switchAddr + 0);

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i <= count; i++)
            {
                uint addr = baseAddr + i * 4;
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint funcPtr = rom.u32(addr);
                string ptrStr = U.isPointer(funcPtr) ? $"0x{funcPtr:X08}" : (funcPtr == 0 ? "NULL" : $"0x{funcPtr:X08}");
                string display = $"0x{i:X2} {ptrStr}";
                result.Add(new AddrResult(addr, display, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["D0"];
            PointerInfo = U.isPointer(P0) ? $"Function at 0x{(P0 & 0x1FFFFFF):X08}" : (P0 == 0 ? "NULL (no function)" : $"Invalid: 0x{P0:X08}");
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["D0"] = P0 };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
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
                ["u32@0x00_P0"] = $"0x{rom.u32(a + 0):X08}",
                ["u8@0x00_SwitchCount"] = $"0x{rom.u8(a):X02}",
            };
        }
    }
}
