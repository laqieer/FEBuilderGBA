using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMCALLTALKViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        uint _currentAddr;
        bool _isLoaded;
        uint _fromUnit;
        uint _toUnit;
        uint _unused2;
        uint _unused3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Unit that initiates the talk event (offset 0)</summary>
        public uint FromUnit { get => _fromUnit; set => SetField(ref _fromUnit, value); }
        /// <summary>Unit that is the talk target (offset 1)</summary>
        public uint ToUnit { get => _toUnit; set => SetField(ref _toUnit, value); }
        /// <summary>Unused byte (offset 2)</summary>
        public uint Unused2 { get => _unused2; set => SetField(ref _unused2, value); }
        /// <summary>Unused byte (offset 3)</summary>
        public uint Unused3 { get => _unused3; set => SetField(ref _unused3, value); }

        public List<AddrResult> LoadList()
        {
            // Context-dependent sub-editor (#1414): reachable in WinForms ONLY via the
            // AIScript per-parameter POINTER_AICALLTALK dispatch, which supplies the real
            // script pointer (allocate-on-null + isSafetyOffset guard). Standalone (no
            // parent pointer), we MUST NOT guess a write target by scanning AI scripts —
            // that silently corrupts ROM. Present a single placeholder entry at addr 0 so
            // Write() is a guarded no-op (CurrentAddr == 0). A real address still arrives
            // via NavigateTo()/LoadEntry() from the parent.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return new List<AddrResult> { new AddrResult(0, "AI ASM Call Talk", 0) };
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            FromUnit = values["B0"];
            ToUnit = values["B1"];
            Unused2 = values["B2"];
            Unused3 = values["B3"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = FromUnit, ["B1"] = ToUnit,
                ["B2"] = Unused2, ["B3"] = Unused3,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["FromUnit"] = $"0x{FromUnit:X02}",
                ["ToUnit"] = $"0x{ToUnit:X02}",
                ["Unused2"] = $"0x{Unused2:X02}",
                ["Unused3"] = $"0x{Unused3:X02}",
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
                ["u8@0x00_FromUnit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_ToUnit"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unused2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unused3"] = $"0x{rom.u8(a + 3):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["FromUnit"] = "u8@0x00_FromUnit",
            ["ToUnit"] = "u8@0x01_ToUnit",
            ["Unused2"] = "u8@0x02_Unused2",
            ["Unused3"] = "u8@0x03_Unused3",
        };
    }
}
