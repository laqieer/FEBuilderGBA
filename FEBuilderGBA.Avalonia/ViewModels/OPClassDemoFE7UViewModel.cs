using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7U variant).
    /// Data: op_class_demo_pointer, datasize=28, classId at offset 11.
    /// </summary>
    public class OPClassDemoFE7UViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _unavailableMessage = "";
        uint _englishNamePointer;
        uint _descriptionTextId;
        uint _japaneseNamePointer;
        uint _japaneseNameLength;
        uint _classId;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown15;
        uint _unknown16;
        uint _unknown17;
        uint _unknown19;
        uint _terrainLeft;
        uint _terrainRight;
        uint _unknown22;
        uint _unknown23;
        uint _animePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint EnglishNamePointer { get => _englishNamePointer; set => SetField(ref _englishNamePointer, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown15 { get => _unknown15; set => SetField(ref _unknown15, value); }
        public uint Unknown16 { get => _unknown16; set => SetField(ref _unknown16, value); }
        public uint Unknown17 { get => _unknown17; set => SetField(ref _unknown17, value); }
        public uint Unknown19 { get => _unknown19; set => SetField(ref _unknown19, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint Unknown22 { get => _unknown22; set => SetField(ref _unknown22, value); }
        public uint Unknown23 { get => _unknown23; set => SetField(ref _unknown23, value); }
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
            // datasize=28, up to 0x42 entries
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                uint cid = rom.u8(addr + 11);
                string name = U.ToHexString(cid) + " Class Demo (FE7U)";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EnglishNamePointer = rom.u32(addr + 0);
            DescriptionTextId = rom.u16(addr + 4);
            JapaneseNamePointer = rom.u16(addr + 8);
            JapaneseNameLength = rom.u8(addr + 10);
            ClassId = rom.u8(addr + 11);
            AllyEnemyColor = rom.u8(addr + 12);
            BattleAnime = rom.u8(addr + 13);
            MagicEffect = rom.u8(addr + 14);
            Unknown15 = rom.u8(addr + 15);
            Unknown16 = rom.u8(addr + 16);
            Unknown17 = rom.u8(addr + 17);
            Unknown19 = rom.u8(addr + 19);
            TerrainLeft = rom.u8(addr + 20);
            TerrainRight = rom.u8(addr + 21);
            Unknown22 = rom.u8(addr + 22);
            Unknown23 = rom.u8(addr + 23);
            AnimePointer = rom.u32(addr + 24);
            CanWrite = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, EnglishNamePointer);
            rom.write_u16(addr + 4, (ushort)DescriptionTextId);
            rom.write_u16(addr + 8, (ushort)JapaneseNamePointer);
            rom.write_u8(addr + 10, (byte)JapaneseNameLength);
            rom.write_u8(addr + 11, (byte)ClassId);
            rom.write_u8(addr + 12, (byte)AllyEnemyColor);
            rom.write_u8(addr + 13, (byte)BattleAnime);
            rom.write_u8(addr + 14, (byte)MagicEffect);
            rom.write_u8(addr + 15, (byte)Unknown15);
            rom.write_u8(addr + 16, (byte)Unknown16);
            rom.write_u8(addr + 17, (byte)Unknown17);
            rom.write_u8(addr + 19, (byte)Unknown19);
            rom.write_u8(addr + 20, (byte)TerrainLeft);
            rom.write_u8(addr + 21, (byte)TerrainRight);
            rom.write_u8(addr + 22, (byte)Unknown22);
            rom.write_u8(addr + 23, (byte)Unknown23);
            rom.write_u32(addr + 24, AnimePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EnglishNamePointer"] = $"0x{EnglishNamePointer:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X04}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X04}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown15"] = $"0x{Unknown15:X02}",
                ["Unknown16"] = $"0x{Unknown16:X02}",
                ["Unknown17"] = $"0x{Unknown17:X02}",
                ["Unknown19"] = $"0x{Unknown19:X02}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
                ["Unknown22"] = $"0x{Unknown22:X02}",
                ["Unknown23"] = $"0x{Unknown23:X02}",
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
                ["u16@0x04_DescriptionTextId"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x08_JapaneseNamePointer"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_JapaneseNameLength"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_ClassId"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_AllyEnemyColor"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_BattleAnime"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_MagicEffect"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown15"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_Unknown16"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_Unknown17"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x13_Unknown19"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14_TerrainLeft"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_TerrainRight"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_Unknown22"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_Unknown23"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18_AnimePointer"] = $"0x{rom.u32(a + 24):X08}",
            };
        }
    }
}
