using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for EventUnitForm (FE8 — 20-byte unit placement blocks).
    /// Layout: UnitID(B0), ClassID(B1), LeaderUnitID(B2), UnitInfo(B3),
    ///         UnitGrowth(W4), Reserved6(B6), CoordCount(B7), CoordPointer(P8),
    ///         Item1(B12), Item2(B13), Item3(B14), Item4(B15),
    ///         AI1Primary(B16), AI2Secondary(B17), AI3TargetRecovery(B18), AI4Retreat(B19).
    /// </summary>
    public class EventUnitViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;

        uint _unitID, _classID, _leaderUnitID, _unitInfo;
        uint _unitGrowth;
        uint _reserved6, _coordCount;
        uint _coordPointer;
        uint _item1, _item2, _item3, _item4;
        uint _ai1Primary, _ai2Secondary, _ai3TargetRecovery, _ai4Retreat;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint UnitID { get => _unitID; set => SetField(ref _unitID, value); }
        public uint ClassID { get => _classID; set => SetField(ref _classID, value); }
        public uint LeaderUnitID { get => _leaderUnitID; set => SetField(ref _leaderUnitID, value); }
        public uint UnitInfo { get => _unitInfo; set => SetField(ref _unitInfo, value); }
        public uint UnitGrowth { get => _unitGrowth; set => SetField(ref _unitGrowth, value); }
        public uint Reserved6 { get => _reserved6; set => SetField(ref _reserved6, value); }
        public uint CoordCount { get => _coordCount; set => SetField(ref _coordCount, value); }
        public uint CoordPointer { get => _coordPointer; set => SetField(ref _coordPointer, value); }
        public uint Item1 { get => _item1; set => SetField(ref _item1, value); }
        public uint Item2 { get => _item2; set => SetField(ref _item2, value); }
        public uint Item3 { get => _item3; set => SetField(ref _item3, value); }
        public uint Item4 { get => _item4; set => SetField(ref _item4, value); }
        public uint AI1Primary { get => _ai1Primary; set => SetField(ref _ai1Primary, value); }
        public uint AI2Secondary { get => _ai2Secondary; set => SetField(ref _ai2Secondary, value); }
        public uint AI3TargetRecovery { get => _ai3TargetRecovery; set => SetField(ref _ai3TargetRecovery, value); }
        public uint AI4Retreat { get => _ai4Retreat; set => SetField(ref _ai4Retreat, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // EventUnit is pointer-based (not a fixed table).
            // Return a placeholder list; actual list is built from event script pointers.
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Event Unit Placement (FE8)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.eventunit_data_size; // 20 for FE8
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            UnitID = rom.u8(addr + 0);
            ClassID = rom.u8(addr + 1);
            LeaderUnitID = rom.u8(addr + 2);
            UnitInfo = rom.u8(addr + 3);
            UnitGrowth = rom.u16(addr + 4);
            Reserved6 = rom.u8(addr + 6);
            CoordCount = rom.u8(addr + 7);
            CoordPointer = rom.u32(addr + 8);
            Item1 = rom.u8(addr + 12);
            Item2 = rom.u8(addr + 13);
            Item3 = rom.u8(addr + 14);
            Item4 = rom.u8(addr + 15);
            AI1Primary = rom.u8(addr + 16);
            AI2Secondary = rom.u8(addr + 17);
            AI3TargetRecovery = rom.u8(addr + 18);
            AI4Retreat = rom.u8(addr + 19);

            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)UnitID);
            rom.write_u8(addr + 1, (byte)ClassID);
            rom.write_u8(addr + 2, (byte)LeaderUnitID);
            rom.write_u8(addr + 3, (byte)UnitInfo);
            rom.write_u16(addr + 4, (ushort)UnitGrowth);
            rom.write_u8(addr + 6, (byte)Reserved6);
            rom.write_u8(addr + 7, (byte)CoordCount);
            rom.write_u32(addr + 8, CoordPointer);
            rom.write_u8(addr + 12, (byte)Item1);
            rom.write_u8(addr + 13, (byte)Item2);
            rom.write_u8(addr + 14, (byte)Item3);
            rom.write_u8(addr + 15, (byte)Item4);
            rom.write_u8(addr + 16, (byte)AI1Primary);
            rom.write_u8(addr + 17, (byte)AI2Secondary);
            rom.write_u8(addr + 18, (byte)AI3TargetRecovery);
            rom.write_u8(addr + 19, (byte)AI4Retreat);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitID"] = $"0x{UnitID:X02}",
                ["ClassID"] = $"0x{ClassID:X02}",
                ["LeaderUnitID"] = $"0x{LeaderUnitID:X02}",
                ["UnitInfo"] = $"0x{UnitInfo:X02}",
                ["UnitGrowth"] = $"0x{UnitGrowth:X04}",
                ["Reserved6"] = $"0x{Reserved6:X02}",
                ["CoordCount"] = $"0x{CoordCount:X02}",
                ["CoordPointer"] = $"0x{CoordPointer:X08}",
                ["Item1"] = $"0x{Item1:X02}",
                ["Item2"] = $"0x{Item2:X02}",
                ["Item3"] = $"0x{Item3:X02}",
                ["Item4"] = $"0x{Item4:X02}",
                ["AI1Primary"] = $"0x{AI1Primary:X02}",
                ["AI2Secondary"] = $"0x{AI2Secondary:X02}",
                ["AI3TargetRecovery"] = $"0x{AI3TargetRecovery:X02}",
                ["AI4Retreat"] = $"0x{AI4Retreat:X02}",
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
                ["UnitID@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["ClassID@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["LeaderUnitID@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["UnitInfo@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["UnitGrowth@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["Reserved6@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["CoordCount@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["CoordPointer@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["Item1@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["Item2@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["Item3@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["Item4@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["AI1Primary@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["AI2Secondary@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["AI3TargetRecovery@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["AI4Retreat@0x13"] = $"0x{rom.u8(a + 19):X02}",
            };
        }
    }
}
