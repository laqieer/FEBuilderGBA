using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// Selects which physical FE6 battle-talk table this view is browsing.
        /// Mirrors WinForms EventBattleTalkFE6Form's two address lists: the main
        /// 12-byte table (event_ballte_talk_pointer) plus the FE6-only secondary
        /// 16-byte table (event_ballte_talk2_pointer — the boss
        /// generic-conversation table).
        /// </summary>
        public enum BattleTalkTable
        {
            /// <summary>Main battle-talk table — 12-byte records (event_ballte_talk_pointer).</summary>
            Main = 0,
            /// <summary>Secondary (boss generic-conversation) table — 16-byte records (event_ballte_talk2_pointer).</summary>
            Secondary = 1,
        }

        // Main (12-byte) schema — matches WinForms EventBattleTalkFE6Form.Init.
        // [B0 atk][B1 def][B2][B3][W4 text][B6][B7][W8 flag][B10][B11]
        static readonly List<EditorFormRef.FieldDef> _fieldsMain =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "W8", "B10", "B11" });

        // Secondary (16-byte) schema — matches WinForms EventBattleTalkFE6Form
        // N_Init designer controls (N_B0/B1/B2/B3, N_W4 text, N_B6/B7, N_W8 flag,
        // N_B10/B11, N_D12 event pointer):
        // [B0 unit][B1][B2][B3][W4 text][B6][B7][W8 flag][B10][B11][D12 event ptr]
        static readonly List<EditorFormRef.FieldDef> _fieldsSecondary =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "W8", "B10", "B11", "D12" });

        BattleTalkTable _table = BattleTalkTable.Main;

        uint _currentAddr;
        bool _isLoaded;
        uint _attackerUnit;
        uint _defenderUnit;
        uint _unknown02;
        uint _unknown03;
        uint _text;
        uint _unknown06;
        uint _unknown07;
        uint _achievementFlag;
        uint _unknown0A;
        uint _unknown0B;
        uint _eventPointer;

        /// <summary>The currently-selected table (Main / Secondary).</summary>
        public BattleTalkTable Table
        {
            get => _table;
            set
            {
                if (SetField(ref _table, value))
                {
                    // Notify the derived read-only properties so bindings on
                    // IsSecondaryTable / BlockSize refresh too.
                    OnPropertyChanged(nameof(IsSecondaryTable));
                    OnPropertyChanged(nameof(BlockSize));
                }
            }
        }

        /// <summary>True when the current table uses the 16-byte secondary schema.</summary>
        public bool IsSecondaryTable => _table == BattleTalkTable.Secondary;

        /// <summary>Block size (stride) of the current table: 12 for Main, 16 for Secondary.</summary>
        public uint BlockSize => IsSecondaryTable ? 16u : 12u;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackerUnit { get => _attackerUnit; set => SetField(ref _attackerUnit, value); }
        public uint DefenderUnit { get => _defenderUnit; set => SetField(ref _defenderUnit, value); }
        public uint Unknown02 { get => _unknown02; set => SetField(ref _unknown02, value); }
        public uint Unknown03 { get => _unknown03; set => SetField(ref _unknown03, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint Unknown06 { get => _unknown06; set => SetField(ref _unknown06, value); }
        public uint Unknown07 { get => _unknown07; set => SetField(ref _unknown07, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Unknown0A { get => _unknown0A; set => SetField(ref _unknown0A, value); }
        public uint Unknown0B { get => _unknown0B; set => SetField(ref _unknown0B, value); }

        /// <summary>
        /// Event pointer (offset +0x0C) — only present in the secondary
        /// 16-byte table. The main 12-byte table has no event pointer field, so
        /// this stays 0 for main rows.
        /// </summary>
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }

        /// <summary>
        /// Resolves the pointer-location for the given table to its data base
        /// address (after a p32 deref). Returns 0 when the pointer is missing
        /// or the dereffed base is unsafe.
        /// </summary>
        public static uint ResolveBaseAddr(ROM rom, BattleTalkTable table)
        {
            if (rom?.RomInfo == null) return 0;
            uint pointer = table == BattleTalkTable.Secondary
                ? rom.RomInfo.event_ballte_talk2_pointer
                : rom.RomInfo.event_ballte_talk_pointer;
            if (pointer == 0) return 0;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            return baseAddr;
        }

        public List<AddrResult> LoadList() => LoadList(_table);

        public List<AddrResult> LoadList(BattleTalkTable table)
        {
            // Use the property setter so PropertyChanged fires for
            // Table / IsSecondaryTable / BlockSize.
            Table = table;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = ResolveBaseAddr(rom, table);
            if (baseAddr == 0) return new List<AddrResult>();

            if (table == BattleTalkTable.Secondary)
                return LoadSecondaryList(rom, baseAddr);

            const uint blockSize = 12; // FE6 main uses 12-byte blocks
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u16(addr);
                if (unit == 0 || unit == 0xFFFF) break;

                // 1-based ROM-stored unit IDs (WinForms UnitForm.GetUnitName convention).
                string atkName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr));
                string defName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr + 1));
                result.Add(new AddrResult(addr, $"0x{i:X2} {atkName} vs {defName}", (uint)i));
            }
            return result;
        }

        // Secondary 16-byte list — WinForms N_Init draws only the single unit
        // name and terminates on a 0/0xFFFF u16 unit (NOT a u8 like FE7's
        // secondary table — mirror FE6 exactly).
        static List<AddrResult> LoadSecondaryList(ROM rom, uint baseAddr)
        {
            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u16(addr);
                if (unit == 0 || unit == 0xFFFF) break;

                string unitName = NameResolver.GetUnitNameByOneBasedId(rom.u8(addr));
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint blockSize = BlockSize;
            if (addr + blockSize > (uint)rom.Data.Length) return;
            CurrentAddr = addr;

            if (IsSecondaryTable)
            {
                // 16-byte secondary schema (text @0x04, flag @0x08, event ptr @0x0C).
                var v = EditorFormRef.ReadFields(rom, addr, _fieldsSecondary);
                AttackerUnit = v["B0"];
                DefenderUnit = v["B1"];
                Unknown02 = v["B2"];
                Unknown03 = v["B3"];
                Text = v["W4"];
                Unknown06 = v["B6"];
                Unknown07 = v["B7"];
                AchievementFlag = v["W8"];
                Unknown0A = v["B10"];
                Unknown0B = v["B11"];
                EventPointer = v["D12"];
            }
            else
            {
                var v = EditorFormRef.ReadFields(rom, addr, _fieldsMain);
                AttackerUnit = v["B0"];
                DefenderUnit = v["B1"];
                Unknown02 = v["B2"];
                Unknown03 = v["B3"];
                Text = v["W4"];
                Unknown06 = v["B6"];
                Unknown07 = v["B7"];
                AchievementFlag = v["W8"];
                Unknown0A = v["B10"];
                Unknown0B = v["B11"];
                EventPointer = 0; // main rows carry no event pointer
            }
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (IsSecondaryTable)
            {
                var values = new Dictionary<string, uint>
                {
                    ["B0"] = AttackerUnit, ["B1"] = DefenderUnit,
                    ["B2"] = Unknown02, ["B3"] = Unknown03,
                    ["W4"] = Text, ["B6"] = Unknown06, ["B7"] = Unknown07,
                    ["W8"] = AchievementFlag, ["B10"] = Unknown0A, ["B11"] = Unknown0B,
                    ["D12"] = EventPointer,
                };
                EditorFormRef.WriteFields(rom, a, values, _fieldsSecondary);
            }
            else
            {
                var values = new Dictionary<string, uint>
                {
                    ["B0"] = AttackerUnit, ["B1"] = DefenderUnit,
                    ["B2"] = Unknown02, ["B3"] = Unknown03,
                    ["W4"] = Text, ["B6"] = Unknown06, ["B7"] = Unknown07,
                    ["W8"] = AchievementFlag, ["B10"] = Unknown0A, ["B11"] = Unknown0B,
                };
                EditorFormRef.WriteFields(rom, a, values, _fieldsMain);
            }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Table"] = _table.ToString(),
                ["AttackerUnit"] = $"0x{AttackerUnit:X02}",
                ["DefenderUnit"] = $"0x{DefenderUnit:X02}",
                ["Unknown02"] = $"0x{Unknown02:X02}",
                ["Unknown03"] = $"0x{Unknown03:X02}",
                ["Text"] = $"0x{Text:X04}",
                ["Unknown06"] = $"0x{Unknown06:X02}",
                ["Unknown07"] = $"0x{Unknown07:X02}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Unknown0A"] = $"0x{Unknown0A:X02}",
                ["Unknown0B"] = $"0x{Unknown0B:X02}",
            };
            if (IsSecondaryTable)
                report["EventPointer"] = $"0x{EventPointer:X08}";
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_AttackerUnit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_DefenderUnit"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown03"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04_Text"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06_Unknown06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_Unknown07"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08_AchievementFlag"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_Unknown0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Unknown0B"] = $"0x{rom.u8(a + 11):X02}",
            };
            if (IsSecondaryTable)
                report["u32@0x0C_EventPointer"] = $"0x{rom.u32(a + 12):X08}";
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            var map = new Dictionary<string, string>
            {
                ["AttackerUnit"] = "u8@0x00_AttackerUnit",
                ["DefenderUnit"] = "u8@0x01_DefenderUnit",
                ["Unknown02"] = "u8@0x02_Unknown02",
                ["Unknown03"] = "u8@0x03_Unknown03",
                ["Text"] = "u16@0x04_Text",
                ["Unknown06"] = "u8@0x06_Unknown06",
                ["Unknown07"] = "u8@0x07_Unknown07",
                ["AchievementFlag"] = "u16@0x08_AchievementFlag",
                ["Unknown0A"] = "u8@0x0A_Unknown0A",
                ["Unknown0B"] = "u8@0x0B_Unknown0B",
            };
            if (IsSecondaryTable)
                map["EventPointer"] = "u32@0x0C_EventPointer";
            return map;
        }
    }
}
