using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleDataFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _attackType;
        uint _attacker;
        uint _damage;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackType { get => _attackType; set => SetField(ref _attackType, value); }
        public uint Attacker { get => _attacker; set => SetField(ref _attacker, value); }
        public uint Damage { get => _damage; set => SetField(ref _damage, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Battle Data (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            AttackType = rom.u16(addr + 0);
            Attacker = rom.u8(addr + 2);
            Damage = rom.u8(addr + 3);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            rom.write_u16(a + 0, (ushort)AttackType);
            rom.write_u8(a + 2, (byte)Attacker);
            rom.write_u8(a + 3, (byte)Damage);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AttackType"] = $"0x{AttackType:X04}",
                ["Attacker"] = $"0x{Attacker:X02}",
                ["Damage"] = $"0x{Damage:X02}",
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
                ["u16@0x00_AttackType"] = $"0x{rom.u16(a + 0):X04}",
                ["u8@0x02_Attacker"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Damage"] = $"0x{rom.u8(a + 3):X02}",
            };
        }
    }
}
