using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for EventUnitForm (FE8 — 20-byte unit placement blocks).
    /// Provides 3-level navigation: Map -> Unit Group -> Unit.
    ///
    /// Layout: UnitID(B0), ClassID(B1), LeaderUnitID(B2), UnitInfo(B3),
    ///         UnitGrowth(W4), Reserved6(B6), CoordCount(B7), CoordPointer(D8),
    ///         Item1(B12), Item2(B13), Item3(B14), Item4(B15),
    ///         AI1Primary(B16), AI2Secondary(B17), AI3TargetRecovery(B18), AI4Retreat(B19).
    /// </summary>
    public class EventUnitViewModel : ViewModelBase, IDataVerifiable
    {
        // EditorFormRef field definitions
        static readonly string[] FieldNames = new[]
        {
            "B0", "B1", "B2", "B3", "W4", "B6", "B7", "D8",
            "B12", "B13", "B14", "B15", "B16", "B17", "B18", "B19"
        };
        static readonly List<EditorFormRef.FieldDef> Fields = EditorFormRef.DetectFields(FieldNames);

        uint _currentAddr;
        bool _isLoaded;

        uint _unitID, _classID, _leaderUnitID, _unitInfo;
        uint _unitGrowth;
        uint _reserved6, _coordCount;
        uint _coordPointer;
        uint _item1, _item2, _item3, _item4;
        uint _ai1Primary, _ai2Secondary, _ai3TargetRecovery, _ai4Retreat;

        // Navigation state
        uint _selectedMapId = uint.MaxValue;
        uint _selectedGroupAddr;

        // Resolved display names
        string _unitName = "";
        string _className = "";
        string _item1Name = "", _item2Name = "", _item3Name = "", _item4Name = "";
        string _ai1Desc = "", _ai2Desc = "", _ai3Desc = "", _ai4Desc = "";

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

        // Resolved name properties
        public string UnitName { get => _unitName; set => SetField(ref _unitName, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public string Item1Name { get => _item1Name; set => SetField(ref _item1Name, value); }
        public string Item2Name { get => _item2Name; set => SetField(ref _item2Name, value); }
        public string Item3Name { get => _item3Name; set => SetField(ref _item3Name, value); }
        public string Item4Name { get => _item4Name; set => SetField(ref _item4Name, value); }
        public string AI1Desc { get => _ai1Desc; set => SetField(ref _ai1Desc, value); }
        public string AI2Desc { get => _ai2Desc; set => SetField(ref _ai2Desc, value); }
        public string AI3Desc { get => _ai3Desc; set => SetField(ref _ai3Desc, value); }
        public string AI4Desc { get => _ai4Desc; set => SetField(ref _ai4Desc, value); }

        /// <summary>Build the map list (Level 1 navigation).</summary>
        public List<AddrResult> LoadMapList()
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>Build the unit group list for a map (Level 2 navigation).</summary>
        public List<AddrResult> LoadUnitGroups(uint mapId)
        {
            _selectedMapId = mapId;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);
        }

        /// <summary>Build the unit list from a base address (Level 3 navigation).</summary>
        public List<AddrResult> LoadUnitList(uint baseAddr)
        {
            _selectedGroupAddr = baseAddr;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.EnumerateUnits(rom, baseAddr);
        }

        /// <summary>Load unit list from an arbitrary address (manual entry).</summary>
        public List<AddrResult> LoadUnitListFromAddress(uint baseAddr)
        {
            _selectedMapId = uint.MaxValue;
            _selectedGroupAddr = baseAddr;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.EnumerateUnits(rom, baseAddr);
        }

        /// <summary>Legacy compatibility: returns placeholder list.</summary>
        public List<AddrResult> LoadList()
        {
            return LoadMapList();
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.eventunit_data_size; // 20 for FE8
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            var values = EditorFormRef.ReadFields(rom, addr, Fields);
            UnitID = values["B0"];
            ClassID = values["B1"];
            LeaderUnitID = values["B2"];
            UnitInfo = values["B3"];
            UnitGrowth = values["W4"];
            Reserved6 = values["B6"];
            CoordCount = values["B7"];
            CoordPointer = values["D8"];
            Item1 = values["B12"];
            Item2 = values["B13"];
            Item3 = values["B14"];
            Item4 = values["B15"];
            AI1Primary = values["B16"];
            AI2Secondary = values["B17"];
            AI3TargetRecovery = values["B18"];
            AI4Retreat = values["B19"];

            // Resolve display names
            UnitName = NameResolver.GetUnitName(UnitID);
            ClassName = NameResolver.GetClassName(ClassID);
            Item1Name = Item1 > 0 ? NameResolver.GetItemName(Item1) : "";
            Item2Name = Item2 > 0 ? NameResolver.GetItemName(Item2) : "";
            Item3Name = Item3 > 0 ? NameResolver.GetItemName(Item3) : "";
            Item4Name = Item4 > 0 ? NameResolver.GetItemName(Item4) : "";
            AI1Desc = MapEventUnitCore.GetAI1Description((byte)AI1Primary);
            AI2Desc = MapEventUnitCore.GetAI2Description((byte)AI2Secondary);
            AI3Desc = MapEventUnitCore.GetAI3Description((byte)AI3TargetRecovery);
            AI4Desc = MapEventUnitCore.GetAI4Description((byte)AI4Retreat);

            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = UnitID, ["B1"] = ClassID, ["B2"] = LeaderUnitID, ["B3"] = UnitInfo,
                ["W4"] = UnitGrowth, ["B6"] = Reserved6, ["B7"] = CoordCount, ["D8"] = CoordPointer,
                ["B12"] = Item1, ["B13"] = Item2, ["B14"] = Item3, ["B15"] = Item4,
                ["B16"] = AI1Primary, ["B17"] = AI2Secondary, ["B18"] = AI3TargetRecovery, ["B19"] = AI4Retreat,
            };
            EditorFormRef.WriteFields(rom, addr, values, Fields);
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
