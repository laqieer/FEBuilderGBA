using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIStealItemViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _item;
        uint _unused1;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Item ID to steal (u8 at offset 0)</summary>
        public uint Item { get => _item; set => SetField(ref _item, value); }
        /// <summary>Unused byte (u8 at offset 1)</summary>
        public uint Unused1 { get => _unused1; set => SetField(ref _unused1, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.ai_steal_item_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0xFF) break;

                uint itemId = rom.u8(addr);
                string itemName = NameResolver.GetItemName(itemId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {itemName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Item = rom.u8(addr + 0);
            Unused1 = rom.u8(addr + 1);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u8(addr + 0, Item);
            rom.write_u8(addr + 1, Unused1);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Item"] = Item.ToString("X02"),
                ["Unused1"] = Unused1.ToString("X02"),
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
                ["u8@0x00_Item"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Unused1"] = $"0x{rom.u8(a + 1):X02}",
            };
        }
    }
}
