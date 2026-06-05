using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventHaikuFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// Selects which physical FE7 death-quote table this view is browsing.
        /// Mirrors WinForms EventHaikuFE7Form's two address lists + the
        /// N1_FilterComboBox: the main 16-byte table plus the two 12-byte
        /// N1 tutorial tables (Lyn / Eliwood prologue chapters).
        /// </summary>
        public enum HaikuTable
        {
            /// <summary>Main death-quote table — 16-byte records (event_haiku_pointer).</summary>
            Main = 0,
            /// <summary>Lyn-chapter tutorial deaths — 12-byte records (event_haiku_tutorial_1_pointer).</summary>
            Tutorial1 = 1,
            /// <summary>Eliwood-chapter tutorial deaths — 12-byte records (event_haiku_tutorial_2_pointer).</summary>
            Tutorial2 = 2,
        }

        // Main (16-byte) schema — matches WinForms EventHaikuFE7Form.Init.
        // [B0 unit][B1 map][B2][B3][W4 text][B6][B7][D8 event ptr][W12 flag][B14][B15]
        static readonly List<EditorFormRef.FieldDef> _fieldsMain =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "B6", "B7", "D8", "W12", "B14", "B15" });

        // Tutorial (12-byte) schema — matches WinForms EventHaikuFE7Form.N1_Init
        // designer controls (N1_B0/B1/B2/B3, N1_P4 event pointer, N1_W8 flag,
        // N1_B10/B11): [B0 unit][B1 map][B2][B3][P4 event ptr][W8 flag][B10][B11]
        static readonly List<EditorFormRef.FieldDef> _fieldsTutorial =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "P4", "W8", "B10", "B11" });

        HaikuTable _table = HaikuTable.Main;

        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _chapterID;
        uint _unknown02;
        uint _unknown03;
        uint _text;
        uint _unknown06;
        uint _unknown07;
        uint _eventPointer;
        uint _achievementFlag;
        uint _unknown0E;
        uint _unknown0F;

        /// <summary>The currently-selected table (Main / Tutorial1 / Tutorial2).</summary>
        public HaikuTable Table
        {
            get => _table;
            set
            {
                if (SetField(ref _table, value))
                {
                    // Notify the derived read-only properties so bindings on
                    // IsTutorialTable / BlockSize refresh too (#958 review).
                    OnPropertyChanged(nameof(IsTutorialTable));
                    OnPropertyChanged(nameof(BlockSize));
                }
            }
        }

        /// <summary>True when the current table uses the 12-byte tutorial schema.</summary>
        public bool IsTutorialTable => _table != HaikuTable.Main;

        /// <summary>Block size (stride) of the current table: 16 for Main, 12 for tutorial tables.</summary>
        public uint BlockSize => IsTutorialTable ? 12u : 16u;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        public uint ChapterID { get => _chapterID; set => SetField(ref _chapterID, value); }
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
        public static uint ResolveBaseAddr(ROM rom, HaikuTable table)
        {
            if (rom?.RomInfo == null) return 0;
            uint pointer = table switch
            {
                HaikuTable.Tutorial1 => rom.RomInfo.event_haiku_tutorial_1_pointer,
                HaikuTable.Tutorial2 => rom.RomInfo.event_haiku_tutorial_2_pointer,
                _ => rom.RomInfo.event_haiku_pointer,
            };
            if (pointer == 0) return 0;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            return baseAddr;
        }

        public List<AddrResult> LoadList() => LoadList(_table);

        public List<AddrResult> LoadList(HaikuTable table)
        {
            // Use the property setter so PropertyChanged fires for
            // Table / IsTutorialTable / BlockSize (#958 review).
            Table = table;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = ResolveBaseAddr(rom, table);
            if (baseAddr == 0) return new List<AddrResult>();

            uint blockSize = table == HaikuTable.Main ? 16u : 12u;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Both the main and tutorial tables terminate on a 0 unit id
                // (WinForms Init/N1_Init IsDataExists: u8(addr)==0 => stop).
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                // 1-based ROM-stored unit ID.
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
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

            if (IsTutorialTable)
            {
                // 12-byte tutorial schema (event pointer at 0x04, flag at 0x08).
                var v = EditorFormRef.ReadFields(rom, addr, _fieldsTutorial);
                Unit = v["B0"];
                ChapterID = v["B1"];
                Unknown02 = v["B2"];
                Unknown03 = v["B3"];
                Text = 0;             // tutorial rows carry no text id
                Unknown06 = 0;
                Unknown07 = 0;
                EventPointer = v["P4"];
                AchievementFlag = v["W8"];
                Unknown0E = v["B10"];
                Unknown0F = v["B11"];
            }
            else
            {
                var v = EditorFormRef.ReadFields(rom, addr, _fieldsMain);
                Unit = v["B0"];
                ChapterID = v["B1"];
                Unknown02 = v["B2"];
                Unknown03 = v["B3"];
                Text = v["W4"];
                Unknown06 = v["B6"];
                Unknown07 = v["B7"];
                EventPointer = v["D8"];
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
            if (IsTutorialTable)
            {
                var values = new Dictionary<string, uint>
                {
                    ["B0"] = Unit, ["B1"] = ChapterID,
                    ["B2"] = Unknown02, ["B3"] = Unknown03,
                    ["P4"] = EventPointer, ["W8"] = AchievementFlag,
                    ["B10"] = Unknown0E, ["B11"] = Unknown0F,
                };
                EditorFormRef.WriteFields(rom, a, values, _fieldsTutorial);
            }
            else
            {
                var values = new Dictionary<string, uint>
                {
                    ["B0"] = Unit, ["B1"] = ChapterID,
                    ["B2"] = Unknown02, ["B3"] = Unknown03,
                    ["W4"] = Text, ["B6"] = Unknown06, ["B7"] = Unknown07,
                    ["D8"] = EventPointer, ["W12"] = AchievementFlag,
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
                ["Unit"] = $"0x{Unit:X02}",
                ["ChapterID"] = $"0x{ChapterID:X02}",
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
            if (IsTutorialTable)
            {
                return new Dictionary<string, string>
                {
                    ["addr"] = $"0x{a:X08}",
                    ["u8@0x00_Unit"] = $"0x{rom.u8(a + 0):X02}",
                    ["u8@0x01_ChapterID"] = $"0x{rom.u8(a + 1):X02}",
                    ["u8@0x02_Unknown02"] = $"0x{rom.u8(a + 2):X02}",
                    ["u8@0x03_Unknown03"] = $"0x{rom.u8(a + 3):X02}",
                    ["u32@0x04_EventPointer"] = $"0x{rom.u32(a + 4):X08}",
                    ["u16@0x08_AchievementFlag"] = $"0x{rom.u16(a + 8):X04}",
                    ["u8@0x0A_Unknown0E"] = $"0x{rom.u8(a + 10):X02}",
                    ["u8@0x0B_Unknown0F"] = $"0x{rom.u8(a + 11):X02}",
                };
            }
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_Unit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_ChapterID"] = $"0x{rom.u8(a + 1):X02}",
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
            if (IsTutorialTable)
            {
                return new Dictionary<string, string>
                {
                    ["Unit"] = "u8@0x00_Unit",
                    ["ChapterID"] = "u8@0x01_ChapterID",
                    ["Unknown02"] = "u8@0x02_Unknown02",
                    ["Unknown03"] = "u8@0x03_Unknown03",
                    ["EventPointer"] = "u32@0x04_EventPointer",
                    ["AchievementFlag"] = "u16@0x08_AchievementFlag",
                    ["Unknown0E"] = "u8@0x0A_Unknown0E",
                    ["Unknown0F"] = "u8@0x0B_Unknown0F",
                };
            }
            return new Dictionary<string, string>
            {
                ["Unit"] = "u8@0x00_Unit",
                ["ChapterID"] = "u8@0x01_ChapterID",
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
