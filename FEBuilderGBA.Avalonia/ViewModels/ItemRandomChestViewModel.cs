using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemRandomChestViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _itemId, _probability;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Probability { get => _probability; set => SetField(ref _probability, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Random Chest Items", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ItemId = rom.u8(addr + 0);
            Probability = rom.u8(addr + 1);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            // Match WinForms PreWrite logic: if item is 0, clear probability too
            if (ItemId == 0)
            {
                Probability = 0;
            }

            rom.write_u8(addr + 0, (byte)ItemId);
            rom.write_u8(addr + 1, (byte)Probability);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ItemId"] = $"0x{ItemId:X02}",
                ["Probability"] = $"0x{Probability:X02}",
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
                ["u8@0x00_ItemId"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Probability"] = $"0x{rom.u8(a + 1):X02}",
            };
        }
    }
}
