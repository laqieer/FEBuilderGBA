using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemUsagePointerViewerViewModel : ViewModelBase
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
    }
}
