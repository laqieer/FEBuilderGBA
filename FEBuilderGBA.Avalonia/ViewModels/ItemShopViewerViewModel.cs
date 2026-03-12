using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemShopViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _itemId;
        uint _quantity;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Quantity { get => _quantity; set => SetField(ref _quantity, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

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

                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
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

            CanWrite = true;
        }

        public void WriteItemShop()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr, ItemId);
            rom.write_u8(addr + 1, Quantity);
        }

        public int GetListCount() => LoadItemShopList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ItemId"] = $"0x{ItemId:X02}",
                ["Quantity"] = $"0x{Quantity:X02}",
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
            };
        }
    }
}
