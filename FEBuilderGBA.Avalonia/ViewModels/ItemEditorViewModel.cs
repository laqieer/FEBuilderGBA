using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        string _name = "";
        uint _nameId, _descId, _useDescId;
        uint _weaponType, _rank, _might, _hit, _weight, _crit, _range;
        uint _uses, _price;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint DescId { get => _descId; set => SetField(ref _descId, value); }
        public uint UseDescId { get => _useDescId; set => SetField(ref _useDescId, value); }
        public uint WeaponType { get => _weaponType; set => SetField(ref _weaponType, value); }
        public uint Rank { get => _rank; set => SetField(ref _rank, value); }
        public uint Might { get => _might; set => SetField(ref _might, value); }
        public uint Hit { get => _hit; set => SetField(ref _hit, value); }
        public uint Weight { get => _weight; set => SetField(ref _weight, value); }
        public uint Crit { get => _crit; set => SetField(ref _crit, value); }
        public uint Range { get => _range; set => SetField(ref _range, value); }
        public uint Uses { get => _uses; set => SetField(ref _uses, value); }
        public uint Price { get => _price; set => SetField(ref _price, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

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
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

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

            // Bounds check: item data extends to at least addr + 0x20 (offset 0x1E + 2 bytes)
            uint minSize = 0x20;
            if (addr + minSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            NameId = rom.u16(addr + 0);
            DescId = rom.u16(addr + 2);
            UseDescId = rom.u16(addr + 4);
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // Item struct layout (FE8U-style)
            // 0x00: Name ID (2)
            // 0x02: Desc ID (2)
            // 0x04: Use Desc ID (2)
            // 0x06: Item number (1)
            // 0x07: Weapon type (1)
            // 0x11: Rank (1)
            // 0x17: Might (1)
            // 0x18: Hit (1)
            // 0x19: Weight (1)
            // 0x1A: Crit (1)
            // 0x1B: Range (1)
            // 0x1C: Uses (1) — approximate, varies by version
            // 0x1E: Price (2)

            WeaponType = rom.u8(addr + 0x07);
            Rank = rom.u8(addr + 0x11);
            Might = rom.u8(addr + 0x17);
            Hit = rom.u8(addr + 0x18);
            Weight = rom.u8(addr + 0x19);
            Crit = rom.u8(addr + 0x1A);
            Range = rom.u8(addr + 0x1B);
            Uses = rom.u8(addr + 0x1C);
            Price = rom.u16(addr + 0x1E);

            CanWrite = true;
        }

        public void WriteItem()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, NameId);
            rom.write_u16(addr + 2, DescId);
            rom.write_u16(addr + 4, UseDescId);
            rom.write_u8(addr + 0x07, WeaponType);
            rom.write_u8(addr + 0x11, Rank);
            rom.write_u8(addr + 0x17, Might);
            rom.write_u8(addr + 0x18, Hit);
            rom.write_u8(addr + 0x19, Weight);
            rom.write_u8(addr + 0x1A, Crit);
            rom.write_u8(addr + 0x1B, Range);
            rom.write_u8(addr + 0x1C, Uses);
            rom.write_u16(addr + 0x1E, Price);
        }
    }
}
