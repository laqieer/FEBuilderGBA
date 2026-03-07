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
        // B44-B51: additional fields (weapon levels continued or flags)
        uint _b44, _b45, _b46, _b47, _b48, _b49, _b50, _b51;
        // P52, P56, P60, P64, P68, P72, P76: pointers
        uint _ptr52, _ptr56, _ptr60, _ptr64, _ptr68, _ptr72, _ptr76;
        // D80: u32 field
        uint _d80;

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

        // B44-B51: Additional fields
        public uint B44 { get => _b44; set => SetField(ref _b44, value); }
        public uint B45 { get => _b45; set => SetField(ref _b45, value); }
        public uint B46 { get => _b46; set => SetField(ref _b46, value); }
        public uint B47 { get => _b47; set => SetField(ref _b47, value); }
        public uint B48 { get => _b48; set => SetField(ref _b48, value); }
        public uint B49 { get => _b49; set => SetField(ref _b49, value); }
        public uint B50 { get => _b50; set => SetField(ref _b50, value); }
        public uint B51 { get => _b51; set => SetField(ref _b51, value); }

        // P52-P76: Pointers
        public uint Ptr52 { get => _ptr52; set => SetField(ref _ptr52, value); }
        public uint Ptr56 { get => _ptr56; set => SetField(ref _ptr56, value); }
        public uint Ptr60 { get => _ptr60; set => SetField(ref _ptr60, value); }
        public uint Ptr64 { get => _ptr64; set => SetField(ref _ptr64, value); }
        public uint Ptr68 { get => _ptr68; set => SetField(ref _ptr68, value); }
        public uint Ptr72 { get => _ptr72; set => SetField(ref _ptr72, value); }
        public uint Ptr76 { get => _ptr76; set => SetField(ref _ptr76, value); }

        // D80: u32 field
        public uint D80 { get => _d80; set => SetField(ref _d80, value); }

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

            // Additional fields
            B44 = rom.u8(addr + 44);          // B44
            B45 = rom.u8(addr + 45);          // B45
            B46 = rom.u8(addr + 46);          // B46
            B47 = rom.u8(addr + 47);          // B47
            B48 = rom.u8(addr + 48);          // B48
            B49 = rom.u8(addr + 49);          // B49
            B50 = rom.u8(addr + 50);          // B50
            B51 = rom.u8(addr + 51);          // B51

            // Pointers
            Ptr52 = rom.u32(addr + 52);       // P52
            Ptr56 = rom.u32(addr + 56);       // P56
            Ptr60 = rom.u32(addr + 60);       // P60
            Ptr64 = rom.u32(addr + 64);       // P64
            Ptr68 = rom.u32(addr + 68);       // P68
            Ptr72 = rom.u32(addr + 72);       // P72
            Ptr76 = rom.u32(addr + 76);       // P76

            // D80
            D80 = rom.u32(addr + 80);         // D80

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
                ["B17_Mov"] = $"0x{Mov:X02}",
                ["B27_GrowHp"] = $"0x{GrowHp:X02}",
                ["b34_CapHp"] = $"{CapHp}",
                ["B40_Ability1"] = $"0x{Ability1:X02}",
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
                ["u16@0"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@2"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@4"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@5"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@11"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@17"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@27"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@34"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@40"] = $"0x{rom.u8(a + 40):X02}",
                ["u32@52"] = $"0x{rom.u32(a + 52):X08}",
                ["u32@80"] = $"0x{rom.u32(a + 80):X08}",
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

            rom.write_u8(addr + 44, B44);
            rom.write_u8(addr + 45, B45);
            rom.write_u8(addr + 46, B46);
            rom.write_u8(addr + 47, B47);
            rom.write_u8(addr + 48, B48);
            rom.write_u8(addr + 49, B49);
            rom.write_u8(addr + 50, B50);
            rom.write_u8(addr + 51, B51);

            rom.write_u32(addr + 52, Ptr52);
            rom.write_u32(addr + 56, Ptr56);
            rom.write_u32(addr + 60, Ptr60);
            rom.write_u32(addr + 64, Ptr64);
            rom.write_u32(addr + 68, Ptr68);
            rom.write_u32(addr + 72, Ptr72);
            rom.write_u32(addr + 76, Ptr76);

            rom.write_u32(addr + 80, D80);
        }
    }
}
