using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SummonUnitViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitId;
        uint _unknown;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint Unknown { get => _unknown; set => SetField(ref _unknown, value); }

        public List<AddrResult> LoadSummonUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.summon_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 2);
                if (addr + 2 > (uint)rom.Data.Length) break;

                uint unitId = rom.u8(addr);
                if (unitId == 0x00) break;

                string name = U.ToHexString(unitId) + " Summon Unit";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSummonUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr + 0);
            Unknown = rom.u8(addr + 1);
            IsLoaded = true;
        }
    }
}
