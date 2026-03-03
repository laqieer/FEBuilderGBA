using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterItemViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _itemId;
        uint _dropRate;
        uint _unknown1, _unknown2, _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint DropRate { get => _dropRate; set => SetField(ref _dropRate, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

        public List<AddrResult> LoadMonsterItemList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 5);
                if (addr + 5 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                string name = U.ToHexString(i) + " Monster Item";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterItem(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 5 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ItemId = rom.u8(addr + 0);
            DropRate = rom.u8(addr + 1);
            Unknown1 = rom.u8(addr + 2);
            Unknown2 = rom.u8(addr + 3);
            Unknown3 = rom.u8(addr + 4);
            IsLoaded = true;
        }
    }
}
