using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPClassDemoViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "B12", "B13", "B14", "B15", "B16", "B17", "D18", "B22", "B23", "D24" });

        uint _currentAddr;
        bool _canWrite;
        uint _englishNamePointer;
        uint _descriptionTextId;
        uint _japaneseNamePointer;
        uint _japaneseNameLength;
        uint _paletteId;
        uint _displayWeapon;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown18;
        uint _terrainLeft;
        uint _terrainRight;
        uint _animePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint EnglishNamePointer { get => _englishNamePointer; set => SetField(ref _englishNamePointer, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint PaletteId { get => _paletteId; set => SetField(ref _paletteId, value); }
        public uint DisplayWeapon { get => _displayWeapon; set => SetField(ref _displayWeapon, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown18 { get => _unknown18; set => SetField(ref _unknown18, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint AnimePointer { get => _animePointer; set => SetField(ref _animePointer, value); }

        public List<AddrResult> LoadOPClassDemoList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();

            // Double dereference (same as InputFormRef: p32 in caller + p32 in Init)
            uint baseAddr = rom.p32p(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                // First dword should be a valid pointer
                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;

                uint cid = rom.u8(addr + 14);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadOPClassDemo(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            EnglishNamePointer = v["D0"];
            DescriptionTextId = v["D4"];
            JapaneseNamePointer = v["D8"];
            JapaneseNameLength = v["B12"];
            PaletteId = v["B13"];
            DisplayWeapon = v["B14"];
            AllyEnemyColor = v["B15"];
            BattleAnime = v["B16"];
            MagicEffect = v["B17"];
            Unknown18 = v["D18"];
            TerrainLeft = v["B22"];
            TerrainRight = v["B23"];
            AnimePointer = v["D24"];
            CanWrite = true;
        }

        public void WriteOPClassDemo()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["D0"] = EnglishNamePointer, ["D4"] = DescriptionTextId, ["D8"] = JapaneseNamePointer,
                ["B12"] = JapaneseNameLength, ["B13"] = PaletteId, ["B14"] = DisplayWeapon,
                ["B15"] = AllyEnemyColor, ["B16"] = BattleAnime, ["B17"] = MagicEffect,
                ["D18"] = Unknown18, ["B22"] = TerrainLeft, ["B23"] = TerrainRight, ["D24"] = AnimePointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadOPClassDemoList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EnglishNamePointer"] = $"0x{EnglishNamePointer:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X08}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X08}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["PaletteId"] = $"0x{PaletteId:X02}",
                ["DisplayWeapon"] = $"0x{DisplayWeapon:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown18"] = $"0x{Unknown18:X08}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
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
                ["u32@0x04_DescriptionTextId"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_JapaneseNamePointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C_JapaneseNameLength"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_PaletteId"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_DisplayWeapon"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_AllyEnemyColor"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_BattleAnime"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_MagicEffect"] = $"0x{rom.u8(a + 17):X02}",
                ["u32@0x12_Unknown18"] = $"0x{rom.u32(a + 18):X08}",
                ["u8@0x16_TerrainLeft"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_TerrainRight"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18_AnimePointer"] = $"0x{rom.u32(a + 24):X08}",
            };
        }
    }
}
