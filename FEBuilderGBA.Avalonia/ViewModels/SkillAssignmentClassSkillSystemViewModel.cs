// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms SkillAssignmentClassSkillSystemForm.
// Gap-sweep #416 raises the AXAML control surface from 7 to MEDIUM-verdict
// density (target >= 33 controls) and wires master + N1 ROM writes under
// View-owned UndoService scopes.
//
// The WF form is a master/detail editor over the SkillSystems patch's
// per-class skill table:
//   - Master row (B0 u8) at assignClassBase + classId.
//   - Per-class level-up pointer (u32) at assignLevelUpBase + 4*classId,
//     which dereferences to a 0x0000-terminated u16 array (lv|skill, 2
//     bytes per entry).
//   - X_LevelAddPanel difficulty checkboxes are PATCH-CONDITIONAL on
//     SkillSystemPatchScanner.IsClassSkillExtends (gates LV+32/+64/+96/+128
//     difficulty bits on the level field).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentClassSkillSystemViewModel : ViewModelBase, IDataVerifiable
    {
        public const uint LEVELUP_BLOCK_SIZE = 2;
        public const uint LEVELUP_MAX_ROWS = 20;
        public const uint DEFAULT_LEVELUP_DEFAULT = 0x0101;
        public const uint LEVELUP_TERMINATOR = 0x0000;
        public const uint MAX_CLASS_COUNT = 255;
        public const uint MASTER_BLOCK_SIZE = 1;
        const uint LEVELUP_BIT_PLAYER = 32;
        const uint LEVELUP_BIT_ENEMY = 64;
        const uint LEVELUP_BIT_NORMALHARD = 96;
        const uint LEVELUP_BIT_HARDONLY = 128;
        const uint LEVELUP_BIT_LEVEL_MASK = 0x1F;

        uint _assignClassPointerLocation = U.NOT_FOUND;
        uint _assignLevelUpPointerLocation = U.NOT_FOUND;
        uint _iconBaseAddress;
        uint _textBaseAddress;
        uint _assignClassBaseAddress;
        uint _assignLevelUpBaseAddress;
        bool _isClassSkillExtendsActive;

        public uint AssignClassPointerLocation { get => _assignClassPointerLocation; set => SetField(ref _assignClassPointerLocation, value); }
        public uint AssignLevelUpPointerLocation { get => _assignLevelUpPointerLocation; set => SetField(ref _assignLevelUpPointerLocation, value); }
        public uint IconBaseAddress { get => _iconBaseAddress; set => SetField(ref _iconBaseAddress, value); }
        public uint TextBaseAddress { get => _textBaseAddress; set => SetField(ref _textBaseAddress, value); }
        public uint AssignClassBaseAddress { get => _assignClassBaseAddress; set => SetField(ref _assignClassBaseAddress, value); }
        public uint AssignLevelUpBaseAddress { get => _assignLevelUpBaseAddress; set => SetField(ref _assignLevelUpBaseAddress, value); }
        public bool IsClassSkillExtendsActive { get => _isClassSkillExtendsActive; set => SetField(ref _isClassSkillExtendsActive, value); }

        uint _readStartAddress;
        uint _readCount;
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint MasterBlockSize => MASTER_BLOCK_SIZE;
        public uint LevelUpBlockSize => LEVELUP_BLOCK_SIZE;

        uint _currentAddr;
        bool _isLoaded;
        uint _selectedId;
        uint _classSkill;
        uint _xLevelUpAddr;
        bool _isZeroPointer;
        bool _isIndependenceVisible;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }
        public uint ClassSkill { get => _classSkill; set => SetField(ref _classSkill, value); }
        public uint XLevelUpAddr { get => _xLevelUpAddr; set => SetField(ref _xLevelUpAddr, value); }
        public bool IsZeroPointer { get => _isZeroPointer; set => SetField(ref _isZeroPointer, value); }
        public bool IsIndependenceVisible { get => _isIndependenceVisible; set => SetField(ref _isIndependenceVisible, value); }

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
        bool _isPlayerOnly;
        bool _isEnemyOnly;
        bool _isNormalHard;
        bool _isHardOnly;
        bool _isLv255;
        uint _levelValue;

        public uint SelectedLevelUpAddr { get => _selectedLevelUpAddr; set => SetField(ref _selectedLevelUpAddr, value); }
        public uint SelectedLevelUpId { get => _selectedLevelUpId; set => SetField(ref _selectedLevelUpId, value); }
        public uint LevelUpRaw { get => _levelUpRaw; set => SetField(ref _levelUpRaw, value); }
        public uint LevelUpSkill { get => _levelUpSkill; set => SetField(ref _levelUpSkill, value); }
        public bool IsPlayerOnly { get => _isPlayerOnly; set => SetField(ref _isPlayerOnly, value); }
        public bool IsEnemyOnly { get => _isEnemyOnly; set => SetField(ref _isEnemyOnly, value); }
        public bool IsNormalHard { get => _isNormalHard; set => SetField(ref _isNormalHard, value); }
        public bool IsHardOnly { get => _isHardOnly; set => SetField(ref _isHardOnly, value); }
        public bool IsLv255 { get => _isLv255; set => SetField(ref _isLv255, value); }
        public uint LevelValue { get => _levelValue; set => SetField(ref _levelValue, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint assignClassLoc = PreviewIconHelper.FindSkillSystemAssignClassPointerLocation();
            uint assignLevelUpLoc = PreviewIconHelper.FindSkillSystemAssignLevelUpPointerLocation();
            uint iconBase = PreviewIconHelper.FindSkillSystemIconBaseAddress();
            uint textLoc = PreviewIconHelper.FindSkillSystemTextPointerLocation();

            AssignClassPointerLocation = assignClassLoc;
            AssignLevelUpPointerLocation = assignLevelUpLoc;
            IconBaseAddress = iconBase;
            TextBaseAddress = textLoc != U.NOT_FOUND ? rom.p32(textLoc) : 0;
            IsClassSkillExtendsActive = SkillSystemPatchScanner.IsClassSkillExtends(rom);

            if (assignClassLoc == U.NOT_FOUND || assignLevelUpLoc == U.NOT_FOUND
                || iconBase == 0 || textLoc == U.NOT_FOUND)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint assignClassBase = rom.p32(assignClassLoc);
            uint assignLevelUpBase = rom.p32(assignLevelUpLoc);
            if (!U.isSafetyOffset(assignClassBase, rom) || !U.isSafetyOffset(assignLevelUpBase, rom))
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            AssignClassBaseAddress = assignClassBase;
            AssignLevelUpBaseAddress = assignLevelUpBase;
            ReadStartAddress = assignClassBase;

            uint classCount = ResolveClassCount(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < classCount && i < MAX_CLASS_COUNT; i++)
            {
                uint addr = assignClassBase + i * MASTER_BLOCK_SIZE;
                if (!U.isSafetyOffset(addr, rom)) break;
                if (i >= 0xFE && rom.u8(addr) == 0xFF) break;
                string className = NameResolver.GetClassName(i);
                string label = string.IsNullOrEmpty(className) || className == "???"
                    ? "0x" + i.ToString("X02")
                    : "0x" + i.ToString("X02") + " " + className;
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        static uint ResolveClassCount(ROM rom)
        {
            try
            {
                if (rom == null || rom.RomInfo == null) return MAX_CLASS_COUNT;
                uint classP = rom.RomInfo.class_pointer;
                uint classSize = rom.RomInfo.class_datasize;
                if (classP == 0 || classSize == 0) return MAX_CLASS_COUNT;
                uint classBase = rom.p32(classP);
                if (!U.isSafetyOffset(classBase, rom)) return MAX_CLASS_COUNT;
                uint maxByLayout = ((uint)rom.Data.Length - classBase) / classSize;
                uint count = Math.Min(MAX_CLASS_COUNT, maxByLayout);
                return count;
            }
            catch { return MAX_CLASS_COUNT; }
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr, rom)) return;

            CurrentAddr = addr;

            if (_assignClassPointerLocation == U.NOT_FOUND)
                AssignClassPointerLocation = PreviewIconHelper.FindSkillSystemAssignClassPointerLocation();
            if (_assignLevelUpPointerLocation == U.NOT_FOUND)
                AssignLevelUpPointerLocation = PreviewIconHelper.FindSkillSystemAssignLevelUpPointerLocation();
            if (_iconBaseAddress == 0)
                IconBaseAddress = PreviewIconHelper.FindSkillSystemIconBaseAddress();
            // IsClassSkillExtendsActive cached in LoadList - do NOT re-scan on every
            // selection (Copilot bot review on PR #555: full byte-pattern scan is
            // expensive UI-side work and the value cannot change without a ROM swap).

            if (_assignClassBaseAddress == 0 && _assignClassPointerLocation != U.NOT_FOUND)
            {
                uint baseAddr = rom.p32(_assignClassPointerLocation);
                if (U.isSafetyOffset(baseAddr, rom)) AssignClassBaseAddress = baseAddr;
            }
            if (_assignLevelUpBaseAddress == 0 && _assignLevelUpPointerLocation != U.NOT_FOUND)
            {
                uint baseAddr = rom.p32(_assignLevelUpPointerLocation);
                if (U.isSafetyOffset(baseAddr, rom)) AssignLevelUpBaseAddress = baseAddr;
            }

            SelectedId = (_assignClassBaseAddress > 0 && addr >= _assignClassBaseAddress)
                ? (addr - _assignClassBaseAddress) / MASTER_BLOCK_SIZE
                : 0;

            ClassSkill = rom.u8(addr);

            uint levelUpPointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            uint xLevelUpPointer = U.isSafetyOffset(levelUpPointerAddr + 3, rom)
                ? rom.u32(levelUpPointerAddr) : 0;
            XLevelUpAddr = xLevelUpPointer;

            RecomputeVisibilityFlags(rom);
            LoadLevelUpList(rom);

            IsLoaded = true;
        }

        public void LoadLevelUpList(ROM rom)
        {
            LevelUpEntries.Clear();
            if (rom == null) return;
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
            if (_assignLevelUpBaseAddress > 0)
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
            RefreshDifficultyFlagsFromRaw();
        }

        public void RefreshDifficultyFlagsFromRaw()
        {
            uint lv = LevelUpRaw;
            if (lv == 0xFF)
            {
                IsLv255 = true;
                LevelValue = 0;
                IsPlayerOnly = false;
                IsEnemyOnly = false;
                IsNormalHard = false;
                IsHardOnly = false;
                return;
            }
            IsLv255 = false;
            LevelValue = lv & LEVELUP_BIT_LEVEL_MASK;
            IsPlayerOnly = (lv & LEVELUP_BIT_PLAYER) == LEVELUP_BIT_PLAYER;
            IsEnemyOnly = (lv & LEVELUP_BIT_ENEMY) == LEVELUP_BIT_ENEMY;
            IsNormalHard = (lv & LEVELUP_BIT_NORMALHARD) == LEVELUP_BIT_NORMALHARD;
            IsHardOnly = (lv & LEVELUP_BIT_HARDONLY) == LEVELUP_BIT_HARDONLY;
        }

        public void ApplyDifficultyFlagsToRaw()
        {
            if (IsLv255) { LevelUpRaw = 0xFF; return; }
            uint lv = LevelValue & LEVELUP_BIT_LEVEL_MASK;
            if (IsPlayerOnly) lv |= LEVELUP_BIT_PLAYER;
            if (IsEnemyOnly) lv |= LEVELUP_BIT_ENEMY;
            if (IsNormalHard) lv |= LEVELUP_BIT_NORMALHARD;
            if (IsHardOnly) lv |= LEVELUP_BIT_HARDONLY;
            LevelUpRaw = lv;
        }

        public void WriteMaster()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            if (U.isSafetyOffset(CurrentAddr, rom))
            {
                rom.write_u8(CurrentAddr, ClassSkill);
            }

            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (U.isSafetyOffset(pointerAddr + 3, rom))
            {
                uint asOffset = U.toOffset(XLevelUpAddr);
                rom.write_p32(pointerAddr, asOffset);
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

        public LevelUpExpandResult ExpandLevelUpList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return new LevelUpExpandResult { Success = false, Error = "ROM not loaded." };
            if (_assignLevelUpBaseAddress == 0)
                return new LevelUpExpandResult { Success = false, Error = "Skill assignment level-up table not resolved." };

            uint pointerAddr = _assignLevelUpBaseAddress + SelectedId * 4;
            if (!U.isSafetyOffset(pointerAddr + 3, rom))
                return new LevelUpExpandResult { Success = false, Error = "Per-class pointer slot out of bounds." };

            uint gbaPtr = rom.u32(pointerAddr);

            if (gbaPtr == 0 || !U.isSafetyPointer(gbaPtr))
            {
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

            uint baseAddr2 = U.toOffset(gbaPtr);
            uint oldCount = CountLevelUpEntries(rom, baseAddr2);
            var expandResult = DataExpansionCore.ExpandTable(rom, pointerAddr, LEVELUP_BLOCK_SIZE, oldCount);
            if (!expandResult.Success)
                return new LevelUpExpandResult { Success = false, Error = expandResult.Error };
            uint newBaseAddr = expandResult.NewBaseAddress;
            uint newCount = expandResult.NewCount;
            uint newEntryAddr = newBaseAddr + oldCount * LEVELUP_BLOCK_SIZE;
            rom.write_u8(newEntryAddr + 0, 0x01);
            rom.write_u8(newEntryAddr + 1, 0x01);
            uint terminatorAddr = newBaseAddr + newCount * LEVELUP_BLOCK_SIZE;
            if (U.isSafetyOffset(terminatorAddr + 1, rom))
            {
                rom.write_u16(terminatorAddr, (ushort)LEVELUP_TERMINATOR);
            }
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

        void RecomputeVisibilityFlags(ROM rom)
        {
            if (rom == null) { IsZeroPointer = false; IsIndependenceVisible = false; return; }
            if (SelectedId == 0)
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

        bool IsTableShared(ROM rom, uint currentGbaPtr)
        {
            if (!U.isSafetyPointer(currentGbaPtr)) return false;
            uint classCount = ReadCount;
            for (uint i = 0; i < classCount; i++)
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
            ClassSkill = 0;
            XLevelUpAddr = 0;
            IsZeroPointer = false;
            IsIndependenceVisible = false;
            LevelUpEntries.Clear();
            SelectedLevelUpAddr = 0;
            SelectedLevelUpId = 0;
            LevelUpRaw = 0;
            LevelUpSkill = 0;
            IsPlayerOnly = false;
            IsEnemyOnly = false;
            IsNormalHard = false;
            IsHardOnly = false;
            IsLv255 = false;
            LevelValue = 0;
            AssignClassBaseAddress = 0;
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

        public bool ExportAllData(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (_assignClassPointerLocation == U.NOT_FOUND
                || _assignLevelUpPointerLocation == U.NOT_FOUND) return false;
            uint classBase = rom.p32(_assignClassPointerLocation);
            uint classCount = ResolveClassCount(rom);
            return SkillAssignmentClassSkillSystemCore.ExportAllData(
                rom, classBase, _assignLevelUpPointerLocation, classCount, filename);
        }

        public bool ImportAllData(string filename)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (_assignClassPointerLocation == U.NOT_FOUND
                || _assignLevelUpPointerLocation == U.NOT_FOUND) return false;
            uint classBase = rom.p32(_assignClassPointerLocation);
            uint classCount = ResolveClassCount(rom);
            return SkillAssignmentClassSkillSystemCore.ImportAllData(
                rom, classBase, _assignLevelUpPointerLocation, classCount, filename);
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
                ["ClassSkill"] = $"0x{ClassSkill:X02}",
                ["XLevelUpAddr"] = $"0x{XLevelUpAddr:X08}",
                ["LevelUpRaw"] = $"0x{LevelUpRaw:X02}",
                ["LevelUpSkill"] = $"0x{LevelUpSkill:X02}",
                ["IsClassSkillExtendsActive"] = IsClassSkillExtendsActive ? "true" : "false",
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
                ["u8@0x00_ClassSkill"] = $"0x{rom.u8(a + 0):X02}",
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
            ["ClassSkill"] = "u8@0x00",
        };
    }
}
