// SPDX-License-Identifier: GPL-3.0-or-later
// MonsterItem editor ViewModel — covers all 3 WinForms lists in one VM:
//
//   Tab 1 (Item):        monster_item_item_pointer, 5-byte records
//   Tab 2 (Probability): monster_item_probability_pointer, 5-byte records
//   Tab 3 (Holdings):    monster_item_table_pointer, 32-byte records
//
// See issue #394 plan v2 for the structural rationale.

using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterItemViewerViewModel : ViewModelBase, IDataVerifiable
    {
        // ----- Tab 1 (Item) -----
        // 5 bytes: B0..B4 = 5 item ids. The legacy property names
        // (ItemId / DropRate / Unknown1..3) are preserved as aliases so
        // existing tests + ListParityHelper keep working.
        static readonly List<EditorFormRef.FieldDef> _itemFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4" });

        // ----- Tab 2 (Probability) -----
        // 5 bytes: B0..B4 = probability per item slot.
        static readonly List<EditorFormRef.FieldDef> _probFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4" });

        // ----- Tab 3 (Holdings) -----
        // 32 bytes:
        //   B0       = class id
        //   B1..B5   = holdings set 1 — 5 item ids
        //   B6..B10  = holdings set 2 — 5 item ids
        //   B11..B15 = holdings set 1 — 5 probabilities
        //   B16..B20 = holdings set 2 — 5 probabilities
        //   B21..B25 = holdings set 1 — 5 item probabilities
        //   B26..B30 = holdings set 2 — 5 item probabilities
        //   B31      = unknown / 00
        static readonly List<EditorFormRef.FieldDef> _holdingFields =
            EditorFormRef.DetectFields(new[]
            {
                "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7",
                "B8", "B9", "B10", "B11", "B12", "B13", "B14", "B15",
                "B16", "B17", "B18", "B19", "B20", "B21", "B22", "B23",
                "B24", "B25", "B26", "B27", "B28", "B29", "B30", "B31",
            });

        // =================== Tab 1 (Item) state ===================
        uint _currentAddr;
        bool _canWrite;
        uint _itemId;
        uint _dropRate;
        uint _unknown1, _unknown2, _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // Legacy alias names (Tab 1). DO NOT rename — existing
        // AvaloniaEditorTests + ListParityHelper depend on the public surface.
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint DropRate { get => _dropRate; set => SetField(ref _dropRate, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

        // =================== Tab 2 (Probability) state ===================
        uint _probAddr;
        bool _probCanWrite;
        uint _prob1, _prob2, _prob3, _prob4, _prob5;

        public uint ProbabilityAddr { get => _probAddr; set => SetField(ref _probAddr, value); }
        public bool ProbabilityCanWrite { get => _probCanWrite; set => SetField(ref _probCanWrite, value); }
        public uint Prob1 { get => _prob1; set => SetField(ref _prob1, value); }
        public uint Prob2 { get => _prob2; set => SetField(ref _prob2, value); }
        public uint Prob3 { get => _prob3; set => SetField(ref _prob3, value); }
        public uint Prob4 { get => _prob4; set => SetField(ref _prob4, value); }
        public uint Prob5 { get => _prob5; set => SetField(ref _prob5, value); }

        public uint ProbabilitySum => Prob1 + Prob2 + Prob3 + Prob4 + Prob5;

        // =================== Tab 3 (Holdings) state ===================
        uint _holdingAddr;
        bool _holdingCanWrite;
        uint _classId;
        // 10 item ids (5 set-1 + 5 set-2)
        uint _hi1, _hi2, _hi3, _hi4, _hi5, _hi6, _hi7, _hi8, _hi9, _hi10;
        // 10 probabilities (5 set-1 + 5 set-2)
        uint _hp1, _hp2, _hp3, _hp4, _hp5, _hp6, _hp7, _hp8, _hp9, _hp10;
        // 10 item-probabilities (5 set-1 + 5 set-2)
        uint _hip1, _hip2, _hip3, _hip4, _hip5, _hip6, _hip7, _hip8, _hip9, _hip10;
        uint _b31;

        public uint HoldingAddr { get => _holdingAddr; set => SetField(ref _holdingAddr, value); }
        public bool HoldingCanWrite { get => _holdingCanWrite; set => SetField(ref _holdingCanWrite, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }

        public uint HoldingItem1 { get => _hi1; set => SetField(ref _hi1, value); }
        public uint HoldingItem2 { get => _hi2; set => SetField(ref _hi2, value); }
        public uint HoldingItem3 { get => _hi3; set => SetField(ref _hi3, value); }
        public uint HoldingItem4 { get => _hi4; set => SetField(ref _hi4, value); }
        public uint HoldingItem5 { get => _hi5; set => SetField(ref _hi5, value); }
        public uint HoldingItem6 { get => _hi6; set => SetField(ref _hi6, value); }
        public uint HoldingItem7 { get => _hi7; set => SetField(ref _hi7, value); }
        public uint HoldingItem8 { get => _hi8; set => SetField(ref _hi8, value); }
        public uint HoldingItem9 { get => _hi9; set => SetField(ref _hi9, value); }
        public uint HoldingItem10 { get => _hi10; set => SetField(ref _hi10, value); }

        public uint HoldingProb1 { get => _hp1; set => SetField(ref _hp1, value); }
        public uint HoldingProb2 { get => _hp2; set => SetField(ref _hp2, value); }
        public uint HoldingProb3 { get => _hp3; set => SetField(ref _hp3, value); }
        public uint HoldingProb4 { get => _hp4; set => SetField(ref _hp4, value); }
        public uint HoldingProb5 { get => _hp5; set => SetField(ref _hp5, value); }
        public uint HoldingProb6 { get => _hp6; set => SetField(ref _hp6, value); }
        public uint HoldingProb7 { get => _hp7; set => SetField(ref _hp7, value); }
        public uint HoldingProb8 { get => _hp8; set => SetField(ref _hp8, value); }
        public uint HoldingProb9 { get => _hp9; set => SetField(ref _hp9, value); }
        public uint HoldingProb10 { get => _hp10; set => SetField(ref _hp10, value); }

        public uint HoldingItemProb1 { get => _hip1; set => SetField(ref _hip1, value); }
        public uint HoldingItemProb2 { get => _hip2; set => SetField(ref _hip2, value); }
        public uint HoldingItemProb3 { get => _hip3; set => SetField(ref _hip3, value); }
        public uint HoldingItemProb4 { get => _hip4; set => SetField(ref _hip4, value); }
        public uint HoldingItemProb5 { get => _hip5; set => SetField(ref _hip5, value); }
        public uint HoldingItemProb6 { get => _hip6; set => SetField(ref _hip6, value); }
        public uint HoldingItemProb7 { get => _hip7; set => SetField(ref _hip7, value); }
        public uint HoldingItemProb8 { get => _hip8; set => SetField(ref _hip8, value); }
        public uint HoldingItemProb9 { get => _hip9; set => SetField(ref _hip9, value); }
        public uint HoldingItemProb10 { get => _hip10; set => SetField(ref _hip10, value); }

        public uint B31 { get => _b31; set => SetField(ref _b31, value); }

        /// <summary>Sum of holdings set-1 probabilities (B11..B15).</summary>
        public uint HoldingSum1 =>
            HoldingProb1 + HoldingProb2 + HoldingProb3 + HoldingProb4 + HoldingProb5;
        /// <summary>Sum of holdings set-2 probabilities (B16..B20).</summary>
        public uint HoldingSum2 =>
            HoldingProb6 + HoldingProb7 + HoldingProb8 + HoldingProb9 + HoldingProb10;

        // =================== Tab 1 (Item) load/write ===================

        public List<AddrResult> LoadMonsterItemList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 5);
                if (addr + 5 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                uint itemId = rom.u8(addr);
                string itemName = NameResolver.GetItemName(itemId);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterItem(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 5 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _itemFields);
            ItemId = values["B0"];
            DropRate = values["B1"];
            Unknown1 = values["B2"];
            Unknown2 = values["B3"];
            Unknown3 = values["B4"];
            CanWrite = true;
        }

        public void WriteMonsterItem()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 5 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = ItemId, ["B1"] = DropRate,
                ["B2"] = Unknown1, ["B3"] = Unknown2, ["B4"] = Unknown3,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _itemFields);
        }

        // =================== Tab 2 (Probability) load/write ===================

        public List<AddrResult> LoadMonsterItemProbabilityList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_item_probability_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 5);
                if (addr + 5 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                string name = $"{U.ToHexString(i)} Prob";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterItemProbability(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 5 > (uint)rom.Data.Length) return;

            ProbabilityAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _probFields);
            Prob1 = values["B0"];
            Prob2 = values["B1"];
            Prob3 = values["B2"];
            Prob4 = values["B3"];
            Prob5 = values["B4"];
            ProbabilityCanWrite = true;
        }

        public void WriteMonsterItemProbability()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || ProbabilityAddr == 0) return;
            if (ProbabilityAddr + 5 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = Prob1, ["B1"] = Prob2, ["B2"] = Prob3,
                ["B3"] = Prob4, ["B4"] = Prob5,
            };
            EditorFormRef.WriteFields(rom, ProbabilityAddr, values, _probFields);
        }

        // =================== Tab 3 (Holdings) load/write ===================

        public List<AddrResult> LoadMonsterItemHoldingsList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_item_table_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 32);
                if (addr + 32 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                uint classId = rom.u8(addr);
                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterItemHoldings(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 32 > (uint)rom.Data.Length) return;

            HoldingAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _holdingFields);
            ClassId = v["B0"];
            HoldingItem1 = v["B1"]; HoldingItem2 = v["B2"]; HoldingItem3 = v["B3"];
            HoldingItem4 = v["B4"]; HoldingItem5 = v["B5"];
            HoldingItem6 = v["B6"]; HoldingItem7 = v["B7"]; HoldingItem8 = v["B8"];
            HoldingItem9 = v["B9"]; HoldingItem10 = v["B10"];
            HoldingProb1 = v["B11"]; HoldingProb2 = v["B12"]; HoldingProb3 = v["B13"];
            HoldingProb4 = v["B14"]; HoldingProb5 = v["B15"];
            HoldingProb6 = v["B16"]; HoldingProb7 = v["B17"]; HoldingProb8 = v["B18"];
            HoldingProb9 = v["B19"]; HoldingProb10 = v["B20"];
            HoldingItemProb1 = v["B21"]; HoldingItemProb2 = v["B22"]; HoldingItemProb3 = v["B23"];
            HoldingItemProb4 = v["B24"]; HoldingItemProb5 = v["B25"];
            HoldingItemProb6 = v["B26"]; HoldingItemProb7 = v["B27"]; HoldingItemProb8 = v["B28"];
            HoldingItemProb9 = v["B29"]; HoldingItemProb10 = v["B30"];
            B31 = v["B31"];
            HoldingCanWrite = true;
        }

        public void WriteMonsterItemHoldings()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || HoldingAddr == 0) return;
            if (HoldingAddr + 32 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = ClassId,
                ["B1"] = HoldingItem1, ["B2"] = HoldingItem2, ["B3"] = HoldingItem3,
                ["B4"] = HoldingItem4, ["B5"] = HoldingItem5,
                ["B6"] = HoldingItem6, ["B7"] = HoldingItem7, ["B8"] = HoldingItem8,
                ["B9"] = HoldingItem9, ["B10"] = HoldingItem10,
                ["B11"] = HoldingProb1, ["B12"] = HoldingProb2, ["B13"] = HoldingProb3,
                ["B14"] = HoldingProb4, ["B15"] = HoldingProb5,
                ["B16"] = HoldingProb6, ["B17"] = HoldingProb7, ["B18"] = HoldingProb8,
                ["B19"] = HoldingProb9, ["B20"] = HoldingProb10,
                ["B21"] = HoldingItemProb1, ["B22"] = HoldingItemProb2, ["B23"] = HoldingItemProb3,
                ["B24"] = HoldingItemProb4, ["B25"] = HoldingItemProb5,
                ["B26"] = HoldingItemProb6, ["B27"] = HoldingItemProb7, ["B28"] = HoldingItemProb8,
                ["B29"] = HoldingItemProb9, ["B30"] = HoldingItemProb10,
                ["B31"] = B31,
            };
            EditorFormRef.WriteFields(rom, HoldingAddr, values, _holdingFields);
        }

        // =================== Cross-tab navigation helpers ===================

        /// <summary>
        /// Map a 0-based row index in the Item list to the row's ROM
        /// address. Returns 0 if the index is past the live list count,
        /// the ROM is unavailable, or the row would fall outside ROM
        /// bounds.
        /// </summary>
        public uint GetItemRowAddress(uint rowIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return 0;
            uint addr = baseAddr + rowIndex * 5;
            if (addr + 5 > (uint)rom.Data.Length) return 0;
            // Bound by the live list count — beyond the 0xFF terminator the
            // entry is meaningless even if it fits in ROM bytes.
            uint count = (uint)LoadMonsterItemList().Count;
            if (rowIndex >= count) return 0;
            return addr;
        }

        /// <summary>
        /// Map a 0-based row index in the Probability list to the row's
        /// ROM address. Returns 0 if the index is past the live list count
        /// or the ROM is unavailable.
        /// </summary>
        public uint GetProbabilityRowAddress(uint rowIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.monster_item_probability_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return 0;
            uint addr = baseAddr + rowIndex * 5;
            if (addr + 5 > (uint)rom.Data.Length) return 0;
            uint count = (uint)LoadMonsterItemProbabilityList().Count;
            if (rowIndex >= count) return 0;
            return addr;
        }

        public int GetListCount() => LoadMonsterItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ItemId"] = $"0x{ItemId:X02}",
                ["DropRate"] = $"0x{DropRate:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            // Static u8@0xXX keys so AvaloniaFieldCompletenessTests
            // `AllViewModels_ReportMethodsAreConsistent` finds a matching
            // u8 entry for every ["BN"] write-key referenced anywhere in
            // this ViewModel (covers the full 32-byte holdings record).
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0x00):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 0x01):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 0x02):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 0x03):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 0x04):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 0x05):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 0x06):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 0x07):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 0x08):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 0x09):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 0x0A):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 0x0B):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 0x0C):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 0x0D):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 0x0E):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 0x0F):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 0x10):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 0x11):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 0x12):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 0x13):X02}",
                ["u8@0x14"] = $"0x{rom.u8(a + 0x14):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 0x15):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 0x16):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 0x17):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 0x18):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 0x19):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 0x1A):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 0x1B):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 0x1C):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 0x1D):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 0x1E):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 0x1F):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["ItemId"] = "u8@0x00",
            ["DropRate"] = "u8@0x01",
            ["Unknown1"] = "u8@0x02",
            ["Unknown2"] = "u8@0x03",
            ["Unknown3"] = "u8@0x04",
        };
    }
}
