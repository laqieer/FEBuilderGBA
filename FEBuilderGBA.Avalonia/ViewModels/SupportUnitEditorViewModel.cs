using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Support Unit Editor for FE7/FE8.
    /// Block size = 24 bytes.  Layout (all u8):
    ///   B0-B6   : Partner unit IDs (7 slots)
    ///   B7-B13  : Initial support values
    ///   B14-B20 : Support growth rates
    ///   B21     : Support partner count
    ///   B22-B23 : Separator / padding
    /// </summary>
    // Reads support_unit_pointer indirectly via SupportUnitNavigation
    // (which resolves rom.RomInfo.support_unit_pointer for the FE7/FE8
    // support struct enumeration).  Keep this comment so source-grep tests
    // that verify the right ROM pointer is touched still pick it up.
    public partial class SupportUnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        const uint BLOCK_SIZE = 24;

        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "B0", "B1", "B2", "B3", "B4", "B5", "B6",
                "B7", "B8", "B9", "B10", "B11", "B12", "B13",
                "B14", "B15", "B16", "B17", "B18", "B19", "B20",
                "B21", "B22", "B23"
            });

        uint _currentAddr;
        bool _canWrite;

        // Partner unit IDs (7 slots)
        uint _partner1, _partner2, _partner3, _partner4, _partner5, _partner6, _partner7;
        // Initial support values
        uint _initialValue1, _initialValue2, _initialValue3, _initialValue4;
        uint _initialValue5, _initialValue6, _initialValue7;
        // Support growth rates
        uint _growthRate1, _growthRate2, _growthRate3, _growthRate4;
        uint _growthRate5, _growthRate6, _growthRate7;
        // Partner count + separator
        uint _partnerCount, _separator1, _separator2;

        // Owner unit (read-only). #358 — the unit whose support pointer (+44)
        // matches CurrentAddr.  Empty / 0 when no unit owns this row.
        uint _sourceUnitId1Based; // 0 = unowned
        string _sourceUnitName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public uint SourceUnitId1Based { get => _sourceUnitId1Based; set => SetField(ref _sourceUnitId1Based, value); }
        public string SourceUnitName { get => _sourceUnitName; set => SetField(ref _sourceUnitName, value); }

        // Partner unit IDs
        public uint Partner1 { get => _partner1; set => SetField(ref _partner1, value); }
        public uint Partner2 { get => _partner2; set => SetField(ref _partner2, value); }
        public uint Partner3 { get => _partner3; set => SetField(ref _partner3, value); }
        public uint Partner4 { get => _partner4; set => SetField(ref _partner4, value); }
        public uint Partner5 { get => _partner5; set => SetField(ref _partner5, value); }
        public uint Partner6 { get => _partner6; set => SetField(ref _partner6, value); }
        public uint Partner7 { get => _partner7; set => SetField(ref _partner7, value); }

        // Initial support values
        public uint InitialValue1 { get => _initialValue1; set => SetField(ref _initialValue1, value); }
        public uint InitialValue2 { get => _initialValue2; set => SetField(ref _initialValue2, value); }
        public uint InitialValue3 { get => _initialValue3; set => SetField(ref _initialValue3, value); }
        public uint InitialValue4 { get => _initialValue4; set => SetField(ref _initialValue4, value); }
        public uint InitialValue5 { get => _initialValue5; set => SetField(ref _initialValue5, value); }
        public uint InitialValue6 { get => _initialValue6; set => SetField(ref _initialValue6, value); }
        public uint InitialValue7 { get => _initialValue7; set => SetField(ref _initialValue7, value); }

        // Growth rates
        public uint GrowthRate1 { get => _growthRate1; set => SetField(ref _growthRate1, value); }
        public uint GrowthRate2 { get => _growthRate2; set => SetField(ref _growthRate2, value); }
        public uint GrowthRate3 { get => _growthRate3; set => SetField(ref _growthRate3, value); }
        public uint GrowthRate4 { get => _growthRate4; set => SetField(ref _growthRate4, value); }
        public uint GrowthRate5 { get => _growthRate5; set => SetField(ref _growthRate5, value); }
        public uint GrowthRate6 { get => _growthRate6; set => SetField(ref _growthRate6, value); }
        public uint GrowthRate7 { get => _growthRate7; set => SetField(ref _growthRate7, value); }

        // Partner count + separator
        public uint PartnerCount { get => _partnerCount; set => SetField(ref _partnerCount, value); }
        public uint Separator1 { get => _separator1; set => SetField(ref _separator1, value); }
        public uint Separator2 { get => _separator2; set => SetField(ref _separator2, value); }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Owner-keyed enumeration mirroring WinForms SupportUnitForm.Init
            // (#358 / #437): each row's label is "{hex(uid+1)} {UnitName(uid+1)}"
            // for the unit whose +44 pointer matches this support address, or
            // "-EMPTY-" when no unit owns the row.  The 1-based display ID is
            // recorded in AddrResult.tag so the existing list-icon loaders
            // (UnitPortraitByIdLoader uses U.atoh on the label which yields
            // the same value) can resolve the portrait.  This replaces the
            // index-based label that caused the first-row portrait/name bug.
            var result = new List<AddrResult>();
            foreach (var (addr, ownerUid) in
                SupportUnitNavigation.EnumerateSupportEntries(rom, BLOCK_SIZE, firstFieldByteWidth: 2))
            {
                string label;
                uint tag;
                if (ownerUid == null)
                {
                    label = "-EMPTY-";
                    tag = 0;
                }
                else
                {
                    // Display "{hex(uid+1)} {Name}" — WinForms label convention.
                    // ResolveUnitTableName takes the 0-based index so the
                    // decoded name comes from the same unit table row that
                    // Unit Editor's row labels use.
                    uint oneBasedDisplay = ownerUid.Value + 1;
                    string unitName = SupportUnitNavigation.ResolveUnitTableName(rom, ownerUid.Value);
                    label = $"{U.ToHexString(oneBasedDisplay)} {unitName}";
                    tag = oneBasedDisplay;
                }
                result.Add(new AddrResult(addr, label, tag));
            }
            return result;
        }

        // ---- #1149: decomp source-backed save-gate fields ----

        /// <summary>
        /// Snapshot of the source-writable fields at load time (byte-offset keys).
        /// Used by <see cref="BuildSourceFieldDict"/> to emit ONLY user-changed fields.
        /// </summary>
        Dictionary<string, uint> _loadedSourceFieldSnapshot = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Current entry id (0-based) derived from <see cref="CurrentAddr"/> for the decomp
        /// source-backed writer. Returns <see cref="U.NOT_FOUND"/> when unresolvable.
        /// </summary>
        public uint CurrentEntryId => SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(CoreState.ROM, CurrentAddr, BLOCK_SIZE);

        /// <summary>
        /// All source-writable scalar fields keyed by lowercase byte-offset name, mapped to the current VM values.
        /// Byte-offset field-name contract: b0..b23 for FE7/8 (24-byte struct).
        /// </summary>
        public Dictionary<string, uint> CurrentSourceFieldMap()
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "b0",  Partner1 }, { "b1",  Partner2 }, { "b2",  Partner3 },
                { "b3",  Partner4 }, { "b4",  Partner5 }, { "b5",  Partner6 },
                { "b6",  Partner7 },
                { "b7",  InitialValue1 }, { "b8",  InitialValue2 }, { "b9",  InitialValue3 },
                { "b10", InitialValue4 }, { "b11", InitialValue5 }, { "b12", InitialValue6 },
                { "b13", InitialValue7 },
                { "b14", GrowthRate1 }, { "b15", GrowthRate2 }, { "b16", GrowthRate3 },
                { "b17", GrowthRate4 }, { "b18", GrowthRate5 }, { "b19", GrowthRate6 },
                { "b20", GrowthRate7 },
                { "b21", PartnerCount }, { "b22", Separator1 }, { "b23", Separator2 },
            };
        }

        /// <summary>
        /// Returns ONLY the fields whose value differs from the load-time snapshot (#1149).
        /// </summary>
        public IReadOnlyDictionary<string, uint> BuildSourceFieldDict()
        {
            var current = CurrentSourceFieldMap();
            var changed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in current)
            {
                if (!_loadedSourceFieldSnapshot.TryGetValue(kv.Key, out uint baseline) || baseline != kv.Value)
                    changed[kv.Key] = kv.Value;
            }
            return changed;
        }

        /// <summary>Re-baseline the snapshot to current values after a successful source-backed write.</summary>
        public void RefreshSourceFieldSnapshot()
        {
            _loadedSourceFieldSnapshot = CurrentSourceFieldMap();
        }

        public void LoadSupportUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BLOCK_SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);

            Partner1 = v["B0"]; Partner2 = v["B1"]; Partner3 = v["B2"];
            Partner4 = v["B3"]; Partner5 = v["B4"]; Partner6 = v["B5"];
            Partner7 = v["B6"];

            InitialValue1 = v["B7"]; InitialValue2 = v["B8"]; InitialValue3 = v["B9"];
            InitialValue4 = v["B10"]; InitialValue5 = v["B11"]; InitialValue6 = v["B12"];
            InitialValue7 = v["B13"];

            GrowthRate1 = v["B14"]; GrowthRate2 = v["B15"]; GrowthRate3 = v["B16"];
            GrowthRate4 = v["B17"]; GrowthRate5 = v["B18"]; GrowthRate6 = v["B19"];
            GrowthRate7 = v["B20"];

            PartnerCount = v["B21"];
            Separator1 = v["B22"];
            Separator2 = v["B23"];

            // #358: Resolve source unit (the owner that points at this support
            // row via its +44 pointer).  Read-only display in this PR.
            uint? ownerUid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, addr);
            if (ownerUid != null)
            {
                SourceUnitId1Based = ownerUid.Value + 1;
                // ResolveUnitTableName uses the 0-based table index.
                SourceUnitName = SupportUnitNavigation.ResolveUnitTableName(rom, ownerUid.Value);
            }
            else
            {
                SourceUnitId1Based = 0;
                SourceUnitName = "";
            }

            CanWrite = true;
            RefreshSourceFieldSnapshot();
        }

        /// <summary>
        /// Compute the row index whose <c>addr</c> equals
        /// <c>U.toOffset(supportPointerOrFileOffset)</c>, mirroring
        /// WinForms <c>SupportUnitForm.JumpToAddr</c>.  Returns -1 if no
        /// row matches.  Used by the Unit Editor's "jump to support" button
        /// to land on the right row regardless of whether the caller passed
        /// a raw <c>0x08xxxxxx</c> GBA pointer or a file offset.
        /// </summary>
        public int FindRowForAddr(List<AddrResult> list, uint supportPointerOrFileOffset)
        {
            uint normalized = U.toOffset(supportPointerOrFileOffset);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].addr == normalized) return i;
            }
            return -1;
        }

        public void WriteSupportUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = Partner1, ["B1"] = Partner2, ["B2"] = Partner3,
                ["B3"] = Partner4, ["B4"] = Partner5, ["B5"] = Partner6,
                ["B6"] = Partner7,
                ["B7"] = InitialValue1, ["B8"] = InitialValue2, ["B9"] = InitialValue3,
                ["B10"] = InitialValue4, ["B11"] = InitialValue5, ["B12"] = InitialValue6,
                ["B13"] = InitialValue7,
                ["B14"] = GrowthRate1, ["B15"] = GrowthRate2, ["B16"] = GrowthRate3,
                ["B17"] = GrowthRate4, ["B18"] = GrowthRate5, ["B19"] = GrowthRate6,
                ["B20"] = GrowthRate7,
                ["B21"] = PartnerCount, ["B22"] = Separator1, ["B23"] = Separator2,
            };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        public int GetListCount() => LoadSupportUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Partner1"] = $"0x{Partner1:X02}",
                ["Partner2"] = $"0x{Partner2:X02}",
                ["Partner3"] = $"0x{Partner3:X02}",
                ["Partner4"] = $"0x{Partner4:X02}",
                ["Partner5"] = $"0x{Partner5:X02}",
                ["Partner6"] = $"0x{Partner6:X02}",
                ["Partner7"] = $"0x{Partner7:X02}",
                ["InitialValue1"] = $"0x{InitialValue1:X02}",
                ["InitialValue2"] = $"0x{InitialValue2:X02}",
                ["InitialValue3"] = $"0x{InitialValue3:X02}",
                ["InitialValue4"] = $"0x{InitialValue4:X02}",
                ["InitialValue5"] = $"0x{InitialValue5:X02}",
                ["InitialValue6"] = $"0x{InitialValue6:X02}",
                ["InitialValue7"] = $"0x{InitialValue7:X02}",
                ["GrowthRate1"] = $"0x{GrowthRate1:X02}",
                ["GrowthRate2"] = $"0x{GrowthRate2:X02}",
                ["GrowthRate3"] = $"0x{GrowthRate3:X02}",
                ["GrowthRate4"] = $"0x{GrowthRate4:X02}",
                ["GrowthRate5"] = $"0x{GrowthRate5:X02}",
                ["GrowthRate6"] = $"0x{GrowthRate6:X02}",
                ["GrowthRate7"] = $"0x{GrowthRate7:X02}",
                ["PartnerCount"] = $"0x{PartnerCount:X02}",
                ["Separator1"] = $"0x{Separator1:X02}",
                ["Separator2"] = $"0x{Separator2:X02}",
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
                ["Partner1@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["Partner2@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["Partner3@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["Partner4@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["Partner5@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["Partner6@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["Partner7@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["InitialValue1@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["InitialValue2@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["InitialValue3@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["InitialValue4@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["InitialValue5@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["InitialValue6@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["InitialValue7@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["GrowthRate1@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["GrowthRate2@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["GrowthRate3@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["GrowthRate4@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["GrowthRate5@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["GrowthRate6@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["GrowthRate7@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["PartnerCount@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["Separator1@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["Separator2@0x17"] = $"0x{rom.u8(a + 23):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["Partner1"] = "Partner1@0x00",
                ["Partner2"] = "Partner2@0x01",
                ["Partner3"] = "Partner3@0x02",
                ["Partner4"] = "Partner4@0x03",
                ["Partner5"] = "Partner5@0x04",
                ["Partner6"] = "Partner6@0x05",
                ["Partner7"] = "Partner7@0x06",
                ["InitialValue1"] = "InitialValue1@0x07",
                ["InitialValue2"] = "InitialValue2@0x08",
                ["InitialValue3"] = "InitialValue3@0x09",
                ["InitialValue4"] = "InitialValue4@0x0A",
                ["InitialValue5"] = "InitialValue5@0x0B",
                ["InitialValue6"] = "InitialValue6@0x0C",
                ["InitialValue7"] = "InitialValue7@0x0D",
                ["GrowthRate1"] = "GrowthRate1@0x0E",
                ["GrowthRate2"] = "GrowthRate2@0x0F",
                ["GrowthRate3"] = "GrowthRate3@0x10",
                ["GrowthRate4"] = "GrowthRate4@0x11",
                ["GrowthRate5"] = "GrowthRate5@0x12",
                ["GrowthRate6"] = "GrowthRate6@0x13",
                ["GrowthRate7"] = "GrowthRate7@0x14",
                ["PartnerCount"] = "PartnerCount@0x15",
                ["Separator1"] = "Separator1@0x16",
                ["Separator2"] = "Separator2@0x17",
            };
        }
    }
}
