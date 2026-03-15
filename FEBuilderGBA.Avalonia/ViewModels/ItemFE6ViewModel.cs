using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _name = "";
        // W0, W2, W4 (u16 text IDs)
        uint _nameId, _descId, _useDescId;
        // B6 (item number), B7 (weapon type)
        uint _itemNumber, _weaponType;
        // B8-B11 (traits/flags)
        uint _trait1, _trait2, _trait3, _trait4;
        // P12 (stat bonuses ptr), P16 (effectiveness ptr)
        uint _statBonusesPtr, _effectivenessPtr;
        // B20-B25 (combat stats)
        uint _uses, _might, _hit, _weight, _crit, _range;
        // W26 (price)
        uint _price;
        // B28-B31 (weapon rank, icon, usage effect, damage effect)
        uint _weaponRank, _icon, _usageEffect, _damageEffect;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        // W0: Name text ID
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        // W2: Description text ID
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        // W4: Usage description text ID
        public uint UseDescId { get => _useDescId; set => SetField(ref _useDescId, value); }
        // B6: Item number / ID
        public uint ItemNumber { get => _itemNumber; set => SetField(ref _itemNumber, value); }
        // B7: Weapon type
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        // B8: Trait flags 1
        public uint Trait1 { get => _trait1; set => SetField(ref _trait1, value); }
        // B9: Trait flags 2
        public uint Trait2 { get => _trait2; set => SetField(ref _trait2, value); }
        // B10: Trait flags 3
        public uint Trait3 { get => _trait3; set => SetField(ref _trait3, value); }
        // B11: Trait flags 4
        public uint Trait4 { get => _trait4; set => SetField(ref _trait4, value); }
        // P12: Pointer to stat bonuses data
        public uint StatBonusesPtr { get => _statBonusesPtr; set => SetField(ref _statBonusesPtr, value); }
        // P16: Pointer to effectiveness data
        public uint EffectivenessPtr { get => _effectivenessPtr; set => SetField(ref _effectivenessPtr, value); }
        // B20: Durability / uses
        public uint Uses { get => _uses; set => SetField(ref _uses, value); }
        // B21: Might / attack power
        public uint Might { get => _might; set => SetField(ref _might, value); }
        // B22: Hit rate
        public uint Hit { get => _hit; set => SetField(ref _hit, value); }
        // B23: Weight
        public uint Weight { get => _weight; set => SetField(ref _weight, value); }
        // B24: Critical rate
        public uint Crit { get => _crit; set => SetField(ref _crit, value); }
        // B25: Range
        public uint Range { get => _range; set => SetField(ref _range, value); }
        // W26: Unit price
        public uint Price { get => _price; set => SetField(ref _price, value); }
        // B28: Weapon rank / level
        public uint WeaponRank { get => _weaponRank; set => SetField(ref _weaponRank, value); }
        // B29: Icon ID
        public uint Icon { get => _icon; set => SetField(ref _icon, value); }
        // B30: Usage effect (staff effect)
        public uint UsageEffect { get => _usageEffect; set => SetField(ref _usageEffect, value); }
        // B31: Damage additional effect
        public uint DamageEffect { get => _damageEffect; set => SetField(ref _damageEffect, value); }

        public List<AddrResult> LoadItemList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0)
                {
                    uint p12 = rom.u32(addr + 12);
                    uint p16 = rom.u32(addr + 16);
                    if (!U.isPointerOrNULL(p12) || !U.isPointerOrNULL(p16))
                        break;
                }

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = NameResolver.GetTextById(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItem(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo?.item_datasize ?? 32;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Text IDs
            NameId = rom.u16(addr + 0);      // W0
            DescId = rom.u16(addr + 2);      // W2
            UseDescId = rom.u16(addr + 4);   // W4

            try { Name = NameResolver.GetTextById(NameId); }
            catch { Name = "???"; }

            // Identity
            ItemNumber = rom.u8(addr + 6);   // B6
            WeaponType = rom.u8(addr + 7);   // B7

            // Trait flags
            Trait1 = rom.u8(addr + 8);       // B8
            Trait2 = rom.u8(addr + 9);       // B9
            Trait3 = rom.u8(addr + 10);      // B10
            Trait4 = rom.u8(addr + 11);      // B11

            // Pointers
            StatBonusesPtr = rom.u32(addr + 12);  // P12
            EffectivenessPtr = rom.u32(addr + 16); // P16

            // Combat stats
            Uses = rom.u8(addr + 20);        // B20
            Might = rom.u8(addr + 21);       // B21
            Hit = rom.u8(addr + 22);         // B22
            Weight = rom.u8(addr + 23);      // B23
            Crit = rom.u8(addr + 24);        // B24
            Range = rom.u8(addr + 25);       // B25
            Price = rom.u16(addr + 26);      // W26

            // Weapon rank and effects
            WeaponRank = rom.u8(addr + 28);  // B28
            Icon = rom.u8(addr + 29);        // B29
            UsageEffect = rom.u8(addr + 30); // B30
            DamageEffect = rom.u8(addr + 31); // B31

            IsLoaded = true;
        }

        public void WriteItem()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u16(addr + 4, UseDescId);
            rom.write_u8(addr + 6, ItemNumber);
            rom.write_u8(addr + 7, WeaponType);
            rom.write_u8(addr + 8, Trait1);
            rom.write_u8(addr + 9, Trait2);
            rom.write_u8(addr + 10, Trait3);
            rom.write_u8(addr + 11, Trait4);
            rom.write_u32(addr + 12, StatBonusesPtr);
            rom.write_u32(addr + 16, EffectivenessPtr);
            rom.write_u8(addr + 20, Uses);
            rom.write_u8(addr + 21, Might);
            rom.write_u8(addr + 22, Hit);
            rom.write_u8(addr + 23, Weight);
            rom.write_u8(addr + 24, Crit);
            rom.write_u8(addr + 25, Range);
            rom.write_u16(addr + 26, Price);
            rom.write_u8(addr + 28, WeaponRank);
            rom.write_u8(addr + 29, Icon);
            rom.write_u8(addr + 30, UsageEffect);
            rom.write_u8(addr + 31, DamageEffect);
        }

        public int GetListCount() => LoadItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2_DescId"] = $"0x{DescId:X04}",
                ["W4_UseDescId"] = $"0x{UseDescId:X04}",
                ["B6_ItemNumber"] = $"0x{ItemNumber:X02}",
                ["B7_WeaponType"] = $"0x{WeaponType:X02}",
                ["B8_Trait1"] = $"0x{Trait1:X02}",
                ["B9_Trait2"] = $"0x{Trait2:X02}",
                ["B10_Trait3"] = $"0x{Trait3:X02}",
                ["B11_Trait4"] = $"0x{Trait4:X02}",
                ["P12_StatBonuses"] = $"0x{StatBonusesPtr:X08}",
                ["P16_Effectiveness"] = $"0x{EffectivenessPtr:X08}",
                ["B20_Uses"] = $"0x{Uses:X02}",
                ["B21_Might"] = $"0x{Might:X02}",
                ["B22_Hit"] = $"0x{Hit:X02}",
                ["B23_Weight"] = $"0x{Weight:X02}",
                ["B24_Crit"] = $"0x{Crit:X02}",
                ["B25_Range"] = $"0x{Range:X02}",
                ["W26_Price"] = $"0x{Price:X04}",
                ["B28_WeaponRank"] = $"0x{WeaponRank:X02}",
                ["B29_Icon"] = $"0x{Icon:X02}",
                ["B30_UsageEffect"] = $"0x{UsageEffect:X02}",
                ["B31_DamageEffect"] = $"0x{DamageEffect:X02}",
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
            };
        }
    }
}
