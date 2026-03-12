using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIMapSettingViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        uint _currentAddr;
        bool _isLoaded;
        uint _trait1;
        uint _trait2;
        uint _trait3;
        uint _trait4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>AI trait flags byte 1 (offset 0, bit flags)</summary>
        public uint Trait1 { get => _trait1; set => SetField(ref _trait1, value); }
        /// <summary>AI trait flags byte 2 (offset 1, bit flags)</summary>
        public uint Trait2 { get => _trait2; set => SetField(ref _trait2, value); }
        /// <summary>AI trait flags byte 3 (offset 2, bit flags)</summary>
        public uint Trait3 { get => _trait3; set => SetField(ref _trait3, value); }
        /// <summary>AI trait flags byte 4 (offset 3, bit flags)</summary>
        public uint Trait4 { get => _trait4; set => SetField(ref _trait4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.ai_map_setting_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                string display = $"0x{i:X2} Map {i}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            Trait1 = values["B0"];
            Trait2 = values["B1"];
            Trait3 = values["B2"];
            Trait4 = values["B3"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = Trait1, ["B1"] = Trait2,
                ["B2"] = Trait3, ["B3"] = Trait4,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Trait1"] = Trait1.ToString("X02"),
                ["Trait2"] = Trait2.ToString("X02"),
                ["Trait3"] = Trait3.ToString("X02"),
                ["Trait4"] = Trait4.ToString("X02"),
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
                ["u8@0x00_Trait1"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Trait2"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Trait3"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Trait4"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
