using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemWeaponTriangleViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _weaponType1;
        uint _weaponType2;
        uint _bonus;
        uint _penalty;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint WeaponType1 { get => _weaponType1; set => SetField(ref _weaponType1, value); }
        public uint WeaponType2 { get => _weaponType2; set => SetField(ref _weaponType2, value); }
        public uint Bonus { get => _bonus; set => SetField(ref _bonus, value); }
        public uint Penalty { get => _penalty; set => SetField(ref _penalty, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadItemWeaponTriangleList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_cornered_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 255) break;

                uint w1 = rom.u8(addr);
                uint w2 = rom.u8(addr + 1);
                string name = U.ToHexString(i) + " Type 0x" + w1.ToString("X02") + " -> 0x" + w2.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemWeaponTriangle(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            WeaponType1 = rom.u8(addr + 0);
            WeaponType2 = rom.u8(addr + 1);
            Bonus = rom.u8(addr + 2);
            Penalty = rom.u8(addr + 3);

            IsLoaded = true;
        }
    }
}
