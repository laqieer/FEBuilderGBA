using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterProbabilityViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _classId1, _classId2, _classId3, _classId4, _classId5;
        uint _prob1, _prob2, _prob3, _prob4, _prob5;
        uint _unknown1, _unknown2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
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
            CanWrite = true;
        }

        public void WriteMonsterProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 12 > (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr + 0, (byte)ClassId1);
            rom.write_u8(CurrentAddr + 1, (byte)ClassId2);
            rom.write_u8(CurrentAddr + 2, (byte)ClassId3);
            rom.write_u8(CurrentAddr + 3, (byte)ClassId4);
            rom.write_u8(CurrentAddr + 4, (byte)ClassId5);
            rom.write_u8(CurrentAddr + 5, (byte)Prob1);
            rom.write_u8(CurrentAddr + 6, (byte)Prob2);
            rom.write_u8(CurrentAddr + 7, (byte)Prob3);
            rom.write_u8(CurrentAddr + 8, (byte)Prob4);
            rom.write_u8(CurrentAddr + 9, (byte)Prob5);
            rom.write_u8(CurrentAddr + 10, (byte)Unknown1);
            rom.write_u8(CurrentAddr + 11, (byte)Unknown2);
        }

        public int GetListCount() => LoadMonsterProbabilityList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ClassId1"] = $"0x{ClassId1:X02}",
                ["ClassId2"] = $"0x{ClassId2:X02}",
                ["ClassId3"] = $"0x{ClassId3:X02}",
                ["ClassId4"] = $"0x{ClassId4:X02}",
                ["ClassId5"] = $"0x{ClassId5:X02}",
                ["Prob1"] = $"0x{Prob1:X02}",
                ["Prob2"] = $"0x{Prob2:X02}",
                ["Prob3"] = $"0x{Prob3:X02}",
                ["Prob4"] = $"0x{Prob4:X02}",
                ["Prob5"] = $"0x{Prob5:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
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
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
