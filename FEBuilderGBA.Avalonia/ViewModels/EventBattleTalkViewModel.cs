using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _attackerUnit;
        uint _defenderUnit;
        uint _map;
        uint _unknown05;
        uint _achievementFlag;
        uint _text;
        uint _unknown0A;
        uint _unknown0B;
        uint _eventPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackerUnit { get => _attackerUnit; set => SetField(ref _attackerUnit, value); }
        public uint DefenderUnit { get => _defenderUnit; set => SetField(ref _defenderUnit, value); }
        public uint Map { get => _map; set => SetField(ref _map, value); }
        public uint Unknown05 { get => _unknown05; set => SetField(ref _unknown05, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint Unknown0A { get => _unknown0A; set => SetField(ref _unknown0A, value); }
        public uint Unknown0B { get => _unknown0B; set => SetField(ref _unknown0B, value); }
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_ballte_talk_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;

                uint attacker = rom.u16(addr);
                uint defender = rom.u16(addr + 2);
                string atkName = NameResolver.GetUnitName(attacker);
                string defName = NameResolver.GetUnitName(defender);
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            AttackerUnit = rom.u16(addr + 0);
            DefenderUnit = rom.u16(addr + 2);
            Map = rom.u8(addr + 4);
            Unknown05 = rom.u8(addr + 5);
            AchievementFlag = rom.u16(addr + 6);
            Text = rom.u16(addr + 8);
            Unknown0A = rom.u8(addr + 10);
            Unknown0B = rom.u8(addr + 11);
            EventPointer = rom.u32(addr + 12);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            rom.write_u16(a + 0, (ushort)AttackerUnit);
            rom.write_u16(a + 2, (ushort)DefenderUnit);
            rom.write_u8(a + 4, (byte)Map);
            rom.write_u8(a + 5, (byte)Unknown05);
            rom.write_u16(a + 6, (ushort)AchievementFlag);
            rom.write_u16(a + 8, (ushort)Text);
            rom.write_u8(a + 10, (byte)Unknown0A);
            rom.write_u8(a + 11, (byte)Unknown0B);
            rom.write_u32(a + 12, EventPointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AttackerUnit"] = $"0x{AttackerUnit:X04}",
                ["DefenderUnit"] = $"0x{DefenderUnit:X04}",
                ["Map"] = $"0x{Map:X02}",
                ["Unknown05"] = $"0x{Unknown05:X02}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Text"] = $"0x{Text:X04}",
                ["Unknown0A"] = $"0x{Unknown0A:X02}",
                ["Unknown0B"] = $"0x{Unknown0B:X02}",
                ["EventPointer"] = $"0x{EventPointer:X08}",
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
                ["u16@0x00_AttackerUnit"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02_DefenderUnit"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04_Map"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_Unknown05"] = $"0x{rom.u8(a + 5):X02}",
                ["u16@0x06_AchievementFlag"] = $"0x{rom.u16(a + 6):X04}",
                ["u16@0x08_Text"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_Unknown0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Unknown0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C_EventPointer"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
