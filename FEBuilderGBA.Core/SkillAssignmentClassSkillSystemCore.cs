// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side TSV import/export for the per-class skill assignment table
// of the SkillSystems patch family. Mirrors WinForms
// SkillAssignmentClassSkillSystemForm.{ExportAllData, ImportAllData}.
//
// All entry points take resolved pointer locations + class count as
// parameters. The Core class makes NO calls to WinForms pattern scanners
// or UI services, so it builds clean against FEBuilderGBA.Core and is
// reusable from the CLI and Avalonia projects.
//
// File format (mirrors WF):
//   one line per class index, tab-separated:
//     class_skill_u8_hex   levelup_table_gba_pointer_hex
//         [level_u8 skill_u8]+
//
//   - First TSV column is the per-class B0 skill byte.
//   - Second column is the per-class level-up table GBA pointer
//     (raw u32 read from assignLevelUpBase + 4*classId; GBA pointer
//     form, NOT a stripped offset).
//   - Remaining columns are level/skill u8 pairs walking the existing
//     referenced level-up table (max LEVELUP_MAX_ROWS rows).
//
// Import branches (mirrors WF):
//   - levelup pointer in TSV is 0 or in extended ROM area
//     -> write the per-class p32 pointer directly (independence repoint).
//   - otherwise -> walk the EXISTING referenced level-up table and write
//     level/skill u8 pairs in-place (shared-table mutation).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    public static class SkillAssignmentClassSkillSystemCore
    {
        const uint LEVELUP_BLOCK_SIZE = 2;
        const uint LEVELUP_MAX_ROWS = 20; // WF predicate cap for sub-list iteration.

        public static bool ExportAllData(ROM rom, uint assignClassBase, uint assignLevelUpPointerLocation,
            uint classCount, string filename)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(assignClassBase, rom)) return false;
            if (!U.isSafetyOffset(assignLevelUpPointerLocation + 3, rom)) return false;
            if (string.IsNullOrEmpty(filename)) return false;

            try
            {
                var lines = new List<string>();
                uint assignLevelUpBase = rom.p32(assignLevelUpPointerLocation);

                uint classBaseSkillAddr = assignClassBase;
                uint assignLevelUpAddr = assignLevelUpBase;

                for (uint i = 0; i < classCount;
                    i++, assignLevelUpAddr += 4, classBaseSkillAddr += 1)
                {
                    if (!U.isSafetyOffset(assignLevelUpAddr + 3, rom)) break;
                    if (!U.isSafetyOffset(classBaseSkillAddr, rom)) break;

                    var sb = new StringBuilder();
                    sb.Append(U.ToHexString(rom.u8(classBaseSkillAddr)));

                    // WF parity: TSV column 2 is the level-up table OFFSET
                    // (rom.p32 strips the 0x08000000 high bit). Writing the
                    // raw u32 would break U.isExtrendsROMArea / write_p32
                    // round-trip semantics (Copilot CLI review on PR #555).
                    uint levelupOffset = rom.p32(assignLevelUpAddr);
                    sb.Append('\t');
                    sb.Append(U.ToHexString(levelupOffset));

                    if (U.isSafetyOffset(levelupOffset, rom))
                    {
                        uint levelupBase = levelupOffset;
                        if (U.isSafetyOffset(levelupBase, rom))
                        {
                            uint levelupAddr = levelupBase;
                            for (uint n = 0; n < LEVELUP_MAX_ROWS; n++, levelupAddr += LEVELUP_BLOCK_SIZE)
                            {
                                if (!U.isSafetyOffset(levelupAddr + 1, rom)) break;
                                uint pair = rom.u16(levelupAddr);
                                if (pair == 0 || pair == 0xFFFF) break;

                                sb.Append('\t');
                                sb.Append(U.ToHexString(rom.u8(levelupAddr + 0)));
                                sb.Append('\t');
                                sb.Append(U.ToHexString(rom.u8(levelupAddr + 1)));
                            }
                        }
                    }

                    lines.Add(sb.ToString());
                }

                File.WriteAllLines(filename, lines);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ImportAllData(ROM rom, uint assignClassBase, uint assignLevelUpPointerLocation,
            uint classCount, string filename)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(assignClassBase, rom)) return false;
            if (!U.isSafetyOffset(assignLevelUpPointerLocation + 3, rom)) return false;
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename)) return false;

            try
            {
                string[] lines = File.ReadAllLines(filename);
                if (lines == null) return false;

                uint classBaseSkillAddr = assignClassBase;
                uint assignLevelUpAddr = rom.p32(assignLevelUpPointerLocation);

                for (uint i = 0; i < classCount;
                    i++, assignLevelUpAddr += 4, classBaseSkillAddr += 1)
                {
                    if (i >= lines.Length) break;
                    if (!U.isSafetyOffset(assignLevelUpAddr + 3, rom)) break;
                    if (!U.isSafetyOffset(classBaseSkillAddr, rom)) break;

                    string[] sp = lines[i].Split('\t');
                    if (sp.Length < 2) continue;

                    uint skill = U.atoh(sp[0]);
                    rom.write_u8(classBaseSkillAddr, skill);

                    // WF parity: TSV column 2 is an OFFSET. write_p32 expects
                    // an offset (it re-adds 0x08000000 internally). Compare
                    // against the ROM's extends_address (as an offset) to
                    // detect the independence/repoint branch (Copilot CLI
                    // review on PR #555).
                    uint levelupOffset = U.atoh(sp[1]);
                    if (IsExtendedRomAreaOffset(rom, levelupOffset) || levelupOffset == 0)
                    {
                        rom.write_p32(assignLevelUpAddr, levelupOffset);
                        continue;
                    }

                    uint levelupBaseRead = rom.p32(assignLevelUpAddr);
                    if (!U.isSafetyOffset(levelupBaseRead, rom)) continue;

                    uint levelupAddr = levelupBaseRead;
                    for (uint n = 0; n < LEVELUP_MAX_ROWS; n++, levelupAddr += LEVELUP_BLOCK_SIZE)
                    {
                        if (!U.isSafetyOffset(levelupAddr + 1, rom)) break;
                        uint pair = rom.u16(levelupAddr);
                        if (pair == 0 || pair == 0xFFFF) break;

                        uint level = U.atoh(SafeAt(sp, 2 + (n * 2) + 0));
                        uint skillByte = U.atoh(SafeAt(sp, 2 + (n * 2) + 1));
                        rom.write_u8(levelupAddr + 0, level);
                        rom.write_u8(levelupAddr + 1, skillByte);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string SafeAt(string[] arr, uint idx)
        {
            if (arr == null) return string.Empty;
            if (idx >= arr.Length) return string.Empty;
            return arr[idx] ?? string.Empty;
        }

        /// <summary>
        /// True when the provided ROM OFFSET lies at or past the ROM's
        /// extends_address threshold. Mirrors WinForms U.isExtrendsROMArea
        /// applied to an offset value (the value read from TSV column 2,
        /// which is itself an offset per WF export format).
        /// </summary>
        static bool IsExtendedRomAreaOffset(ROM rom, uint offset)
        {
            if (rom == null || rom.RomInfo == null) return false;
            uint extendsOffset = U.toOffset(rom.RomInfo.extends_address);
            return offset >= extendsOffset;
        }
    }
}
