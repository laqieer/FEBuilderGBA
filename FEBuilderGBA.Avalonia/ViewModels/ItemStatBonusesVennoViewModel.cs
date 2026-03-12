using System.Collections.Generic;
using System.Linq;
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

        // EditorFormRef field definitions (S = signed byte)
        static readonly string[] FieldNames = new[]
        {
            "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8",
            "S9", "S10", "S11", "S12", "S13", "S14"
        };
        static readonly List<EditorFormRef.FieldDef> Fields = EditorFormRef.DetectFields(FieldNames);

        // Map field names to property accessors
        static readonly string[] PropOrder = new[]
        {
            "HP", "Str", "Skill", "Speed", "Def", "Res", "Luck", "Move", "Con",
            "GrowHP", "GrowStr", "GrowSkill", "GrowSpeed", "GrowDef", "GrowRes"
        };

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
            var values = EditorFormRef.ReadFields(rom, addr, Fields);

            // Map field values to properties (SByte values come as sign-extended uint)
            HP = (int)values["S0"];
            Str = (int)values["S1"];
            Skill = (int)values["S2"];
            Speed = (int)values["S3"];
            Def = (int)values["S4"];
            Res = (int)values["S5"];
            Luck = (int)values["S6"];
            Move = (int)values["S7"];
            Con = (int)values["S8"];

            GrowHP = (int)values["S9"];
            GrowStr = (int)values["S10"];
            GrowSkill = (int)values["S11"];
            GrowSpeed = (int)values["S12"];
            GrowDef = (int)values["S13"];
            GrowRes = (int)values["S14"];

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            var values = new Dictionary<string, uint>
            {
                ["S0"] = (uint)HP,
                ["S1"] = (uint)Str,
                ["S2"] = (uint)Skill,
                ["S3"] = (uint)Speed,
                ["S4"] = (uint)Def,
                ["S5"] = (uint)Res,
                ["S6"] = (uint)Luck,
                ["S7"] = (uint)Move,
                ["S8"] = (uint)Con,
                ["S9"] = (uint)GrowHP,
                ["S10"] = (uint)GrowStr,
                ["S11"] = (uint)GrowSkill,
                ["S12"] = (uint)GrowSpeed,
                ["S13"] = (uint)GrowDef,
                ["S14"] = (uint)GrowRes,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, Fields);
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
