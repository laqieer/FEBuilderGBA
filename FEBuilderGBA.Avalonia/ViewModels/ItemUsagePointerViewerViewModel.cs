using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemUsagePointerViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _usabilityPointer;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint UsabilityPointer { get => _usabilityPointer; set => SetField(ref _usabilityPointer, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadItemUsagePointerList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_usability_array_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Read the switch2 address to determine the starting item ID
            uint startItemId = 0;
            uint switchAddr = rom.RomInfo.item_usability_array_switch2_address;
            if (switchAddr != 0 && U.isSafetyOffset(switchAddr + 2))
            {
                startItemId = rom.u8(switchAddr);
            }

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint funcPtr = rom.u32(addr);
                // Each entry should be a pointer or null
                if (!U.isPointerOrNULL(funcPtr)) break;

                uint itemId = startItemId + i;
                string name = U.ToHexString(itemId) + " Func=0x" + funcPtr.ToString("X08");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemUsagePointer(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UsabilityPointer = rom.u32(addr);

            IsLoaded = true;
        }

        public int GetListCount() => LoadItemUsagePointerList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UsabilityPointer"] = $"0x{UsabilityPointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
