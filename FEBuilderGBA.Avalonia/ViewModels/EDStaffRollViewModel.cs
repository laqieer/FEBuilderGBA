using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDStaffRollViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _dataPointer;
        uint _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataPointer { get => _dataPointer; set => SetField(ref _dataPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadEDStaffRollList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ed_staffroll_image_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 12; i++) // Staff roll is limited to ~12 entries
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " Staff Roll";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEDStaffRoll(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataPointer = rom.u32(addr);
            PalettePointer = rom.u32(addr + 4);
            IsLoaded = true;
        }
    }
}
