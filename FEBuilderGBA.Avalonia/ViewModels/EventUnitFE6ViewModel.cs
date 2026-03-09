using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for EventUnitFE6Form (FE6 — 16-byte unit placement blocks).
    /// Layout: UnitID(B0), ClassID(B1), LeaderUnitID(B2), UnitInfo(B3),
    ///         StartX(B4), StartY(B5), EndX(B6), EndY(B7),
    ///         Item1(B8), Item2(B9), Item3(B10), Item4(B11),
    ///         AI1Primary(B12), AI2Secondary(B13), AI3TargetRecovery(B14), AI4Retreat(B15).
    /// </summary>
    public class EventUnitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;

        uint _unitID, _classID, _leaderUnitID, _unitInfo;
        uint _startX, _startY, _endX, _endY;
        uint _item1, _item2, _item3, _item4;
        uint _ai1Primary, _ai2Secondary, _ai3TargetRecovery, _ai4Retreat;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint UnitID { get => _unitID; set => SetField(ref _unitID, value); }
        public uint ClassID { get => _classID; set => SetField(ref _classID, value); }
        public uint LeaderUnitID { get => _leaderUnitID; set => SetField(ref _leaderUnitID, value); }
        public uint UnitInfo { get => _unitInfo; set => SetField(ref _unitInfo, value); }
        public uint StartX { get => _startX; set => SetField(ref _startX, value); }
        public uint StartY { get => _startY; set => SetField(ref _startY, value); }
        public uint EndX { get => _endX; set => SetField(ref _endX, value); }
        public uint EndY { get => _endY; set => SetField(ref _endY, value); }
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
            result.Add(new AddrResult(0, "Event Unit Placement (FE6)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.eventunit_data_size; // 16 for FE6
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            UnitID = rom.u8(addr + 0);
            ClassID = rom.u8(addr + 1);
            LeaderUnitID = rom.u8(addr + 2);
            UnitInfo = rom.u8(addr + 3);
            StartX = rom.u8(addr + 4);
            StartY = rom.u8(addr + 5);
            EndX = rom.u8(addr + 6);
            EndY = rom.u8(addr + 7);
            Item1 = rom.u8(addr + 8);
            Item2 = rom.u8(addr + 9);
            Item3 = rom.u8(addr + 10);
            Item4 = rom.u8(addr + 11);
            AI1Primary = rom.u8(addr + 12);
            AI2Secondary = rom.u8(addr + 13);
            AI3TargetRecovery = rom.u8(addr + 14);
            AI4Retreat = rom.u8(addr + 15);

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
            rom.write_u8(addr + 4, (byte)StartX);
            rom.write_u8(addr + 5, (byte)StartY);
            rom.write_u8(addr + 6, (byte)EndX);
            rom.write_u8(addr + 7, (byte)EndY);
            rom.write_u8(addr + 8, (byte)Item1);
            rom.write_u8(addr + 9, (byte)Item2);
            rom.write_u8(addr + 10, (byte)Item3);
            rom.write_u8(addr + 11, (byte)Item4);
            rom.write_u8(addr + 12, (byte)AI1Primary);
            rom.write_u8(addr + 13, (byte)AI2Secondary);
            rom.write_u8(addr + 14, (byte)AI3TargetRecovery);
            rom.write_u8(addr + 15, (byte)AI4Retreat);
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
                ["StartX"] = $"0x{StartX:X02}",
                ["StartY"] = $"0x{StartY:X02}",
                ["EndX"] = $"0x{EndX:X02}",
                ["EndY"] = $"0x{EndY:X02}",
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
                ["StartX@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["StartY@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["EndX@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["EndY@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["Item1@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["Item2@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["Item3@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["Item4@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["AI1Primary@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["AI2Secondary@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["AI3TargetRecovery@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["AI4Retreat@0x0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
