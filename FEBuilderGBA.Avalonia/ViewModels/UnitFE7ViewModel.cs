using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _name = "";

        // Identity fields
        uint _nameId, _descId, _unitId, _classId;
        uint _portraitId, _mapFace, _affinity, _sortOrder, _level;

        // Base stats (signed bytes, offsets 12-19)
        int _hp, _str, _skl, _spd, _def, _res, _lck, _con;

        // Weapon ranks (offsets 20-27)
        uint _wepSword, _wepLance, _wepAxe, _wepBow;
        uint _wepStaff, _wepAnima, _wepLight, _wepDark;

        // Growth rates (offsets 28-34)
        uint _growHP, _growSTR, _growSKL, _growSPD, _growDEF, _growRES, _growLCK;

        // Palette & custom anime (offsets 35-38)
        uint _lowerClassPalette, _upperClassPalette;
        uint _lowerClassAnime, _upperClassAnime;

        // Unknown (offset 39)
        uint _unk39;

        // Ability flags (offsets 40-43)
        uint _ability1, _ability2, _ability3, _ability4;

        // Support pointer (offset 44)
        uint _supportPtr;

        // Talk group & unknowns (offsets 48-51)
        uint _talkGroup, _unk49, _unk50, _unk51;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        // Identity
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint PortraitId { get => _portraitId; set => SetField(ref _portraitId, value); }
        public uint MapFace { get => _mapFace; set => SetField(ref _mapFace, value); }
        public uint Affinity { get => _affinity; set => SetField(ref _affinity, value); }
        public uint SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }

        // Base stats (signed)
        public int HP { get => _hp; set => SetField(ref _hp, value); }
        public int Str { get => _str; set => SetField(ref _str, value); }
        public int Skl { get => _skl; set => SetField(ref _skl, value); }
        public int Spd { get => _spd; set => SetField(ref _spd, value); }
        public int Def { get => _def; set => SetField(ref _def, value); }
        public int Res { get => _res; set => SetField(ref _res, value); }
        public int Lck { get => _lck; set => SetField(ref _lck, value); }
        public int Con { get => _con; set => SetField(ref _con, value); }

        // Weapon ranks
        public uint WepSword { get => _wepSword; set => SetField(ref _wepSword, value); }
        public uint WepLance { get => _wepLance; set => SetField(ref _wepLance, value); }
        public uint WepAxe { get => _wepAxe; set => SetField(ref _wepAxe, value); }
        public uint WepBow { get => _wepBow; set => SetField(ref _wepBow, value); }
        public uint WepStaff { get => _wepStaff; set => SetField(ref _wepStaff, value); }
        public uint WepAnima { get => _wepAnima; set => SetField(ref _wepAnima, value); }
        public uint WepLight { get => _wepLight; set => SetField(ref _wepLight, value); }
        public uint WepDark { get => _wepDark; set => SetField(ref _wepDark, value); }

        // Growth rates
        public uint GrowHP { get => _growHP; set => SetField(ref _growHP, value); }
        public uint GrowSTR { get => _growSTR; set => SetField(ref _growSTR, value); }
        public uint GrowSKL { get => _growSKL; set => SetField(ref _growSKL, value); }
        public uint GrowSPD { get => _growSPD; set => SetField(ref _growSPD, value); }
        public uint GrowDEF { get => _growDEF; set => SetField(ref _growDEF, value); }
        public uint GrowRES { get => _growRES; set => SetField(ref _growRES, value); }
        public uint GrowLCK { get => _growLCK; set => SetField(ref _growLCK, value); }

        // Palette assignment
        public uint LowerClassPalette { get => _lowerClassPalette; set => SetField(ref _lowerClassPalette, value); }
        public uint UpperClassPalette { get => _upperClassPalette; set => SetField(ref _upperClassPalette, value); }

        // Custom battle animation
        public uint LowerClassAnime { get => _lowerClassAnime; set => SetField(ref _lowerClassAnime, value); }
        public uint UpperClassAnime { get => _upperClassAnime; set => SetField(ref _upperClassAnime, value); }

        // Unknown 39
        public uint Unk39 { get => _unk39; set => SetField(ref _unk39, value); }

        // Ability flags
        public uint Ability1 { get => _ability1; set => SetField(ref _ability1, value); }
        public uint Ability2 { get => _ability2; set => SetField(ref _ability2, value); }
        public uint Ability3 { get => _ability3; set => SetField(ref _ability3, value); }
        public uint Ability4 { get => _ability4; set => SetField(ref _ability4, value); }

        // Support pointer
        public uint SupportPtr { get => _supportPtr; set => SetField(ref _supportPtr, value); }

        // Talk group & unknowns
        public uint TalkGroup { get => _talkGroup; set => SetField(ref _talkGroup, value); }
        public uint Unk49 { get => _unk49; set => SetField(ref _unk49, value); }
        public uint Unk50 { get => _unk50; set => SetField(ref _unk50, value); }
        public uint Unk51 { get => _unk51; set => SetField(ref _unk51, value); }

        public List<AddrResult> LoadUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 253;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Identity (u16 + u8 fields)
            NameId = rom.u16(addr + 0);
            DescId = rom.u16(addr + 2);
            UnitId = rom.u8(addr + 4);
            ClassId = rom.u8(addr + 5);
            PortraitId = rom.u16(addr + 6);
            MapFace = rom.u8(addr + 8);
            Affinity = rom.u8(addr + 9);
            SortOrder = rom.u8(addr + 10);
            Level = rom.u8(addr + 11);

            try { Name = NameResolver.GetTextById(NameId); }
            catch { Name = "???"; }

            // Base stats (signed byte)
            HP = (sbyte)rom.u8(addr + 12);
            Str = (sbyte)rom.u8(addr + 13);
            Skl = (sbyte)rom.u8(addr + 14);
            Spd = (sbyte)rom.u8(addr + 15);
            Def = (sbyte)rom.u8(addr + 16);
            Res = (sbyte)rom.u8(addr + 17);
            Lck = (sbyte)rom.u8(addr + 18);
            Con = (sbyte)rom.u8(addr + 19);

            // Weapon ranks
            WepSword = rom.u8(addr + 20);
            WepLance = rom.u8(addr + 21);
            WepAxe = rom.u8(addr + 22);
            WepBow = rom.u8(addr + 23);
            WepStaff = rom.u8(addr + 24);
            WepAnima = rom.u8(addr + 25);
            WepLight = rom.u8(addr + 26);
            WepDark = rom.u8(addr + 27);

            // Growth rates
            GrowHP = rom.u8(addr + 28);
            GrowSTR = rom.u8(addr + 29);
            GrowSKL = rom.u8(addr + 30);
            GrowSPD = rom.u8(addr + 31);
            GrowDEF = rom.u8(addr + 32);
            GrowRES = rom.u8(addr + 33);
            GrowLCK = rom.u8(addr + 34);

            // Palette & custom anime
            LowerClassPalette = rom.u8(addr + 35);
            UpperClassPalette = rom.u8(addr + 36);
            LowerClassAnime = rom.u8(addr + 37);
            UpperClassAnime = rom.u8(addr + 38);

            // Unknown 39
            Unk39 = rom.u8(addr + 39);

            // Ability flags
            Ability1 = rom.u8(addr + 40);
            Ability2 = rom.u8(addr + 41);
            Ability3 = rom.u8(addr + 42);
            Ability4 = rom.u8(addr + 43);

            // Support pointer
            SupportPtr = rom.u32(addr + 44);

            // Talk group & unknowns
            TalkGroup = rom.u8(addr + 48);
            Unk49 = rom.u8(addr + 49);
            Unk50 = rom.u8(addr + 50);
            Unk51 = rom.u8(addr + 51);

            CanWrite = true;
        }

        public void WriteUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            // Identity
            rom.write_u16(addr + 0, (ushort)NameId);
            rom.write_u16(addr + 2, (ushort)DescId);
            rom.write_u8(addr + 4, (byte)UnitId);
            rom.write_u8(addr + 5, (byte)ClassId);
            rom.write_u16(addr + 6, (ushort)PortraitId);
            rom.write_u8(addr + 8, (byte)MapFace);
            rom.write_u8(addr + 9, (byte)Affinity);
            rom.write_u8(addr + 10, (byte)SortOrder);
            rom.write_u8(addr + 11, (byte)Level);

            // Base stats
            rom.write_u8(addr + 12, (byte)(sbyte)HP);
            rom.write_u8(addr + 13, (byte)(sbyte)Str);
            rom.write_u8(addr + 14, (byte)(sbyte)Skl);
            rom.write_u8(addr + 15, (byte)(sbyte)Spd);
            rom.write_u8(addr + 16, (byte)(sbyte)Def);
            rom.write_u8(addr + 17, (byte)(sbyte)Res);
            rom.write_u8(addr + 18, (byte)(sbyte)Lck);
            rom.write_u8(addr + 19, (byte)(sbyte)Con);

            // Weapon ranks
            rom.write_u8(addr + 20, (byte)WepSword);
            rom.write_u8(addr + 21, (byte)WepLance);
            rom.write_u8(addr + 22, (byte)WepAxe);
            rom.write_u8(addr + 23, (byte)WepBow);
            rom.write_u8(addr + 24, (byte)WepStaff);
            rom.write_u8(addr + 25, (byte)WepAnima);
            rom.write_u8(addr + 26, (byte)WepLight);
            rom.write_u8(addr + 27, (byte)WepDark);

            // Growth rates
            rom.write_u8(addr + 28, (byte)GrowHP);
            rom.write_u8(addr + 29, (byte)GrowSTR);
            rom.write_u8(addr + 30, (byte)GrowSKL);
            rom.write_u8(addr + 31, (byte)GrowSPD);
            rom.write_u8(addr + 32, (byte)GrowDEF);
            rom.write_u8(addr + 33, (byte)GrowRES);
            rom.write_u8(addr + 34, (byte)GrowLCK);

            // Palette & custom anime
            rom.write_u8(addr + 35, (byte)LowerClassPalette);
            rom.write_u8(addr + 36, (byte)UpperClassPalette);
            rom.write_u8(addr + 37, (byte)LowerClassAnime);
            rom.write_u8(addr + 38, (byte)UpperClassAnime);

            // Unknown 39
            rom.write_u8(addr + 39, (byte)Unk39);

            // Ability flags
            rom.write_u8(addr + 40, (byte)Ability1);
            rom.write_u8(addr + 41, (byte)Ability2);
            rom.write_u8(addr + 42, (byte)Ability3);
            rom.write_u8(addr + 43, (byte)Ability4);

            // Support pointer
            rom.write_u32(addr + 44, SupportPtr);

            // Talk group & unknowns
            rom.write_u8(addr + 48, (byte)TalkGroup);
            rom.write_u8(addr + 49, (byte)Unk49);
            rom.write_u8(addr + 50, (byte)Unk50);
            rom.write_u8(addr + 51, (byte)Unk51);
        }

        public int GetListCount() => LoadUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2_DescId"] = $"0x{DescId:X04}",
                ["B4_UnitId"] = $"0x{UnitId:X02}",
                ["B5_ClassId"] = $"0x{ClassId:X02}",
                ["W6_PortraitId"] = $"0x{PortraitId:X04}",
                ["B8_MapFace"] = $"0x{MapFace:X02}",
                ["B9_Affinity"] = $"0x{Affinity:X02}",
                ["B10_SortOrder"] = $"0x{SortOrder:X02}",
                ["B11_Level"] = $"0x{Level:X02}",
                ["b12_HP"] = $"{HP}",
                ["b13_Str"] = $"{Str}",
                ["b14_Skl"] = $"{Skl}",
                ["b15_Spd"] = $"{Spd}",
                ["b16_Def"] = $"{Def}",
                ["b17_Res"] = $"{Res}",
                ["b18_Lck"] = $"{Lck}",
                ["b19_Con"] = $"{Con}",
                ["B20_WepSword"] = $"0x{WepSword:X02}",
                ["B21_WepLance"] = $"0x{WepLance:X02}",
                ["B22_WepAxe"] = $"0x{WepAxe:X02}",
                ["B23_WepBow"] = $"0x{WepBow:X02}",
                ["B24_WepStaff"] = $"0x{WepStaff:X02}",
                ["B25_WepAnima"] = $"0x{WepAnima:X02}",
                ["B26_WepLight"] = $"0x{WepLight:X02}",
                ["B27_WepDark"] = $"0x{WepDark:X02}",
                ["B28_GrowHP"] = $"0x{GrowHP:X02}",
                ["B29_GrowSTR"] = $"0x{GrowSTR:X02}",
                ["B30_GrowSKL"] = $"0x{GrowSKL:X02}",
                ["B31_GrowSPD"] = $"0x{GrowSPD:X02}",
                ["B32_GrowDEF"] = $"0x{GrowDEF:X02}",
                ["B33_GrowRES"] = $"0x{GrowRES:X02}",
                ["B34_GrowLCK"] = $"0x{GrowLCK:X02}",
                ["B35_LowerClassPalette"] = $"0x{LowerClassPalette:X02}",
                ["B36_UpperClassPalette"] = $"0x{UpperClassPalette:X02}",
                ["B37_LowerClassAnime"] = $"0x{LowerClassAnime:X02}",
                ["B38_UpperClassAnime"] = $"0x{UpperClassAnime:X02}",
                ["B39_Unk"] = $"0x{Unk39:X02}",
                ["B40_Ability1"] = $"0x{Ability1:X02}",
                ["B41_Ability2"] = $"0x{Ability2:X02}",
                ["B42_Ability3"] = $"0x{Ability3:X02}",
                ["B43_Ability4"] = $"0x{Ability4:X02}",
                ["P44_SupportPtr"] = $"0x{SupportPtr:X08}",
                ["B48_TalkGroup"] = $"0x{TalkGroup:X02}",
                ["B49_Unk"] = $"0x{Unk49:X02}",
                ["B50_Unk"] = $"0x{Unk50:X02}",
                ["B51_Unk"] = $"0x{Unk51:X02}",
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
                ["u16@0x00_NameId"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02_DescId"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04_UnitId"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_ClassId"] = $"0x{rom.u8(a + 5):X02}",
                ["u16@0x06_PortraitId"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08_MapFace"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_Affinity"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_SortOrder"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Level"] = $"0x{rom.u8(a + 11):X02}",
                ["s8@0x0C_HP"] = $"{(sbyte)rom.u8(a + 12)}",
                ["s8@0x0D_Str"] = $"{(sbyte)rom.u8(a + 13)}",
                ["s8@0x0E_Skl"] = $"{(sbyte)rom.u8(a + 14)}",
                ["s8@0x0F_Spd"] = $"{(sbyte)rom.u8(a + 15)}",
                ["s8@0x10_Def"] = $"{(sbyte)rom.u8(a + 16)}",
                ["s8@0x11_Res"] = $"{(sbyte)rom.u8(a + 17)}",
                ["s8@0x12_Lck"] = $"{(sbyte)rom.u8(a + 18)}",
                ["s8@0x13_Con"] = $"{(sbyte)rom.u8(a + 19)}",
                ["u8@0x14_WepSword"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_WepLance"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_WepAxe"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_WepBow"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18_WepStaff"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19_WepAnima"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A_WepLight"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B_WepDark"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C_GrowHP"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D_GrowSTR"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E_GrowSKL"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F_GrowSPD"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20_GrowDEF"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21_GrowRES"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22_GrowLCK"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23_LowerClassPalette"] = $"0x{rom.u8(a + 35):X02}",
                ["u8@0x24_UpperClassPalette"] = $"0x{rom.u8(a + 36):X02}",
                ["u8@0x25_LowerClassAnime"] = $"0x{rom.u8(a + 37):X02}",
                ["u8@0x26_UpperClassAnime"] = $"0x{rom.u8(a + 38):X02}",
                ["u8@0x27_Unk39"] = $"0x{rom.u8(a + 39):X02}",
                ["u8@0x28_Ability1"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29_Ability2"] = $"0x{rom.u8(a + 41):X02}",
                ["u8@0x2A_Ability3"] = $"0x{rom.u8(a + 42):X02}",
                ["u8@0x2B_Ability4"] = $"0x{rom.u8(a + 43):X02}",
                ["u32@0x2C_SupportPtr"] = $"0x{rom.u32(a + 44):X08}",
                ["u8@0x30_TalkGroup"] = $"0x{rom.u8(a + 48):X02}",
                ["u8@0x31_Unk49"] = $"0x{rom.u8(a + 49):X02}",
                ["u8@0x32_Unk50"] = $"0x{rom.u8(a + 50):X02}",
                ["u8@0x33_Unk51"] = $"0x{rom.u8(a + 51):X02}",
            };
        }
    }
}
