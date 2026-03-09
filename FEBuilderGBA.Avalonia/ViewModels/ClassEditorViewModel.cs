using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";

        // W0, W2: text IDs
        uint _nameId, _descId;
        // B4, B5: class number, promotion level
        uint _classNumber, _promotionLevel;
        // B6, B7: wait icon, walk speed/type
        uint _waitIcon, _walkSpeed;
        // W8: portrait or related u16
        uint _portraitId;
        // B10: con/build or related stat
        uint _buildStat;
        // B11-B17: base stats + mov
        uint _baseHp, _baseStr, _baseSkl, _baseSpd, _baseDef, _baseRes, _mov;
        // B18-B19: con, weight (class stats)
        uint _con, _classStat19;
        // B20-B26: weapon levels (7 weapon types)
        uint _wepSword, _wepLance, _wepAxe, _wepBow, _wepStaff, _wepAnima, _wepLight;
        // B27-B33: growth rates
        uint _growHp, _growStr, _growSkl, _growSpd, _growDef, _growRes, _growLck;
        // b34-b39: signed stat caps / promotion bonuses
        int _capHp, _capStr, _capSkl, _capSpd, _capDef, _capRes;
        // B40-B43: ability flags
        uint _ability1, _ability2, _ability3, _ability4;
        // B44-B51: weapon rank levels (Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark)
        uint _wepRankSword, _wepRankLance, _wepRankAxe, _wepRankBow;
        uint _wepRankStaff, _wepRankAnima, _wepRankLight, _wepRankDark;
        // P52: Battle animation pointer, P56-P64: Move cost pointers
        uint _battleAnimePtr, _moveCostPtr, _moveCostRainPtr, _moveCostSnowPtr;
        // P68: Terrain avoid, P72: Terrain defense, P76: Terrain resistance
        uint _terrainAvoidPtr, _terrainDefPtr, _terrainResPtr;
        // D80: unknown u32
        uint _unknownD80;

        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        // W0: Name text ID
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        // W2: Description text ID
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        // B4: Class Number
        public uint ClassNumber { get => _classNumber; set => SetField(ref _classNumber, value); }
        // B5: Promotion level / tier
        public uint PromotionLevel { get => _promotionLevel; set => SetField(ref _promotionLevel, value); }
        // B6: Wait icon
        public uint WaitIcon { get => _waitIcon; set => SetField(ref _waitIcon, value); }
        // B7: Walk speed / type
        public uint WalkSpeed { get => _walkSpeed; set => SetField(ref _walkSpeed, value); }
        // W8: Portrait / map sprite
        public uint PortraitId { get => _portraitId; set => SetField(ref _portraitId, value); }
        // B10: Build stat
        public uint BuildStat { get => _buildStat; set => SetField(ref _buildStat, value); }

        // B11-B16: Base stats
        public uint BaseHp { get => _baseHp; set => SetField(ref _baseHp, value); }
        public uint BaseStr { get => _baseStr; set => SetField(ref _baseStr, value); }
        public uint BaseSkl { get => _baseSkl; set => SetField(ref _baseSkl, value); }
        public uint BaseSpd { get => _baseSpd; set => SetField(ref _baseSpd, value); }
        public uint BaseDef { get => _baseDef; set => SetField(ref _baseDef, value); }
        public uint BaseRes { get => _baseRes; set => SetField(ref _baseRes, value); }
        // B17: Movement
        public uint Mov { get => _mov; set => SetField(ref _mov, value); }
        // B18: Constitution
        public uint Con { get => _con; set => SetField(ref _con, value); }
        // B19: Unknown class stat
        public uint ClassStat19 { get => _classStat19; set => SetField(ref _classStat19, value); }

        // B20-B26: Weapon proficiency levels
        public uint WepSword { get => _wepSword; set => SetField(ref _wepSword, value); }
        public uint WepLance { get => _wepLance; set => SetField(ref _wepLance, value); }
        public uint WepAxe { get => _wepAxe; set => SetField(ref _wepAxe, value); }
        public uint WepBow { get => _wepBow; set => SetField(ref _wepBow, value); }
        public uint WepStaff { get => _wepStaff; set => SetField(ref _wepStaff, value); }
        public uint WepAnima { get => _wepAnima; set => SetField(ref _wepAnima, value); }
        public uint WepLight { get => _wepLight; set => SetField(ref _wepLight, value); }

        // B27-B33: Growth rates
        public uint GrowHp { get => _growHp; set => SetField(ref _growHp, value); }
        public uint GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public uint GrowSkl { get => _growSkl; set => SetField(ref _growSkl, value); }
        public uint GrowSpd { get => _growSpd; set => SetField(ref _growSpd, value); }
        public uint GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public uint GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public uint GrowLck { get => _growLck; set => SetField(ref _growLck, value); }

        // b34-b39: Stat caps / promotion bonuses (signed)
        public int CapHp { get => _capHp; set => SetField(ref _capHp, value); }
        public int CapStr { get => _capStr; set => SetField(ref _capStr, value); }
        public int CapSkl { get => _capSkl; set => SetField(ref _capSkl, value); }
        public int CapSpd { get => _capSpd; set => SetField(ref _capSpd, value); }
        public int CapDef { get => _capDef; set => SetField(ref _capDef, value); }
        public int CapRes { get => _capRes; set => SetField(ref _capRes, value); }

        // B40-B43: Ability flags
        public uint Ability1 { get => _ability1; set => SetField(ref _ability1, value); }
        public uint Ability2 { get => _ability2; set => SetField(ref _ability2, value); }
        public uint Ability3 { get => _ability3; set => SetField(ref _ability3, value); }
        public uint Ability4 { get => _ability4; set => SetField(ref _ability4, value); }

        // B44-B51: Weapon rank levels
        public uint WepRankSword { get => _wepRankSword; set => SetField(ref _wepRankSword, value); }
        public uint WepRankLance { get => _wepRankLance; set => SetField(ref _wepRankLance, value); }
        public uint WepRankAxe { get => _wepRankAxe; set => SetField(ref _wepRankAxe, value); }
        public uint WepRankBow { get => _wepRankBow; set => SetField(ref _wepRankBow, value); }
        public uint WepRankStaff { get => _wepRankStaff; set => SetField(ref _wepRankStaff, value); }
        public uint WepRankAnima { get => _wepRankAnima; set => SetField(ref _wepRankAnima, value); }
        public uint WepRankLight { get => _wepRankLight; set => SetField(ref _wepRankLight, value); }
        public uint WepRankDark { get => _wepRankDark; set => SetField(ref _wepRankDark, value); }

        // P52: Battle animation pointer
        public uint BattleAnimePtr { get => _battleAnimePtr; set => SetField(ref _battleAnimePtr, value); }
        // P56: Move cost (normal)
        public uint MoveCostPtr { get => _moveCostPtr; set => SetField(ref _moveCostPtr, value); }
        // P60: Move cost (rain)
        public uint MoveCostRainPtr { get => _moveCostRainPtr; set => SetField(ref _moveCostRainPtr, value); }
        // P64: Move cost (snow)
        public uint MoveCostSnowPtr { get => _moveCostSnowPtr; set => SetField(ref _moveCostSnowPtr, value); }
        // P68: Terrain avoid
        public uint TerrainAvoidPtr { get => _terrainAvoidPtr; set => SetField(ref _terrainAvoidPtr, value); }
        // P72: Terrain defense
        public uint TerrainDefPtr { get => _terrainDefPtr; set => SetField(ref _terrainDefPtr, value); }
        // P76: Terrain resistance
        public uint TerrainResPtr { get => _terrainResPtr; set => SetField(ref _terrainResPtr, value); }

        // D80: Unknown u32
        public uint UnknownD80 { get => _unknownD80; set => SetField(ref _unknownD80, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadClassList()
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

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadClass(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.class_datasize ?? 84;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Text IDs
            NameId = rom.u16(addr + 0);       // W0
            DescId = rom.u16(addr + 2);       // W2
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // Identity
            ClassNumber = rom.u8(addr + 4);   // B4
            PromotionLevel = rom.u8(addr + 5); // B5
            WaitIcon = rom.u8(addr + 6);      // B6
            WalkSpeed = rom.u8(addr + 7);     // B7
            PortraitId = rom.u16(addr + 8);   // W8
            BuildStat = rom.u8(addr + 10);    // B10

            // Base stats
            BaseHp = rom.u8(addr + 11);       // B11
            BaseStr = rom.u8(addr + 12);      // B12
            BaseSkl = rom.u8(addr + 13);      // B13
            BaseSpd = rom.u8(addr + 14);      // B14
            BaseDef = rom.u8(addr + 15);      // B15
            BaseRes = rom.u8(addr + 16);      // B16
            Mov = rom.u8(addr + 17);          // B17
            Con = rom.u8(addr + 18);          // B18
            ClassStat19 = rom.u8(addr + 19);  // B19

            // Weapon proficiency
            WepSword = rom.u8(addr + 20);     // B20
            WepLance = rom.u8(addr + 21);     // B21
            WepAxe = rom.u8(addr + 22);       // B22
            WepBow = rom.u8(addr + 23);       // B23
            WepStaff = rom.u8(addr + 24);     // B24
            WepAnima = rom.u8(addr + 25);     // B25
            WepLight = rom.u8(addr + 26);     // B26

            // Growth rates
            GrowHp = rom.u8(addr + 27);       // B27
            GrowStr = rom.u8(addr + 28);      // B28
            GrowSkl = rom.u8(addr + 29);      // B29
            GrowSpd = rom.u8(addr + 30);      // B30
            GrowDef = rom.u8(addr + 31);      // B31
            GrowRes = rom.u8(addr + 32);      // B32
            GrowLck = rom.u8(addr + 33);      // B33

            // Stat caps / promotion bonuses (signed)
            CapHp = (sbyte)rom.u8(addr + 34);  // b34
            CapStr = (sbyte)rom.u8(addr + 35); // b35
            CapSkl = (sbyte)rom.u8(addr + 36); // b36
            CapSpd = (sbyte)rom.u8(addr + 37); // b37
            CapDef = (sbyte)rom.u8(addr + 38); // b38
            CapRes = (sbyte)rom.u8(addr + 39); // b39

            // Ability flags
            Ability1 = rom.u8(addr + 40);     // B40
            Ability2 = rom.u8(addr + 41);     // B41
            Ability3 = rom.u8(addr + 42);     // B42
            Ability4 = rom.u8(addr + 43);     // B43

            // Weapon rank levels
            WepRankSword = rom.u8(addr + 44); // B44
            WepRankLance = rom.u8(addr + 45); // B45
            WepRankAxe = rom.u8(addr + 46);   // B46
            WepRankBow = rom.u8(addr + 47);   // B47
            WepRankStaff = rom.u8(addr + 48); // B48
            WepRankAnima = rom.u8(addr + 49); // B49
            WepRankLight = rom.u8(addr + 50); // B50
            WepRankDark = rom.u8(addr + 51);  // B51

            // Pointers
            BattleAnimePtr = rom.u32(addr + 52);   // P52
            MoveCostPtr = rom.u32(addr + 56);      // P56
            MoveCostRainPtr = rom.u32(addr + 60);  // P60
            MoveCostSnowPtr = rom.u32(addr + 64);  // P64
            TerrainAvoidPtr = rom.u32(addr + 68);  // P68
            TerrainDefPtr = rom.u32(addr + 72);    // P72
            TerrainResPtr = rom.u32(addr + 76);    // P76

            // D80
            UnknownD80 = rom.u32(addr + 80);      // D80

            CanWrite = true;
        }

        public int GetListCount() => LoadClassList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2_DescId"] = $"0x{DescId:X04}",
                ["B4_ClassNumber"] = $"0x{ClassNumber:X02}",
                ["B5_PromoLevel"] = $"0x{PromotionLevel:X02}",
                ["B6_WaitIcon"] = $"0x{WaitIcon:X02}",
                ["B7_WalkSpeed"] = $"0x{WalkSpeed:X02}",
                ["W8_PortraitId"] = $"0x{PortraitId:X04}",
                ["B10_BuildStat"] = $"0x{BuildStat:X02}",
                ["B11_BaseHp"] = $"0x{BaseHp:X02}",
                ["B12_BaseStr"] = $"0x{BaseStr:X02}",
                ["B13_BaseSkl"] = $"0x{BaseSkl:X02}",
                ["B14_BaseSpd"] = $"0x{BaseSpd:X02}",
                ["B15_BaseDef"] = $"0x{BaseDef:X02}",
                ["B16_BaseRes"] = $"0x{BaseRes:X02}",
                ["B17_Mov"] = $"0x{Mov:X02}",
                ["B18_Con"] = $"0x{Con:X02}",
                ["B19_ClassStat19"] = $"0x{ClassStat19:X02}",
                ["B20_WepSword"] = $"0x{WepSword:X02}",
                ["B21_WepLance"] = $"0x{WepLance:X02}",
                ["B22_WepAxe"] = $"0x{WepAxe:X02}",
                ["B23_WepBow"] = $"0x{WepBow:X02}",
                ["B24_WepStaff"] = $"0x{WepStaff:X02}",
                ["B25_WepAnima"] = $"0x{WepAnima:X02}",
                ["B26_WepLight"] = $"0x{WepLight:X02}",
                ["B27_GrowHp"] = $"0x{GrowHp:X02}",
                ["B28_GrowStr"] = $"0x{GrowStr:X02}",
                ["B29_GrowSkl"] = $"0x{GrowSkl:X02}",
                ["B30_GrowSpd"] = $"0x{GrowSpd:X02}",
                ["B31_GrowDef"] = $"0x{GrowDef:X02}",
                ["B32_GrowRes"] = $"0x{GrowRes:X02}",
                ["B33_GrowLck"] = $"0x{GrowLck:X02}",
                ["b34_CapHp"] = $"{CapHp}",
                ["b35_CapStr"] = $"{CapStr}",
                ["b36_CapSkl"] = $"{CapSkl}",
                ["b37_CapSpd"] = $"{CapSpd}",
                ["b38_CapDef"] = $"{CapDef}",
                ["b39_CapRes"] = $"{CapRes}",
                ["B40_Ability1"] = $"0x{Ability1:X02}",
                ["B41_Ability2"] = $"0x{Ability2:X02}",
                ["B42_Ability3"] = $"0x{Ability3:X02}",
                ["B43_Ability4"] = $"0x{Ability4:X02}",
                ["B44_WepRankSword"] = $"0x{WepRankSword:X02}",
                ["B45_WepRankLance"] = $"0x{WepRankLance:X02}",
                ["B46_WepRankAxe"] = $"0x{WepRankAxe:X02}",
                ["B47_WepRankBow"] = $"0x{WepRankBow:X02}",
                ["B48_WepRankStaff"] = $"0x{WepRankStaff:X02}",
                ["B49_WepRankAnima"] = $"0x{WepRankAnima:X02}",
                ["B50_WepRankLight"] = $"0x{WepRankLight:X02}",
                ["B51_WepRankDark"] = $"0x{WepRankDark:X02}",
                ["P52_BattleAnimePtr"] = $"0x{BattleAnimePtr:X08}",
                ["P56_MoveCostPtr"] = $"0x{MoveCostPtr:X08}",
                ["P60_MoveCostRainPtr"] = $"0x{MoveCostRainPtr:X08}",
                ["P64_MoveCostSnowPtr"] = $"0x{MoveCostSnowPtr:X08}",
                ["P68_TerrainAvoidPtr"] = $"0x{TerrainAvoidPtr:X08}",
                ["P72_TerrainDefPtr"] = $"0x{TerrainDefPtr:X08}",
                ["P76_TerrainResPtr"] = $"0x{TerrainResPtr:X08}",
                ["D80_Unknown"] = $"0x{UnknownD80:X08}",
            };
            return d;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            return new Dictionary<string, string>
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
                ["u8@0x30"] = $"0x{rom.u8(a + 48):X02}",
                ["u8@0x31"] = $"0x{rom.u8(a + 49):X02}",
                ["u8@0x32"] = $"0x{rom.u8(a + 50):X02}",
                ["u8@0x33"] = $"0x{rom.u8(a + 51):X02}",
                ["u32@0x34"] = $"0x{rom.u32(a + 52):X08}",
                ["u32@0x38"] = $"0x{rom.u32(a + 56):X08}",
                ["u32@0x3C"] = $"0x{rom.u32(a + 60):X08}",
                ["u32@0x40"] = $"0x{rom.u32(a + 64):X08}",
                ["u32@0x44"] = $"0x{rom.u32(a + 68):X08}",
                ["u32@0x48"] = $"0x{rom.u32(a + 72):X08}",
                ["u32@0x4C"] = $"0x{rom.u32(a + 76):X08}",
                ["u32@0x50"] = $"0x{rom.u32(a + 80):X08}",
            };
        }

        public void WriteClass()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u8(addr + 4, ClassNumber);
            rom.write_u8(addr + 5, PromotionLevel);
            rom.write_u8(addr + 6, WaitIcon);
            rom.write_u8(addr + 7, WalkSpeed);
            rom.write_u16(addr + 8, PortraitId);
            rom.write_u8(addr + 10, BuildStat);

            rom.write_u8(addr + 11, BaseHp);
            rom.write_u8(addr + 12, BaseStr);
            rom.write_u8(addr + 13, BaseSkl);
            rom.write_u8(addr + 14, BaseSpd);
            rom.write_u8(addr + 15, BaseDef);
            rom.write_u8(addr + 16, BaseRes);
            rom.write_u8(addr + 17, Mov);
            rom.write_u8(addr + 18, Con);
            rom.write_u8(addr + 19, ClassStat19);

            rom.write_u8(addr + 20, WepSword);
            rom.write_u8(addr + 21, WepLance);
            rom.write_u8(addr + 22, WepAxe);
            rom.write_u8(addr + 23, WepBow);
            rom.write_u8(addr + 24, WepStaff);
            rom.write_u8(addr + 25, WepAnima);
            rom.write_u8(addr + 26, WepLight);

            rom.write_u8(addr + 27, GrowHp);
            rom.write_u8(addr + 28, GrowStr);
            rom.write_u8(addr + 29, GrowSkl);
            rom.write_u8(addr + 30, GrowSpd);
            rom.write_u8(addr + 31, GrowDef);
            rom.write_u8(addr + 32, GrowRes);
            rom.write_u8(addr + 33, GrowLck);

            rom.write_u8(addr + 34, (uint)(byte)(sbyte)CapHp);
            rom.write_u8(addr + 35, (uint)(byte)(sbyte)CapStr);
            rom.write_u8(addr + 36, (uint)(byte)(sbyte)CapSkl);
            rom.write_u8(addr + 37, (uint)(byte)(sbyte)CapSpd);
            rom.write_u8(addr + 38, (uint)(byte)(sbyte)CapDef);
            rom.write_u8(addr + 39, (uint)(byte)(sbyte)CapRes);

            rom.write_u8(addr + 40, Ability1);
            rom.write_u8(addr + 41, Ability2);
            rom.write_u8(addr + 42, Ability3);
            rom.write_u8(addr + 43, Ability4);

            rom.write_u8(addr + 44, WepRankSword);
            rom.write_u8(addr + 45, WepRankLance);
            rom.write_u8(addr + 46, WepRankAxe);
            rom.write_u8(addr + 47, WepRankBow);
            rom.write_u8(addr + 48, WepRankStaff);
            rom.write_u8(addr + 49, WepRankAnima);
            rom.write_u8(addr + 50, WepRankLight);
            rom.write_u8(addr + 51, WepRankDark);

            rom.write_u32(addr + 52, BattleAnimePtr);
            rom.write_u32(addr + 56, MoveCostPtr);
            rom.write_u32(addr + 60, MoveCostRainPtr);
            rom.write_u32(addr + 64, MoveCostSnowPtr);
            rom.write_u32(addr + 68, TerrainAvoidPtr);
            rom.write_u32(addr + 72, TerrainDefPtr);
            rom.write_u32(addr + 76, TerrainResPtr);

            rom.write_u32(addr + 80, UnknownD80);
        }
    }
}
