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
        bool _hasLevelUpTable;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }
        public uint UnitSkill { get => _unitSkill; set => SetField(ref _unitSkill, value); }
        public uint XLevelUpAddr { get => _xLevelUpAddr; set => SetField(ref _xLevelUpAddr, value); }
        public bool IsZeroPointer { get => _isZeroPointer; set => SetField(ref _isZeroPointer, value); }

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

        void RecomputeVisibilityFlags(ROM rom)
        {
            if (rom == null) { IsZeroPointer = false; return; }
            // Mirror WF: hide panel for index-0 (sentinel slot).
            if (SelectedId == 0)
            {
                IsZeroPointer = false;
                return;
            }
            // CRITICAL: only compute when LEVELUP base is valid.
            if (_assignLevelUpBaseAddress == 0 || _assignLevelUpBaseAddress == U.NOT_FOUND)
            {
                IsZeroPointer = false;
                return;
            }
            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            uint gbaPtr = U.isSafetyOffset(pointerAddr + 3, rom) ? rom.u32(pointerAddr) : 0;
            IsZeroPointer = (gbaPtr == 0);
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
