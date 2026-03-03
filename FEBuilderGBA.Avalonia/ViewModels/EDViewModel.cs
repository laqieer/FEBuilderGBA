using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitId;
        uint _flag;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint Flag { get => _flag; set => SetField(ref _flag, value); }

        public List<AddrResult> LoadEDList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ed_1_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                // Termination: all zeros
                if (rom.u32(addr) == 0x00) break;

                uint uid = rom.u8(addr);
                string name = U.ToHexString(i) + " Unit 0x" + uid.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadED(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr);
            Flag = rom.u8(addr + 1);
            IsLoaded = true;
        }
    }
}
