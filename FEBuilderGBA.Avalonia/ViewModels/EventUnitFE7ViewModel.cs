using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for EventUnitFE7Form (FE7 — 16-byte unit placement blocks).
    /// Provides 3-level navigation: Map -> Unit Group -> Unit.
    ///
    /// Layout: UnitID(B0), ClassID(B1), LeaderUnitID(B2), UnitInfo(B3),
    ///         StartX(B4), StartY(B5), EndX(B6), EndY(B7),
    ///         Item1(B8), Item2(B9), Item3(B10), Item4(B11),
    ///         AI1Primary(B12), AI2Secondary(B13), AI3TargetRecovery(B14), AI4Retreat(B15).
    ///
    /// B3 (UnitInfo) decomposes into three packed sub-fields per WF
    /// `InputFormRef.cs:993-1083` (the UNITGROW binding):
    ///   bit 0       — Growth flag (0 = no growth, 1 = class-dependent)
    ///   bits 1-2    — Allegiance (0 = Player, 1 = Ally, 2 = Enemy, 3 = Disappear)
    ///   bits 3-7    — Level (0-31)
    /// </summary>
    public partial class EventUnitFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        // EditorFormRef field definitions
        static readonly string[] FieldNames = new[]
        {
            "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7",
            "B8", "B9", "B10", "B11", "B12", "B13", "B14", "B15"
        };
        static readonly List<EditorFormRef.FieldDef> Fields = EditorFormRef.DetectFields(FieldNames);

        uint _currentAddr;
        bool _isLoaded;

        uint _unitID, _classID, _leaderUnitID, _unitInfo;
        uint _startX, _startY, _endX, _endY;
        uint _item1, _item2, _item3, _item4;
        uint _ai1Primary, _ai2Secondary, _ai3TargetRecovery, _ai4Retreat;

        // Resolved display names
        string _unitName = "";
        string _className = "";
        string _item1Name = "", _item2Name = "", _item3Name = "", _item4Name = "";
        string _ai1Desc = "", _ai2Desc = "", _ai3Desc = "", _ai4Desc = "";
        string _itemDropDisplay = "";
        string _comment = "";

        // Currently selected map id — needed by the Haiku jump (B0 + mapId).
        uint _selectedMapId;

        // Base address of the unit-list table for the currently-loaded group.
        // Used by ExpandList to know which list to grow.
        uint _selectedUnitListBase;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint UnitID { get => _unitID; set => SetField(ref _unitID, value); }
        public uint ClassID { get => _classID; set => SetField(ref _classID, value); }
        public uint LeaderUnitID { get => _leaderUnitID; set => SetField(ref _leaderUnitID, value); }
        public uint UnitInfo
        {
            get => _unitInfo;
            set
            {
                if (SetField(ref _unitInfo, value))
                {
                    // Raise notifications for B3 sub-field properties so any
                    // bound control (LV/Allegiance/Grow combos) refreshes.
                    OnPropertyChanged(nameof(UnitInfoLV));
                    OnPropertyChanged(nameof(UnitInfoAllegiance));
                    OnPropertyChanged(nameof(UnitInfoGrow));
                }
            }
        }
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

        /// <summary>Item-drop display string, mirroring WF X_ITEMDROP label.</summary>
        public string ItemDropDisplay { get => _itemDropDisplay; set => SetField(ref _itemDropDisplay, value); }

        /// <summary>
        /// User annotation persisted through <c>CoreState.CommentCache</c>
        /// (matches the WF InputFormRef Comment-cache pattern + the Avalonia
        /// precedent established by ImageBattleBG / ImagePortraitFE6).
        /// </summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        /// <summary>
        /// The map id whose event-condition block the current group came
        /// from. Used as the second key for the Haiku jump (WF
        /// EventHaikuFE7Form.JumpTo takes unit_id + map_id).
        /// </summary>
        public uint SelectedMapId { get => _selectedMapId; set => SetField(ref _selectedMapId, value); }

        /// <summary>
        /// The base address of the unit-list table for the currently loaded
        /// group. <see cref="ExpandUnitListCurrent"/> uses this to identify
        /// which list to grow.
        /// </summary>
        public uint SelectedUnitListBase { get => _selectedUnitListBase; set => SetField(ref _selectedUnitListBase, value); }

        // ---------------------------------------------------------------
        // B3 (UnitInfo) decomposition properties — UI sugar over the raw byte.
        // Each setter recomposes UnitInfo so the byte stays the source of truth.
        // ---------------------------------------------------------------

        public uint UnitInfoLV
        {
            get => U.ParseUnitGrowLV(_unitInfo);
            set
            {
                uint b3 = U.MakeUnitGrowB3(
                    lv: value,
                    assign: U.ParseUnitGrowAssign(_unitInfo),
                    grow: U.ParseUnitGrowGrow(_unitInfo));
                UnitInfo = b3;
            }
        }

        public uint UnitInfoAllegiance
        {
            get => U.ParseUnitGrowAssign(_unitInfo);
            set
            {
                uint b3 = U.MakeUnitGrowB3(
                    lv: U.ParseUnitGrowLV(_unitInfo),
                    assign: value,
                    grow: U.ParseUnitGrowGrow(_unitInfo));
                UnitInfo = b3;
            }
        }

        public uint UnitInfoGrow
        {
            get => U.ParseUnitGrowGrow(_unitInfo);
            set
            {
                uint b3 = U.MakeUnitGrowB3(
                    lv: U.ParseUnitGrowLV(_unitInfo),
                    assign: U.ParseUnitGrowAssign(_unitInfo),
                    grow: value);
                UnitInfo = b3;
            }
        }

        /// <summary>Build the map list (Level 1 navigation).</summary>
        public List<AddrResult> LoadMapList()
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>Build the unit group list for a map (Level 2 navigation).</summary>
        public List<AddrResult> LoadUnitGroups(uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            SelectedMapId = mapId;
            return MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);
        }

        /// <summary>Build the unit list from a base address (Level 3 navigation).</summary>
        public List<AddrResult> LoadUnitList(uint baseAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            SelectedUnitListBase = baseAddr;
            return MapEventUnitCore.EnumerateUnits(rom, baseAddr);
        }

        /// <summary>
        /// Expand the currently-loaded unit list by the given number of rows.
        /// Mirrors the WF AddressListExpandsButton behavior:
        /// <list type="number">
        ///   <item>Resolve the event-condition pointer slot for the current group.</item>
        ///   <item>Find free space, copy old rows, plant B0=0x01/B1=0x01 starter
        ///     bytes per WF EventUnitFE7Form.cs:485-490.</item>
        ///   <item>Repoint the slot so the next reload picks up the new base.</item>
        /// </list>
        /// Returns the new base ROM offset or <see cref="U.NOT_FOUND"/> on failure.
        /// Side-effects on success: <see cref="SelectedUnitListBase"/> updated to
        /// the new base.
        ///
        /// The caller MUST open an ambient undo scope before invoking this.
        /// </summary>
        public uint ExpandUnitListCurrent(uint addRows)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (SelectedUnitListBase == 0) return U.NOT_FOUND;
            if (addRows == 0) return U.NOT_FOUND;

            uint slot = MapEventUnitCore.FindEventPointerSlotForUnitList(
                rom, SelectedMapId, SelectedUnitListBase);
            if (slot == 0) return U.NOT_FOUND;

            uint oldCount = MapEventUnitCore.CountEventUnitRows(rom, SelectedUnitListBase);
            if (oldCount == 0) return U.NOT_FOUND;
            uint newCount = oldCount + addRows;

            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom, slot, SelectedUnitListBase, oldCount, newCount);
            if (newBase == U.NOT_FOUND) return U.NOT_FOUND;

            SelectedUnitListBase = newBase;
            return newBase;
        }

        /// <summary>Legacy compatibility.</summary>
        public List<AddrResult> LoadList()
        {
            return LoadMapList();
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.eventunit_data_size; // 16 for FE7
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            var values = EditorFormRef.ReadFields(rom, addr, Fields);
            UnitID = values["B0"];
            ClassID = values["B1"];
            LeaderUnitID = values["B2"];
            UnitInfo = values["B3"];
            StartX = values["B4"];
            StartY = values["B5"];
            EndX = values["B6"];
            EndY = values["B7"];
            Item1 = values["B8"];
            Item2 = values["B9"];
            Item3 = values["B10"];
            Item4 = values["B11"];
            AI1Primary = values["B12"];
            AI2Secondary = values["B13"];
            AI3TargetRecovery = values["B14"];
            AI4Retreat = values["B15"];

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

            // Update item-drop display (mirrors WF UpdateItemDropLabel).
            ItemDropDisplay = ComputeItemDropDisplay(UnitID, ClassID);

            // Load comment annotation from CoreState.CommentCache (matches the
            // WF InputFormRef.cs Comment-cache pattern). Comments are keyed
            // by the entry's byte address.
            if (CoreState.CommentCache != null
                && CoreState.CommentCache.TryGetValue(addr, out string commentValue))
            {
                Comment = commentValue ?? "";
            }
            else
            {
                Comment = "";
            }

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
                ["B4"] = StartX, ["B5"] = StartY, ["B6"] = EndX, ["B7"] = EndY,
                ["B8"] = Item1, ["B9"] = Item2, ["B10"] = Item3, ["B11"] = Item4,
                ["B12"] = AI1Primary, ["B13"] = AI2Secondary, ["B14"] = AI3TargetRecovery, ["B15"] = AI4Retreat,
            };
            EditorFormRef.WriteFields(rom, addr, values, Fields);

            // Persist the Comment to CoreState.CommentCache. The cache is a
            // separate annotation store and lives OUTSIDE the ROM undo
            // scope by precedent (ImagePortraitFE6View.WriteButton_Click,
            // ImageBattleBGViewModel.WriteCommentToCache).
            CoreState.CommentCache?.Update(addr, Comment ?? "");
        }

        /// <summary>
        /// Mirrors WF EventUnitFE7Form.UpdateItemDropLabel: returns
        /// localised "Item Drop: drops" / "Item Drop: doesn't drop" based on
        /// the unit/class characteristic-4 ItemDrop bit (0x08 at offset 43
        /// in the unit/class struct). Pure UI affordance; ROM is read-only.
        /// </summary>
        public static string ComputeItemDropDisplay(uint unitId, uint classId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return "";

            uint unitAddr = LookupUnitAddr(rom, unitId);
            uint classAddr = LookupClassAddr(rom, classId);
            bool drops = IsItemDropFlag(rom, unitAddr) || IsItemDropFlag(rom, classAddr);
            // Use R._() so the runtime translation layer can localize the
            // status text (Copilot review #522 third pass — these strings
            // appear directly in the GUI, not just logs).
            return drops ? R._("Item Drop: drops") : R._("Item Drop: doesn't drop");
        }

        static uint LookupUnitAddr(ROM rom, uint unitId)
        {
            // Unit table layout: unit_pointer base, 0x34 (52)-byte rows
            // for FE7. unitId is 1-based for index lookups in WF, so
            // mirror that: (unitId - 1) * 0x34.
            if (unitId == 0) return 0;
            uint p = rom.RomInfo.unit_pointer;
            if (p == 0) return 0;
            uint baseAddr = rom.p32(p);
            if (!U.isSafetyOffset(baseAddr)) return 0;
            uint rowSize = rom.RomInfo.unit_datasize;
            if (rowSize == 0) return 0;
            return baseAddr + (unitId - 1) * rowSize;
        }

        static uint LookupClassAddr(ROM rom, uint classId)
        {
            uint p = rom.RomInfo.class_pointer;
            if (p == 0) return 0;
            uint baseAddr = rom.p32(p);
            if (!U.isSafetyOffset(baseAddr)) return 0;
            uint rowSize = rom.RomInfo.class_datasize;
            if (rowSize == 0) return 0;
            return baseAddr + classId * rowSize;
        }

        static bool IsItemDropFlag(ROM rom, uint addr)
        {
            if (rom == null) return false;
            if (!U.isSafetyOffset(addr)) return false;
            if (!U.isSafetyOffset(addr + 43)) return false;
            uint a = rom.u8(addr + 43);
            return (a & 0x08) == 0x08;
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

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitID"] = "UnitID@0x00",
            ["ClassID"] = "ClassID@0x01",
            ["LeaderUnitID"] = "LeaderUnitID@0x02",
            ["UnitInfo"] = "UnitInfo@0x03",
            ["StartX"] = "StartX@0x04",
            ["StartY"] = "StartY@0x05",
            ["EndX"] = "EndX@0x06",
            ["EndY"] = "EndY@0x07",
            ["Item1"] = "Item1@0x08",
            ["Item2"] = "Item2@0x09",
            ["Item3"] = "Item3@0x0A",
            ["Item4"] = "Item4@0x0B",
            ["AI1Primary"] = "AI1Primary@0x0C",
            ["AI2Secondary"] = "AI2Secondary@0x0D",
            ["AI3TargetRecovery"] = "AI3TargetRecovery@0x0E",
            ["AI4Retreat"] = "AI4Retreat@0x0F",
        };
    }
}
