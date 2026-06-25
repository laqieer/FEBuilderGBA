using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMRangeViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        uint _currentAddr;
        bool _isLoaded;
        uint _x1;
        uint _y1;
        uint _x2;
        uint _y2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Range start X coordinate (offset 0)</summary>
        public uint X1 { get => _x1; set => SetField(ref _x1, value); }
        /// <summary>Range start Y coordinate (offset 1)</summary>
        public uint Y1 { get => _y1; set => SetField(ref _y1, value); }
        /// <summary>Range end X coordinate (offset 2)</summary>
        public uint X2 { get => _x2; set => SetField(ref _x2, value); }
        /// <summary>Range end Y coordinate (offset 3)</summary>
        public uint Y2 { get => _y2; set => SetField(ref _y2, value); }

        public List<AddrResult> LoadList()
        {
            // Context-dependent sub-editor (#1414): reachable in WinForms ONLY via the
            // AIScript per-parameter POINTER_AIRANGE dispatch, which supplies the real
            // script pointer (allocate-on-null + isSafetyOffset guard). Standalone (no
            // parent pointer), we MUST NOT guess a write target by scanning AI scripts —
            // that silently corrupts ROM. Present a single placeholder entry at addr 0 so
            // Write() is a guarded no-op (CurrentAddr == 0). A real address still arrives
            // via NavigateTo()/LoadEntry() from the parent.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return new List<AddrResult> { new AddrResult(0, "AI ASM Range", 0) };
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            X1 = values["B0"];
            Y1 = values["B1"];
            X2 = values["B2"];
            Y2 = values["B3"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = X1, ["B1"] = Y1,
                ["B2"] = X2, ["B3"] = Y2,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["X1"] = $"0x{X1:X02}",
                ["Y1"] = $"0x{Y1:X02}",
                ["X2"] = $"0x{X2:X02}",
                ["Y2"] = $"0x{Y2:X02}",
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
                ["u8@0x00_X1"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Y1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_X2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Y2"] = $"0x{rom.u8(a + 3):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["X1"] = "u8@0x00_X1",
            ["Y1"] = "u8@0x01_Y1",
            ["X2"] = "u8@0x02_X2",
            ["Y2"] = "u8@0x03_Y2",
        };
    }
}
