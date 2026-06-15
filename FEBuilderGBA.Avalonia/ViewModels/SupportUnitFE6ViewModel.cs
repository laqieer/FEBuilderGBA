using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Support Unit Editor for FE6.
    /// Block size = 32 bytes.  Layout (all u8):
    ///   B0-B9   : Partner unit IDs (10 slots)
    ///   B10-B19 : Initial support values
    ///   B20-B29 : Support growth rates
    ///   B30     : Support partner count
    ///   B31     : Separator / padding
    /// </summary>
    public partial class SupportUnitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        const uint BLOCK_SIZE = 32;

        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9",
                "B10", "B11", "B12", "B13", "B14", "B15", "B16", "B17", "B18", "B19",
                "B20", "B21", "B22", "B23", "B24", "B25", "B26", "B27", "B28", "B29",
                "B30", "B31"
            });

        uint _currentAddr;
        bool _isLoaded;

        // Partner unit IDs (10 slots)
        uint _partner1, _partner2, _partner3, _partner4, _partner5;
        uint _partner6, _partner7, _partner8, _partner9, _partner10;
        // Initial support values
        uint _initialValue1, _initialValue2, _initialValue3, _initialValue4, _initialValue5;
        uint _initialValue6, _initialValue7, _initialValue8, _initialValue9, _initialValue10;
        // Support growth rates
        uint _growthRate1, _growthRate2, _growthRate3, _growthRate4, _growthRate5;
        uint _growthRate6, _growthRate7, _growthRate8, _growthRate9, _growthRate10;
        // Partner count + separator
        uint _partnerCount, _separator;

        // Owner unit (read-only). #358 — the unit whose support pointer (+44)
        // matches CurrentAddr.  Empty / 0 when no unit owns this row.
        uint _sourceUnitId1Based; // 0 = unowned
        string _sourceUnitName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

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
        public uint Partner8 { get => _partner8; set => SetField(ref _partner8, value); }
        public uint Partner9 { get => _partner9; set => SetField(ref _partner9, value); }
        public uint Partner10 { get => _partner10; set => SetField(ref _partner10, value); }

        // Initial support values
        public uint InitialValue1 { get => _initialValue1; set => SetField(ref _initialValue1, value); }
        public uint InitialValue2 { get => _initialValue2; set => SetField(ref _initialValue2, value); }
        public uint InitialValue3 { get => _initialValue3; set => SetField(ref _initialValue3, value); }
        public uint InitialValue4 { get => _initialValue4; set => SetField(ref _initialValue4, value); }
        public uint InitialValue5 { get => _initialValue5; set => SetField(ref _initialValue5, value); }
        public uint InitialValue6 { get => _initialValue6; set => SetField(ref _initialValue6, value); }
        public uint InitialValue7 { get => _initialValue7; set => SetField(ref _initialValue7, value); }
        public uint InitialValue8 { get => _initialValue8; set => SetField(ref _initialValue8, value); }
        public uint InitialValue9 { get => _initialValue9; set => SetField(ref _initialValue9, value); }
        public uint InitialValue10 { get => _initialValue10; set => SetField(ref _initialValue10, value); }

        // Growth rates
        public uint GrowthRate1 { get => _growthRate1; set => SetField(ref _growthRate1, value); }
        public uint GrowthRate2 { get => _growthRate2; set => SetField(ref _growthRate2, value); }
        public uint GrowthRate3 { get => _growthRate3; set => SetField(ref _growthRate3, value); }
        public uint GrowthRate4 { get => _growthRate4; set => SetField(ref _growthRate4, value); }
        public uint GrowthRate5 { get => _growthRate5; set => SetField(ref _growthRate5, value); }
        public uint GrowthRate6 { get => _growthRate6; set => SetField(ref _growthRate6, value); }
        public uint GrowthRate7 { get => _growthRate7; set => SetField(ref _growthRate7, value); }
        public uint GrowthRate8 { get => _growthRate8; set => SetField(ref _growthRate8, value); }
        public uint GrowthRate9 { get => _growthRate9; set => SetField(ref _growthRate9, value); }
        public uint GrowthRate10 { get => _growthRate10; set => SetField(ref _growthRate10, value); }

        // Partner count + separator
        public uint PartnerCount { get => _partnerCount; set => SetField(ref _partnerCount, value); }
        public uint Separator { get => _separator; set => SetField(ref _separator, value); }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Owner-keyed enumeration mirroring WinForms SupportUnitFE6Form.Init
            // (#358 / #436): FE6 uses u8 at +0 for the "is owned" heuristic
            // (vs FE7/8's u16).  See SupportUnitNavigation.EnumerateSupportEntries.
            var result = new List<AddrResult>();
            foreach (var (addr, ownerUid) in
                SupportUnitNavigation.EnumerateSupportEntries(rom, BLOCK_SIZE, firstFieldByteWidth: 1))
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

        /// <summary>Snapshot of source-writable fields at load time (byte-offset keys, OrdinalIgnoreCase).</summary>
        Dictionary<string, uint> _loadedSourceFieldSnapshot = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        /// <summary>0-based entry id for the decomp source-backed writer. <see cref="U.NOT_FOUND"/> when unresolvable.</summary>
        public uint CurrentEntryId => SupportUnitNavigation.GetSupportUnitEntryIdFromAddr(CoreState.ROM, CurrentAddr, BLOCK_SIZE);

        /// <summary>
        /// All source-writable scalar fields keyed by lowercase byte-offset name (b0..b31 for FE6 32-byte struct).
        /// </summary>
        public Dictionary<string, uint> CurrentSourceFieldMap()
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "b0",  Partner1 }, { "b1",  Partner2 }, { "b2",  Partner3 },
                { "b3",  Partner4 }, { "b4",  Partner5 }, { "b5",  Partner6 },
                { "b6",  Partner7 }, { "b7",  Partner8 }, { "b8",  Partner9 },
                { "b9",  Partner10 },
                { "b10", InitialValue1 }, { "b11", InitialValue2 }, { "b12", InitialValue3 },
                { "b13", InitialValue4 }, { "b14", InitialValue5 }, { "b15", InitialValue6 },
                { "b16", InitialValue7 }, { "b17", InitialValue8 }, { "b18", InitialValue9 },
                { "b19", InitialValue10 },
                { "b20", GrowthRate1 }, { "b21", GrowthRate2 }, { "b22", GrowthRate3 },
                { "b23", GrowthRate4 }, { "b24", GrowthRate5 }, { "b25", GrowthRate6 },
                { "b26", GrowthRate7 }, { "b27", GrowthRate8 }, { "b28", GrowthRate9 },
                { "b29", GrowthRate10 },
                { "b30", PartnerCount }, { "b31", Separator },
            };
        }

        /// <summary>Returns ONLY fields whose value differs from the load-time snapshot (#1149).</summary>
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
            Partner7 = v["B6"]; Partner8 = v["B7"]; Partner9 = v["B8"];
            Partner10 = v["B9"];

            InitialValue1 = v["B10"]; InitialValue2 = v["B11"]; InitialValue3 = v["B12"];
            InitialValue4 = v["B13"]; InitialValue5 = v["B14"]; InitialValue6 = v["B15"];
            InitialValue7 = v["B16"]; InitialValue8 = v["B17"]; InitialValue9 = v["B18"];
            InitialValue10 = v["B19"];

            GrowthRate1 = v["B20"]; GrowthRate2 = v["B21"]; GrowthRate3 = v["B22"];
            GrowthRate4 = v["B23"]; GrowthRate5 = v["B24"]; GrowthRate6 = v["B25"];
            GrowthRate7 = v["B26"]; GrowthRate8 = v["B27"]; GrowthRate9 = v["B28"];
            GrowthRate10 = v["B29"];

            PartnerCount = v["B30"];
            Separator = v["B31"];

            // #358: Resolve source unit (the owner that points at this support
            // row via its +44 pointer).  Read-only display in this PR.
            uint? ownerUid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, addr);
            if (ownerUid != null)
            {
                SourceUnitId1Based = ownerUid.Value + 1;
                SourceUnitName = SupportUnitNavigation.ResolveUnitTableName(rom, ownerUid.Value);
            }
            else
            {
                SourceUnitId1Based = 0;
                SourceUnitName = "";
            }

            IsLoaded = true;
            RefreshSourceFieldSnapshot();
        }

        /// <summary>
        /// Compute the row index whose <c>addr</c> equals
        /// <c>U.toOffset(supportPointerOrFileOffset)</c>, mirroring
        /// WinForms <c>SupportUnitFE6Form.JumpToAddr</c>.  Returns -1 if no
        /// row matches.  Used by the Unit Editor's "jump to support" button.
        /// Accepts both raw <c>0x08xxxxxx</c> pointers and file offsets.
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
                ["B6"] = Partner7, ["B7"] = Partner8, ["B8"] = Partner9,
                ["B9"] = Partner10,
                ["B10"] = InitialValue1, ["B11"] = InitialValue2, ["B12"] = InitialValue3,
                ["B13"] = InitialValue4, ["B14"] = InitialValue5, ["B15"] = InitialValue6,
                ["B16"] = InitialValue7, ["B17"] = InitialValue8, ["B18"] = InitialValue9,
                ["B19"] = InitialValue10,
                ["B20"] = GrowthRate1, ["B21"] = GrowthRate2, ["B22"] = GrowthRate3,
                ["B23"] = GrowthRate4, ["B24"] = GrowthRate5, ["B25"] = GrowthRate6,
                ["B26"] = GrowthRate7, ["B27"] = GrowthRate8, ["B28"] = GrowthRate9,
                ["B29"] = GrowthRate10,
                ["B30"] = PartnerCount, ["B31"] = Separator,
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
                ["Partner8"] = $"0x{Partner8:X02}",
                ["Partner9"] = $"0x{Partner9:X02}",
                ["Partner10"] = $"0x{Partner10:X02}",
                ["InitialValue1"] = $"0x{InitialValue1:X02}",
                ["InitialValue2"] = $"0x{InitialValue2:X02}",
                ["InitialValue3"] = $"0x{InitialValue3:X02}",
                ["InitialValue4"] = $"0x{InitialValue4:X02}",
                ["InitialValue5"] = $"0x{InitialValue5:X02}",
                ["InitialValue6"] = $"0x{InitialValue6:X02}",
                ["InitialValue7"] = $"0x{InitialValue7:X02}",
                ["InitialValue8"] = $"0x{InitialValue8:X02}",
                ["InitialValue9"] = $"0x{InitialValue9:X02}",
                ["InitialValue10"] = $"0x{InitialValue10:X02}",
                ["GrowthRate1"] = $"0x{GrowthRate1:X02}",
                ["GrowthRate2"] = $"0x{GrowthRate2:X02}",
                ["GrowthRate3"] = $"0x{GrowthRate3:X02}",
                ["GrowthRate4"] = $"0x{GrowthRate4:X02}",
                ["GrowthRate5"] = $"0x{GrowthRate5:X02}",
                ["GrowthRate6"] = $"0x{GrowthRate6:X02}",
                ["GrowthRate7"] = $"0x{GrowthRate7:X02}",
                ["GrowthRate8"] = $"0x{GrowthRate8:X02}",
                ["GrowthRate9"] = $"0x{GrowthRate9:X02}",
                ["GrowthRate10"] = $"0x{GrowthRate10:X02}",
                ["PartnerCount"] = $"0x{PartnerCount:X02}",
                ["Separator"] = $"0x{Separator:X02}",
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
                ["Partner8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["Partner9@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["Partner10@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["InitialValue1@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["InitialValue2@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["InitialValue3@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["InitialValue4@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["InitialValue5@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["InitialValue6@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["InitialValue7@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["InitialValue8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["InitialValue9@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["InitialValue10@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["GrowthRate1@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["GrowthRate2@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["GrowthRate3@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["GrowthRate4@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["GrowthRate5@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["GrowthRate6@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["GrowthRate7@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["GrowthRate8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["GrowthRate9@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["GrowthRate10@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["PartnerCount@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["Separator@0x1F"] = $"0x{rom.u8(a + 31):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Partner1"] = "Partner1@0x00",
            ["Partner2"] = "Partner2@0x01",
            ["Partner3"] = "Partner3@0x02",
            ["Partner4"] = "Partner4@0x03",
            ["Partner5"] = "Partner5@0x04",
            ["Partner6"] = "Partner6@0x05",
            ["Partner7"] = "Partner7@0x06",
            ["Partner8"] = "Partner8@0x07",
            ["Partner9"] = "Partner9@0x08",
            ["Partner10"] = "Partner10@0x09",
            ["InitialValue1"] = "InitialValue1@0x0A",
            ["InitialValue2"] = "InitialValue2@0x0B",
            ["InitialValue3"] = "InitialValue3@0x0C",
            ["InitialValue4"] = "InitialValue4@0x0D",
            ["InitialValue5"] = "InitialValue5@0x0E",
            ["InitialValue6"] = "InitialValue6@0x0F",
            ["InitialValue7"] = "InitialValue7@0x10",
            ["InitialValue8"] = "InitialValue8@0x11",
            ["InitialValue9"] = "InitialValue9@0x12",
            ["InitialValue10"] = "InitialValue10@0x13",
            ["GrowthRate1"] = "GrowthRate1@0x14",
            ["GrowthRate2"] = "GrowthRate2@0x15",
            ["GrowthRate3"] = "GrowthRate3@0x16",
            ["GrowthRate4"] = "GrowthRate4@0x17",
            ["GrowthRate5"] = "GrowthRate5@0x18",
            ["GrowthRate6"] = "GrowthRate6@0x19",
            ["GrowthRate7"] = "GrowthRate7@0x1A",
            ["GrowthRate8"] = "GrowthRate8@0x1B",
            ["GrowthRate9"] = "GrowthRate9@0x1C",
            ["GrowthRate10"] = "GrowthRate10@0x1D",
            ["PartnerCount"] = "PartnerCount@0x1E",
            ["Separator"] = "Separator@0x1F",
        };
    }
}
