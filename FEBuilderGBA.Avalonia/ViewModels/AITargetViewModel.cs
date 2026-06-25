using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AITargetViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// Fixed number of AI Target (AI3) profiles. WinForms <c>AITargetForm</c> uses the count
        /// predicate <c>i &lt; 8</c> and <c>StructExportCore</c> hardcodes 8 for <c>ai_targets</c>
        /// (issue #1419).
        /// </summary>
        public const uint ProfileCount = 8;

        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] {
                "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7",
                "B8", "B9", "B10", "B11", "B12", "B13", "B14", "B15",
                "B16", "B17", "B18", "B19" });

        uint _currentAddr;
        bool _isLoaded;
        uint _lethalDamagePriority;
        uint _enemyRemainingHPPriority;
        uint _enemyDistancePriority;
        uint _enemyClassPriority;
        uint _currentTurnPriority;
        uint _counterDamageWarning;
        uint _surroundWarning;
        uint _selfRemainingHPWarning;
        uint _unknown8;
        uint _unknown9;
        uint _unknown10;
        uint _unknown11;
        uint _unknown12;
        uint _unknown13;
        uint _unknown14;
        uint _unknown15;
        uint _unknown16;
        uint _unknown17;
        uint _unknown18;
        uint _unknown19;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Lethal damage and final damage priority (offset 0)</summary>
        public uint LethalDamagePriority { get => _lethalDamagePriority; set => SetField(ref _lethalDamagePriority, value); }
        /// <summary>Enemy remaining HP priority (offset 1)</summary>
        public uint EnemyRemainingHPPriority { get => _enemyRemainingHPPriority; set => SetField(ref _enemyRemainingHPPriority, value); }
        /// <summary>Enemy distance priority (offset 2)</summary>
        public uint EnemyDistancePriority { get => _enemyDistancePriority; set => SetField(ref _enemyDistancePriority, value); }
        /// <summary>Enemy class priority (offset 3)</summary>
        public uint EnemyClassPriority { get => _enemyClassPriority; set => SetField(ref _enemyClassPriority, value); }
        /// <summary>Current turn priority (offset 4)</summary>
        public uint CurrentTurnPriority { get => _currentTurnPriority; set => SetField(ref _currentTurnPriority, value); }
        /// <summary>Counter damage warning level (offset 5)</summary>
        public uint CounterDamageWarning { get => _counterDamageWarning; set => SetField(ref _counterDamageWarning, value); }
        /// <summary>Surround warning level (offset 6)</summary>
        public uint SurroundWarning { get => _surroundWarning; set => SetField(ref _surroundWarning, value); }
        /// <summary>Self remaining HP warning level (offset 7)</summary>
        public uint SelfRemainingHPWarning { get => _selfRemainingHPWarning; set => SetField(ref _selfRemainingHPWarning, value); }
        public uint Unknown8 { get => _unknown8; set => SetField(ref _unknown8, value); }
        public uint Unknown9 { get => _unknown9; set => SetField(ref _unknown9, value); }
        public uint Unknown10 { get => _unknown10; set => SetField(ref _unknown10, value); }
        public uint Unknown11 { get => _unknown11; set => SetField(ref _unknown11, value); }
        public uint Unknown12 { get => _unknown12; set => SetField(ref _unknown12, value); }
        public uint Unknown13 { get => _unknown13; set => SetField(ref _unknown13, value); }
        public uint Unknown14 { get => _unknown14; set => SetField(ref _unknown14, value); }
        public uint Unknown15 { get => _unknown15; set => SetField(ref _unknown15, value); }
        public uint Unknown16 { get => _unknown16; set => SetField(ref _unknown16, value); }
        public uint Unknown17 { get => _unknown17; set => SetField(ref _unknown17, value); }
        public uint Unknown18 { get => _unknown18; set => SetField(ref _unknown18, value); }
        public uint Unknown19 { get => _unknown19; set => SetField(ref _unknown19, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ai3_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 20;
            var result = new List<AddrResult>();
            // Fixed count of 8 profiles, matching WinForms AITargetForm (count predicate i < 8)
            // and StructExportCore (ai_targets hardcodes 8). Do NOT use an all-zero early-stop:
            // it can hide valid later profiles, and a >8 cap surfaces phantom rows 8-15 whose
            // edits write 20 bytes outside the AI3 table (issue #1419).
            for (uint i = 0; i < ProfileCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                result.Add(new AddrResult(addr, $"0x{i:X02} AI Target Profile {i}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            LethalDamagePriority = values["B0"];
            EnemyRemainingHPPriority = values["B1"];
            EnemyDistancePriority = values["B2"];
            EnemyClassPriority = values["B3"];
            CurrentTurnPriority = values["B4"];
            CounterDamageWarning = values["B5"];
            SurroundWarning = values["B6"];
            SelfRemainingHPWarning = values["B7"];
            Unknown8 = values["B8"];
            Unknown9 = values["B9"];
            Unknown10 = values["B10"];
            Unknown11 = values["B11"];
            Unknown12 = values["B12"];
            Unknown13 = values["B13"];
            Unknown14 = values["B14"];
            Unknown15 = values["B15"];
            Unknown16 = values["B16"];
            Unknown17 = values["B17"];
            Unknown18 = values["B18"];
            Unknown19 = values["B19"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = LethalDamagePriority, ["B1"] = EnemyRemainingHPPriority,
                ["B2"] = EnemyDistancePriority, ["B3"] = EnemyClassPriority,
                ["B4"] = CurrentTurnPriority, ["B5"] = CounterDamageWarning,
                ["B6"] = SurroundWarning, ["B7"] = SelfRemainingHPWarning,
                ["B8"] = Unknown8, ["B9"] = Unknown9,
                ["B10"] = Unknown10, ["B11"] = Unknown11,
                ["B12"] = Unknown12, ["B13"] = Unknown13,
                ["B14"] = Unknown14, ["B15"] = Unknown15,
                ["B16"] = Unknown16, ["B17"] = Unknown17,
                ["B18"] = Unknown18, ["B19"] = Unknown19,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["LethalDamagePriority"] = $"0x{LethalDamagePriority:X02}",
                ["EnemyRemainingHPPriority"] = $"0x{EnemyRemainingHPPriority:X02}",
                ["EnemyDistancePriority"] = $"0x{EnemyDistancePriority:X02}",
                ["EnemyClassPriority"] = $"0x{EnemyClassPriority:X02}",
                ["CurrentTurnPriority"] = $"0x{CurrentTurnPriority:X02}",
                ["CounterDamageWarning"] = $"0x{CounterDamageWarning:X02}",
                ["SurroundWarning"] = $"0x{SurroundWarning:X02}",
                ["SelfRemainingHPWarning"] = $"0x{SelfRemainingHPWarning:X02}",
                ["Unknown8"] = $"0x{Unknown8:X02}",
                ["Unknown9"] = $"0x{Unknown9:X02}",
                ["Unknown10"] = $"0x{Unknown10:X02}",
                ["Unknown11"] = $"0x{Unknown11:X02}",
                ["Unknown12"] = $"0x{Unknown12:X02}",
                ["Unknown13"] = $"0x{Unknown13:X02}",
                ["Unknown14"] = $"0x{Unknown14:X02}",
                ["Unknown15"] = $"0x{Unknown15:X02}",
                ["Unknown16"] = $"0x{Unknown16:X02}",
                ["Unknown17"] = $"0x{Unknown17:X02}",
                ["Unknown18"] = $"0x{Unknown18:X02}",
                ["Unknown19"] = $"0x{Unknown19:X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["LethalDamagePriority"] = "u8@0x00_LethalDamagePriority",
                ["EnemyRemainingHPPriority"] = "u8@0x01_EnemyRemainingHPPriority",
                ["EnemyDistancePriority"] = "u8@0x02_EnemyDistancePriority",
                ["EnemyClassPriority"] = "u8@0x03_EnemyClassPriority",
                ["CurrentTurnPriority"] = "u8@0x04_CurrentTurnPriority",
                ["CounterDamageWarning"] = "u8@0x05_CounterDamageWarning",
                ["SurroundWarning"] = "u8@0x06_SurroundWarning",
                ["SelfRemainingHPWarning"] = "u8@0x07_SelfRemainingHPWarning",
                ["Unknown8"] = "u8@0x08_Unknown8",
                ["Unknown9"] = "u8@0x09_Unknown9",
                ["Unknown10"] = "u8@0x0A_Unknown10",
                ["Unknown11"] = "u8@0x0B_Unknown11",
                ["Unknown12"] = "u8@0x0C_Unknown12",
                ["Unknown13"] = "u8@0x0D_Unknown13",
                ["Unknown14"] = "u8@0x0E_Unknown14",
                ["Unknown15"] = "u8@0x0F_Unknown15",
                ["Unknown16"] = "u8@0x10_Unknown16",
                ["Unknown17"] = "u8@0x11_Unknown17",
                ["Unknown18"] = "u8@0x12_Unknown18",
                ["Unknown19"] = "u8@0x13_Unknown19",
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
                ["u8@0x00_LethalDamagePriority"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_EnemyRemainingHPPriority"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_EnemyDistancePriority"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_EnemyClassPriority"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04_CurrentTurnPriority"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_CounterDamageWarning"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06_SurroundWarning"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_SelfRemainingHPWarning"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08_Unknown8"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_Unknown9"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_Unknown10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Unknown11"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C_Unknown12"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_Unknown13"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_Unknown14"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown15"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_Unknown16"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_Unknown17"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12_Unknown18"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13_Unknown19"] = $"0x{rom.u8(a + 19):X02}",
            };
        }
    }
}
