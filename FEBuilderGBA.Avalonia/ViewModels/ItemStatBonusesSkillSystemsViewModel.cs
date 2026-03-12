using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemStatBonusesSkillSystemsViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        // Stat bonus fields (row 1)
        int _hp, _str, _skill, _speed, _def, _res, _luck, _move, _con, _magicOrUnknown;
        // Growth rate bonus fields (row 2)
        int _growHP, _growStr, _growSkill, _growSpeed, _growDef, _growRes, _growLuck, _growUnknown;
        // Padding fields (row 3)
        int _padding1, _padding2;

        // EditorFormRef field definitions (S = signed byte)
        static readonly string[] FieldNames = new[]
        {
            "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9",
            "S10", "S11", "S12", "S13", "S14", "S15", "S16", "S17",
            "S18", "S19"
        };
        static readonly List<EditorFormRef.FieldDef> Fields = EditorFormRef.DetectFields(FieldNames);

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // Stat bonuses (byte 0-9)
        public int HP { get => _hp; set => SetField(ref _hp, value); }
        public int Str { get => _str; set => SetField(ref _str, value); }
        public int Skill { get => _skill; set => SetField(ref _skill, value); }
        public int Speed { get => _speed; set => SetField(ref _speed, value); }
        public int Def { get => _def; set => SetField(ref _def, value); }
        public int Res { get => _res; set => SetField(ref _res, value); }
        public int Luck { get => _luck; set => SetField(ref _luck, value); }
        public int Move { get => _move; set => SetField(ref _move, value); }
        public int Con { get => _con; set => SetField(ref _con, value); }
        public int MagicOrUnknown { get => _magicOrUnknown; set => SetField(ref _magicOrUnknown, value); }

        // Growth rate bonuses (byte 10-17)
        public int GrowHP { get => _growHP; set => SetField(ref _growHP, value); }
        public int GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public int GrowSkill { get => _growSkill; set => SetField(ref _growSkill, value); }
        public int GrowSpeed { get => _growSpeed; set => SetField(ref _growSpeed, value); }
        public int GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public int GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public int GrowLuck { get => _growLuck; set => SetField(ref _growLuck, value); }
        public int GrowUnknown { get => _growUnknown; set => SetField(ref _growUnknown, value); }

        // Padding (byte 18-19)
        public int Padding1 { get => _padding1; set => SetField(ref _padding1, value); }
        public int Padding2 { get => _padding2; set => SetField(ref _padding2, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Stat Bonuses (Skill Systems)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 19 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, Fields);

            // Stat bonuses (signed bytes)
            HP = (int)values["S0"];
            Str = (int)values["S1"];
            Skill = (int)values["S2"];
            Speed = (int)values["S3"];
            Def = (int)values["S4"];
            Res = (int)values["S5"];
            Luck = (int)values["S6"];
            Move = (int)values["S7"];
            Con = (int)values["S8"];
            MagicOrUnknown = (int)values["S9"];

            // Growth rate bonuses (signed bytes)
            GrowHP = (int)values["S10"];
            GrowStr = (int)values["S11"];
            GrowSkill = (int)values["S12"];
            GrowSpeed = (int)values["S13"];
            GrowDef = (int)values["S14"];
            GrowRes = (int)values["S15"];
            GrowLuck = (int)values["S16"];
            GrowUnknown = (int)values["S17"];

            // Padding
            Padding1 = (int)values["S18"];
            Padding2 = (int)values["S19"];

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
                ["S9"] = (uint)MagicOrUnknown,
                ["S10"] = (uint)GrowHP,
                ["S11"] = (uint)GrowStr,
                ["S12"] = (uint)GrowSkill,
                ["S13"] = (uint)GrowSpeed,
                ["S14"] = (uint)GrowDef,
                ["S15"] = (uint)GrowRes,
                ["S16"] = (uint)GrowLuck,
                ["S17"] = (uint)GrowUnknown,
                ["S18"] = (uint)Padding1,
                ["S19"] = (uint)Padding2,
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
                ["MagicOrUnknown"] = $"0x{(byte)MagicOrUnknown:X02}",
                ["GrowHP"] = $"0x{(byte)GrowHP:X02}",
                ["GrowStr"] = $"0x{(byte)GrowStr:X02}",
                ["GrowSkill"] = $"0x{(byte)GrowSkill:X02}",
                ["GrowSpeed"] = $"0x{(byte)GrowSpeed:X02}",
                ["GrowDef"] = $"0x{(byte)GrowDef:X02}",
                ["GrowRes"] = $"0x{(byte)GrowRes:X02}",
                ["GrowLuck"] = $"0x{(byte)GrowLuck:X02}",
                ["GrowUnknown"] = $"0x{(byte)GrowUnknown:X02}",
                ["Padding1"] = $"0x{(byte)Padding1:X02}",
                ["Padding2"] = $"0x{(byte)Padding2:X02}",
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
                ["u8@0x09_MagicOrUnknown"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_GrowHP"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_GrowStr"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_GrowSkill"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_GrowSpeed"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_GrowDef"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_GrowRes"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_GrowLuck"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_GrowUnknown"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12_Padding1"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13_Padding2"] = $"0x{rom.u8(a + 19):X02}",
            };
        }
    }
}
