using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7U variant).
    /// Data: <c>op_class_demo_pointer</c>, datasize = 28, classId at offset 11.
    ///
    /// The +0 (English name pointer) and +24 (animation block pointer) fields
    /// are <see cref="EditorFormRef.FieldType.Pointer"/> so they round-trip
    /// through <c>rom.p32</c>/<c>write_p32</c>. Storing them as raw DWords
    /// (D0/D24) would write the offset form back to ROM without the
    /// <c>0x08000000</c> high bit, corrupting the address (#421 Copilot CLI
    /// plan-review finding #1).
    ///
    /// The N2 sub-list (animation command sequence) is a separate variable-
    /// length array of 2-byte entries: u8 command + u8 argument, terminated
    /// by command == 0. The array starts at the address pointed to by P24.
    /// Command set (from WF L_0_COMBO):
    ///   1 = ranged attack anime              5 = wait N frames (arg = N/60 sec)
    ///   2 = ranged critical anime            6 = ranged evade
    ///   3 = "hit effect applied"             7 = unused in FE8 (hit effect)
    ///   4 = ranged attack anime              8 = wait for cmd C01/C02/C18
    /// </summary>
    public class OPClassDemoFE7UViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "P0", "W4", "W8",
                "B10", "B11", "B12", "B13", "B14",
                "B15", "B16", "B17", "B19",
                "B20", "B21", "B22", "B23",
                "P24"
            });

        /// <summary>WF main block size constant (28 bytes per entry).</summary>
        public const uint BLOCK_SIZE = 28;
        /// <summary>WF N2 (anime command) block size constant (2 bytes per row).</summary>
        public const uint N2_BLOCK_SIZE = 2;

        /// <summary>One row of the N2 (animation command sequence) sub-list.</summary>
        public class N2Row
        {
            public uint Index;
            public uint Address;
            public uint Command;
            public uint Argument;
        }

        uint _currentAddr;
        bool _canWrite;
        string _unavailableMessage = "";
        uint _englishNamePointer;
        uint _descriptionTextId;
        uint _japaneseNamePointer;
        uint _japaneseNameLength;
        uint _classId;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown15;
        uint _unknown16;
        uint _unknown17;
        uint _unknown19;
        uint _terrainLeft;
        uint _terrainRight;
        uint _unknown22;
        uint _unknown23;
        uint _animePointer;

        // N2 sub-list state.
        List<N2Row> _n2Entries = new();
        int _selectedN2Index = -1;
        uint _n2Command;
        uint _n2Argument;
        uint _n2SelectedAddress;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }
        public uint EnglishNamePointer { get => _englishNamePointer; set => SetField(ref _englishNamePointer, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown15 { get => _unknown15; set => SetField(ref _unknown15, value); }
        public uint Unknown16 { get => _unknown16; set => SetField(ref _unknown16, value); }
        public uint Unknown17 { get => _unknown17; set => SetField(ref _unknown17, value); }
        public uint Unknown19 { get => _unknown19; set => SetField(ref _unknown19, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint Unknown22 { get => _unknown22; set => SetField(ref _unknown22, value); }
        public uint Unknown23 { get => _unknown23; set => SetField(ref _unknown23, value); }
        public uint AnimePointer { get => _animePointer; set => SetField(ref _animePointer, value); }

        public List<N2Row> N2Entries
        {
            get => _n2Entries;
            set => SetField(ref _n2Entries, value ?? new());
        }
        public int SelectedN2Index { get => _selectedN2Index; set => SetField(ref _selectedN2Index, value); }
        public uint N2Command { get => _n2Command; set => SetField(ref _n2Command, value); }
        public uint N2Argument { get => _n2Argument; set => SetField(ref _n2Argument, value); }
        public uint N2SelectedAddress { get => _n2SelectedAddress; set => SetField(ref _n2SelectedAddress, value); }
        public uint BlockSize => BLOCK_SIZE;
        public uint N2BlockSize => N2_BLOCK_SIZE;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(ptrAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            // Dereference pointer: RomInfo values are pointer addresses, not data addresses.
            // WinForms InputFormRef constructor always does p32() on the basepointer.
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid data address for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // datasize=28, up to 0x42 entries (WF: i <= 0x41).
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                uint cid = rom.u8(addr + 11);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(cid)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            EnglishNamePointer = v["P0"];
            DescriptionTextId = v["W4"];
            JapaneseNamePointer = v["W8"];
            JapaneseNameLength = v["B10"];
            ClassId = v["B11"];
            AllyEnemyColor = v["B12"];
            BattleAnime = v["B13"];
            MagicEffect = v["B14"];
            Unknown15 = v["B15"];
            Unknown16 = v["B16"];
            Unknown17 = v["B17"];
            Unknown19 = v["B19"];
            TerrainLeft = v["B20"];
            TerrainRight = v["B21"];
            Unknown22 = v["B22"];
            Unknown23 = v["B23"];
            AnimePointer = v["P24"];
            CanWrite = true;

            // Rebuild the N2 (animation command) sub-list for the new entry.
            LoadN2List();
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["P0"] = EnglishNamePointer, ["W4"] = DescriptionTextId, ["W8"] = JapaneseNamePointer,
                ["B10"] = JapaneseNameLength, ["B11"] = ClassId, ["B12"] = AllyEnemyColor,
                ["B13"] = BattleAnime, ["B14"] = MagicEffect, ["B15"] = Unknown15,
                ["B16"] = Unknown16, ["B17"] = Unknown17, ["B19"] = Unknown19,
                ["B20"] = TerrainLeft, ["B21"] = TerrainRight, ["B22"] = Unknown22,
                ["B23"] = Unknown23, ["P24"] = AnimePointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        /// <summary>
        /// Scan the animation-command block at <see cref="AnimePointer"/>,
        /// populating <see cref="N2Entries"/>. The block is a sequence of
        /// 2-byte rows (u8 command + u8 argument); iteration stops when
        /// command == 0 (mirrors WF <c>N2_Init</c>'s <c>isValid</c> predicate).
        /// Safe to call when AnimePointer is 0 or out of range - clears the
        /// list and selection state.
        /// </summary>
        public void LoadN2List()
        {
            ROM rom = CoreState.ROM;
            var result = new List<N2Row>();
            uint ptr = AnimePointer;
            uint offset = U.toOffset(ptr);
            if (rom == null || ptr == 0 || !U.isSafetyOffset(offset, rom))
            {
                N2Entries = result;
                SelectedN2Index = -1;
                N2Command = 0;
                N2Argument = 0;
                N2SelectedAddress = 0;
                return;
            }
            // Iterate up to a safety cap. WF uses i <= 0xFF but the terminator
            // (cmd == 0) almost always hits inside a few rows.
            const int MaxRows = 0x100;
            for (int i = 0; i < MaxRows; i++)
            {
                uint rowAddr = offset + (uint)(i * 2);
                if (rowAddr + 2 > (uint)rom.Data.Length) break;
                uint cmd = rom.u8(rowAddr + 0);
                if (cmd == 0) break;
                result.Add(new N2Row
                {
                    Index = (uint)i,
                    Address = rowAddr,
                    Command = cmd,
                    Argument = rom.u8(rowAddr + 1),
                });
            }
            N2Entries = result;
            if (result.Count > 0)
            {
                SelectedN2Index = 0;
                N2Command = result[0].Command;
                N2Argument = result[0].Argument;
                N2SelectedAddress = result[0].Address;
            }
            else
            {
                SelectedN2Index = -1;
                N2Command = 0;
                N2Argument = 0;
                N2SelectedAddress = 0;
            }
        }

        /// <summary>Load the selected N2 row's command/argument into the editor fields.</summary>
        public void LoadN2Row(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= N2Entries.Count)
            {
                SelectedN2Index = -1;
                N2Command = 0;
                N2Argument = 0;
                N2SelectedAddress = 0;
                return;
            }
            var row = N2Entries[rowIndex];
            SelectedN2Index = rowIndex;
            N2Command = row.Command;
            N2Argument = row.Argument;
            N2SelectedAddress = row.Address;
        }

        /// <summary>
        /// Write the currently selected N2 row (Command + Argument) back to
        /// ROM at <c>AnimePointer + 2 * SelectedN2Index</c>. Returns
        /// <c>true</c> on success, <c>false</c> when the row index, anime
        /// pointer, or row address is invalid. Caller should wrap the call
        /// in <c>UndoService.Begin/Commit</c> for undo support.
        /// </summary>
        public bool WriteN2Entry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (SelectedN2Index < 0 || SelectedN2Index >= N2Entries.Count) return false;
            uint offset = U.toOffset(AnimePointer);
            if (AnimePointer == 0 || !U.isSafetyOffset(offset, rom)) return false;
            uint rowAddr = offset + (uint)(SelectedN2Index * 2);
            if (rowAddr + 2 > (uint)rom.Data.Length) return false;
            rom.write_u8(rowAddr + 0, N2Command & 0xFF);
            rom.write_u8(rowAddr + 1, N2Argument & 0xFF);
            // Refresh the in-memory row so the list reflects the new bytes.
            var row = N2Entries[SelectedN2Index];
            row.Command = N2Command & 0xFF;
            row.Argument = N2Argument & 0xFF;
            return true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EnglishNamePointer"] = $"0x{EnglishNamePointer:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X04}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X04}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown15"] = $"0x{Unknown15:X02}",
                ["Unknown16"] = $"0x{Unknown16:X02}",
                ["Unknown17"] = $"0x{Unknown17:X02}",
                ["Unknown19"] = $"0x{Unknown19:X02}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
                ["Unknown22"] = $"0x{Unknown22:X02}",
                ["Unknown23"] = $"0x{Unknown23:X02}",
                ["AnimePointer"] = $"0x{AnimePointer:X08}",
                ["N2RowCount"] = N2Entries.Count.ToString(),
                ["N2SelectedIndex"] = SelectedN2Index.ToString(),
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
                ["u32@0x00_EnglishNamePointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04_DescriptionTextId"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x08_JapaneseNamePointer"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_JapaneseNameLength"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_ClassId"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_AllyEnemyColor"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_BattleAnime"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_MagicEffect"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown15"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_Unknown16"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_Unknown17"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x13_Unknown19"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14_TerrainLeft"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_TerrainRight"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_Unknown22"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_Unknown23"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18_AnimePointer"] = $"0x{rom.u32(a + 24):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["EnglishNamePointer"] = "u32@0x00_EnglishNamePointer",
            ["DescriptionTextId"] = "u16@0x04_DescriptionTextId",
            ["JapaneseNamePointer"] = "u16@0x08_JapaneseNamePointer",
            ["JapaneseNameLength"] = "u8@0x0A_JapaneseNameLength",
            ["ClassId"] = "u8@0x0B_ClassId",
            ["AllyEnemyColor"] = "u8@0x0C_AllyEnemyColor",
            ["BattleAnime"] = "u8@0x0D_BattleAnime",
            ["MagicEffect"] = "u8@0x0E_MagicEffect",
            ["Unknown15"] = "u8@0x0F_Unknown15",
            ["Unknown16"] = "u8@0x10_Unknown16",
            ["Unknown17"] = "u8@0x11_Unknown17",
            ["Unknown19"] = "u8@0x13_Unknown19",
            ["TerrainLeft"] = "u8@0x14_TerrainLeft",
            ["TerrainRight"] = "u8@0x15_TerrainRight",
            ["Unknown22"] = "u8@0x16_Unknown22",
            ["Unknown23"] = "u8@0x17_Unknown23",
            ["AnimePointer"] = "u32@0x18_AnimePointer",
        };
    }
}
