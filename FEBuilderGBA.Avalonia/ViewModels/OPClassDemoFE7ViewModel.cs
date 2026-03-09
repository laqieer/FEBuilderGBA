using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7 Japanese / FE6 variant).
    /// Data: op_class_demo_pointer, datasize=32, classId at offset 15.
    /// Used by FE6 (MainFE6Form opens OPClassDemoFE7Form).
    /// </summary>
    public class OPClassDemoFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _unavailableMessage = "";
        uint _englishNamePointer;
        uint _descriptionTextIdLow;
        uint _descriptionTextIdHigh;
        uint _japaneseNamePointer;
        uint _japaneseNameLength;
        uint _paletteId;
        uint _displayWeapon;
        uint _classId;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown19;
        uint _unknown20;
        uint _unknown21;
        uint _terrainLeft;
        uint _terrainRight;
        uint _unknown24;
        uint _unknown25;
        uint _unknown26;
        uint _unknown27;
        uint _animePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint EnglishNamePointer { get => _englishNamePointer; set => SetField(ref _englishNamePointer, value); }
        public uint DescriptionTextIdLow { get => _descriptionTextIdLow; set => SetField(ref _descriptionTextIdLow, value); }
        public uint DescriptionTextIdHigh { get => _descriptionTextIdHigh; set => SetField(ref _descriptionTextIdHigh, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint PaletteId { get => _paletteId; set => SetField(ref _paletteId, value); }
        public uint DisplayWeapon { get => _displayWeapon; set => SetField(ref _displayWeapon, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown19 { get => _unknown19; set => SetField(ref _unknown19, value); }
        public uint Unknown20 { get => _unknown20; set => SetField(ref _unknown20, value); }
        public uint Unknown21 { get => _unknown21; set => SetField(ref _unknown21, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint Unknown24 { get => _unknown24; set => SetField(ref _unknown24, value); }
        public uint Unknown25 { get => _unknown25; set => SetField(ref _unknown25, value); }
        public uint Unknown26 { get => _unknown26; set => SetField(ref _unknown26, value); }
        public uint Unknown27 { get => _unknown27; set => SetField(ref _unknown27, value); }
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
            // datasize=32, up to 0x42 entries
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                uint cid = rom.u8(addr + 15);
                string name = U.ToHexString(cid) + " Class Demo (FE7)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EnglishNamePointer = rom.u32(addr + 0);
            DescriptionTextIdLow = rom.u16(addr + 4);
            DescriptionTextIdHigh = rom.u16(addr + 6);
            JapaneseNamePointer = rom.u32(addr + 8);
            JapaneseNameLength = rom.u8(addr + 12);
            PaletteId = rom.u8(addr + 13);
            DisplayWeapon = rom.u8(addr + 14);
            ClassId = rom.u8(addr + 15);
            AllyEnemyColor = rom.u8(addr + 16);
            BattleAnime = rom.u8(addr + 17);
            MagicEffect = rom.u8(addr + 18);
            Unknown19 = rom.u8(addr + 19);
            Unknown20 = rom.u8(addr + 20);
            Unknown21 = rom.u8(addr + 21);
            TerrainLeft = rom.u8(addr + 22);
            TerrainRight = rom.u8(addr + 23);
            Unknown24 = rom.u8(addr + 24);
            Unknown25 = rom.u8(addr + 25);
            Unknown26 = rom.u8(addr + 26);
            Unknown27 = rom.u8(addr + 27);
            AnimePointer = rom.u32(addr + 28);
            CanWrite = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, EnglishNamePointer);
            rom.write_u16(addr + 4, (ushort)DescriptionTextIdLow);
            rom.write_u16(addr + 6, (ushort)DescriptionTextIdHigh);
            rom.write_u32(addr + 8, JapaneseNamePointer);
            rom.write_u8(addr + 12, (byte)JapaneseNameLength);
            rom.write_u8(addr + 13, (byte)PaletteId);
            rom.write_u8(addr + 14, (byte)DisplayWeapon);
            rom.write_u8(addr + 15, (byte)ClassId);
            rom.write_u8(addr + 16, (byte)AllyEnemyColor);
            rom.write_u8(addr + 17, (byte)BattleAnime);
            rom.write_u8(addr + 18, (byte)MagicEffect);
            rom.write_u8(addr + 19, (byte)Unknown19);
            rom.write_u8(addr + 20, (byte)Unknown20);
            rom.write_u8(addr + 21, (byte)Unknown21);
            rom.write_u8(addr + 22, (byte)TerrainLeft);
            rom.write_u8(addr + 23, (byte)TerrainRight);
            rom.write_u8(addr + 24, (byte)Unknown24);
            rom.write_u8(addr + 25, (byte)Unknown25);
            rom.write_u8(addr + 26, (byte)Unknown26);
            rom.write_u8(addr + 27, (byte)Unknown27);
            rom.write_u32(addr + 28, AnimePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EnglishNamePointer"] = $"0x{EnglishNamePointer:X08}",
                ["DescriptionTextIdLow"] = $"0x{DescriptionTextIdLow:X04}",
                ["DescriptionTextIdHigh"] = $"0x{DescriptionTextIdHigh:X04}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X08}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["PaletteId"] = $"0x{PaletteId:X02}",
                ["DisplayWeapon"] = $"0x{DisplayWeapon:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown19"] = $"0x{Unknown19:X02}",
                ["Unknown20"] = $"0x{Unknown20:X02}",
                ["Unknown21"] = $"0x{Unknown21:X02}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
                ["Unknown24"] = $"0x{Unknown24:X02}",
                ["Unknown25"] = $"0x{Unknown25:X02}",
                ["Unknown26"] = $"0x{Unknown26:X02}",
                ["Unknown27"] = $"0x{Unknown27:X02}",
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
                ["u32@0x00_EnglishNamePointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04_DescriptionTextIdLow"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06_DescriptionTextIdHigh"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@0x08_JapaneseNamePointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C_JapaneseNameLength"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_PaletteId"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_DisplayWeapon"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_ClassId"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_AllyEnemyColor"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_BattleAnime"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12_MagicEffect"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13_Unknown19"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14_Unknown20"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_Unknown21"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_TerrainLeft"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_TerrainRight"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18_Unknown24"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19_Unknown25"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A_Unknown26"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B_Unknown27"] = $"0x{rom.u8(a + 27):X02}",
                ["u32@0x1C_AnimePointer"] = $"0x{rom.u32(a + 28):X08}",
            };
        }
    }
}
