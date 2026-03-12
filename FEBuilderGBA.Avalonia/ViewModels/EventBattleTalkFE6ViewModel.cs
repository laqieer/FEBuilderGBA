using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "W8", "B10", "B11" });

        uint _currentAddr;
        bool _isLoaded;
        uint _attackerUnit;
        uint _defenderUnit;
        uint _unknown02;
        uint _unknown03;
        uint _text;
        uint _unknown06;
        uint _unknown07;
        uint _achievementFlag;
        uint _unknown0A;
        uint _unknown0B;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackerUnit { get => _attackerUnit; set => SetField(ref _attackerUnit, value); }
        public uint DefenderUnit { get => _defenderUnit; set => SetField(ref _defenderUnit, value); }
        public uint Unknown02 { get => _unknown02; set => SetField(ref _unknown02, value); }
        public uint Unknown03 { get => _unknown03; set => SetField(ref _unknown03, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint Unknown06 { get => _unknown06; set => SetField(ref _unknown06, value); }
        public uint Unknown07 { get => _unknown07; set => SetField(ref _unknown07, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Unknown0A { get => _unknown0A; set => SetField(ref _unknown0A, value); }
        public uint Unknown0B { get => _unknown0B; set => SetField(ref _unknown0B, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_ballte_talk_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 12; // FE6 uses 12-byte blocks
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u16(addr);
                if (unit == 0 || unit == 0xFFFF) break;

                string atkName = NameResolver.GetUnitName(rom.u8(addr));
                string defName = NameResolver.GetUnitName(rom.u8(addr + 1));
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            AttackerUnit = v["B0"];
            DefenderUnit = v["B1"];
            Unknown02 = v["B2"];
            Unknown03 = v["B3"];
            Text = v["W4"];
            Unknown06 = v["B6"];
            Unknown07 = v["B7"];
            AchievementFlag = v["W8"];
            Unknown0A = v["B10"];
            Unknown0B = v["B11"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = AttackerUnit, ["B1"] = DefenderUnit,
                ["B2"] = Unknown02, ["B3"] = Unknown03,
                ["W4"] = Text, ["B6"] = Unknown06, ["B7"] = Unknown07,
                ["W8"] = AchievementFlag, ["B10"] = Unknown0A, ["B11"] = Unknown0B,
            };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AttackerUnit"] = $"0x{AttackerUnit:X02}",
                ["DefenderUnit"] = $"0x{DefenderUnit:X02}",
                ["Unknown02"] = $"0x{Unknown02:X02}",
                ["Unknown03"] = $"0x{Unknown03:X02}",
                ["Text"] = $"0x{Text:X04}",
                ["Unknown06"] = $"0x{Unknown06:X02}",
                ["Unknown07"] = $"0x{Unknown07:X02}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Unknown0A"] = $"0x{Unknown0A:X02}",
                ["Unknown0B"] = $"0x{Unknown0B:X02}",
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
                ["u8@0x00_AttackerUnit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_DefenderUnit"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown03"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04_Text"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06_Unknown06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_Unknown07"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08_AchievementFlag"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_Unknown0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Unknown0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
