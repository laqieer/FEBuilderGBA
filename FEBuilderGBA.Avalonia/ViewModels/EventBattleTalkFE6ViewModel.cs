using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkFE6ViewModel : ViewModelBase, IDataVerifiable
    {
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
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Battle Dialogue (FE6)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            AttackerUnit = rom.u8(addr + 0);
            DefenderUnit = rom.u8(addr + 1);
            Unknown02 = rom.u8(addr + 2);
            Unknown03 = rom.u8(addr + 3);
            Text = rom.u16(addr + 4);
            Unknown06 = rom.u8(addr + 6);
            Unknown07 = rom.u8(addr + 7);
            AchievementFlag = rom.u16(addr + 8);
            Unknown0A = rom.u8(addr + 10);
            Unknown0B = rom.u8(addr + 11);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            rom.write_u8(a + 0, (byte)AttackerUnit);
            rom.write_u8(a + 1, (byte)DefenderUnit);
            rom.write_u8(a + 2, (byte)Unknown02);
            rom.write_u8(a + 3, (byte)Unknown03);
            rom.write_u16(a + 4, (ushort)Text);
            rom.write_u8(a + 6, (byte)Unknown06);
            rom.write_u8(a + 7, (byte)Unknown07);
            rom.write_u16(a + 8, (ushort)AchievementFlag);
            rom.write_u8(a + 10, (byte)Unknown0A);
            rom.write_u8(a + 11, (byte)Unknown0B);
        }

        public int GetListCount() => 0;

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
