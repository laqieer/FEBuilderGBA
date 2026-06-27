// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms SkillAssignmentUnitSkillSystemForm.
// Gap-sweep #995 raises the AXAML control surface from a single-placeholder
// stub to a functional master/detail editor.
//
// The WF form is a master/detail editor over the SkillSystems patch's
// per-unit personal-skill table:
//   - Master row (B0 u8) at assignUnitBase + unitId.
//   - Per-unit level-up pointer (u32) at assignLevelUpBase + 4*unitId,
//     which dereferences to a 0x0000-terminated u16 array (lv|skill, 2
//     bytes per entry).
//   - The level-up table is OPTIONAL (old patches may not have it).
//   - NO difficulty checkboxes (WF Unit form does not expose them).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentUnitSkillSystemViewModel : ViewModelBase, IDataVerifiable
    {
        public const uint LEVELUP_BLOCK_SIZE = 2;
        public const uint LEVELUP_MAX_ROWS = 20;
        public const uint LEVELUP_TERMINATOR = 0x0000;
        public const uint DEFAULT_LEVELUP_DEFAULT = 0x0101;
        public const uint MASTER_BLOCK_SIZE = 1;

        uint _assignUnitPointerLocation = U.NOT_FOUND;
        uint _assignLevelUpPointerLocation = U.NOT_FOUND;
        uint _iconBaseAddress;
        uint _textBaseAddress;
        uint _assignUnitBaseAddress;
        uint _assignLevelUpBaseAddress;

        public uint AssignUnitPointerLocation { get => _assignUnitPointerLocation; set => SetField(ref _assignUnitPointerLocation, value); }
        public uint AssignLevelUpPointerLocation { get => _assignLevelUpPointerLocation; set => SetField(ref _assignLevelUpPointerLocation, value); }
        public uint IconBaseAddress { get => _iconBaseAddress; set => SetField(ref _iconBaseAddress, value); }
        public uint TextBaseAddress { get => _textBaseAddress; set => SetField(ref _textBaseAddress, value); }
        public uint AssignUnitBaseAddress { get => _assignUnitBaseAddress; set => SetField(ref _assignUnitBaseAddress, value); }
        public uint AssignLevelUpBaseAddress { get => _assignLevelUpBaseAddress; set => SetField(ref _assignLevelUpBaseAddress, value); }

        uint _readStartAddress;
        uint _readCount;
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint MasterBlockSize => MASTER_BLOCK_SIZE;
        public uint LevelUpBlockSize => LEVELUP_BLOCK_SIZE;

        uint _currentAddr;
        bool _isLoaded;
        uint _selectedId;
        uint _unitSkill;
        uint _xLevelUpAddr;
        bool _isZeroPointer;
        bool _isIndependenceVisible;
        bool _hasLevelUpTable;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }
        public uint UnitSkill { get => _unitSkill; set => SetField(ref _unitSkill, value); }
        public uint XLevelUpAddr { get => _xLevelUpAddr; set => SetField(ref _xLevelUpAddr, value); }
        public bool IsZeroPointer { get => _isZeroPointer; set => SetField(ref _isZeroPointer, value); }

        /// <summary>
        /// True when the selected unit's level-up pointer is shared with at least
        /// one OTHER unit slot — i.e. "Make Independent" is meaningful. Drives the
        /// View's IndependencePanel visibility. Mirrors the sibling Class VM's
        /// <c>IsIndependenceVisible</c> and WF
        /// <c>SkillAssignmentUnitSkillSystemForm.IsShowIndependencePanel</c>.
        /// </summary>
        public bool IsIndependenceVisible { get => _isIndependenceVisible; set => SetField(ref _isIndependenceVisible, value); }

        /// <summary>
        /// True only when the per-unit level-up table (LEVELUP+4) resolves to a
        /// valid, safe ROM offset. Old SkillSystems patches lack the unit-based
        /// level-up table, so the View hides the entire N1 level-up group when
        /// this is false — mirroring WinForms
        /// <c>SkillAssignmentUnitSkillSystemForm</c> which calls
        /// <c>UnitLevelUpSkill.Hide()</c> when
        /// <c>FindAssignUnitLevelUpSkillPointer() == U.NOT_FOUND</c>.
        /// </summary>
        public bool HasLevelUpTable { get => _hasLevelUpTable; set => SetField(ref _hasLevelUpTable, value); }

        public sealed class LevelUpEntry
        {
            public uint Addr { get; init; }
            public string Name { get; init; }
            public uint Tag { get; init; }
            public override string ToString() => Name;
        }
        public ObservableCollection<LevelUpEntry> LevelUpEntries { get; } = new();
        uint _selectedLevelUpAddr;
        uint _selectedLevelUpId;
        uint _levelUpRaw;
        uint _levelUpSkill;

        public uint SelectedLevelUpAddr { get => _selectedLevelUpAddr; set => SetField(ref _selectedLevelUpAddr, value); }
        public uint SelectedLevelUpId { get => _selectedLevelUpId; set => SetField(ref _selectedLevelUpId, value); }
        public uint LevelUpRaw { get => _levelUpRaw; set => SetField(ref _levelUpRaw, value); }
        public uint LevelUpSkill { get => _levelUpSkill; set => SetField(ref _levelUpSkill, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            // Personal-skill pointer: ASSIGN+0
            uint assignUnitLoc = SkillSystemPatchScanner.FindAssignPersonalSkillPointerLocation(rom);
            // Level-up pointer: LEVELUP+4 (optional — old patches may not have it)
            uint assignLevelUpLoc = SkillSystemPatchScanner.FindAssignUnitLevelUpSkillPointerLocation(rom);
            uint iconBase = PreviewIconHelper.FindSkillSystemIconBaseAddress();
            uint textLoc = PreviewIconHelper.FindSkillSystemTextPointerLocation();

            AssignUnitPointerLocation = assignUnitLoc;
            AssignLevelUpPointerLocation = assignLevelUpLoc;
            IconBaseAddress = iconBase;
            TextBaseAddress = textLoc != U.NOT_FOUND ? rom.p32(textLoc) : 0;

            // ASSIGN is mandatory; icon/text are used for display but not mandatory.
            if (assignUnitLoc == U.NOT_FOUND)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint assignUnitBase = rom.p32(assignUnitLoc);
            if (!U.isSafetyOffset(assignUnitBase, rom))
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            AssignUnitBaseAddress = assignUnitBase;
            ReadStartAddress = assignUnitBase;

            // Level-up is optional: tolerate NOT_FOUND
            if (assignLevelUpLoc != U.NOT_FOUND)
            {
                uint assignLevelUpBase = rom.p32(assignLevelUpLoc);
                AssignLevelUpBaseAddress = U.isSafetyOffset(assignLevelUpBase, rom) ? assignLevelUpBase : 0;
            }
            else
            {
                AssignLevelUpBaseAddress = 0;
            }
            // HasLevelUpTable drives the View's N1-group visibility (mirror WF
            // UnitLevelUpSkill.Hide() when the unit-based level-up table is absent).
            HasLevelUpTable = AssignLevelUpBaseAddress != 0 && AssignLevelUpBaseAddress != U.NOT_FOUND;

            uint unitCount = rom.RomInfo.unit_maxcount;

            var result = new List<AddrResult>();
            for (uint i = 0; i < unitCount; i++)
            {
                uint addr = assignUnitBase + i * MASTER_BLOCK_SIZE;
                if (!U.isSafetyOffset(addr, rom)) break;
                // The row index i IS the 1-based WF uid (0 = empty sentinel,
                // 1 = Eirika on FE8). Use the 1-based resolver so the displayed
                // name matches WF UnitForm.GetUnitName((uint)i). Omit the trailing
                // space when the name is empty (the 0x00 sentinel row reads "0x00").
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
            if (!U.isSafetyOffset(addr, rom)) return;

            CurrentAddr = addr;

            if (_assignUnitPointerLocation == U.NOT_FOUND)
                AssignUnitPointerLocation = SkillSystemPatchScanner.FindAssignPersonalSkillPointerLocation(rom);
            if (_assignLevelUpPointerLocation == U.NOT_FOUND)
                AssignLevelUpPointerLocation = SkillSystemPatchScanner.FindAssignUnitLevelUpSkillPointerLocation(rom);
            if (_iconBaseAddress == 0)
                IconBaseAddress = PreviewIconHelper.FindSkillSystemIconBaseAddress();

            if (_assignUnitBaseAddress == 0 && _assignUnitPointerLocation != U.NOT_FOUND)
            {
                uint baseAddr = rom.p32(_assignUnitPointerLocation);
                if (U.isSafetyOffset(baseAddr, rom)) AssignUnitBaseAddress = baseAddr;
            }
            if (_assignLevelUpBaseAddress == 0 && _assignLevelUpPointerLocation != U.NOT_FOUND
                && _assignLevelUpPointerLocation != 0)
            {
                uint baseAddr = rom.p32(_assignLevelUpPointerLocation);
                if (U.isSafetyOffset(baseAddr, rom)) AssignLevelUpBaseAddress = baseAddr;
            }
            // Recompute the N1-group visibility flag (LoadEntry may run before
            // LoadList in a Jump-to path).
            HasLevelUpTable = _assignLevelUpBaseAddress != 0 && _assignLevelUpBaseAddress != U.NOT_FOUND;

            SelectedId = (_assignUnitBaseAddress > 0 && addr >= _assignUnitBaseAddress)
                ? (addr - _assignUnitBaseAddress) / MASTER_BLOCK_SIZE
                : 0;

            UnitSkill = rom.u8(addr);

            // Level-up pointer is OPTIONAL — guard against NOT_FOUND + bad address.
            // CRITICAL: never compute NOT_FOUND + selectedId*4.
            if (_assignLevelUpBaseAddress != 0 && _assignLevelUpBaseAddress != U.NOT_FOUND)
            {
                uint levelUpPointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
                uint xLevelUpPointer = U.isSafetyOffset(levelUpPointerAddr + 3, rom)
                    ? rom.u32(levelUpPointerAddr) : 0;
                XLevelUpAddr = xLevelUpPointer;

                RecomputeVisibilityFlags(rom);
                LoadLevelUpList(rom);
            }
            else
            {
                XLevelUpAddr = 0;
                IsZeroPointer = false;
                IsIndependenceVisible = false;
                LevelUpEntries.Clear();
                SelectedLevelUpAddr = 0;
                SelectedLevelUpId = 0;
                LevelUpRaw = 0;
                LevelUpSkill = 0;
            }

            IsLoaded = true;
        }

        public void LoadLevelUpList(ROM rom)
        {
            LevelUpEntries.Clear();
            if (rom == null) return;
            // CRITICAL: skip all N1 work when LEVELUP is unavailable.
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND) return;

            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (!U.isSafetyOffset(pointerAddr + 3, rom)) return;

            uint gbaPtr = rom.u32(pointerAddr);
            if (!U.isSafetyPointer(gbaPtr)) return;
            uint baseAddr = U.toOffset(gbaPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            uint cursor = baseAddr;
            for (uint n = 0; n < LEVELUP_MAX_ROWS; n++, cursor += LEVELUP_BLOCK_SIZE)
            {
                if (!U.isSafetyOffset(cursor + 1, rom)) break;
                uint pair = rom.u16(cursor);
                if (pair == 0 || pair == 0xFFFF) break;
                LevelUpEntries.Add(new LevelUpEntry { Addr = cursor, Name = "0x" + n.ToString("X02"), Tag = n });
            }
        }

        public void LoadLevelUpEntry(uint entryAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(entryAddr + 1, rom)) return;

            SelectedLevelUpAddr = entryAddr;

            // CRITICAL: skip if LEVELUP not available.
            if (_assignLevelUpBaseAddress != 0 && _assignLevelUpBaseAddress != U.NOT_FOUND)
            {
                uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
                if (U.isSafetyOffset(pointerAddr + 3, rom))
                {
                    uint baseGba = rom.u32(pointerAddr);
                    if (U.isSafetyPointer(baseGba))
                    {
                        uint baseAddr = U.toOffset(baseGba);
                        SelectedLevelUpId = entryAddr >= baseAddr
                            ? (entryAddr - baseAddr) / LEVELUP_BLOCK_SIZE
                            : 0;
                    }
                }
            }

            LevelUpRaw = rom.u8(entryAddr + 0);
            LevelUpSkill = rom.u8(entryAddr + 1);
        }

        public void WriteMaster()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            if (U.isSafetyOffset(CurrentAddr, rom))
            {
                rom.write_u8(CurrentAddr, UnitSkill);
            }

            // Only write level-up pointer when LEVELUP is available and slot is valid.
            if (_assignLevelUpBaseAddress != 0 && _assignLevelUpBaseAddress != U.NOT_FOUND)
            {
                uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
                if (U.isSafetyOffset(pointerAddr + 3, rom))
                {
                    uint asOffset = U.toOffset(XLevelUpAddr);
                    rom.write_p32(pointerAddr, asOffset);
                }
            }
        }

        public void WriteLevelUp()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SelectedLevelUpAddr == 0) return;
            if (!U.isSafetyOffset(SelectedLevelUpAddr + 1, rom)) return;
            rom.write_u8(SelectedLevelUpAddr + 0, LevelUpRaw);
            rom.write_u8(SelectedLevelUpAddr + 1, LevelUpSkill);
        }

        public sealed class LevelUpExpandResult
        {
            public bool Success { get; init; }
            public string Error { get; init; }
            public uint NewBaseAddress { get; init; }
            public uint NewDataCount { get; init; }
        }

        /// <summary>
        /// Allocate (when the per-unit pointer is 0) or grow the selected unit's
        /// 0x0000-terminated level-up list by one entry, then repoint ONLY this
        /// unit's <c>LEVELUP+4</c> slot. Mirrors WF
        /// <c>SkillAssignmentUnitSkillSystemForm.N1_InputFormRef_AddressListExpandsEvent</c>.
        ///
        /// CRITICAL (sharing-safe, single-slot): the grow path COPIES the existing
        /// rows + a new sentinel row + a 0x0000 terminator into FRESH free space,
        /// repoints ONLY this unit's pointer slot, and DELIBERATELY LEAVES THE OLD
        /// TABLE BYTES INTACT — so any other unit that shares the same level-up
        /// table keeps its data (it is NOT a move-and-wipe). This is why the grow
        /// path does NOT use <see cref="DataExpansionCore.ExpandTableTo"/> /
        /// <see cref="DataExpansionCore.ExpandTable"/>, both of which zero/0xFF-wipe
        /// the old region and would corrupt a sharing sibling
        /// (Copilot PR-review finding #1). The new last row is seeded 0x01/0x01
        /// (a 0x0000 pair would read as a terminator and hide the grown row),
        /// matching WF. All writes route through the ambient undo scope opened by
        /// the View's <c>UndoService.Begin</c>.
        /// </summary>
        public LevelUpExpandResult ExpandLevelUpList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return new LevelUpExpandResult { Success = false, Error = "ROM not loaded." };
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND)
                return new LevelUpExpandResult { Success = false, Error = "Skill assignment level-up table not resolved." };

            // WF treats the index-0 row (the 0x00 sentinel) as non-editable; the
            // VM also hides its panels in RecomputeVisibilityFlags. Refuse to
            // allocate/repoint the sentinel slot (Copilot bot review).
            if (SelectedId == 0)
                return new LevelUpExpandResult { Success = false, Error = "The sentinel unit (0x00) has no editable level-up table." };

            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (!U.isSafetyOffset(pointerAddr + 3, rom))
                return new LevelUpExpandResult { Success = false, Error = "Per-unit pointer slot out of bounds." };

            uint gbaPtr = rom.u32(pointerAddr);

            if (gbaPtr == 0 || !U.isSafetyPointer(gbaPtr))
            {
                // First-allocation: one entry + a 0x0000 terminator.
                const uint INITIAL_ENTRIES = 1;
                uint allocSize = (INITIAL_ENTRIES + 1) * LEVELUP_BLOCK_SIZE;
                uint newBase = DataExpansionCore.FindFreeSpace(rom, allocSize);
                if (newBase == U.NOT_FOUND)
                    return new LevelUpExpandResult { Success = false, Error = "No free space for new level-up table." };
                rom.write_u8(newBase + 0, 0x01);
                rom.write_u8(newBase + 1, 0x01);
                rom.write_u16(newBase + LEVELUP_BLOCK_SIZE, (ushort)LEVELUP_TERMINATOR);
                rom.write_p32(pointerAddr, newBase);
                XLevelUpAddr = U.toPointer(newBase);
                RecomputeVisibilityFlags(rom);
                LoadLevelUpList(rom);
                return new LevelUpExpandResult { Success = true, NewBaseAddress = newBase, NewDataCount = INITIAL_ENTRIES };
            }

            uint oldBase = U.toOffset(gbaPtr);
            uint oldCount = CountLevelUpEntries(rom, oldBase);
            // The N1 sub-list only ever shows LEVELUP_MAX_ROWS entries, so an
            // expand past that cap could never become visible/editable. Block it
            // (Copilot bot review).
            if (oldCount >= LEVELUP_MAX_ROWS)
                return new LevelUpExpandResult { Success = false, Error = $"The level-up list already holds the maximum {LEVELUP_MAX_ROWS} entries." };
            uint newCount = oldCount + 1;

            // COPY (never move-and-wipe): allocate (newCount + 1) rows so the
            // existing rows, one fresh sentinel row, AND the 0x0000 terminator
            // all fit. The old table is left untouched so sharing units keep it.
            uint allocBytes = (newCount + 1) * LEVELUP_BLOCK_SIZE;
            uint oldBytes = oldCount * LEVELUP_BLOCK_SIZE;
            if (oldBase + oldBytes > (uint)rom.Data.Length)
                return new LevelUpExpandResult { Success = false, Error = "Existing level-up table extends beyond ROM bounds." };

            uint newBaseAddr = DataExpansionCore.FindFreeSpace(rom, allocBytes);
            if (newBaseAddr == U.NOT_FOUND)
                return new LevelUpExpandResult { Success = false, Error = "No free space for the expanded level-up table." };

            // Copy the existing rows verbatim (ambient undo captures the pre-copy
            // bytes at newBaseAddr).
            if (oldBytes > 0)
            {
                byte[] existing = rom.getBinaryData(oldBase, oldBytes);
                rom.write_range(newBaseAddr, existing);
            }
            // New (last) data row: 0x01/0x01 so it is not mistaken for a terminator.
            uint newEntryAddr = newBaseAddr + oldBytes;
            rom.write_u8(newEntryAddr + 0, 0x01);
            rom.write_u8(newEntryAddr + 1, 0x01);
            // 0x0000 terminator right after the new row.
            rom.write_u16(newBaseAddr + newCount * LEVELUP_BLOCK_SIZE, (ushort)LEVELUP_TERMINATOR);
            // Repoint ONLY this unit's slot; the old table is left intact.
            rom.write_p32(pointerAddr, newBaseAddr);

            XLevelUpAddr = U.toPointer(newBaseAddr);
            RecomputeVisibilityFlags(rom);
            LoadLevelUpList(rom);
            return new LevelUpExpandResult { Success = true, NewBaseAddress = newBaseAddr, NewDataCount = newCount };
        }

        static uint CountLevelUpEntries(ROM rom, uint baseAddr)
        {
            uint count = 0;
            uint cursor = baseAddr;
            for (uint n = 0; n < LEVELUP_MAX_ROWS; n++, cursor += LEVELUP_BLOCK_SIZE)
            {
                if (!U.isSafetyOffset(cursor + 1, rom)) break;
                uint pair = rom.u16(cursor);
                if (pair == 0 || pair == 0xFFFF) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// True when the selected unit's level-up table currently holds 0 rows,
        /// so the View can mirror WF's "the list is empty, separate it anyway?"
        /// confirm before <see cref="MakeIndependent"/>. Mirrors WF
        /// <c>IndependenceButton_Click</c>'s <c>N1_InputFormRef.DataCount == 0</c>
        /// check.
        /// </summary>
        public bool IsSelectedLevelUpListEmpty()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return true;
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND) return true;
            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (!U.isSafetyOffset(pointerAddr + 3, rom)) return true;
            uint gbaPtr = rom.u32(pointerAddr);
            if (!U.isSafetyPointer(gbaPtr)) return true;
            return SkillAssignmentIndependenceCore.CountLevelUpRows(rom, U.toOffset(gbaPtr)) == 0;
        }

        /// <summary>
        /// Clone the selected unit's SHARED level-up table into a fresh
        /// free-space block and repoint ONLY this unit's pointer slot — mirrors WF
        /// <c>SkillAssignmentUnitSkillSystemForm.IndependenceButton_Click</c> →
        /// <c>PatchUtil.WriteIndependence</c>. Delegates to the generic
        /// <see cref="SkillAssignmentIndependenceCore.MakeIndependent"/> (shared
        /// with the Class VM). CRITICALLY a SINGLE-slot repoint, NOT
        /// <c>RepointAllReferences</c>: every other sharing unit deliberately
        /// stays on the intact original table.
        /// </summary>
        /// <returns>The new block's GBA pointer (0x08xxxxxx) on success, or 0 on
        /// no-op / failure.</returns>
        public uint MakeIndependent(Undo.UndoData undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return 0;
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND) return 0;

            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (!U.isSafetyOffset(pointerAddr + 3, rom)) return 0;

            uint gbaPtr = rom.u32(pointerAddr);
            if (!U.isSafetyPointer(gbaPtr)) return 0;
            if (!U.isSafetyOffset(U.toOffset(gbaPtr), rom)) return 0;

            uint newOffset = SkillAssignmentIndependenceCore.MakeIndependent(
                rom, _assignLevelUpBaseAddress, SelectedId, undoData);
            if (newOffset == U.NOT_FOUND || newOffset == 0) return 0;

            XLevelUpAddr = U.toPointer(newOffset);
            RecomputeVisibilityFlags(rom);
            LoadLevelUpList(rom);
            return U.toPointer(newOffset);
        }

        /// <summary>
        /// Bulk-export the per-unit skill assignment table to a
        /// <c>*.SkillAssignmentUnit.tsv</c>. Reuses the generic
        /// <see cref="SkillAssignmentClassSkillSystemCore.ExportAllData"/> — the
        /// per-unit TSV format is byte-identical to the per-class one (master B0
        /// u8 + level-up table offset + level/skill u8 pairs), confirmed against
        /// WF <c>SkillAssignmentUnitSkillSystemForm.ExportAllData</c>.
        /// </summary>
        public bool ExportAllData(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (_assignUnitPointerLocation == U.NOT_FOUND
                || _assignLevelUpPointerLocation == U.NOT_FOUND) return false;
            // Bounds-guard the FULL 4-byte pointer extents before p32 (which only
            // checks addr >= Length, so a slot in the last 3 bytes would read OOB)
            // and validate the resolved base (Copilot bot review).
            if (!U.isSafetyOffset(_assignUnitPointerLocation + 3, rom)) return false;
            if (!U.isSafetyOffset(_assignLevelUpPointerLocation + 3, rom)) return false;
            uint unitBase = rom.p32(_assignUnitPointerLocation);
            if (!U.isSafetyOffset(unitBase, rom)) return false;
            uint unitCount = ResolveUnitCount(rom);
            return SkillAssignmentClassSkillSystemCore.ExportAllData(
                rom, unitBase, _assignLevelUpPointerLocation, unitCount, filename);
        }

        /// <summary>
        /// Bulk-import the per-unit skill assignment table from a TSV. See
        /// <see cref="ExportAllData"/> for the shared format note.
        /// </summary>
        public bool ImportAllData(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (_assignUnitPointerLocation == U.NOT_FOUND
                || _assignLevelUpPointerLocation == U.NOT_FOUND) return false;
            // Same full-extent bounds + base validation as ExportAllData
            // (Copilot bot review).
            if (!U.isSafetyOffset(_assignUnitPointerLocation + 3, rom)) return false;
            if (!U.isSafetyOffset(_assignLevelUpPointerLocation + 3, rom)) return false;
            uint unitBase = rom.p32(_assignUnitPointerLocation);
            if (!U.isSafetyOffset(unitBase, rom)) return false;
            uint unitCount = ResolveUnitCount(rom);
            return SkillAssignmentClassSkillSystemCore.ImportAllData(
                rom, unitBase, _assignLevelUpPointerLocation, unitCount, filename);
        }

        static uint ResolveUnitCount(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return 0;
            return rom.RomInfo.unit_maxcount;
        }

        void RecomputeVisibilityFlags(ROM rom)
        {
            if (rom == null) { IsZeroPointer = false; IsIndependenceVisible = false; return; }
            // Mirror WF: hide both panels for index-0 (sentinel slot).
            if (SelectedId == 0)
            {
                IsZeroPointer = false;
                IsIndependenceVisible = false;
                return;
            }
            // CRITICAL: only compute when LEVELUP base is valid.
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND)
            {
                IsZeroPointer = false;
                IsIndependenceVisible = false;
                return;
            }
            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            uint gbaPtr = U.isSafetyOffset(pointerAddr + 3, rom) ? rom.u32(pointerAddr) : 0;
            if (gbaPtr == 0)
            {
                IsZeroPointer = true;
                IsIndependenceVisible = false;
                return;
            }
            IsZeroPointer = false;
            IsIndependenceVisible = IsTableShared(rom, gbaPtr);
        }

        // True when another unit slot points at the SAME level-up table. Mirrors
        // the Class VM's IsTableShared / WF IsExistsAssignLevelUpPointer.
        bool IsTableShared(ROM rom, uint currentGbaPtr)
        {
            if (!U.isSafetyPointer(currentGbaPtr)) return false;
            // ReadCount may still be 0 in a LoadEntry-before-LoadList (Jump-to)
            // path; fall back to the ROM's unit_maxcount so sharing detection
            // doesn't incorrectly return false and hide the Independence panel
            // (Copilot bot review).
            uint unitCount = ReadCount;
            if (unitCount == 0 && rom.RomInfo != null) unitCount = rom.RomInfo.unit_maxcount;
            for (uint i = 0; i < unitCount; i++)
            {
                if (i == SelectedId) continue;
                uint slot = _assignLevelUpBaseAddress + i * 4;
                if (!U.isSafetyOffset(slot + 3, rom)) continue;
                uint other = rom.u32(slot);
                if (other == currentGbaPtr) return true;
            }
            return false;
        }

        void ResetDerivedListState()
        {
            ReadStartAddress = 0;
            ReadCount = 0;
            CurrentAddr = 0;
            SelectedId = 0;
            IsLoaded = false;
            UnitSkill = 0;
            XLevelUpAddr = 0;
            IsZeroPointer = false;
            IsIndependenceVisible = false;
            HasLevelUpTable = false;
            LevelUpEntries.Clear();
            SelectedLevelUpAddr = 0;
            SelectedLevelUpId = 0;
            LevelUpRaw = 0;
            LevelUpSkill = 0;
            AssignUnitBaseAddress = 0;
            AssignLevelUpBaseAddress = 0;
        }

        /// <summary>
        /// Resolve a skill description by reading the u16 text id at
        /// <c>TextBaseAddress + 2 * skillId</c> and looking it up via
        /// <see cref="NameResolver.GetTextById"/>. Mirrors WinForms
        /// <c>SkillConfigSkillSystemForm.GetSkillText(skillId, textBase)</c>.
        /// Returns empty string if the SkillSystems patch isn't installed,
        /// the lookup fails, or the resolved string is the "???" sentinel.
        /// </summary>
        public string ResolveSkillTextById(uint skillId)
        {
            if (skillId == 0 || skillId == 0xFF) return string.Empty;
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return string.Empty;
            if (TextBaseAddress == 0) return string.Empty;
            uint addr = TextBaseAddress + 2 * skillId;
            if (!U.isSafetyOffset(addr + 1, rom)) return string.Empty;
            uint textId = rom.u16(addr);
            if (textId == 0 || textId == 0xFFFF) return string.Empty;
            try
            {
                string text = NameResolver.GetTextById(textId);
                if (string.IsNullOrEmpty(text) || text == "???") return string.Empty;
                return text;
            }
            catch { return string.Empty; }
        }

        public int GetListCount()
        {
            if (ReadCount > 0) return (int)ReadCount;
            return LoadList().Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SelectedId"] = $"0x{SelectedId:X02}",
                ["UnitSkill"] = $"0x{UnitSkill:X02}",
                ["XLevelUpAddr"] = $"0x{XLevelUpAddr:X08}",
                ["LevelUpRaw"] = $"0x{LevelUpRaw:X02}",
                ["LevelUpSkill"] = $"0x{LevelUpSkill:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var dict = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_UnitSkill"] = $"0x{rom.u8(a + 0):X02}",
            };
            if (SelectedLevelUpAddr != 0 && U.isSafetyOffset(SelectedLevelUpAddr + 1, rom))
            {
                dict["u8@0x00_LevelUpRaw"] = $"0x{rom.u8(SelectedLevelUpAddr + 0):X02}";
                dict["u8@0x01_LevelUpSkill"] = $"0x{rom.u8(SelectedLevelUpAddr + 1):X02}";
            }
            return dict;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitSkill"] = "u8@0x00",
        };
    }
}
