using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemWeaponEffectViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _itemId;
        uint _unknown1;
        uint _animType;
        uint _unknown3;
        uint _effectId;
        uint _unknown6;
        uint _mapEffectPointer;
        uint _damageEffect;
        uint _motion;
        uint _hitColor;
        uint _unknown15;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint AnimType { get => _animType; set => SetField(ref _animType, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }
        public uint EffectId { get => _effectId; set => SetField(ref _effectId, value); }
        public uint Unknown6 { get => _unknown6; set => SetField(ref _unknown6, value); }
        public uint MapEffectPointer { get => _mapEffectPointer; set => SetField(ref _mapEffectPointer, value); }
        public uint DamageEffect { get => _damageEffect; set => SetField(ref _damageEffect, value); }
        public uint Motion { get => _motion; set => SetField(ref _motion, value); }
        public uint HitColor { get => _hitColor; set => SetField(ref _hitColor, value); }
        public uint Unknown15 { get => _unknown15; set => SetField(ref _unknown15, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadItemWeaponEffectList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_effect_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 16);
                if (addr + 15 >= (uint)rom.Data.Length) break;

                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.u32(addr) == 0 && rom.u32(addr + 4) == 0
                    && rom.u32(addr + 8) == 0 && rom.u32(addr + 12) == 0)
                    break;

                uint itemId = rom.u8(addr);
                string name = U.ToHexString(i) + " ItemID=0x" + itemId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemWeaponEffect(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 15 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ItemId = rom.u8(addr + 0);
            Unknown1 = rom.u8(addr + 1);
            AnimType = rom.u8(addr + 2);
            Unknown3 = rom.u8(addr + 3);
            EffectId = rom.u16(addr + 4);
            Unknown6 = rom.u16(addr + 6);
            MapEffectPointer = rom.u32(addr + 8);
            DamageEffect = rom.u8(addr + 12);
            Motion = rom.u8(addr + 13);
            HitColor = rom.u8(addr + 14);
            Unknown15 = rom.u8(addr + 15);

            IsLoaded = true;
        }
    }
}
