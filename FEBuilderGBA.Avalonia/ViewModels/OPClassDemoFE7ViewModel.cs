using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Demo editor (FE7J / FE6 variant).
    /// Data: <c>op_class_demo_pointer</c>, datasize = 32, classId at offset 15.
    /// Used by FE7J (MainFE7Form opens OPClassDemoFE7View when the ROM is
    /// AE7J — the FE7U variant uses a separate 28-byte struct in
    /// <see cref="OPClassDemoFE7UViewModel"/>).
    ///
    /// Pointer fields (P0 English-name, P8 Japanese-name-image, P28 anime
    /// block) use <see cref="EditorFormRef.FieldType.Pointer"/> so they
    /// round-trip through <c>rom.p32</c>/<c>write_p32</c>. Storing them as
    /// raw DWords (D0/D8/D28) would write the offset form back to ROM
    /// without the <c>0x08000000</c> high bit, corrupting the address
    /// (gap-sweep #414 fix — same bug PR #537 fixed for FE7U).
    ///
    /// Field semantics re-verified against the WinForms designer labels +
    /// <c>B17_ValueChanged</c> handler:
    ///   P0  = English name pointer (J_0_TEXT "英語ポインタ")
    ///   W4  = Description text ID low (J_4_TEXT "説明文ID")
    ///   W6  = Unknown6 (J_6 "??")
    ///   P8  = Japanese name image pointer (J_8 "日本語名アドレス")
    ///   B12 = Japanese name start position (J_12 "日本語名 開始位置")
    ///   B13 = Japanese name length (J_13 "日本語名の長さ")
    ///   B14 = Palette ID (J_14_UNITPALETTE_PLUS1 "パレットID" — also used as
    ///         palette index by B17_ValueChanged when calling DrawBattleAnime;
    ///         was wrongly named "DisplayWeapon" in the previous VM)
    ///   B15 = Class ID (init callback reads addr+15 for the list name)
    ///   B16 = Ally/Enemy color (J_16 "敵味方カラー")
    ///   B17 = Battle anime (J_17_BATTLEANIME "戦闘アニメ")
    ///   B18 = Magic effect (J_18 "魔法エフェクト")
    ///   B19/B20/B21/B22 = Unknown (J_19 "??" — 4 inputs share one label)
    ///   B23 = Terrain left (J_23_TERRAINBATTLE "表示地形左半分")
    ///   B24 = Terrain right (J_24_TERRAINBATTLE "表示地形右半分")
    ///   B25/B26/B27 = Unknown (J_25 "??" — 3 inputs share one label)
    ///   P28 = Anime block pointer (J_28 "アニメ指定のポインタ")
    ///
    /// The N2 sub-list (animation command sequence) is a separate variable-
    /// length array of 2-byte entries: u8 command + u8 argument, terminated
    /// by command == 0. The array starts at the address pointed to by P28.
    /// Command set (from WF L_0_COMBO):
    ///   1 = ranged attack anime              5 = wait N frames (arg = N/60 sec)
    ///   2 = ranged critical anime            6 = ranged evade
    ///   3 = "hit effect applied"             7 = unused in FE8 (hit effect)
    ///   4 = ranged attack anime              8 = wait for cmd C01/C02/C18
    /// </summary>
    public class OPClassDemoFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "P0", "W4", "W6", "P8",
                "B12", "B13", "B14", "B15",
                "B16", "B17", "B18", "B19",
                "B20", "B21", "B22", "B23",
                "B24", "B25", "B26", "B27",
                "P28"
            });

        /// <summary>WF main block size constant (32 bytes per entry).</summary>
        public const uint BLOCK_SIZE = 32;
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
        uint _descriptionTextIdLow;
        uint _unknown6;
        uint _japaneseNamePointer;
        uint _japaneseNameStart;
        uint _japaneseNameLength;
        uint _paletteId;
        uint _classId;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown19;
        uint _unknown20;
        uint _unknown21;
        uint _unknown22;
        uint _terrainLeft;
        uint _terrainRight;
        uint _unknown25;
        uint _unknown26;
        uint _unknown27;
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
        public uint DescriptionTextIdLow { get => _descriptionTextIdLow; set => SetField(ref _descriptionTextIdLow, value); }
        public uint Unknown6 { get => _unknown6; set => SetField(ref _unknown6, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameStart { get => _japaneseNameStart; set => SetField(ref _japaneseNameStart, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint PaletteId { get => _paletteId; set => SetField(ref _paletteId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown19 { get => _unknown19; set => SetField(ref _unknown19, value); }
        public uint Unknown20 { get => _unknown20; set => SetField(ref _unknown20, value); }
        public uint Unknown21 { get => _unknown21; set => SetField(ref _unknown21, value); }
        public uint Unknown22 { get => _unknown22; set => SetField(ref _unknown22, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint Unknown25 { get => _unknown25; set => SetField(ref _unknown25, value); }
        public uint Unknown26 { get => _unknown26; set => SetField(ref _unknown26, value); }
        public uint Unknown27 { get => _unknown27; set => SetField(ref _unknown27, value); }
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
            // datasize=32, up to 0x42 entries (WF: i <= 0x41).
            for (uint i = 0; i <= 0x41; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                uint cid = rom.u8(addr + 15);
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
            if (addr + 32 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            EnglishNamePointer = v["P0"];
            DescriptionTextIdLow = v["W4"];
            Unknown6 = v["W6"];
            JapaneseNamePointer = v["P8"];
            JapaneseNameStart = v["B12"];
            JapaneseNameLength = v["B13"];
            PaletteId = v["B14"];
            ClassId = v["B15"];
            AllyEnemyColor = v["B16"];
            BattleAnime = v["B17"];
            MagicEffect = v["B18"];
            Unknown19 = v["B19"];
            Unknown20 = v["B20"];
            Unknown21 = v["B21"];
            Unknown22 = v["B22"];
            TerrainLeft = v["B23"];
            TerrainRight = v["B24"];
            Unknown25 = v["B25"];
            Unknown26 = v["B26"];
            Unknown27 = v["B27"];
            AnimePointer = v["P28"];
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
                ["P0"] = EnglishNamePointer, ["W4"] = DescriptionTextIdLow, ["W6"] = Unknown6,
                ["P8"] = JapaneseNamePointer, ["B12"] = JapaneseNameStart, ["B13"] = JapaneseNameLength,
                ["B14"] = PaletteId, ["B15"] = ClassId, ["B16"] = AllyEnemyColor,
                ["B17"] = BattleAnime, ["B18"] = MagicEffect, ["B19"] = Unknown19,
                ["B20"] = Unknown20, ["B21"] = Unknown21, ["B22"] = Unknown22,
                ["B23"] = TerrainLeft, ["B24"] = TerrainRight, ["B25"] = Unknown25,
                ["B26"] = Unknown26, ["B27"] = Unknown27, ["P28"] = AnimePointer,
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
                ["DescriptionTextIdLow"] = $"0x{DescriptionTextIdLow:X04}",
                ["Unknown6"] = $"0x{Unknown6:X04}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X08}",
                ["JapaneseNameStart"] = $"0x{JapaneseNameStart:X02}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["PaletteId"] = $"0x{PaletteId:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown19"] = $"0x{Unknown19:X02}",
                ["Unknown20"] = $"0x{Unknown20:X02}",
                ["Unknown21"] = $"0x{Unknown21:X02}",
                ["Unknown22"] = $"0x{Unknown22:X02}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
                ["Unknown25"] = $"0x{Unknown25:X02}",
                ["Unknown26"] = $"0x{Unknown26:X02}",
                ["Unknown27"] = $"0x{Unknown27:X02}",
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
                ["u16@0x04_DescriptionTextIdLow"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06_Unknown6"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@0x08_JapaneseNamePointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C_JapaneseNameStart"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_JapaneseNameLength"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_PaletteId"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_ClassId"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_AllyEnemyColor"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_BattleAnime"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12_MagicEffect"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13_Unknown19"] = $"0x{rom.u8(a + 19):X02}",
                ["u8@0x14_Unknown20"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15_Unknown21"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16_Unknown22"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_TerrainLeft"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18_TerrainRight"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19_Unknown25"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A_Unknown26"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B_Unknown27"] = $"0x{rom.u8(a + 27):X02}",
                ["u32@0x1C_AnimePointer"] = $"0x{rom.u32(a + 28):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["EnglishNamePointer"] = "u32@0x00_EnglishNamePointer",
            ["DescriptionTextIdLow"] = "u16@0x04_DescriptionTextIdLow",
            ["Unknown6"] = "u16@0x06_Unknown6",
            ["JapaneseNamePointer"] = "u32@0x08_JapaneseNamePointer",
            ["JapaneseNameStart"] = "u8@0x0C_JapaneseNameStart",
            ["JapaneseNameLength"] = "u8@0x0D_JapaneseNameLength",
            ["PaletteId"] = "u8@0x0E_PaletteId",
            ["ClassId"] = "u8@0x0F_ClassId",
            ["AllyEnemyColor"] = "u8@0x10_AllyEnemyColor",
            ["BattleAnime"] = "u8@0x11_BattleAnime",
            ["MagicEffect"] = "u8@0x12_MagicEffect",
            ["Unknown19"] = "u8@0x13_Unknown19",
            ["Unknown20"] = "u8@0x14_Unknown20",
            ["Unknown21"] = "u8@0x15_Unknown21",
            ["Unknown22"] = "u8@0x16_Unknown22",
            ["TerrainLeft"] = "u8@0x17_TerrainLeft",
            ["TerrainRight"] = "u8@0x18_TerrainRight",
            ["Unknown25"] = "u8@0x19_Unknown25",
            ["Unknown26"] = "u8@0x1A_Unknown26",
            ["Unknown27"] = "u8@0x1B_Unknown27",
            ["AnimePointer"] = "u32@0x1C_AnimePointer",
        };
    }
}
