// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side extraction of FEBuilderGBA/ImageUtilMagic.cs's read-only
// helpers used by the Avalonia ImageMagicFEditorView rebuild (#418).
//
// The original WF file lives in FEBuilderGBA/ImageUtilMagic.cs and
// depends on System.Windows.Forms (R.ShowWarning) and the static
// Program.ROM accessor. This Core extraction removes those couplings
// so the same detection logic can run from Avalonia / CLI / tests.
//
// The WF file retains a thin shim delegating to this Core helper to
// preserve binary signatures consumed by the rest of WinForms.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Detects which "magic engine" patch (if any) is installed in a
    /// ROM and locates the CSA spell-table the patch uses to drive
    /// the per-row magic-effect editor. Read-only: no ROM writes
    /// happen here. Used by both `FEBuilderGBA/ImageUtilMagic.cs`
    /// (WinForms) and `FEBuilderGBA.Avalonia` (#418).
    /// </summary>
    public static class ImageUtilMagicCore
    {
        /// <summary>
        /// Magic-engine identifier. Mirrors WF
        /// `ImageUtilMagic.magic_system_enum`.
        /// </summary>
        public enum MagicSystem
        {
            /// <summary>No magic-engine patch installed.</summary>
            No = 0,
            /// <summary>FEditorAdv patch (most common).</summary>
            FEditorAdv = 1,
            /// <summary>SCA_Creator patch.</summary>
            CsaCreator = 2,
        }

        /// <summary>
        /// One row in the static signature table. Matches the WF
        /// `MagicPatchTableSt` struct.
        /// </summary>
        struct MagicPatchTableSt
        {
            public string name;
            public string ver;
            public uint addr;
            public byte[] data;
            public uint dim;
            public uint no_dim;
        }

        /// <summary>
        /// One row in the CSA spell-table signature table. Matches the
        /// WF `SpellTableSt` struct.
        /// </summary>
        struct SpellTableSt
        {
            public string name;
            public string ver;
            public byte[] data;
        }

        /// <summary>
        /// Static signature table — direct port from WF
        /// `FEBuilderGBA/ImageUtilMagic.cs:57-67`. Keep in sync if the
        /// WF table changes (the WF shim re-uses this same data via
        /// the static accessor below).
        /// </summary>
        static readonly MagicPatchTableSt[] PatchSignatures = new MagicPatchTableSt[]
        {
            new MagicPatchTableSt{ name = "SCA_Creator", ver = "FE8U", addr = 0x95d780u, data = new byte[]{0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08, 0x03, 0x00, 0x00, 0x00, 0xD9, 0xD8, 0x95, 0x08}, dim = 0x95d7edu, no_dim = 0x95d899u },
            new MagicPatchTableSt{ name = "FEditor",     ver = "FE8U", addr = 0x95d780u, data = new byte[]{0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08, 0x03, 0x00, 0x00, 0x00, 0x39, 0xD9, 0x95, 0x08}, dim = 0x95D7EDu, no_dim = 0x95D8EFu },
            new MagicPatchTableSt{ name = "SCA_Creator", ver = "FE8J", addr = 0x9cd3bcu, data = new byte[]{0x01, 0x00, 0x00, 0x00, 0xCC, 0xD3, 0x9C, 0x08, 0x03, 0x00, 0x00, 0x00, 0x15, 0xD5, 0x9C, 0x08}, dim = 0x9CD429u, no_dim = 0x9CD4D5u },
            new MagicPatchTableSt{ name = "SCA_Creator", ver = "FE8J", addr = 0x5BDC80u, data = new byte[]{0x01, 0x00, 0x00, 0x00, 0xCC, 0xD3, 0x9C, 0x08, 0x03, 0x00, 0x00, 0x00, 0x15, 0xD5, 0x9C, 0x08}, dim = 0x5BDCEDu, no_dim = 0x5BDD99u }, // fixed version
            new MagicPatchTableSt{ name = "FEditor",     ver = "FE8J", addr = 0xEFBE00u, data = new byte[]{0x01, 0x00, 0x00, 0x00, 0x10, 0xBE, 0xEF, 0x08, 0x03, 0x00, 0x00, 0x00, 0xB9, 0xBF, 0xEF, 0x08}, dim = 0xEFBE6Du, no_dim = 0xEFBF6Fu },
            new MagicPatchTableSt{ name = "SCA_Creator", ver = "FE7U", addr = 0xCB680u,  data = new byte[]{0x19, 0x00, 0x00, 0x00, 0x90, 0xB6, 0x0C, 0x08, 0x03, 0x00, 0x00, 0x00, 0xD9, 0xB7, 0x0C, 0x08}, dim = 0xCB6EDu, no_dim = 0xCB799u },
            new MagicPatchTableSt{ name = "FEditor",     ver = "FE7U", addr = 0xCB680u,  data = new byte[]{0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0xD9, 0xB7, 0x0C, 0x08}, dim = 0xCB699u, no_dim = 0xCB787u },
            new MagicPatchTableSt{ name = "FEditor",     ver = "FE7J", addr = 0xC69B4u,  data = new byte[]{0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x29, 0x6B, 0x0C, 0x08}, dim = 0xC69CDu, no_dim = 0xC69CDu },
            new MagicPatchTableSt{ name = "SCA_Creator", ver = "FE6",  addr = 0x2DC078u, data = new byte[]{0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x61, 0xC1, 0x2D, 0x08}, dim = 0x2DC091u, no_dim = 0x2DC129u },
            new MagicPatchTableSt{ name = "FEditor",     ver = "FE6",  addr = 0x2DC078u, data = new byte[]{0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0xC5, 0xC1, 0x2D, 0x08}, dim = 0x2DC091u, no_dim = 0x2DC17Fu },
        };

        /// <summary>
        /// CSA spell-table signature pattern table — port from WF
        /// `FEBuilderGBA/ImageUtilMagic.cs:138-148`.
        /// </summary>
        static readonly SpellTableSt[] CsaSignatures = new SpellTableSt[]
        {
            new SpellTableSt{ name = "SCA_Creator", ver = "FE8U", data = new byte[]{0x1C, 0x58, 0x05, 0x08, 0x00, 0x01, 0x00, 0x80, 0xED, 0xD7, 0x95, 0x08, 0x99, 0xD8, 0x95, 0x08}},
            new SpellTableSt{ name = "FEditor",     ver = "FE8U", data = new byte[]{0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02, 0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02}},
            new SpellTableSt{ name = "SCA_Creator", ver = "FE8J", data = new byte[]{0xB8, 0x67, 0x05, 0x08, 0x00, 0x01, 0x00, 0x80, 0x29, 0xD4, 0x9C, 0x08, 0xD5, 0xD4, 0x9C, 0x08}},
            new SpellTableSt{ name = "FEditor",     ver = "FE8J", data = new byte[]{0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02, 0x00, 0xBE, 0xEF, 0x08, 0x16, 0xE1, 0x03, 0x02}},
            new SpellTableSt{ name = "SCA_Creator", ver = "FE7U", data = new byte[]{0x0C, 0x06, 0x05, 0x08, 0x00, 0x01, 0x00, 0x80, 0xED, 0xB6, 0x0C, 0x08, 0x99, 0xB7, 0x0C, 0x08}},
            new SpellTableSt{ name = "FEditor",     ver = "FE7U", data = new byte[]{0x00, 0x28, 0x17, 0xD1, 0x18, 0xE0, 0x70, 0xB5, 0x05, 0x1C, 0x00, 0x20, 0x01, 0xB4, 0x87, 0xE7, 0x34, 0xFF, 0x03, 0x02, 0x80, 0xB6, 0x0C, 0x08, 0x26, 0xE0, 0x03, 0x02}},
            new SpellTableSt{ name = "FEditor",     ver = "FE7J", data = new byte[]{0x01, 0xB4, 0x79, 0xE7, 0x34, 0xFF, 0x03, 0x02, 0xB4, 0x69, 0x0C, 0x08, 0xFE, 0xDF, 0x03, 0x02}},
            new SpellTableSt{ name = "SCA_Creator", ver = "FE6",  data = new byte[]{0x48, 0x19, 0x02, 0x02, 0x00, 0x01, 0x00, 0x80, 0x91, 0xC0, 0x2D, 0x08, 0x29, 0xC1, 0x2D, 0x08}},
            new SpellTableSt{ name = "FEditor",     ver = "FE6",  data = new byte[]{0xE7, 0x7D, 0xB4, 0x01, 0x34, 0xFF, 0x03, 0x02, 0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02}},
        };

        /// <summary>
        /// Scan the given ROM for one of the magic-engine signatures.
        /// Returns the engine type, base / dim / no-dim addresses; on
        /// "No" all the out params are set to <c>U.NOT_FOUND</c>.
        ///
        /// <para>
        /// Mirrors WF <c>ImageUtilMagic.SearchMagicSystem(out baseaddr,
        /// out dimaddr, out nodimaddr)</c> minus the cache writes
        /// (the WF cache is owned by the WinForms-only static class).
        /// </para>
        /// </summary>
        public static MagicSystem SearchMagicSystem(ROM rom,
            out uint baseaddr, out uint dimaddr, out uint nodimaddr)
        {
            baseaddr = U.NOT_FOUND;
            dimaddr = U.NOT_FOUND;
            nodimaddr = U.NOT_FOUND;

            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return MagicSystem.No;

            string version = rom.RomInfo.VersionToFilename;

            foreach (var entry in PatchSignatures)
            {
                if (entry.ver != version) continue;
                if ((long)entry.addr + entry.data.Length > rom.Data.Length) continue;

                byte[] data = rom.getBinaryData(entry.addr, (uint)entry.data.Length);
                if (U.memcmp(entry.data, data) != 0) continue;

                MagicSystem candidate = entry.name == "FEditor"
                    ? MagicSystem.FEditorAdv
                    : entry.name == "SCA_Creator"
                        ? MagicSystem.CsaCreator
                        : MagicSystem.No;
                if (candidate == MagicSystem.No) continue;

                // The WF code requires that the CSA spell table is also
                // findable; otherwise the patch is "broken" and we keep
                // scanning.
                uint csaPointer;
                uint csaAddr = FindCSASpellTable(rom, candidate, out csaPointer);
                if (csaAddr == U.NOT_FOUND || csaPointer == U.NOT_FOUND) continue;

                baseaddr = entry.addr;
                dimaddr = entry.dim;
                nodimaddr = entry.no_dim;
                return candidate;
            }
            return MagicSystem.No;
        }

        /// <summary>
        /// Scan the ROM for the CSA spell-table pattern matching the
        /// detected magic system. Returns the table address (the
        /// resolved p32 pointer) and the pointer slot itself
        /// (<paramref name="outPointer"/>). Both are
        /// <c>U.NOT_FOUND</c> on a non-match.
        ///
        /// <para>
        /// Mirrors WF <c>ImageUtilMagic.FindCSASpellTableLow</c>.
        /// </para>
        /// </summary>
        public static uint FindCSASpellTable(ROM rom, MagicSystem system,
            out uint outPointer)
        {
            outPointer = U.NOT_FOUND;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return U.NOT_FOUND;
            if (system == MagicSystem.No) return U.NOT_FOUND;

            string version = rom.RomInfo.VersionToFilename;
            string typeName = system == MagicSystem.FEditorAdv ? "FEditor" : "SCA_Creator";

            foreach (var sig in CsaSignatures)
            {
                if (sig.name != typeName) continue;
                if (sig.ver != version) continue;

                uint hit = U.Grep(rom.Data, sig.data, 0x10000, 0, 4);
                if (hit == U.NOT_FOUND) continue;

                uint csaSpellTablePointer = hit + (uint)sig.data.Length;
                if (csaSpellTablePointer + 4 > rom.Data.Length) continue;

                uint csaSpellTable = rom.p32(csaSpellTablePointer);
                if (!U.isSafetyOffset(csaSpellTable, rom)) continue;

                outPointer = csaSpellTablePointer;
                return csaSpellTable;
            }
            return U.NOT_FOUND;
        }

        /// <summary>
        /// Count how many pointer-or-NULL entries follow the ROM's
        /// `magic_effect_pointer` after the original count.
        /// Mirrors WF <c>ImageUtilMagicFEditor.SpellDataCount</c>.
        ///
        /// <para>
        /// Returns 0 when the ROM is null or when the pointer table
        /// has been wiped. Returns at most 0xFD (0xFE/0xFF reserved).
        /// </para>
        /// </summary>
        public static uint GetSpellDataCount(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return 0u;
            uint baseaddr = rom.p32(rom.RomInfo.magic_effect_pointer);
            if (!U.isSafetyOffset(baseaddr, rom)) return 0u;

            uint baseid = rom.RomInfo.magic_effect_original_data_count;
            uint search = baseaddr + (baseid * 4);
            uint p = search;
            uint dataLen = (uint)rom.Data.Length;
            for (; p + 4 <= dataLen; p += 4)
            {
                uint d = rom.u32(p);
                if (U.isPointerOrNULL(d)) continue;
                break;
            }
            uint count = (p - baseaddr) / 4;
            if (count == 0) return 0u;
            uint a = count - 1;
            if (a >= 0xFD) return 0xFD;
            return count - 1;
        }

        // NOTE: GetPatchSignatures / GetCsaSignatures accessor helpers
        // were initially defined here for the WF shim but were never
        // wired (Copilot CLI re-review on PR #554). The WF shim in
        // FEBuilderGBA/ImageUtilMagic.cs delegates directly to
        // SearchMagicSystem / FindCSASpellTable above, which already
        // expose all the information the WF cache needs.
    }
}
