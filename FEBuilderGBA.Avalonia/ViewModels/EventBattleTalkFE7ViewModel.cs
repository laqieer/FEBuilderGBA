using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkFE7ViewModel : ViewModelBase, IDataVerifiable
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
        uint _eventPointer;
        uint _achievementFlag;
        uint _unknown0E;
        uint _unknown0F;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackerUnit { get => _attackerUnit; set => SetField(ref _attackerUnit, value); }
        public uint DefenderUnit { get => _defenderUnit; set => SetField(ref _defenderUnit, value); }
        public uint Unknown02 { get => _unknown02; set => SetField(ref _unknown02, value); }
        public uint Unknown03 { get => _unknown03; set => SetField(ref _unknown03, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint Unknown06 { get => _unknown06; set => SetField(ref _unknown06, value); }
        public uint Unknown07 { get => _unknown07; set => SetField(ref _unknown07, value); }
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Unknown0E { get => _unknown0E; set => SetField(ref _unknown0E, value); }
        public uint Unknown0F { get => _unknown0F; set => SetField(ref _unknown0F, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_ballte_talk_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 16; // FE7 uses 16-byte blocks
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
            result.Add(new AddrResult(0, "Battle Dialogue (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            AttackerUnit = rom.u8(addr + 0);
            DefenderUnit = rom.u8(addr + 1);
            Unknown02 = rom.u8(addr + 2);
            Unknown03 = rom.u8(addr + 3);
            Text = rom.u16(addr + 4);
            Unknown06 = rom.u8(addr + 6);
            Unknown07 = rom.u8(addr + 7);
            EventPointer = rom.u32(addr + 8);
            AchievementFlag = rom.u16(addr + 12);
            Unknown0E = rom.u8(addr + 14);
            Unknown0F = rom.u8(addr + 15);
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
            rom.write_u32(a + 8, EventPointer);
            rom.write_u16(a + 12, (ushort)AchievementFlag);
            rom.write_u8(a + 14, (byte)Unknown0E);
            rom.write_u8(a + 15, (byte)Unknown0F);
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
                ["EventPointer"] = $"0x{EventPointer:X08}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Unknown0E"] = $"0x{Unknown0E:X02}",
                ["Unknown0F"] = $"0x{Unknown0F:X02}",
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
                ["u32@0x08_EventPointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u16@0x0C_AchievementFlag"] = $"0x{rom.u16(a + 12):X04}",
                ["u8@0x0E_Unknown0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
