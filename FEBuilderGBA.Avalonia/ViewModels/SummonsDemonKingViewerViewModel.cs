using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SummonsDemonKingViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "B0", "B1", "B2", "B3", "W4", "B6", "B7",
                "D8", "B12", "B13", "B14", "B15",
                "B16", "B17", "B18", "B19"
            });

        uint _currentAddr;
        bool _canWrite;
        uint _unitId;
        uint _classId;
        uint _commander;
        uint _levelGrowth;
        uint _coordinates;
        uint _special, _padding7;
        uint _aiPointer;
        uint _item1, _item2, _item3, _item4;
        uint _primaryAI, _secondaryAI, _targetRecoveryAI, _retreatAI;
        uint _unitGrow;
        uint _level;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint Commander { get => _commander; set => SetField(ref _commander, value); }
        public uint LevelGrowth { get => _levelGrowth; set => SetField(ref _levelGrowth, value); }
        public uint Coordinates { get => _coordinates; set => SetField(ref _coordinates, value); }
        public uint Special { get => _special; set => SetField(ref _special, value); }
        public uint Padding7 { get => _padding7; set => SetField(ref _padding7, value); }
        public uint AIPointer { get => _aiPointer; set => SetField(ref _aiPointer, value); }
        public uint Item1 { get => _item1; set => SetField(ref _item1, value); }
        public uint Item2 { get => _item2; set => SetField(ref _item2, value); }
        public uint Item3 { get => _item3; set => SetField(ref _item3, value); }
        public uint Item4 { get => _item4; set => SetField(ref _item4, value); }
        public uint PrimaryAI { get => _primaryAI; set => SetField(ref _primaryAI, value); }
        public uint SecondaryAI { get => _secondaryAI; set => SetField(ref _secondaryAI, value); }
        public uint TargetRecoveryAI { get => _targetRecoveryAI; set => SetField(ref _targetRecoveryAI, value); }
        public uint RetreatAI { get => _retreatAI; set => SetField(ref _retreatAI, value); }
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
                    string unitName = NameResolver.GetUnitName(unitId);
                    string className = NameResolver.GetClassName(classId);
                    name = $"{U.ToHexString(i)} {unitName} ({className})";
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
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            UnitId = values["B0"];
            ClassId = values["B1"];
            Commander = values["B2"];
            LevelGrowth = values["B3"];
            Coordinates = values["W4"];
            Special = values["B6"];
            Padding7 = values["B7"];
            AIPointer = values["D8"];
            Item1 = values["B12"];
            Item2 = values["B13"];
            Item3 = values["B14"];
            Item4 = values["B15"];
            PrimaryAI = values["B16"];
            SecondaryAI = values["B17"];
            TargetRecoveryAI = values["B18"];
            RetreatAI = values["B19"];
            // Overlapping read: UnitGrow spans B3+B4 as u16
            UnitGrow = rom.u16(addr + 3);
            Level = U.ParseUnitGrowLV(UnitGrow);
            CanWrite = true;
        }

        public void WriteSummonsDemonKing()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 20 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = UnitId, ["B1"] = ClassId,
                ["B2"] = Commander, ["B3"] = LevelGrowth,
                ["W4"] = Coordinates, ["B6"] = Special, ["B7"] = Padding7,
                ["D8"] = AIPointer,
                ["B12"] = Item1, ["B13"] = Item2,
                ["B14"] = Item3, ["B15"] = Item4,
                ["B16"] = PrimaryAI, ["B17"] = SecondaryAI,
                ["B18"] = TargetRecoveryAI, ["B19"] = RetreatAI,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadSummonsDemonKingList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["B2_Commander"] = $"0x{Commander:X02}",
                ["B3_LevelGrowth"] = $"0x{LevelGrowth:X02}",
                ["W4_Coordinates"] = $"0x{Coordinates:X04}",
                ["B6_Special"] = $"0x{Special:X02}",
                ["B7_Padding"] = $"0x{Padding7:X02}",
                ["P8_AIPointer"] = $"0x{AIPointer:X08}",
                ["B12_Item1"] = $"0x{Item1:X02}",
                ["B13_Item2"] = $"0x{Item2:X02}",
                ["B14_Item3"] = $"0x{Item3:X02}",
                ["B15_Item4"] = $"0x{Item4:X02}",
                ["B16_PrimaryAI"] = $"0x{PrimaryAI:X02}",
                ["B17_SecondaryAI"] = $"0x{SecondaryAI:X02}",
                ["B18_TargetRecoveryAI"] = $"0x{TargetRecoveryAI:X02}",
                ["B19_RetreatAI"] = $"0x{RetreatAI:X02}",
                ["UnitGrow"] = $"0x{UnitGrow:X04}",
                ["Level"] = $"0x{Level:X08}",
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
                ["u16@0x03"] = $"0x{rom.u16(a + 3):X04}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
            };
        }
    }
}
