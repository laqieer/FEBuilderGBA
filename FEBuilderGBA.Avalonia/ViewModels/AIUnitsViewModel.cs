using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIUnitsViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1" });

        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _unknown1;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Unit ID (u8 at offset 0)</summary>
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        /// <summary>Unknown byte (u8 at offset 1)</summary>
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }

        public List<AddrResult> LoadList()
        {
            // Context-dependent sub-editor (#1414): reachable in WinForms ONLY via the
            // AIScript per-parameter POINTER_AIUNIT dispatch, which supplies the real
            // script pointer. Standalone (no parent pointer), we MUST NOT guess a write
            // target by scanning AI scripts — that silently corrupts ROM. Present a single
            // placeholder entry at addr 0 so Write() is a guarded no-op (CurrentAddr == 0).
            // A real address still arrives via NavigateTo()/LoadEntry() from the parent.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return new List<AddrResult> { new AddrResult(0, "AI Units Evaluation", 0) };
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            Unit = values["B0"];
            Unknown1 = values["B1"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = Unit,
                ["B1"] = Unknown1,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unit"] = $"0x{Unit:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
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
                ["u8@0x00_Unit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Unknown1"] = $"0x{rom.u8(a + 1):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Unit"] = "u8@0x00_Unit",
            ["Unknown1"] = "u8@0x01_Unknown1",
        };
    }
}
