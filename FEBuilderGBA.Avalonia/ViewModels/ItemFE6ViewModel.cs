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
        uint _nameId, _descId, _descId2;
        uint _itemType, _itemNumber;
        uint _statBonusPtr, _effectivenessPtr;
        uint _pricePerUse, _uses, _weaponEffect;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        public uint DescId2 { get => _descId2; set => SetField(ref _descId2, value); }
        public uint ItemType { get => _itemType; set => SetField(ref _itemType, value); }
        public uint ItemNumber { get => _itemNumber; set => SetField(ref _itemNumber, value); }
        public uint StatBonusPtr { get => _statBonusPtr; set => SetField(ref _statBonusPtr, value); }
        public uint EffectivenessPtr { get => _effectivenessPtr; set => SetField(ref _effectivenessPtr, value); }
        public uint PricePerUse { get => _pricePerUse; set => SetField(ref _pricePerUse, value); }
        public uint Uses { get => _uses; set => SetField(ref _uses, value); }
        public uint WeaponEffect { get => _weaponEffect; set => SetField(ref _weaponEffect, value); }

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

                // Validate pointer fields at offsets 12 and 16
                if (i > 0)
                {
                    uint p12 = rom.u32(addr + 12);
                    uint p16 = rom.u32(addr + 16);
                    if (!U.isPointerOrNULL(p12) || !U.isPointerOrNULL(p16))
                        break;
                }

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
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

            uint dataSize = rom.RomInfo.item_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            NameId = rom.u16(addr + 0);
            DescId = rom.u16(addr + 2);
            DescId2 = rom.u16(addr + 4);
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            ItemType = rom.u8(addr + 6);
            ItemNumber = rom.u8(addr + 7);
            StatBonusPtr = rom.u32(addr + 12);
            EffectivenessPtr = rom.u32(addr + 16);
            PricePerUse = rom.u8(addr + 20);
            Uses = rom.u16(addr + 26);
            WeaponEffect = rom.u8(addr + 31);

            IsLoaded = true;
        }

        public int GetListCount() => LoadItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["NameId"] = $"0x{NameId:X04}",
                ["DescId"] = $"0x{DescId:X04}",
                ["DescId2"] = $"0x{DescId2:X04}",
                ["ItemType"] = $"0x{ItemType:X02}",
                ["ItemNumber"] = $"0x{ItemNumber:X02}",
                ["StatBonusPtr"] = $"0x{StatBonusPtr:X08}",
                ["EffectivenessPtr"] = $"0x{EffectivenessPtr:X08}",
                ["PricePerUse"] = $"0x{PricePerUse:X02}",
                ["Uses"] = $"0x{Uses:X04}",
                ["WeaponEffect"] = $"0x{WeaponEffect:X02}",
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
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u16@0x1A"] = $"0x{rom.u16(a + 26):X04}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
            };
        }
    }
}
