using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventBattleTalkFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// Selects which physical FE7 battle-talk table this view is browsing.
        /// Mirrors WinForms EventBattleTalkFE7Form's two address lists: the main
        /// 16-byte table (event_ballte_talk_pointer) plus the secondary 12-byte
        /// N1 table (event_ballte_talk2_pointer — in FE6 this slot holds the
        /// boss generic-conversation table, hence the second list in FE7 too).
        /// </summary>
        public enum BattleTalkTable
        {
            /// <summary>Main battle-talk table — 16-byte records (event_ballte_talk_pointer).</summary>
            Main = 0,
            /// <summary>Secondary battle-talk table — 12-byte records (event_ballte_talk2_pointer).</summary>
            Secondary = 1,
        }

        // Main (16-byte) schema — matches WinForms EventBattleTalkFE7Form.Init.
        // [B0 atk][B1 def][B2][B3][W4 text][B6][B7][P8 event ptr][W12 flag][B14][B15]
        // Offset 0x08 is a P (pointer) field, matching the WinForms Designer control
        // named "P8" (Hexadecimal NumericUpDown) + "L_8_EVENT" event-address box: it
        // must read via rom.p32 (strip 0x08000000) and write via rom.write_p32
        // (re-add 0x08000000), NOT a raw u32 — otherwise a nonzero event pointer
        // round-trips as a bare offset (#1437 plan review).
        static readonly List<EditorFormRef.FieldDef> _fieldsMain =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "P8", "W12", "B14", "B15" });

        // Secondary (12-byte) schema — matches WinForms EventBattleTalkFE7Form
        // N1_Init designer controls (N1_B0/B1/B2/B3, N1_W4 text, N1_B6/B7,
        // N1_W8 flag, N1_B10/B11):
        // [B0 unit][B1 map][B2][B3][W4 text][B6][B7][W8 flag][B10][B11]
        static readonly List<EditorFormRef.FieldDef> _fieldsSecondary =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "W8", "B10", "B11" });

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
        uint _eventPointer;
        uint _achievementFlag;
        uint _unknown0E;
        uint _unknown0F;

        /// <summary>The currently-selected table (Main / Secondary).</summary>
        public BattleTalkTable Table
        {
            get => _table;
            set
            {
                if (SetField(ref _table, value))
                {
                    // Notify the derived read-only properties so bindings on
                    // IsSecondaryTable / BlockSize refresh too (#958 review).
                    OnPropertyChanged(nameof(IsSecondaryTable));
                    OnPropertyChanged(nameof(BlockSize));
                }
            }
        }

        /// <summary>True when the current table uses the 12-byte secondary schema.</summary>
        public bool IsSecondaryTable => _table == BattleTalkTable.Secondary;

        /// <summary>Block size (stride) of the current table: 16 for Main, 12 for Secondary.</summary>
        public uint BlockSize => IsSecondaryTable ? 12u : 16u;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AttackerUnit { get => _attackerUnit; set => SetField(ref _attackerUnit, value); }
        public uint DefenderUnit { get => _defenderUnit; set => SetField(ref _defenderUnit, value); }
        public uint Unknown02 { get => _unknown02; set => SetField(ref _unknown02, value); }
        public uint Unknown03 { get => _unknown03; set => SetField(ref _unknown03, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint Unknown06 { get => _unknown06; set => SetField(ref _unknown06, value); }
        public uint Unknown07 { get => _unknown07; set => SetField(ref _unknown07, value); }
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Unknown0E { get => _unknown0E; set => SetField(ref _unknown0E, value); }
        public uint Unknown0F { get => _unknown0F; set => SetField(ref _unknown0F, value); }

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
            // Table / IsSecondaryTable / BlockSize (#958 review).
            Table = table;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = ResolveBaseAddr(rom, table);
            if (baseAddr == 0) return new List<AddrResult>();

            if (table == BattleTalkTable.Secondary)
                return LoadSecondaryList(rom, baseAddr);

            const uint blockSize = 16; // FE7 main uses 16-byte blocks
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

        // Secondary 12-byte list — WinForms N1_Init terminates on a 0/0xFF unit
        // (single u8), and the N1 list draws only the single unit name.
        static List<AddrResult> LoadSecondaryList(ROM rom, uint baseAddr)
        {
            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint unit = rom.u8(addr);
                if (unit == 0 || unit == 0xFF) break;

                string unitName = NameResolver.GetUnitNameByOneBasedId(unit);
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
                // 12-byte secondary schema (text at 0x04, flag at 0x08, no event ptr).
                var v = EditorFormRef.ReadFields(rom, addr, _fieldsSecondary);
                AttackerUnit = v["B0"];
                DefenderUnit = v["B1"]; // secondary stores map/chapter id here
                Unknown02 = v["B2"];
                Unknown03 = v["B3"];
                Text = v["W4"];
                Unknown06 = v["B6"];
                Unknown07 = v["B7"];
                EventPointer = 0;       // secondary rows carry no event pointer
                AchievementFlag = v["W8"];
                Unknown0E = v["B10"];
                Unknown0F = v["B11"];
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
                EventPointer = v["P8"]; // p32-decoded offset (0x08000000 stripped)
                AchievementFlag = v["W12"];
                Unknown0E = v["B14"];
                Unknown0F = v["B15"];
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
                    ["W8"] = AchievementFlag,
                    ["B10"] = Unknown0E, ["B11"] = Unknown0F,
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
                    ["P8"] = EventPointer, ["W12"] = AchievementFlag,
                    ["B14"] = Unknown0E, ["B15"] = Unknown0F,
                };
                EditorFormRef.WriteFields(rom, a, values, _fieldsMain);
            }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
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
                ["EventPointer"] = $"0x{EventPointer:X08}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Unknown0E"] = $"0x{Unknown0E:X02}",
                ["Unknown0F"] = $"0x{Unknown0F:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            if (IsSecondaryTable)
            {
                return new Dictionary<string, string>
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
                    ["u8@0x0A_Unknown0E"] = $"0x{rom.u8(a + 10):X02}",
                    ["u8@0x0B_Unknown0F"] = $"0x{rom.u8(a + 11):X02}",
                };
            }
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_AttackerUnit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_DefenderUnit"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown03"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04_Text"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06_Unknown06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_Unknown07"] = $"0x{rom.u8(a + 7):X02}",
                ["u32@0x08_EventPointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u16@0x0C_AchievementFlag"] = $"0x{rom.u16(a + 12):X04}",
                ["u8@0x0E_Unknown0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            if (IsSecondaryTable)
            {
                return new Dictionary<string, string>
                {
                    ["AttackerUnit"] = "u8@0x00_AttackerUnit",
                    ["DefenderUnit"] = "u8@0x01_DefenderUnit",
                    ["Unknown02"] = "u8@0x02_Unknown02",
                    ["Unknown03"] = "u8@0x03_Unknown03",
                    ["Text"] = "u16@0x04_Text",
                    ["Unknown06"] = "u8@0x06_Unknown06",
                    ["Unknown07"] = "u8@0x07_Unknown07",
                    ["AchievementFlag"] = "u16@0x08_AchievementFlag",
                    ["Unknown0E"] = "u8@0x0A_Unknown0E",
                    ["Unknown0F"] = "u8@0x0B_Unknown0F",
                };
            }
            return new Dictionary<string, string>
            {
                ["AttackerUnit"] = "u8@0x00_AttackerUnit",
                ["DefenderUnit"] = "u8@0x01_DefenderUnit",
                ["Unknown02"] = "u8@0x02_Unknown02",
                ["Unknown03"] = "u8@0x03_Unknown03",
                ["Text"] = "u16@0x04_Text",
                ["Unknown06"] = "u8@0x06_Unknown06",
                ["Unknown07"] = "u8@0x07_Unknown07",
                ["EventPointer"] = "u32@0x08_EventPointer",
                ["AchievementFlag"] = "u16@0x0C_AchievementFlag",
                ["Unknown0E"] = "u8@0x0E_Unknown0E",
                ["Unknown0F"] = "u8@0x0F_Unknown0F",
            };
        }
    }
}
