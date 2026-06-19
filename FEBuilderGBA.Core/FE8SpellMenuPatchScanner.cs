// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side ROM byte-pattern scanner for the FE8 SkillSystems spell-menu
// (Gaiden-style spell list) patch. Mirrors the WinForms
// FE8SpellMenuExtendsForm.FindFE8SpellPatchPointerLow helpers so the byte
// tables live in one place and the resolution is reusable from the CLI and
// Avalonia projects (issue #1167).
//
// No CoreState reads. All entry points take an explicit ROM + base directory
// so the scanner is headless-testable. The OldSystem path needs an external
// .dmp from config/patch2; it degrades to NOT_FOUND when the file is missing.
// The SkillSystems202201 path uses a hard-coded 44-byte signature (no external
// file) — it is the path provable on a synthetic ROM.
using System;
using System.IO;

namespace FEBuilderGBA
{
    public static class FE8SpellMenuPatchScanner
    {
        public const uint NOT_FOUND = U.NOT_FOUND;

        // SkillSystems202201 signature: the bytes that immediately precede the
        // assignLevelUpP pointer slot in the SpellsGetter routine. GrepEnd lands
        // on the 4-byte pointer slot right after this 44-byte run. This path
        // needs no external file, so it is the synthetic-test path. (Faithful
        // copy of WF FindFE8SpellPatchPointerLow_SkillSystems202201.)
        static readonly byte[] s_SpellsGetter202201 = new byte[]
        {
            0x9E, 0x42, 0x04, 0xDA, 0x02, 0x34, 0xEF, 0xE7,
            0x00, 0x9A, 0x9A, 0x42, 0xFA, 0xD1, 0x01, 0x9B,
            0x01, 0x33, 0x03, 0xD1, 0x63, 0x78, 0x2B, 0x70,
            0x01, 0x35, 0xF3, 0xE7, 0x60, 0x78, 0xFF, 0xF7,
            0xBB, 0xFF, 0x01, 0x9B, 0x98, 0x42, 0xED, 0xD1,
            0xF4, 0xE7, 0xC0, 0x46,
        };

        /// <summary>
        /// Resolve the address of the assignLevelUpP pointer slot for the FE8
        /// SkillSystems spell-menu patch. Returns <see cref="NOT_FOUND"/> when:
        /// the ROM is null, the ROM is not FE8 (RomInfo.version != 8), the ROM
        /// is multibyte (is_multibyte), or neither the OldSystem .dmp grep nor
        /// the hard-coded SkillSystems202201 signature matches.
        ///
        /// The returned value is the address of the u32 slot itself
        /// (assignLevelUpP); *assignLevelUpP is the per-unit pointer-table base.
        /// </summary>
        /// <param name="rom">Target ROM (explicit; no CoreState read).</param>
        /// <param name="baseDir">Base directory whose config/patch2 holds the
        /// OldSystem SpellsGetter.dmp. May be null/empty — the OldSystem path is
        /// then skipped and only the hard-coded signature path is tried.</param>
        public static uint FindFE8SpellPatchPointer(ROM rom, string baseDir)
        {
            if (rom == null || rom.Data == null || rom.RomInfo == null)
            {
                return NOT_FOUND;
            }
            if (rom.RomInfo.version != 8)
            {
                return NOT_FOUND;
            }
            if (rom.RomInfo.is_multibyte)
            {
                return NOT_FOUND;
            }

            uint pointer = FindFE8SpellPatchPointer_OldSystem(rom, baseDir);
            if (pointer != NOT_FOUND)
            {
                return pointer;
            }
            pointer = FindFE8SpellPatchPointer_SkillSystems202201(rom);
            if (pointer != NOT_FOUND)
            {
                return pointer;
            }
            return NOT_FOUND;
        }

        static uint FindFE8SpellPatchPointer_OldSystem(ROM rom, string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                return NOT_FOUND;
            }
            string spellsGetterDmp = Path.Combine(baseDir, "config", "patch2",
                "FE8U", "FE8SpellMenu", "GaidenMagic", "SpellsGetter.dmp");
            if (!File.Exists(spellsGetterDmp))
            {
                return NOT_FOUND;
            }
            byte[] sig;
            try
            {
                sig = File.ReadAllBytes(spellsGetterDmp);
            }
            catch
            {
                return NOT_FOUND;
            }
            if (sig == null || sig.Length == 0)
            {
                return NOT_FOUND;
            }
            return U.GrepEnd(rom.Data, sig, rom.RomInfo.compress_image_borderline_address, 0, 4);
        }

        static uint FindFE8SpellPatchPointer_SkillSystems202201(ROM rom)
        {
            return U.GrepEnd(rom.Data, s_SpellsGetter202201,
                rom.RomInfo.compress_image_borderline_address, 0, 4);
        }
    }
}
