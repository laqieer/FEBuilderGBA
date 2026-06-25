// SPDX-License-Identifier: GPL-3.0-or-later
// Canonical ViewModel for SkillAssignmentUnitCSkillSysView (gap-sweep #1451).
//
// Replaces the inert placeholder pair
// (SkillAssignmentUnitCSkillSysViewModel.cs stub + ...ViewViewModel.cs)
// with a single ViewModel matching the XView -> XViewModel naming contract
// used by both the JumpParityScanner and the UndoCoverageScanner View-to-VM
// upgrade pass.
//
// Source-of-truth WinForms surface: FEBuilderGBA/SkillAssignmentUnitCSkillSysForm.cs
// + .Designer.cs. The form is opened for PatchUtil.skill_system_enum
// CSkillSys09x / CSkillSys300 (UnitEditorView.EditSkills_Click:721-723,
// mirroring WF MainFE8Form routing). It is a full master/detail editor:
//   - Per-UNIT personal-skill table (W0 u16 @ +0, 4-byte stride) at
//     p32(gpConstSkillTable_Person).
//   - Per-unit level-up pointer (u32) at p32(gpCharLevelUpSkillTable) + 4*unitId,
//     which dereferences to a 0x0000/0xFFFF-terminated u16 array (level|skill,
//     2 bytes per entry). OPTIONAL on old patches (gpCharLevelUpSkillTable may
//     not resolve) -> the View hides the entire N1 level-up group.
//
// This is the UNIT sibling of SkillAssignmentClassCSkillSysViewModel (#415);
// the two differ only in the master table constant (Person vs Job), the
// level-up table constant (Char vs Class), and the master enumeration (unit
// list vs class list). The Unit WinForms Designer has NO X_LV level-breakdown
// panel (Class-only), so this VM omits the level-packing helpers. It DOES carry
// the per-unit level-up pointer (X_LevelUpAddr) which WF's WriteButton commits
// to AssignLevelUpBaseAddress + unitId*4.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentUnitCSkillSysViewModel : ViewModelBase, IDataVerifiable
    {
        // ---- Unit table field definition (4-byte entry, u16 W0 at +0) ----
        // The remaining 2 bytes are padding; WinForms keeps them intact, so we
        // model only the W0 field and leave EditorFormRef to no-op on the
        // padding bytes during writes.
        static readonly List<EditorFormRef.FieldDef> _unitFields =
            EditorFormRef.DetectFields(new[] { "W0" });

        // ---- N1 (level-up skill) table field definition ----
        // 2 bytes per entry: B0 = level (u8), B1 = skill id (u8 hex).
        static readonly List<EditorFormRef.FieldDef> _n1Fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1" });

        // ---- WF fixed addresses (mirror SkillConfigCSkillSystem09xForm) ----
        // GP-pointer slots into the per-character skill / character-levelup-skill
        // tables. These are the UNIT counterparts of the Class VM's
        // gpConstSkillTable_Job / gpClassLevelUpSkillTable.
        public const uint gpConstSkillTable_Person = 0xB2A61C;
        public const uint gpCharLevelUpSkillTable = 0xB2A7FC;
        // Skill-info table (8-byte per skill: u32 icon-pointer + u16 nameMsg + u16 descMsg).
        // Shared with SkillConfigCSkillSystem09xForm.
        public const uint gpSkillInfos = 0xB2A614;
        public const uint SKILL_INFO_SIZE = 8;

        // ---- Block sizes ----
        public const uint UNIT_BLOCK_SIZE = 4;
        public const uint N1_BLOCK_SIZE = 2;
        public const uint MAX_N1_COUNT = 0x100; // WF caps N1_ReadCount.Maximum at 256.

        // ---- State ----
        uint _currentAddr;
        uint _selectedUnitIndex;
        bool _isLoaded;
        bool _isCSkillSysActive;
        bool _hasLevelUpTable;

        uint _unitSkill;            // W0 - personal skill

        uint _xLevelUpAddr;         // per-unit level-up pointer (GBA-pointer form for the UI)
        uint _levelUpAddr;          // resolved level-up list base (offset form)
        uint _n1CurrentAddr;        // currently selected level-up row addr
        uint _n1Level;              // B0 - acquisition level
        uint _n1Skill;              // B1 - skill id

        uint _readStartAddress;
        uint _readCount;            // 0/unset => full unit_maxcount (Copilot review #5)
        uint _n1ReadCount = 16;

        string _statusMessage = "Skill Assignment - Unit (CSkillSys).\nThis editor requires the CSkillSys (0.9x / 3.00+) skill system patch to be installed.\nUse the Patch Manager to install the CSkillSys patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SelectedUnitIndex { get => _selectedUnitIndex; set => SetField(ref _selectedUnitIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool IsCSkillSysActive { get => _isCSkillSysActive; set => SetField(ref _isCSkillSysActive, value); }

        /// <summary>
        /// True only when the per-unit level-up table (gpCharLevelUpSkillTable)
        /// resolves to a valid, safe ROM offset. Old CSkillSys patches lack the
        /// unit-based level-up table, so the View hides the entire N1 level-up
        /// group when this is false — mirroring WinForms
        /// <c>SkillAssignmentUnitCSkillSysForm</c> which calls
        /// <c>UnitLevelUpSkill.Hide()</c> when
        /// <c>GetPrCharLevelUpSkillTable() == U.NOT_FOUND</c>.
        /// </summary>
        public bool HasLevelUpTable { get => _hasLevelUpTable; set => SetField(ref _hasLevelUpTable, value); }

        public uint UnitSkill { get => _unitSkill; set => SetField(ref _unitSkill, value); }
        public uint XLevelUpAddr { get => _xLevelUpAddr; set => SetField(ref _xLevelUpAddr, value); }
        public uint LevelUpAddr { get => _levelUpAddr; set => SetField(ref _levelUpAddr, value); }
        public uint N1CurrentAddr { get => _n1CurrentAddr; set => SetField(ref _n1CurrentAddr, value); }
        public uint N1Level { get => _n1Level; set => SetField(ref _n1Level, value); }
        public uint N1Skill { get => _n1Skill; set => SetField(ref _n1Skill, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint N1ReadCount { get => _n1ReadCount; set => SetField(ref _n1ReadCount, value); }

        public uint BlockSize => UNIT_BLOCK_SIZE;
        public uint N1BlockSize => N1_BLOCK_SIZE;

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Recompute the CSkillSys patch-active flag. The Unit form is routed
        /// for both CSkillSys09x and CSkillSys300, so either counts as active.
        /// </summary>
        public void RefreshPatchState()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null)
            {
                IsCSkillSysActive = false;
                return;
            }
            var svc = PatchDetectionService.Instance;
            IsCSkillSysActive =
                svc.SkillSystem == PatchDetectionService.SkillSystemType.CSkillSys09x ||
                svc.SkillSystem == PatchDetectionService.SkillSystemType.CSkillSys300;
        }

        /// <summary>
        /// Master unit list: one entry per unit (4-byte W0 row), capped by the
        /// unit-table data count. Mirrors WF
        /// <c>SkillAssignmentUnitCSkillSysForm.Init</c> (stride 4,
        /// <c>UnitForm.DataCount()</c> rows). A 0/unset ReadCount means the FULL
        /// unit_maxcount (Copilot review #5: no class-editor-style 32-row cap).
        /// </summary>
        public List<AddrResult> LoadUnitList()
        {
            var result = new List<AddrResult>();
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            uint baseAddr = (ReadStartAddress != 0)
                ? ReadStartAddress
                : rom.p32(gpConstSkillTable_Person);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            // Level-up table is OPTIONAL — tolerate an unsafe dereference.
            uint levelUpBase = rom.p32(gpCharLevelUpSkillTable);
            HasLevelUpTable = U.isSafetyOffset(levelUpBase, rom);

            uint unitCount = rom.RomInfo.unit_maxcount;
            uint cap = (ReadCount > 0 && ReadCount < unitCount) ? ReadCount : unitCount;
            for (uint i = 0; i < cap; i++)
            {
                uint addr = baseAddr + i * UNIT_BLOCK_SIZE;
                if (addr + UNIT_BLOCK_SIZE > (uint)rom.Data.Length) break;
                // Row index i IS the 1-based WF uid (0 = empty sentinel,
                // 1 = Eirika on FE8) — the same value WF feeds to
                // UnitForm.GetUnitName. Use the one-based resolver so the
                // displayed name matches WF (Copilot review #1).
                string unitName = NameResolver.GetUnitNameByOneBasedId(i);
                string name = U.ToHexString(i) + " " + unitName;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Load a single unit entry's W0 (personal skill) + level-up pointer.</summary>
        public void LoadEntry(uint addr)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + UNIT_BLOCK_SIZE > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _unitFields);
            UnitSkill = values["W0"];

            if (rom.RomInfo != null)
            {
                uint levelUpBase = rom.p32(gpCharLevelUpSkillTable);
                HasLevelUpTable = U.isSafetyOffset(levelUpBase, rom);
                // Resolve the per-unit level-up pointer (display as GBA pointer).
                if (HasLevelUpTable)
                {
                    uint slotAddr = levelUpBase + SelectedUnitIndex * 4;
                    if (slotAddr + 4 <= (uint)rom.Data.Length)
                    {
                        uint ptrOffset = rom.p32(slotAddr);
                        LevelUpAddr = ptrOffset;
                        XLevelUpAddr = (ptrOffset != 0) ? U.toPointer(ptrOffset) : 0;
                    }
                }
                else
                {
                    LevelUpAddr = 0;
                    XLevelUpAddr = 0;
                }
            }
            IsLoaded = true;
        }

        // ---- Skill preview helpers (shared layout with the Class variant) ----

        public static string ResolveSkillName(ROM? rom, uint id)
        {
            if (rom == null) return "";
            try
            {
                if (!U.isSafetyOffset(gpSkillInfos + 4, rom)) return "";
                uint baseAddr = rom.p32(gpSkillInfos);
                if (!U.isSafetyOffset(baseAddr, rom)) return "";
                uint entryAddr = baseAddr + SKILL_INFO_SIZE * id;
                if (!U.isSafetyOffset(entryAddr + 6, rom)) return "";
                uint nameMsg = rom.u16(entryAddr + 4);
                if (nameMsg != 0)
                {
                    string text = NameResolver.GetTextById(nameMsg);
                    if (!string.IsNullOrEmpty(text) && text != "???") return text;
                }
                uint descMsg = rom.u16(entryAddr + 6);
                if (descMsg != 0)
                {
                    string desc = NameResolver.GetTextById(descMsg);
                    if (!string.IsNullOrEmpty(desc) && desc != "???")
                    {
                        int colon = desc.IndexOf(':');
                        if (colon > 0) return desc.Substring(0, colon).Trim();
                    }
                }
            }
            catch { /* swallow */ }
            return "";
        }

        public static string ResolveSkillDescription(ROM? rom, uint id)
        {
            if (rom == null) return "";
            try
            {
                if (!U.isSafetyOffset(gpSkillInfos + 4, rom)) return "";
                uint baseAddr = rom.p32(gpSkillInfos);
                if (!U.isSafetyOffset(baseAddr, rom)) return "";
                uint entryAddr = baseAddr + SKILL_INFO_SIZE * id;
                if (!U.isSafetyOffset(entryAddr + 6, rom)) return "";
                uint descMsg = rom.u16(entryAddr + 6);
                if (descMsg == 0) return "";
                return NameResolver.GetTextById(descMsg) ?? "";
            }
            catch { return ""; }
        }

        public static uint ResolveSkillIconGbaPointer(ROM? rom, uint id)
        {
            if (rom == null) return 0;
            try
            {
                if (!U.isSafetyOffset(gpSkillInfos + 4, rom)) return 0;
                uint baseAddr = rom.p32(gpSkillInfos);
                if (!U.isSafetyOffset(baseAddr, rom)) return 0;
                uint entryAddr = baseAddr + SKILL_INFO_SIZE * id;
                if (!U.isSafetyOffset(entryAddr + 3, rom)) return 0;
                return rom.u32(entryAddr); // raw GBA pointer (high bit set)
            }
            catch { return 0; }
        }

        /// <summary>
        /// Walk the N1 level-up skill list at <paramref name="addr"/>. Mirrors
        /// WF <c>N1_Init</c>: 2 bytes per entry, terminator on u16 == 0x0000 OR
        /// u16 == 0xFFFF. Cap by <see cref="N1ReadCount"/> when set (>0).
        /// </summary>
        public List<AddrResult> LoadN1List(uint addr)
        {
            var result = new List<AddrResult>();
            ROM? rom = CoreState.ROM;
            if (rom == null || addr == 0) return result;
            if (!U.isSafetyOffset(addr, rom)) return result;

            uint cap = (N1ReadCount > 0 && N1ReadCount < MAX_N1_COUNT)
                ? N1ReadCount
                : MAX_N1_COUNT;
            for (uint i = 0; i < cap; i++)
            {
                uint rowAddr = addr + i * N1_BLOCK_SIZE;
                if (rowAddr + N1_BLOCK_SIZE > (uint)rom.Data.Length) break;
                uint row = rom.u16(rowAddr);
                if (row == 0xFFFF || row == 0x0000) break;
                uint level = rom.u8(rowAddr + 0);
                uint skillId = rom.u8(rowAddr + 1);
                string name = U.ToHexString(skillId) + " (Lv" + level + ")";
                result.Add(new AddrResult(rowAddr, name, i));
            }
            return result;
        }

        public void LoadN1Entry(uint addr)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + N1_BLOCK_SIZE > (uint)rom.Data.Length) return;
            N1CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _n1Fields);
            N1Level = values["B0"];
            N1Skill = values["B1"];
        }

        // -----------------------------------------------------------------
        // Write methods - canonical names tracked by UndoCoverageScanner.
        // -----------------------------------------------------------------

        /// <summary>Write the personal-skill W0 value at the current address.</summary>
        public void WriteUnitSkill()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["W0"] = UnitSkill };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _unitFields);
        }

        /// <summary>
        /// Write the edited per-unit level-up pointer (<see cref="XLevelUpAddr"/>)
        /// back to <c>p32(gpCharLevelUpSkillTable) + unitId*4</c>. Mirrors WF
        /// <c>SkillAssignmentUnitCSkillSysForm.WriteButton_Click</c>. No-op when
        /// the level-up table is absent (Copilot review #2). The UI carries the
        /// GBA-pointer form (0x08xxxxxx); we normalize to an offset before
        /// committing with <c>write_p32</c>.
        /// </summary>
        public void WriteLevelUpPointer()
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            if (!HasLevelUpTable) return;
            uint levelUpBase = rom.p32(gpCharLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpBase, rom)) return;
            uint slotAddr = levelUpBase + SelectedUnitIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return;
            uint asOffset = U.toOffset(XLevelUpAddr);
            rom.write_p32(slotAddr, asOffset);
            LevelUpAddr = asOffset;
        }

        /// <summary>Write a single N1 row (B0 + B1) at the current N1 address.</summary>
        public void WriteN1Entry()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null || N1CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = N1Level & 0xFF,
                ["B1"] = N1Skill & 0xFF,
            };
            EditorFormRef.WriteFields(rom, N1CurrentAddr, values, _n1Fields);
        }

        /// <summary>
        /// Expand the N1 level-up skill list for the currently selected unit.
        /// Mirrors WF <c>N1_InputFormRef_AddressListExpandsEvent</c>. Returns
        /// the new base address (0 on failure).
        /// </summary>
        public uint ExpandN1List(uint newCount)
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint levelUpTablePtr = rom.p32(gpCharLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr, rom)) return 0;
            uint slotAddr = levelUpTablePtr + SelectedUnitIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return 0;

            uint oldPointer = rom.p32(slotAddr);
            uint oldCount = 0;
            if (U.isSafetyOffset(oldPointer, rom))
            {
                for (uint i = 0; i < MAX_N1_COUNT; i++)
                {
                    uint rowAddr = oldPointer + i * N1_BLOCK_SIZE;
                    if (rowAddr + N1_BLOCK_SIZE > (uint)rom.Data.Length) break;
                    uint row = rom.u16(rowAddr);
                    if (row == 0xFFFF || row == 0x0000) break;
                    oldCount++;
                }
            }

            if (newCount <= oldCount) return oldPointer;

            uint newBufferSize = (newCount + 1) * N1_BLOCK_SIZE; // +1 for terminator
            newBufferSize = (newBufferSize + 3) & ~3u;
            uint newBase = DataExpansionCore.FindFreeSpace(rom, newBufferSize);
            if (newBase == U.NOT_FOUND || !U.isSafetyOffset(newBase, rom)) return 0;
            rom.write_p32(slotAddr, newBase);

            for (uint i = 0; i < oldCount; i++)
            {
                uint oldRow = oldPointer + i * N1_BLOCK_SIZE;
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, rom.u16(oldRow));
            }
            // Fill new slots with default 0x0101 (Lv1, Skill1) — skill==0 reads
            // as a terminator (WF default_skill_lv).
            for (uint i = oldCount; i < newCount; i++)
            {
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, 0x0101);
            }
            rom.write_u16(newBase + newCount * N1_BLOCK_SIZE, 0x0000);

            LevelUpAddr = newBase;
            XLevelUpAddr = U.toPointer(newBase);
            return newBase;
        }

        /// <summary>
        /// Mark the currently selected unit's level-up skill table as
        /// independent (copy-on-write). Mirrors WF
        /// <c>SkillAssignmentUnitCSkillSysForm.IndependenceButton_Click</c>.
        /// </summary>
        public uint MakeIndependent()
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint levelUpTablePtr = rom.p32(gpCharLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr, rom)) return 0;
            uint slotAddr = levelUpTablePtr + SelectedUnitIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return 0;

            uint oldPointer = rom.p32(slotAddr);
            if (!U.isSafetyOffset(oldPointer, rom)) return 0;

            uint rowCount = 0;
            for (uint i = 0; i < MAX_N1_COUNT; i++)
            {
                uint rowAddr = oldPointer + i * N1_BLOCK_SIZE;
                if (rowAddr + N1_BLOCK_SIZE > (uint)rom.Data.Length) break;
                uint row = rom.u16(rowAddr);
                if (row == 0xFFFF || row == 0x0000) break;
                rowCount++;
            }

            uint bufferSize = (rowCount + 1) * N1_BLOCK_SIZE;
            bufferSize = (bufferSize + 3) & ~3u;
            uint newBase = DataExpansionCore.FindFreeSpace(rom, bufferSize);
            if (newBase == U.NOT_FOUND || !U.isSafetyOffset(newBase, rom)) return 0;
            rom.write_p32(slotAddr, newBase);
            for (uint i = 0; i < rowCount; i++)
            {
                uint oldRow = oldPointer + i * N1_BLOCK_SIZE;
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, rom.u16(oldRow));
            }
            rom.write_u16(newBase + rowCount * N1_BLOCK_SIZE, 0x0000);
            LevelUpAddr = newBase;
            XLevelUpAddr = U.toPointer(newBase);
            return newBase;
        }

        /// <summary>
        /// True when the level-up skill pointer for the selected unit is shared
        /// with at least one other unit. Mirrors WF
        /// <c>IsShowIndependencePanel</c>.
        /// </summary>
        public bool IsShowIndependencePanel(uint unitCount)
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            uint levelUpTablePtr = rom.p32(gpCharLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr, rom)) return false;
            uint slotAddr = levelUpTablePtr + SelectedUnitIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return false;
            uint currentPtr = rom.p32(slotAddr);
            if (!U.isSafetyOffset(currentPtr, rom)) return false;
            for (uint i = 0; i < unitCount; i++)
            {
                if (i == SelectedUnitIndex) continue;
                uint otherSlot = levelUpTablePtr + i * 4;
                if (otherSlot + 4 > (uint)rom.Data.Length) continue;
                uint otherPtr = rom.p32(otherSlot);
                if (otherPtr == currentPtr) return true;
            }
            return false;
        }

        public int GetListCount()
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return (int)rom.RomInfo.unit_maxcount;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = "0x" + CurrentAddr.ToString("X08"),
                ["UnitSkill"] = NameResolver.GetSkillName(UnitSkill),
                ["XLevelUpAddr"] = "0x" + XLevelUpAddr.ToString("X08"),
                ["LevelUpAddr"] = "0x" + LevelUpAddr.ToString("X08"),
                ["HasLevelUpTable"] = HasLevelUpTable.ToString(),
                ["B0"] = N1Level.ToString(),
                ["B1"] = N1Skill.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = "0x" + a.ToString("X08"),
                ["u16@0x00"] = "0x" + rom.u16(a + 0).ToString("X04"),
            };
            if (N1CurrentAddr != 0 && U.isSafetyOffset(N1CurrentAddr, rom) &&
                N1CurrentAddr + 1 < (uint)rom.Data.Length)
            {
                report["n1_addr"] = "0x" + N1CurrentAddr.ToString("X08");
                report["u8@0x00"] = "0x" + rom.u8(N1CurrentAddr + 0).ToString("X02");
                report["u8@0x01"] = "0x" + rom.u8(N1CurrentAddr + 1).ToString("X02");
            }
            try
            {
                if (U.isSafetyOffset(gpSkillInfos + 4, rom))
                {
                    uint baseAddr = rom.p32(gpSkillInfos);
                    if (U.isSafetyOffset(baseAddr, rom))
                    {
                        uint entry = baseAddr + SKILL_INFO_SIZE * UnitSkill;
                        if (U.isSafetyOffset(entry + 6, rom))
                        {
                            report["skill_info_addr"] = "0x" + entry.ToString("X08");
                            report["u32@0x00"] = "0x" + rom.u32(entry + 0).ToString("X08");
                            report["u16@0x04"] = "0x" + rom.u16(entry + 4).ToString("X04");
                            report["u16@0x06"] = "0x" + rom.u16(entry + 6).ToString("X04");
                        }
                    }
                }
            }
            catch { /* swallow - raw report is best-effort */ }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitSkill"] = "u16@0x00",
            ["B0"] = "u8@0x00",
            ["B1"] = "u8@0x01",
            ["SkillIconPtr"] = "u32@0x00",
            ["SkillNameMsg"] = "u16@0x04",
            ["SkillDescMsg"] = "u16@0x06",
        };
    }
}
