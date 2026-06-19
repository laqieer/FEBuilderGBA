// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms FE8SpellMenuExtendsForm. Issue #1167 raises
// the AXAML control surface from a single-placeholder stub to a functional
// master/detail editor over the FE8 SkillSystems spell-menu (Gaiden-style
// spell list) patch.
//
// Master row (unit) at unitTableBase + 4*unitId is a u32 GBA pointer to that
// unit's spell-list block; the block is a 0x0000-terminated u16 array of
// [B0|B1] entries (B0 = level|promoted, B1 = item/spell id).
//
// This VM READS ROM bytes, so it implements IDataVerifiable (NOT a tool
// orphan). All scan/read logic lives in Core (FE8SpellMenuPatchScanner +
// FE8SpellMenuExtendsCore); this VM is the thin CoreState.ROM-bound adapter.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class FE8SpellMenuExtendsViewModel : ViewModelBase, IDataVerifiable
    {
        public const uint MASTER_BLOCK_SIZE = FE8SpellMenuExtendsCore.MASTER_BLOCK_SIZE; // 4
        public const uint N1_BLOCK_SIZE = FE8SpellMenuExtendsCore.N1_BLOCK_SIZE;          // 2
        public const uint UNIT_MAX_ROWS = FE8SpellMenuExtendsCore.UNIT_MAX_ROWS;          // 0xFF

        uint _assignLevelUpP = U.NOT_FOUND;
        uint _unitTableBase;

        public uint AssignLevelUpP { get => _assignLevelUpP; set => SetField(ref _assignLevelUpP, value); }
        public uint UnitTableBase { get => _unitTableBase; set => SetField(ref _unitTableBase, value); }

        uint _readStartAddress;
        uint _readCount;
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint MasterBlockSize => MASTER_BLOCK_SIZE;
        public uint N1BlockSize => N1_BLOCK_SIZE;

        uint _currentAddr;        // selected unit's pointer-slot address
        bool _isLoaded;
        uint _selectedUnitId;
        uint _unitListPointer;    // the GBA pointer value in the slot
        bool _isZeroPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SelectedUnitId { get => _selectedUnitId; set => SetField(ref _selectedUnitId, value); }
        public uint UnitListPointer { get => _unitListPointer; set => SetField(ref _unitListPointer, value); }

        /// <summary>True when the selected unit's pointer slot is 0 (no spell
        /// list allocated). Drives the View's "no list" hint panel.</summary>
        public bool IsZeroPointer { get => _isZeroPointer; set => SetField(ref _isZeroPointer, value); }

        public sealed class SpellEntry
        {
            public uint Addr { get; init; }
            public string Name { get; init; }
            public uint Tag { get; init; }
            public override string ToString() => Name;
        }
        public ObservableCollection<SpellEntry> SpellEntries { get; } = new();

        uint _selectedN1Addr;
        uint _selectedN1Id;
        uint _n1Level;
        bool _n1Promoted;
        uint _n1SpellId;

        public uint SelectedN1Addr { get => _selectedN1Addr; set => SetField(ref _selectedN1Addr, value); }
        public uint SelectedN1Id { get => _selectedN1Id; set => SetField(ref _selectedN1Id, value); }
        public uint N1Level { get => _n1Level; set => SetField(ref _n1Level, value); }
        public bool N1Promoted { get => _n1Promoted; set => SetField(ref _n1Promoted, value); }
        public uint N1SpellId { get => _n1SpellId; set => SetField(ref _n1SpellId, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint assignLevelUpP = FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, CoreState.BaseDirectory);
            AssignLevelUpP = assignLevelUpP;
            if (assignLevelUpP == U.NOT_FOUND)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint unitTableBase = FE8SpellMenuExtendsCore.GetUnitTableBase(rom, assignLevelUpP);
            if (unitTableBase == U.NOT_FOUND)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }
            UnitTableBase = unitTableBase;
            ReadStartAddress = unitTableBase;

            var result = new List<AddrResult>();
            for (uint i = 0; i < UNIT_MAX_ROWS; i++)
            {
                uint addr = FE8SpellMenuExtendsCore.GetUnitSlotAddr(unitTableBase, i);
                if (!U.isSafetyOffset(addr + 3, rom)) break;
                // The row index i IS the WF uid (UnitForm.GetUnitName((uint)i)).
                string unitName = NameResolver.GetUnitNameByOneBasedId(i);
                string label = string.IsNullOrEmpty(unitName)
                    ? "0x" + i.ToString("X02")
                    : "0x" + i.ToString("X02") + " " + unitName;
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr + 3, rom)) return;

            CurrentAddr = addr;

            if (_assignLevelUpP == U.NOT_FOUND)
                AssignLevelUpP = FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, CoreState.BaseDirectory);
            if (_unitTableBase == 0 && _assignLevelUpP != U.NOT_FOUND)
            {
                uint baseAddr = FE8SpellMenuExtendsCore.GetUnitTableBase(rom, _assignLevelUpP);
                if (baseAddr != U.NOT_FOUND) UnitTableBase = baseAddr;
            }

            SelectedUnitId = (_unitTableBase > 0 && addr >= _unitTableBase)
                ? (addr - _unitTableBase) / MASTER_BLOCK_SIZE
                : 0;

            UnitListPointer = rom.u32(addr);
            IsZeroPointer = !U.isSafetyPointer(UnitListPointer);

            LoadSpellList(rom);

            IsLoaded = true;
        }

        public void LoadSpellList(ROM rom)
        {
            SpellEntries.Clear();
            SelectedN1Addr = 0;
            SelectedN1Id = 0;
            N1Level = 0;
            N1Promoted = false;
            N1SpellId = 0;
            if (rom == null) return;
            if (!U.isSafetyPointer(UnitListPointer)) return;

            uint listBase = U.toOffset(UnitListPointer);
            var entries = FE8SpellMenuExtendsCore.ReadSpellList(rom, listBase);
            uint n = 0;
            foreach (var (entryAddr, b0, b1) in entries)
            {
                string itemName = NameResolver.GetItemName(b1);
                string label = string.IsNullOrEmpty(itemName)
                    ? "0x" + b1.ToString("X02")
                    : "0x" + b1.ToString("X02") + " " + itemName;
                SpellEntries.Add(new SpellEntry { Addr = entryAddr, Name = label, Tag = n });
                n++;
            }
        }

        public void LoadN1Entry(uint entryAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(entryAddr + 1, rom)) return;

            SelectedN1Addr = entryAddr;
            if (U.isSafetyPointer(UnitListPointer))
            {
                uint listBase = U.toOffset(UnitListPointer);
                SelectedN1Id = entryAddr >= listBase
                    ? (entryAddr - listBase) / N1_BLOCK_SIZE
                    : 0;
            }

            uint b0 = rom.u8(entryAddr + 0);
            FE8SpellMenuExtendsCore.SplitB0(b0, out uint level, out bool promoted);
            N1Level = level;
            N1Promoted = promoted;
            N1SpellId = rom.u8(entryAddr + 1);
        }

        /// <summary>Compose the current B0 from N1Level + N1Promoted. Mirrors
        /// WF FE8SpellMenuExtendsForm.MakeB0.</summary>
        public uint MakeB0() => FE8SpellMenuExtendsCore.MakeB0(N1Level, N1Promoted);

        /// <summary>Write the per-unit pointer slot (master Write). Mirrors WF
        /// WriteButton_Click.</summary>
        public void WriteMaster()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (!U.isSafetyOffset(CurrentAddr + 3, rom)) return;
            rom.write_p32(CurrentAddr, U.toOffset(UnitListPointer));
        }

        /// <summary>Write the selected N1 entry's B0/B1 in place. Mirrors WF
        /// N1 list write.</summary>
        public void WriteN1()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SelectedN1Addr == 0) return;
            uint b0 = MakeB0();
            FE8SpellMenuExtendsCore.WriteN1Entry(rom, SelectedN1Addr, b0, N1SpellId);
        }

        /// <summary>
        /// Expand the selected unit's spell list to <paramref name="newCount"/>
        /// entries, allocating a fresh 0x0000-terminated block and repointing the
        /// unit slot. Mirrors WF N1_InputFormRef_AddressListExpandsEvent. The
        /// caller MUST open an ambient UndoService scope first.
        /// </summary>
        /// <returns>True on success; false on failure (no expand performed).</returns>
        public bool ExpandN1List(uint newCount)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _unitTableBase == 0) return false;
            uint newAddr = FE8SpellMenuExtendsCore.ExpandSpellList(rom, _unitTableBase, SelectedUnitId, newCount);
            if (newAddr == U.NOT_FOUND) return false;
            // The unit slot was already repointed by ExpandSpellList; refresh the
            // in-memory pointer + sub-list to match.
            UnitListPointer = rom.u32(CurrentAddr);
            IsZeroPointer = !U.isSafetyPointer(UnitListPointer);
            LoadSpellList(rom);
            return true;
        }

        void ResetDerivedListState()
        {
            ReadStartAddress = 0;
            ReadCount = 0;
            CurrentAddr = 0;
            SelectedUnitId = 0;
            IsLoaded = false;
            UnitTableBase = 0;
            UnitListPointer = 0;
            IsZeroPointer = false;
            SpellEntries.Clear();
            SelectedN1Addr = 0;
            SelectedN1Id = 0;
            N1Level = 0;
            N1Promoted = false;
            N1SpellId = 0;
        }

        public int GetListCount()
        {
            if (ReadCount > 0) return (int)ReadCount;
            return LoadList().Count;
        }

        // ----------------------------------------------------------------
        // IDataVerifiable (this VM reads ROM bytes — it is NOT a tool orphan).
        // ----------------------------------------------------------------

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SelectedUnitId"] = $"0x{SelectedUnitId:X02}",
                ["UnitListPointer"] = $"0x{UnitListPointer:X08}",
                ["N1Level"] = $"0x{N1Level:X02}",
                ["N1Promoted"] = N1Promoted ? "1" : "0",
                ["N1SpellId"] = $"0x{N1SpellId:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            var dict = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["u32@0x00_UnitListPointer"] = $"0x{rom.u32(CurrentAddr):X08}",
            };
            if (SelectedN1Addr != 0 && U.isSafetyOffset(SelectedN1Addr + 1, rom))
            {
                dict["u8@0x00_B0"] = $"0x{rom.u8(SelectedN1Addr + 0):X02}";
                dict["u8@0x01_B1"] = $"0x{rom.u8(SelectedN1Addr + 1):X02}";
            }
            return dict;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitListPointer"] = "u32@0x00",
        };
    }
}
