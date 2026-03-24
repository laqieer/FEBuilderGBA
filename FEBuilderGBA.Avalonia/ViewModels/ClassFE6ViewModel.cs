using System;
using System.Collections.Generic;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        string _growthSimText = "";
        uint _simLevel = 20;

        // W0, W2: text IDs
        uint _nameId, _descId;
        // B4, B5: class ID, promotion
        uint _classId, _promotionLevel;
        // B6, B7: wait icon, walk speed
        uint _waitIcon, _walkSpeed;
        // W8: portrait / map sprite
        uint _portraitId;
        // B10: sort order
        uint _sortOrder;
        // B11-B18: base stats (HP, Str, Skl, Spd, Def, Res, Con, Mov)
        uint _baseHp, _baseStr, _baseSkl, _baseSpd, _baseDef, _baseRes, _baseCon, _baseMov;
        // B19-B26: stat caps (HP, Str, Skl, Spd, Def, Res, Con, Power/EXP)
        uint _maxHp, _maxStr, _maxSkl, _maxSpd, _maxDef, _maxRes, _maxCon, _classPower;
        // B27-B33: growth rates
        uint _growHp, _growStr, _growSkl, _growSpd, _growDef, _growRes, _growLck;
        // b34, b35: CC bonus / promotion gains (HP, Str)
        int _promoHp, _promoStr;
        // B36-B39: ability flags (with bit fields)
        uint _ability1, _ability2, _ability3, _ability4;
        // B40-B47: weapon levels (Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark)
        uint _wepSword, _wepLance, _wepAxe, _wepBow, _wepStaff, _wepAnima, _wepLight, _wepDark;
        // P48: Battle animation pointer
        uint _battleAnimePtr;
        // P52: Move cost (normal) — FE6 has no rain/snow move cost
        uint _moveCostPtr;
        // P56: Terrain avoid, P60: Terrain defense, P64: Terrain resistance
        uint _terrainAvoidPtr, _terrainDefPtr, _terrainResPtr;
        // D68: unknown u32
        uint _unknownD68;

        bool _canWrite;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // W0: Name text ID
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        // W2: Description text ID
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        // B4: Class ID
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        // B5: Promotion level
        public uint PromotionLevel { get => _promotionLevel; set => SetField(ref _promotionLevel, value); }
        // B6: Wait icon
        public uint WaitIcon { get => _waitIcon; set => SetField(ref _waitIcon, value); }
        // B7: Walk speed
        public uint WalkSpeed { get => _walkSpeed; set => SetField(ref _walkSpeed, value); }
        // W8: Portrait / map sprite
        public uint PortraitId { get => _portraitId; set => SetField(ref _portraitId, value); }
        // B10: Sort order
        public uint SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }

        // B11-B18: Base stats
        public uint BaseHp { get => _baseHp; set => SetField(ref _baseHp, value); }
        public uint BaseStr { get => _baseStr; set => SetField(ref _baseStr, value); }
        public uint BaseSkl { get => _baseSkl; set => SetField(ref _baseSkl, value); }
        public uint BaseSpd { get => _baseSpd; set => SetField(ref _baseSpd, value); }
        public uint BaseDef { get => _baseDef; set => SetField(ref _baseDef, value); }
        public uint BaseRes { get => _baseRes; set => SetField(ref _baseRes, value); }
        // B17: Con, B18: Mov
        public uint BaseCon { get => _baseCon; set => SetField(ref _baseCon, value); }
        public uint BaseMov { get => _baseMov; set => SetField(ref _baseMov, value); }

        // B19-B26: Stat caps (max values)
        public uint MaxHp { get => _maxHp; set => SetField(ref _maxHp, value); }
        public uint MaxStr { get => _maxStr; set => SetField(ref _maxStr, value); }
        public uint MaxSkl { get => _maxSkl; set => SetField(ref _maxSkl, value); }
        public uint MaxSpd { get => _maxSpd; set => SetField(ref _maxSpd, value); }
        public uint MaxDef { get => _maxDef; set => SetField(ref _maxDef, value); }
        public uint MaxRes { get => _maxRes; set => SetField(ref _maxRes, value); }
        public uint MaxCon { get => _maxCon; set => SetField(ref _maxCon, value); }
        public uint ClassPower { get => _classPower; set => SetField(ref _classPower, value); }

        // B27-B33: Growth rates
        public uint GrowHp { get => _growHp; set => SetField(ref _growHp, value); }
        public uint GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public uint GrowSkl { get => _growSkl; set => SetField(ref _growSkl, value); }
        public uint GrowSpd { get => _growSpd; set => SetField(ref _growSpd, value); }
        public uint GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public uint GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public uint GrowLck { get => _growLck; set => SetField(ref _growLck, value); }

        // b34, b35: CC bonus / promotion gains (HP, Str)
        public int PromoHp { get => _promoHp; set => SetField(ref _promoHp, value); }
        public int PromoStr { get => _promoStr; set => SetField(ref _promoStr, value); }

        // B36-B39: Ability flags
        public uint Ability1 { get => _ability1; set => SetField(ref _ability1, value); }
        public uint Ability2 { get => _ability2; set => SetField(ref _ability2, value); }
        public uint Ability3 { get => _ability3; set => SetField(ref _ability3, value); }
        public uint Ability4 { get => _ability4; set => SetField(ref _ability4, value); }

        // B40-B47: Weapon levels
        public uint WepSword { get => _wepSword; set => SetField(ref _wepSword, value); }
        public uint WepLance { get => _wepLance; set => SetField(ref _wepLance, value); }
        public uint WepAxe { get => _wepAxe; set => SetField(ref _wepAxe, value); }
        public uint WepBow { get => _wepBow; set => SetField(ref _wepBow, value); }
        public uint WepStaff { get => _wepStaff; set => SetField(ref _wepStaff, value); }
        public uint WepAnima { get => _wepAnima; set => SetField(ref _wepAnima, value); }
        public uint WepLight { get => _wepLight; set => SetField(ref _wepLight, value); }
        public uint WepDark { get => _wepDark; set => SetField(ref _wepDark, value); }

        // P48: Battle animation pointer
        public uint BattleAnimePtr { get => _battleAnimePtr; set => SetField(ref _battleAnimePtr, value); }
        // P52: Move cost (normal) — FE6 has no rain/snow move cost
        public uint MoveCostPtr { get => _moveCostPtr; set => SetField(ref _moveCostPtr, value); }
        // P56: Terrain avoid
        public uint TerrainAvoidPtr { get => _terrainAvoidPtr; set => SetField(ref _terrainAvoidPtr, value); }
        // P60: Terrain defense
        public uint TerrainDefPtr { get => _terrainDefPtr; set => SetField(ref _terrainDefPtr, value); }
        // P64: Terrain resistance
        public uint TerrainResPtr { get => _terrainResPtr; set => SetField(ref _terrainResPtr, value); }

        // D68: Unknown u32
        public uint UnknownD68 { get => _unknownD68; set => SetField(ref _unknownD68, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameTextId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameTextId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.class_datasize ?? 84;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;

            // Text IDs
            NameId = rom.u16(addr + 0);
            DescId = rom.u16(addr + 2);
            try { Name = NameResolver.GetTextById(NameId); }
            catch { Name = "???"; }

            // Identity
            ClassId = rom.u8(addr + 4);
            PromotionLevel = rom.u8(addr + 5);
            WaitIcon = rom.u8(addr + 6);
            WalkSpeed = rom.u8(addr + 7);
            PortraitId = rom.u16(addr + 8);
            SortOrder = rom.u8(addr + 10);

            // Base stats
            BaseHp = rom.u8(addr + 11);
            BaseStr = rom.u8(addr + 12);
            BaseSkl = rom.u8(addr + 13);
            BaseSpd = rom.u8(addr + 14);
            BaseDef = rom.u8(addr + 15);
            BaseRes = rom.u8(addr + 16);
            BaseCon = rom.u8(addr + 17);      // B17 (Con)
            BaseMov = rom.u8(addr + 18);      // B18 (Mov)

            // Stat caps (max values)
            MaxHp = rom.u8(addr + 19);
            MaxStr = rom.u8(addr + 20);
            MaxSkl = rom.u8(addr + 21);
            MaxSpd = rom.u8(addr + 22);
            MaxDef = rom.u8(addr + 23);
            MaxRes = rom.u8(addr + 24);
            MaxCon = rom.u8(addr + 25);
            ClassPower = rom.u8(addr + 26);

            // Growth rates
            GrowHp = rom.u8(addr + 27);
            GrowStr = rom.u8(addr + 28);
            GrowSkl = rom.u8(addr + 29);
            GrowSpd = rom.u8(addr + 30);
            GrowDef = rom.u8(addr + 31);
            GrowRes = rom.u8(addr + 32);
            GrowLck = rom.u8(addr + 33);

            // CC bonus / promotion gains (HP, Str)
            PromoHp = (sbyte)rom.u8(addr + 34);
            PromoStr = (sbyte)rom.u8(addr + 35);

            // Ability flags
            Ability1 = rom.u8(addr + 36);
            Ability2 = rom.u8(addr + 37);
            Ability3 = rom.u8(addr + 38);
            Ability4 = rom.u8(addr + 39);

            // Weapon levels
            WepSword = rom.u8(addr + 40);
            WepLance = rom.u8(addr + 41);
            WepAxe = rom.u8(addr + 42);
            WepBow = rom.u8(addr + 43);
            WepStaff = rom.u8(addr + 44);
            WepAnima = rom.u8(addr + 45);
            WepLight = rom.u8(addr + 46);
            WepDark = rom.u8(addr + 47);

            // Pointers — FE6 has no rain/snow move cost
            BattleAnimePtr = rom.u32(addr + 48);
            MoveCostPtr = rom.u32(addr + 52);      // P52: Move cost (normal)
            TerrainAvoidPtr = rom.u32(addr + 56);   // P56: Terrain avoid
            TerrainDefPtr = rom.u32(addr + 60);     // P60: Terrain defense
            TerrainResPtr = rom.u32(addr + 64);     // P64: Terrain resistance

            // D68
            UnknownD68 = rom.u32(addr + 68);

            CanWrite = true;
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2_DescId"] = $"0x{DescId:X04}",
                ["B4_ClassId"] = $"0x{ClassId:X02}",
                ["B5_PromotionLevel"] = $"0x{PromotionLevel:X02}",
                ["B6_WaitIcon"] = $"0x{WaitIcon:X02}",
                ["B7_WalkSpeed"] = $"0x{WalkSpeed:X02}",
                ["W8_PortraitId"] = $"0x{PortraitId:X04}",
                ["B10_SortOrder"] = $"0x{SortOrder:X02}",
                ["B11_BaseHp"] = $"0x{BaseHp:X02}",
                ["B12_BaseStr"] = $"0x{BaseStr:X02}",
                ["B13_BaseSkl"] = $"0x{BaseSkl:X02}",
                ["B14_BaseSpd"] = $"0x{BaseSpd:X02}",
                ["B15_BaseDef"] = $"0x{BaseDef:X02}",
                ["B16_BaseRes"] = $"0x{BaseRes:X02}",
                ["B17_BaseCon"] = $"0x{BaseCon:X02}",
                ["B18_BaseMov"] = $"0x{BaseMov:X02}",
                ["B19_MaxHp"] = $"0x{MaxHp:X02}",
                ["B20_MaxStr"] = $"0x{MaxStr:X02}",
                ["B21_MaxSkl"] = $"0x{MaxSkl:X02}",
                ["B22_MaxSpd"] = $"0x{MaxSpd:X02}",
                ["B23_MaxDef"] = $"0x{MaxDef:X02}",
                ["B24_MaxRes"] = $"0x{MaxRes:X02}",
                ["B25_MaxCon"] = $"0x{MaxCon:X02}",
                ["B26_ClassPower"] = $"0x{ClassPower:X02}",
                ["B27_GrowHp"] = $"0x{GrowHp:X02}",
                ["B28_GrowStr"] = $"0x{GrowStr:X02}",
                ["B29_GrowSkl"] = $"0x{GrowSkl:X02}",
                ["B30_GrowSpd"] = $"0x{GrowSpd:X02}",
                ["B31_GrowDef"] = $"0x{GrowDef:X02}",
                ["B32_GrowRes"] = $"0x{GrowRes:X02}",
                ["B33_GrowLck"] = $"0x{GrowLck:X02}",
                ["b34_PromoHp"] = $"{PromoHp}",
                ["b35_PromoStr"] = $"{PromoStr}",
                ["B36_Ability1"] = $"0x{Ability1:X02}",
                ["B37_Ability2"] = $"0x{Ability2:X02}",
                ["B38_Ability3"] = $"0x{Ability3:X02}",
                ["B39_Ability4"] = $"0x{Ability4:X02}",
                ["B40_WepSword"] = $"0x{WepSword:X02}",
                ["B41_WepLance"] = $"0x{WepLance:X02}",
                ["B42_WepAxe"] = $"0x{WepAxe:X02}",
                ["B43_WepBow"] = $"0x{WepBow:X02}",
                ["B44_WepStaff"] = $"0x{WepStaff:X02}",
                ["B45_WepAnima"] = $"0x{WepAnima:X02}",
                ["B46_WepLight"] = $"0x{WepLight:X02}",
                ["B47_WepDark"] = $"0x{WepDark:X02}",
                ["P48_BattleAnimePtr"] = $"0x{BattleAnimePtr:X08}",
                ["P52_MoveCostPtr"] = $"0x{MoveCostPtr:X08}",
                ["P56_TerrainAvoidPtr"] = $"0x{TerrainAvoidPtr:X08}",
                ["P60_TerrainDefPtr"] = $"0x{TerrainDefPtr:X08}",
                ["P64_TerrainResPtr"] = $"0x{TerrainResPtr:X08}",
                ["D68_Unknown"] = $"0x{UnknownD68:X08}",
            };
            return d;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23"] = $"0x{rom.u8(a + 35):X02}",
                ["u8@0x24"] = $"0x{rom.u8(a + 36):X02}",
                ["u8@0x25"] = $"0x{rom.u8(a + 37):X02}",
                ["u8@0x26"] = $"0x{rom.u8(a + 38):X02}",
                ["u8@0x27"] = $"0x{rom.u8(a + 39):X02}",
                ["u8@0x28"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29"] = $"0x{rom.u8(a + 41):X02}",
                ["u8@0x2A"] = $"0x{rom.u8(a + 42):X02}",
                ["u8@0x2B"] = $"0x{rom.u8(a + 43):X02}",
                ["u8@0x2C"] = $"0x{rom.u8(a + 44):X02}",
                ["u8@0x2D"] = $"0x{rom.u8(a + 45):X02}",
                ["u8@0x2E"] = $"0x{rom.u8(a + 46):X02}",
                ["u8@0x2F"] = $"0x{rom.u8(a + 47):X02}",
                ["u32@0x30"] = $"0x{rom.u32(a + 48):X08}",
                ["u32@0x34"] = $"0x{rom.u32(a + 52):X08}",
                ["u32@0x38"] = $"0x{rom.u32(a + 56):X08}",
                ["u32@0x3C"] = $"0x{rom.u32(a + 60):X08}",
                ["u32@0x40"] = $"0x{rom.u32(a + 64):X08}",
                ["u32@0x44"] = $"0x{rom.u32(a + 68):X08}",
            };
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["W0_NameId"] = "u16@0x00",
                ["W2_DescId"] = "u16@0x02",
                ["B4_ClassId"] = "u8@0x04",
                ["B5_PromotionLevel"] = "u8@0x05",
                ["B6_WaitIcon"] = "u8@0x06",
                ["B7_WalkSpeed"] = "u8@0x07",
                ["W8_PortraitId"] = "u16@0x08",
                ["B10_SortOrder"] = "u8@0x0A",
                ["B11_BaseHp"] = "u8@0x0B",
                ["B12_BaseStr"] = "u8@0x0C",
                ["B13_BaseSkl"] = "u8@0x0D",
                ["B14_BaseSpd"] = "u8@0x0E",
                ["B15_BaseDef"] = "u8@0x0F",
                ["B16_BaseRes"] = "u8@0x10",
                ["B17_BaseCon"] = "u8@0x11",
                ["B18_BaseMov"] = "u8@0x12",
                ["B19_MaxHp"] = "u8@0x13",
                ["B20_MaxStr"] = "u8@0x14",
                ["B21_MaxSkl"] = "u8@0x15",
                ["B22_MaxSpd"] = "u8@0x16",
                ["B23_MaxDef"] = "u8@0x17",
                ["B24_MaxRes"] = "u8@0x18",
                ["B25_MaxCon"] = "u8@0x19",
                ["B26_ClassPower"] = "u8@0x1A",
                ["B27_GrowHp"] = "u8@0x1B",
                ["B28_GrowStr"] = "u8@0x1C",
                ["B29_GrowSkl"] = "u8@0x1D",
                ["B30_GrowSpd"] = "u8@0x1E",
                ["B31_GrowDef"] = "u8@0x1F",
                ["B32_GrowRes"] = "u8@0x20",
                ["B33_GrowLck"] = "u8@0x21",
                ["b34_PromoHp"] = "u8@0x22",
                ["b35_PromoStr"] = "u8@0x23",
                ["B36_Ability1"] = "u8@0x24",
                ["B37_Ability2"] = "u8@0x25",
                ["B38_Ability3"] = "u8@0x26",
                ["B39_Ability4"] = "u8@0x27",
                ["B40_WepSword"] = "u8@0x28",
                ["B41_WepLance"] = "u8@0x29",
                ["B42_WepAxe"] = "u8@0x2A",
                ["B43_WepBow"] = "u8@0x2B",
                ["B44_WepStaff"] = "u8@0x2C",
                ["B45_WepAnima"] = "u8@0x2D",
                ["B46_WepLight"] = "u8@0x2E",
                ["B47_WepDark"] = "u8@0x2F",
                ["P48_BattleAnimePtr"] = "u32@0x30",
                ["P52_MoveCostPtr"] = "u32@0x34",
                ["P56_TerrainAvoidPtr"] = "u32@0x38",
                ["P60_TerrainDefPtr"] = "u32@0x3C",
                ["P64_TerrainResPtr"] = "u32@0x40",
                ["D68_Unknown"] = "u32@0x44",
            };
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            // Text IDs
            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);

            // Identity
            rom.write_u8(addr + 4, ClassId);
            rom.write_u8(addr + 5, PromotionLevel);
            rom.write_u8(addr + 6, WaitIcon);
            rom.write_u8(addr + 7, WalkSpeed);
            rom.write_u16(addr + 8, PortraitId);
            rom.write_u8(addr + 10, SortOrder);

            // Base stats
            rom.write_u8(addr + 11, BaseHp);
            rom.write_u8(addr + 12, BaseStr);
            rom.write_u8(addr + 13, BaseSkl);
            rom.write_u8(addr + 14, BaseSpd);
            rom.write_u8(addr + 15, BaseDef);
            rom.write_u8(addr + 16, BaseRes);
            rom.write_u8(addr + 17, BaseCon);      // B17 (Con)
            rom.write_u8(addr + 18, BaseMov);      // B18 (Mov)

            // Stat caps (max values)
            rom.write_u8(addr + 19, MaxHp);
            rom.write_u8(addr + 20, MaxStr);
            rom.write_u8(addr + 21, MaxSkl);
            rom.write_u8(addr + 22, MaxSpd);
            rom.write_u8(addr + 23, MaxDef);
            rom.write_u8(addr + 24, MaxRes);
            rom.write_u8(addr + 25, MaxCon);
            rom.write_u8(addr + 26, ClassPower);

            // Growth rates
            rom.write_u8(addr + 27, GrowHp);
            rom.write_u8(addr + 28, GrowStr);
            rom.write_u8(addr + 29, GrowSkl);
            rom.write_u8(addr + 30, GrowSpd);
            rom.write_u8(addr + 31, GrowDef);
            rom.write_u8(addr + 32, GrowRes);
            rom.write_u8(addr + 33, GrowLck);

            // CC bonus / promotion gains
            rom.write_u8(addr + 34, (uint)(byte)(sbyte)PromoHp);
            rom.write_u8(addr + 35, (uint)(byte)(sbyte)PromoStr);

            // Ability flags
            rom.write_u8(addr + 36, Ability1);
            rom.write_u8(addr + 37, Ability2);
            rom.write_u8(addr + 38, Ability3);
            rom.write_u8(addr + 39, Ability4);

            // Weapon levels
            rom.write_u8(addr + 40, WepSword);
            rom.write_u8(addr + 41, WepLance);
            rom.write_u8(addr + 42, WepAxe);
            rom.write_u8(addr + 43, WepBow);
            rom.write_u8(addr + 44, WepStaff);
            rom.write_u8(addr + 45, WepAnima);
            rom.write_u8(addr + 46, WepLight);
            rom.write_u8(addr + 47, WepDark);

            // Pointers — FE6 has no rain/snow move cost
            rom.write_u32(addr + 48, BattleAnimePtr);
            rom.write_u32(addr + 52, MoveCostPtr);       // P52: Move cost (normal)
            rom.write_u32(addr + 56, TerrainAvoidPtr);    // P56: Terrain avoid
            rom.write_u32(addr + 60, TerrainDefPtr);      // P60: Terrain defense
            rom.write_u32(addr + 64, TerrainResPtr);      // P64: Terrain resistance

            // D68
            rom.write_u32(addr + 68, UnknownD68);
        }

        public string GrowthSimText { get => _growthSimText; set => SetField(ref _growthSimText, value); }

        /// <summary>
        /// Target level for growth simulation. Default 20. Clamped to 1-99.
        /// Changing this auto-recalculates when not loading.
        /// </summary>
        public uint SimLevel
        {
            get => _simLevel;
            set
            {
                uint clamped = Math.Clamp(value, 1, 99);
                if (SetField(ref _simLevel, clamped))
                    AutoRecalcGrowth();
            }
        }

        public void CalculateGrowth()
        {
            try
            {
                var sim = new GrowSimulator();
                sim.SetUnitBase(1, 0, 0, 0, 0, 0, 0, 0, 0);
                sim.SetClassBase((int)BaseHp, (int)BaseStr, (int)BaseSkl, (int)BaseSpd, (int)BaseDef, (int)BaseRes, 0);
                sim.SetUnitGrow(0, 0, 0, 0, 0, 0, 0, 0);
                sim.SetClassGrow((int)GrowHp, (int)GrowStr, (int)GrowSkl, (int)GrowSpd, (int)GrowDef, (int)GrowRes, (int)GrowLck, 0);

                var sb = new StringBuilder();
                sb.AppendLine("Class Growth Simulator:");
                sb.AppendLine("LV   HP  Str  Skl  Spd  Def  Res  Lck");
                sim.Grow(10, GrowSimulator.GrowOptionEnum.ClassGrow);
                sb.AppendLine($"10  {sim.sim_hp,3}  {sim.sim_str,3}  {sim.sim_skill,3}  {sim.sim_spd,3}  {sim.sim_def,3}  {sim.sim_res,3}  {sim.sim_luck,3}");
                sim.Grow(20, GrowSimulator.GrowOptionEnum.ClassGrow);
                sb.AppendLine($"20  {sim.sim_hp,3}  {sim.sim_str,3}  {sim.sim_skill,3}  {sim.sim_spd,3}  {sim.sim_def,3}  {sim.sim_res,3}  {sim.sim_luck,3}");

                // Custom level (skip if it duplicates 10 or 20)
                if (SimLevel != 10 && SimLevel != 20)
                {
                    sim.Grow((int)SimLevel, GrowSimulator.GrowOptionEnum.ClassGrow);
                    sb.AppendLine($"{SimLevel,-2}  {sim.sim_hp,3}  {sim.sim_str,3}  {sim.sim_skill,3}  {sim.sim_spd,3}  {sim.sim_def,3}  {sim.sim_res,3}  {sim.sim_luck,3}");
                }

                GrowthSimText = sb.ToString();
            }
            catch (Exception ex) { GrowthSimText = $"Error: {ex.Message}"; }
        }

        void AutoRecalcGrowth()
        {
            if (!IsLoading && CanWrite)
                CalculateGrowth();
        }
    }
}
