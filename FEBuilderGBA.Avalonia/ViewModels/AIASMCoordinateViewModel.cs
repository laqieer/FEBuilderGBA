using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIASMCoordinateViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        uint _currentAddr;
        bool _isLoaded;
        uint _x;
        uint _y;
        uint _unused2;
        uint _unused3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>X coordinate (offset 0)</summary>
        public uint X { get => _x; set => SetField(ref _x, value); }
        /// <summary>Y coordinate (offset 1)</summary>
        public uint Y { get => _y; set => SetField(ref _y, value); }
        /// <summary>Unused byte (offset 2)</summary>
        public uint Unused2 { get => _unused2; set => SetField(ref _unused2, value); }
        /// <summary>Unused byte (offset 3)</summary>
        public uint Unused3 { get => _unused3; set => SetField(ref _unused3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Find first valid 4-byte AI coordinate sub-data from AI scripts
            uint addr = AISubEditorHelper.FindFirstValidAISubData(rom, 4);
            if (addr != 0)
            {
                LoadEntry(addr);
                return new List<AddrResult> { new AddrResult(addr, "AI ASM Coordinate", 0) };
            }

            return new List<AddrResult>();
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            X = values["B0"];
            Y = values["B1"];
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
                ["B0"] = X, ["B1"] = Y,
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
                ["X"] = $"0x{X:X02}",
                ["Y"] = $"0x{Y:X02}",
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
                ["u8@0x00_X"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Y"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unused2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unused3"] = $"0x{rom.u8(a + 3):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["X"] = "u8@0x00_X",
            ["Y"] = "u8@0x01_Y",
            ["Unused2"] = "u8@0x02_Unused2",
            ["Unused3"] = "u8@0x03_Unused3",
        };
    }
}
