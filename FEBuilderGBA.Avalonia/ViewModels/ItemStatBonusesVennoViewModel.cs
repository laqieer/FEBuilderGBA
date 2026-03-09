using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemStatBonusesVennoViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        // Stat bonus fields (row 1)
        int _hp, _str, _skill, _speed, _def, _res, _luck, _move, _con;
        // Growth rate bonus fields (row 2)
        int _growHP, _growStr, _growSkill, _growSpeed, _growDef, _growRes;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // Stat bonuses (byte 0-8)
        public int HP { get => _hp; set => SetField(ref _hp, value); }
        public int Str { get => _str; set => SetField(ref _str, value); }
        public int Skill { get => _skill; set => SetField(ref _skill, value); }
        public int Speed { get => _speed; set => SetField(ref _speed, value); }
        public int Def { get => _def; set => SetField(ref _def, value); }
        public int Res { get => _res; set => SetField(ref _res, value); }
        public int Luck { get => _luck; set => SetField(ref _luck, value); }
        public int Move { get => _move; set => SetField(ref _move, value); }
        public int Con { get => _con; set => SetField(ref _con, value); }

        // Growth rate bonuses (byte 9-14)
        public int GrowHP { get => _growHP; set => SetField(ref _growHP, value); }
        public int GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public int GrowSkill { get => _growSkill; set => SetField(ref _growSkill, value); }
        public int GrowSpeed { get => _growSpeed; set => SetField(ref _growSpeed, value); }
        public int GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public int GrowRes { get => _growRes; set => SetField(ref _growRes, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Stat Bonuses (Venno)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 14 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            // Stat bonuses (signed bytes)
            HP = (int)(sbyte)rom.u8(addr + 0);
            Str = (int)(sbyte)rom.u8(addr + 1);
            Skill = (int)(sbyte)rom.u8(addr + 2);
            Speed = (int)(sbyte)rom.u8(addr + 3);
            Def = (int)(sbyte)rom.u8(addr + 4);
            Res = (int)(sbyte)rom.u8(addr + 5);
            Luck = (int)(sbyte)rom.u8(addr + 6);
            Move = (int)(sbyte)rom.u8(addr + 7);
            Con = (int)(sbyte)rom.u8(addr + 8);

            // Growth rate bonuses (signed bytes)
            GrowHP = (int)(sbyte)rom.u8(addr + 9);
            GrowStr = (int)(sbyte)rom.u8(addr + 10);
            GrowSkill = (int)(sbyte)rom.u8(addr + 11);
            GrowSpeed = (int)(sbyte)rom.u8(addr + 12);
            GrowDef = (int)(sbyte)rom.u8(addr + 13);
            GrowRes = (int)(sbyte)rom.u8(addr + 14);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, (byte)(sbyte)HP);
            rom.write_u8(addr + 1, (byte)(sbyte)Str);
            rom.write_u8(addr + 2, (byte)(sbyte)Skill);
            rom.write_u8(addr + 3, (byte)(sbyte)Speed);
            rom.write_u8(addr + 4, (byte)(sbyte)Def);
            rom.write_u8(addr + 5, (byte)(sbyte)Res);
            rom.write_u8(addr + 6, (byte)(sbyte)Luck);
            rom.write_u8(addr + 7, (byte)(sbyte)Move);
            rom.write_u8(addr + 8, (byte)(sbyte)Con);

            rom.write_u8(addr + 9, (byte)(sbyte)GrowHP);
            rom.write_u8(addr + 10, (byte)(sbyte)GrowStr);
            rom.write_u8(addr + 11, (byte)(sbyte)GrowSkill);
            rom.write_u8(addr + 12, (byte)(sbyte)GrowSpeed);
            rom.write_u8(addr + 13, (byte)(sbyte)GrowDef);
            rom.write_u8(addr + 14, (byte)(sbyte)GrowRes);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["HP"] = $"0x{(byte)HP:X02}",
                ["Str"] = $"0x{(byte)Str:X02}",
                ["Skill"] = $"0x{(byte)Skill:X02}",
                ["Speed"] = $"0x{(byte)Speed:X02}",
                ["Def"] = $"0x{(byte)Def:X02}",
                ["Res"] = $"0x{(byte)Res:X02}",
                ["Luck"] = $"0x{(byte)Luck:X02}",
                ["Move"] = $"0x{(byte)Move:X02}",
                ["Con"] = $"0x{(byte)Con:X02}",
                ["GrowHP"] = $"0x{(byte)GrowHP:X02}",
                ["GrowStr"] = $"0x{(byte)GrowStr:X02}",
                ["GrowSkill"] = $"0x{(byte)GrowSkill:X02}",
                ["GrowSpeed"] = $"0x{(byte)GrowSpeed:X02}",
                ["GrowDef"] = $"0x{(byte)GrowDef:X02}",
                ["GrowRes"] = $"0x{(byte)GrowRes:X02}",
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
                ["u8@0x00_HP"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Str"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Skill"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Speed"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04_Def"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_Res"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06_Luck"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_Move"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08_Con"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_GrowHP"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_GrowStr"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_GrowSkill"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_GrowSpeed"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_GrowDef"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_GrowRes"] = $"0x{rom.u8(a + 14):X02}",
            };
        }
    }
}
