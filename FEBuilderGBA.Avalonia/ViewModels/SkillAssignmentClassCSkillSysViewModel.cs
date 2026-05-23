// SPDX-License-Identifier: GPL-3.0-or-later
// Canonical ViewModel for SkillAssignmentClassCSkillSysView (gap-sweep #415).
//
// Replaces the placeholder duplicate-name pair
// (SkillAssignmentClassCSkillSysViewModel.cs stub + ...ViewViewModel.cs)
// with a single ViewModel matching the XView -> XViewModel naming contract
// used by both the JumpParityScanner and the UndoCoverageScanner View-to-VM
// upgrade pass.
//
// Source-of-truth WinForms surface: FEBuilderGBA/SkillAssignmentClassCSkillSysForm.cs
// + .Designer.cs. The form is opened only when
// PatchUtil.skill_system_enum.CSkillSys300 is detected (MainFE8Form.cs:715).
// IsClassSkillExtends gating mirrors WF
// SkillConfigSkillSystemForm.IsClassSkillExtends, now backed by Core
// SkillSystemPatchScanner.IsClassSkillExtends so both UIs (and the sister
// gap-sweep #416 form) share one detector.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentClassCSkillSysViewModel : ViewModelBase, IDataVerifiable
    {
        // ---- Class table field definition (4-byte entry, u16 W0 at +0) ----
        // The remaining 2 bytes are padding; WinForms keeps them intact, so
        // we model only the W0 field and leave EditorFormRef to no-op on the
        // padding bytes during writes.
        static readonly List<EditorFormRef.FieldDef> _classFields =
            EditorFormRef.DetectFields(new[] { "W0" });

        // ---- N1 (level-up skill) table field definition ----
        // 2 bytes per entry: B0 = level (u8), B1 = skill id (u8 hex).
        static readonly List<EditorFormRef.FieldDef> _n1Fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1" });

        // ---- WF fixed addresses (mirror SkillConfigCSkillSystem09xForm) ----
        // GP-pointer slots into class skill / class-levelup-skill tables.
        public const uint gpConstSkillTable_Job = 0xB2A620;
        public const uint gpClassLevelUpSkillTable = 0xB2A7F8;
        // Skill-info table (8-byte per skill: u32 icon-pointer + u16 nameMsg + u16 descMsg).
        // Shared with SkillConfigCSkillSystem09xForm. Used by ResolveSkillName /
        // ResolveSkillDescription / ResolveSkillIconGbaPointer for the inline
        // preview labels (Copilot CLI PR #552 review #3).
        public const uint gpSkillInfos = 0xB2A614;
        public const uint SKILL_INFO_SIZE = 8;

        // ---- Block sizes ----
        public const uint CLASS_BLOCK_SIZE = 4;
        public const uint N1_BLOCK_SIZE = 2;
        public const uint MAX_CLASS_COUNT = 0x100;
        public const uint MAX_N1_COUNT = 0x40; // WF reads up to 0xFE before terminator; cap defensively.

        // ---- State ----
        uint _currentAddr;
        uint _selectedClassIndex;
        bool _isLoaded;
        bool _isCSkillSys300Active;
        bool _isClassSkillExtendsActive;

        // Class-skill row
        uint _classSkill;

        // Level-up skill sub-list state
        uint _levelUpAddr;       // pointer for currently selected class
        uint _n1CurrentAddr;     // currently selected level-up row addr
        uint _n1Level;           // B0 - composite (low 5 bits = level, bits 5..7 = mode flags)
        uint _n1Skill;           // B1 - skill id

        // Read-config bar
        uint _readStartAddress;
        uint _readCount = 32;
        uint _n1ReadCount = 16;

        // Status banner names CSkillSys300 explicitly so the WF gating decision
        // is visible to users (Copilot CLI plan-review concern #1).
        string _statusMessage = "Skill Assignment - Class (CSkillSys 3.00).\nThis editor requires the CSkillSys 3.00+ skill system patch to be installed.\nUse the Patch Manager to install the CSkillSys 3.00 patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SelectedClassIndex { get => _selectedClassIndex; set => SetField(ref _selectedClassIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public bool IsCSkillSys300Active { get => _isCSkillSys300Active; set => SetField(ref _isCSkillSys300Active, value); }
        public bool IsClassSkillExtendsActive { get => _isClassSkillExtendsActive; set => SetField(ref _isClassSkillExtendsActive, value); }

        public uint ClassSkill { get => _classSkill; set => SetField(ref _classSkill, value); }

        public uint LevelUpAddr { get => _levelUpAddr; set => SetField(ref _levelUpAddr, value); }
        public uint N1CurrentAddr { get => _n1CurrentAddr; set => SetField(ref _n1CurrentAddr, value); }
        public uint N1Level { get => _n1Level; set => SetField(ref _n1Level, value); }
        public uint N1Skill { get => _n1Skill; set => SetField(ref _n1Skill, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint N1ReadCount { get => _n1ReadCount; set => SetField(ref _n1ReadCount, value); }

        public uint BlockSize => CLASS_BLOCK_SIZE;
        public uint N1BlockSize => N1_BLOCK_SIZE;

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Recompute patch-active flags from the current CoreState.ROM. Cheap
        /// to call - the only ROM scan is the Core
        /// <see cref="SkillSystemPatchScanner.IsClassSkillExtends"/> which is
        /// itself cached at the field level.
        /// </summary>
        public void RefreshPatchState()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null)
            {
                IsCSkillSys300Active = false;
                IsClassSkillExtendsActive = false;
                return;
            }
            // Patch detection - see PatchDetectionService for the byte
            // signature catalog. The View also queries the service so this
            // can be plumbed lazily; we keep the VM probe so synthetic-ROM
            // tests can pass without spinning up the full service.
            var svc = PatchDetectionService.Instance;
            IsCSkillSys300Active = svc.SkillSystem == PatchDetectionService.SkillSystemType.CSkillSys300;
            IsClassSkillExtendsActive = SkillSystemPatchScanner.IsClassSkillExtends(rom);
        }

        /// <summary>
        /// Master class list: one entry per class (4-byte W0 row), capped
        /// by the class-table data count. Mirrors WF
        /// <c>SkillAssignmentClassCSkillSysForm.Init</c>.
        /// </summary>
        public List<AddrResult> LoadClassList()
        {
            var result = new List<AddrResult>();
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            // Honor user-overridden read-config (Copilot CLI PR #552 review #2).
            // If ReadStartAddress != 0, use it directly as the table base
            // (overrides the gpConstSkillTable_Job pointer dereference).
            uint baseAddr = (ReadStartAddress != 0)
                ? ReadStartAddress
                : rom.p32(gpConstSkillTable_Job);
            if (!U.isSafetyOffset(baseAddr)) return result;

            // Use the same class-count probe as CCBranchEditorViewModel
            // (mirrors WF ClassForm.DataCount semantics): scan classes
            // table until u8(class+4) == 0 sentinel. Cap by ReadCount when
            // user set it (>0); otherwise use the full enumerated count.
            int classCount = ComputeClassCount(rom);
            int cap = (ReadCount > 0 && (int)ReadCount < classCount)
                ? (int)ReadCount
                : classCount;
            for (int i = 0; i < cap; i++)
            {
                uint addr = baseAddr + (uint)(i * (int)CLASS_BLOCK_SIZE);
                if (addr + CLASS_BLOCK_SIZE > (uint)rom.Data.Length) break;
                // Terminator: WF allows 0xFF at i>=0xFE; we keep that semantic.
                if (i >= 0xFE && rom.u8(addr) == 0xFF) break;
                string name = U.ToHexString((uint)i) + " " + ResolveClassName(rom, (uint)i);
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            return result;
        }

        static int ComputeClassCount(ROM rom)
        {
            if (rom?.RomInfo == null) return 0x80;
            uint classPtr = rom.RomInfo.class_pointer;
            uint classBase = (classPtr != 0) ? rom.p32(classPtr) : 0;
            uint classDataSize = rom.RomInfo.class_datasize;
            int classCount = 0;
            if (classBase != 0 && U.isSafetyOffset(classBase) && classDataSize > 0)
            {
                for (uint i = 0; i <= 0xFF; i++)
                {
                    uint classAddr = (uint)(classBase + i * classDataSize);
                    if (classAddr + classDataSize > (uint)rom.Data.Length) break;
                    if (i > 0 && rom.u8(classAddr + 4) == 0) break;
                    classCount++;
                }
            }
            if (classCount == 0) classCount = 0x80;
            return classCount;
        }

        static string ResolveClassName(ROM rom, uint classIndex)
        {
            try { return NameResolver.GetClassName(classIndex); }
            catch { return "???"; }
        }

        /// <summary>Load a single class entry's W0 (class skill).</summary>
        public void LoadEntry(uint addr)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + CLASS_BLOCK_SIZE > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _classFields);
            ClassSkill = values["W0"];
            IsLoaded = true;
        }

        // ---- Skill preview helpers (Copilot CLI PR #552 review #3) ----
        // Mirrors WF SkillConfigCSkillSystem09xForm.GetSkillName / GetSkillDesc /
        // DrawSkillIcon. All resolvers are static + null-safe so they can be
        // called from both the VM and the View code-behind without spinning up
        // additional service infrastructure.

        /// <summary>
        /// Resolve the display name of skill <paramref name="id"/>. Reads
        /// u16 nameMsg @ skill-info+4; falls back to the colon prefix of the
        /// description text (matches WF SkillTextToName).
        /// </summary>
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

        /// <summary>
        /// Resolve the description/effect text of skill <paramref name="id"/>.
        /// Reads u16 descMsg @ skill-info+6.
        /// </summary>
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

        /// <summary>
        /// Resolve the GBA pointer (high-bit set) to the 4bpp icon tile data
        /// for skill <paramref name="id"/>. Returns 0 if unavailable.
        /// </summary>
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
        /// Walk the N1 level-up skill list at <paramref name="addr"/>.
        /// Mirrors WF <c>N1_Init</c>: 2 bytes per entry, terminator on
        /// u16 == 0x0000 OR u16 == 0xFFFF. Cap by <see cref="N1ReadCount"/>
        /// when set (>0); falls back to MAX_N1_COUNT cap otherwise
        /// (Copilot CLI PR #552 review #2: previously this control was inert).
        /// </summary>
        public List<AddrResult> LoadN1List(uint addr)
        {
            var result = new List<AddrResult>();
            ROM? rom = CoreState.ROM;
            if (rom == null || addr == 0) return result;
            if (!U.isSafetyOffset(addr)) return result;

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

        /// <summary>Load a single N1 row (B0 level + B1 skill id).</summary>
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
        // Write methods - the canonical names below are the ones the
        // UndoCoverageScanner uses for its View-to-VM Begin/Commit upgrade.
        // The View MUST wrap each callsite in _undoService.Begin/Commit/
        // Rollback so the scanner picks them up. Parity test
        // UndoCoverage_ViewCoversCanonicalVmWriteMethods enforces this.
        // -----------------------------------------------------------------

        /// <summary>Write the class-level W0 (class skill) value at the current address.</summary>
        public void WriteClassSkill()
        {
            ROM? rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["W0"] = ClassSkill };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _classFields);
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
        /// Expand the N1 level-up skill list for the currently selected class.
        /// Mirrors WF <c>N1_InputFormRef_AddressListExpandsEvent</c>.
        /// Returns the new base address (0 on failure).
        /// </summary>
        public uint ExpandN1List(uint newCount)
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint levelUpTablePtr = rom.p32(gpClassLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr)) return 0;
            uint slotAddr = levelUpTablePtr + SelectedClassIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return 0;

            uint oldPointer = rom.p32(slotAddr);
            uint oldCount = 0;
            if (U.isSafetyOffset(oldPointer))
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

            if (newCount <= oldCount) return oldPointer; // no expansion needed

            // Defensive: round up to next 4-byte boundary for the new buffer.
            uint newBufferSize = (newCount + 1) * N1_BLOCK_SIZE; // +1 for terminator
            uint newBase = DataExpansionCore.FindFreeSpace(rom, newBufferSize);
            if (newBase == U.NOT_FOUND || !U.isSafetyOffset(newBase)) return 0;
            // Repoint the slot to the new region.
            rom.write_p32(slotAddr, newBase);

            // Copy old rows.
            for (uint i = 0; i < oldCount; i++)
            {
                uint oldRow = oldPointer + i * N1_BLOCK_SIZE;
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, rom.u16(oldRow));
            }
            // Fill new slots with default 0x0101 (Lv1, Skill1) until newCount.
            for (uint i = oldCount; i < newCount; i++)
            {
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, 0x0101);
            }
            // Terminator at newCount.
            rom.write_u16(newBase + newCount * N1_BLOCK_SIZE, 0x0000);

            LevelUpAddr = newBase;
            return newBase;
        }

        /// <summary>
        /// Mark the currently selected class's level-up skill table as
        /// independent (copy-on-write). Mirrors WF
        /// <c>SkillAssignmentClassCSkillSysForm.IndependenceButton_Click</c>.
        /// Returns the new base address (0 if not needed / failed).
        /// </summary>
        public uint MakeIndependent()
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint levelUpTablePtr = rom.p32(gpClassLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr)) return 0;
            uint slotAddr = levelUpTablePtr + SelectedClassIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return 0;

            uint oldPointer = rom.p32(slotAddr);
            if (!U.isSafetyOffset(oldPointer)) return 0;

            // Count current rows
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
            uint newBase = DataExpansionCore.FindFreeSpace(rom, bufferSize);
            if (newBase == U.NOT_FOUND || !U.isSafetyOffset(newBase)) return 0;
            rom.write_p32(slotAddr, newBase);
            for (uint i = 0; i < rowCount; i++)
            {
                uint oldRow = oldPointer + i * N1_BLOCK_SIZE;
                uint newRow = newBase + i * N1_BLOCK_SIZE;
                rom.write_u16(newRow, rom.u16(oldRow));
            }
            rom.write_u16(newBase + rowCount * N1_BLOCK_SIZE, 0x0000);
            LevelUpAddr = newBase;
            return newBase;
        }

        /// <summary>
        /// True when the level-up skill pointer for the selected class is
        /// shared with at least one other class. Mirrors WF
        /// <c>IsShowIndependencePanel</c>.
        /// </summary>
        public bool IsShowIndependencePanel(uint classCount)
        {
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            uint levelUpTablePtr = rom.p32(gpClassLevelUpSkillTable);
            if (!U.isSafetyOffset(levelUpTablePtr)) return false;
            uint slotAddr = levelUpTablePtr + SelectedClassIndex * 4;
            if (slotAddr + 4 > (uint)rom.Data.Length) return false;
            uint currentPtr = rom.p32(slotAddr);
            if (!U.isSafetyOffset(currentPtr)) return false;
            for (uint i = 0; i < classCount; i++)
            {
                if (i == SelectedClassIndex) continue;
                uint otherSlot = levelUpTablePtr + i * 4;
                if (otherSlot + 4 > (uint)rom.Data.Length) continue;
                uint otherPtr = rom.p32(otherSlot);
                if (otherPtr == currentPtr) return true;
            }
            return false;
        }

        public int GetListCount()
        {
            // Mirror WF: count of class entries in the skill-table.
            ROM? rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return ComputeClassCount(rom);
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = "0x" + CurrentAddr.ToString("X08"),
                ["ClassSkill"] = NameResolver.GetSkillName(ClassSkill),
                ["LevelUpAddr"] = "0x" + LevelUpAddr.ToString("X08"),
                ["IsClassSkillExtendsActive"] = IsClassSkillExtendsActive.ToString(),
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
            if (N1CurrentAddr != 0 && U.isSafetyOffset(N1CurrentAddr) &&
                N1CurrentAddr + 1 < (uint)rom.Data.Length)
            {
                report["n1_addr"] = "0x" + N1CurrentAddr.ToString("X08");
                report["u8@0x00"] = "0x" + rom.u8(N1CurrentAddr + 0).ToString("X02");
                report["u8@0x01"] = "0x" + rom.u8(N1CurrentAddr + 1).ToString("X02");
            }
            // Skill-info inline preview reads (icon GBA pointer + nameMsg +
            // descMsg) - same offsets as the static Resolve* helpers.
            try
            {
                if (U.isSafetyOffset(gpSkillInfos + 4, rom))
                {
                    uint baseAddr = rom.p32(gpSkillInfos);
                    if (U.isSafetyOffset(baseAddr, rom))
                    {
                        uint entry = baseAddr + SKILL_INFO_SIZE * ClassSkill;
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
            ["ClassSkill"] = "u16@0x00",
            ["B0"] = "u8@0x00",
            ["B1"] = "u8@0x01",
            ["SkillIconPtr"] = "u32@0x00",
            ["SkillNameMsg"] = "u16@0x04",
            ["SkillDescMsg"] = "u16@0x06",
        };
    }
}
