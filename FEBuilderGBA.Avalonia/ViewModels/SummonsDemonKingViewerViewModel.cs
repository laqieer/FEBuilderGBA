using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SummonsDemonKingViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitId;
        uint _classId;
        uint _unknown1;
        uint _unitGrow;
        uint _level;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint UnitGrow { get => _unitGrow; set => SetField(ref _unitGrow, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }

        public List<AddrResult> LoadSummonsDemonKingList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.summons_demon_king_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint maxCount = 0;
            uint countAddr = rom.RomInfo.summons_demon_king_count_address;
            if (countAddr != 0 && U.isSafetyOffset(countAddr))
            {
                maxCount = rom.u8(countAddr);
            }
            if (maxCount == 0 || maxCount >= 100) maxCount = 20;

            var result = new List<AddrResult>();
            for (uint i = 0; i <= maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                uint unitId = rom.u8(addr);
                string name;
                if (unitId == 0)
                {
                    name = U.ToHexString(i) + " -EMPTY-";
                }
                else
                {
                    uint classId = rom.u8(addr + 1);
                    name = U.ToHexString(i) + " Demon King Summon Unit:0x" + unitId.ToString("X02") + " Class:0x" + classId.ToString("X02");
                }
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSummonsDemonKing(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr + 0);
            ClassId = rom.u8(addr + 1);
            Unknown1 = rom.u8(addr + 2);
            UnitGrow = rom.u16(addr + 3);
            Level = U.ParseUnitGrowLV(UnitGrow);
            IsLoaded = true;
        }
    }
}
