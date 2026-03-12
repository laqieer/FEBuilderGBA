using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterProbabilityViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11" });

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
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            ClassId1 = values["B0"];
            ClassId2 = values["B1"];
            ClassId3 = values["B2"];
            ClassId4 = values["B3"];
            ClassId5 = values["B4"];
            Prob1 = values["B5"];
            Prob2 = values["B6"];
            Prob3 = values["B7"];
            Prob4 = values["B8"];
            Prob5 = values["B9"];
            Unknown1 = values["B10"];
            Unknown2 = values["B11"];
            CanWrite = true;
        }

        public void WriteMonsterProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 12 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = ClassId1, ["B1"] = ClassId2, ["B2"] = ClassId3,
                ["B3"] = ClassId4, ["B4"] = ClassId5,
                ["B5"] = Prob1, ["B6"] = Prob2, ["B7"] = Prob3,
                ["B8"] = Prob4, ["B9"] = Prob5,
                ["B10"] = Unknown1, ["B11"] = Unknown2,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
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
