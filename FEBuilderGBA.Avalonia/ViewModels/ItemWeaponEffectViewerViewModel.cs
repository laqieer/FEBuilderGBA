using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemWeaponEffectViewerViewModel : ViewModelBase, IDataVerifiable
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
        bool _canWrite;

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
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

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
                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
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

            CanWrite = true;
        }

        public void WriteItemWeaponEffect()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)ItemId);
            rom.write_u8(addr + 1, (byte)Unknown1);
            rom.write_u8(addr + 2, (byte)AnimType);
            rom.write_u8(addr + 3, (byte)Unknown3);
            rom.write_u16(addr + 4, (ushort)EffectId);
            rom.write_u16(addr + 6, (ushort)Unknown6);
            rom.write_u32(addr + 8, MapEffectPointer);
            rom.write_u8(addr + 12, (byte)DamageEffect);
            rom.write_u8(addr + 13, (byte)Motion);
            rom.write_u8(addr + 14, (byte)HitColor);
            rom.write_u8(addr + 15, (byte)Unknown15);
        }

        public int GetListCount() => LoadItemWeaponEffectList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ItemId"] = $"0x{ItemId:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["AnimType"] = $"0x{AnimType:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
                ["EffectId"] = $"0x{EffectId:X04}",
                ["Unknown6"] = $"0x{Unknown6:X04}",
                ["MapEffectPointer"] = $"0x{MapEffectPointer:X08}",
                ["DamageEffect"] = $"0x{DamageEffect:X02}",
                ["Motion"] = $"0x{Motion:X02}",
                ["HitColor"] = $"0x{HitColor:X02}",
                ["Unknown15"] = $"0x{Unknown15:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
