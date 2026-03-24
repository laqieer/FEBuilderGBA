using System;
using System.Collections.Generic;
using System.Text;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ClassEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        string _growthSimText = "";
        uint _simLevel = 20;

        // W0, W2: text IDs
        uint _nameId, _descId;
        // B4, B5: class number, promotion level
        uint _classNumber, _promotionLevel;
        // B6, B7: wait icon, walk speed/type
        uint _waitIcon, _walkSpeed;
        // W8: portrait or related u16
        uint _portraitId;
        // B10: sort order
        uint _sortOrder;
        // B11-B16: base stats
        uint _baseHp, _baseStr, _baseSkl, _baseSpd, _baseDef, _baseRes;
        // B17: base constitution, B18: base movement
        uint _baseCon, _baseMov;
        // B19-B25: stat caps (max HP, Str, Skl, Spd, Def, Res, Con)
        uint _maxHp, _maxStr, _maxSkl, _maxSpd, _maxDef, _maxRes, _maxCon;
        // B26: class power / EXP correction
        uint _classPower;
        // B27-B33: growth rates
        uint _growHp, _growStr, _growSkl, _growSpd, _growDef, _growRes, _growLck;
        // b34-b39: signed CC bonus / promotion gains
        int _promoHp, _promoStr, _promoSkl, _promoSpd, _promoDef, _promoRes;
        // B40-B43: ability flags
        uint _ability1, _ability2, _ability3, _ability4;
        // B44-B51: weapon rank levels (Sword, Lance, Axe, Bow, Staff, Anima, Light, Dark)
        uint _wepRankSword, _wepRankLance, _wepRankAxe, _wepRankBow;
        uint _wepRankStaff, _wepRankAnima, _wepRankLight, _wepRankDark;
        // FE7/8: P52=BattleAnime, P56=MoveCost, P60=MoveCostRain, P64=MoveCostSnow
        // FE6:   P48=BattleAnime, P52=MoveCost (no rain/snow)
        uint _battleAnimePtr, _moveCostPtr, _moveCostRainPtr, _moveCostSnowPtr;
        // FE7/8: P68=TerrainAvoid, P72=TerrainDef, P76=TerrainRes
        // FE6:   P56=TerrainAvoid, P60=TerrainDef, P64=TerrainRes
        uint _terrainAvoidPtr, _terrainDefPtr, _terrainResPtr;
        // D80: unknown u32
        uint _unknownD80;

        // Magic split extension fields (patch-dependent)
        int _magicExtBase, _magicExtGrow, _magicExtLimit, _magicExtPromoGain;
        bool _showMagicExtension;
        uint _currentClassIndex; // 0-based index for MagicSplitUtil

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
        // B10: Sort order
        public uint SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }

        // B11-B16: Base stats
        public uint BaseHp { get => _baseHp; set => SetField(ref _baseHp, value); }
        public uint BaseStr { get => _baseStr; set => SetField(ref _baseStr, value); }
        public uint BaseSkl { get => _baseSkl; set => SetField(ref _baseSkl, value); }
        public uint BaseSpd { get => _baseSpd; set => SetField(ref _baseSpd, value); }
        public uint BaseDef { get => _baseDef; set => SetField(ref _baseDef, value); }
        public uint BaseRes { get => _baseRes; set => SetField(ref _baseRes, value); }
        // B17: Constitution
        public uint BaseCon { get => _baseCon; set => SetField(ref _baseCon, value); }
        // B18: Movement
        public uint BaseMov { get => _baseMov; set => SetField(ref _baseMov, value); }

        // B19-B25: Stat caps (max values)
        public uint MaxHp { get => _maxHp; set => SetField(ref _maxHp, value); }
        public uint MaxStr { get => _maxStr; set => SetField(ref _maxStr, value); }
        public uint MaxSkl { get => _maxSkl; set => SetField(ref _maxSkl, value); }
        public uint MaxSpd { get => _maxSpd; set => SetField(ref _maxSpd, value); }
        public uint MaxDef { get => _maxDef; set => SetField(ref _maxDef, value); }
        public uint MaxRes { get => _maxRes; set => SetField(ref _maxRes, value); }
        public uint MaxCon { get => _maxCon; set => SetField(ref _maxCon, value); }
        // B26: Class power / EXP correction
        public uint ClassPower { get => _classPower; set => SetField(ref _classPower, value); }

        // B27-B33: Growth rates
        public uint GrowHp { get => _growHp; set => SetField(ref _growHp, value); }
        public uint GrowStr { get => _growStr; set => SetField(ref _growStr, value); }
        public uint GrowSkl { get => _growSkl; set => SetField(ref _growSkl, value); }
        public uint GrowSpd { get => _growSpd; set => SetField(ref _growSpd, value); }
        public uint GrowDef { get => _growDef; set => SetField(ref _growDef, value); }
        public uint GrowRes { get => _growRes; set => SetField(ref _growRes, value); }
        public uint GrowLck { get => _growLck; set => SetField(ref _growLck, value); }

        // b34-b39: CC bonus / promotion gains (signed)
        public int PromoHp { get => _promoHp; set => SetField(ref _promoHp, value); }
        public int PromoStr { get => _promoStr; set => SetField(ref _promoStr, value); }
        public int PromoSkl { get => _promoSkl; set => SetField(ref _promoSkl, value); }
        public int PromoSpd { get => _promoSpd; set => SetField(ref _promoSpd, value); }
        public int PromoDef { get => _promoDef; set => SetField(ref _promoDef, value); }
        public int PromoRes { get => _promoRes; set => SetField(ref _promoRes, value); }

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

        // Battle animation pointer (FE6: P48, FE7/8: P52)
        public uint BattleAnimePtr { get => _battleAnimePtr; set => SetField(ref _battleAnimePtr, value); }
        // Move cost — normal (FE6: P52, FE7/8: P56)
        public uint MoveCostPtr { get => _moveCostPtr; set => SetField(ref _moveCostPtr, value); }
        // Move cost — rain (FE7/8 only: P60; FE6 has no rain move cost)
        public uint MoveCostRainPtr { get => _moveCostRainPtr; set => SetField(ref _moveCostRainPtr, value); }
        // Move cost — snow (FE7/8 only: P64; FE6 has no snow move cost)
        public uint MoveCostSnowPtr { get => _moveCostSnowPtr; set => SetField(ref _moveCostSnowPtr, value); }
        // Terrain avoid (FE6: P56, FE7/8: P68)
        public uint TerrainAvoidPtr { get => _terrainAvoidPtr; set => SetField(ref _terrainAvoidPtr, value); }
        // Terrain defense (FE6: P60, FE7/8: P72)
        public uint TerrainDefPtr { get => _terrainDefPtr; set => SetField(ref _terrainDefPtr, value); }
        // Terrain resistance (FE6: P64, FE7/8: P76)
        public uint TerrainResPtr { get => _terrainResPtr; set => SetField(ref _terrainResPtr, value); }

        // D80: Unknown u32
        public uint UnknownD80 { get => _unknownD80; set => SetField(ref _unknownD80, value); }

        // Magic split extension properties
        /// <summary>Magic base stat extension (from magic split patch).</summary>
        public int MagicExtBase { get => _magicExtBase; set => SetField(ref _magicExtBase, value); }
        /// <summary>Magic growth rate extension (from magic split patch).</summary>
        public int MagicExtGrow { get => _magicExtGrow; set => SetField(ref _magicExtGrow, value); }
        /// <summary>Magic stat cap extension (from magic split patch).</summary>
        public int MagicExtLimit { get => _magicExtLimit; set => SetField(ref _magicExtLimit, value); }
        /// <summary>Magic promotion gain extension (from magic split patch).</summary>
        public int MagicExtPromoGain { get => _magicExtPromoGain; set => SetField(ref _magicExtPromoGain, value); }
        /// <summary>True when the magic split patch is detected and extension fields should be shown.</summary>
        public bool ShowMagicExtension { get => _showMagicExtension; private set => SetField(ref _showMagicExtension, value); }

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
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>True when the currently loaded ROM is FE6 (72-byte class struct).</summary>
        public bool IsFE6 => CoreState.ROM?.RomInfo?.version == 6;

        public void LoadClass(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.class_datasize ?? 84;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;

            // Text IDs (same offset for all versions)
            NameId = rom.u16(addr + 0);       // W0
            DescId = rom.u16(addr + 2);       // W2
            try { Name = NameResolver.GetTextById(NameId); }
            catch { Name = "???"; }

            // Identity (same offset for all versions)
            ClassNumber = rom.u8(addr + 4);   // B4
            PromotionLevel = rom.u8(addr + 5); // B5
            WaitIcon = rom.u8(addr + 6);      // B6
            WalkSpeed = rom.u8(addr + 7);     // B7
            PortraitId = rom.u16(addr + 8);   // W8
            SortOrder = rom.u8(addr + 10);    // B10

            // Base stats (same offset for all versions)
            BaseHp = rom.u8(addr + 11);       // B11
            BaseStr = rom.u8(addr + 12);      // B12
            BaseSkl = rom.u8(addr + 13);      // B13
            BaseSpd = rom.u8(addr + 14);      // B14
            BaseDef = rom.u8(addr + 15);      // B15
            BaseRes = rom.u8(addr + 16);      // B16
            BaseCon = rom.u8(addr + 17);      // B17 (Con)
            BaseMov = rom.u8(addr + 18);      // B18 (Mov)

            // Stat caps (same offset for all versions)
            MaxHp = rom.u8(addr + 19);        // B19
            MaxStr = rom.u8(addr + 20);       // B20
            MaxSkl = rom.u8(addr + 21);       // B21
            MaxSpd = rom.u8(addr + 22);       // B22
            MaxDef = rom.u8(addr + 23);       // B23
            MaxRes = rom.u8(addr + 24);       // B24
            MaxCon = rom.u8(addr + 25);       // B25
            ClassPower = rom.u8(addr + 26);   // B26 (EXP correction)

            // Growth rates (same offset for all versions)
            GrowHp = rom.u8(addr + 27);       // B27
            GrowStr = rom.u8(addr + 28);      // B28
            GrowSkl = rom.u8(addr + 29);      // B29
            GrowSpd = rom.u8(addr + 30);      // B30
            GrowDef = rom.u8(addr + 31);      // B31
            GrowRes = rom.u8(addr + 32);      // B32
            GrowLck = rom.u8(addr + 33);      // B33

            // CC bonus / promotion gains (signed, same offset for all versions)
            PromoHp = (sbyte)rom.u8(addr + 34);  // b34
            PromoStr = (sbyte)rom.u8(addr + 35); // b35

            // From here, offsets diverge between FE6 (72-byte struct) and FE7/8 (84-byte struct).
            // FE6: b36-b39 = ability flags, B40-B47 = weapon ranks, P48 = battle anim,
            //      P52 = move cost, P56 = terrain avoid, P60 = terrain def,
            //      P64 = terrain res, D68 = unknown
            // FE7/8: b36-b39 = promo gains continued, B40-B43 = ability flags,
            //        B44-B51 = weapon ranks, P52 = battle anim, P56-P64 = move costs,
            //        P68/P72/P76 = terrain ptrs, D80 = unknown
            if (IsFE6)
            {
                // FE6: promo gains are only HP and Str (b34-b35)
                // b36-b39 are ability flags in FE6
                PromoSkl = 0;
                PromoSpd = 0;
                PromoDef = 0;
                PromoRes = 0;

                // Ability flags at +36..+39
                Ability1 = rom.u8(addr + 36);
                Ability2 = rom.u8(addr + 37);
                Ability3 = rom.u8(addr + 38);
                Ability4 = rom.u8(addr + 39);

                // Weapon rank levels at +40..+47
                WepRankSword = rom.u8(addr + 40);
                WepRankLance = rom.u8(addr + 41);
                WepRankAxe = rom.u8(addr + 42);
                WepRankBow = rom.u8(addr + 43);
                WepRankStaff = rom.u8(addr + 44);
                WepRankAnima = rom.u8(addr + 45);
                WepRankLight = rom.u8(addr + 46);
                WepRankDark = rom.u8(addr + 47);

                // Pointers (FE6) — per WinForms ClassFE6Form.GetMoveCostAddrLow:
                // +48 = battle anime, +52 = move cost,
                // +56 = terrain avoid, +60 = terrain def, +64 = terrain res
                BattleAnimePtr = rom.u32(addr + 48);
                MoveCostPtr = rom.u32(addr + 52);
                MoveCostRainPtr = 0;  // FE6 has no rain/snow move costs
                MoveCostSnowPtr = 0;
                TerrainAvoidPtr = rom.u32(addr + 56);
                TerrainDefPtr = rom.u32(addr + 60);
                TerrainResPtr = rom.u32(addr + 64);
                UnknownD80 = rom.u32(addr + 68);  // D68 in FE6
            }
            else
            {
                // FE7/8: promo gains continue at b36-b39
                PromoSkl = (sbyte)rom.u8(addr + 36);
                PromoSpd = (sbyte)rom.u8(addr + 37);
                PromoDef = (sbyte)rom.u8(addr + 38);
                PromoRes = (sbyte)rom.u8(addr + 39);

                // Ability flags at +40..+43
                Ability1 = rom.u8(addr + 40);
                Ability2 = rom.u8(addr + 41);
                Ability3 = rom.u8(addr + 42);
                Ability4 = rom.u8(addr + 43);

                // Weapon rank levels at +44..+51
                WepRankSword = rom.u8(addr + 44);
                WepRankLance = rom.u8(addr + 45);
                WepRankAxe = rom.u8(addr + 46);
                WepRankBow = rom.u8(addr + 47);
                WepRankStaff = rom.u8(addr + 48);
                WepRankAnima = rom.u8(addr + 49);
                WepRankLight = rom.u8(addr + 50);
                WepRankDark = rom.u8(addr + 51);

                // Pointers (FE7/8)
                BattleAnimePtr = rom.u32(addr + 52);
                MoveCostPtr = rom.u32(addr + 56);
                MoveCostRainPtr = rom.u32(addr + 60);
                MoveCostSnowPtr = rom.u32(addr + 64);
                TerrainAvoidPtr = rom.u32(addr + 68);
                TerrainDefPtr = rom.u32(addr + 72);
                TerrainResPtr = rom.u32(addr + 76);
                UnknownD80 = rom.u32(addr + 80);
            }

            // Compute class index from address for MagicSplitUtil
            uint classPtr2 = rom.RomInfo.class_pointer;
            uint classBase2 = rom.p32(classPtr2);
            _currentClassIndex = (addr >= classBase2 && dataSize > 0)
                ? (addr - classBase2) / dataSize
                : 0;

            // Magic split extension (patch-dependent)
            ShowMagicExtension = PatchDetectionService.Instance.HasMagicSplit;
            if (ShowMagicExtension)
            {
                uint cid = _currentClassIndex;
                MagicExtBase = (int)(sbyte)(byte)MagicSplitUtil.GetClassBaseMagicExtends(cid, addr);
                MagicExtGrow = (int)(sbyte)(byte)MagicSplitUtil.GetClassGrowMagicExtends(cid, addr);
                MagicExtLimit = (int)(sbyte)(byte)MagicSplitUtil.GetClassLimitMagicExtends(cid, addr);
                MagicExtPromoGain = (int)(sbyte)(byte)MagicSplitUtil.GetClassPromotionGainMagicExtends(cid, addr);
            }
            else
            {
                MagicExtBase = 0;
                MagicExtGrow = 0;
                MagicExtLimit = 0;
                MagicExtPromoGain = 0;
            }

            CanWrite = true;
            IsLoading = false;
            MarkClean();
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
                ["b36_PromoSkl"] = $"{PromoSkl}",
                ["b37_PromoSpd"] = $"{PromoSpd}",
                ["b38_PromoDef"] = $"{PromoDef}",
                ["b39_PromoRes"] = $"{PromoRes}",
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

            // Common fields (offsets 0x00-0x23, same for all versions)
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
                // Bytes at +36..+47 have the same raw offsets 0x24..0x2F for both versions
                // (ability vs promo gains, then weapons vs ability+weapons)
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
            };

            if (IsFE6)
            {
                // FE6: 72-byte struct — pointers start at +48, end at +68
                report["u32@0x30"] = $"0x{rom.u32(a + 48):X08}";  // P48 battle anim
                report["u32@0x34"] = $"0x{rom.u32(a + 52):X08}";  // P52 move cost
                report["u32@0x38"] = $"0x{rom.u32(a + 56):X08}";  // P56 terrain avoid
                report["u32@0x3C"] = $"0x{rom.u32(a + 60):X08}";  // P60 terrain def
                report["u32@0x40"] = $"0x{rom.u32(a + 64):X08}";  // P64 terrain res
                report["u32@0x44"] = $"0x{rom.u32(a + 68):X08}";  // D68 unknown
            }
            else
            {
                // FE7/8: 84-byte struct — weapon ranks continue at +48..+51, pointers at +52..+80
                report["u8@0x30"] = $"0x{rom.u8(a + 48):X02}";
                report["u8@0x31"] = $"0x{rom.u8(a + 49):X02}";
                report["u8@0x32"] = $"0x{rom.u8(a + 50):X02}";
                report["u8@0x33"] = $"0x{rom.u8(a + 51):X02}";
                report["u32@0x34"] = $"0x{rom.u32(a + 52):X08}";
                report["u32@0x38"] = $"0x{rom.u32(a + 56):X08}";
                report["u32@0x3C"] = $"0x{rom.u32(a + 60):X08}";
                report["u32@0x40"] = $"0x{rom.u32(a + 64):X08}";
                report["u32@0x44"] = $"0x{rom.u32(a + 68):X08}";
                report["u32@0x48"] = $"0x{rom.u32(a + 72):X08}";
                report["u32@0x4C"] = $"0x{rom.u32(a + 76):X08}";
                report["u32@0x50"] = $"0x{rom.u32(a + 80):X08}";
            }

            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            // Common fields (offsets 0x00-0x23, same for all versions)
            var map = new Dictionary<string, string>
            {
                ["W0_NameId"] = "u16@0x00",
                ["W2_DescId"] = "u16@0x02",
                ["B4_ClassNumber"] = "u8@0x04",
                ["B5_PromoLevel"] = "u8@0x05",
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
            };

            if (IsFE6)
            {
                // FE6: +36..+39 = ability flags, +40..+47 = weapon ranks,
                //      P48 = battle anim, P52 = move cost, P56 = terrain avoid,
                //      P60 = terrain def, P64 = terrain res, D68 = unknown
                map["B40_Ability1"] = "u8@0x24";  // +36
                map["B41_Ability2"] = "u8@0x25";  // +37
                map["B42_Ability3"] = "u8@0x26";  // +38
                map["B43_Ability4"] = "u8@0x27";  // +39
                map["B44_WepRankSword"] = "u8@0x28";  // +40
                map["B45_WepRankLance"] = "u8@0x29";  // +41
                map["B46_WepRankAxe"] = "u8@0x2A";    // +42
                map["B47_WepRankBow"] = "u8@0x2B";    // +43
                map["B48_WepRankStaff"] = "u8@0x2C";  // +44
                map["B49_WepRankAnima"] = "u8@0x2D";  // +45
                map["B50_WepRankLight"] = "u8@0x2E";  // +46
                map["B51_WepRankDark"] = "u8@0x2F";   // +47
                map["P52_BattleAnimePtr"] = "u32@0x30";  // +48
                map["P56_MoveCostPtr"] = "u32@0x34";     // +52
                map["P68_TerrainAvoidPtr"] = "u32@0x38"; // +56
                map["P72_TerrainDefPtr"] = "u32@0x3C";   // +60
                map["P76_TerrainResPtr"] = "u32@0x40";   // +64
                map["D80_Unknown"] = "u32@0x44";         // +68
            }
            else
            {
                // FE7/8: +36..+39 = promo gains, +40..+43 = ability flags,
                //        +44..+51 = weapon ranks, P52 = battle anim,
                //        P56..P64 = move costs, P68..P76 = terrain, D80 = unknown
                map["b36_PromoSkl"] = "u8@0x24";
                map["b37_PromoSpd"] = "u8@0x25";
                map["b38_PromoDef"] = "u8@0x26";
                map["b39_PromoRes"] = "u8@0x27";
                map["B40_Ability1"] = "u8@0x28";
                map["B41_Ability2"] = "u8@0x29";
                map["B42_Ability3"] = "u8@0x2A";
                map["B43_Ability4"] = "u8@0x2B";
                map["B44_WepRankSword"] = "u8@0x2C";
                map["B45_WepRankLance"] = "u8@0x2D";
                map["B46_WepRankAxe"] = "u8@0x2E";
                map["B47_WepRankBow"] = "u8@0x2F";
                map["B48_WepRankStaff"] = "u8@0x30";
                map["B49_WepRankAnima"] = "u8@0x31";
                map["B50_WepRankLight"] = "u8@0x32";
                map["B51_WepRankDark"] = "u8@0x33";
                map["P52_BattleAnimePtr"] = "u32@0x34";
                map["P56_MoveCostPtr"] = "u32@0x38";
                map["P60_MoveCostRainPtr"] = "u32@0x3C";
                map["P64_MoveCostSnowPtr"] = "u32@0x40";
                map["P68_TerrainAvoidPtr"] = "u32@0x44";
                map["P72_TerrainDefPtr"] = "u32@0x48";
                map["P76_TerrainResPtr"] = "u32@0x4C";
                map["D80_Unknown"] = "u32@0x50";
            }

            return map;
        }

        // --- Validation ---
        List<string> _validationWarnings = new();
        public List<string> ValidationWarnings { get => _validationWarnings; set => SetField(ref _validationWarnings, value); }
        public bool HasWarnings => _validationWarnings?.Count > 0;

        /// <summary>
        /// Validate class data and return a list of warnings.
        /// These are advisory only and do not block writes.
        /// </summary>
        public List<string> ValidateClass()
        {
            var warnings = new List<string>();

            // Only validate non-zero class indices (index 0 is null/unused)
            if (_currentClassIndex > 0)
            {
                if (NameId == 0)
                    warnings.Add("No name assigned (NameId is 0)");
            }

            if (BaseMov == 0)
                warnings.Add("Movement is 0");

            // Check if all base stats are zero
            if (BaseHp == 0 && BaseStr == 0 && BaseSkl == 0 && BaseSpd == 0 && BaseDef == 0 && BaseRes == 0)
                warnings.Add("All base stats are zero");

            ValidationWarnings = warnings;
            OnPropertyChanged(nameof(HasWarnings));
            return warnings;
        }

        public void WriteClass()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            // Common fields (same offset for all versions)
            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u8(addr + 4, ClassNumber);
            rom.write_u8(addr + 5, PromotionLevel);
            rom.write_u8(addr + 6, WaitIcon);
            rom.write_u8(addr + 7, WalkSpeed);
            rom.write_u16(addr + 8, PortraitId);
            rom.write_u8(addr + 10, SortOrder);

            rom.write_u8(addr + 11, BaseHp);
            rom.write_u8(addr + 12, BaseStr);
            rom.write_u8(addr + 13, BaseSkl);
            rom.write_u8(addr + 14, BaseSpd);
            rom.write_u8(addr + 15, BaseDef);
            rom.write_u8(addr + 16, BaseRes);
            rom.write_u8(addr + 17, BaseCon);
            rom.write_u8(addr + 18, BaseMov);

            rom.write_u8(addr + 19, MaxHp);
            rom.write_u8(addr + 20, MaxStr);
            rom.write_u8(addr + 21, MaxSkl);
            rom.write_u8(addr + 22, MaxSpd);
            rom.write_u8(addr + 23, MaxDef);
            rom.write_u8(addr + 24, MaxRes);
            rom.write_u8(addr + 25, MaxCon);
            rom.write_u8(addr + 26, ClassPower);

            rom.write_u8(addr + 27, GrowHp);
            rom.write_u8(addr + 28, GrowStr);
            rom.write_u8(addr + 29, GrowSkl);
            rom.write_u8(addr + 30, GrowSpd);
            rom.write_u8(addr + 31, GrowDef);
            rom.write_u8(addr + 32, GrowRes);
            rom.write_u8(addr + 33, GrowLck);

            rom.write_u8(addr + 34, (uint)(byte)(sbyte)PromoHp);
            rom.write_u8(addr + 35, (uint)(byte)(sbyte)PromoStr);

            // Version-specific fields from offset 36 onward
            if (IsFE6)
            {
                // FE6: ability flags at +36..+39
                rom.write_u8(addr + 36, Ability1);
                rom.write_u8(addr + 37, Ability2);
                rom.write_u8(addr + 38, Ability3);
                rom.write_u8(addr + 39, Ability4);

                // FE6: weapon rank levels at +40..+47
                rom.write_u8(addr + 40, WepRankSword);
                rom.write_u8(addr + 41, WepRankLance);
                rom.write_u8(addr + 42, WepRankAxe);
                rom.write_u8(addr + 43, WepRankBow);
                rom.write_u8(addr + 44, WepRankStaff);
                rom.write_u8(addr + 45, WepRankAnima);
                rom.write_u8(addr + 46, WepRankLight);
                rom.write_u8(addr + 47, WepRankDark);

                // FE6: pointers at +48..+64, unknown at +68
                // Per WinForms ClassFE6Form: +52=MoveCost, +56=TerrainAvoid,
                // +60=TerrainDef, +64=TerrainRes
                rom.write_u32(addr + 48, BattleAnimePtr);
                rom.write_u32(addr + 52, MoveCostPtr);
                rom.write_u32(addr + 56, TerrainAvoidPtr);
                rom.write_u32(addr + 60, TerrainDefPtr);
                rom.write_u32(addr + 64, TerrainResPtr);
                rom.write_u32(addr + 68, UnknownD80);
            }
            else
            {
                // FE7/8: promo gains continue at b36-b39
                rom.write_u8(addr + 36, (uint)(byte)(sbyte)PromoSkl);
                rom.write_u8(addr + 37, (uint)(byte)(sbyte)PromoSpd);
                rom.write_u8(addr + 38, (uint)(byte)(sbyte)PromoDef);
                rom.write_u8(addr + 39, (uint)(byte)(sbyte)PromoRes);

                // FE7/8: ability flags at +40..+43
                rom.write_u8(addr + 40, Ability1);
                rom.write_u8(addr + 41, Ability2);
                rom.write_u8(addr + 42, Ability3);
                rom.write_u8(addr + 43, Ability4);

                // FE7/8: weapon rank levels at +44..+51
                rom.write_u8(addr + 44, WepRankSword);
                rom.write_u8(addr + 45, WepRankLance);
                rom.write_u8(addr + 46, WepRankAxe);
                rom.write_u8(addr + 47, WepRankBow);
                rom.write_u8(addr + 48, WepRankStaff);
                rom.write_u8(addr + 49, WepRankAnima);
                rom.write_u8(addr + 50, WepRankLight);
                rom.write_u8(addr + 51, WepRankDark);

                // FE7/8: pointers and terrain
                rom.write_u32(addr + 52, BattleAnimePtr);
                rom.write_u32(addr + 56, MoveCostPtr);
                rom.write_u32(addr + 60, MoveCostRainPtr);
                rom.write_u32(addr + 64, MoveCostSnowPtr);
                rom.write_u32(addr + 68, TerrainAvoidPtr);
                rom.write_u32(addr + 72, TerrainDefPtr);
                rom.write_u32(addr + 76, TerrainResPtr);
                rom.write_u32(addr + 80, UnknownD80);
            }

            // Magic split extension write-back
            if (ShowMagicExtension)
            {
                uint cid = _currentClassIndex;
                var undoData = new Undo.UndoData
                {
                    time = DateTime.Now,
                    name = "ClassEditor.MagicExt",
                    list = new System.Collections.Generic.List<Undo.UndoPostion>()
                };
                MagicSplitUtil.WriteClassBaseMagicExtends(cid, addr, (uint)(byte)(sbyte)MagicExtBase, undoData);
                MagicSplitUtil.WriteClassGrowMagicExtends(cid, addr, (uint)(byte)(sbyte)MagicExtGrow, undoData);
                MagicSplitUtil.WriteClassLimitMagicExtends(cid, addr, (uint)(byte)(sbyte)MagicExtLimit, undoData);
                MagicSplitUtil.WriteClassPromotionGainMagicExtends(cid, addr, (uint)(byte)(sbyte)MagicExtPromoGain, undoData);
            }
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

        /// <summary>
        /// Calculate projected stats for a generic unit of this class at LV10, LV20, and SimLevel.
        /// </summary>
        public void CalculateGrowth()
        {
            try
            {
                var sim = new GrowSimulator();
                // Use class base stats as starting point (level 1, no unit bonuses)
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
            catch (Exception ex)
            {
                GrowthSimText = $"Growth sim error: {ex.Message}";
            }
        }

        /// <summary>
        /// Trigger auto-recalculation if not in a loading state and data is loaded.
        /// </summary>
        void AutoRecalcGrowth()
        {
            if (!IsLoading && CanWrite)
                CalculateGrowth();
        }
    }
}
