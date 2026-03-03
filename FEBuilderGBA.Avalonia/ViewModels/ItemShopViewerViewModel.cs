using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemShopViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _itemId;
        uint _quantity;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Quantity { get => _quantity; set => SetField(ref _quantity, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadItemShopList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_shop_hensei_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 2);
                if (addr + 1 >= (uint)rom.Data.Length) break;

                uint itemId = rom.u8(addr);
                if (itemId == 0x00) break;

                string name = U.ToHexString(i) + " ItemID=0x" + itemId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemShop(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ItemId = rom.u8(addr);
            Quantity = rom.u8(addr + 1);

            IsLoaded = true;
        }
    }
}
