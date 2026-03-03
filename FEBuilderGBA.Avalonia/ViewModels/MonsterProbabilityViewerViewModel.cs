using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterProbabilityViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _classId1, _classId2, _classId3, _classId4, _classId5;
        uint _prob1, _prob2, _prob3, _prob4, _prob5;
        uint _unknown1, _unknown2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ClassId1 { get => _classId1; set => SetField(ref _classId1, value); }
        public uint ClassId2 { get => _classId2; set => SetField(ref _classId2, value); }
        public uint ClassId3 { get => _classId3; set => SetField(ref _classId3, value); }
        public uint ClassId4 { get => _classId4; set => SetField(ref _classId4, value); }
        public uint ClassId5 { get => _classId5; set => SetField(ref _classId5, value); }
        public uint Prob1 { get => _prob1; set => SetField(ref _prob1, value); }
        public uint Prob2 { get => _prob2; set => SetField(ref _prob2, value); }
        public uint Prob3 { get => _prob3; set => SetField(ref _prob3, value); }
        public uint Prob4 { get => _prob4; set => SetField(ref _prob4, value); }
        public uint Prob5 { get => _prob5; set => SetField(ref _prob5, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }

        public List<AddrResult> LoadMonsterProbabilityList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_probability_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                string name = U.ToHexString(i) + " Monster Prob";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterProbability(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ClassId1 = rom.u8(addr + 0);
            ClassId2 = rom.u8(addr + 1);
            ClassId3 = rom.u8(addr + 2);
            ClassId4 = rom.u8(addr + 3);
            ClassId5 = rom.u8(addr + 4);
            Prob1 = rom.u8(addr + 5);
            Prob2 = rom.u8(addr + 6);
            Prob3 = rom.u8(addr + 7);
            Prob4 = rom.u8(addr + 8);
            Prob5 = rom.u8(addr + 9);
            Unknown1 = rom.u8(addr + 10);
            Unknown2 = rom.u8(addr + 11);
            IsLoaded = true;
        }
    }
}
