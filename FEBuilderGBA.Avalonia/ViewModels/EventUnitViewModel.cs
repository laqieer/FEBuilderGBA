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
    ///         UnitPos/W4(B4..B5), Reserved6(B6), AfterCoordCount(B7),
    ///         AfterCoordPointer(D8..D11),
    ///         Item1(B12), Item2(B13), Item3(B14), Item4(B15),
    ///         AI1Primary(B16), AI2Secondary(B17), AI3TargetRecovery(B18),
    ///         AI4Retreat(B19).
    ///
    /// B3 (UnitInfo) decomposes into three packed sub-fields per WF
    /// <c>InputFormRef.cs</c> (the UNITGROW binding):
    ///   bit 0       — Growth flag (0 = no growth, 1 = class-dependent)
    ///   bits 1-2    — Allegiance (0 = Player, 1 = Ally, 2 = Enemy, 3 = Disappear)
    ///   bits 3-7    — Level (0-31)
    ///
    /// W4 (UnitPos) decomposes via U.MakeFe8UnitPos / U.ParseFe8UnitPos*:
    ///   bits 0-5    — Before X (0-63)
    ///   bits 6-11   — Before Y (0-63)
    ///   bits 12-14  — Ext flags (bit 0x2 = Item Drop)
    ///
    /// B7/D8 hold the after-coord LIST (count + pointer). Editing the
    /// LIST itself (multi-coord, the WF FE8CoordListBox) is OUT OF SCOPE
    /// for this gap-sweep PR — the new view shows B7/D8 read-only.
    /// </summary>
    public partial class EventUnitViewModel : ViewModelBase, IDataVerifiable
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
        uint _unitGrowth; // raw W4 (UnitPos)
        uint _reserved6, _coordCount;
        uint _coordPointer;
        uint _item1, _item2, _item3, _item4;
        uint _ai1Primary, _ai2Secondary, _ai3TargetRecovery, _ai4Retreat;

        // Navigation state
        uint _selectedMapId = uint.MaxValue;
        uint _selectedGroupAddr;

        // Comment annotation
        string _comment = "";

        // Resolved display names
        string _unitName = "";
        string _className = "";
        string _item1Name = "", _item2Name = "", _item3Name = "", _item4Name = "";
        string _ai1Desc = "", _ai2Desc = "", _ai3Desc = "", _ai4Desc = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint UnitID
        {
            get => _unitID;
            set => SetField(ref _unitID, value);
        }
        public uint ClassID
        {
            get => _classID;
            set => SetField(ref _classID, value);
        }
        public uint LeaderUnitID { get => _leaderUnitID; set => SetField(ref _leaderUnitID, value); }

        public uint UnitInfo
        {
            get => _unitInfo;
            set
            {
                if (SetField(ref _unitInfo, value))
                {
                    OnPropertyChanged(nameof(UnitInfoLV));
                    OnPropertyChanged(nameof(UnitInfoAllegiance));
                    OnPropertyChanged(nameof(UnitInfoGrow));
                }
            }
        }
        public uint UnitGrowth
        {
            // Backwards-compat alias for the W4 packed word. New view
            // surfaces this as the "Unit Pos (W4)" raw input; the
            // BeforeX/BeforeY/UnitPosExt properties decompose it.
            get => _unitGrowth;
            set
            {
                if (SetField(ref _unitGrowth, value))
                {
                    OnPropertyChanged(nameof(BeforeX));
                    OnPropertyChanged(nameof(BeforeY));
                    OnPropertyChanged(nameof(UnitPosExt));
                    OnPropertyChanged(nameof(ItemDropFlag));
                    RefreshItemDropDisplay();
                }
            }
        }
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

        /// <summary>
        /// Item-drop display string mirroring WF X_ITEMDROP label. Get-only
        /// computed property — the canonical source is the W4 ext bit;
        /// <see cref="UnitGrowth"/> setter raises
        /// <c>PropertyChanged(nameof(ItemDropDisplay))</c> via
        /// <c>RefreshItemDropDisplay</c> so bound controls refresh in
        /// lockstep with the underlying byte (Copilot bot review on PR
        /// #540: a setter would be dead code since reads come from
        /// <c>_unitGrowth</c> not from a cached backing field).
        /// </summary>
        public string ItemDropDisplay => ComputeItemDropDisplay(_unitGrowth);

        /// <summary>
        /// User annotation persisted through <c>CoreState.CommentCache</c>
        /// (matches the WF InputFormRef Comment-cache pattern + the Avalonia
        /// precedent established by ImageBattleBG / ImagePortraitFE6).
        /// </summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        /// <summary>
        /// The currently-selected map id, used as the second key by jump
        /// helpers (BattleTalk + Haiku take unit_id + map_id).
        /// </summary>
        public uint SelectedMapId { get => _selectedMapId; set => SetField(ref _selectedMapId, value); }

        /// <summary>
        /// Base address of the unit-list table for the currently-loaded
        /// group. <see cref="ExpandUnitListCurrent"/> uses this to identify
        /// which list to grow.
        /// </summary>
        public uint SelectedUnitListBase { get => _selectedGroupAddr; set => SetField(ref _selectedGroupAddr, value); }

        // ---------------------------------------------------------------
        // B3 (UnitInfo) sub-field properties — UI sugar over the raw byte.
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

        // ---------------------------------------------------------------
        // W4 (UnitPos / UnitGrowth alias) sub-field properties — decompose
        // the packed word into BeforeX/BeforeY/Ext components via
        // U.ParseFe8UnitPos* / U.MakeFe8UnitPos.
        //
        // Setting any sub-field recomposes UnitGrowth so the packed W4
        // remains the source of truth. RefreshItemDropDisplay runs whenever
        // ext changes so the FE8 "Item Drop: drops/doesn't drop" label
        // stays honest while editing.
        // ---------------------------------------------------------------

        public uint BeforeX
        {
            get => U.ParseFe8UnitPosX(_unitGrowth);
            set
            {
                uint w4 = U.MakeFe8UnitPos(
                    x: value,
                    y: U.ParseFe8UnitPosY(_unitGrowth),
                    ext: U.ParseFe8UnitPosExt(_unitGrowth));
                UnitGrowth = w4;
            }
        }

        public uint BeforeY
        {
            get => U.ParseFe8UnitPosY(_unitGrowth);
            set
            {
                uint w4 = U.MakeFe8UnitPos(
                    x: U.ParseFe8UnitPosX(_unitGrowth),
                    y: value,
                    ext: U.ParseFe8UnitPosExt(_unitGrowth));
                UnitGrowth = w4;
            }
        }

        public uint UnitPosExt
        {
            get => U.ParseFe8UnitPosExt(_unitGrowth);
            set
            {
                uint w4 = U.MakeFe8UnitPos(
                    x: U.ParseFe8UnitPosX(_unitGrowth),
                    y: U.ParseFe8UnitPosY(_unitGrowth),
                    ext: value);
                UnitGrowth = w4;
            }
        }

        /// <summary>
        /// Item Drop flag — bit 0x2 of the W4 ext field. Mirrors WF
        /// EventUnitForm's UpdateItemDropLabel + X_ITEMDROP_Click logic
        /// (the canonical FE8 source of truth, not unit/class
        /// characteristics).
        /// </summary>
        public bool ItemDropFlag
        {
            get => (U.ParseFe8UnitPosExt(_unitGrowth) & 0x2) == 0x2;
            set
            {
                uint ext = U.ParseFe8UnitPosExt(_unitGrowth);
                if (value)
                    ext |= 0x2;
                else
                    ext = ext & ~0x2u;
                UnitPosExt = ext;
            }
        }

        void RefreshItemDropDisplay()
        {
            // ItemDropDisplay is computed from _unitGrowth; raise change
            // notification so bound controls re-read.
            OnPropertyChanged(nameof(ItemDropDisplay));
        }

        /// <summary>Build the map list (Level 1 navigation).</summary>
        public List<AddrResult> LoadMapList()
        {
            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>Build the unit group list for a map (Level 2 navigation).</summary>
        public List<AddrResult> LoadUnitGroups(uint mapId)
        {
            SelectedMapId = mapId;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.GetUnitGroupsForMap(rom, mapId);
        }

        /// <summary>Build the unit list from a base address (Level 3 navigation).</summary>
        public List<AddrResult> LoadUnitList(uint baseAddr)
        {
            SelectedUnitListBase = baseAddr;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.EnumerateUnits(rom, baseAddr);
        }

        /// <summary>Load unit list from an arbitrary address (manual entry).</summary>
        public List<AddrResult> LoadUnitListFromAddress(uint baseAddr)
        {
            SelectedMapId = uint.MaxValue;
            SelectedUnitListBase = baseAddr;
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapEventUnitCore.EnumerateUnits(rom, baseAddr);
        }

        /// <summary>
        /// Expand the currently-loaded unit list by the given number of rows.
        /// Mirrors the WF AddressListExpandsButton behavior with FE8 starter
        /// bytes (B0=0x01, B1=0x02) per WF EventUnitForm.AddressListExpandsEvent.
        /// Returns the new base ROM offset or <see cref="U.NOT_FOUND"/> on failure.
        ///
        /// Side-effects on success: <see cref="SelectedUnitListBase"/> updated
        /// to the new base. The caller MUST open an ambient undo scope before
        /// invoking this.
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

            // FE8 starter B1 = 0x02 per WF EventUnitForm.AddressListExpandsEvent
            // (vs FE7's 0x01).
            uint newBase = MapEventUnitCore.ExpandUnitList(
                rom, slot, SelectedUnitListBase, oldCount, newCount, starterB1: 0x02);
            if (newBase == U.NOT_FOUND) return U.NOT_FOUND;

            SelectedUnitListBase = newBase;
            return newBase;
        }

        /// <summary>IDataVerifiable fallback: returns the map list (Level 1) when no specific group is selected.</summary>
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

            // Update FE8 item-drop display from the W4 ext bit (NOT unit/
            // class characteristic — the FE8 canonical source is W4@bit12+1).
            RefreshItemDropDisplay();

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
                ["W4"] = UnitGrowth, ["B6"] = Reserved6, ["B7"] = CoordCount, ["D8"] = CoordPointer,
                ["B12"] = Item1, ["B13"] = Item2, ["B14"] = Item3, ["B15"] = Item4,
                ["B16"] = AI1Primary, ["B17"] = AI2Secondary, ["B18"] = AI3TargetRecovery, ["B19"] = AI4Retreat,
            };
            EditorFormRef.WriteFields(rom, addr, values, Fields);

            // Persist the Comment to CoreState.CommentCache. The cache is a
            // separate annotation store and lives OUTSIDE the ROM undo
            // scope by precedent (ImagePortraitFE6View.WriteButton_Click,
            // ImageBattleBGViewModel.WriteCommentToCache, PR #522).
            CoreState.CommentCache?.Update(addr, Comment ?? "");
        }

        /// <summary>
        /// Returns the localized FE8 Item Drop status string for the given
        /// W4 (UnitPos) value. Mirrors WF EventUnitForm.UpdateItemDropLabel:
        /// the drop bit is <c>ext &amp; 0x2</c> where ext = bits 12-14 of W4.
        /// Pure UI affordance; ROM is read-only.
        /// </summary>
        public static string ComputeItemDropDisplay(uint w4Word)
        {
            uint ext = U.ParseFe8UnitPosExt(w4Word);
            bool drops = (ext & 0x2) == 0x2;
            return drops ? R._("Item Drop: drops") : R._("Item Drop: doesn't drop");
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

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitID"] = "UnitID@0x00",
            ["ClassID"] = "ClassID@0x01",
            ["LeaderUnitID"] = "LeaderUnitID@0x02",
            ["UnitInfo"] = "UnitInfo@0x03",
            ["UnitGrowth"] = "UnitGrowth@0x04",
            ["Reserved6"] = "Reserved6@0x06",
            ["CoordCount"] = "CoordCount@0x07",
            ["CoordPointer"] = "CoordPointer@0x08",
            ["Item1"] = "Item1@0x0C",
            ["Item2"] = "Item2@0x0D",
            ["Item3"] = "Item3@0x0E",
            ["Item4"] = "Item4@0x0F",
            ["AI1Primary"] = "AI1Primary@0x10",
            ["AI2Secondary"] = "AI2Secondary@0x11",
            ["AI3TargetRecovery"] = "AI3TargetRecovery@0x12",
            ["AI4Retreat"] = "AI4Retreat@0x13",
        };
    }
}
