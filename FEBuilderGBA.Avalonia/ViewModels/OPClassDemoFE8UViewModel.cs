using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE8U variant).
    /// Data: op_class_demo_pointer, datasize=20, classId at offset 5.
    /// Validation: u8(addr+0x0F) must be &lt;= 6.
    /// </summary>
    public class OPClassDemoFE8UViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "B4", "B5", "B6", "B7", "W8", "W10", "B12", "B13", "B14", "B15", "D16" });

        uint _currentAddr;
        bool _canWrite;
        string _unavailableMessage = "";
        uint _descriptionTextId;
        uint _displayWeapon;
        uint _classId;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _terrainLeft;
        uint _terrainRight;
        uint _magicEffect;
        uint _unknown13;
        uint _unknown14;
        uint _animeType;
        uint _animePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public uint DisplayWeapon { get => _displayWeapon; set => SetField(ref _displayWeapon, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown13 { get => _unknown13; set => SetField(ref _unknown13, value); }
        public uint Unknown14 { get => _unknown14; set => SetField(ref _unknown14, value); }
        public uint AnimeType { get => _animeType; set => SetField(ref _animeType, value); }
        public uint AnimePointer { get => _animePointer; set => SetField(ref _animePointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_demo_pointer;
            if (baseAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // datasize=20, validate u8(addr+0xF) <= 6
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                uint animCheck = rom.u8(addr + 0x0F);
                if (animCheck > 6) break;

                uint cid = rom.u8(addr + 5);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(cid)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            DescriptionTextId = v["D0"];
            DisplayWeapon = v["B4"];
            ClassId = v["B5"];
            AllyEnemyColor = v["B6"];
            BattleAnime = v["B7"];
            TerrainLeft = v["W8"];
            TerrainRight = v["W10"];
            MagicEffect = v["B12"];
            Unknown13 = v["B13"];
            Unknown14 = v["B14"];
            AnimeType = v["B15"];
            AnimePointer = v["D16"];
            CanWrite = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["D0"] = DescriptionTextId, ["B4"] = DisplayWeapon, ["B5"] = ClassId,
                ["B6"] = AllyEnemyColor, ["B7"] = BattleAnime, ["W8"] = TerrainLeft,
                ["W10"] = TerrainRight, ["B12"] = MagicEffect, ["B13"] = Unknown13,
                ["B14"] = Unknown14, ["B15"] = AnimeType, ["D16"] = AnimePointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X08}",
                ["DisplayWeapon"] = $"0x{DisplayWeapon:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X04}",
                ["TerrainRight"] = $"0x{TerrainRight:X04}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown13"] = $"0x{Unknown13:X02}",
                ["Unknown14"] = $"0x{Unknown14:X02}",
                ["AnimeType"] = $"0x{AnimeType:X02}",
                ["AnimePointer"] = $"0x{AnimePointer:X08}",
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
                ["u32@0x00_DescriptionTextId"] = $"0x{rom.u32(a + 0):X08}",
                ["u8@0x04_DisplayWeapon"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_ClassId"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06_AllyEnemyColor"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_BattleAnime"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08_TerrainLeft"] = $"0x{rom.u16(a + 8):X04}",
                ["u16@0x0A_TerrainRight"] = $"0x{rom.u16(a + 10):X04}",
                ["u8@0x0C_MagicEffect"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_Unknown13"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_Unknown14"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_AnimeType"] = $"0x{rom.u8(a + 15):X02}",
                ["u32@0x10_AnimePointer"] = $"0x{rom.u32(a + 16):X08}",
            };
        }
    }
}
