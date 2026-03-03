using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportAttributeViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _affinityType;
        uint _attackBonus;
        uint _defenseBonus;
        uint _hitBonus;
        uint _avoidBonus;
        uint _critBonus;
        uint _critAvoidBonus;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint AffinityType { get => _affinityType; set => SetField(ref _affinityType, value); }
        public uint AttackBonus { get => _attackBonus; set => SetField(ref _attackBonus, value); }
        public uint DefenseBonus { get => _defenseBonus; set => SetField(ref _defenseBonus, value); }
        public uint HitBonus { get => _hitBonus; set => SetField(ref _hitBonus, value); }
        public uint AvoidBonus { get => _avoidBonus; set => SetField(ref _avoidBonus, value); }
        public uint CritBonus { get => _critBonus; set => SetField(ref _critBonus, value); }
        public uint CritAvoidBonus { get => _critAvoidBonus; set => SetField(ref _critAvoidBonus, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadSupportAttributeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_attribute_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Each entry is 8 bytes; iterate until first byte is 0
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 7 >= (uint)rom.Data.Length) break;

                uint v = rom.u8(addr);
                if (v == 0 && i > 0) break;

                string name = U.ToHexString(i + 1) + " Affinity 0x" + v.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportAttribute(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 7 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            AffinityType = rom.u8(addr + 0);
            AttackBonus = rom.u8(addr + 1);
            DefenseBonus = rom.u8(addr + 2);
            HitBonus = rom.u8(addr + 3);
            AvoidBonus = rom.u8(addr + 4);
            CritBonus = rom.u8(addr + 5);
            CritAvoidBonus = rom.u8(addr + 6);

            IsLoaded = true;
        }
    }
}
